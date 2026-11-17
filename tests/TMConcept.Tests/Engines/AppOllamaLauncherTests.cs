using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.AppOllama;
using TMConcept.Engines.AppOllama;

namespace TMConcept.Tests.Engines;

/// <summary>
/// The launcher of the app's own Ollama server (0.1.20): a program that cannot be started is the app's own error ("start"), and a port is
/// free only when nothing holds it on 127.0.0.1 or on the wildcard address. Nothing is started: the program does not exist, and the sockets
/// are only bound (never listened on), so no firewall question appears.
/// </summary>
public sealed class AppOllamaLauncherTests
{
    [Fact]
    public void AProgramThatCannotBeStartedIsAnAppOllamaError()
    {
        using var guard = ChildProcessTests.Guard();
        var launcher = new AppOllamaLauncher(guard, NullLogger<AppOllamaLauncher>.Instance);
        var folder = Path.Combine(Path.GetTempPath(), "tmconcept-tests", "no-ollama-" + Guid.NewGuid().ToString("N"));
        var launch = new AppOllamaLaunch(Path.Combine(folder, "ollama.exe"), Path.GetTempPath(), 11534, new Dictionary<string, string>());

        var error = Should.Throw<AppOllamaException>(() => launcher.Start(launch));

        error.Code.ShouldBe("start");
        error.InnerException.ShouldNotBeNull();
        launcher.EndLeftovers(folder).ShouldBe(0, "a folder that does not exist has nothing to end");
    }

    [Fact]
    public void APortHeldOnTheLoopbackOrTheWildcardAddressIsNotFree()
    {
        using var guard = ChildProcessTests.Guard();
        var launcher = new AppOllamaLauncher(guard, NullLogger<AppOllamaLauncher>.Instance);

        foreach (var address in new[] { IPAddress.Loopback, IPAddress.Any })
        {
            using var holder = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            holder.Bind(new IPEndPoint(address, 0));
            var port = ((IPEndPoint)holder.LocalEndPoint!).Port;

            launcher.IsPortFree(port).ShouldBeFalse(address.ToString());
        }

        launcher.IsPortFree(launcher.AnyFreePort()).ShouldBeTrue();
    }
}
