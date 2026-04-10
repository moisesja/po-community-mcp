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

public abstract class FhirSearchToolBase<TResource>(ILogger logger) : IMcpTool
    where TResource : DomainResource, new()
{
    private const string PatientIdParameter = "patientId";

    protected abstract string LoincCode { get; }

    protected abstract string ResourceLabel { get; }

    protected abstract CallToolResult ExtractResult(TResource resource);

    public abstract string Name { get; }

    public abstract string? Description { get; }

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
            "Getting {ResourceLabel} for patient. PatientIdFromContext={PatientIdFromContext}",
            ResourceLabel,
            patientIdFromContext
        );

        var fhirClient = httpContext.CreateFhirClientWithContext();

        var (errorResponse, resource) = await SearchResourceAsync(fhirClient, patientId);
        if (errorResponse is not null)
        {
            logger.LogWarning("{ResourceLabel} lookup returned an error response", ResourceLabel);
            return errorResponse;
        }

        if (resource is null)
        {
            logger.LogWarning("{ResourceLabel} lookup returned no resource", ResourceLabel);
            return McpToolUtilities.CreateTextToolResponse(
                $"No {ResourceLabel} could be found for the patient.",
                isError: true
            );
        }

        return ExtractResult(resource);
    }

    protected virtual IEnumerable<(string Name, string Query)> GetPatientSearchVariants(
        string patientId
    )
    {
        yield return ("patient", $"patient={patientId}");
        yield return ("subject", $"subject=Patient/{patientId}");
        yield return ("subject:Patient", $"subject:Patient={patientId}");
    }

    private async Task<(CallToolResult? errorResponse, TResource? resource)> SearchResourceAsync(
        FhirClient fhirClient,
        string? patientId
    )
    {
        if (string.IsNullOrWhiteSpace(patientId))
        {
            return (
                McpToolUtilities.CreateTextToolResponse(
                    $"A patient id is required to look up {ResourceLabel}.",
                    isError: true
                ),
                null
            );
        }

        logger.LogDebug(
            "Searching for {ResourceLabel}. HasPatientId={HasPatientId}",
            ResourceLabel,
            true
        );

        var hadUnsupportedParameter = false;

        foreach (var searchVariant in GetPatientSearchVariants(patientId))
        {
            var parameters = new List<string>
            {
                searchVariant.Query,
                $"code={LoincCode}",
                "_sort=-date",
                "_count=1",
            };

            try
            {
                logger.LogDebug(
                    "Searching for {ResourceLabel} using parameter {SearchParameterName}",
                    ResourceLabel,
                    searchVariant.Name
                );

                var response = await fhirClient.SearchAsync<TResource>([.. parameters]);

                var resource = response
                    ?.Entry.Where(e =>
                        e.Search?.Mode is null or Bundle.SearchEntryMode.Match
                    )
                    .Select(e => e.Resource)
                    .OfType<TResource>()
                    .FirstOrDefault();

                if (resource is null)
                {
                    logger.LogDebug(
                        "{ResourceLabel} search with parameter {SearchParameterName} returned no results",
                        ResourceLabel,
                        searchVariant.Name
                    );
                    continue;
                }

                logger.LogDebug(
                    "{ResourceLabel} search with parameter {SearchParameterName} returned a result",
                    ResourceLabel,
                    searchVariant.Name
                );
                return (null, resource);
            }
            catch (FhirOperationException exception)
                when (IsUnsupportedSearchParameter(exception, searchVariant.Name))
            {
                hadUnsupportedParameter = true;
                logger.LogWarning(
                    exception,
                    "FHIR server does not support {ResourceType} search parameter {SearchParameterName}; trying fallback",
                    typeof(TResource).Name,
                    searchVariant.Name
                );
            }
        }

        if (hadUnsupportedParameter)
        {
            return (
                McpToolUtilities.CreateTextToolResponse(
                    $"The connected FHIR server does not support the {typeof(TResource).Name} patient search parameters this tool tried.",
                    isError: true
                ),
                null
            );
        }

        logger.LogDebug("{ResourceLabel} search returned no results", ResourceLabel);
        return (null, null);
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
