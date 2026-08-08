using System;
using System.Threading.Tasks;
using InkTag.Core.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Server;

namespace InkTag.Mcp;

internal class Program
{
    private static async Task Main(string[] args)
    {
        AppLogger.Initialize();

        var builder = Host.CreateApplicationBuilder(args);

        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithToolsFromAssembly();

        var app = builder.Build();
        await app.RunAsync();
    }
}
