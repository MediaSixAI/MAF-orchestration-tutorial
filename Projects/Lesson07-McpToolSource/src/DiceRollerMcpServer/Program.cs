// Lesson 7 - MCP as a Tool Source
// A minimal Model Context Protocol server exposing two simple tools - RollDice and FlipCoin -
// over standard input/output. This process is launched as a subprocess by the companion
// McpToolSource client project; a human does not run it directly.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiceRollerMcpServer
{
    /// <summary>
    /// Entry point that hosts an MCP server over standard input/output, exposing every tool
    /// method found in <see cref="DiceTools"/>.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds and runs the MCP server host. Logging is redirected to standard error, since
        /// standard output is reserved exclusively for the MCP JSON-RPC protocol stream - any
        /// stray write to standard output would corrupt that stream from the client's point of
        /// view.
        /// </summary>
        private static async Task Main(string[] args)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            await builder.Build().RunAsync();
        }
    }
}
