namespace TMConcept.Core.Models;

/// <summary>
/// What a local model's license lets the user do with what it makes (TM Concept users publish videos, often monetized): used for the badge in
/// the model picker and the license question before the first download.
/// </summary>
public enum LicenseUse
{
    /// <summary>Free for any use, commercial included (Apache-2.0, MIT…): no badge, no question.</summary>
    Commercial = 0,

    /// <summary>Commercial use only under conditions (a revenue limit, use restrictions): a badge and a question before the first download.</summary>
    Limited = 1,

    /// <summary>Not for commercial use at all: a warning badge and a question before the first download.</summary>
    NonCommercial = 2,
}
