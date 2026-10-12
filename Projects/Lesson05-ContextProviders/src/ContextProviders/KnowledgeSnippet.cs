using Microsoft.Extensions.AI;

namespace ContextProviders
{
    /// <summary>
    /// A single piece of retrievable knowledge together with its precomputed embedding vector.
    /// Represents the kind of small, static knowledge base a semantic memory context provider
    /// might search over - the "retrieved documents" case called out in this lesson.
    /// </summary>
    internal sealed class KnowledgeSnippet
    {
        /// <summary>
        /// The plain-text content of the snippet, as it should be shown to the model if retrieved.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>
        /// The embedding vector for <see cref="Text"/>, computed once when the knowledge base is built.
        /// </summary>
        public required Embedding<float> Embedding { get; init; }
    }
}
