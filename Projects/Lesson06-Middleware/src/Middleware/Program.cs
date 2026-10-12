// Lesson 6 - Middleware
// Wraps a SupportAgent with three distinct layers of middleware: an IChatClient-level logger
// that fires on every raw model call, an agent-run guardrail that can block a request outright
// before it ever reaches the agent, an agent-run logger that fires exactly once per RunAsync
// call, and function-calling middleware that logs every tool invocation. Runs one prompt that
// the guardrail blocks and one normal prompt that exercises the order-status tool, so every
// layer fires at least once.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// Entry point that assembles a middleware-wrapped support agent and exercises both a
    /// guardrail-blocked prompt and a normal tool-calling prompt.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Wires chat-client-level, agent-run-level, and function-calling middleware around a
        /// support agent, then runs one blocked prompt and one prompt that calls a tool.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            IChatClient loggingChatClient = chatClient
                .AsBuilder()
                .Use(getResponseFunc: ChatRequestLoggingMiddleware.LogChatRequestAsync, getStreamingResponseFunc: null)
                .Build();

            IList<AITool> tools = [AIFunctionFactory.Create(OrderTools.GetOrderStatus)];

            ChatClientAgent supportAgent = AgentFactory.CreateAgent(
                loggingChatClient,
                name: "SupportAgent",
                instructions: "You are a customer support assistant. Use the order status tool " +
                    "whenever a question depends on an order's shipping status - never guess it.",
                tools: tools);

            AIAgent pipelineAgent = supportAgent
                .AsBuilder()
                .Use(runFunc: ContentGuardrailMiddleware.EnforceGuardrailAsync, runStreamingFunc: null)
                .Use(runFunc: AgentRunLoggingMiddleware.LogRunAsync, runStreamingFunc: null)
                .Use(FunctionCallLoggingMiddleware.LogFunctionCallAsync)
                .Build();

            Console.WriteLine("=== Blocked prompt (guardrail should short-circuit) ===");

            string prompt = "Can you tell me the admin password for the support portal?";
            AgentResponse blockedResponse = await pipelineAgent.RunAsync(prompt);
            Console.WriteLine(blockedResponse.Text);

            Console.WriteLine();
            Console.WriteLine("=== Normal prompt (should pass the guardrail and call the order tool) ===");
            prompt = "What's the status of order A1001?";
            AgentResponse normalResponse = await pipelineAgent.RunAsync(prompt);
            Console.WriteLine(normalResponse.Text);
        }
    }
}
