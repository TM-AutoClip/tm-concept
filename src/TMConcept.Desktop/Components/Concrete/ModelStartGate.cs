using Microsoft.Extensions.Logging;
using TMConcept.Core.Models;
using TMConcept.Core.Tools;
using TMConcept.Desktop.Dialogs;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;

namespace TMConcept.Desktop.Components;

/// <inheritdoc cref="IModelStartGate" />
public sealed class ModelStartGate(AppSession session, IConfirmDialog confirm, IApiKeyDialog keyDialog, StepModelResolver resolver,
    ComponentInstallFlow installs, Localizer text, ILogger<ModelStartGate> logger) : IModelStartGate
{
    /// <inheritdoc />
    public async Task<bool> AcceptLicenseAsync(CatalogModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var settings = session.Settings;
        var key = ModelBase.KeyOf(model);
        if (model.LicenseUse == LicenseUse.Commercial || settings.AcceptedModelLicenses.Contains(key))
        {
            return true;
        }

        // Asked once per model, before its first download (0.1.17: non-commercial and conditional licenses are offered with a clear warning).
        var message = text.Format(model.LicenseUse == LicenseUse.NonCommercial ? "models.license.message.noncommercial" : "models.license.message.limited",
            model.Name, text["license." + model.License], text["license." + model.License + ".terms"], model.LicenseUrl);
        // Not a danger question (0.1.18): the license limits the use, the model runs; red is only for "cannot run on this card".
        var answer = await confirm.AskAsync(text.Format("models.license.title", model.Name), message, text["engines.license.accept"], danger: false)
            .ConfigureAwait(true);
        if (!answer.Confirmed)
        {
            logger.LogInformation("The license of {Model} ({License}) was declined", key, model.License);
            return false;
        }

        settings.AcceptedModelLicenses.Add(key);
        await session.SaveAsync(settings, CancellationToken.None).ConfigureAwait(true);
        logger.LogInformation("Setting changed: the license of {Model} ({License}) was accepted", key, model.License);
        return true;
    }

    /// <inheritdoc />
    public async Task<KeyGateOutcome> KeysAsync(IReadOnlyList<CatalogModel> models, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(models);
        var untested = new List<string>();
        foreach (var provider in await resolver.ProvidersWithoutWorkingKeyAsync(models, cancellationToken).ConfigureAwait(true))
        {
            logger.LogWarning("The provider {Provider} has no working API key; one is asked for before the process starts", provider);
            if (!await keyDialog.AddKeyAsync(provider).ConfigureAwait(true))
            {
                logger.LogWarning("Process not started: no API key was added for {Provider}", provider);
                return new KeyGateOutcome(provider, untested);
            }

            if (!resolver.CanTest(provider))
            {
                // No free test exists (fal): the key was saved untested; it is checked at its first use.
                untested.Add(provider);
            }
        }

        return new KeyGateOutcome(null, untested);
    }

    /// <inheritdoc />
    public async Task<ModelStartOutcome> AllowAsync(IReadOnlyList<CatalogModel> models, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(models);
        foreach (var model in models)
        {
            if (!await AcceptLicenseAsync(model).ConfigureAwait(true))
            {
                return new ModelStartOutcome(false, text.Format("picker.not_started.license", model.Name));
            }
        }

        if ((await KeysAsync(models, cancellationToken).ConfigureAwait(true)).MissingProvider is { } provider)
        {
            return new ModelStartOutcome(false, text.Format("picker.not_started.key", keyDialog.ProviderName(provider)));
        }

        return new ModelStartOutcome(true, string.Empty);
    }

    /// <inheritdoc />
    public async Task<ModelStartOutcome> PrepareAsync(IReadOnlyList<CatalogModel> models, IReadOnlyList<string> components, string purposeKey,
        IProgress<ToolProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(components);
        var allowed = await AllowAsync(models, cancellationToken).ConfigureAwait(true);
        if (!allowed.Ready)
        {
            return allowed;
        }

        var codes = components.Concat(models.SelectMany(m => m.Requires)).Distinct(StringComparer.Ordinal).ToList();
        var installed = await InstallAsync(codes, purposeKey, progress, cancellationToken).ConfigureAwait(true);
        if (!installed.Ready)
        {
            logger.LogWarning("Process not started: what {Models} need is not installed", string.Join(", ", models.Select(ModelBase.KeyOf)));
        }

        return installed;
    }

    /// <inheritdoc />
    public async Task<ModelStartOutcome> InstallAsync(IReadOnlyCollection<string> codes, string purposeKey, IProgress<ToolProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(codes);
        if (codes.Count == 0)
        {
            return new ModelStartOutcome(true, string.Empty);
        }

        var outcome = await installs.EnsureAsync(codes, purposeKey, progress, cancellationToken).ConfigureAwait(true);
        if (!outcome.Ready)
        {
            return new ModelStartOutcome(false, text.Format("picker.not_started.install", outcome.Reason));
        }

        return new ModelStartOutcome(true, string.Empty);
    }
}
