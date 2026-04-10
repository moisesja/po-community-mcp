using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Po.Community.Core.Utilities;

namespace Po.Community.Core.McpTools;

public class TumorStageTool(ILogger<TumorStageTool> logger)
    : FhirSearchToolBase<Observation>(logger)
{
    protected override string LoincCode => "21908-9";

    protected override string ResourceLabel => "tumor stage observation";

    public override string Name { get; } = "GetTumorStage";

    public override string? Description { get; } =
        "Finds a patient's tumor stage given their patient id or patient context.";

    protected override CallToolResult ExtractResult(Observation observation)
    {
        var stageText =
            (observation.Value as CodeableConcept)?.Text
            ?? observation.Code?.Text
            ?? "unknown";

        logger.LogInformation(
            "Tumor stage lookup succeeded with stage {StageText}",
            stageText
        );

        return McpToolUtilities.CreateTextToolResponse(
            $"The patient's tumor stage is: {stageText}"
        );
    }
}
