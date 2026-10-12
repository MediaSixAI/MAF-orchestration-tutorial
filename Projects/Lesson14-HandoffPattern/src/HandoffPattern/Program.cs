// Lesson 14 - Handoff Pattern
// A TriageAgent decides, per request, whether to hand off to BillingAgent or TechSupportAgent -
// there is no fixed graph deciding which specialist runs; the triage agent's own judgment about
// the request's content picks the path, mid-conversation, at run time.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HandoffPattern
{
    /// <summary>
    /// Entry point that builds a triage agent and two specialists, wires up handoff routes
    /// between them, and runs two different requests to show the triage agent choosing a
    /// different specialist each time based on what was actually asked.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds the three agents once, then runs a billing request and a technical support
        /// request through independently-built handoff workflows.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent triageAgent = AgentFactory.CreateAgent(
                chatClient,
                "TriageAgent",
                "You are the first point of contact for customer support. Decide whether the " +
                    "user's request is a billing question or a technical support question, and " +
                    "hand off to the correct specialist immediately. Do not try to answer billing " +
                    "or technical questions yourself.");
            ChatClientAgent billingAgent = AgentFactory.CreateAgent(
                chatClient,
                "BillingAgent",
                "You handle billing questions. Answer the user's question directly and briefly, " +
                    "as if you are speaking straight to them.");
            ChatClientAgent techSupportAgent = AgentFactory.CreateAgent(
                chatClient,
                "TechSupportAgent",
                "You handle technical support questions. Answer the user's question directly and " +
                    "briefly, as if you are speaking straight to them.");

            await RunHandoffScenarioAsync(
                triageAgent, billingAgent, techSupportAgent,
                "Billing question", "I was charged twice for my subscription this month, can you help?");

            Console.WriteLine();

            await RunHandoffScenarioAsync(
                triageAgent, billingAgent, techSupportAgent,
                "Tech support question", "My app keeps crashing when I open the settings screen.");
        }

        /// <summary>
        /// Builds a fresh handoff workflow from the given agents, declaring which specialists
        /// the triage agent may hand off to and why, then runs a single prompt through it and
        /// prints the full transcript, flagging the message where the handoff decision happens.
        /// </summary>
        private static async Task RunHandoffScenarioAsync(
            ChatClientAgent triageAgent,
            ChatClientAgent billingAgent,
            ChatClientAgent techSupportAgent,
            string label,
            string prompt)
        {
            Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
                .WithHandoff(triageAgent, billingAgent, "The user has a billing, payment, or subscription question.")
                .WithHandoff(triageAgent, techSupportAgent, "The user has a technical problem or bug with the product.")
                .Build();

            Console.WriteLine($"=== {label} ===");
            Console.WriteLine($"User: {prompt}");

            Run result = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, prompt));
            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> transcript)
                {
                    foreach (ChatMessage message in transcript)
                    {
                        string author = string.IsNullOrEmpty(message.AuthorName) ? message.Role.ToString() : message.AuthorName;
                        bool isHandoffDecision = message.Contents.Any(content => content is FunctionCallContent);
                        string marker = isHandoffDecision ? " (handoff decision)" : "";
                        if (!string.IsNullOrWhiteSpace(message.Text) || isHandoffDecision)
                        {
                            Console.WriteLine($"  [{author}]{marker} {message.Text}");
                        }
                    }
                }
            }
        }
    }
}
