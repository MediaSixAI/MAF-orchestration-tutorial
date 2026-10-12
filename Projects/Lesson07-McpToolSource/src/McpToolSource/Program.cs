// Lesson 7 - MCP as a Tool Source
// Connects to the companion DiceRollerMcpServer process over stdio, discovers its tools at
// runtime via the Model Context Protocol, and hands them straight to an OpenAI-powered agent as
// AITool instances - no tool was hand-coded into this project the way Lesson 2's tools were.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace McpToolSource
{
    /// <summary>
    /// Entry point that connects to a local MCP server, discovers its tools, and runs a
    /// game-master agent that uses those tools to answer two prompts.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Connects to the DiceRollerMcpServer subprocess, lists its tools, wires them into an
        /// agent, and asks it to roll dice and flip a coin.
        /// </summary>
        private static async Task Main(string[] args)
        {
            await using McpClient mcpClient = await McpServerConnectionFactory.ConnectAsync();

            IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
            Console.WriteLine($"Discovered {mcpTools.Count} tool(s) from the MCP server: " +
                string.Join(", ", mcpTools.Select(t => t.Name)));
            Console.WriteLine();

            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent gameMasterAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "GameMasterAgent",
                instructions: "You are a tabletop game master. Use the dice and coin tools " +
                    "available to you whenever a request depends on a random outcome - never " +
                    "make up a roll or a coin flip yourself.",
                tools: [.. mcpTools.Cast<AITool>()]);

            Console.WriteLine("=== Prompt 1: roll dice ===");
            AgentResponse diceResponse = await gameMasterAgent.RunAsync(
                "Roll two six-sided dice for our game and tell me the total.");
            Console.WriteLine(diceResponse.Text);

            Console.WriteLine();
            Console.WriteLine("=== Prompt 2: flip a coin ===");
            AgentResponse coinResponse = await gameMasterAgent.RunAsync(
                "Flip a coin to decide who goes first.");
            Console.WriteLine(coinResponse.Text);
        }
    }
}
