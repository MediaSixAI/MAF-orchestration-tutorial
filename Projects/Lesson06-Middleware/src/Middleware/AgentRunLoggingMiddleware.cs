using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// Agent-run middleware that logs the size of the request going into a run and the size of
    /// the response coming out of it. Unlike <see cref="ChatRequestLoggingMiddleware"/>, this
    /// fires exactly once per RunAsync call, regardless of how many internal model calls the
    /// agent makes while resolving tool calls along the way.
    /// </summary>
    internal static class AgentRunLoggingMiddleware
    {
        /// <summary>
        /// Logs the inbound message count, forwards the call to the next agent in the pipeline,
        /// then logs the outbound message count before returning the response unchanged.
        /// </summary>
        internal static async Task<AgentResponse> LogRunAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            AIAgent innerAgent,
            CancellationToken cancellationToken)
        {
            Console.WriteLine($"  [Run] -> starting run with {messages.Count()} input message(s).");
            AgentResponse response = await innerAgent.RunAsync(messages, session, options, cancellationToken);
            Console.WriteLine($"  [Run] <- run completed with {response.Messages.Count} output message(s).");
            return response;
        }
    }
}
