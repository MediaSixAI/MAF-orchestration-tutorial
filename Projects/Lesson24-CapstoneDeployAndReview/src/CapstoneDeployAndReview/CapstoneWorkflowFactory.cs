using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Builds the capstone graph: Research -> Analysis -> approval port -> Writer. The ids below
    /// are fixed so a checkpoint written by one process restores onto the graph another process
    /// builds.
    /// </summary>
    internal static class CapstoneWorkflowFactory
    {
        /// <summary>
        /// Id of the start node, which runs the Researcher agent.
        /// </summary>
        internal const string ResearchExecutorId = "research";

        /// <summary>
        /// Id of the node that runs the Analyst agent and raises the approval request.
        /// </summary>
        internal const string AnalysisExecutorId = "analysis";

        /// <summary>
        /// Id of the request port where the workflow pauses for a human decision.
        /// </summary>
        internal const string ApprovalPortId = "report-approval";

        /// <summary>
        /// Id of the node that runs the Writer agent.
        /// </summary>
        internal const string WriterExecutorId = "writer";

        /// <summary>
        /// Workflow-state scope that holds the analysis between the Analysis and Writer nodes.
        /// </summary>
        internal const string StateScope = "capstone";

        /// <summary>
        /// Workflow-state key of the approved analysis.
        /// </summary>
        internal const string AnalysisStateKey = "analysis";

        /// <summary>
        /// Wires the three agents into the graph. The approval port's request and response types
        /// are the standard tool-approval contents, which every hosting surface knows how to carry.
        /// </summary>
        internal static Workflow Build(AIAgent researcher, AIAgent analyst, AIAgent writer)
        {
            ResearchExecutor research = new(researcher);
            AnalysisExecutor analysis = new(analyst);
            RequestPort<ToolApprovalRequestContent, ToolApprovalResponseContent> approval =
                RequestPort.Create<ToolApprovalRequestContent, ToolApprovalResponseContent>(ApprovalPortId);
            WriterExecutor write = new(writer);

            return new WorkflowBuilder(research)
                .AddEdge(research, analysis)
                .AddEdge(analysis, approval)
                .AddEdge(approval, write)
                .WithOutputFrom(write)
                .Build();
        }
    }
}
