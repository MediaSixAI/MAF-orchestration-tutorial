// Lesson 9 - Approvals (Function Invocation Gating)
// Wraps a local weather tool in ApprovalRequiredAIFunction so the agent must pause and wait for
// approval before the tool actually runs. Demonstrates both outcomes on separate sessions: a
// human (or a policy) approving the call, and one rejecting it - the first governance layer in
// the series, and the direct setup for the multi-step Human-in-the-Loop pattern in Lesson 17.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Approvals
{
    /// <summary>
    /// Entry point that runs the same approval-gated weather tool through both an approval and
    /// a rejection, each on its own session.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds a weather agent whose only tool requires approval, then walks through the
        /// approve path and the reject path in turn.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            AIFunction weatherFunction = AIFunctionFactory.Create(WeatherTools.GetWeather);
            AIFunction approvalRequiredWeatherFunction = new ApprovalRequiredAIFunction(weatherFunction);

            ChatClientAgent weatherAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "WeatherAgent",
                instructions: "You are a helpful weather assistant. Use the weather tool for any " +
                    "question about current conditions - never guess.",
                tools: [approvalRequiredWeatherFunction]);

            Console.WriteLine("=== Scenario 1: approving the tool call ===");
            await RunApprovalScenarioAsync(weatherAgent, "What is the weather like in Amsterdam?", approve: true);

            Console.WriteLine();
            Console.WriteLine("=== Scenario 2: rejecting the tool call ===");
            await RunApprovalScenarioAsync(weatherAgent, "What is the weather like in Tokyo?", approve: false);
        }

        /// <summary>
        /// Runs a prompt on a fresh session, surfaces any pending tool approval request exactly
        /// as a human reviewer would see it, then resolves it with the given decision and prints
        /// the agent's final response.
        /// </summary>
        private static async Task RunApprovalScenarioAsync(ChatClientAgent agent, string prompt, bool approve)
        {
            AgentSession session = await agent.CreateSessionAsync();
            AgentResponse response = await agent.RunAsync(prompt, session);

            List<ToolApprovalRequestContent> approvalRequests = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();

            if (approvalRequests.Count == 0)
            {
                Console.WriteLine("No approval was requested - unexpected for this lesson.");
                Console.WriteLine(response.Text);
                return;
            }

            ToolApprovalRequestContent requestContent = approvalRequests[0];
            if (requestContent.ToolCall is FunctionCallContent functionCall)
            {
                string arguments = string.Join(", ", functionCall.Arguments?.Select(kv => $"{kv.Key}={kv.Value}") ?? []);
                Console.WriteLine($"Approval requested to call '{functionCall.Name}' with arguments: {arguments}");
            }

            Console.WriteLine(approve ? "Decision: APPROVED" : "Decision: REJECTED");

            ToolApprovalResponseContent approvalResponse = requestContent.CreateResponse(
                approve, reason: approve ? "Approved by reviewer." : "Rejected by reviewer.");
            ChatMessage approvalMessage = new(ChatRole.User, [approvalResponse]);

            AgentResponse finalResponse = await agent.RunAsync(approvalMessage, session);
            Console.WriteLine(finalResponse.Text);
        }
    }
}
