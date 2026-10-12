using Microsoft.Agents.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// Builds an inline, code-defined Agent Skill that reports the current process's runtime
    /// environment. Its resource is computed at read time rather than served from a static file,
    /// showing the third way to author a skill alongside the file-based and class-based skills
    /// also composed into this lesson's provider.
    /// </summary>
    internal static class ProjectInfoSkillFactory
    {
        /// <summary>
        /// Creates the "project-info" inline skill, with a dynamic "environment" resource that
        /// re-reads its environment variables every time the agent calls read_skill_resource.
        /// </summary>
        internal static AgentInlineSkill Create()
        {
            return new AgentInlineSkill(
                name: "project-info",
                description: "Reports the current build configuration and target framework this " +
                    "tutorial series runs on. Use when asked what environment or runtime this project targets.",
                instructions: """
                    Use this skill when the user asks about the runtime environment this project targets.
                    1. Read the environment resource for the current configuration details.
                    2. Answer using only the values from that resource - do not guess.
                    """)
                .AddResource("environment", () =>
                {
                    string configuration = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
                    return $"Target framework: net10.0, Configuration: {configuration}, Machine: {Environment.MachineName}";
                });
        }
    }
}
