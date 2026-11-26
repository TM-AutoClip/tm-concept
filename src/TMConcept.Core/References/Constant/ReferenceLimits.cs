namespace TMConcept.Core.References;

/// <summary>
/// The limits and numbers of the reference video measure (S1, 0.1.52; "bu videonun temposunda yap"). The cut numbers are the motion check's
/// (<see cref="Videos.MotionCheckLimits"/>, ReelMimic video-clone <c>motion_check.py</c>, MIT) with the histogram test of its
/// <c>perception/camera.py</c> <c>looks_like_cut</c>; the camera, tension and peak numbers follow its <c>camera.py</c> and <c>tension.py</c>
/// (MIT; THIRD-PARTY-NOTICES.md). The numbers marked "ours" fit them to a measure on the processor with no OpenCV.
/// </summary>
public static class ReferenceLimits
{
    /// <summary>
    /// Only this much of a reference is measured (seconds; ours: 10 minutes, the beat grid's own limit <see cref="Music.MusicLibraryRules.MaxAnalysisSeconds"/>;
    /// a longer one is measured from its start).
    /// </summary>
    public const int MaxSeconds = Music.MusicLibraryRules.MaxAnalysisSeconds;

    /// <summary>A downloaded reference is at most this high (pixels; ours: the measure reads 160 × 96 frames).</summary>
    public const int MaxHeight = 480;

    /// <summary>yt-dlp prefers the largest format at most this high (pixels; ours: the lowest that is still plenty for 160 × 96 frames).</summary>
    public const int PreferredHeight = 360;

    /// <summary>A download larger than this is refused by yt-dlp (ours).</summary>
    public const string MaxFileSize = "400M";

    /// <summary>yt-dlp gives up on a silent connection after this many seconds (ours).</summary>
    public const int SocketTimeoutSeconds = 30;

    /// <summary>The grey frames of the measure are read at this rate (frames a second; ours: enough for cuts and camera moves).</summary>
    public const double FrameFps = 10;

    /// <summary>The colour frames are read at this rate (ours).</summary>
    public const double ColorFps = 1;

    /// <summary>Width of a colour frame (ours).</summary>
    public const int ColorWidth = 64;

    /// <summary>Height of a colour frame (ours).</summary>
    public const int ColorHeight = 36;

    /// <summary>Bins of the grey histogram that tells a cut from a fast pan (ours, 16 like the hue bins of ReelMimic's HSV histogram).</summary>
    public const int HistogramBins = 16;

    /// <summary>A cut: the histograms of the two frames correlate less than this (ReelMimic <c>looks_like_cut</c>).</summary>
    public const double CutCorrelation = 0.6;

    /// <summary>Two cuts closer than this are one (a flash, a two-frame dissolve; ours).</summary>
    public const double CutGapSeconds = 0.25;

    /// <summary>The camera shift is searched up to this many pixels each way on the 160 × 96 frame (ours: 8 px = 0.5 frame widths a second at 10 fps).</summary>
    public const int MaxShift = 8;

    /// <summary>
    /// A frame moved with the camera when its shift is at least this many frame widths a second (ReelMimic <c>tension.py</c>: a slow but
    /// visible drift ≈ 0.05; its dead zone is 0.04).
    /// </summary>
    public const double CameraSpeed = 0.05;

    /// <summary>The shifted frame must explain the change at least this much better than standing still (ReelMimic <c>camera.py</c> PHOTO_GAIN).</summary>
    public const double CameraGain = 0.75;

    /// <summary>A frame that changed less than this (mean, 0–1) did not move at all (ReelMimic BG_SAME).</summary>
    public const double StillChange = 0.0015;

    /// <summary>A shot "has a camera move" when the camera moved on at least this share of its frames (ours).</summary>
    public const double CameraShotShare = 0.3;

    /// <summary>Shots shorter than this are not judged for a camera move (ours).</summary>
    public const double ShortShotSeconds = 0.5;

    /// <summary>The tension curve is smoothed over this much (ReelMimic SMOOTH_S).</summary>
    public const double SmoothSeconds = 0.15;

    /// <summary>The cut density is spread over this much (ReelMimic: a Gaussian of 0.75 s).</summary>
    public const double CutSpreadSeconds = 0.75;

    /// <summary>At most this many peaks (ReelMimic <c>describe_peaks</c> n).</summary>
    public const int Peaks = 3;

    /// <summary>Peaks are at least this far apart (ReelMimic min_gap_s).</summary>
    public const double PeakGapSeconds = 4;

    /// <summary>A peak rises at least this much above its surroundings (ReelMimic prominence).</summary>
    public const double PeakProminence = 0.12;

    /// <summary>Weight of the loudness in the tension (ReelMimic).</summary>
    public const double LoudnessWeight = 0.28;

    /// <summary>Weight of the camera speed (ReelMimic).</summary>
    public const double CameraWeight = 0.14;

    /// <summary>Weight of the frame difference (ReelMimic).</summary>
    public const double ChangeWeight = 0.10;

    /// <summary>Weight of the cut density (ReelMimic).</summary>
    public const double CutWeight = 0.10;

    /// <summary>The loudness range a viewer notices (dB, ReelMimic JND).</summary>
    public const double LoudnessJnd = 6.0;

    /// <summary>The camera speed range a viewer notices (frame widths a second, ReelMimic JND).</summary>
    public const double CameraJnd = 0.08;

    /// <summary>The frame difference range a viewer notices (ReelMimic JND).</summary>
    public const double ChangeJnd = 0.02;

    /// <summary>The cut density range a viewer notices (cuts a second, ReelMimic JND).</summary>
    public const double CutJnd = 0.5;

    /// <summary>Camera motion below this is not seen (frame widths a second, ReelMimic DEADZONE).</summary>
    public const double CameraDeadZone = 0.04;

    /// <summary>Frame difference below this is not seen (ReelMimic DEADZONE).</summary>
    public const double ChangeDeadZone = 0.005;

    /// <summary>A sound quieter than this (RMS, dBFS) counts as silence: a reference without louder sound has no sound (ours).</summary>
    public const double SilenceDb = -60;

    /// <summary>Colours of the palette.</summary>
    public const int PaletteSize = 5;

    /// <summary>At most this many colour samples go into the palette's clustering (ours).</summary>
    public const int PaletteSamples = 24_000;

    /// <summary>Rounds of the palette's clustering (ours).</summary>
    public const int PaletteRounds = 16;

    /// <summary>A camera move that is this many times faster in its first third than in its last eases out (and the other way round in; ours).</summary>
    public const double EaseRatio = 1.6;

    /// <summary>The temporary folder of a measure, under the work folder (deleted afterwards, and at every start).</summary>
    public const string Folder = "reference";

    /// <summary>The file name yt-dlp writes (its extension is the container's).</summary>
    public const string DownloadName = "reference";

    /// <summary>A reference shorter than this cannot be measured (seconds; ours).</summary>
    public const double MinSeconds = 2;

    /// <summary>The sample rate of the sound measure (the beat grid's, <see cref="Music.MusicLibraryRules.SampleRate"/>).</summary>
    public const int SampleRate = Music.MusicLibraryRules.SampleRate;

    /// <summary>The video files a reference may be (ffmpeg reads them).</summary>
    public static readonly IReadOnlyList<string> VideoFilePatterns = ["*.mp4", "*.mov", "*.mkv", "*.webm", "*.avi", "*.m4v"];
}
