# Lesson 16 — Magentic-One Pattern

## Project Overview

A single lead `ManagerAgent` decomposes a user request into a plan, tracks a live progress ledger turn by turn, and decides which specialist — `ResearchAgent` or `WriterAgent` — should act next, until it judges the request satisfied and synthesizes the final answer itself. This is the most autonomous orchestration pattern in the series: nothing about who talks next, how many rounds happen, or when to stop is fixed by our code — the manager decides all of it at runtime.

## Prerequisites

Lessons 12–15 — this lesson assumes `AgentWorkflowBuilder`, `InProcessExecution.RunAsync`, and the `List<ChatMessage>` output shape introduced there. `MagenticWorkflowBuilder` is the fifth sibling builder from that family this series has used, and the one with the most significant departures yet from the shape of its siblings.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model — this lesson uses `AgentWorkflowBuilder.CreateMagenticBuilderWith` / `MagenticWorkflowBuilder` |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No new packages this lesson — the same packages as Lessons 10–15.

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

- **The manager is a plain agent, not a special type.** `ManagerAgent` in this lesson is built with the exact same `AgentFactory.CreateAgent` helper as every specialist — there is no distinct "manager" agent class. What makes it a manager is only that it's the one passed to `new MagenticWorkflowBuilder(managerAgent)` (equivalently, `AgentWorkflowBuilder.CreateMagenticBuilderWith(managerAgent)` — the two are interchangeable). Specialists are supplied separately: `.AddParticipants(IEnumerable<AIAgent> agents)`.
- **Two ledgers.** Magentic-One's design (and this package's implementation of it) runs on two internal artifacts, both surfaced as workflow events. A **task ledger** — a fact sheet plus a step-by-step plan — is produced once at the start of the run and surfaced as `MagenticPlanCreatedEvent.FullTaskLedger` (a `ChatMessage`). A **progress ledger** is then re-evaluated every round and surfaced as `MagenticProgressLedgerUpdatedEvent.ProgressLedger` (a `MagenticProgressLedger`), exposing `NextSpeaker`, `IsRequestSatisfied`, `IsInLoop`, `IsProgressBeingMade`, and `InstructionOrQuestion`. This is how the manager decides who talks next and when to stop, round after round, with no fixed graph edges telling it what comes next.
- **The final output is a single message, not the full transcript — a departure from every prior pattern.** Sequential, Concurrent, Handoff, and Group Chat all returned a `List<ChatMessage>` containing the *entire* multi-agent conversation. Magentic's `WorkflowOutputEvent.Data` is also a `List<ChatMessage>`, but it contains **exactly one message**, authored by the manager agent itself, synthesizing whatever the specialists produced into one final answer. The specialists' raw contributions never appear in the returned list — only in the intermediate events, if you choose to observe them.
- **`RequirePlanSignoff` defaults to `true` — a deliberate governance-by-default choice.** Building a `MagenticWorkflowBuilder` without calling `.RequirePlanSignoff(false)` causes the run to pause immediately after the plan is created: it emits a `RequestInfoEvent` (whose `.Request` is an `ExternalRequest` wrapping a `MagenticPlanReviewRequest`) and `InProcessExecution.RunAsync` returns with the workflow still suspended, not finished. The run does not hang; it simply stops emitting further events until resumed. Resuming means building an `ExternalResponse` from `MagenticPlanReviewRequest.Approve()` or `.Revise(...)` and calling `Run.ResumeAsync(IEnumerable<ExternalResponse>, ct)`. This is the same generic external-request/response port (`RequestInfoEvent`/`ExternalRequest`/`ExternalResponse`) that Lesson 17 (Human-in-the-Loop) formalizes at the workflow level — distinct from Lesson 9's agent-level `ToolApprovalRequestContent`. This lesson calls `.RequirePlanSignoff(false)` to keep the demo unattended; a production use of Magentic-One would very plausibly leave the default `true` in place and wire the plan-review pause into an approval step.
- **Round/reset limits are unbounded by default; only the stall limit has a sane default.** `WithMaxRounds(int? maxRounds)` and `WithMaxResets(int? maxResets)` are unlimited unless you set them (`null` means unlimited), while `WithMaxStalls(int maxStalls = 3)` defaults to 3. An unbounded `MaxRounds` on a genuinely autonomous, re-planning orchestrator is a real runaway-cost risk — the same category of gotcha as Lesson 15's `MaximumIterationCount` defaulting to 40, just with no cap at all here instead of a generous one. This lesson sets `WithMaxRounds(8)` and `WithMaxStalls(2)` explicitly.
- **Be careful giving a Magentic specialist agent tools.** Because Magentic shares one running chat history across the whole team, a specialist's `FunctionCallContent`/`FunctionResultContent` turns end up replayed into *other* agents' turns too. Model APIs validate the order of tool-call and tool-result turns, and a history in which one agent's tool calls show up inside another agent's turn can be rejected outright — the request fails rather than the model quietly ignoring the stray turns. How strictly this is enforced differs between providers (and therefore between `MAF_LLM` settings). This lesson's specialists are deliberately tool-free to keep the focus on planning and delegation; if your own Magentic team needs specialist tools, test the combination against the provider and model you actually deploy on before relying on it.

## Complete C# Code

**`src/MagenticOnePattern/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace MagenticOnePattern
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

**`src/MagenticOnePattern/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace MagenticOnePattern
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

**`src/MagenticOnePattern/Program.cs`**

```csharp
// Lesson 16 - Magentic-One Pattern
// A lead ManagerAgent decomposes a user request into a plan, tracks a progress ledger turn by
// turn, and delegates each step to whichever specialist agent (ResearchAgent, WriterAgent) it
// decides should act next - the most autonomous orchestration pattern in the series. Unlike every
// prior AgentWorkflowBuilder pattern (Sequential, Concurrent, Handoff, Group Chat), the manager
// itself both plans the work AND synthesizes the single final answer, rather than the workflow's
// output being the raw multi-agent transcript.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;

namespace MagenticOnePattern
{
    /// <summary>
    /// Entry point that builds a manager agent and two specialists, wires them into a Magentic-One
    /// workflow, and runs a single research-and-write request through it while narrating the
    /// manager's plan and turn-by-turn progress ledger as the run proceeds.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds ManagerAgent, ResearchAgent, and WriterAgent, wires them into a Magentic-One
        /// workflow with an explicit round cap, and runs one request through it end to end.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent managerAgent = AgentFactory.CreateAgent(
                chatClient, "ManagerAgent",
                "You are the orchestrator that plans and delegates tasks to your team.");

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient, "ResearchAgent",
                "You are a trivia researcher. When asked, list exactly two little-known, " +
                    "verifiable facts about the given topic as a short bullet list. Never write " +
                    "prose paragraphs, only bullets.");

            ChatClientAgent writerAgent = AgentFactory.CreateAgent(
                chatClient, "WriterAgent",
                "You are a copywriter. When given a list of facts, weave them into exactly one " +
                    "short, engaging prose paragraph. Never produce bullet lists, only flowing prose.");

            List<AIAgent> team = [researchAgent, writerAgent];

            // RequirePlanSignoff defaults to true - the workflow would otherwise pause after the
            // plan is created and wait for an external approval response before delegating any
            // work. That pause/resume mechanism (RequestInfoEvent -> ExternalRequest/ExternalResponse
            // -> Run.ResumeAsync) is the same generic port Lesson 17 (Human-in-the-Loop) formalizes,
            // so it is disabled here to keep this lesson focused on the planning/delegation loop.
            Workflow workflow = new MagenticWorkflowBuilder(managerAgent)
                .AddParticipants(team)
                .WithMaxRounds(8)
                .WithMaxStalls(2)
                .RequirePlanSignoff(false)
                .Build();

            Console.WriteLine("=== Magentic-One: manager plans, delegates, and synthesizes the final answer ===");
            const string request = "Get two little-known facts about the planet Saturn from the " +
                "researcher, then have the writer turn them into one short engaging paragraph.";
            Console.WriteLine($"User: {request}");
            Console.WriteLine();

            Run run = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, request));

            foreach (WorkflowEvent evt in run.NewEvents)
            {
                switch (evt)
                {
                    case MagenticPlanCreatedEvent planEvent:
                        Console.WriteLine("--- Manager's fact sheet and plan ---");
                        Console.WriteLine(planEvent.FullTaskLedger.Text);
                        Console.WriteLine();
                        break;

                    case MagenticProgressLedgerUpdatedEvent ledgerEvent:
                        MagenticProgressLedger ledger = ledgerEvent.ProgressLedger;
                        Console.WriteLine(
                            $"--- Progress ledger: next speaker = {ledger.NextSpeaker ?? "(none)"}, " +
                            $"satisfied = {ledger.IsRequestSatisfied} ---");
                        Console.WriteLine($"    {ledger.InstructionOrQuestion}");
                        Console.WriteLine();
                        break;

                    case WorkflowOutputEvent outputEvent when outputEvent.Data is List<ChatMessage> finalMessages:
                        Console.WriteLine("--- Final answer ---");
                        foreach (ChatMessage message in finalMessages)
                        {
                            string author = string.IsNullOrEmpty(message.AuthorName) ? message.Role.ToString() : message.AuthorName;
                            Console.WriteLine($"  [{author}] {message.Text}");
                        }
                        break;
                }
            }
        }
    }
}
```

## Walkthrough

1. **Three agents, one factory, no special "manager" type.** `managerAgent`, `researchAgent`, and `writerAgent` are all built with the identical `AgentFactory.CreateAgent` call used in every prior lesson. `researchAgent` and `writerAgent` are given deliberately narrow, mutually exclusive personas (bullets only vs. prose only) so the eventual transcript makes the hand-off between them obvious.
2. **The manager is wired in via the constructor, specialists via `.AddParticipants(...)`.** `new MagenticWorkflowBuilder(managerAgent)` establishes who plans and synthesizes; `team` (just the two specialists, not the manager) is supplied separately — a similar two-step wiring shape to Lesson 15's Group Chat builder, but here the first argument is a single fixed agent rather than a factory function.
3. **`.WithMaxRounds(8)` and `.WithMaxStalls(2)` bound an otherwise-unbounded loop.** Without an explicit `WithMaxRounds`, this orchestrator will keep re-planning and delegating indefinitely if it judges progress is being made; capping it is the Lesson-16 equivalent of Lesson 15's explicit `MaximumIterationCount`.
4. **`.RequirePlanSignoff(false)` disables the default pause-for-approval behavior.** Left at its default (`true`), the run would stop after printing the plan and never reach the delegation rounds at all in this lesson's unattended form — see the Core Concepts callout for what resuming that pause actually involves.
5. **A single `RunAsync` call drives the entire plan → delegate → re-assess → synthesize loop.** Exactly like every prior `AgentWorkflowBuilder` pattern, one call to `InProcessExecution.RunAsync` runs the whole multi-round conversation internally; the caller only sees the resulting event stream once it's all done.
6. **The event loop narrates three distinct moments, not the full turn-by-turn chatter.** `MagenticPlanCreatedEvent` fires once, right after the manager produces its fact sheet and plan. `MagenticProgressLedgerUpdatedEvent` fires every round after that, showing who the manager wants to speak next and whether it considers the request satisfied. `WorkflowOutputEvent` fires once, at the very end, carrying the single synthesized answer — not the specialists' raw bullets or prose.

## Expected Output

```
=== Magentic-One: manager plans, delegates, and synthesizes the final answer ===
User: Get two little-known facts about the planet Saturn from the researcher, then have the writer turn them into one short engaging paragraph.

--- Manager's fact sheet and plan ---
We are working to address the following user request:

Get two little-known facts about the planet Saturn from the researcher, then have the writer turn them into one short engaging paragraph.

To answer this request we have assembled the following team:

- ResearchAgent:
- WriterAgent:

Here is an initial fact sheet to consider:

Here is the pre-survey:

### 1. GIVEN OR VERIFIED FACTS
*   The subject of the facts is the planet Saturn.
*   The quantity of facts to be obtained is two.
*   The nature of the facts must be "little-known".
*   The final output will be one short engaging paragraph.

### 2. FACTS TO LOOK UP
*   Two specific "little-known" facts about the planet Saturn.
*   Where specifically: reputable astronomical databases, academic journals, and official space agency websites.

### 3. FACTS TO DERIVE
*   None. The request involves retrieval and synthesis of existing information.

### 4. EDUCATED GUESSES
*   The term "little-known" likely refers to facts not commonly known by the general public.
*   The "engaging" requirement suggests the chosen facts should have an inherent intrigue.

Here is the plan to follow as best as possible:

Here is the plan:

*   ResearchAgent: Find two distinct, little-known facts about the planet Saturn.
*   WriterAgent: Using the two facts provided by the ResearchAgent, craft one short, engaging paragraph.

--- Progress ledger: next speaker = ResearchAgent, satisfied = False ---
    Please provide two distinct, little-known facts about the planet Saturn.

--- Progress ledger: next speaker = WriterAgent, satisfied = False ---
    Please craft one short, engaging paragraph using the two facts just provided about Saturn.

--- Progress ledger: next speaker = N/A, satisfied = True ---
    The request has been successfully completed. No further instructions are needed.

--- Final answer ---
  [ManagerAgent] Here is the short, engaging paragraph about Saturn, featuring two little-known facts:

Saturn truly stands out in our solar system, not just for its iconic rings but for its profound mysteries, including an enormous, persistent hexagonal storm at its north pole with sides each wider than Earth – a unique geometric weather pattern seen nowhere else. Adding to its enigmatic nature, the gas giant also boasts a magnetic field almost perfectly aligned with its rotational axis, with a tilt of less than 0.01 degrees, leaving scientists puzzling over the mechanism of its internal dynamo.
```

The exact wording of the fact sheet, plan, progress-ledger instructions, and final paragraph will all vary between runs — every step is a live model call, and the manager's own judgment decides how many rounds it takes. What will not vary: exactly one `MagenticPlanCreatedEvent`, one or more `MagenticProgressLedgerUpdatedEvent`s ending in `satisfied = True`, and a final answer authored solely by `ManagerAgent`.
