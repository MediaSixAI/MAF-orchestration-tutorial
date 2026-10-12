using System.ComponentModel;

namespace HostedTools
{
    /// <summary>
    /// A local C# function tool exposed to the agent, used in this lesson to contrast with the
    /// hosted tools below - this one's entire implementation lives in this process.
    /// </summary>
    internal static class OpsTools
    {
        private static readonly Dictionary<string, string> OnCallByTeam = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Platform"] = "Jordan Lee",
            ["Payments"] = "Priya Patel",
        };

        /// <summary>
        /// Looks up the current on-call engineer for a known team. Unknown teams produce a
        /// clear error string instead of a guess.
        /// </summary>
        /// <param name="team">The team name to look up, e.g. 'Platform' or 'Payments'.</param>
        [Description("Gets the name of the current on-call engineer for a given team.")]
        internal static string GetOnCallEngineer(
            [Description("The team name, e.g. 'Platform' or 'Payments'.")] string team)
        {
            if (!OnCallByTeam.TryGetValue(team, out string? engineer))
            {
                return $"No on-call data is available for the '{team}' team.";
            }

            return engineer;
        }
    }
}
