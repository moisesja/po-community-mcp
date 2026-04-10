using Hl7.Fhir.Model;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using Po.Community.Core.Utilities;

namespace Po.Community.Core.McpTools;

public class HistologyTool(ILogger<HistologyTool> logger)
    : FhirSearchToolBase<DiagnosticReport>(logger)
{
    private const string SnomedSystem = "http://snomed.info/sct";
    private const string AdenocarcinomaCode = "35917007";

    protected override string LoincCode => "11529-5";

    protected override string ResourceLabel => "histology diagnostic report";

    public override string Name { get; } = "GetHistology";

    public override string? Description { get; } =
        "Determines if a patient has adenocarcinoma based on their surgical pathology diagnostic report. Returns 1 if adenocarcinoma is confirmed, 0 otherwise.";

    protected override CallToolResult ExtractResult(DiagnosticReport report)
    {
        var isAdenocarcinoma = IsAdenocarcinoma(report);

        logger.LogInformation(
            "Histology lookup completed. Adenocarcinoma={IsAdenocarcinoma}",
            isAdenocarcinoma
        );

        return McpToolUtilities.CreateTextToolResponse(isAdenocarcinoma ? "1" : "0");
    }

    private static bool IsAdenocarcinoma(DiagnosticReport report)
    {
        if (report.ConclusionCode is { Count: > 0 })
        {
            foreach (var codeableConcept in report.ConclusionCode)
            {
                if (codeableConcept.Coding is null)
                {
                    continue;
                }

                foreach (var coding in codeableConcept.Coding)
                {
                    if (
                        string.Equals(
                            coding.System,
                            SnomedSystem,
                            StringComparison.OrdinalIgnoreCase
                        )
                        && string.Equals(
                            coding.Code,
                            AdenocarcinomaCode,
                            StringComparison.Ordinal
                        )
                    )
                    {
                        return true;
                    }
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(report.Conclusion))
        {
            return report.Conclusion.Contains(
                "adenocarcinoma",
                StringComparison.OrdinalIgnoreCase
            );
        }

        return false;
    }
}
