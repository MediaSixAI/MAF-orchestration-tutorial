using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace HumanInTheLoop
{
    /// <summary>
    /// The workflow's last node. It only ever receives an approved <see cref="ReviewDecision"/>,
    /// runs WriterAgent on the approved facts, and yields the paragraph as the workflow's output.
    /// The class is <c>partial</c> so the workflow source generator can wire up its
    /// <c>[MessageHandler]</c>, and <c>[YieldsOutput]</c> declares the output type it produces.
    /// </summary>
    [YieldsOutput(typeof(string))]
    internal sealed partial class WriterExecutor : Executor
    {
        /// <summary>
        /// The agent that turns approved facts into prose.
        /// </summary>
        private readonly AIAgent writerAgent;

        /// <summary>
        /// Registers this executor under a fixed ID and keeps the agent it delegates to.
        /// </summary>
        internal WriterExecutor(AIAgent writerAgent) : base(nameof(WriterExecutor))
        {
            this.writerAgent = writerAgent;
        }

        /// <summary>
        /// Asks WriterAgent to turn the approved facts into one paragraph and yields that text as
        /// the workflow's final output.
        /// </summary>
        [MessageHandler]
        private async ValueTask WriteAsync(ReviewDecision decision, IWorkflowContext context, CancellationToken cancellationToken)
        {
            string prompt = $"Topic: {decision.Topic}\n\nApproved facts:\n{decision.Facts}";

            AgentResponse response = await this.writerAgent.RunAsync(prompt, cancellationToken: cancellationToken);

            await context.YieldOutputAsync(response.Text, cancellationToken);
        }
    }
}
