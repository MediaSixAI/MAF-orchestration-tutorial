using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// Agent-run middleware that inspects the newest user message before it ever reaches the
    /// agent and blocks the run outright if it looks like it is asking for sensitive
    /// information such as a password or secret. This is the "guardrail" middleware type: unlike
    /// logging middleware, it can refuse to call the next stage in the pipeline at all.
    /// </summary>
    internal static class ContentGuardrailMiddleware
    {
        private static readonly string[] BlockedTerms = ["password", "secret", "api key", "api_key"];

        /// <summary>
        /// Short-circuits the pipeline with a fixed refusal response when the latest user
        /// message contains a blocked term; otherwise forwards the call unchanged to the next
        /// agent in the pipeline, which may be more middleware or the underlying agent itself.
        /// </summary>
        internal static async Task<AgentResponse> EnforceGuardrailAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            AIAgent innerAgent,
            CancellationToken cancellationToken)
        {
            ChatMessage? lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
            string text = lastUserMessage?.Text ?? string.Empty;

            if (BlockedTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("  [Guardrail] Blocked request - detected a sensitive term in the user message.");
                return new AgentResponse(new ChatMessage(
                    ChatRole.Assistant,
                    "I can't help with requests that involve passwords, secrets, or API keys."));
            }

            Console.WriteLine("  [Guardrail] Passed - no blocked terms detected.");
            return await innerAgent.RunAsync(messages, session, options, cancellationToken);
        }
    }
}
