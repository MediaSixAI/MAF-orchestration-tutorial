# Lesson 5 — Context Providers

## Project Overview

In this lesson we generalize beyond raw chat history: a `HelpDeskAgent` gets a custom `AIContextProvider` that embeds the user's question, searches a small precomputed knowledge base by cosine similarity, and injects the single best-matching HR policy snippet into the run — but only when it's actually relevant. This is retrieval-augmented context injection built from first principles, and the mechanism generalizes to any "arbitrary memory/context" a real agent needs: user profiles, prior summaries, or retrieved documents.

## Prerequisites

Lessons 1–4 — this lesson assumes familiarity with `IChatClient`, `ChatClientAgent`, `RunAsync`, and `AgentSession`. This lesson is otherwise standalone: it does not reference any prior project.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AIContextProvider`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

**Environment variables:**

```powershell
$env:MAF_LLM = "OPENAI"
$env:MAF_MODEL = "gpt-4.1-mini"
$env:OPENAI_API_KEY = "your-openai-api-key"
```

`MAF_LLM` (the provider) and `MAF_MODEL` (the model; on Azure, the deployment name) are both required — `ChatClientFactory` has no built-in defaults and stops with an error naming whichever variable is missing. Get a key at [OpenAI's API keys page](https://platform.openai.com/api-keys), and never hardcode it. Every provider goes through the same OpenAI SDK against its OpenAI-compatible endpoint, so switching provider changes configuration, not agent code:

| `MAF_LLM` | Recommended `MAF_MODEL` | Also set |
|---|---|---|
| `OPENAI` | `gpt-4.1-mini` | `OPENAI_API_KEY` |
| `AZURE` | your `gpt-4.1-mini` deployment name | `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY` |
| `GEMINI` | `gemini-2.5-flash` (`ChatClientFactory` rejects `gemini-3*` models: Gemini 3 tool calls fail on the OpenAI-compatible endpoint) | `GEMINI_API_KEY` |
| `OLLAMA` | `qwen3:8b`, served at `http://localhost:11434/v1/` | nothing — no key needed |
| `LMSTUDIO` | the same Qwen3 8B model (Q4_K_M), by the id LM Studio shows for it — e.g. `qwen/qwen3-8b` — served at `http://localhost:1234/v1/` | nothing — no key needed |

The demos and Expected Output in this series use `MAF_LLM=OPENAI` with `MAF_MODEL=gpt-4.1-mini`.

**Embeddings always use OpenAI.** This lesson also needs an `IEmbeddingGenerator<string, Embedding<float>>`, which `OpenAIEmbeddingGeneratorFactory` builds on OpenAI's `text-embedding-3-small` model through the same `Microsoft.Extensions.AI.OpenAI` package (`AsIEmbeddingGenerator()`). It ignores `MAF_LLM`, so `OPENAI_API_KEY` is required even when `MAF_LLM` selects another chat provider.

## Core Concepts

- **`AIContextProvider`** — the abstract base class for anything that injects additional context into an agent run beyond ordinary chat history. Where a `ChatHistoryProvider` (Lesson 4) answers "what was said before," an `AIContextProvider` answers "what else does the model need to know right now" — a user's stored preferences, a retrieved document, a running summary, or in this lesson's case, a semantically matched policy snippet.
- **Two-phase lifecycle.** A context provider participates at the start of a run via `ProvideAIContextAsync` (return additional instructions, messages, or tools to merge in) and, optionally, at the end via `StoreAIContextAsync` (extract and persist anything learned from this turn). This lesson only implements the "provide" half — a read-only knowledge base — since it has nothing new to learn from each conversation. Lesson 4's `ChatHistoryProvider` and this lesson's `AIContextProvider` are sibling mechanisms with a very similar `Provide`/`Store` shape by design.
- **Providers are shared; state is not.** A single `AIContextProvider` instance is attached to the agent and reused across every session, so it must not cache anything session-specific in its own fields. Our provider only ever holds the precomputed knowledge base — the same for everyone — which is why it needs no session state at all.
- **Retrieval by embedding similarity, not keywords.** `IEmbeddingGenerator<string, Embedding<float>>.GenerateAsync(...)` turns text into a vector; cosine similarity between two vectors measures how semantically close their meanings are, independent of shared words. This lesson's on-topic test question shares almost no vocabulary with the matching policy snippet, and it's still retrieved correctly.
- **Relevance gating matters as much as retrieval.** A provider that always injects its "best" match — even when nothing is actually relevant — pollutes every unrelated answer with noise. This lesson only injects a snippet when its similarity score clears a fixed threshold; otherwise it returns an empty `AIContext` and the agent answers from general knowledge instead.
- **Context providers are a trust boundary.** Anything an `AIContextProvider` injects — especially messages — is merged into the request and trusted by the model like any other input. If a provider pulls from an external or attacker-influenced source, sanitize before returning it; this is the same category of risk as restoring an untrusted serialized session from Lesson 4.

## Complete C# Code

**`src/ContextProviders/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ContextProviders
{
    /// <summary>
    /// Builds the <see cref="IChatClient"/> for the provider and model chosen by the MAF_LLM and
    /// MAF_MODEL environment variables. Every provider uses the OpenAI SDK against its
    /// OpenAI-compatible endpoint.
    /// </summary>
    internal static class ChatClientFactory
    {
        /// <summary>
        /// Creates the chat client. Throws if a required environment variable is missing or empty,
        /// or if the provider is not recognised.
        /// </summary>
        internal static IChatClient Create()
        {
            Func<string, string> requiredVariable = name =>
                Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                    ? value
                    : throw new InvalidOperationException($"{name} is not set. Set it before running this lesson.");

            string provider = requiredVariable("MAF_LLM");
            string modelId = requiredVariable("MAF_MODEL");

            Uri endpoint;
            string apiKey;

            switch (provider.ToUpperInvariant())
            {
                case "OPENAI":
                    endpoint = new Uri("https://api.openai.com/v1/");
                    apiKey = requiredVariable("OPENAI_API_KEY");
                    break;
                case "AZURE":
                    endpoint = new Uri(new Uri(requiredVariable("AZURE_OPENAI_ENDPOINT")), "/openai/v1/");
                    apiKey = requiredVariable("AZURE_OPENAI_API_KEY");
                    break;
                case "GEMINI":
                    // Gemini 3 tool calls need thought signatures, which this endpoint doesn't carry.
                    if (modelId.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"MAF_MODEL is '{modelId}'. Gemini 3 models fail tool calls over the OpenAI-compatible endpoint; use gemini-2.5-flash.");
                    }

                    endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/");
                    apiKey = requiredVariable("GEMINI_API_KEY");
                    break;
                case "OLLAMA":
                    endpoint = new Uri("http://localhost:11434/v1/");
                    apiKey = "ollama"; // Ollama ignores the key, but the SDK refuses an empty one
                    break;
                case "LMSTUDIO":
                    endpoint = new Uri("http://localhost:1234/v1/");
                    apiKey = "lm-studio"; // LM Studio ignores the key, but the SDK refuses an empty one
                    break;
                default:
                    throw new InvalidOperationException(
                        $"MAF_LLM is '{provider}'. Set it to OPENAI, AZURE, GEMINI, OLLAMA or LMSTUDIO.");
            }

            return new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint })
                .GetChatClient(modelId)
                .AsIChatClient();
        }
    }
}
```

**`src/ContextProviders/OpenAIEmbeddingGeneratorFactory.cs`**

```csharp
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
```

**`src/ContextProviders/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ContextProviders
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances. This factory shape is reintroduced across lessons so that assembling the
    /// Lesson 22-24 capstone is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name, system
        /// instructions that define its persona, and an optional set of function tools the
        /// agent may call while responding.
        /// </summary>
        //internal static ChatClientAgent CreateAgent(
        //    IChatClient chatClient,
        //    string name,
        //    string instructions,
        //    IList<AITool>? tools = null)
        //{
        //    return new ChatClientAgent(chatClient, name: name, instructions: instructions, tools: tools);
        //}

        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name, system
        /// instructions, and one or more AIContextProvider instances that inject additional
        /// context - memory, retrieved documents, or anything else - into every run.
        /// </summary>
        internal static ChatClientAgent CreateAgentWithContextProviders(
            IChatClient chatClient,
            string name,
            string instructions,
            IList<AIContextProvider> contextProviders)
        {
            ChatClientAgentOptions options = new()
            {
                Name = name,
                ChatOptions = new ChatOptions { Instructions = instructions },
                AIContextProviders = contextProviders,
            };

            return new ChatClientAgent(chatClient, options);
        }
    }
}
```

**`src/ContextProviders/KnowledgeSnippet.cs`**

```csharp
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
```

**`src/ContextProviders/CosineSimilarity.cs`**

```csharp
namespace ContextProviders
{
    /// <summary>
    /// Computes cosine similarity between two embedding vectors - the standard measure of how
    /// semantically close two pieces of text are once embedded into the same vector space.
    /// </summary>
    internal static class CosineSimilarity
    {
        /// <summary>
        /// Returns a value between -1 and 1 indicating how similar two vectors are in direction,
        /// independent of their magnitude. Values close to 1 mean "very semantically similar."
        /// </summary>
        internal static float Compute(ReadOnlySpan<float> first, ReadOnlySpan<float> second)
        {
            float dotProduct = 0f;
            float firstMagnitude = 0f;
            float secondMagnitude = 0f;

            for (int i = 0; i < first.Length; i++)
            {
                dotProduct += first[i] * second[i];
                firstMagnitude += first[i] * first[i];
                secondMagnitude += second[i] * second[i];
            }

            float denominator = MathF.Sqrt(firstMagnitude) * MathF.Sqrt(secondMagnitude);
            return denominator == 0f ? 0f : dotProduct / denominator;
        }
    }
}
```

**`src/ContextProviders/SemanticMemoryContextProvider.cs`**

```csharp
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
```

**`src/ContextProviders/Program.cs`**

```csharp
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
```

**`src/ContextProviders/ContextProviders.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ContextProviders</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.24.0" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.10.1" />
    <PackageReference Include="OpenAI" Version="2.14.0" />
  </ItemGroup>

</Project>
```

**`ContextProviders.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/ContextProviders/ContextProviders.csproj" />
  </Folder>
</Solution>
```

## Walkthrough

1. **`SemanticMemoryContextProvider.BuildKnowledgeBaseAsync`** embeds each of the three fixed policy snippets exactly once, up front, and pairs each with its embedding in a `KnowledgeSnippet`. This happens before any agent run — the knowledge base is static and reused across every question.
2. **`AgentFactory.CreateAgentWithContextProviders`** is a new overload alongside Lesson 1-4's `CreateAgent`. Context providers aren't available on the simple `ChatClientAgent(chatClient, name, instructions, tools)` constructor — they require building a `ChatClientAgentOptions` with `AIContextProviders` set, and passing that to the `ChatClientAgent(chatClient, options)` overload instead.
3. **`ProvideAIContextAsync`** runs at the start of every `RunAsync` call on `helpDeskAgent`. It pulls the current user text out of `context.AIContext.Messages`, embeds it, and compares it against every snippet in the knowledge base using `CosineSimilarity.Compute`.
4. **The threshold check is the relevance gate.** `bestScore` starts at `RelevanceThreshold` (0.7), not zero — so a snippet is only ever selected if it beats that bar. For the off-topic goldfish question, nothing clears it, `bestMatch` stays `null`, and an empty `AIContext` is returned: no injection at all.
5. **When a match is found**, it comes back as an extra `ChatMessage` in the returned `AIContext.Messages` — merged in by the framework alongside the caller's own input before the model ever sees it. The agent's instructions explicitly tell it to use this context when present, which is why the on-topic answer cites the 5-day carryover limit verbatim.

## Expected Output

Exact wording will vary by run, but the shape will look like:

```
=== On-topic question (should retrieve a policy snippet) ===
You may carry over up to 5 unused vacation days into the next calendar year. Any unused
vacation time beyond those 5 days will be forfeited.

=== Off-topic question (should retrieve nothing) ===
That's a fun question! While it's not strictly an HR matter, I'm happy to help you brainstorm
some ideas for your new finned friend...
[general-knowledge suggestions follow, with no policy content anywhere]
```

The first answer is accurate specifically because the exact number (5 days) came from the retrieved snippet, not from the model's own knowledge. The second answer proves the relevance gate works: nothing about vacation, expenses, or remote work leaks into an unrelated conversation.
