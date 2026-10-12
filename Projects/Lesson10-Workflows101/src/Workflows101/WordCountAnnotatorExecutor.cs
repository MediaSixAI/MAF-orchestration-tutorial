using Microsoft.Agents.AI.Workflows;

namespace Workflows101
{
    /// <summary>
    /// Second stage of the pipeline: counts the words in the normalized text and appends the
    /// count, producing a single annotated string for the final formatting stage.
    /// </summary>
    internal sealed partial class WordCountAnnotatorExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal WordCountAnnotatorExecutor() : base(nameof(WordCountAnnotatorExecutor))
        {
        }

        /// <summary>
        /// Counts whitespace-separated words in the incoming text and returns an annotated
        /// string carrying both the original text and its word count.
        /// </summary>
        [MessageHandler]
        private ValueTask<string> AnnotateAsync(string message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            int wordCount = message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return ValueTask.FromResult($"{message} ({wordCount} words)");
        }
    }
}
