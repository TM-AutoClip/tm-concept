using Microsoft.Extensions.Logging.Abstractions;
using Shouldly;
using TMConcept.Core.Endpoints;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Stores;

namespace TMConcept.Tests.Storage;

/// <summary>
/// Local servers in a backup: the endpoint list goes with the settings, its tokens only with the API keys (a keyless backup holds no token,
/// the restored endpoint works again once the token is typed in).
/// </summary>
public sealed class EndpointBackupTests
{
    private const string Password = "backup password 123";
    private const string Token = "endpoint-token-0123456789";
    private static readonly CancellationToken None = CancellationToken.None;

    private static BackupService Service(StorageHarness h) =>
        new(h.Paths, h.Key, h.Vault, h.Database, KdfParameters.Fast, TimeProvider.System, NullLogger<BackupService>.Instance);

    [Fact]
    public async Task EndpointsGoWithTheSettingsAndTheirTokensOnlyWithTheKeys()
    {
        using var source = await StorageHarness.CreatedAsync();
        var endpoint = new LocalEndpoint
        {
            Name = "Lemonade",
            BaseUrl = "http://localhost:13305/api/v1",
            Token = Token,
            Models = [new EndpointModel { Id = "bonsai-27b", ContextTokens = 32768, MaxOutputTokens = 8192, Vision = true }],
        };
        await new EndpointStore(source.Database).SaveAsync(endpoint, None);
        var service = Service(source);
        await service.SetPasswordAsync(Password, None);
        var automatic = Path.Combine(source.Paths.DataDirectory, "automatic" + BackupRules.Extension);
        var withKeys = Path.Combine(source.Paths.DataDirectory, "keys" + BackupRules.Extension);
        var noSettings = Path.Combine(source.Paths.DataDirectory, "no-settings" + BackupRules.Extension);
        await service.CreateAsync(automatic, BackupContents.Automatic, None);
        await service.CreateAsync(withKeys, new BackupContents { Keys = true }, None);
        await service.CreateAsync(noSettings, new BackupContents { Settings = false }, None);

        using (var target = new StorageHarness())
        {
            await Service(target).RestoreAsync(automatic, Password, target.Protector, None);
            var restored = (await new EndpointStore(target.Database).ListAsync(None)).Single();
            (restored.Id, restored.Name, restored.BaseUrl, restored.Token).ShouldBe((endpoint.Id, "Lemonade", endpoint.BaseUrl, string.Empty));
            restored.Models.Single().Vision.ShouldBeTrue();
        }

        using (var target = new StorageHarness())
        {
            await Service(target).RestoreAsync(withKeys, Password, target.Protector, None);
            (await new EndpointStore(target.Database).ListAsync(None)).Single().Token.ShouldBe(Token);
        }

        using (var target = new StorageHarness())
        {
            await Service(target).RestoreAsync(noSettings, Password, target.Protector, None);
            (await new EndpointStore(target.Database).ListAsync(None)).ShouldBeEmpty();
        }

        (await new EndpointStore(source.Database).ListAsync(None)).Single().Token.ShouldBe(Token, "the source keeps its token");
    }
}
