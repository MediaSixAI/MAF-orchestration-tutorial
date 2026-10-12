using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneBuild
{
    /// <summary>
    /// The final node. It receives the reviewer's decision from the approval port. On approval
    /// it reads the analysis back from workflow state, has the Writer agent draft the report, and
    /// yields the report as the workflow's output; on rejection it yields a short notice instead.
    /// <c>partial</c> so the workflow source generator can wire up its <c>[MessageHandler]</c>.
    /// </summary>
    [YieldsOutput(typeof(string))]
    internal sealed partial class WriterExecutor : Executor
    {
        /// <summary>
        /// The Writer agent, with no tools.
        /// </summary>
        private readonly AIAgent writer;

        /// <summary>
        /// Registers this executor under its fixed id.
        /// </summary>
        internal WriterExecutor(AIAgent writer) : base(CapstoneWorkflowFactory.WriterExecutorId)
        {
            this.writer = writer;
        }

        /// <summary>
        /// Acts on the reviewer's decision: drafts and yields the report when approved, or yields
        /// a rejection notice (with the reviewer's reason, if one was given) when not.
        /// </summary>
        [MessageHandler]
        private async ValueTask WriteAsync(
            ToolApprovalResponseContent decision, IWorkflowContext context, CancellationToken cancellationToken)
        {
            if (!decision.Approved)
            {
                string reason = string.IsNullOrWhiteSpace(decision.Reason) ? string.Empty : $" Reason: {decision.Reason}";
                await context.YieldOutputAsync($"The analysis was rejected, so no report was written.{reason}", cancellationToken);
                return;
            }

            AnalysisResult analysis = await context.ReadStateAsync<AnalysisResult>(
                CapstoneWorkflowFactory.AnalysisStateKey, CapstoneWorkflowFactory.StateScope, cancellationToken)
                ?? throw new InvalidOperationException("No analysis was found in workflow state.");

            AgentResponse response = await this.writer.RunAsync(
                $"Write a short, engaging report from this approved analysis:\n{JsonSerializer.Serialize(analysis)}",
                cancellationToken: cancellationToken);

            await context.YieldOutputAsync(response.Text, cancellationToken);
        }
    }
}
