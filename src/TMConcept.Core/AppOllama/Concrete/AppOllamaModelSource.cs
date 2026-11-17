using Microsoft.Extensions.Logging;
using TMConcept.Core.Hardware;
using TMConcept.Core.Models;

namespace TMConcept.Core.AppOllama;

/// <summary>
/// The local models of the app's own Ollama copy (provider <see cref="ModelProviders.AppOllama"/>): on the graphics card when this computer
/// has a card of a known maker with a runtime Ollama can use (the CUDA driver or a Vulkan loader), otherwise on the processor.
/// </summary>
public sealed class AppOllamaModelSource(AppModelRegistry registry, IAppOllamaServer server, IAppOllamaApi api, IGpuInfo gpu, ILogger<AppOllamaModelSource> logger)
    : ILocalModelSource
{
    /// <inheritdoc />
    public ILocalModel? Create(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!string.Equals(model.Provider, ModelProviders.AppOllama, StringComparison.Ordinal))
        {
            return null;
        }

        var info = registry.Info(model.Id) ?? AppModelAdvisor.Find(model.Id) ?? AppModelAdvisor.Unknown(model.Id, 0);
        return new AppOllamaLocalModel(model, info, server, api, OnCard(gpu.Detect()), logger);
    }

    /// <summary>Whether Ollama can use a graphics card here: one of a known maker, with the CUDA driver (NVIDIA) or a Vulkan loader.</summary>
    public static bool OnCard(GpuReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return report.Adapters.Any(a => (a.Vendor == GpuVendor.Nvidia && report.CudaDriver)
            || ((a.Vendor is GpuVendor.Nvidia or GpuVendor.Amd or GpuVendor.Intel) && report.VulkanLoader));
    }
}
