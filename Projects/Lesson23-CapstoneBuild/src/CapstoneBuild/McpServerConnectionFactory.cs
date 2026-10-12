using ModelContextProtocol.Client;

namespace CapstoneBuild
{
    /// <summary>
    /// Builds the MCP client connection to the companion ResearchMcpServer process,
    /// launched as a subprocess and spoken to over standard input/output. Same pattern as Lesson 7's
    /// McpServerConnectionFactory, extended with an optional explicit path override sourced from
    /// PipelineConfig.
    /// </summary>
    internal static class McpServerConnectionFactory
    {
        /// <summary>
        /// Launches the research MCP server as a subprocess over stdio and returns a connected
        /// client. If overridePath is null, the companion server's built DLL is located via the
        /// same sibling-project, AppContext.BaseDirectory-anchored resolution Lesson 7 established -
        /// never Directory.GetCurrentDirectory(), which resolves differently depending on launch
        /// method. Throws a clear error if the companion server project has not been built yet.
        /// </summary>
        internal static async Task<McpClient> ConnectAsync(string? overridePath)
        {
            string serverDllPath = overridePath ?? ComputeDefaultServerDllPath();

            if (!File.Exists(serverDllPath))
            {
                throw new InvalidOperationException(
                    "The companion MCP server has not been built yet. Run 'dotnet build' inside " +
                    $"ResearchMcpServer before running this lesson. Expected to find: {serverDllPath}");
            }

            return await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Name = "ResearchMcpServer",
                Command = "dotnet",
                Arguments = [serverDllPath],
            }));
        }

        /// <summary>
        /// Computes the companion server's expected build output path relative to this running
        /// assembly's own folder.
        /// </summary>
        private static string ComputeDefaultServerDllPath()
        {
            string serverProjectDirectory = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ResearchMcpServer"));

            return Path.Combine(
                serverProjectDirectory, "bin", "Debug", "net10.0", "ResearchMcpServer.dll");
        }
    }
}
