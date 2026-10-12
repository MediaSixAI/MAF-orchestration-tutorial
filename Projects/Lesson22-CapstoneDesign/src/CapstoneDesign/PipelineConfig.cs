namespace CapstoneDesign
{
    /// <summary>
    /// Loads the small set of environment-driven settings the capstone pipeline needs beyond
    /// the model provider settings (which ChatClientFactory already owns). Reused by Lessons 23 and 24 -
    /// CAPSTONE_MCP_SERVER_PATH is not read by this lesson's code, but is declared here so the
    /// Researcher's tool wiring in Lesson 23 is composition, not new config plumbing.
    /// </summary>
    internal sealed class PipelineConfig
    {
        /// <summary>
        /// The topic the Researcher agent investigates. Defaults to a fixed demo topic so this
        /// lesson runs unattended.
        /// </summary>
        internal required string ResearchTopic { get; init; }

        /// <summary>
        /// Filesystem path or launch command for the Researcher's MCP server, following Lesson 7's
        /// pattern. Null in this lesson since the Researcher has no tools yet - see
        /// <see cref="AgentInstructions.ResearcherPersona"/>.
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
