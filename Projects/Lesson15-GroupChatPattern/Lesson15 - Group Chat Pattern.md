# Lesson 15 — Group Chat Pattern

## Project Overview

Three role-specialized agents — `ProductAgent`, `EngineeringAgent`, `DesignAgent` — discuss a single feature question in a shared conversational context, each seeing every prior contribution before adding its own perspective. This is different from every other orchestration pattern so far: Lesson 12's sequential chain and Lesson 14's handoff both eventually settle on one agent's answer, and Lesson 13's concurrent pattern keeps agents from seeing each other at all — group chat is the pattern where multiple agents genuinely converse.

## Prerequisites

Lessons 12–14 — this lesson assumes `AgentWorkflowBuilder`, `InProcessExecution.RunAsync`, and the `List<ChatMessage>` transcript output shape introduced there. `AgentWorkflowBuilder.CreateGroupChatBuilderWith` is the fourth sibling builder from that class this series has used — the pattern of checking there first before reaching for manual `WorkflowBuilder` graph construction has now held for four lessons running.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model — this lesson uses `AgentWorkflowBuilder.CreateGroupChatBuilderWith` and `RoundRobinGroupChatManager` |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No new packages this lesson — the same packages as Lessons 10–14.

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

- **Group chat has a genuinely different builder shape than every prior pattern.** `CreateHandoffBuilderWith` takes a single agent; `BuildSequential`/`BuildConcurrent` take a plain list of agents. `CreateGroupChatBuilderWith` takes neither — its only parameter is `Func<IReadOnlyList<AIAgent>, GroupChatManager> managerFactory`, a **factory function**. Participants are supplied separately, via `.AddParticipants(agents)` on the builder it returns; the factory receives that same participant list and is responsible for producing whatever `GroupChatManager` will decide the speaking order.
- **`GroupChatManager` is the abstraction for "who talks next," and it's pluggable.** Its one abstract method is `SelectNextAgentAsync(IReadOnlyList<ChatMessage> history, CancellationToken) -> ValueTask<AIAgent>` — given everything said so far, pick the next speaker. `RoundRobinGroupChatManager`, shipped in the package, is the simplest possible implementation: cycle through the participant list in the order it was given, one turn each.
- **The manager also decides when the conversation stops.** `GroupChatManager.MaximumIterationCount` defaults to a generous **40** iterations (one agent turn each), which would let a small demo run far longer than intended. This lesson sets `MaximumIterationCount = participants.Count` explicitly, producing exactly one round-robin round: one turn per agent, then done.
- **Every agent sees the entire conversation before it speaks, and the final output is a growing shared transcript.** By the time `DesignAgent` takes its turn, it has the original question, `ProductAgent`'s point, and `EngineeringAgent`'s point all in context — none of Lesson 13's isolation, none of Lesson 14's single-branch routing. The final `WorkflowOutputEvent.Data` is a `List<ChatMessage>` — the same shape every prior orchestration lesson produced — with the original question plus one message per participant, in speaking order.
- **`RoundRobinGroupChatManager`'s constructor takes an optional custom termination function, not just the iteration cap.** `new RoundRobinGroupChatManager(agents, shouldTerminateFunc)` accepts a `Func<RoundRobinGroupChatManager, IEnumerable<ChatMessage>, CancellationToken, ValueTask<bool>>` for stopping based on the conversation's actual content rather than a fixed turn count — not needed for this lesson's bounded, one-round demo, but the hook exists for a group chat that should end once, say, the group reaches agreement.
- **Writing a custom `GroupChatManager` is the natural next step past round-robin.** Because `SelectNextAgentAsync` is the only abstract member, a subclass could pick the next speaker based on the conversation's content (e.g., "whoever hasn't spoken yet and is most relevant") instead of a fixed rotation — round-robin is the simplest correct implementation, not the only one.

## Complete C# Code

**`src/GroupChatPattern/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace GroupChatPattern
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

**`src/GroupChatPattern/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace GroupChatPattern
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

**`src/GroupChatPattern/Program.cs`**

```csharp
// Lesson 15 - Group Chat Pattern
// Three agents - Product, Engineering, and Design - discuss a single feature question in a
// shared conversational context via AgentWorkflowBuilder.CreateGroupChatBuilderWith and a
// RoundRobinGroupChatManager. Each agent sees every prior contribution before speaking and adds
// its own role-based perspective, unlike Lesson 14's handoff (only one agent ever responds) or
// Lesson 13's concurrent pattern (agents never see each other's output).

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace GroupChatPattern
{
    /// <summary>
    /// Entry point that builds three role-specialized agents, wires them into a round-robin
    /// group chat, and runs a single feature question through it.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds Product, Engineering, and Design agents, chains them into a three-turn
        /// round-robin group chat, and prints the resulting shared transcript.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent productAgent = AgentFactory.CreateAgent(
                chatClient, "ProductAgent",
                "You represent the product perspective. Contribute one short point about user " +
                    "value for the feature being discussed. Keep it to one sentence.");
            ChatClientAgent engineeringAgent = AgentFactory.CreateAgent(
                chatClient, "EngineeringAgent",
                "You represent the engineering perspective. Contribute one short point about " +
                    "feasibility or implementation for the feature being discussed. Keep it to one sentence.");
            ChatClientAgent designAgent = AgentFactory.CreateAgent(
                chatClient, "DesignAgent",
                "You represent the design perspective. Contribute one short point about user " +
                    "experience for the feature being discussed. Keep it to one sentence.");

            List<AIAgent> participants = [productAgent, engineeringAgent, designAgent];

            Workflow workflow = AgentWorkflowBuilder
                .CreateGroupChatBuilderWith(agents => new RoundRobinGroupChatManager(agents)
                {
                    MaximumIterationCount = participants.Count,
                })
                .AddParticipants(participants)
                .Build();

            Console.WriteLine("=== Group chat: one round-robin round, three role-based perspectives ===");
            const string question = "Should we add a dark mode toggle to the app?";
            Console.WriteLine($"User: {question}");
            Console.WriteLine();

            Run result = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, question));
            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> transcript)
                {
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

1. **Three agents are built with distinct, non-overlapping roles.** `productAgent`, `engineeringAgent`, and `designAgent` each get instructions constraining them to one specific lens on the same question — this is what makes the eventual transcript read like three genuinely different perspectives rather than three similar-sounding answers.
2. **The manager factory is where `RoundRobinGroupChatManager` gets constructed from the participant list the builder will supply.** `agents => new RoundRobinGroupChatManager(agents) { MaximumIterationCount = participants.Count }` — the lambda parameter `agents` is the `IReadOnlyList<AIAgent>` the framework passes in once it knows the full participant list from `.AddParticipants(...)`, and `participants.Count` (3) caps the conversation at exactly one turn per agent.
3. **`.AddParticipants(participants)` is what actually tells the builder who's in the conversation.** Unlike `BuildSequential`/`BuildConcurrent`, where the agent list was the direct argument to the build call, here it's supplied as a separate step — the manager factory and the participant list are configured independently, then wired together internally when `.Build()` runs.
4. **A single `RunAsync` call drives the entire multi-turn conversation.** Unlike a hand-rolled loop that would call each agent in turn yourself, `InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, question))` runs all three turns internally, governed by the `RoundRobinGroupChatManager`'s own `SelectNextAgentAsync` and `ShouldTerminateAsync` logic, and returns only once the conversation has actually stopped.
5. **The printed transcript shows the round-robin order directly.** The final `List<ChatMessage>` lists `ProductAgent`, then `EngineeringAgent`, then `DesignAgent` — the exact order `participants` was constructed in — because that's what "round robin" means: the manager cycles through the list it was given, in the order it was given.

## Expected Output

```
=== Group chat: one round-robin round, three role-based perspectives ===
User: Should we add a dark mode toggle to the app?

  [user] Should we add a dark mode toggle to the app?
  [ProductAgent] Dark mode offers users a more comfortable viewing experience and can reduce eye strain, especially in low-light conditions.
  [EngineeringAgent] Implementing dark mode will require a comprehensive theming strategy to ensure consistent styling across all UI components and assets.
  [DesignAgent] A dark mode option provides users with a personalized viewing experience that reduces eye strain and improves focus, particularly in low-light settings.
```

The exact wording from each agent will vary between runs — every contribution is a live model call — but the shape will not: the transcript will always contain exactly four messages, and the three agent contributions will always appear in the same round-robin order the participants were added in.
