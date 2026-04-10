using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Po.Community.Core.Extensions;
using Po.Community.Core.Models;
using Po.Community.Core.Utilities;

namespace Po.Community.Core.McpTools;

public class TumorStageTool(ILogger<TumorStageTool> logger) : IMcpTool
{
    private const string PatientIdParameter = "patientId";
    private const string TumorStageCode = "21908-9";

    public string Name { get; } = "GetTumorStage";

    public string? Description { get; } = "Finds a patient's tumor stage given their patient id or patient context.";

    public List<McpToolArgument> Arguments { get; } =
    [
        new McpToolArgument
        {
            Type = "string",
            Name = PatientIdParameter,
            Description =
                "The id of the patient. This is optional if patient context already exists",
            IsRequired = false,
        },
    ];

    public async Task<CallToolResult> HandleAsync(
        HttpContext httpContext,
        McpServer mcpServer,
        IServiceProvider serviceProvider,
        CallToolRequestParams context
    )
    {
        var patientId = httpContext.GetPatientIdIfContextExists();
        var patientIdFromContext = !string.IsNullOrWhiteSpace(patientId);
        if (string.IsNullOrWhiteSpace(patientId))
        {
            patientId = context.GetRequiredArgumentValue(PatientIdParameter);
        }

        logger.LogDebug(
            "Getting tumor stage for patient. PatientIdFromContext={PatientIdFromContext}",
            patientIdFromContext
        );

        var fhirClient = httpContext.CreateFhirClientWithContext();

        var (errorResponse, observation) = await GetTumorStageAsync(fhirClient, patientId);
        if (errorResponse is not null)
        {
            logger.LogWarning("Tumor stage lookup returned an error response");
            return errorResponse;
        }

        if (observation is null)
        {
            logger.LogWarning("Tumor stage lookup returned no observation");
            return McpToolUtilities.CreateTextToolResponse(
                "No tumor stage observation could be found for the patient.",
                isError: true
            );
        }



        var stageText = (observation.Value as CodeableConcept)?.Text
            ?? observation.Code?.Text
            ?? "unknown";

        logger.LogInformation("Tumor stage lookup succeeded with stage {StageText}", stageText);

        return McpToolUtilities.CreateTextToolResponse(
            $"The patient's tumor stage is: {stageText}"
        );
    }

    private async Task<(CallToolResult? errorResponse, Observation? observation)> GetTumorStageAsync(
        FhirClient fhirClient, string? patientId)
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return (
                McpToolUtilities.CreateTextToolResponse(
                    "A patient id is required to look up tumor stage.",
                    isError: true
                ),
                null
            );
        }

        logger.LogDebug(
            "Searching for tumor stage observation. HasPatientId={HasPatientId}",
            true
        );

        var hadUnsupportedParameter = false;

        foreach (var searchVariant in GetPatientSearchVariants(patientId))
        {
            var parameters = new List<string> { searchVariant.Query, $"code={TumorStageCode}", "_sort=-date", "_count=1" };

            try
            {
                logger.LogDebug(
                    "Searching for tumor stage observation using parameter {SearchParameterName}",
                    searchVariant.Name
                );

                var response = await fhirClient.SearchAsync<Observation>([.. parameters]);

                var observation = response?.Entry
                    .Where(e => e.Search?.Mode is null or Bundle.SearchEntryMode.Match)
                    .Select(e => e.Resource)
                    .OfType<Observation>()
                    .FirstOrDefault();

                if (observation is null)
                {
                    logger.LogDebug(
                        "Tumor stage search with parameter {SearchParameterName} returned no observations",
                        searchVariant.Name
                    );
                    continue;
                }

                logger.LogDebug(
                    "Tumor stage search with parameter {SearchParameterName} returned an observation",
                    searchVariant.Name
                );
                return (null, observation);
            }
            catch (FhirOperationException exception)
                when (IsUnsupportedSearchParameter(exception, searchVariant.Name))
            {
                hadUnsupportedParameter = true;
                logger.LogWarning(
                    exception,
                    "FHIR server does not support Observation search parameter {SearchParameterName}; trying fallback",
                    searchVariant.Name
                );
            }
        }

        if (hadUnsupportedParameter)
        {
            return (
                McpToolUtilities.CreateTextToolResponse(
                    "The connected FHIR server does not support the Observation patient search parameters this tool tried.",
                    isError: true
                ),
                null
            );
        }

        logger.LogDebug("Tumor stage search returned no observations");
        return (null, null);
    }

    private static IEnumerable<(string Name, string Query)> GetPatientSearchVariants(string patientId)
    {
        yield return ("patient", $"patient={patientId}");
        yield return ("subject", $"subject=Patient/{patientId}");
        yield return ("subject:Patient", $"subject:Patient={patientId}");
    }

    private static bool IsUnsupportedSearchParameter(
        FhirOperationException exception,
        string parameterName
    )
    {
        return exception.Message.Contains(parameterName, StringComparison.OrdinalIgnoreCase)
            && exception.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase);
    }
}
