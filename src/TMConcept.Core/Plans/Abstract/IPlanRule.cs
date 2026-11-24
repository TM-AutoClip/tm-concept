namespace TMConcept.Core.Plans;

/// <summary>
/// One rule of the app's scene plan that the code enforces (F2, 0.1.41): the prompt asks for it, the model may miss it, the code finds it.
/// <see cref="PlanReader.Problems(IReadOnlyList{PlanItem}, PlanCheck)"/> applies every rule in order; what they find joins the plan's
/// single repair request (no extra call). Findings are in English, for the log and the repair request.
/// </summary>
public interface IPlanRule
{
    /// <summary>What <paramref name="items"/> (the answer as read, before cleaning) break of this rule (empty = kept).</summary>
    IEnumerable<string> Problems(IReadOnlyList<PlanItem> items, PlanCheck check);
}
