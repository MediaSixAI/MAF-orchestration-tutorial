using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// The graph's start node. It speaks MAF's chat protocol: incoming chat messages are collected,
    /// and a <see cref="TurnToken"/> tells it to take its turn. That is the input shape
    /// <c>Workflow.AsAIAgent()</c> drives, so the same graph runs in a console app and behind a
    /// hosted agent endpoint. On its turn it hands the user's topic to the Researcher agent and
    /// sends the agent's notes along the next edge.
    /// </summary>
    internal sealed class ResearchExecutor : ChatProtocolExecutor
    {
        /// <summary>
        /// The Researcher agent, with its MCP research tool.
        /// </summary>
        private readonly AIAgent researcher;

        /// <summary>
        /// Registers this executor under its fixed id. AutoSendTurnToken is off because the next
        /// node is a plain executor that takes a <see cref="ResearchNotes"/> message, not a turn.
        /// </summary>
        internal ResearchExecutor(AIAgent researcher)
            : base(CapstoneWorkflowFactory.ResearchExecutorId, new ChatProtocolExecutorOptions { AutoSendTurnToken = false })
        {
            this.researcher = researcher;
        }

        /// <summary>
        /// Declares the extra message type this executor sends, on top of the chat protocol.
        /// </summary>
        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return base.ConfigureProtocol(protocolBuilder).SendsMessage<ResearchNotes>();
        }

        /// <summary>
        /// Runs the Researcher agent on the user's topic and sends its notes to the analysis step.
        /// A turn with no user text is skipped: a hosted session takes one such turn after it
        /// delivers the approval response, and there is nothing to research in it.
        /// </summary>
        protected override async ValueTask TakeTurnAsync(
            List<ChatMessage> messages, IWorkflowContext context, bool? emitEvents, CancellationToken cancellationToken = default)
        {
            string topic = string.Join(" ", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text)).Trim();
            if (topic.Length == 0)
            {
                return;
            }

            AgentResponse response = await this.researcher.RunAsync(topic, cancellationToken: cancellationToken);
            await context.SendMessageAsync(new ResearchNotes(topic, response.Text), cancellationToken: cancellationToken);
        }
    }
}
