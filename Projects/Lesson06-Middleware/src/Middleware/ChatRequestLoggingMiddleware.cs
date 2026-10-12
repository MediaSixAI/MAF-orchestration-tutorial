using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// IChatClient-level middleware that logs every raw request and response exchanged with the
    /// underlying model. Because it sits below the agent's function-invocation loop, this
    /// middleware fires once per model call - which can be more than once per RunAsync call
    /// whenever the agent works through a multi-step tool-calling turn.
    /// </summary>
    internal static class ChatRequestLoggingMiddleware
    {
        /// <summary>
        /// Logs the outgoing message count before forwarding the call to the inner chat client,
        /// then logs the returned message count before handing the response back up the chain.
        /// </summary>
        internal static async Task<ChatResponse> LogChatRequestAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options,
            IChatClient innerChatClient,
            CancellationToken cancellationToken)
        {
            Console.WriteLine($"  [ChatClient] -> sending {messages.Count()} message(s) to the model.");
            ChatResponse response = await innerChatClient.GetResponseAsync(messages, options, cancellationToken);
            Console.WriteLine($"  [ChatClient] <- received {response.Messages.Count} message(s) from the model.");
            return response;
        }
    }
}
