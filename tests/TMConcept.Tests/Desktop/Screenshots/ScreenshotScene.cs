using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using TMConcept.Core.Activity;
using TMConcept.Core.Chat;
using TMConcept.Core.Concepts;
using TMConcept.Core.Keys;
using TMConcept.Core.Paths;
using TMConcept.Core.Profiles;
using TMConcept.Core.Runs;
using TMConcept.Core.Videos;
using TMConcept.Desktop.Composition;
using TMConcept.Desktop.Localization;
using TMConcept.Desktop.Logging;
using TMConcept.Desktop.Startup;
using TMConcept.Desktop.ViewModels;
using TMConcept.Desktop.Views;
using TMConcept.Tests.Providers;

namespace TMConcept.Tests.Desktop.Screenshots;

/// <summary>
/// A filled app for screenshots: the real composition root on a temporary data folder with a fake chat (nothing reaches a provider), a
/// Turkish channel, a fake key, new, approved and trashed concepts, a made video with a thumbnail written here (no ffmpeg), a few activity
/// lines and this month's spending. <see cref="Open"/> opens the main window at a size with the app's language texts and page views on the
/// window itself (not on <c>Application.Current</c>, which every test shares).
/// </summary>
internal sealed class ScreenshotScene : IDisposable
{
    private ScreenshotScene(DesktopHarness harness, string language)
    {
        Harness = harness;
        Language = language;
    }

    public DesktopHarness Harness { get; }

    /// <summary>The interface language of the shots (<c>tr</c> or <c>en</c>).</summary>
    public string Language { get; }

    public FakeChat Chat { get; } = new();

    public ShellViewModel Shell => Harness.Get<ShellViewModel>();

    public Profile Profile { get; private set; } = new();

    public static async Task<ScreenshotScene> CreateAsync(string language)
    {
        FakeChat? chat = null;
        var harness = new DesktopHarness(s => s.AddSingleton<IChatClientFactory>(_ => chat!));
        var scene = new ScreenshotScene(harness, language);
        chat = scene.Chat;
        harness.Get<Localizer>().Use(language);
        await FirstRunTests.CreateAsync(harness);
        await harness.Get<AppSession>().LoadAsync(CancellationToken.None);
        harness.Get<Localizer>().Use(language);
        await scene.SeedAsync();
        await scene.Shell.StartAsync();
        return scene;
    }

    /// <summary>Opens the main window at <paramref name="width"/>×<paramref name="height"/> and lays it out.</summary>
    public MainWindow Open(int width, int height)
    {
        var window = new MainWindow { DataContext = Shell, Width = width, Height = height };
        Harness.Get<LanguageResources>().Attach(window.Resources.MergedDictionaries);
        window.DataTemplates.Add(Harness.Get<ViewLocator>());
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    public void Dispose() => Harness.Dispose();

    private async Task SeedAsync()
    {
        var turkish = Language == "tr";
        Profile = new Profile
        {
            Name = turkish ? "Bizans" : "Byzantium",
            ContentLanguage = turkish ? ContentLanguages.Turkish : ContentLanguages.English,
            ConceptText = turkish ? "Bizans tarihi: kuşatmalar, saray entrikaları, unutulmuş icatlar." : "Byzantine history.",
        };
        await Harness.Get<IProfileStore>().SaveAsync(Profile, CancellationToken.None);
        await Harness.Get<AppSession>().SelectProfileAsync(Profile, CancellationToken.None);
        await Harness.Get<IApiKeyStore>().SaveAsync(new ApiKey { Provider = "anthropic", Label = "mine", Secret = "fake-test-key-shots-0123456789" },
            CancellationToken.None);

        var titles = turkish
            ? new[]
            {
                "Konstantinopolis'in düşüşünde son gece", "Theodosius surlarının üç katmanı", "Bizans'ta Grek ateşinin sırrı",
                "Justinianus vebası ve boşalan şehir", "Nika ayaklanması: hipodromda beş gün", "Ayasofya'nın kubbesi neden çöktü?",
            }
            : new[]
            {
                "The last night of Constantinople", "The three layers of the Theodosian walls", "The secret of Greek fire",
                "The plague of Justinian and the empty city", "The Nika riots: five days in the Hippodrome", "Why the dome of Hagia Sophia fell",
            };
        var content = turkish
            ? "[ekranda: kuşatılmış şehrin haritası]\n1453 baharında şehir elli üç gündür kuşatma altındaydı.\n[ekranda: surlarda meşaleler]\nSon gece kimse uyumadı."
            : "[on screen: a map of the besieged city]\nIn the spring of 1453 the city had been under siege for fifty-three days.\n[on screen: torches on the walls]\nOn the last night nobody slept.";
        var summary = turkish
            ? "Kuşatmanın son gecesi, surların içinden ve dışından anlatılır."
            : "The last night of the siege, told from inside and outside the walls.";
        var concepts = titles.Select((title, i) => new Concept
        {
            ProfileId = Profile.Id,
            Title = title,
            Summary = summary,
            Content = content,
            Language = Profile.ContentLanguage,
            Status = i < 4 ? ConceptStatus.New : ConceptStatus.Approved,
        }).ToList();
        var produced = new Concept
        {
            ProfileId = Profile.Id, Title = titles[1], Summary = summary, Content = content, Language = Profile.ContentLanguage,
            Status = ConceptStatus.Produced,
        };
        concepts.Add(produced);
        concepts.Add(new Concept
        {
            ProfileId = Profile.Id,
            Title = turkish ? "Bizans'ın en uzun kuşatması" : "The longest siege of Byzantium",
            Summary = summary,
            Content = content,
            Language = Profile.ContentLanguage,
            Status = ConceptStatus.Trashed,
            TrashReason = TrashReason.Topic,
            DecidedAtUtc = DateTimeOffset.UtcNow,
        });
        await Harness.Get<IConceptStore>().AddAsync(concepts, CancellationToken.None);

        await Harness.Get<IRunStore>().AddAsync(new Run
        {
            ProfileId = Profile.Id, Kind = RunKind.Concepts, Provider = "anthropic", Model = "claude-sonnet-5-5", ActualUsd = 0.12m, Accepted = 6,
            Status = RunStatus.Succeeded,
        }, CancellationToken.None);
        await AddVideoAsync(produced);

        var log = Harness.Get<ActivityLog>();
        log.Add(ActivityLevel.Info, turkish ? "Konu önerisi bitti: 20 konu · 0,004 $" : "Topic suggestions done: 20 topics · 0.004 $");
        log.Add(ActivityLevel.Warning, turkish ? "Seslendirme modeli seçilmedi; video sessiz üretilecek." : "No voice chosen; the video will be silent.");
        log.Add(ActivityLevel.Info, turkish ? "Konseptler hazır: 6 · kesin 0,12 $" : "Concepts ready: 6 · exact 0.12 $");
    }

    private async Task AddVideoAsync(Concept concept)
    {
        var paths = Harness.Get<IAppPaths>();
        var folder = Path.Combine(paths.LibraryDirectory, Profile.Name, "2026-11-11-1-walls");
        Directory.CreateDirectory(folder);
        await File.WriteAllBytesAsync(Path.Combine(folder, "video.mp4"), new byte[16]);
        Thumbnail(Path.Combine(folder, "thumbnail.png"));
        var video = new VideoRecord
        {
            ConceptId = concept.Id,
            ProfileId = Profile.Id,
            Title = concept.Title,
            Folder = LibraryPaths.Relative(paths.LibraryDirectory, folder),
            FileName = "video.mp4",
            ThumbnailName = "thumbnail.png",
            DurationMs = 92_000,
            Width = 1920,
            Height = 1080,
            Fps = 30,
            Engine = "HyperFrames",
            TotalUsd = 0.38m,
            CostExact = true,
        };
        await Harness.Get<IVideoStore>().AddAsync(video, [], CancellationToken.None);
    }

    /// <summary>A 320×180 poster: a navy-to-gold gradient, enough to see where the thumbnail sits.</summary>
    private static void Thumbnail(string path)
    {
        const int width = 320, height = 180;
        using var bitmap = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            var pixels = new byte[buffer.RowBytes * height];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var t = (double)x / width;
                    var at = (y * buffer.RowBytes) + (x * 4);
                    pixels[at] = (byte)(0x5E - (0x30 * t));
                    pixels[at + 1] = (byte)(0x2A + (0x89 * t));
                    pixels[at + 2] = (byte)(0x12 + (0xD1 * t));
                    pixels[at + 3] = 0xFF;
                }
            }

            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, buffer.Address, pixels.Length);
        }

        bitmap.Save(path, PngBitmapEncoderOptions.Default);
    }
}
