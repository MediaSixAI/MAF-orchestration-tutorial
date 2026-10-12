using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DiceRollerMcpServer
{
    /// <summary>
    /// MCP tools exposed by this server. Every method here is discovered automatically by
    /// <c>WithToolsFromAssembly</c> via reflection and advertised to any connecting MCP client -
    /// the class and its methods must be public for that reflection-based scan to find them.
    /// </summary>
    [McpServerToolType]
    public static class DiceTools
    {
        private static readonly Random Rng = new();

        /// <summary>
        /// Rolls one or more dice with the given number of sides and returns each individual
        /// result along with the total.
        /// </summary>
        /// <param name="sides">The number of sides on each die. Defaults to 6.</param>
        /// <param name="count">The number of dice to roll. Defaults to 1.</param>
        [McpServerTool, Description("Rolls one or more dice with a given number of sides and returns the results.")]
        public static string RollDice(
            [Description("The number of sides on each die, e.g. 6 or 20.")] int sides = 6,
            [Description("The number of dice to roll.")] int count = 1)
        {
            int[] rolls = Enumerable.Range(0, count).Select(_ => Rng.Next(1, sides + 1)).ToArray();
            return $"Rolled {count}d{sides}: [{string.Join(", ", rolls)}], total = {rolls.Sum()}.";
        }

        /// <summary>
        /// Flips a single coin and returns whether it landed on heads or tails.
        /// </summary>
        [McpServerTool, Description("Flips a coin and returns whether it landed on heads or tails.")]
        public static string FlipCoin()
        {
            return Rng.Next(2) == 0 ? "Heads" : "Tails";
        }
    }
}
