using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;

namespace ContextProviders
{
    /// <summary>
    /// Builds the <see cref="IEmbeddingGenerator{String, Embedding}"/> used to compute text
    /// embeddings for semantic retrieval in this lesson, backed by OpenAI's text-embedding-3-small
    /// model. Kept separate from <see cref="ChatClientFactory"/> since embeddings and chat
    /// are different capabilities with different model ids. Embeddings always use OpenAI,
    /// whichever chat provider MAF_LLM selects, so OPENAI_API_KEY is required for this lesson.
    /// </summary>
    internal static class OpenAIEmbeddingGeneratorFactory
    {
        /// <summary>
        /// The OpenAI embedding model used for every embedding-relevant lesson in this series.
        /// </summary>
        internal const string EmbeddingModelId = "text-embedding-3-small";

        /// <summary>
        /// Creates an <see cref="IEmbeddingGenerator{String, Embedding}"/> targeting OpenAI's
        /// text-embedding-3-small model, reading the API key from the OPENAI_API_KEY environment
        /// variable. Throws if the key has not been configured.
        /// </summary>
        internal static IEmbeddingGenerator<string, Embedding<float>> Create()
        {
            string apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY")
                ?? throw new InvalidOperationException(
                    "The OPENAI_API_KEY environment variable is not set. " +
                    "Create a key at https://platform.openai.com/api-keys and set it before running this lesson.");

            EmbeddingClient embeddingClient = new OpenAIClient(apiKey).GetEmbeddingClient(EmbeddingModelId);
            return embeddingClient.AsIEmbeddingGenerator();
        }
    }
}
