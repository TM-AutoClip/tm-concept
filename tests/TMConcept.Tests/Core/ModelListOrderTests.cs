using Shouldly;
using TMConcept.Core.Endpoints;
using TMConcept.Core.Models;

namespace TMConcept.Tests.Core;

/// <summary>
/// 0.1.60, the plan's step 2: the one order of every model list. What runs on this computer (local, the app's Ollama, a user-added server)
/// first, online second, red (this computer cannot run it) always last; the given order inside each group; nothing chosen = the first that runs.
/// </summary>
public sealed class ModelListOrderTests
{
    private static readonly IModelListOrder Order = new ModelListOrder();

    private static CatalogModel Model(string provider, string id) => new() { Provider = provider, Id = id, Name = id };

    [Fact]
    public void TheGroupsAreRunsOnlineAndRed()
    {
        Order.GroupOf(Model(ModelProviders.Local, "a"), cannotRun: false).ShouldBe(ModelListGroup.Runs);
        Order.GroupOf(Model(ModelProviders.AppOllama, "b"), cannotRun: false).ShouldBe(ModelListGroup.Runs);
        Order.GroupOf(Model(EndpointCodes.For(Guid.NewGuid()), "c"), cannotRun: false).ShouldBe(ModelListGroup.Runs);
        Order.GroupOf(Model("google", "d"), cannotRun: false).ShouldBe(ModelListGroup.Online);
        Order.GroupOf(Model(ModelProviders.Local, "e"), cannotRun: true).ShouldBe(ModelListGroup.CannotRun);
        Order.GroupOf(Model("google", "f"), cannotRun: true).ShouldBe(ModelListGroup.CannotRun, "the picker's red verdict always wins");
    }

    [Fact]
    public void RedIsLastAndTheGivenOrderIsKeptInsideEachGroup()
    {
        var bigLocal = Model(ModelProviders.Local, "big");
        var smallLocal = Model(ModelProviders.Local, "small");
        var cpuLocal = Model(ModelProviders.Local, "cpu");
        var lyria = Model("google", "lyria");
        var eleven = Model("elevenlabs", "music");
        var red = new HashSet<CatalogModel> { bigLocal };

        var ordered = Order.Order([bigLocal, lyria, smallLocal, eleven, cpuLocal], m => Order.GroupOf(m, red.Contains(m)));

        ordered.ShouldBe([smallLocal, cpuLocal, lyria, eleven, bigLocal]);
        Order.First(ordered, m => Order.GroupOf(m, red.Contains(m))).ShouldBe(smallLocal);
    }

    [Fact]
    public void WhenEverythingIsRedTheFirstIsStillChosen()
    {
        var a = Model(ModelProviders.Local, "a");
        var b = Model(ModelProviders.Local, "b");

        Order.First([a, b], _ => ModelListGroup.CannotRun).ShouldBe(a, "never a block (rule 19): a red model is offered and tried");
        Order.First(Array.Empty<CatalogModel>(), _ => ModelListGroup.Runs).ShouldBeNull();
    }
}
