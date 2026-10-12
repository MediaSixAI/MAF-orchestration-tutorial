# Lesson 14 — Handoff Pattern

## Project Overview

A `TriageAgent` decides, per request, whether to hand off to `BillingAgent` or `TechSupportAgent` — there is no fixed graph deciding which specialist runs. Unlike Lesson 12's sequential pipeline or Lesson 13's concurrent fan-out, where the shape of execution is fixed in advance, this lesson's routing decision is made by the triage agent's own judgment about what the user actually asked, at run time.

## Prerequisites

Lessons 12–13 — this lesson assumes `AgentWorkflowBuilder`, `InProcessExecution.RunAsync`, and the `List<ChatMessage>` transcript output shape introduced there. It uses a third sibling builder from the same class, following the pattern that's now held for three lessons running: check `AgentWorkflowBuilder` for a purpose-built method before reaching for manual `WorkflowBuilder` graph construction.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model — this lesson uses `AgentWorkflowBuilder.CreateHandoffBuilderWith` specifically |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No new packages this lesson — `AgentWorkflowBuilder.CreateHandoffBuilderWith` is a sibling of Lesson 12's `BuildSequential` and Lesson 13's `BuildConcurrent`, in the same already-referenced package.

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

- **A handoff is a tool call, not a graph edge the framework picks for you.** `AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent).WithHandoff(triageAgent, billingAgent, "reason").WithHandoff(triageAgent, techSupportAgent, "reason").Build()` gives `TriageAgent` two *possible* routes, each described by a reason string. Under the hood, each route becomes something `TriageAgent`'s underlying model can call like any other tool — you can see it in this lesson's transcript, where the triage agent's turn contains a `FunctionCallContent` instead of ordinary text. The model itself decides which one (if any) to invoke, based on the reason strings and the actual conversation.
- **The reason string is the whole routing mechanism.** `"The user has a billing, payment, or subscription question."` and `"The user has a technical problem or bug with the product."` aren't documentation — they're what the model reads to decide which handoff applies. This lesson's two test prompts route correctly specifically because each reason string clearly matches one request and not the other; vague or overlapping reasons would make the routing decision less reliable.
- **The receiving specialist picks up the same transcript, not a fresh conversation.** `BillingAgent` and `TechSupportAgent` never see `TriageAgent`'s system instructions, but they do see the original user message and the handoff having happened — the final transcript is one continuous `List<ChatMessage>`, the same shape Lesson 12 and Lesson 13 both produced, just with a different agent contributing partway through instead of at a fixed position.
- **This is fundamentally different from Lesson 12's sequential chain.** `BuildSequential` runs every agent, in the order you gave it, every time. This lesson's `TriageAgent` might route to `BillingAgent`, or to `TechSupportAgent`, or — if neither reason matches — potentially neither. Which path executes is a property of the *conversation*, decided fresh on each run, not a property of the workflow definition.
- **`HandoffWorkflowBuilderCore<TBuilder>` has more capability than this lesson exercises.** `EnableReturnToPrevious()` lets a specialist hand the conversation back to whichever agent handed off to it (useful when a specialist realizes a request is outside its scope after all); `WithAutonomousMode(...)` and `WithTerminationCondition(...)` support open-ended multi-hop handoff chains with turn limits instead of a single decision point; `WithToolCallFilteringBehavior(...)` controls how much of the handoff machinery's own tool-call noise a receiving agent sees. This lesson sticks to the simplest, most directly verified shape — one triage point, two possible destinations — since that's the core of what "Handoff Pattern" means in the syllabus.

## Complete C# Code

**`src/HandoffPattern/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HandoffPattern
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

**`src/HandoffPattern/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HandoffPattern
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

**`src/HandoffPattern/Program.cs`**

```csharp
// Lesson 14 - Handoff Pattern
// A TriageAgent decides, per request, whether to hand off to BillingAgent or TechSupportAgent -
// there is no fixed graph deciding which specialist runs; the triage agent's own judgment about
// the request's content picks the path, mid-conversation, at run time.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HandoffPattern
{
    /// <summary>
    /// Entry point that builds a triage agent and two specialists, wires up handoff routes
    /// between them, and runs two different requests to show the triage agent choosing a
    /// different specialist each time based on what was actually asked.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds the three agents once, then runs a billing request and a technical support
        /// request through independently-built handoff workflows.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent triageAgent = AgentFactory.CreateAgent(
                chatClient,
                "TriageAgent",
                "You are the first point of contact for customer support. Decide whether the " +
                    "user's request is a billing question or a technical support question, and " +
                    "hand off to the correct specialist immediately. Do not try to answer billing " +
                    "or technical questions yourself.");
            ChatClientAgent billingAgent = AgentFactory.CreateAgent(
                chatClient,
                "BillingAgent",
                "You handle billing questions. Answer the user's question directly and briefly, " +
                    "as if you are speaking straight to them.");
            ChatClientAgent techSupportAgent = AgentFactory.CreateAgent(
                chatClient,
                "TechSupportAgent",
                "You handle technical support questions. Answer the user's question directly and " +
                    "briefly, as if you are speaking straight to them.");

            await RunHandoffScenarioAsync(
                triageAgent, billingAgent, techSupportAgent,
                "Billing question", "I was charged twice for my subscription this month, can you help?");

            Console.WriteLine();

            await RunHandoffScenarioAsync(
                triageAgent, billingAgent, techSupportAgent,
                "Tech support question", "My app keeps crashing when I open the settings screen.");
        }

        /// <summary>
        /// Builds a fresh handoff workflow from the given agents, declaring which specialists
        /// the triage agent may hand off to and why, then runs a single prompt through it and
        /// prints the full transcript, flagging the message where the handoff decision happens.
        /// </summary>
        private static async Task RunHandoffScenarioAsync(
            ChatClientAgent triageAgent,
            ChatClientAgent billingAgent,
            ChatClientAgent techSupportAgent,
            string label,
            string prompt)
        {
            Workflow workflow = AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)
                .WithHandoff(triageAgent, billingAgent, "The user has a billing, payment, or subscription question.")
                .WithHandoff(triageAgent, techSupportAgent, "The user has a technical problem or bug with the product.")
                .Build();

            Console.WriteLine($"=== {label} ===");
            Console.WriteLine($"User: {prompt}");

            Run result = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, prompt));
            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> transcript)
                {
                    foreach (ChatMessage message in transcript)
                    {
                        string author = string.IsNullOrEmpty(message.AuthorName) ? message.Role.ToString() : message.AuthorName;
                        bool isHandoffDecision = message.Contents.Any(content => content is FunctionCallContent);
                        string marker = isHandoffDecision ? " (handoff decision)" : "";
                        if (!string.IsNullOrWhiteSpace(message.Text) || isHandoffDecision)
                        {
                            Console.WriteLine($"  [{author}]{marker} {message.Text}");
                        }
                    }
                }
            }
        }
    }
}
```

## Walkthrough

1. **Three agents are built once, and none of them is told about the others' existence.** `triageAgent`, `billingAgent`, and `techSupportAgent` are three ordinary `ChatClientAgent`s. Nothing in their own instructions describes the handoff mechanism — the routing knowledge lives entirely in how they're wired together afterward, not in any agent's persona.
2. **`WithHandoff` is called once per possible destination, each with its own reason.** `AgentWorkflowBuilder.CreateHandoffBuilderWith(triageAgent)` starts the builder from the triage agent; each `.WithHandoff(triageAgent, target, reason)` call adds one possible route out of it. Two calls here mean `TriageAgent` has exactly two options to choose between on any given turn.
3. **A fresh workflow is built for each scenario, reusing the same three agent objects.** `RunHandoffScenarioAsync` calls `.Build()` inside itself, so each scenario gets its own `Workflow` instance — consistent with Lesson 12/13's rule that a `Workflow` is single-use per runner, while the underlying `AIAgent` instances are safe to reuse.
4. **The transcript printout marks the exact message where the routing decision happens.** `message.Contents.Any(content => content is FunctionCallContent)` detects `TriageAgent`'s turn where it calls the handoff tool instead of producing ordinary text — this is the concrete, inspectable evidence that "dynamic delegation" in this lesson's title is implemented as a tool call the model chooses to make, not a hidden framework decision.
5. **The two scenarios prove the routing is content-driven, not fixed.** The billing prompt and the tech-support prompt are handled by the *same* `TriageAgent`, wired to the *same* two possible destinations, and each one correctly reaches a different specialist — the only thing that changed between runs was what the user actually asked.

## Expected Output

```
=== Billing question ===
User: I was charged twice for my subscription this month, can you help?
  [user] I was charged twice for my subscription this month, can you help?
  [TriageAgent] (handoff decision)
  [BillingAgent] It sounds like you were double-charged for your subscription. I can definitely help you with that.

=== Tech support question ===
User: My app keeps crashing when I open the settings screen.
  [user] My app keeps crashing when I open the settings screen.
  [TriageAgent] (handoff decision)
  [TechSupportAgent] Okay, I understand. Your app is crashing when you try to open the settings screen.
```

The specialists' exact wording will vary between runs — both are live model calls — but the shape will not: `TriageAgent` always produces a `(handoff decision)` turn with no text, and the very next assistant message always comes from the specialist that actually matches the question asked.
