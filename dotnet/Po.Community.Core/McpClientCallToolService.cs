using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Po.Community.Core.Utilities;

namespace Po.Community.Core;

public static class McpClientCallToolService
{
    public static async ValueTask<CallToolResult> Handler(
        RequestContext<CallToolRequestParams> context,
        CancellationToken cancellationToken
    )
    {
        if (context.Services is null)
        {
            return McpToolUtilities.CreateTextToolResponse(
                "An unexpected server error occurred. Services were not found.",
                isError: true
            );
        }

        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("Po.Community.Mcp");

        if (context.Params is null)
        {
            logger.LogError("An MCP tool request was received without parameters.");
            return McpToolUtilities.CreateTextToolResponse(
                "An unexpected server error occurred. No tool parameters found.",
                isError: true
            );
        }

        var poMcpTools = context.Services.GetRequiredService<IEnumerable<IMcpTool>>();
        var contextAccessor = context.Services.GetRequiredService<IHttpContextAccessor>();

        if (contextAccessor.HttpContext is null)
        {
            logger.LogWarning(
                "MCP tool {ToolName} could not execute because the HTTP context was unavailable.",
                context.Params.Name
            );
            return McpToolUtilities.CreateTextToolResponse(
                "An unexpected server error occurred. The HTTP context could not be determined.",
                isError: true
            );
        }

        foreach (var poMcpTool in poMcpTools)
        {
            if (poMcpTool.Name != context.Params.Name)
            {
                continue;
            }

            logger.LogDebug("Executing MCP tool {ToolName}.", poMcpTool.Name);
            var stopwatch = Stopwatch.StartNew();

            try
            {
                var result = await poMcpTool.HandleAsync(
                    contextAccessor.HttpContext,
                    context.Server,
                    context.Services,
                    context.Params
                );

                stopwatch.Stop();

                if (result.IsError is true)
                {
                    logger.LogInformation(
                        "MCP tool {ToolName} completed with an error response in {ElapsedMilliseconds}ms.",
                        poMcpTool.Name,
                        stopwatch.ElapsedMilliseconds
                    );
                }
                else
                {
                    logger.LogInformation(
                        "MCP tool {ToolName} completed successfully in {ElapsedMilliseconds}ms.",
                        poMcpTool.Name,
                        stopwatch.ElapsedMilliseconds
                    );
                }

                return result;
            }
            catch (Exception exception)
            {
                stopwatch.Stop();
                logger.LogError(
                    exception,
                    "MCP tool {ToolName} failed after {ElapsedMilliseconds}ms.",
                    poMcpTool.Name,
                    stopwatch.ElapsedMilliseconds
                );

                return McpToolUtilities.CreateTextToolResponse(
                    $"An internal exception occurred with message: {exception.Message}",
                    isError: true
                );
            }
        }

        logger.LogWarning(
            "MCP tool {ToolName} was requested but no handler was registered.",
            context.Params.Name
        );
        return McpToolUtilities.CreateTextToolResponse(
            $"A tool handler was not found for the tool: {context.Params.Name}.",
            isError: true
        );
    }
}
