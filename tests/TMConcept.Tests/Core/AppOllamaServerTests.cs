using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.AppOllama;

namespace TMConcept.Tests.Core;

/// <summary>
/// The app's own Ollama server (0.1.20): started on demand on its own port with its own environment, never on the user's Ollama port, never
/// without the installed program; stopped on request and on dispose. Fake launcher and API: no process, no network.
/// </summary>
public sealed class AppOllamaServerTests : IDisposable
{
    private readonly string _folder = Path.Combine(TestFolders.Root, "app-ollama-" + Guid.NewGuid().ToString("N"));

    private AppOllamaLayout Layout => new(Path.Combine(_folder, "ollama-models"), Path.Combine(_folder, "ollama-home"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private AppOllamaServer Server(FakeAppOllamaLauncher launcher, FakeAppOllamaApi? api = null, FakeAppOllamaRuntime? runtime = null) =>
        new(runtime ?? new FakeAppOllamaRuntime(), launcher, api ?? new FakeAppOllamaApi(), Layout, TimeProvider.System, NullLogger<AppOllamaServer>.Instance)
        {
            StartTimeout = TimeSpan.FromSeconds(2),
            StartPoll = TimeSpan.FromMilliseconds(10),
        };

    [Fact]
    public async Task StartsOnItsOwnPortWithItsOwnEnvironment()
    {
        var launcher = new FakeAppOllamaLauncher();
        using var server = Server(launcher);

        var root = await server.StartAsync(CancellationToken.None);

        root.ShouldBe(new Uri("http://127.0.0.1:11534/"));
        server.Running.ShouldBeTrue();
        var launch = launcher.Started.ShouldHaveSingleItem().Launch;
        launch.Port.ShouldBe(AppOllamaDefaults.Port);
        launch.Environment["OLLAMA_HOST"].ShouldBe("127.0.0.1:11534");
        launch.Environment["OLLAMA_MODELS"].ShouldBe(Layout.ModelsDirectory);
        launch.Environment["OLLAMA_MAX_LOADED_MODELS"].ShouldBe("1");
        launch.Environment["OLLAMA_NUM_PARALLEL"].ShouldBe("1");
        launch.Environment["OLLAMA_NO_CLOUD"].ShouldBe("1");
        launch.Environment["OLLAMA_NOPRUNE"].ShouldBe("1", "a cancelled pull keeps its received part across restarts");
        launch.Environment["HOME"].ShouldBe(Layout.HomeDirectory);
        launch.Environment["USERPROFILE"].ShouldBe(Layout.HomeDirectory);
        Directory.Exists(Layout.ModelsDirectory).ShouldBeTrue();

        // A second use finds it running: no second process.
        (await server.StartAsync(CancellationToken.None)).ShouldBe(root);
        launcher.Started.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ATakenPortFallsBackToTheNextFreeOne()
    {
        var launcher = new FakeAppOllamaLauncher();
        launcher.Taken.Add(AppOllamaDefaults.Port);
        launcher.Taken.Add(AppOllamaDefaults.Port + 1);
        using var server = Server(launcher);

        var root = await server.StartAsync(CancellationToken.None);

        root.Port.ShouldBe(AppOllamaDefaults.Port + 2);
        launcher.Started.ShouldHaveSingleItem().Launch.Environment["OLLAMA_HOST"].ShouldBe("127.0.0.1:11536");
    }

    [Fact]
    public void TheUsersOllamaPortIsNeverChosen()
    {
        // Every port of the range is taken: the system's free port is asked, and 11434 is refused even then.
        var asked = new Queue<int>([AppOllamaDefaults.UserOllamaPort, AppOllamaDefaults.UserOllamaPort, 40_555]);
        AppOllamaPorts.Choose(_ => false, asked.Dequeue).ShouldBe(40_555);

        // A range that runs over 11434 skips it.
        AppOllamaPorts.Choose(p => true, () => 40_000, preferred: AppOllamaDefaults.UserOllamaPort).ShouldBe(AppOllamaDefaults.UserOllamaPort + 1);
        AppOllamaPorts.Allowed(AppOllamaDefaults.UserOllamaPort).ShouldBeFalse();
        Should.Throw<ArgumentOutOfRangeException>(() => AppOllamaEnvironment.For(Layout, AppOllamaDefaults.UserOllamaPort));
        Should.Throw<InvalidOperationException>(() => AppOllamaPorts.Choose(_ => false, () => AppOllamaDefaults.UserOllamaPort));
    }

    [Fact]
    public void InheritedOllamaVariablesNeverReachTheAppsServer()
    {
        var environment = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["OLLAMA_HOST"] = "0.0.0.0:11434",
            ["ollama_models"] = @"D:\my-ollama-models",
            ["OLLAMA_ORIGINS"] = "*",
            ["PATH"] = "/usr/bin",
        };

        AppOllamaEnvironment.Apply(environment, AppOllamaEnvironment.For(Layout, 11534));

        environment["OLLAMA_HOST"].ShouldBe("127.0.0.1:11534");
        environment["OLLAMA_MODELS"].ShouldBe(Layout.ModelsDirectory);
        environment.ContainsKey("OLLAMA_ORIGINS").ShouldBeFalse();
        environment["PATH"].ShouldBe("/usr/bin");
    }

    [Fact]
    public async Task WithoutTheInstalledProgramNothingIsStarted()
    {
        var launcher = new FakeAppOllamaLauncher();
        using var server = Server(launcher, runtime: new FakeAppOllamaRuntime(ready: false));

        var error = await Should.ThrowAsync<AppOllamaException>(() => server.StartAsync(CancellationToken.None));

        error.Code.ShouldBe("missing");
        launcher.Started.ShouldBeEmpty();
        server.Running.ShouldBeFalse();
    }

    [Fact]
    public async Task AServerThatEndsWhileStartingFailsWithItsOutput()
    {
        var launcher = new FakeAppOllamaLauncher
        {
            OnStart = p =>
            {
                p.HasExited = true;
                p.ExitCode = 1;
                p.RecentOutput = "Error: listen tcp 127.0.0.1:11534: bind: address already in use";
            },
        };
        using var server = Server(launcher, new FakeAppOllamaApi { Up = false });

        var error = await Should.ThrowAsync<AppOllamaException>(() => server.StartAsync(CancellationToken.None));

        error.Code.ShouldBe("start");
        error.Message.ShouldContain("address already in use");
        server.Running.ShouldBeFalse();
        server.Address.ShouldBeNull();
    }

    [Fact]
    public async Task AnAnswerWhileTheStartedServerHasEndedIsNotTakenForTheAppsServer()
    {
        // Another server answers on the port while the app's own could not bind and ended: the start fails, nothing is used.
        var launcher = new FakeAppOllamaLauncher
        {
            OnStart = p =>
            {
                p.HasExited = true;
                p.ExitCode = 1;
            },
        };
        using var server = Server(launcher, new FakeAppOllamaApi { Up = true });

        var error = await Should.ThrowAsync<AppOllamaException>(() => server.StartAsync(CancellationToken.None));

        error.Code.ShouldBe("start");
        server.Address.ShouldBeNull();
    }

    [Fact]
    public async Task WhatACrashedServerLeftIsEndedButARunningServerIsNotTouched()
    {
        var launcher = new FakeAppOllamaLauncher();
        var runtime = new FakeAppOllamaRuntime();
        using var server = Server(launcher, runtime: runtime);
        await server.StartAsync(CancellationToken.None);

        await server.EndLeftoversAsync(CancellationToken.None);
        launcher.Swept.ShouldBeEmpty("a running server and its runners are never touched");
        server.Running.ShouldBeTrue();

        // A crash: its model runner may still hold the card; the coordinator's unload ends what it left.
        launcher.Started[0].HasExited = true;
        await server.EndLeftoversAsync(CancellationToken.None);
        launcher.Swept.ShouldBe([Path.GetDirectoryName(runtime.Executable)!]);
        launcher.Started[0].Disposed.ShouldBeTrue();

        // The same happens when the next use finds it crashed.
        await server.StartAsync(CancellationToken.None);
        launcher.Started[1].HasExited = true;
        await server.StartAsync(CancellationToken.None);
        launcher.Swept.Count.ShouldBe(2);
        launcher.Started.Count.ShouldBe(3);
    }

    [Fact]
    public async Task AServerThatNeverAnswersIsEndedAfterTheWait()
    {
        var launcher = new FakeAppOllamaLauncher();
        using var server = Server(launcher, new FakeAppOllamaApi { Up = false });

        var error = await Should.ThrowAsync<AppOllamaException>(() => server.StartAsync(CancellationToken.None));

        error.Code.ShouldBe("start");
        launcher.Started.ShouldHaveSingleItem().Stops.ShouldBe(1);
    }

    [Fact]
    public async Task StopEndsItAndACrashedServerIsStartedAgain()
    {
        var launcher = new FakeAppOllamaLauncher();
        var server = Server(launcher);
        await server.StartAsync(CancellationToken.None);

        await server.StopAsync(CancellationToken.None);
        launcher.Started[0].Stops.ShouldBe(1);
        launcher.Started[0].Disposed.ShouldBeTrue();
        server.Running.ShouldBeFalse();

        await server.StartAsync(CancellationToken.None);
        launcher.Started[1].HasExited = true;
        server.Running.ShouldBeFalse("a crashed server does not count as running");
        await server.StartAsync(CancellationToken.None);
        launcher.Started.Count.ShouldBe(3);

        // The app closes: the server it started ends with it.
        server.Dispose();
        launcher.Started[2].Stops.ShouldBe(1);
    }
}
