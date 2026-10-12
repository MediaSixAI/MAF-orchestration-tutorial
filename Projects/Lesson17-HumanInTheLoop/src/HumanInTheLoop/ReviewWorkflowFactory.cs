using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace HumanInTheLoop
{
    /// <summary>
    /// Builds the research-review-write workflow: ResearchExecutor feeds a request port that
    /// pauses the run for a human, and the human's decision either releases the facts to
    /// WriterExecutor or sends them back to ResearchExecutor. This is the same shape as the
    /// capstone's Researcher, approval, Writer pipeline.
    /// </summary>
    internal static class ReviewWorkflowFactory
    {
        /// <summary>
        /// The request port's ID. It appears on every <see cref="ExternalRequest"/> the port raises.
        /// </summary>
        internal const string ReviewPortId = "FactReview";

        /// <summary>
        /// Wires the two agents and the review port into a graph with one approval checkpoint and
        /// a revision loop, and returns the built workflow.
        /// </summary>
        internal static Workflow Build(AIAgent researchAgent, AIAgent writerAgent)
        {
            ResearchExecutor research = new(researchAgent);
            WriterExecutor writer = new(writerAgent);
            RequestPort<FactsForReview, ReviewDecision> review =
                RequestPort.Create<FactsForReview, ReviewDecision>(ReviewPortId);

            return new WorkflowBuilder(research)
                .AddEdge(research, review)
                .AddEdge<ReviewDecision>(review, writer, decision => decision is { Approved: true })
                .AddEdge<ReviewDecision>(review, research, decision => decision is { Approved: false })
                .WithOutputFrom(writer)
                .Build();
        }
    }
}
