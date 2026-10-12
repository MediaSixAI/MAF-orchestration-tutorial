using Microsoft.Extensions.AI;

namespace ObservabilityAndEvaluation
{
    /// <summary>
    /// Delegating <see cref="IChatClient"/> that sets every outgoing request's MaxOutputTokens to
    /// at least a configured budget before forwarding it to the inner client. This lesson wraps
    /// the evaluators' "judge" chat client in it, so the judge always has room to write its
    /// reasoning and finish with the tagged score the quality evaluators parse.
    /// </summary>
    internal sealed class MaxOutputTokensChatClient : DelegatingChatClient
    {
        private readonly int _minimumMaxOutputTokens;

        /// <summary>
        /// Wraps innerClient, ensuring every outgoing request's MaxOutputTokens is at least
        /// minimumMaxOutputTokens.
        /// </summary>
        internal MaxOutputTokensChatClient(IChatClient innerClient, int minimumMaxOutputTokens)
            : base(innerClient)
        {
            _minimumMaxOutputTokens = minimumMaxOutputTokens;
        }

        /// <summary>
        /// Forwards a non-streaming request to the inner client after raising MaxOutputTokens if needed.
        /// </summary>
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetResponseAsync(messages, Boosted(options), cancellationToken);

        /// <summary>
        /// Forwards a streaming request to the inner client after raising MaxOutputTokens if needed.
        /// </summary>
        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetStreamingResponseAsync(messages, Boosted(options), cancellationToken);

        /// <summary>
        /// Clones the given options (or creates new ones) and raises MaxOutputTokens to the
        /// configured floor if it is unset or lower.
        /// </summary>
        private ChatOptions Boosted(ChatOptions? options)
        {
            ChatOptions boosted = options?.Clone() ?? new ChatOptions();
            if (boosted.MaxOutputTokens is null || boosted.MaxOutputTokens < _minimumMaxOutputTokens)
            {
                boosted.MaxOutputTokens = _minimumMaxOutputTokens;
            }
            return boosted;
        }
    }
}
