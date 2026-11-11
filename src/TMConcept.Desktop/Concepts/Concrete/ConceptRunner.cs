using Avalonia.Threading;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Pipeline;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Work;

namespace TMConcept.Desktop.Concepts;

/// <summary>
/// The paid work of the concept page, one at a time (0.1.13): the two generate sections and "Suggest topics" share it, so while one runs
/// the other buttons are off with the reason, and the cost shown on the spending pill and in the activity log belongs to one call.
/// <see cref="RunAsync"/> makes the models ready (license, a key for an online provider, installs; nothing is paid before this passes),
/// runs <see cref="ConceptGenerator"/> with the section's <see cref="ConceptBrief"/>, follows the call step by step (on the section and on
/// the strip at the bottom of the window), puts each saved batch on the page at once (<see cref="BatchSaved"/>) and what it cost so far
/// on the spending pill, and ends with one report line (the activity log and a notification get it too). The models, the fine-tuning and
/// the word check come from the page's bar (<see cref="Attach"/>).
/// </summary>
public sealed class ConceptRunner(AppSession session, ConceptGenerator generator, StepModelResolver resolver, INotifier notifier, IActivityLog activity,
    Localizer text, WorkStatus work, SpendingMeter spending, ILogger<ConceptRunner> logger)
{
    private ConceptSettingsViewModel? _settings;

    /// <summary>Who runs now (a section of the page), or null.</summary>
    public object? Owner { get; private set; }

    /// <summary>Whether a paid call of the page runs.</summary>
    public bool Busy => Owner is not null;

    /// <summary>The page's bar: models, fine-tuning and the word check.</summary>
    public ConceptSettingsViewModel Settings => _settings ?? throw new InvalidOperationException("The concept page attaches its bar first.");

    /// <summary><see cref="Owner"/> changed.</summary>
    public event EventHandler? Changed;

    /// <summary>A batch was saved: its concepts go on the page at once.</summary>
    public event EventHandler<ConceptBatch>? BatchSaved;

    /// <summary>A run ended (whatever its outcome): the page reads its lists again.</summary>
    public event EventHandler? Finished;

    /// <summary>Uses the page's bar.</summary>
    public void Attach(ConceptSettingsViewModel settings) => _settings = settings;

    /// <summary>Takes the page's one paid call for <paramref name="owner"/>; false while another one runs.</summary>
    public bool TryEnter(object owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        if (Busy)
        {
            return false;
        }

        Owner = owner;
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Gives the page's paid call back (only its owner can).</summary>
    public void Leave(object owner)
    {
        if (ReferenceEquals(Owner, owner))
        {
            Owner = null;
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Runs <paramref name="brief"/> for <paramref name="owner"/>: <paramref name="call"/> follows the model call, <paramref name="progress"/>
    /// gets the one-line progress ("Batch 2/5 · … is writing (~3,100 tokens) · concept 2/5 ready"), <paramref name="saved"/> every saved
    /// batch (before <see cref="BatchSaved"/>).
    /// </summary>
    public async Task<ConceptRunReport> RunAsync(ConceptBrief brief, object owner, CallStatusViewModel call, Action<string> progress,
        Action<ConceptBatch> saved, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(brief);
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(saved);
        if (session.Profile is not { } profile)
        {
            return new ConceptRunReport(null, text["concepts.no_profile"], ActivityLevel.Error);
        }

        if (!TryEnter(owner))
        {
            return new ConceptRunReport(null, text["concepts.busy_other"], ActivityLevel.Warning);
        }

        ConceptCallProgress? live = null;
        void Show()
        {
            var line = ProgressLine(call, live);
            progress(line);
            work.Show(owner, text.Format("work.concepts", line));
        }

        void OnCall(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(CallStatusViewModel.Text) && call.Active)
            {
                Show();
            }
        }

        call.PropertyChanged += OnCall;
        call.Preparing();
        try
        {
            var picker = Settings.Picker;
            var needed = new List<string> { PipelineSteps.Concepts };
            if (picker.Model(PipelineSteps.Embedding) is not null)
            {
                needed.Add(PipelineSteps.Embedding);
            }

            if (!await picker.PrepareAsync(needed, "install.purpose.concepts", cancellationToken).ConfigureAwait(true))
            {
                return new ConceptRunReport(null, picker.Status, ActivityLevel.Error);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var model = await resolver.ResolveAsync(PipelineSteps.Concepts, picker.Choice(PipelineSteps.Concepts), CancellationToken.None).ConfigureAwait(true);
            var embedding = picker.Model(PipelineSteps.Embedding) is null
                ? null
                : await resolver.ResolveAsync(PipelineSteps.Embedding, picker.Choice(PipelineSteps.Embedding), CancellationToken.None).ConfigureAwait(true);
            call.Begin(model.Model.Name);
            var batches = new Progress<ConceptBatch>(batch =>
            {
                // The pill shows what the run has paid so far (the batch carries the run's total up to it).
                spending.Live(owner, batch.CostUsd);
                saved(batch);
                BatchSaved?.Invoke(this, batch);
            });
            var steps = new Progress<ConceptCallProgress>(p =>
            {
                live = p;
                call.Report(p.Call);
                Show();
            });
            var result = await generator.GenerateAsync(new ConceptRequest(profile, brief, model, Settings.LocalCheck, embedding,
                Settings.Focus(profile.ContentLanguage)), batches, steps, cancellationToken).ConfigureAwait(true);

            // The batches are reported from the generator's thread and posted here: they are all on the page (and the topics they made off
            // section A's list) before the run's end is reported.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.ContextIdle);
            return Report(result);
        }
        catch (ModelNotReadyException ex)
        {
            logger.LogWarning("Concept generation not started: the {Step} step has no {Missing}", ex.Step, ex.Missing);
            Settings.ModelsOpen = true;
            return new ConceptRunReport(null, text["concepts.no_model"], ActivityLevel.Error);
        }
        catch (OperationCanceledException)
        {
            // Canceled before anything was sent: nothing was paid.
            var line = text.Format("concepts.canceled", 0, text.Usd(0));
            activity.Add(ActivityLevel.Warning, line);
            return new ConceptRunReport(null, line, ActivityLevel.Warning);
        }
        catch (Exception ex)
        {
            // A locked database or an unexpected answer after some batches were saved: those stay on the page.
            logger.LogError(ex, "Concept generation failed");
            var line = text.Format("concepts.failed", ex.Message);
            activity.Add(ActivityLevel.Error, line);
            notifier.Notify(new AppNotification(NotificationKind.Failed, text["concepts.error.title"], line));
            return new ConceptRunReport(null, line, ActivityLevel.Error);
        }
        finally
        {
            call.PropertyChanged -= OnCall;
            call.Stop();
            progress(string.Empty);
            work.Clear(owner);
            Leave(owner);
            await spending.EndAsync(owner).ConfigureAwait(true);
            Finished?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// What <paramref name="brief"/> would cost with the models chosen now, the meaning check included (null without a profile, a brief or a
    /// concept model). The tiny estimate next to a section's button and the question before "Fill the missing" both come from here.
    /// </summary>
    public async Task<RunPrice?> PriceAsync(ConceptBrief? brief)
    {
        var picker = Settings.Picker;
        if (session.Profile is not { } profile || brief is null || picker.Model(PipelineSteps.Concepts) is not { } model)
        {
            return null;
        }

        var effort = Settings.ConceptEffort();
        var estimate = await generator.EstimateAsync(profile, brief, Settings.Focus(profile.ContentLanguage), model, effort, CancellationToken.None)
            .ConfigureAwait(true);
        var backfill = 0;
        if (picker.Model(PipelineSteps.Embedding) is { } embedding)
        {
            var size = await generator.BatchSizeAsync(profile, model, effort, CancellationToken.None).ConfigureAwait(true);
            var meaning = await generator.EstimateEmbeddingAsync(profile, embedding, brief.Count, CancellationToken.None, size).ConfigureAwait(true);
            estimate = estimate.Plus(meaning.Estimate);
            backfill = meaning.Backfill;
        }

        return new RunPrice(model.Name, estimate, backfill);
    }

    /// <summary>The estimate of <paramref name="brief"/> as the page shows it (empty without a profile, a brief or a concept model).</summary>
    public async Task<EstimateLine> EstimateAsync(ConceptBrief? brief)
    {
        if (await PriceAsync(brief).ConfigureAwait(true) is not { } price || brief is null)
        {
            return new EstimateLine(string.Empty, string.Empty);
        }

        var estimate = price.Estimate;
        var detail = text.Format("concepts.estimate", price.ModelName, brief.Count, text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd),
                Thousands(estimate.InputTokens), Thousands(estimate.OutputTokensMax))
            + (price.Backfill > 0 ? "\n" + text.Format("concepts.estimate.backfill", price.Backfill) : string.Empty);
        return new EstimateLine(text.Format("concepts.estimate.tiny", text.Usd(estimate.MinUsd), text.Usd(estimate.MaxUsd)), detail);
    }

    /// <summary>"Batch 2/5 · Claude Sonnet 5.5 is writing (~3,100 tokens) · concept 2/5 ready" (the batch only when there are several).</summary>
    private string ProgressLine(CallStatusViewModel call, ConceptCallProgress? live)
    {
        var parts = new List<string>();
        if (live is { Batches: > 1 } batches)
        {
            parts.Add(text.Format("concepts.progress", batches.Batch, batches.Batches));
        }

        if (call.Text.Length > 0)
        {
            parts.Add(call.Text);
        }

        if (live is { Call.Phase: ChatPhase.Writing, Ready: > 0 } writing)
        {
            parts.Add(text.Format("concepts.ready", writing.Ready, writing.Size));
        }
        else if (live is { Call.Phase: ChatPhase.Done })
        {
            parts.Add(text["concepts.saving"]);
        }

        return string.Join(" · ", parts);
    }

    /// <summary>The run's line for the section, the activity log and a notification.</summary>
    private ConceptRunReport Report(ConceptRunResult result)
    {
        var cost = text.Usd(result.Run.ActualUsd);
        var extra = string.Empty;
        if (result.RejectedDuplicates > 0)
        {
            var dropped = text.Format("concepts.duplicates_dropped", result.RejectedDuplicates);
            activity.Add(ActivityLevel.Info, dropped);
            extra += " · " + dropped;
        }

        var level = ActivityLevel.Info;
        if (result.SemanticSkipped > 0)
        {
            var skipped = text.Format("concepts.semantic.failed", result.SemanticSkipped);
            activity.Add(ActivityLevel.Warning, skipped);
            extra += " · " + skipped;
            level = ActivityLevel.Warning;
        }

        string line;
        switch (result.ErrorCode)
        {
            case "":
                line = text.Format("concepts.done", result.Accepted.Count, cost);
                activity.Add(ActivityLevel.Info, line);
                notifier.Notify(new AppNotification(NotificationKind.Done, text["concepts.ready.title"], line));
                break;
            case "canceled":
                line = text.Format("concepts.canceled", result.Accepted.Count, cost);
                activity.Add(ActivityLevel.Warning, line);
                level = ActivityLevel.Warning;
                break;
            case "truncated" or "incomplete":
                // The model reached its answer limit (or the answer ended early): the complete concepts before the cut were saved; a
                // warning, and only a failure when none was saved. The duplicates thrown away are told apart.
                var dropped = result.RejectedDuplicates > 0 ? text.Format("concepts.missing.dropped", result.RejectedDuplicates) : string.Empty;
                line = text.Format(result.ErrorCode == "truncated" ? "concepts.cut" : "concepts.incomplete", result.Accepted.Count, result.Missing, cost, dropped);
                level = result.Accepted.Count > 0 ? ActivityLevel.Warning : ActivityLevel.Error;
                activity.Add(level, line);
                notifier.Notify(new AppNotification(result.Accepted.Count > 0 ? NotificationKind.Warning : NotificationKind.Failed, text["concepts.cut.title"], line));
                return new ConceptRunReport(result, line, level);
            default:
                var reason = result.ErrorCode == "parse" ? text["concepts.parse_failed"] : ProviderErrors.Text(text, result.ErrorCode);
                line = text.Format("concepts.error", reason, result.Accepted.Count, cost);
                activity.Add(ActivityLevel.Error, line);
                notifier.Notify(new AppNotification(NotificationKind.Failed, text["concepts.error.title"], reason));
                return new ConceptRunReport(result, line, ActivityLevel.Error);
        }

        return new ConceptRunReport(result, line + extra, level);
    }

    private string Thousands(long tokens) => tokens < 1000 ? tokens.ToString(text.Culture) : text.Format("concepts.thousand", Math.Round(tokens / 1000.0));
}
