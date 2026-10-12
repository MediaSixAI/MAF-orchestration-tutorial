using Microsoft.Agents.AI.Workflows;

namespace Workflows101
{
    /// <summary>
    /// First stage of the pipeline: trims surrounding whitespace and lowercases the input so
    /// every downstream executor works with a consistent, normalized string. The class is
    /// <c>partial</c> so the workflow source generator can wire up its <c>[MessageHandler]</c>.
    /// </summary>
    internal sealed partial class TextNormalizerExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal TextNormalizerExecutor() : base(nameof(TextNormalizerExecutor))
        {
        }

        /// <summary>
        /// Trims and lowercases the incoming text. The returned value is automatically routed
        /// to whichever executor this one is connected to via an edge.
        /// </summary>
        [MessageHandler]
        private ValueTask<string> NormalizeAsync(string message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(message.Trim().ToLowerInvariant());
        }
    }
}
