namespace TMConcept.Speech.Chatterbox;

/// <summary>
/// Picks the next speech token the way upstream's T3.inference does with the Hugging Face logits processors, in the same order: repetition
/// penalty over every token so far (a positive score divided, a negative one multiplied), temperature, min-p (tokens below min_p × the top
/// probability dropped), top-p, then a draw from the softmax. The draw uses a fixed seed (<see cref="ChatterboxModel.Seed"/>), so a text
/// gets the same speech tokens every time.
/// </summary>
internal sealed class ChatterboxSampler(float repetitionPenalty, float temperature, float minP, float topP, int seed)
{
    private readonly Random _random = new(seed);
    private readonly HashSet<long> _seen = [];
    private float[] _scores = [];

    /// <summary>A sampler with upstream's multilingual defaults.</summary>
    public static ChatterboxSampler Default() =>
        new(ChatterboxModel.RepetitionPenalty, ChatterboxModel.Temperature, ChatterboxModel.MinP, ChatterboxModel.TopP, ChatterboxModel.Seed);

    /// <summary>Remembers a token for the repetition penalty (the start token and every token picked).</summary>
    public void Saw(long token) => _seen.Add(token);

    /// <summary>The next token for the logits of the last position.</summary>
    public long Next(ReadOnlySpan<float> logits)
    {
        if (_scores.Length != logits.Length)
        {
            _scores = new float[logits.Length];
        }

        logits.CopyTo(_scores);
        var scores = _scores;
        foreach (var token in _seen)
        {
            if (token >= 0 && token < scores.Length)
            {
                var s = scores[token];
                scores[token] = s < 0 ? s * repetitionPenalty : s / repetitionPenalty;
            }
        }

        if (temperature != 1f)
        {
            for (var i = 0; i < scores.Length; i++)
            {
                scores[i] /= temperature;
            }
        }

        var probabilities = Softmax(scores);
        if (minP > 0)
        {
            var top = probabilities.Max();
            var limit = minP * top;
            var best = Array.IndexOf(probabilities, top);
            for (var i = 0; i < scores.Length; i++)
            {
                if (probabilities[i] < limit && i != best)
                {
                    scores[i] = float.NegativeInfinity;
                }
            }

            probabilities = Softmax(scores);
        }

        if (topP < 1f)
        {
            // Drop the least likely tokens whose summed probability stays within 1 - top_p (the most likely one always stays).
            var order = Enumerable.Range(0, scores.Length).OrderBy(i => probabilities[i]).ToArray();
            var sum = 0.0;
            for (var k = 0; k < order.Length - 1; k++)
            {
                sum += probabilities[order[k]];
                if (sum > 1 - topP)
                {
                    break;
                }

                scores[order[k]] = float.NegativeInfinity;
            }

            probabilities = Softmax(scores);
        }

        var draw = _random.NextDouble();
        var cumulative = 0.0;
        var picked = probabilities.Length - 1;
        for (var i = 0; i < probabilities.Length; i++)
        {
            cumulative += probabilities[i];
            if (draw < cumulative)
            {
                picked = i;
                break;
            }
        }

        _seen.Add(picked);
        return picked;
    }

    private static double[] Softmax(float[] scores)
    {
        var max = float.NegativeInfinity;
        foreach (var s in scores)
        {
            max = Math.Max(max, s);
        }

        var result = new double[scores.Length];
        var total = 0.0;
        for (var i = 0; i < scores.Length; i++)
        {
            result[i] = float.IsNegativeInfinity(scores[i]) ? 0 : Math.Exp(scores[i] - max);
            total += result[i];
        }

        for (var i = 0; i < result.Length; i++)
        {
            result[i] /= total;
        }

        return result;
    }
}
