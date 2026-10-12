# Lesson 13 — Concurrent Pattern

## Project Overview

Three independent specialist agents — `SeoAgent`, `LegalAgent`, `ToneAgent` — react to the same marketing headline at the same time via `AgentWorkflowBuilder.BuildConcurrent`, and their responses are merged into a single result with a custom aggregator. Unlike Lesson 12's sequential pipeline, none of these agents depends on another's output — they're independent sub-tasks, so running them concurrently is a real, measured speedup, not just a different code shape.

## Prerequisites

Lesson 12 — this lesson assumes `AgentWorkflowBuilder`, `InProcessExecution.RunAsync`, and the `List<ChatMessage>` output shape introduced there. Where Lesson 12 chained agents with `BuildSequential`, this lesson uses the sibling API, `BuildConcurrent`, for agents that don't depend on each other at all.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model — this lesson uses `AgentWorkflowBuilder.BuildConcurrent` specifically |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No new packages this lesson — `AgentWorkflowBuilder.BuildConcurrent` is a sibling of Lesson 12's `BuildSequential`, in the same `Microsoft.Agents.AI.Workflows` namespace, on the same already-referenced package.

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

## Core Concepts

- **`BuildConcurrent` runs every agent against the same input at once, and an aggregator merges the results.** `AgentWorkflowBuilder.BuildConcurrent(agents, aggregator)` sends the same input to every agent, waits for all of them to finish, then hands their results to an aggregator: a `Func<IList<List<ChatMessage>>, List<ChatMessage>>` that turns several answers into one result list. The aggregator is optional. Leave it out and the workflow keeps the last message from each agent. This lesson passes its own, `ReviewAggregator.Combine`, so the merge rule is visible in the code and easy to change.
- **The aggregator receives each agent's whole conversation, input included.** It gets one `List<ChatMessage>` per agent, and each list holds that agent's side of the run: the shared headline as a `User` message, followed by the agent's `Assistant` reply. Concatenating the lists as they are would repeat the headline once per agent, so `ReviewAggregator.Combine` selects the `ChatRole.Assistant` messages, which are the agents' replies, and merges only those.
- **Concurrency here is real, not just a different API shape.** The program times the identical three agents both ways: three plain `RunAsync` calls one after another, then the same agents through `BuildConcurrent`. In the run shown in Expected Output, the sequential baseline took about 13 seconds and the concurrent workflow about 5 seconds, close to the time of the single slowest agent rather than the sum of all three. This is the concrete payoff of the pattern: independent work that has no ordering dependency should not pay for ordering it anyway.
- **This is the right tool specifically because the three agents don't need each other's output.** `SeoAgent`, `LegalAgent`, and `ToneAgent` all react to the *same* headline in isolation — none of them needs to see what the others said. That independence is what makes concurrency safe here; Lesson 12's `DraftAgent`/`EditorAgent` pipeline couldn't be parallelized this way because each step's whole job was to consume the previous one's output.
- **`AgentWorkflowBuilder.CreateConcurrentBuilderWith(agents)` is the fluent equivalent**, mirroring the `SequentialWorkflowBuilder` that `AgentWorkflowBuilder.CreateSequentialBuilderWith` returns: `.WithAggregator(...)`, `.WithName(...)`, `.WithDescription(...)`, `.WithOutputFrom(...)`, `.WithIntermediateOutputFrom(...)`, then `.Build()`. This lesson uses the static `BuildConcurrent` shortcut since only the aggregator needs configuring, but the fluent form exists for the same reasons Lesson 12's did.

## Complete C# Code

**`src/ConcurrentPattern/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ConcurrentPattern
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

**`src/ConcurrentPattern/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ConcurrentPattern
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>. This factory shape is reintroduced
    /// across lessons so that assembling the Lesson 22-24 capstone is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name and system
        /// instructions that define its persona.
        /// </summary>
        internal static ChatClientAgent CreateAgent(IChatClient chatClient, string name, string instructions)
        {
            return new ChatClientAgent(chatClient, name: name, instructions: instructions);
        }
    }
}
```

**`src/ConcurrentPattern/ReviewAggregator.cs`**

```csharp
using Microsoft.Extensions.AI;

namespace ConcurrentPattern
{
    /// <summary>
    /// Merges the independent results of several concurrently-run agents into a single
    /// transcript. Each agent's own result list includes a copy of the shared input message
    /// alongside its response, so a naive concatenation would repeat that input once per agent
    /// - this aggregator keeps only each agent's own assistant response.
    /// </summary>
    internal static class ReviewAggregator
    {
        /// <summary>
        /// Flattens every agent's result list into one, discarding each list's duplicated copy
        /// of the original input and keeping only the assistant responses.
        /// </summary>
        internal static List<ChatMessage> Combine(IList<List<ChatMessage>> perAgentResults)
        {
            return perAgentResults
                .SelectMany(agentMessages => agentMessages.Where(message => message.Role == ChatRole.Assistant))
                .ToList();
        }
    }
}
```

**`src/ConcurrentPattern/Program.cs`**

```csharp
// Lesson 13 - Concurrent Pattern
// Runs three independent specialist agents - SEO, legal risk, and tone - against the same
// marketing headline at the same time via AgentWorkflowBuilder.BuildConcurrent, then merges
// their responses with a custom aggregator. A sequential baseline using the same three agents
// is timed first, so the concurrent run's speedup is measured, not just asserted.

using System.Diagnostics;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace ConcurrentPattern
{
    /// <summary>
    /// Entry point that times a sequential baseline against a concurrent workflow running the
    /// same three review agents on the same input.
    /// </summary>
    internal sealed class Program
    {
        private const string Headline = "Buy Now, Regret Never!";

        /// <summary>
        /// Builds three independent review agents, runs them sequentially to establish a
        /// timing baseline, then runs the same agents concurrently and prints both durations
        /// alongside the merged result.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent seoAgent = AgentFactory.CreateAgent(
                chatClient, "SeoAgent", "Give one short SEO-focused suggestion for the given headline.");
            ChatClientAgent legalAgent = AgentFactory.CreateAgent(
                chatClient, "LegalAgent", "Give one short legal-risk note for the given headline.");
            ChatClientAgent toneAgent = AgentFactory.CreateAgent(
                chatClient, "ToneAgent", "Give one short tone-and-voice suggestion for the given headline.");

            Console.WriteLine("=== Sequential baseline (same three agents, one after another) ===");
            Stopwatch sequentialTimer = Stopwatch.StartNew();
            await seoAgent.RunAsync(Headline);
            await legalAgent.RunAsync(Headline);
            await toneAgent.RunAsync(Headline);
            sequentialTimer.Stop();
            Console.WriteLine($"Sequential elapsed: {sequentialTimer.ElapsedMilliseconds}ms");

            Console.WriteLine();
            Console.WriteLine("=== Concurrent workflow (BuildConcurrent) ===");
            Workflow workflow = AgentWorkflowBuilder.BuildConcurrent(
                [seoAgent, legalAgent, toneAgent], ReviewAggregator.Combine);

            Stopwatch concurrentTimer = Stopwatch.StartNew();
            Run result = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, Headline));
            concurrentTimer.Stop();
            Console.WriteLine($"Concurrent elapsed: {concurrentTimer.ElapsedMilliseconds}ms");

            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> reviews)
                {
                    Console.WriteLine($"Merged {reviews.Count} independent review(s):");
                    foreach (ChatMessage review in reviews)
                    {
                        Console.WriteLine($"  [{review.AuthorName}] {review.Text}");
                    }
                }
            }
        }
    }
}
```

## Walkthrough

1. **The same three agents are used for both the baseline and the concurrent run.** `seoAgent`, `legalAgent`, and `toneAgent` are built once through the usual `AgentFactory.CreateAgent`. Nothing about their construction differs from a Lesson 12 sequential agent — the difference is entirely in how they're later invoked.
2. **The sequential baseline is deliberately plain.** Three separate `await agent.RunAsync(Headline)` calls, one after another, timed with a `Stopwatch` — no workflow involved at all. This establishes the cost of NOT running them concurrently, as a real number to compare against.
3. **`AgentWorkflowBuilder.BuildConcurrent([seoAgent, legalAgent, toneAgent], ReviewAggregator.Combine)` is the entire orchestration step.** One call, taking the same three agents in any order (order doesn't matter here, unlike Lesson 12) plus the aggregator function that will combine whatever they each independently produce.
4. **`ReviewAggregator.Combine` selects the agents' replies.** Each agent's list holds the shared headline followed by that agent's reply. `.Where(message => message.Role == ChatRole.Assistant)` keeps the replies, and `SelectMany` flattens them into one list with exactly one message per agent.
5. **The timing numbers printed by both `Stopwatch`s are the point of the lesson.** `InProcessExecution.RunAsync(workflow, ...)` for the concurrent run finishes in roughly the time of one agent's response, not three — because all three model calls are genuinely in flight at the same time, not queued behind each other.

## Expected Output

```
=== Sequential baseline (same three agents, one after another) ===
Sequential elapsed: 13246ms

=== Concurrent workflow (BuildConcurrent) ===
Concurrent elapsed: 5356ms
Merged 3 independent review(s):
  [SeoAgent] Add a specific product/service keyword to inform search engines and users what you're selling.
  [LegalAgent] The absolute claim "Regret Never!" could be deemed a misleading or deceptive trade practice, potentially leading to consumer complaints and legal challenges if customers experience any dissatisfaction.
  [ToneAgent] Enthusiastic and slightly audacious.
```

The exact wording and millisecond counts will vary between runs — every response is a live model call, and network/model latency isn't fixed — but the shape holds: the concurrent run's elapsed time will consistently be well under the sequential baseline's, and the merged output will always contain exactly three messages, one reply per agent.
