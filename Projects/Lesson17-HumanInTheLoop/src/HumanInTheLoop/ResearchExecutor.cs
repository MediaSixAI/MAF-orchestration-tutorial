using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace HumanInTheLoop
{
    /// <summary>
    /// The workflow's first node. It runs ResearchAgent on the incoming topic and sends the facts
    /// on to the review port. It also handles a rejected <see cref="ReviewDecision"/> coming back
    /// from that port, asking ResearchAgent for a revised list that addresses the reviewer's
    /// feedback. The class is <c>partial</c> so the workflow source generator can wire up its
    /// <c>[MessageHandler]</c> methods.
    /// </summary>
    internal sealed partial class ResearchExecutor : Executor
    {
        /// <summary>
        /// The agent that produces the facts.
        /// </summary>
        private readonly AIAgent researchAgent;

        /// <summary>
        /// Registers this executor under a fixed ID and keeps the agent it delegates to.
        /// </summary>
        internal ResearchExecutor(AIAgent researchAgent) : base(nameof(ResearchExecutor))
        {
            this.researchAgent = researchAgent;
        }

        /// <summary>
        /// Handles the workflow's starting input: asks ResearchAgent for facts about the topic and
        /// returns them as round 1 of the review. The return value travels along this executor's
        /// outgoing edge to the request port.
        /// </summary>
        [MessageHandler]
        private async ValueTask<FactsForReview> ResearchAsync(string topic, IWorkflowContext context, CancellationToken cancellationToken)
        {
            AgentResponse response = await this.researchAgent.RunAsync(
                $"Topic: {topic}",
                cancellationToken: cancellationToken);

            return new FactsForReview(topic, response.Text, Round: 1);
        }

        /// <summary>
        /// Handles a rejected review: gives ResearchAgent its previous facts plus the reviewer's
        /// feedback, and returns the revised facts as the next review round.
        /// </summary>
        [MessageHandler]
        private async ValueTask<FactsForReview> ReviseAsync(ReviewDecision decision, IWorkflowContext context, CancellationToken cancellationToken)
        {
            string prompt =
                $"Topic: {decision.Topic}\n\n" +
                $"Your previous facts:\n{decision.Facts}\n\n" +
                $"A human reviewer sent them back with this feedback: {decision.Feedback}\n\n" +
                "Produce a revised list that addresses the feedback.";

            AgentResponse response = await this.researchAgent.RunAsync(prompt, cancellationToken: cancellationToken);

            return new FactsForReview(decision.Topic, response.Text, decision.Round + 1);
        }
    }
}
