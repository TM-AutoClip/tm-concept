namespace TMConcept.Core.Concepts;

/// <summary>
/// Pairs the answers of a concept repair with the concepts that were sent (the 0.1.30 review: the first repair took the i-th answer as the
/// fix of the i-th concept, so a model that left one concept out or changed the order put one concept's story in place of another, and a topic
/// run credited a topic with another topic's concept). An answer belongs to the concept with the same title (case, spacing and punctuation
/// ignored; the repair prompt asks for the titles unchanged). Only when the answer has exactly as many concepts as were sent, a concept
/// without a title match takes the answer at its own position, and only when that answer's title is none of the sent concepts' titles (it
/// would belong to another one). Every answer is used once at most; a concept without a pair keeps its original.
/// </summary>
public static class ConceptRepairMatch
{
    /// <summary>
    /// For each of <paramref name="sent"/> (in order) its answer from <paramref name="answers"/>, or null when it has none.
    /// </summary>
    public static IReadOnlyList<ParsedConcept?> Match(IReadOnlyList<ParsedConcept> sent, IReadOnlyList<ParsedConcept> answers)
    {
        ArgumentNullException.ThrowIfNull(sent);
        ArgumentNullException.ThrowIfNull(answers);
        var pairs = new ParsedConcept?[sent.Count];
        var used = new bool[answers.Count];
        var sentKeys = sent.Select(c => Key(c.Title)).ToList();
        var answerKeys = answers.Select(c => Key(c.Title)).ToList();
        for (var i = 0; i < sent.Count; i++)
        {
            var match = Enumerable.Range(0, answers.Count).FirstOrDefault(j => !used[j] && answerKeys[j].Length > 0 && answerKeys[j] == sentKeys[i], -1);
            if (match >= 0)
            {
                used[match] = true;
                pairs[i] = answers[match];
            }
        }

        if (answers.Count == sent.Count)
        {
            for (var i = 0; i < sent.Count; i++)
            {
                if (pairs[i] is null && !used[i] && !sentKeys.Contains(answerKeys[i]))
                {
                    used[i] = true;
                    pairs[i] = answers[i];
                }
            }
        }

        return pairs;
    }

    /// <summary>A title reduced to its lower-case letters and digits.</summary>
    private static string Key(string title) =>
        new([.. title.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant)]);
}
