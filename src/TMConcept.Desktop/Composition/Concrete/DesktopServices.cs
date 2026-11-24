using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TMConcept.Core.Activity;
using TMConcept.Core.Concepts;
using TMConcept.Core.Duplicates;
using TMConcept.Core.Keys;
using TMConcept.Core.Localization;
using TMConcept.Core.Models;
using TMConcept.Core.Notifications;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Settings;
using TMConcept.Core.Templates;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Notifications;
using TMConcept.Desktop.Os;
using TMConcept.Desktop.Paths;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;
using TMConcept.Storage.Backup;
using TMConcept.Storage.Crypto;
using TMConcept.Storage.Database;
using TMConcept.Storage.Keys;
using TMConcept.Storage.Stores;

namespace TMConcept.Desktop.Composition;

/// <summary>The composition root: every service of the app. Lists (pages, settings sections, providers, engines) come from here.</summary>
public static class DesktopServices
{
    private static readonly IOsModule[] OsModules = [new WindowsOsModule(), new LinuxOsModule()];

    /// <summary>The operating system module of this computer.</summary>
    public static IOsModule CurrentOs { get; } =
        OsModules.FirstOrDefault(m => m.IsCurrent) ?? throw new PlatformNotSupportedException("TM Concept runs on Windows and Linux.");

    /// <summary>Builds the service provider.</summary>
    public static ServiceProvider Build(AppPaths paths, Action<IServiceCollection>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var services = new ServiceCollection();
        // Created (and disposed: the file is closed) by the container: Information and above, one file per day, kept 7 days.
        // Paths are masked with this app's folders: the library (profile and video folder names), a data or documents folder outside home.
        services.AddSingleton(LogScrubber.For(paths));
        services.AddSingleton<ILoggerProvider>(s => new FileLoggerProvider(paths.LogDirectory, LogRules.AppKind, s.GetRequiredService<TimeProvider>(),
            s.GetRequiredService<LogScrubber>()));
        services.AddLogging(b => b.SetMinimumLevel(FileLoggerProvider.MinimumLevel));

        // Package C (user-facing errors): warnings and errors of every logger also reach the activity panel. The feed is taken on the first
        // line, not here: the activity log needs a logger, so resolving it while the logger factory is built would be a cycle.
        services.AddSingleton<ILoggerProvider>(s => new ActivityLoggerProvider(() => s.GetService<IActivityFeed>(), s.GetRequiredService<LogScrubber>()));
        services.AddSingleton(TimeProvider.System);
        // The app's step timer: the start steps before the services are measured on the same one (Program).
        services.AddSingleton<Core.Diagnostics.IStepTimer>(Core.Diagnostics.StepTimer.Process);
        services.AddSingleton(s => new CrashLog(s.GetRequiredService<ILoggerFactory>().CreateLogger("TMConcept.Crash")));
        services.AddSingleton(paths);
        services.AddSingleton<IAppPaths>(paths);
        services.AddSingleton<UiPreferencesFile>();
        services.AddSingleton<Localizer>();
        services.AddSingleton<ILocalizer>(s => s.GetRequiredService<Localizer>());
        services.AddSingleton<LanguageResources>();
        services.AddSingleton(CurrentOs);
        services.AddSingleton<ITemplateSource, EmbeddedTemplateSource>();

        AddStorage(services);
        ProviderServices.Add(services);
        services.AddSingleton<Core.Processes.IProcessRunner, Engines.Processes.ProcessRunner>();
        services.AddSingleton(Engines.Tools.DownloadTransport.Network);
        services.AddSingleton<Engines.Tools.ToolDownloader>();
        services.AddSingleton<Core.Tools.IArchiveDownloader>(s => s.GetRequiredService<Engines.Tools.ToolDownloader>());
        services.AddSingleton<Core.Tools.IZipEntryDownloader, Engines.Tools.ZipEntryDownloader>();
        services.AddSingleton<Engines.Media.FfmpegMuxer>();
        services.AddSingleton<Core.Media.IMediaTools, Engines.Media.FfmpegMedia>();
        services.AddSingleton<Core.Media.IMotionFrames, Engines.Media.FfmpegMotionFrames>();
        services.AddSingleton<Core.Media.IPlanSheet, Engines.Media.FfmpegPlanSheet>();
        services.AddSingleton<Core.Plans.ScenePlanner>();

        // 0.1.45 (C4, F7, F8, S6): the ready music library (a component, downloaded after a confirmation), the music analysis with the app's
        // ffmpeg and the Openverse audio search (no key; only when the user searches).
        services.AddSingleton<Core.Music.MusicLibraryStore>();
        services.AddSingleton<Core.Music.IMusicLibrary>(s => s.GetRequiredService<Core.Music.MusicLibraryStore>());
        services.AddSingleton<Core.Components.IComponentSource, Core.Music.MusicLibraryComponentSource>();
        services.AddSingleton<Core.Music.IMusicAudio, Engines.Media.FfmpegMusicAudio>();
        services.AddSingleton<Core.Music.IMusicSearch, Providers.Music.OpenverseAudioClient>();
        // 0.1.46 (C5): generated music — the local ACE-Step 1.5 through rakkase (two components, installed only after a yes) and the online
        // Lyria / ElevenLabs Music models.
        services.AddSingleton<Speech.Music.RakkaseLayout>();
        services.AddSingleton<Speech.Music.RakkaseInstaller>();
        services.AddSingleton<Core.Components.IComponentSource, Speech.Music.MusicComponentSource>();
        services.AddSingleton<ILocalModelSource, Speech.Music.MusicModelSource>();
        services.AddSingleton<Core.Music.IMusicGenerator, Speech.Music.RakkaseMusicGenerator>();
        services.AddSingleton<Core.Music.IMusicGenerator, Providers.Music.LyriaMusicGenerator>();
        services.AddSingleton<Core.Music.IMusicGenerator, Providers.Music.ElevenLabsMusicGenerator>();
        services.AddSingleton<Core.Music.IMusicGenerators, Core.Music.MusicGenerators>();
        services.AddSingleton<Core.Videos.VideoMusicStep>();
        // 0.1.48 (F5, S3): the subtitles' word times heard by whisper.cpp (program and model are components, installed only after a yes);
        // without them the subtitles keep their estimated times.
        services.AddSingleton(s => new Engines.Recognition.RecognizerModelStore(s.GetRequiredService<Core.Paths.IAppPaths>(),
            s.GetRequiredService<Engines.Tools.ResumingDownloader>(), s.GetRequiredService<ILogger<Engines.Recognition.RecognizerModelStore>>()));
        services.AddSingleton<Core.Components.IComponentSource, Engines.Recognition.RecognizerComponentSource>();
        services.AddSingleton<Core.Subtitles.IWordRecognizer, Engines.Recognition.WhisperCppRecognizer>();
        services.AddSingleton<Core.Videos.SubtitleTimingStep>();
        services.AddSingleton<Core.Videos.VideoAssembler>();
        services.AddSingleton<Core.Videos.IQualityReviewer, Core.Videos.QualityReviewer>();
        services.AddSingleton<Core.Videos.QualityStep>();
        services.AddSingleton<Core.Videos.VideoPipeline>();
        // 0.1.28: deleting something deletes everything it generated, for good (files first, the record after).
        services.AddSingleton<Core.Deletion.VideoDeletion>();
        services.AddSingleton<Core.Deletion.ConceptDeletion>();
        services.AddSingleton<Core.Deletion.ProfileDeletion>();
        services.AddSingleton<Core.Videos.IVideoQueueRunner, Core.Videos.VideoQueueRunner>();
        services.AddSingleton<Core.Videos.SceneNarrator>();
        services.AddSingleton<Core.Videos.VideoRemaker>();
        services.AddSingleton<Core.Videos.SceneFixer>();
        services.AddSingleton<Core.Videos.VideoCostEstimator>();
        services.AddSingleton<Speech.Speech.SpeechModelInstaller>();
        services.AddSingleton(s => new Speech.Speech.SpeechWorkerClient(s.GetRequiredService<Core.Processes.IChildProcessGuard>(),
            s.GetRequiredService<ILogger<Speech.Speech.SpeechWorkerClient>>()) { LogDirectory = paths.LogDirectory });
        services.AddSingleton<Core.Speech.ISpeechEngine, Speech.Speech.SupertonicSpeechEngine>();
        services.AddSingleton<Core.Speech.ISpeechEngine, Speech.Speech.KokoroSpeechEngine>();
        services.AddSingleton<Core.Speech.IVoiceSampleConverter, Engines.Media.FfmpegVoiceSampleConverter>();
        services.AddSingleton<Core.Speech.VoiceSampleLibrary>();
        services.AddSingleton<Core.Speech.ISpeechEngine, Speech.Speech.PocketSpeechEngine>();
        services.AddSingleton<Core.Speech.ISpeechEngine, Speech.Speech.ChatterboxSpeechEngine>();

        // The Python voices (0.1.17): uv installs a pinned Python and one environment per voice family into <tools>/speech-py; each voice speaks
        // in one Python worker (one voice at a time), on an NVIDIA card through CUDA when there is one.
        services.AddSingleton<Speech.Python.PythonLayout>();
        services.AddSingleton<Speech.Python.PythonInstaller>();
        services.AddSingleton<Speech.Python.PythonModelStore>();
        services.AddSingleton<Speech.Python.PythonWorkerClient>();
        foreach (var voice in Speech.Python.PythonVoices.All)
        {
            services.AddSingleton<Core.Speech.ISpeechEngine>(s => new Speech.Python.PythonSpeechEngine(voice, s.GetRequiredService<Speech.Python.PythonLayout>(),
                s.GetRequiredService<Speech.Python.PythonInstaller>(), s.GetRequiredService<Speech.Python.PythonModelStore>(),
                s.GetRequiredService<Speech.Python.PythonWorkerClient>(), s.GetRequiredService<Core.Speech.VoiceSampleLibrary>(),
                s.GetRequiredService<Core.Hardware.IGpuInfo>(), s.GetRequiredService<ILogger<Speech.Python.PythonSpeechEngine>>()));
        }

        services.AddSingleton<Core.Speech.ISpeechEngine, Speech.Speech.AhenkCepSpeechEngine>();
        services.AddSingleton<Speech.Embeddings.LocalEmbedder>();
        services.AddSingleton<Core.Duplicates.ILocalEmbedder>(s => s.GetRequiredService<Speech.Embeddings.LocalEmbedder>());
        services.AddSingleton<Core.Speech.ISpeechEngine, Providers.Speech.OpenAISpeechEngine>();
        services.AddSingleton<Core.Speech.ISpeechEngine, Providers.Speech.GeminiSpeechEngine>();
        services.AddSingleton<Core.Speech.ISpeechEngine, Providers.Speech.GoogleCloudSpeechEngine>();
        services.AddSingleton<Core.Speech.ISpeechEngine, Providers.Speech.ElevenLabsSpeechEngine>();
        // 0.1.47 (C6, H4): the voices of the user's ElevenLabs account, read when a key is saved and when a voice picker shows them.
        services.AddSingleton<Core.Speech.IAccountVoiceSource, Providers.Speech.ElevenLabsVoiceSource>();
        services.AddSingleton<Core.Speech.AccountVoiceLibrary>();
        services.AddSingleton<Core.Pronunciation.ISpeechTextNormalizer, Core.Pronunciation.TurkishSpeechTextNormalizer>();
        services.AddSingleton<Core.Pronunciation.PronunciationService>();
        services.AddSingleton<Core.Speech.SpeechService>();
        services.AddSingleton<Core.Images.IImageEngine, Providers.Images.FalImageEngine>();
        services.AddSingleton<Core.Images.IImageEngine, Providers.Images.OpenAIImageEngine>();
        services.AddSingleton<Core.Images.IImageEngine, Providers.Images.ZaiImageEngine>();
        // 0.1.17: image model weights (several GB each) come through the downloader that retries and resumes a broken transfer.
        services.AddSingleton<Engines.Tools.ResumingDownloader>();
        services.AddSingleton(s => new Engines.Images.LocalImageStore(s.GetRequiredService<Core.Paths.IAppPaths>(), s.GetRequiredService<Engines.Tools.ResumingDownloader>(),
            s.GetRequiredService<ILogger<Engines.Images.LocalImageStore>>()));
        services.AddSingleton<Core.Hardware.ISystemMemory, Engines.Hardware.SystemMemory>();
        services.AddSingleton<Core.Images.IImageEngine, Engines.Images.SdCppImageEngine>();
        // Package A (graphics cards, image builds): the card detector (its sources come from the OS module), the Image acceleration setting
        // of preferences.json and the image build choice the image engine and the stable-diffusion.cpp component share.
        services.AddSingleton<Engines.Hardware.IComputeRuntimeProbe, Engines.Hardware.NativeRuntimeProbe>();
        services.AddSingleton<Core.Hardware.IGpuInfo, Engines.Hardware.GpuDetector>();
        services.AddSingleton<PreferencesImageAcceleration>();
        services.AddSingleton<Core.Hardware.IImageAccelerationSetting>(s => s.GetRequiredService<PreferencesImageAcceleration>());
        services.AddSingleton<PreferencesOcrAcceleration>();
        services.AddSingleton<Core.Hardware.IOcrAccelerationSetting>(s => s.GetRequiredService<PreferencesOcrAcceleration>());
        services.AddSingleton<Engines.Images.ImageBackendSelector>();

        // 0.1.17 (one pipeline, local and online models mixed): every model is an IModel; a local one on the graphics card is used only inside
        // a lease of the one coordinator, which unloads the previous model and measures the free memory first.
        services.AddSingleton<Core.Hardware.IVramProbe, Engines.Hardware.VramProbe>();
        // 0.1.18: every handover checks the memory first (3 s after an unload, again 2.5 s later, then the user's Retry / Cancel question).
        services.AddSingleton(MemoryWaitOptions.Default);
        services.AddSingleton<IMemoryShortfallHandler, Dialogs.MemoryShortfallDialog>();
        services.AddSingleton<IMemoryNotices, Memory.MemoryNoticeLog>();
        services.AddSingleton<IMemoryWaiter, MemoryWaiter>();
        services.AddSingleton<IModelDirectory, ModelDirectory>();
        services.AddSingleton<IGpuCoordinator, GpuCoordinator>();
        services.AddSingleton<ILocalModelSource, Engines.Images.SdCppModelSource>();
        // The Python voices' source comes first: the speech worker's source takes every other local voice.
        services.AddSingleton<ILocalModelSource, Speech.Python.PythonModelSource>();
        services.AddSingleton<ILocalModelSource, Speech.Speech.WorkerModelSource>();
        services.AddSingleton<Engines.Tools.ToolInstaller>();
        services.AddSingleton<Core.Tools.IToolLocator>(s => s.GetRequiredService<Engines.Tools.ToolInstaller>());
        services.AddSingleton<Core.Processes.IChildProcessGuard>(s => s.GetRequiredService<Engines.Processes.ChildProcessGuardBase>());
        services.AddSingleton<Engines.Engines.ChromeLocator>();
        services.AddSingleton<Engines.Engines.SampleRender>();
        services.AddSingleton<Core.Engines.IVideoEngine, Engines.Engines.HyperFramesEngine>();
        services.AddSingleton<Core.Engines.IVideoEngine, Engines.Engines.RemotionEngine>();
        services.AddSingleton<Core.Engines.IVideoEngine, Engines.Engines.RevideoEngine>();

        // Everything a chosen model can need on this computer (installed when a process that uses it starts, after one confirmation).
        services.AddSingleton<Core.Components.IComponentSource, Engines.Components.EngineComponentSource>();
        services.AddSingleton<Core.Components.IComponentSource, Speech.Components.VoiceComponentSource>();
        services.AddSingleton<Core.Components.IComponentSource, Speech.Components.EmbeddingComponentSource>();
        services.AddSingleton<Core.Components.IComponentSource, Speech.Components.PythonComponentSource>();
        // 0.1.20: the app's own Ollama copy ("Ollama (TM Concept)"), installed and run by the app on a port of its own, with its own models
        // folder; never the user's Ollama. The program and every model are components (downloaded only after a confirmation); the server
        // starts on the first use and ends with the app; its models take the graphics card like every local model.
        services.AddSingleton(new Core.AppOllama.AppOllamaLayout(Path.Combine(paths.ToolsDirectory, "ollama-models"),
            Path.Combine(paths.ToolsDirectory, "ollama-home")));
        services.AddSingleton<Core.AppOllama.AppModelStore>();
        services.AddSingleton<Core.AppOllama.AppModelRegistry>();
        services.AddSingleton<Engines.AppOllama.AppOllamaComponent>();
        services.AddSingleton<Core.AppOllama.IAppOllamaRuntime>(s => s.GetRequiredService<Engines.AppOllama.AppOllamaComponent>());
        services.AddSingleton<Core.Components.IComponentSource, Engines.AppOllama.AppOllamaComponentSource>();
        services.AddSingleton<Core.Components.IComponentSource, Core.AppOllama.AppModelComponentSource>();
        services.AddSingleton<Core.AppOllama.IAppOllamaLauncher, Engines.AppOllama.AppOllamaLauncher>();
        services.AddSingleton<Core.AppOllama.AppOllamaServer>();
        services.AddSingleton<Core.AppOllama.IAppOllamaServer>(s => s.GetRequiredService<Core.AppOllama.AppOllamaServer>());
        services.AddSingleton<ILocalModelSource, Core.AppOllama.AppOllamaModelSource>();
        services.AddSingleton<IResidentModelSweep, Core.AppOllama.AppOllamaResidentSweep>();
        services.AddSingleton<Core.Components.IInstallRecord>(s => new Components.InstallRecordFile(paths, Logging.AppIdentity.ShortVersion,
            s.GetRequiredService<TimeProvider>(), s.GetRequiredService<ILogger<Components.InstallRecordFile>>()));
        services.AddSingleton<Core.Components.ComponentPlanner>();
        services.AddSingleton<Components.ComponentInstallFlow>();
        services.AddSingleton<Components.IComponentWorkGate, Components.ComponentWorkGate>();
        AddDocuments(services);
        AddDesktop(services);
        overrides?.Invoke(services);
        services.AddSingleton(s =>
        {
            var locator = new ViewLocator(s.GetRequiredService<Core.Diagnostics.IStepTimer>());
            foreach (var registration in s.GetServices<IViewRegistration>())
            {
                registration.Register(locator);
            }

            return locator;
        });
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    /// <summary>
    /// "Belgelerim" and the sources of the concept page (0.1.21; "Araştırmadan" 0.1.22, <see cref="AddResearch"/>): the readers of each file kind, the web page and Wikipedia fetchers,
    /// the meaning model (a component, downloaded after a confirmation) and the keyword search, the library, the documents source and "Find
    /// topics". The concept generator picks the source that takes the page's brief; the page only fills the brief from the user's choices.
    /// </summary>
    private static void AddDocuments(IServiceCollection services)
    {
        services.AddSingleton<IConceptSource, TopicConceptSource>();
        services.AddSingleton<IConceptSource, IdeaConceptSource>();
        services.AddSingleton<IConceptSource, Core.Documents.DocumentConceptSource>();
        services.AddSingleton(TMConcept.Documents.Web.DocumentTransport.Network);
        services.AddSingleton<Core.Documents.IDocumentExtractor, TMConcept.Documents.Extractors.PdfExtractor>();
        services.AddSingleton<Core.Documents.IDocumentExtractor, TMConcept.Documents.Extractors.DocxExtractor>();
        services.AddSingleton<Core.Documents.IDocumentExtractor, TMConcept.Documents.Extractors.PlainTextExtractor>();
        services.AddSingleton<Core.Documents.IDocumentExtractor, TMConcept.Documents.Extractors.MarkdownExtractor>();
        services.AddSingleton<Core.Documents.IDocumentFetcher>(s => s.GetRequiredService<TMConcept.Documents.Web.WebPageFetcher>());
        services.AddSingleton<Core.Documents.IDocumentFetcher>(s => s.GetRequiredService<TMConcept.Documents.Web.WikipediaFetcher>());
        services.AddSingleton<TMConcept.Documents.Embeddings.E5Embedder>();
        services.AddSingleton<Core.Documents.IDocumentEmbedder>(s => s.GetRequiredService<TMConcept.Documents.Embeddings.E5Embedder>());
        services.AddSingleton<Core.Components.IComponentSource, TMConcept.Documents.Embeddings.E5ComponentSource>();
        services.AddSingleton<Core.Documents.IPassageRanker, Core.Documents.Bm25Ranker>();
        services.AddSingleton<Core.Documents.IPassageRanker, Core.Documents.CosineRanker>();
        services.AddSingleton<Core.Documents.PassageRetriever>();
        services.AddSingleton<Core.Documents.DocumentGrounder>();
        services.AddSingleton<Core.Documents.DocumentLibrary>();
        services.AddSingleton<Core.Documents.DocumentTopicFinder>();
        services.AddSingleton<Core.Documents.DocumentTextReader>();
        services.AddSingleton<Core.Documents.DocumentDigester>();

        // 0.1.25: the local OCR of scanned pages (an optional component) and its image decoder.
        services.AddSingleton<TMConcept.Documents.Ocr.IPageImageDecoder, Documents.SkiaPageImageDecoder>();
        services.AddSingleton<TMConcept.Documents.Ocr.LocalOcrEngine>();
        services.AddSingleton<Core.Components.IComponentSource, TMConcept.Documents.Ocr.LocalOcrComponentSource>();

        // 0.1.27: PDF pages drawn by PDFium (a component) for the OCR and vision models; the local OCR on the graphics card (DirectML, Windows,
        // a component) page by page when the card is free.
        services.AddSingleton<TMConcept.Documents.Pdf.PdfiumStore>();
        services.AddSingleton<TMConcept.Documents.Pdf.IPdfPageRenderer, TMConcept.Documents.Pdf.PdfiumRenderer>();
        services.AddSingleton<TMConcept.Documents.Pdf.IPageImageEncoder, Documents.SkiaPageImageEncoder>();
        services.AddSingleton<Core.Components.IComponentSource, TMConcept.Documents.Pdf.PdfiumComponentSource>();
        services.AddSingleton<TMConcept.Documents.Ocr.DirectMlRuntimeStore>();
        services.AddSingleton<Core.Components.IComponentSource, TMConcept.Documents.Ocr.DirectMlRuntimeComponentSource>();
        services.AddSingleton<TMConcept.Documents.Ocr.DirectMlOcrNetworkFactory>();
        services.AddSingleton<TMConcept.Documents.Ocr.OcrGpuModel>();
        AddResearch(services);
    }

    /// <summary>
    /// "Araştırmadan" (0.1.22): the fourth source of the concept page and its backends. Wikipedia, Wikidata and the user's addresses are
    /// the app's own fetch-based research (no paid search of the model providers); Tavily needs the user's own key; the user's MCP servers
    /// (advanced, off by default) are reached through the app's minimal MCP client. Nothing here downloads a model.
    /// </summary>
    private static void AddResearch(IServiceCollection services)
    {
        services.AddSingleton<IConceptSource, Core.Research.ResearchConceptSource>();
        services.AddSingleton<Core.Research.ResearchRun>();
        services.AddSingleton<TMConcept.Documents.Web.WebPageFetcher>();
        services.AddSingleton<TMConcept.Documents.Web.WikipediaFetcher>();
        services.AddSingleton<TMConcept.Documents.Research.WikipediaResearchBackend>();
        services.AddSingleton<TMConcept.Documents.Research.WikidataResearchBackend>();
        services.AddSingleton<TMConcept.Documents.Research.UrlResearchBackend>();
        services.AddSingleton<TMConcept.Documents.Research.TavilyResearchBackend>();
        services.AddSingleton(TMConcept.Documents.Mcp.McpHttpTransport.Network);
        services.AddSingleton<TMConcept.Documents.Mcp.McpConnector>();
        services.AddSingleton<TMConcept.Documents.Research.ResearchBackendSet>();
    }

    private static void AddStorage(IServiceCollection services)
    {
        services.AddSingleton<DatabaseKey>();
        services.AddSingleton(KdfParameters.Default);
        services.AddSingleton<UiMasterPassword>();
        services.AddSingleton<IMasterPasswordSource>(s => s.GetRequiredService<UiMasterPassword>());
        services.AddSingleton<IKeyProtector, PasswordKeyProtector>();
        CurrentOs.Register(services);
        services.AddSingleton<Vault>();
        services.AddSingleton<BackupService>();
        services.AddSingleton<ConceptDatabase>();
        services.AddSingleton<ISettingsStore, SettingsStore>();
        services.AddSingleton<IProfileStore, ProfileStore>();
        services.AddSingleton<IConceptStore, ConceptStore>();
        services.AddSingleton<IRunStore, RunStore>();
        services.AddSingleton<IQueueStore, QueueStore>();
        services.AddSingleton<IVideoStore, VideoStore>();
        services.AddSingleton<IVideoRatingStore, VideoRatingStore>();
        services.AddSingleton<IVideoRater, VideoRater>();
        services.AddSingleton<Core.Styles.IVideoStyleStore, VideoStyleStore>();
        services.AddSingleton<Core.Styles.IVideoPaletteStore, VideoPaletteStore>();
        services.AddSingleton<IApiKeyStore, ApiKeyStore>();
        services.AddSingleton<IStepModelStore, StepModelStore>();
        services.AddSingleton<Core.Pronunciation.IPronunciationStore, PronunciationStore>();
        services.AddSingleton<Core.Speech.IVoiceSampleStore, VoiceSampleStore>();
        services.AddSingleton<ITopicSuggestionStore, TopicSuggestionStore>();
        services.AddSingleton<IConceptWishStore, ConceptWishStore>();
        services.AddSingleton<Core.Concepts.IPromptMemorySource, Core.Concepts.PromptMemorySource>();
        services.AddSingleton<IEmbeddingStore, EmbeddingStore>();
        services.AddSingleton<Core.Documents.IDocumentStore, DocumentStore>();
        services.AddSingleton<Core.Documents.IDocumentPageStore>(s => (DocumentStore)s.GetRequiredService<Core.Documents.IDocumentStore>());

        // Local servers (package B): kept as settings rows, no table of their own.
        services.AddSingleton<Core.Endpoints.IEndpointStore, EndpointStore>();
        services.AddSingleton<ISignInStore, SignInStore>();
        services.AddSingleton<Core.Speech.IAccountVoiceCache, AccountVoiceCache>();
    }

    private static void AddDesktop(IServiceCollection services)
    {
        services.AddSingleton<AppSession>();
        services.AddSingleton<ConceptEvents>();
        services.AddSingleton<Core.Concepts.ConceptGenerator>();
        services.AddSingleton<Core.Concepts.IFocusSource, Core.Concepts.EmbeddedFocusSource>();
        services.AddSingleton<Core.Concepts.TopicSuggester>();
        services.AddSingleton<Core.Concepts.ConceptEditor>();
        services.AddSingleton<ShellLauncher>();
        services.AddSingleton<IShellLauncher>(s => s.GetRequiredService<ShellLauncher>());
        services.AddSingleton<WindowHost>();
        services.AddSingleton<FileDialogs>();
        services.AddSingleton<ActivityLog>();
        services.AddSingleton<IActivityLog>(s => s.GetRequiredService<ActivityLog>());
        services.AddSingleton<IActivityFeed>(s => s.GetRequiredService<ActivityLog>());
        services.AddSingleton<Work.WorkStatus>();
        services.AddSingleton<Work.SpendingMeter>();
        services.AddSingleton<ToastNotifier>();
        services.AddSingleton<INotifier>(s => s.GetRequiredService<ToastNotifier>());
        services.AddSingleton<AppStartup>();
        services.AddSingleton<ShellViewModel>();
        services.AddTransient<FirstRunViewModel>();
        services.AddTransient<UnlockViewModel>();

        // One set of model pickers per page that uses models (Generate concepts, Concept → video, Profile).
        services.AddTransient<StepPickerViewModel>();
        services.AddTransient<VersionEditorViewModel>();
        services.AddTransient<TimeNotesViewModel>();
        services.AddTransient<PlanReviewViewModel>();
        services.AddTransient<ProductionChecklistViewModel>();

        // The concept page's parts: the page (a singleton) gets its own bar, runner, sections and detail panel; the status changes are one
        // service for every page.
        services.AddSingleton<Concepts.ConceptActions>();
        services.AddTransient<Concepts.ConceptRunner>();
        services.AddTransient<ConceptSettingsViewModel>();
        services.AddTransient<TopicSectionViewModel>();
        services.AddTransient<IdeaSectionViewModel>();
        services.AddTransient<DocumentSectionViewModel>();
        services.AddTransient<ResearchSectionViewModel>();
        services.AddTransient<DocumentLibraryViewModel>();
        services.AddTransient<ConceptDetailViewModel>();
        services.AddSingleton<Dialogs.IApiKeyDialog, Dialogs.ApiKeyDialog>();

        // Local servers (package B): the form on Settings → API keys that adds them.
        services.AddSingleton<EndpointEditorViewModel>();
        services.AddSingleton<GoogleSignInViewModel>();

        services.AddSingleton<Dialogs.IConfirmDialog, Dialogs.ConfirmDialog>();
        services.AddSingleton<Dialogs.IInstallDialog, Dialogs.InstallDialog>();

        // Package C (version change): after a new app version the installed components are updated, reinstalled or left for later.
        services.AddSingleton<Dialogs.IUpdateDialog, Dialogs.UpdateDialog>();
        services.AddSingleton(Updates.UpdateTransport.Network);
        services.AddSingleton<Updates.ReleaseFeed>();
        services.AddSingleton<Updates.SelfReplacer>();
        services.AddSingleton<Updates.IAppRestarter, Updates.AppRestarter>();
        services.AddSingleton<Updates.AppUpdater>();

        // Two editions (0.1.26): installed by Setup (Velopack; Program.Main registers what Velopack found) or the portable program.
        services.AddSingleton<Updates.IAppEdition>(_ => Updates.VelopackEdition.Current());
        services.AddSingleton<Updates.IInstalledUpdates, Updates.VelopackUpdates>();
        services.AddSingleton<Updates.ISetupLauncher, Updates.SetupLauncher>();
        services.AddSingleton<Updates.InstalledCopy>();
        services.AddSingleton<Updates.EditionInstaller>();

        // The repository's model catalog (0.1.26): checked, added at start from the kept copy, read again daily in the background.
        services.AddSingleton<Catalog.RemoteCatalogReader>();
        services.AddSingleton<Catalog.RemoteCatalogUpdater>();
        services.AddSingleton<Dialogs.IAppUpdateDialog, Dialogs.AppUpdateDialog>();
        services.AddSingleton<Dialogs.IWhatsNewDialog, Dialogs.WhatsNewDialog>();
        services.AddSingleton<Components.ComponentReinstaller>();

        // The storage manager (Settings → Storage): components, leftovers and orphans as rows, who uses them, and a permanent removal.
        services.AddSingleton<Core.DiskSpace.IComponentUsage, Core.DiskSpace.ComponentUsage>();
        services.AddSingleton<Core.DiskSpace.IDiskItemSource, Core.DiskSpace.ComponentDiskSource>();
        services.AddSingleton<Core.DiskSpace.IDiskItemSource, DiskSpace.LeftoverDiskSource>();
        services.AddSingleton<Core.DiskSpace.IDiskItemSource, DiskSpace.DocumentDiskSource>();
        services.AddSingleton<Core.DiskSpace.IDiskItemSource, DiskSpace.LibraryOrphanSource>();
        services.AddSingleton<DiskSpace.PermanentDiscard>();
        services.AddSingleton<Core.DiskSpace.IFolderDiscard>(sp => sp.GetRequiredService<DiskSpace.PermanentDiscard>());
        services.AddSingleton<Core.DiskSpace.ILeftoverSweep, Core.DiskSpace.LeftoverSweep>();
        services.AddSingleton<DiskSpace.ILeftoverSweeper, DiskSpace.LeftoverSweeper>();
        services.AddSingleton<DiskSpace.IDiskCleaner, DiskSpace.DiskCleaner>();
        services.AddSingleton<VersionCheck>();
        services.AddSingleton<WhatsNewCheck>();

        services.AddSingleton<Reports.SystemReport>();
        services.AddSingleton<Reports.ReportNames>();
        services.AddSingleton<Reports.ProblemReport>();
        services.AddTransient<LogViewerViewModel>();
        services.AddSingleton<Func<LogViewerViewModel>>(s => () => s.GetRequiredService<LogViewerViewModel>());
        services.AddSingleton<Dialogs.ILogViewer, Dialogs.LogViewer>();
        services.AddTransient<ReportProblemViewModel>();
        services.AddSingleton<Func<ReportProblemViewModel>>(s => () => s.GetRequiredService<ReportProblemViewModel>());
        services.AddSingleton<Dialogs.IReportDialog, Dialogs.ReportDialog>();
        services.AddSingleton<Engines.Playback.FfmpegPlayback>();

        // One player for the pages that show made videos (Concept → video, Produced): one page is on screen at a time and each closes it
        // when it is left. The made-video rows and their files are one service for both; every running video gets its own stage list.
        services.AddSingleton<PlayerViewModel>();
        services.AddSingleton<Videos.VideoRows>();
        services.AddSingleton<Videos.VideoFiles>();
        services.AddTransient<ProductionProgressViewModel>();
        services.AddSingleton<Func<ProductionProgressViewModel>>(s => () => s.GetRequiredService<ProductionProgressViewModel>());
        AddPage<ConceptsViewModel, ConceptsView>(services);
        AddPage<VideoViewModel, VideoView>(services);
        AddPage<ProducedViewModel, ProducedView>(services);
        AddPage<TrashViewModel, TrashView>(services);
        AddPage<ProfileViewModel, ProfileView>(services);
        AddPage<SettingsViewModel, SettingsView>(services);
        // The settings page builds its sections the first time it is shown (less memory at start).
        services.AddSingleton<Func<IEnumerable<SettingsSectionBase>>>(s => () => s.GetServices<SettingsSectionBase>());
        AddSection<GeneralSettingsViewModel, GeneralSettingsView>(services);
        AddSection<KeysSettingsViewModel, KeysSettingsView>(services);
        AddSection<ResearchSettingsViewModel, ResearchSettingsView>(services);
        AddSection<StorageSettingsViewModel, StorageSettingsView>(services);
        AddSection<LocalAiSettingsViewModel, LocalAiSettingsView>(services);
        AddSection<VideoStylesSettingsViewModel, VideoStylesSettingsView>(services);
        AddSection<VoiceSettingsViewModel, VoiceSettingsView>(services);
        AddSection<BackupSettingsViewModel, BackupSettingsView>(services);
        AddSection<AboutSettingsViewModel, AboutSettingsView>(services);
    }

    /// <summary>Registers a page (a tab of the main window) and its view.</summary>
    internal static void AddPage<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TPage, TView>(IServiceCollection services)
        where TPage : PageViewModelBase
        where TView : Avalonia.Controls.Control, new()
    {
        services.AddSingleton<TPage>();
        services.AddSingleton<PageViewModelBase>(s => s.GetRequiredService<TPage>());
        // One view per page for the whole run (0.1.40): a page change shows the kept view again.
        services.AddSingleton<IViewRegistration>(new ViewRegistration<TPage, TView>(cache: true));
    }

    /// <summary>Registers a settings section (a tab of the settings page) and its view.</summary>
    internal static void AddSection<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TSection, TView>(
        IServiceCollection services)
        where TSection : SettingsSectionBase
        where TView : Avalonia.Controls.Control, new()
    {
        services.AddSingleton<TSection>();
        services.AddSingleton<SettingsSectionBase>(s => s.GetRequiredService<TSection>());
        services.AddSingleton<IViewRegistration>(new ViewRegistration<TSection, TView>(cache: true));
    }
}
