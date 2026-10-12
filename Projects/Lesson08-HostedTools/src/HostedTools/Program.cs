// Lesson 8 - Hosted Tools
// Contrasts a local C# function tool (this process does all the work) with two of OpenAI's
// hosted tools - web search and code execution (OpenAI's infrastructure does all the work,
// with no local implementation at all) - then puts a local tool and a hosted tool on the same
// agent. Every demo prints the tool activity found in the response, so the output proves which
// tools actually ran rather than trusting the answer text.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedTools
{
    /// <summary>
    /// Entry point that runs three single-purpose agents - local function tool, hosted web
    /// search, hosted code execution - then a fourth agent that combines a local tool with a
    /// hosted tool, printing the evidence of tool use after each answer.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds and runs each agent in turn against one Responses API client, printing each
        /// answer followed by the tool calls and cited sources found in its response.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = OpenAIResponsesClientFactory.Create();

            IList<AITool> localOnlyTools = [AIFunctionFactory.Create(OpsTools.GetOnCallEngineer)];

            ChatClientAgent opsAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "OpsAgent",
                instructions: "You are an on-call ops assistant. Use the on-call lookup tool " +
                    "whenever asked who is on call - never guess a name.",
                tools: localOnlyTools);

            Console.WriteLine("=== Local function tool (runs in this process) ===");
            AgentResponse opsResponse = await opsAgent.RunAsync("Who is currently on call for the Payments team?");
            Console.WriteLine(opsResponse.Text);
            ToolActivityReporter.Print(opsResponse);

            IList<AITool> webSearchOnlyTools = [new HostedWebSearchTool()];

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "ResearchAgent",
                instructions: "You are a research assistant. Use web search for anything " +
                    "requiring current, real-world information.",
                tools: webSearchOnlyTools);

            Console.WriteLine();
            Console.WriteLine("=== Hosted web search tool (runs on OpenAI's infrastructure) ===");
            AgentResponse researchResponse = await researchAgent.RunAsync(
                "What is one real, current news headline about .NET from the past week? Cite what you found.");
            Console.WriteLine(researchResponse.Text);
            ToolActivityReporter.Print(researchResponse);

            IList<AITool> codeInterpreterOnlyTools = [new HostedCodeInterpreterTool()];

            ChatClientAgent codeAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "CodeAgent",
                instructions: "You are a math assistant. Use the code execution tool to " +
                    "actually run code for any calculation - never compute it in your head.",
                tools: codeInterpreterOnlyTools);

            Console.WriteLine();
            Console.WriteLine("=== Hosted code execution tool (runs on OpenAI's infrastructure) ===");
            AgentResponse codeResponse = await codeAgent.RunAsync(
                "Compute the 15th Fibonacci number by actually running code.");
            Console.WriteLine(codeResponse.Text);
            ToolActivityReporter.Print(codeResponse);

            IList<AITool> mixedTools = [AIFunctionFactory.Create(OpsTools.GetOnCallEngineer), new HostedWebSearchTool()];

            ChatClientAgent mixedAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "MixedAgent",
                instructions: "You are an incident assistant. Use the on-call lookup tool for who is " +
                    "on call, and web search for anything requiring current, real-world information.",
                tools: mixedTools);

            Console.WriteLine();
            Console.WriteLine("=== A local tool and a hosted tool on one agent ===");
            AgentResponse mixedResponse = await mixedAgent.RunAsync(
                "Who is on call for the Platform team, and is there any current public news of an " +
                "Azure outage they should know about?");
            Console.WriteLine(mixedResponse.Text);
            ToolActivityReporter.Print(mixedResponse);
        }
    }
}
