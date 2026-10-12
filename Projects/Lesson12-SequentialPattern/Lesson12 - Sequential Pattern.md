# Lesson 12 — Sequential Pattern

## Project Overview

Three agents — `OutlineAgent`, `DraftAgent`, `EditorAgent` — are chained into a fixed blog-writing pipeline where each one consumes what came before it and adds its own contribution. This is the simplest multi-agent orchestration pattern in the series, and per the syllabus it's the backbone the Lesson 22–24 capstone's Researcher → Analyst → Writer pipeline builds on directly.

## Prerequisites

Lessons 10–11 — this lesson assumes the graph/executor model and `InProcessExecution.RunAsync` from Lesson 10. Lessons 10–11 drew every edge by hand with `WorkflowBuilder.AddEdge`; this lesson hands a list of agents to `AgentWorkflowBuilder`, which draws the graph for you.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model — this lesson uses its agent-orchestration builders specifically |
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

## Core Concepts

- **`AgentWorkflowBuilder.BuildSequential` builds the agent graph for you.** `AgentWorkflowBuilder.BuildSequential([outlineAgent, draftAgent, editorAgent])` takes an ordered list of `AIAgent`s and returns a fully-wired `Workflow`: one node per agent, with an edge from each agent to the next. It also forwards each agent's messages along those edges and sends the `TurnToken` (Lesson 10) that tells each agent node to run, so there's no `WorkflowBuilder`, `AddEdge` or `TurnToken` in your code. You just supply the agents in order.
- **The workflow's output is the full conversation transcript, not a single string.** Running the pipeline produces a `WorkflowOutputEvent` whose `.Data` is a `List<ChatMessage>` — the original input plus every agent's response in order, each tagged with `.AuthorName` matching the producing agent's `Name`. This lesson prints the whole list to make each stage's contribution visible.
- **`chainOnlyAgentResponses` controls both what each agent sees and what the final transcript contains.** With the default (`false`), each agent receives the entire running transcript so far — `OutlineAgent` sees 1 message (the topic), `DraftAgent` sees 2 (topic + outline), `EditorAgent` sees 3 (topic + outline + draft) — and the final output preserves all four messages, including the original topic. With `chainOnlyAgentResponses: true`, every agent receives exactly **one** message: only the immediately preceding agent's reply. `DraftAgent` never sees the original topic directly, only `OutlineAgent`'s outline; `EditorAgent` never sees the topic or the outline, only `DraftAgent`'s draft. The final transcript shrinks to a single message — just `EditorAgent`'s answer.
- **Choosing between the two is a real design decision, not a style preference.** Full-transcript chaining (the default) gives every downstream agent — and whoever reads the final output — complete visibility into how the answer was built, at the cost of a growing context each agent has to process. `chainOnlyAgentResponses: true` produces a lean, single-answer result and forces strict "assembly line" isolation between stages, which matters when an earlier stage's reasoning shouldn't leak into a later one's context (or when you only want the final, polished answer, not the intermediate scaffolding).
- **The same agent instances can be reused across multiple workflows.** Both runs in this lesson build their sequential workflow from the exact same three `ChatClientAgent` instances — unlike a `Workflow` object (which Lesson 10 established can only be owned by one runner at a time), an `AIAgent` carries no run-scoped ownership state, so it's safe to hand the same agents to `BuildSequential` more than once.

## Complete C# Code

**`src/SequentialPattern/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace SequentialPattern
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

**`src/SequentialPattern/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SequentialPattern
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

**`src/SequentialPattern/Program.cs`**

```csharp
// Lesson 12 - Sequential Pattern
// Chains three agents into a fixed blog-writing pipeline via AgentWorkflowBuilder.BuildSequential
// - each agent consumes what came before it and adds its own contribution. Runs the same
// three-agent pipeline twice: once with the default full-transcript chaining, once with
// chainOnlyAgentResponses set to true, to show how that one flag changes both what each agent
// can see and what the workflow's final output actually contains.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace SequentialPattern
{
    /// <summary>
    /// Entry point that builds a three-agent blog-writing pipeline and runs it twice against
    /// the same topic, contrasting the default chaining behavior with
    /// <c>chainOnlyAgentResponses: true</c>.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds three agents once, then chains them into two differently-configured
        /// sequential workflows and runs both against the same input topic.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent outlineAgent = AgentFactory.CreateAgent(
                chatClient, "OutlineAgent", "Write a concise 3-point outline for a blog post on the given topic.");
            ChatClientAgent draftAgent = AgentFactory.CreateAgent(
                chatClient, "DraftAgent", "Write a short two-sentence draft based on the outline you received.");
            ChatClientAgent editorAgent = AgentFactory.CreateAgent(
                chatClient, "EditorAgent", "Polish the draft you received into a single tight, punchy sentence.");

            const string topic = "sustainable travel";

            Console.WriteLine("=== Default chaining (each agent sees the full running transcript) ===");
            Workflow defaultWorkflow = AgentWorkflowBuilder.BuildSequential([outlineAgent, draftAgent, editorAgent]);
            await RunPipelineAsync(defaultWorkflow, topic);

            Console.WriteLine();
            Console.WriteLine("=== chainOnlyAgentResponses: true (each agent sees only the previous agent's reply) ===");
            Workflow chainOnlyWorkflow = AgentWorkflowBuilder.BuildSequential(
                chainOnlyAgentResponses: true, [outlineAgent, draftAgent, editorAgent]);
            await RunPipelineAsync(chainOnlyWorkflow, topic);
        }

        /// <summary>
        /// Runs a sequential agent workflow against a single topic and prints the resulting
        /// transcript, whatever its length turns out to be.
        /// </summary>
        private static async Task RunPipelineAsync(Workflow workflow, string topic)
        {
            Run result = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, topic));

            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> transcript)
                {
                    Console.WriteLine($"Final output contains {transcript.Count} message(s):");
                    foreach (ChatMessage message in transcript)
                    {
                        string author = string.IsNullOrEmpty(message.AuthorName) ? message.Role.ToString() : message.AuthorName;
                        Console.WriteLine($"  [{author}] {message.Text}");
                    }
                }
            }
        }
    }
}
```

## Walkthrough

1. **Three agents are built once, with no workflow-specific setup at all.** `outlineAgent`, `draftAgent`, and `editorAgent` are plain `ChatClientAgent`s built through the same `AgentFactory.CreateAgent` used across the series. Nothing about their construction hints that they're about to be chained — the pipeline is entirely a property of how they're later handed to `AgentWorkflowBuilder`.
2. **`BuildSequential` is the entire orchestration layer.** `AgentWorkflowBuilder.BuildSequential([outlineAgent, draftAgent, editorAgent])` is one call, taking the agents in the order they should run. Compare this to Lesson 10/11's `WorkflowBuilder` + repeated `.AddEdge(...)` calls — there's no graph to draw by hand here at all.
3. **The first run uses default chaining, and `RunPipelineAsync` prints the full transcript.** `InProcessExecution.RunAsync(defaultWorkflow, new ChatMessage(ChatRole.User, topic))` runs all three agents in turn; the resulting `WorkflowOutputEvent.Data` is a four-message `List<ChatMessage>` — the topic plus all three agents' contributions — which the loop prints one line per message, tagged by author.
4. **The second run reuses the exact same three agent objects, built with `chainOnlyAgentResponses: true`.** `AgentWorkflowBuilder.BuildSequential(chainOnlyAgentResponses: true, [outlineAgent, draftAgent, editorAgent])` produces a *different* `Workflow` from the *same* agents — proof that agents themselves carry no per-workflow ownership state, unlike the `Workflow` object itself.
5. **The printed output makes the behavioral difference impossible to miss.** Run 1 prints four messages, ending with a final sentence that's visibly a distillation of the outline and draft above it. Run 2 prints exactly one message — `EditorAgent`'s answer alone — because with `chainOnlyAgentResponses: true`, that's genuinely all the final transcript contains; the topic and the intermediate outline and draft never survive into the output at all.

## Expected Output

```
=== Default chaining (each agent sees the full running transcript) ===
Final output contains 4 message(s):
  [user] sustainable travel
  [OutlineAgent] Here's a concise 3-point outline for a blog post on sustainable travel:
...
  [DraftAgent] Sustainable travel involves making eco-conscious choices ...
  [EditorAgent] Sustainable travel prioritizes minimizing environmental impact, empowering local communities, and fostering respectful cultural exchange.

=== chainOnlyAgentResponses: true (each agent sees only the previous agent's reply) ===
Final output contains 1 message(s):
  [EditorAgent] Sustainable travel champions responsible environmental, social, and economic choices to preserve destinations and empower communities for future generations.
```

The exact wording from each agent will vary between runs — every one is a live model call — but the shape will not: the first run always ends with four transcript messages, the second always with exactly one.
