using Microsoft.Agents.AI.Workflows;

namespace Workflows101
{
    /// <summary>
    /// Final stage of the pipeline: wraps the annotated text in a fixed report format. This is
    /// the executor the workflow is configured to treat as a source of terminal output.
    /// </summary>
    internal sealed partial class ReportFormatterExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal ReportFormatterExecutor() : base(nameof(ReportFormatterExecutor))
        {
        }

        /// <summary>
        /// Wraps the incoming annotated text in a fixed report header/footer.
        /// </summary>
        [MessageHandler]
        private ValueTask<string> FormatAsync(string message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult($"=== Workflow Report ===\n{message}\n========================");
        }
    }
}
