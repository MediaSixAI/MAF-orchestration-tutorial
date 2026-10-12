namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Loads the small set of environment-driven settings the capstone pipeline needs beyond
    /// the model provider settings (which ChatClientFactory already owns). Declared in Lesson 22's
    /// design; Lessons 23 and 24 read McpServerPath to locate the companion MCP server.
    /// </summary>
    internal sealed class PipelineConfig
    {
        /// <summary>
        /// The topic the Researcher agent investigates. Defaults to a fixed demo topic so the
        /// capstone runs without any extra setup.
        /// </summary>
        internal required string ResearchTopic { get; init; }

        /// <summary>
        /// Filesystem path to the companion ResearchMcpServer.dll, following Lesson 7's
        /// pattern. A container sets it (see Lesson 24's Dockerfile). Null means "compute the default sibling-project path automatically" - see
        /// <see cref="McpServerConnectionFactory"/>.
        /// </summary>
        internal string? McpServerPath { get; init; }

        /// <summary>
        /// Reads CAPSTONE_RESEARCH_TOPIC and CAPSTONE_MCP_SERVER_PATH from the environment,
        /// falling back to a fixed demo topic and null respectively when unset.
        /// </summary>
        internal static PipelineConfig Load()
        {
            string topic = Environment.GetEnvironmentVariable("CAPSTONE_RESEARCH_TOPIC")
                ?? "the deep sea anglerfish";
            string? mcpServerPath = Environment.GetEnvironmentVariable("CAPSTONE_MCP_SERVER_PATH");

            return new PipelineConfig { ResearchTopic = topic, McpServerPath = mcpServerPath };
        }
    }
}
