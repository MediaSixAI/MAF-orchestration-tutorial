// Lesson 15 - Group Chat Pattern
// Three agents - Product, Engineering, and Design - discuss a single feature question in a
// shared conversational context via AgentWorkflowBuilder.CreateGroupChatBuilderWith and a
// RoundRobinGroupChatManager. Each agent sees every prior contribution before speaking and adds
// its own role-based perspective, unlike Lesson 14's handoff (only one agent ever responds) or
// Lesson 13's concurrent pattern (agents never see each other's output).

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace GroupChatPattern
{
    /// <summary>
    /// Entry point that builds three role-specialized agents, wires them into a round-robin
    /// group chat, and runs a single feature question through it.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds Product, Engineering, and Design agents, chains them into a three-turn
        /// round-robin group chat, and prints the resulting shared transcript.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent productAgent = AgentFactory.CreateAgent(
                chatClient, "ProductAgent",
                "You represent the product perspective. Contribute one short point about user " +
                    "value for the feature being discussed. Keep it to one sentence.");
            ChatClientAgent engineeringAgent = AgentFactory.CreateAgent(
                chatClient, "EngineeringAgent",
                "You represent the engineering perspective. Contribute one short point about " +
                    "feasibility or implementation for the feature being discussed. Keep it to one sentence.");
            ChatClientAgent designAgent = AgentFactory.CreateAgent(
                chatClient, "DesignAgent",
                "You represent the design perspective. Contribute one short point about user " +
                    "experience for the feature being discussed. Keep it to one sentence.");

            List<AIAgent> participants = [productAgent, engineeringAgent, designAgent];

            Workflow workflow = AgentWorkflowBuilder
                .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents)
                {
                    MaximumIterationCount = participants.Count,
                })
                .AddParticipants(participants)
                .Build();

            Console.WriteLine("=== Group chat: one round-robin round, three role-based perspectives ===");
            const string question = "Should we add a dark mode toggle to the app?";
            Console.WriteLine($"User: {question}");
            Console.WriteLine();

            Run result = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, question));
            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> transcript)
                {
                    foreach (ChatMessage message in transcript)
                    {
                        string author = string.IsNullOrEmpty(message.AuthorName) ? message.Role.ToString() : message.AuthorName;
                        Console.WriteLine($"  [{author}] {message.Text}");
                    }
                }
            }
        }
    }
}
