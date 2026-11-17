namespace TMConcept.Desktop.ViewModels;

/// <summary>The recommended models of one graphics card class on Settings → Local AI ("6 GB class · your card").</summary>
/// <param name="Title">The class and whether it is this computer's card.</param>
/// <param name="IsYourCard">Whether it is this computer's card class (shown first and stronger).</param>
/// <param name="Rows">Its models, best first.</param>
public sealed record AppModelGroup(string Title, bool IsYourCard, IReadOnlyList<AppModelRowViewModel> Rows);
