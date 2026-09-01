using System;
using System.Threading.Tasks;
using InkTag.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace InkTag.Mcp;

internal class Program
{
    private static async Task Main(string[] args)
    {
        AppLogger.Initialize();

        if (args.Any(a => string.Equals(a, "--read-only", StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(a, "-r", StringComparison.OrdinalIgnoreCase)))
        {
            ComicTools.ReadOnlyOverride = true;
            AppLogger.LogInfo("[MCP] InkTag MCP server started in strict READ-ONLY mode (--read-only).");
        }
        else if (ComicTools.IsReadOnlyMode)
        {
            AppLogger.LogInfo("[MCP] InkTag MCP server started in strict READ-ONLY mode (INKTAG_MCP_READ_ONLY=true).");
        }

        var builder = Host.CreateApplicationBuilder(args);

        // stdout is the JSON-RPC channel for the stdio transport — force all host logging to stderr
        // so diagnostic output can never corrupt the protocol stream.
        builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var app = builder.Build();
        await app.RunAsync();
    }
}
