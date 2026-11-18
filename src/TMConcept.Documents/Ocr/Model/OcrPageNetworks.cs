using TMConcept.Core.Models;

namespace TMConcept.Documents.Ocr;

/// <summary>The networks one page is read with (0.1.27) and, when they are on the graphics card, the card's lease for that page (null: the processor).</summary>
public sealed record OcrPageNetworks(IOcrNetwork Detector, IOcrNetwork Recognizer, IGpuLease? Lease);
