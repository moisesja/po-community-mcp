using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Po.Community.Core.Extensions;

namespace Po.Community.Core;

public static class McpClientListToolsService
{
    public static ValueTask<ListToolsResult> Handler(
        RequestContext<ListToolsRequestParams> context,
        CancellationToken cancellation
    )
    {
        ArgumentNullException.ThrowIfNull(context.Services);

        var loggerFactory = context.Services.GetRequiredService<ILoggerFactory>();
        var logger = loggerFactory.CreateLogger("Po.Community.Mcp");
        var poMcpTools = context.Services.GetRequiredService<IEnumerable<IMcpTool>>();
        var responseTools = poMcpTools
            .Select(x => new Tool
            {
                Name = x.Name,
                Description = x.Description,
                InputSchema = x.ToInputSchema(),
            })
            .ToList();

        logger.LogDebug("Listed {ToolCount} MCP tools.", responseTools.Count);

        return ValueTask.FromResult(new ListToolsResult { Tools = responseTools });
    }
}
