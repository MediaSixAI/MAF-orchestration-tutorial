using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// The analysis node. It asks the Analyst agent to extract the research notes into the
    /// <see cref="AnalysisResult"/> JSON contract, parses the reply, keeps the result in workflow
    /// state for the Writer, and returns the approval request that the next edge carries to the
    /// approval port. <c>partial</c> so the workflow source generator can wire up its
    /// <c>[MessageHandler]</c>.
    /// </summary>
    internal sealed partial class AnalysisExecutor : Executor
    {
        /// <summary>
        /// The Analyst agent, with no tools.
        /// </summary>
        private readonly AIAgent analyst;

        /// <summary>
        /// Registers this executor under its fixed id.
        /// </summary>
        internal AnalysisExecutor(AIAgent analyst) : base(CapstoneWorkflowFactory.AnalysisExecutorId)
        {
            this.analyst = analyst;
        }

        /// <summary>
        /// Runs the Analyst on the notes, parses its reply, stores the analysis in workflow state
        /// (so it is part of every checkpoint), and returns the request for human approval.
        /// </summary>
        [MessageHandler]
        private async ValueTask<ToolApprovalRequestContent> AnalyzeAsync(
            ResearchNotes notes, IWorkflowContext context, CancellationToken cancellationToken)
        {
            AgentResponse response = await this.analyst.RunAsync(
                $"Topic: {notes.Topic}\n\nResearch notes:\n{notes.Notes}", cancellationToken: cancellationToken);

            AnalysisResult analysis = AnalysisParser.Parse(response.Text);

            await context.QueueStateUpdateAsync(
                CapstoneWorkflowFactory.AnalysisStateKey, analysis, CapstoneWorkflowFactory.StateScope, cancellationToken);

            return ReportApprovalRequest.Create(analysis);
        }
    }
}
