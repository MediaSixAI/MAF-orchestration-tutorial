using Microsoft.Extensions.AI;

namespace ConcurrentPattern
{
    /// <summary>
    /// Merges the independent results of several concurrently-run agents into a single
    /// transcript. Each agent's own result list includes a copy of the shared input message
    /// alongside its response, so a naive concatenation would repeat that input once per agent
    /// - this aggregator keeps only each agent's own assistant response.
    /// </summary>
    internal static class ReviewAggregator
    {
        /// <summary>
        /// Flattens every agent's result list into one, discarding each list's duplicated copy
        /// of the original input and keeping only the assistant responses.
        /// </summary>
        internal static List<ChatMessage> Combine(IList<List<ChatMessage>> perAgentResults)
        {
            return perAgentResults
                .SelectMany(agentMessages => agentMessages.Where(message => message.Role == ChatRole.Assistant))
                .ToList();
        }
    }
}
