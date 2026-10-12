using ModelContextProtocol.Client;

namespace McpToolSource
{
    /// <summary>
    /// Builds the MCP client connection to the companion DiceRollerMcpServer process,
    /// launched as a subprocess and spoken to over standard input/output.
    /// </summary>
    internal static class McpServerConnectionFactory
    {
        /// <summary>
        /// Locates the built DiceRollerMcpServer.dll in the sibling project folder,
        /// launches it as a subprocess over stdio, and returns a connected MCP client. Throws a
        /// clear error if the companion server project has not been built yet.
        /// </summary>
        internal static async Task<McpClient> ConnectAsync()
        {
            // AppContext.BaseDirectory is the folder containing this running assembly (always
            // bin/<Config>/net10.0, regardless of how the app was launched), unlike
            // Directory.GetCurrentDirectory() - which is the project directory itself under
            // `dotnet run`, but the bin output folder under other launch methods. Anchoring to
            // BaseDirectory instead makes this path resolution work the same way no matter how
            // the lesson is started.
            string serverProjectDirectory = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "DiceRollerMcpServer"));
            string serverDllPath = Path.Combine(
                serverProjectDirectory, "bin", "Debug", "net10.0", "DiceRollerMcpServer.dll");

            if (!File.Exists(serverDllPath))
            {
                throw new InvalidOperationException(
                    "The companion MCP server has not been built yet. Run 'dotnet build' inside " +
                    $"DiceRollerMcpServer before running this lesson. Expected to find: {serverDllPath}");
            }

            return await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Name = "DiceRollerServer",
                Command = "dotnet",
                Arguments = [serverDllPath],
            }));
        }
    }
}
