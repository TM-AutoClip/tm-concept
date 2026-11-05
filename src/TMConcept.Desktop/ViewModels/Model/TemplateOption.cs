using TMConcept.Core.Templates;

namespace TMConcept.Desktop.ViewModels;

/// <summary>A template in the template picker ("History → Byzantine history").</summary>
public sealed record TemplateOption(ChannelTemplate Template, string Label);
