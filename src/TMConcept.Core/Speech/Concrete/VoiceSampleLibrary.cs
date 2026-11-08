using System.Globalization;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Paths;

namespace TMConcept.Core.Speech;

/// <summary>
/// The user's voice samples for a voice that copies a voice (Pocket TTS): added from a recording (converted to mono 24 kHz WAV, cut to
/// <see cref="VoiceSampleLimits.MaxSeconds"/>), kept in the encrypted database, listed for the voice pickers (<see cref="Samples"/>, read
/// again by <see cref="RefreshAsync"/>) and written to a work file only for the moment a voice speaks with it (<see cref="WriteAsync"/>).
/// The voice id of a sample is "sample:&lt;id&gt;". No consent step: copying a voice is the user's responsibility (rule 8).
/// </summary>
public sealed class VoiceSampleLibrary(IVoiceSampleStore store, IVoiceSampleConverter converter, IAppPaths paths, ILogger<VoiceSampleLibrary> logger)
{
    private volatile IReadOnlyList<VoiceSampleInfo> _samples = [];

    /// <summary>The samples as last read (oldest first).</summary>
    public IReadOnlyList<VoiceSampleInfo> Samples => _samples;

    /// <summary>Components adding a sample needs (ffmpeg).</summary>
    public IReadOnlyList<string> RequiredComponents => converter.RequiredComponents;

    /// <summary>The voice id of a sample.</summary>
    public static string VoiceId(Guid id) => VoiceSampleLimits.VoicePrefix + id.ToString("N", CultureInfo.InvariantCulture);

    /// <summary>The sample a voice id names; null when it names none.</summary>
    public static Guid? SampleOf(string? voice) =>
        voice is not null && voice.StartsWith(VoiceSampleLimits.VoicePrefix, StringComparison.Ordinal)
            && Guid.TryParseExact(voice[VoiceSampleLimits.VoicePrefix.Length..], "N", out var id) ? id : null;

    /// <summary>Reads the list again.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken) =>
        _samples = await store.ListAsync(cancellationToken).ConfigureAwait(false);

    /// <summary>Adds a recording as a sample named <paramref name="name"/> (ffmpeg must be installed: <see cref="RequiredComponents"/>).</summary>
    public async Task<VoiceSampleImport> ImportAsync(string file, string name, CancellationToken cancellationToken)
    {
        var clean = (name ?? string.Empty).Trim();
        if (clean.Length == 0 || clean.Length > VoiceSampleLimits.NameMax)
        {
            return new VoiceSampleImport(null, "name");
        }

        if (!File.Exists(file) || new FileInfo(file).Length is 0 or > VoiceSampleLimits.FileMaxBytes)
        {
            return new VoiceSampleImport(null, "file");
        }

        var work = Path.Combine(paths.WorkDirectory, "voices");
        Directory.CreateDirectory(work);
        var wav = Path.Combine(work, "import-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".wav");
        try
        {
            if (!await converter.ConvertAsync(file, wav, VoiceSampleLimits.MaxSeconds, cancellationToken).ConfigureAwait(false) || !File.Exists(wav))
            {
                return new VoiceSampleImport(null, "read");
            }

            var duration = WaveFile.DurationMs(wav);
            if (duration < VoiceSampleLimits.MinSeconds * 1000L)
            {
                return new VoiceSampleImport(null, "short");
            }

            var bytes = await File.ReadAllBytesAsync(wav, cancellationToken).ConfigureAwait(false);
            var sha = Convert.ToHexStringLower(SHA256.HashData(bytes));
            if (await store.HasAsync(sha, cancellationToken).ConfigureAwait(false))
            {
                return new VoiceSampleImport(null, "same");
            }

            var sample = new VoiceSample { Name = clean, Wav = bytes, DurationMs = duration, Sha256 = sha };
            await store.AddAsync(sample, cancellationToken).ConfigureAwait(false);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);

            // Only the length: the name and the recording are the user's.
            logger.LogInformation("Voice sample added ({Ms} ms)", duration);
            return new VoiceSampleImport(new VoiceSampleInfo(sample.Id, sample.Name, sample.DurationMs, sample.AddedAtUtc), string.Empty);
        }
        catch (InvalidDataException ex)
        {
            logger.LogWarning(ex, "The converted voice sample could not be read");
            return new VoiceSampleImport(null, "read");
        }
        finally
        {
            Delete(wav);
        }
    }

    /// <summary>Removes a sample (a voice choice that names it falls back to another voice when the page is shown again).</summary>
    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
        await RefreshAsync(cancellationToken).ConfigureAwait(false);
        logger.LogInformation("Voice sample removed");
    }

    /// <summary>The samples as the voices of a voice that copies a voice ("My voice (12 s)").</summary>
    public IReadOnlyList<SpeechVoice> Voices() =>
        Samples.Select(s => new SpeechVoice(VoiceId(s.Id), s.Name + " (" + (s.DurationMs / 1000.0).ToString("0", CultureInfo.InvariantCulture) + " s)"))
            .ToList();

    /// <summary>
    /// Writes the sample <paramref name="voice"/> names to a work file for one request of <paramref name="engine"/> (the caller deletes it with
    /// <see cref="Delete"/>); a <see cref="SpeechException"/> (code "voice") when the voice names no sample or the sample is gone.
    /// </summary>
    public Task<string> WriteForAsync(string voice, string engine, CancellationToken cancellationToken)
    {
        var id = SampleOf(voice) ?? throw new SpeechException(
            engine + " speaks with your own voice: add a voice sample in Settings → Voice and pronunciation and choose it.") { Code = "voice" };
        return WriteAsync(id, cancellationToken);
    }

    /// <summary>
    /// Writes a sample to a work file for one speech request and returns its path (the caller deletes it with <see cref="Delete"/>); a
    /// <see cref="SpeechException"/> (code "voice") when the sample is gone.
    /// </summary>
    public async Task<string> WriteAsync(Guid id, CancellationToken cancellationToken)
    {
        var sample = await store.GetAsync(id, cancellationToken).ConfigureAwait(false)
            ?? throw new SpeechException("The chosen own voice is gone; choose another one on the video page.") { Code = "voice" };
        var work = Path.Combine(paths.WorkDirectory, "voices");
        Directory.CreateDirectory(work);
        var file = Path.Combine(work, "reference-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".wav");
        await File.WriteAllBytesAsync(file, sample.Wav, cancellationToken).ConfigureAwait(false);
        return file;
    }

    /// <summary>Removes a work file of a sample (a failure to remove it is logged; the work folder is cleaned later anyway).</summary>
    public void Delete(string file)
    {
        try
        {
            if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "A work file of a voice sample could not be removed");
        }
    }
}
