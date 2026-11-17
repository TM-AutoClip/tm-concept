using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Processes;
using TMConcept.Core.Research;

namespace TMConcept.Documents.Mcp;

/// <summary>
/// An MCP server that is a program on this computer (stdio): one JSON message per line on its standard input and output. The program runs
/// with the user's rights, so it is started only after the user confirmed the warning, WITHOUT the app's environment (only the variables a
/// program needs to run: PATH, the system and temporary folders, the user's folders, the locale), so keys that sit in environment variables
/// do not reach it. Like every program the app starts, it is handed to the app's <see cref="IChildProcessGuard"/> at once, so it never
/// outlives the app (a closed window, a crash). What it writes to its error output is kept (the last part) to say why it ended. Disposing
/// ends it with its child processes and waits at most <see cref="ExitWaitMax"/> for that (a descendant that kept the error output open
/// cannot hold the page busy).
/// </summary>
public sealed class McpStdioChannel : McpChannelBase
{
    /// <summary>The environment variables a started server gets (when the user's system has them).</summary>
    public static readonly IReadOnlyList<string> PassedVariables =
    [
        "PATH", "PATHEXT", "SystemRoot", "SystemDrive", "windir", "ComSpec", "TEMP", "TMP", "TMPDIR", "HOME", "USERPROFILE", "APPDATA", "LOCALAPPDATA",
        "ProgramData", "ProgramFiles", "ProgramFiles(x86)", "CommonProgramFiles", "LANG", "LC_ALL", "XDG_RUNTIME_DIR", "XDG_CACHE_HOME", "XDG_CONFIG_HOME",
        "XDG_DATA_HOME", "NUMBER_OF_PROCESSORS", "PROCESSOR_ARCHITECTURE", "OS",
    ];

    /// <summary>The longest wait for an ended server program to be gone.</summary>
    public static readonly TimeSpan ExitWaitMax = TimeSpan.FromSeconds(5);

    private const int ErrorTailMax = 2_000;

    private readonly Stream _fromServer;
    private readonly Stream _toServer;
    private readonly Process? _process;
    private readonly IDisposable? _adoption;
    private readonly StringBuilder _errors = new();
    private readonly Decoder _decoder = new UTF8Encoding(false).GetDecoder();
    private readonly byte[] _buffer = new byte[16_384];
    private readonly char[] _chars = new char[16_384 + 4];
    private readonly StringBuilder _line = new();
    private readonly Queue<string> _pending = new();
    private bool _ended;

    /// <summary>A channel over the streams of a server (<paramref name="fromServer"/>: its output; <paramref name="toServer"/>: its input).</summary>
    public McpStdioChannel(Stream fromServer, Stream toServer, ILogger logger, Process? process = null, IDisposable? adoption = null)
        : base(logger)
    {
        _fromServer = fromServer ?? throw new ArgumentNullException(nameof(fromServer));
        _toServer = toServer ?? throw new ArgumentNullException(nameof(toServer));
        _process = process;
        _adoption = adoption;
    }

    /// <summary>The arguments of <paramref name="text"/>: split at spaces, double quotes keep spaces ("C:\My tools\server.js").</summary>
    public static IReadOnlyList<string> SplitArguments(string? text)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in text ?? string.Empty)
        {
            if (c == '"')
            {
                quoted = !quoted;
                any = true;
            }
            else if (char.IsWhiteSpace(c) && !quoted)
            {
                if (any)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    any = false;
                }
            }
            else
            {
                current.Append(c);
                any = true;
            }
        }

        if (any)
        {
            result.Add(current.ToString());
        }

        return result;
    }

    /// <summary>
    /// Starts <paramref name="command"/> with <paramref name="arguments"/> (no shell: nothing in them is interpreted), hands it to
    /// <paramref name="children"/> and gives its channel. <see cref="McpException"/> "start" when it cannot be started.
    /// </summary>
    public static McpStdioChannel Start(string command, string arguments, IChildProcessGuard children, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(children);
        ArgumentNullException.ThrowIfNull(logger);
        if (string.IsNullOrWhiteSpace(command))
        {
            throw new McpException("start", "No program is given.");
        }

        var start = new ProcessStartInfo(command.Trim())
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in SplitArguments(arguments))
        {
            start.ArgumentList.Add(argument);
        }

        var inherited = start.Environment.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.OrdinalIgnoreCase);
        start.Environment.Clear();
        foreach (var name in PassedVariables)
        {
            if (inherited.TryGetValue(name, out var value) && value is not null)
            {
                start.Environment[name] = value;
            }
        }

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new McpException("start", "The program did not start.");
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            logger.LogWarning(ex, "An MCP server program could not be started");
            throw new McpException("start", ex.Message, ex);
        }

        var adoption = children.Adopt(process);
        var channel = new McpStdioChannel(process.StandardOutput.BaseStream, process.StandardInput.BaseStream, logger, process, adoption);
        process.ErrorDataReceived += (_, e) => channel.KeepError(e.Data);
        process.BeginErrorReadLine();
        logger.LogInformation("An MCP server program was started (stdio)");
        return channel;
    }

    /// <inheritdoc />
    protected override async Task<string?> ExchangeAsync(string message, long? id, CancellationToken cancellationToken)
    {
        await WriteAsync(message, cancellationToken).ConfigureAwait(false);
        if (id is not { } wanted)
        {
            return null;
        }

        while (true)
        {
            var line = await ReadLineAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new McpException("closed", "The MCP server ended." + ErrorTail());
            if (line.Trim().Length == 0)
            {
                continue;
            }

            var (isReply, answer) = Inspect(line, wanted);
            if (isReply)
            {
                return line;
            }

            if (answer is not null)
            {
                await WriteAsync(answer, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeCoreAsync()
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                using var wait = new CancellationTokenSource(ExitWaitMax);
                try
                {
                    await _process.WaitForExitAsync(wait.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // Something the program started still holds its error output: the app goes on (the guard ends it with the app).
                    Logger.LogWarning("An MCP server program did not end within {Seconds} s", ExitWaitMax.TotalSeconds);
                }
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
        {
            Logger.LogWarning(ex, "An MCP server program could not be ended");
        }
        finally
        {
            if (_process is not null)
            {
                try
                {
                    _process.CancelErrorRead();
                }
                catch (InvalidOperationException ex)
                {
                    Logger.LogDebug(ex, "The MCP server's error output was not being read");
                }
            }

            await _toServer.DisposeAsync().ConfigureAwait(false);
            await _fromServer.DisposeAsync().ConfigureAwait(false);
            _process?.Dispose();
            _adoption?.Dispose();
        }
    }

    private async Task WriteAsync(string message, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(message.Replace("\n", string.Empty, StringComparison.Ordinal) + "\n");
        try
        {
            await _toServer.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _toServer.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (IOException ex)
        {
            throw new McpException("closed", "The MCP server ended." + ErrorTail(), ex);
        }
    }

    /// <summary>The next line of the server's output; null at its end. A line longer than the message limit is refused.</summary>
    private async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_pending.TryDequeue(out var ready))
            {
                return ready;
            }

            if (_ended)
            {
                return null;
            }

            var read = await _fromServer.ReadAsync(_buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                _ended = true;
                if (_line.Length > 0)
                {
                    _pending.Enqueue(_line.ToString());
                    _line.Clear();
                }

                continue;
            }

            var count = _decoder.GetChars(_buffer, 0, read, _chars, 0);
            for (var i = 0; i < count; i++)
            {
                var c = _chars[i];
                if (c == '\n')
                {
                    _pending.Enqueue(_line.ToString().TrimEnd('\r'));
                    _line.Clear();
                }
                else
                {
                    _line.Append(c);
                    if (_line.Length > ResearchLimits.McpMessageBytesMax)
                    {
                        throw TooLong();
                    }
                }
            }
        }
    }

    private void KeepError(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_errors)
        {
            _errors.Append(line).Append('\n');
            if (_errors.Length > ErrorTailMax)
            {
                _errors.Remove(0, _errors.Length - ErrorTailMax);
            }
        }
    }

    private string ErrorTail()
    {
        lock (_errors)
        {
            return _errors.Length == 0 ? string.Empty : " " + _errors.ToString().Trim();
        }
    }
}
