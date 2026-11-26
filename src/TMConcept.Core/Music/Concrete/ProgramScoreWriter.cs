namespace TMConcept.Core.Music;

/// <summary>
/// The app composes the piece itself (0.1.55, "Program composes"; no model, no network, any computer). The prompt's words choose a mood, a style
/// and a tempo (<see cref="ScorePromptReader"/>); the mood gives the key's colour and a four-bar chord progression (<see cref="ScoreMoods"/>), the
/// style the instruments and drums (<see cref="ScoreStyles"/>, <see cref="ScoreDrumPatterns"/>). The seed picks the key and the tune: a two-bar
/// figure per section (<see cref="ScoreRhythms"/>) whose strong notes land on the chord. Form: two bars of held chords and bass, then sections of
/// eight bars (the tune changes between them, the broken chords get busier in every second one), and a last bar on the key's own chord. The
/// piece is at least as long as asked (whole bars). The same brief and seed always give the same score. Pure.
/// </summary>
public sealed class ProgramScoreWriter : IScoreWriter
{
    private const int EighthTicks = ScoreRules.TicksPerQuarter / 2;
    private const int BarTicks = ScoreRules.TicksPerQuarter * ScoreRules.BeatsPerBar;
    private const int FirstLeadStep = 2;

    /// <inheritdoc />
    public MusicScore Write(ScoreBrief brief)
    {
        ArgumentNullException.ThrowIfNull(brief);
        var traits = ScorePromptReader.Read(brief.Prompt, brief.Bpm);
        var shape = ScoreMoods.All[traits.Mood];
        var instruments = ScoreStyles.All[traits.Style];
        var scale = traits.Flavor is { } flavor ? ScoreScales.Flavors[flavor] : shape.Minor ? ScoreScales.Minor : ScoreScales.Major;
        var random = new Random(brief.Seed);
        var key = ScoreRules.Keys[random.Next(ScoreRules.Keys.Count)];
        var seconds = Math.Clamp(brief.Seconds, ScoreRules.MinSeconds, ScoreRules.MaxSeconds);
        var bars = Math.Max(ScoreRules.IntroBars + ScoreRules.MotifBars,
            (int)Math.Ceiling(seconds * traits.Bpm / (ScoreRules.SecondsPerMinute * ScoreRules.BeatsPerBar)));
        var plan = new ScorePlan(scale, key, shape, bars, ScoreRules.SoftVelocity + (int)Math.Round(ScoreRules.VelocityRange * shape.Energy));

        var tracks = new List<ScoreTrack>
        {
            new("Pad", ScoreMix.PadChannel, instruments.Pad, ScoreMix.PadVolume, ScoreRules.Center, Pad(plan)),
            new("Bass", ScoreMix.BassChannel, instruments.Bass, ScoreMix.BassVolume, ScoreRules.Center, Bass(plan)),
            new("Arpeggio", ScoreMix.ArpeggioChannel, instruments.Arpeggio, ScoreMix.ArpeggioVolume, ScoreMix.ArpeggioPan, Arpeggio(plan)),
        };
        if (instruments.Lead >= 0)
        {
            tracks.Add(new("Lead", ScoreMix.LeadChannel, instruments.Lead, ScoreMix.LeadVolume, ScoreMix.LeadPan, Lead(plan, random)));
        }

        var drums = instruments.Drums != ScoreDrums.None ? instruments.Drums : shape.Energy >= ScoreRules.DrumEnergy ? ScoreDrums.Cinematic : ScoreDrums.None;
        if (drums != ScoreDrums.None)
        {
            tracks.Add(new("Drums", ScoreRules.DrumChannel, 0, ScoreMix.DrumVolume, ScoreRules.Center, Drums(plan, drums)));
        }

        return new MusicScore(traits.Bpm, ScoreRules.TicksPerQuarter, ScoreRules.BeatsPerBar, bars, tracks);
    }

    private static List<ScoreNote> Pad(ScorePlan plan)
    {
        var notes = new List<ScoreNote>();
        for (var bar = 0; bar < plan.Bars; bar++)
        {
            var chord = plan.Chord(bar);
            var root = ScoreHarmony.Above(chord[0], ScoreRules.PadLow);
            var third = ScoreHarmony.Above(chord[1], root + 1);
            var fifth = ScoreHarmony.Above(chord[2], third + 1);
            var velocity = Velocity(plan.Velocity - ScoreMix.PadSofter);
            notes.AddRange([new(bar * BarTicks, BarTicks, root, velocity), new(bar * BarTicks, BarTicks, third, velocity), new(bar * BarTicks, BarTicks, fifth, velocity)]);
        }

        return notes;
    }

    private static List<ScoreNote> Bass(ScorePlan plan)
    {
        var notes = new List<ScoreNote>();
        for (var bar = 0; bar < plan.Bars; bar++)
        {
            var chord = plan.Chord(bar);
            var root = ScoreHarmony.Above(chord[0], ScoreRules.BassLow);
            var fifth = ScoreHarmony.Above(chord[2], root + 1);
            var start = bar * BarTicks;
            if (plan.Last(bar) || plan.Energy < ScoreRules.QuietEnergy || plan.Intro(bar))
            {
                notes.Add(new(start, BarTicks, root, Velocity(plan.Velocity + ScoreMix.Accent)));
            }
            else if (plan.Energy < ScoreRules.BusyEnergy)
            {
                notes.Add(new(start, BarTicks / 2, root, Velocity(plan.Velocity + ScoreMix.Accent)));
                notes.Add(new(start + (BarTicks / 2), BarTicks / 2, fifth, Velocity(plan.Velocity)));
            }
            else
            {
                for (var eighth = 0; eighth < ScoreRhythms.EighthsPerBar; eighth++)
                {
                    notes.Add(new(start + (eighth * EighthTicks), EighthTicks, root, Velocity(plan.Velocity + (eighth == 0 ? ScoreMix.Accent : 0))));
                }
            }
        }

        return notes;
    }

    private static List<ScoreNote> Arpeggio(ScorePlan plan)
    {
        var notes = new List<ScoreNote>();
        for (var bar = 0; bar < plan.Bars; bar++)
        {
            if (plan.Intro(bar))
            {
                continue;
            }

            var chord = plan.Chord(bar);
            var root = ScoreHarmony.Above(chord[0], ScoreRules.ArpeggioLow);
            int[] tones = [root, ScoreHarmony.Above(chord[1], root + 1), ScoreHarmony.Above(chord[2], root + 1), root + ScoreRules.Octave];
            var velocity = Velocity(plan.Velocity - ScoreMix.ArpeggioSofter);
            if (plan.Last(bar))
            {
                notes.Add(new(bar * BarTicks, BarTicks, root, velocity));
                continue;
            }

            // Quarter notes in a calm mood, eighths otherwise; every second section one step busier.
            var busy = plan.Energy >= ScoreRules.QuietEnergy || plan.Section(bar) % 2 == 1;
            var length = busy ? EighthTicks : ScoreRules.TicksPerQuarter;
            for (var (tick, i) = (0, 0); tick < BarTicks; tick += length, i++)
            {
                notes.Add(new((bar * BarTicks) + tick, length, tones[i % tones.Length], velocity));
            }
        }

        return notes;
    }

    private static List<ScoreNote> Lead(ScorePlan plan, Random random)
    {
        var notes = new List<ScoreNote>();
        var leadRoot = ScoreHarmony.Above(plan.Key, ScoreRules.LeadLow);
        var figures = new Dictionary<int, ScoreFigure>();
        var step = FirstLeadStep;
        var restShare = ScoreRhythms.RestShare * (1 - plan.Energy);
        for (var bar = 0; bar < plan.Bars; bar++)
        {
            if (plan.Intro(bar) || plan.Last(bar))
            {
                continue;
            }

            // One figure per section kind (A, B), made the first time it is needed: the tune comes back as the sections repeat.
            var kind = plan.Section(bar) % 2;
            if (!figures.TryGetValue(kind, out var figure))
            {
                figures[kind] = figure = MakeFigure(random, restShare);
            }

            var inSection = (bar - ScoreRules.IntroBars) % ScoreRules.SectionBars;
            var closing = inSection % ScoreRhythms.PhraseBars == ScoreRhythms.PhraseBars - 1;
            var rhythm = closing ? ScoreRhythms.PhraseEnd : figure.Rhythms[inSection % ScoreRules.MotifBars];
            var moves = figure.Steps[inSection % ScoreRules.MotifBars];
            var degree = plan.Degree(bar);
            var tick = bar * BarTicks;
            for (var i = 0; i < rhythm.Count; i++)
            {
                var length = rhythm[i] * EighthTicks;
                step = i == 0 || closing ? Nearest(step, degree) : step + moves[i % moves.Count];
                step = Fit(plan.Scale, leadRoot, step);
                if (i == 0 || closing || !figure.Rests[i % figure.Rests.Count])
                {
                    notes.Add(new(tick, length, ScoreHarmony.Step(plan.Scale, leadRoot, step), Velocity(plan.Velocity - ScoreMix.LeadSofter + (i == 0 ? ScoreMix.Accent : 0))));
                }

                tick += length;
            }
        }

        return notes;
    }

    private static List<ScoreNote> Drums(ScorePlan plan, ScoreDrums drums)
    {
        var notes = new List<ScoreNote>();
        var pattern = ScoreDrumPatterns.All[drums].Where(h => plan.Energy >= h.MinEnergy).ToList();
        for (var bar = 0; bar < plan.Bars; bar++)
        {
            if (plan.Intro(bar))
            {
                continue;
            }

            var start = bar * BarTicks;
            var sectionStart = (bar - ScoreRules.IntroBars) % ScoreRules.SectionBars == 0;
            if (sectionStart || plan.Last(bar))
            {
                notes.Add(new(start, ScoreRules.TicksPerQuarter, ScoreDrumNotes.Crash, Velocity(plan.Velocity)));
            }

            if (plan.Last(bar))
            {
                notes.Add(new(start, ScoreRules.TicksPerQuarter, ScoreDrumNotes.Kick, Velocity(plan.Velocity + ScoreMix.Accent)));
                continue;
            }

            foreach (var hit in pattern)
            {
                foreach (var eighth in hit.Eighths)
                {
                    notes.Add(new(start + (eighth * EighthTicks), EighthTicks, hit.Note, Velocity(plan.Velocity - hit.Softer + (eighth == 0 ? ScoreMix.Accent : 0))));
                }
            }
        }

        return notes;
    }

    /// <summary>The scale step nearest <paramref name="step"/> that is a note of the chord on <paramref name="degree"/> (downwards on a tie).</summary>
    private static int Nearest(int step, int degree)
    {
        for (var distance = 0; ; distance++)
        {
            if (ScoreHarmony.InChord(step - distance, degree))
            {
                return step - distance;
            }

            if (ScoreHarmony.InChord(step + distance, degree))
            {
                return step + distance;
            }
        }
    }

    /// <summary>The step moved into the tune's range by octaves.</summary>
    private static int Fit(IReadOnlyList<int> scale, int root, int step)
    {
        while (ScoreHarmony.Step(scale, root, step) > ScoreRules.LeadHigh)
        {
            step -= ScoreRules.ScaleLength;
        }

        while (ScoreHarmony.Step(scale, root, step) < ScoreRules.LeadLow)
        {
            step += ScoreRules.ScaleLength;
        }

        return step;
    }

    private static int Velocity(int velocity) => Math.Clamp(velocity, ScoreRules.MinVelocity, ScoreRules.MaxData);

    /// <summary>A new tune figure: each bar's rhythm, the steps between its notes and which notes rest.</summary>
    private static ScoreFigure MakeFigure(Random random, double restShare)
    {
        var rhythms = Enumerable.Range(0, ScoreRules.MotifBars).Select(_ => ScoreRhythms.Cells[random.Next(ScoreRhythms.Cells.Count)]).ToList();
        var steps = rhythms.Select(r => (IReadOnlyList<int>)r.Select(_ => ScoreRhythms.Steps[random.Next(ScoreRhythms.Steps.Count)]).ToList()).ToList();
        var rests = Enumerable.Range(0, ScoreRhythms.EighthsPerBar).Select(_ => random.NextDouble() < restShare).ToList();
        return new ScoreFigure(rhythms, steps, rests);
    }
}
