using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ContextProviders
{
    /// <summary>
    /// An AIContextProvider that performs retrieval-augmented context injection: it embeds the
    /// user's latest message, compares it against a small precomputed knowledge base using
    /// cosine similarity, and injects the single best-matching snippet as extra context for the
    /// model - but only when that snippet is actually relevant. Unrelated questions get nothing
    /// injected, since irrelevant context can hurt an answer as much as missing context can.
    /// </summary>
    internal sealed class SemanticMemoryContextProvider : AIContextProvider
    {
        private const float RelevanceThreshold = 0.7f;

        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
        private readonly IReadOnlyList<KnowledgeSnippet> _knowledgeBase;

        /// <summary>
        /// Creates the provider with an embedding generator for query-time embedding and a
        /// precomputed knowledge base to search against.
        /// </summary>
        internal SemanticMemoryContextProvider(
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            IReadOnlyList<KnowledgeSnippet> knowledgeBase)
        {
            this._embeddingGenerator = embeddingGenerator;
            this._knowledgeBase = knowledgeBase;
        }

        /// <summary>
        /// Builds a searchable <see cref="KnowledgeSnippet"/> knowledge base by embedding each
        /// piece of raw text exactly once, up front, rather than re-embedding it on every run.
        /// </summary>
        internal static async Task<IReadOnlyList<KnowledgeSnippet>> BuildKnowledgeBaseAsync(
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
            IReadOnlyList<string> snippetTexts)
        {
            List<KnowledgeSnippet> knowledgeBase = [];
            foreach (string snippetText in snippetTexts)
            {
                Embedding<float> embedding = (await embeddingGenerator.GenerateAsync([snippetText])).Single();
                knowledgeBase.Add(new KnowledgeSnippet { Text = snippetText, Embedding = embedding });
            }

            return knowledgeBase;
        }

        /// <summary>
        /// Embeds the latest user message and, if a knowledge base snippet is similar enough,
        /// returns it as an additional context message for this run only.
        /// </summary>
        protected override async ValueTask<AIContext> ProvideAIContextAsync(
            InvokingContext context,
            CancellationToken cancellationToken = default)
        {
            string latestUserText = string.Join(
                "\n",
                (context.AIContext.Messages ?? []).Where(m => m.Role == ChatRole.User).Select(m => m.Text));

            if (string.IsNullOrWhiteSpace(latestUserText))
            {
                return new AIContext();
            }

            Embedding<float> queryEmbedding = (await this._embeddingGenerator.GenerateAsync([latestUserText])).Single();

            KnowledgeSnippet? bestMatch = null;
            float bestScore = RelevanceThreshold;

            foreach (KnowledgeSnippet snippet in this._knowledgeBase)
            {
                float score = CosineSimilarity.Compute(queryEmbedding.Vector.Span, snippet.Embedding.Vector.Span);
                if (score >= bestScore)
                {
                    bestScore = score;
                    bestMatch = snippet;
                }
            }

            if (bestMatch is null)
            {
                return new AIContext();
            }

            return new AIContext
            {
                Messages = [new ChatMessage(ChatRole.User, $"Relevant company policy: {bestMatch.Text}")],
            };
        }
    }
}
