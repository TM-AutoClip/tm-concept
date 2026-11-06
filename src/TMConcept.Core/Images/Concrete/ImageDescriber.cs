using TMConcept.Core.Chat;
using TMConcept.Core.Costs;
using TMConcept.Core.Models;
using TMConcept.Core.Pipeline;
using TMConcept.Core.Profiles;

namespace TMConcept.Core.Images;

/// <summary>"Describe with a model": sends one sample image to the vision model of the profile's describe step and returns a one-line caption (paid).</summary>
public sealed class ImageDescriber(StepModelResolver resolver, IChatClientFactory chats, ICostCalculator costs)
{
    /// <summary>A caption and its cost.</summary>
    public async Task<(string Caption, decimal CostUsd)> DescribeAsync(byte[] image, string mimeType, string language, Guid? profileId,
        CancellationToken cancellationToken)
    {
        var model = await resolver.ResolveForAsync(PipelineSteps.DescribeImage, profileId, cancellationToken).ConfigureAwait(false);
        var client = chats.Create(model.Model, model.Key.Secret);
        var system = language == ContentLanguages.Turkish
            ? "Görseli tek cümleyle, en çok 150 karakterle anlat: ne görünüyor (yer, nesne, dönem, bakış açısı). Yalnız cümleyi yaz."
            : "Describe the image in one sentence of at most 150 characters: what is shown (place, object, period, viewpoint). Write only the sentence.";
        var answer = await client.SendAsync(new ChatRequest(system, language == ContentLanguages.Turkish ? "Görseli anlat." : "Describe the image.",
            MaxOutputTokens: 400, Effort: model.Effort, Images: [new ImagePart(image, mimeType)]), cancellationToken).ConfigureAwait(false);
        return (Text.TextClean.OneLine(answer.Text, ProfileLimits.CaptionMax), costs.Exact(model.Model, answer.Usage, answer.ReportedCostUsd));
    }
}
