// Lesson 5 - Context Providers
// Builds a HelpDeskAgent equipped with a SemanticMemoryContextProvider that searches a small,
// precomputed knowledge base by embedding similarity (OpenAI's text-embedding-3-small) and injects
// the single best-matching policy snippet into the run - but only when it is actually relevant.
// Demonstrates injecting arbitrary context beyond chat history: retrieved documents, in this case.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ContextProviders
{
    /// <summary>
    /// Entry point that builds a small knowledge base, attaches it to an agent via a semantic
    /// memory context provider, and asks both an on-topic and an off-topic question.
    /// </summary>
    internal sealed class Program
    {
        private static readonly string[] KnowledgeBaseTexts =
        [
            "Employees may carry over up to 5 unused vacation days into the next calendar year; anything beyond that is forfeited.",
            "Expense reports must be submitted within 30 days of the purchase date, with an itemized receipt attached for any amount over $25.",
            "Full-time employees may work remotely up to 3 days per week, with manager approval required for more than 3 consecutive remote days.",
        ];

        /// <summary>
        /// Builds the knowledge base, wires it into a context-provider-equipped agent, and runs
        /// one on-topic question and one off-topic question to show relevance-gated injection.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = OpenAIEmbeddingGeneratorFactory.Create();

            IReadOnlyList<KnowledgeSnippet> knowledgeBase = await SemanticMemoryContextProvider.BuildKnowledgeBaseAsync(
                embeddingGenerator, KnowledgeBaseTexts);

            SemanticMemoryContextProvider memoryProvider = new(embeddingGenerator, knowledgeBase);

            ChatClientAgent helpDeskAgent = AgentFactory.CreateAgentWithContextProviders(
                chatClient,
                name: "HelpDeskAgent",
                instructions: "You are an internal HR help desk assistant. Answer using any relevant " +
                    "company policy context you're given; if none is given, answer from general knowledge.",
                contextProviders: [memoryProvider]);

            //Console.WriteLine("=== On-topic question (should retrieve a policy snippet) ===");
            //AgentResponse onTopicResponse = await helpDeskAgent.RunAsync(
            //    "I didn't use all my time off this year - how many days can I roll over to next year?");
            //Console.WriteLine(onTopicResponse.Text);

            Console.WriteLine();
            Console.WriteLine("=== Off-topic question (should retrieve nothing) ===");
            AgentResponse offTopicResponse = await helpDeskAgent.RunAsync("What's a good name for a pet goldfish?");
            Console.WriteLine(offTopicResponse.Text);
        }
    }
}
