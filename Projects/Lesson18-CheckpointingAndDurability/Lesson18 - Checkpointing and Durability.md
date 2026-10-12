# Lesson 18 — Checkpointing & Durability

## Project Overview

Lesson 16's Magentic-One workflow, with its plan-review pause turned on, now survives a full simulated process restart: the run, its workflow graph, and every agent object are discarded entirely, and a brand-new process rebuilds them from nothing but a session id and a checkpoint store on disk. This is what makes Lesson 17's "pause for a human" story credible outside a single console run — a real approval might take hours or days, long after the process that first asked has exited.

## Prerequisites

Lessons 16–17 — this lesson assumes the Magentic-One workflow shape from Lesson 16 and the `RequestInfoEvent`/`ExternalRequest`/`ExternalResponse`/`Run.ResumeAsync` pause/resume mechanism Lesson 17 teaches with its in-graph `RequestPort`. Lesson 17's Core Concepts shows that `RequirePlanSignoff(true)` pauses a Magentic run through that same mechanism; this lesson turns it on, pauses its own run there, and checkpoints that paused run. Durability is added on top of the mechanism rather than introducing a new one.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model — this lesson uses `CheckpointManager`, `FileSystemJsonCheckpointStore`, `InProcessExecution.ResumeAsync`, and `ChatClientAgentOptions.Id` |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No new packages this lesson — the same packages as Lessons 10–17.

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

**This lesson writes real files to disk.** It creates (and clears, on each run, for a reproducible demo) a `checkpoints` folder next to the built executable. Open it after running — `index.jsonl` and the per-checkpoint `.json` files there are genuinely inspectable, not an abstraction.

## Core Concepts

- **Checkpointing is opt-in per run, and once enabled it's automatic.** Passing a `CheckpointManager` and a `sessionId` into `InProcessExecution.RunAsync(workflow, input, checkpointManager, sessionId, ct)` is the entire "turn it on" step. From then on, a checkpoint is captured after every superstep with no further code required — one full paused-then-approved run of this lesson leaves several checkpoint files in the `checkpoints` folder, not just one at the pause point. `Run` (and `StreamingRun`) both derive from `CheckpointableRunBase`, exposing `.IsCheckpointingEnabled`, `.Checkpoints` (the full list so far), and `.LastCheckpoint`.
- **`CheckpointManager` is a storage abstraction, and the shipped file-system store is real, not a toy.** `CheckpointManager.CreateInMemory()` (or the `.Default` static instance) keeps checkpoints in memory only — fine for a single process, useless across a restart. `CheckpointManager.CreateJson(ICheckpointStore<JsonElement> store, JsonSerializerOptions? customOptions)` paired with the shipped **`FileSystemJsonCheckpointStore(DirectoryInfo directory)`** persists to disk. Each checkpoint is a `.json` file capturing the workflow's executor graph and its state at that superstep, and an append-only `index.jsonl` manifest records every checkpoint's id and its `parentCheckpointId`, forming a real linked history per session — exactly the shape you'd expect from something meant to survive a restart, not an in-memory illusion of one.
- **The session id is the durable handle — not the `Run` object, not the `CheckpointInfo` object.** `sessionId` is a plain string you choose (this lesson uses a fixed one, `"saturn-report-session"`, standing in for a real application's own business key). `CheckpointManager.GetLatestCheckpointAsync(sessionId)` finds the latest checkpoint from nothing but that string — no reference to the original `Run`, `Workflow`, or agents is needed, which is exactly the point: a real application would persist only the session id (in a database row, a queue message) and look everything else up after a restart.
- **`InProcessExecution.ResumeAsync(workflow, fromCheckpoint, checkpointManager, ct) -> Run` is the restart mechanic.** A completely fresh `Workflow` — built by calling `MagenticWorkflowFactory.Build(...)` a second, independent time with brand-new `ChatClientAgent` instances — was handed a `CheckpointInfo` looked up purely from the session id, and the resulting restored `Run` correctly re-surfaced the *exact same* pending plan-review request the run paused on. Approving it drove the run to the same kind of completion as any other lesson, with no connection whatsoever to the original process's objects.
- **A restorable workflow is built from agents with stable ids.** A checkpoint records each agent's executor id, which is `"{AgentName}_{AgentId}"` (for example `"ResearchAgent_research-agent"`), and restoring checks that the rebuilt workflow has the same executors. So every agent in a workflow you intend to restore gets an explicit `ChatClientAgentOptions.Id` — `"manager-agent"`, `"research-agent"`, `"writer-agent"` — that is identical every time the workflow is built, restart or not. If no id is given, `ChatClientAgent` generates a new one on every construction, and a checkpoint taken from one build would not match the next: `ResumeAsync` throws `InvalidDataException` ("The specified checkpoint is not compatible with the workflow associated with this runner"). This lesson's `AgentFactory.CreateAgent` takes an `id` parameter and builds each agent through the `(IChatClient, ChatClientAgentOptions, ...)` constructor.
- **Checkpointing saves state; it does not hand you a complete durability story for free.** Worth stating plainly: this mechanism persists *what the workflow already knew*, but deciding *when* to attempt a restore, detecting that a restore is even needed, and guarding against restoring (and re-running any side effects from) the same checkpoint twice are still the calling application's responsibility — the framework doesn't watch for crashes or dedupe retries on your behalf.

## Complete C# Code

**`src/CheckpointingAndDurability/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CheckpointingAndDurability
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

**`src/CheckpointingAndDurability/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CheckpointingAndDurability
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>. Unlike prior lessons' AgentFactory,
    /// this one requires an explicit, caller-chosen agent id rather than letting one be
    /// auto-generated - see this lesson's Core Concepts section for why that distinction is the
    /// whole point of a checkpoint surviving a rebuilt workflow graph.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a stable id, a
        /// display name, and system instructions that define its persona. The id must be the
        /// same every time this agent is reconstructed - including after a simulated process
        /// restart - for a saved checkpoint to correctly reattach to it.
        /// </summary>
        internal static ChatClientAgent CreateAgent(IChatClient chatClient, string id, string name, string instructions)
        {
            return new ChatClientAgent(chatClient, new ChatClientAgentOptions
            {
                Id = id,
                Name = name,
                ChatOptions = new ChatOptions { Instructions = instructions },
            });
        }
    }
}
```

**`src/CheckpointingAndDurability/MagenticWorkflowFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CheckpointingAndDurability
{
    /// <summary>
    /// Builds the ManagerAgent/ResearchAgent/WriterAgent Magentic-One workflow used by this
    /// lesson. Deliberately called twice - once before a simulated restart, once after - so it
    /// must build an identical executor graph (same agent ids) both times for a saved checkpoint
    /// to reattach correctly to the second, entirely separate instance.
    /// </summary>
    internal static class MagenticWorkflowFactory
    {
        /// <summary>
        /// Builds a fresh ManagerAgent, ResearchAgent, and WriterAgent from the given chat client
        /// and wires them into a Magentic-One workflow with plan sign-off required and a bounded
        /// round count.
        /// </summary>
        internal static Workflow Build(IChatClient chatClient)
        {
            ChatClientAgent managerAgent = AgentFactory.CreateAgent(
                chatClient, "manager-agent", "ManagerAgent",
                "You are the orchestrator that plans and delegates tasks to your team.");

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient, "research-agent", "ResearchAgent",
                "You are a trivia researcher. When asked, list exactly two little-known, " +
                    "verifiable facts about the given topic as a short bullet list. Never write " +
                    "prose paragraphs, only bullets.");

            ChatClientAgent writerAgent = AgentFactory.CreateAgent(
                chatClient, "writer-agent", "WriterAgent",
                "You are a copywriter. When given a list of facts, weave them into exactly one " +
                    "short, engaging prose paragraph. Never produce bullet lists, only flowing prose.");

            List<AIAgent> team = [researchAgent, writerAgent];

            return new MagenticWorkflowBuilder(managerAgent)
                .AddParticipants(team)
                .WithMaxRounds(8)
                .WithMaxStalls(2)
                .RequirePlanSignoff(true)
                .Build();
        }
    }
}
```

**`src/CheckpointingAndDurability/Program.cs`**

```csharp
// Lesson 18 - Checkpointing & Durability
// Runs the Magentic-One workflow from Lesson 16 with checkpointing enabled and plan sign-off turned
// on, pauses at its plan-review request (the RequestInfoEvent pause Lesson 17 teaches), then
// simulates a full process restart: the workflow, its agents, and the paused Run object are all
// discarded and rebuilt from nothing. Only a session id (a plain string) and a checkpoint store on
// disk survive into the "new process." The restored run still has the pending plan-review request
// waiting for it, proving the pause is durable rather than an in-memory artifact of the process
// that started it.

using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;

namespace CheckpointingAndDurability
{
    /// <summary>
    /// Entry point that starts a checkpointed Magentic-One run, lets it pause for plan review,
    /// then simulates a process restart before restoring and completing the run.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// The durable session identifier a real application would persist somewhere (a database
        /// row, a queue message) rather than hold in memory - this and the checkpoint store on
        /// disk are the only things that survive the simulated restart in this lesson.
        /// </summary>
        private const string SessionId = "saturn-report-session";

        /// <summary>
        /// The user request this lesson runs, identical to Lesson 16's request so the only new
        /// variable this lesson introduces is the restart itself.
        /// </summary>
        private const string Request = "Get two little-known facts about the planet Saturn from " +
            "the researcher, then have the writer turn them into one short engaging paragraph.";

        /// <summary>
        /// Clears any checkpoints from a prior run of this lesson, starts a fresh checkpointed
        /// run through the pause point, simulates a process restart, then restores and completes
        /// the run from disk alone.
        /// </summary>
        private static async Task Main(string[] args)
        {
            DirectoryInfo checkpointDirectory = new(Path.Combine(AppContext.BaseDirectory, "checkpoints"));
            if (checkpointDirectory.Exists)
            {
                checkpointDirectory.Delete(recursive: true);
            }
            checkpointDirectory.Create();

            CheckpointManager checkpointManager = CheckpointManager.CreateJson(
                new FileSystemJsonCheckpointStore(checkpointDirectory), customOptions: null);

            Console.WriteLine("=== Phase 1: start a checkpointed run, let it pause for plan review ===");
            await RunUntilPausedAsync(checkpointManager);

            Console.WriteLine();
            Console.WriteLine("=== Simulating a process restart ===");
            Console.WriteLine("(the Run, Workflow, and every agent object from Phase 1 are now gone;");
            Console.WriteLine($" only checkpointManager and the session id \"{SessionId}\" remain)");
            Console.WriteLine();

            Console.WriteLine("=== Phase 2: restore from disk and complete the run ===");
            await RestoreAndCompleteAsync(checkpointManager);
        }

        /// <summary>
        /// Builds a fresh Magentic-One workflow, runs it with checkpointing enabled, and reports
        /// the checkpoint that was automatically captured when the run paused for plan review.
        /// </summary>
        private static async Task RunUntilPausedAsync(CheckpointManager checkpointManager)
        {
            IChatClient chatClient = ChatClientFactory.Create();
            Workflow workflow = MagenticWorkflowFactory.Build(chatClient);

            Run run = await InProcessExecution.RunAsync(
                workflow, new ChatMessage(ChatRole.User, Request), checkpointManager, SessionId);

            Console.WriteLine($"IsCheckpointingEnabled: {run.IsCheckpointingEnabled}");
            Console.WriteLine($"Checkpoints captured so far: {run.Checkpoints.Count}");
            Console.WriteLine($"Latest checkpoint: {run.LastCheckpoint}");

            List<WorkflowEvent> events = run.NewEvents.ToList();
            RequestInfoEvent? pending = TryGetPendingRequest(events);
            if (pending is not null && pending.Request.TryGetDataAs(out MagenticPlanReviewRequest? planReview))
            {
                Console.WriteLine();
                Console.WriteLine("--- Paused, awaiting plan review ---");
                Console.WriteLine(planReview!.Plan.Text);
            }
        }

        /// <summary>
        /// Looks up the latest checkpoint for this lesson's session id, rebuilds the workflow
        /// from scratch, resumes from that checkpoint, and approves the still-pending plan review
        /// to let the run complete.
        /// </summary>
        private static async Task RestoreAndCompleteAsync(CheckpointManager checkpointManager)
        {
            CheckpointInfo? latestCheckpoint = await checkpointManager.GetLatestCheckpointAsync(SessionId);
            if (latestCheckpoint is null)
            {
                Console.WriteLine($"No checkpoint found for session \"{SessionId}\".");
                return;
            }
            Console.WriteLine($"GetLatestCheckpointAsync(\"{SessionId}\") -> {latestCheckpoint}");

            IChatClient chatClient = ChatClientFactory.Create();
            Workflow workflow = MagenticWorkflowFactory.Build(chatClient);

            Run run = await InProcessExecution.ResumeAsync(workflow, latestCheckpoint, checkpointManager);

            List<WorkflowEvent> restoredEvents = run.NewEvents.ToList();
            RequestInfoEvent? pending = TryGetPendingRequest(restoredEvents);
            if (pending is null || !pending.Request.TryGetDataAs(out MagenticPlanReviewRequest? planReview))
            {
                Console.WriteLine("Expected a restored plan-review request but found none.");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("--- Restored plan-review request, identical to Phase 1's pause ---");
            Console.WriteLine(planReview!.Plan.Text);
            Console.WriteLine();
            Console.WriteLine("Approving and resuming...");

            ExternalResponse response = pending.Request.CreateResponse(planReview.Approve());
            await run.ResumeAsync([response]);

            Console.WriteLine();
            Console.WriteLine("--- Final answer ---");
            foreach (WorkflowEvent evt in run.NewEvents.ToList())
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

        /// <summary>
        /// Finds the most recent pending external request in a batch of workflow events, or null
        /// if the run did not pause during that batch. Callers pass an already-materialized list
        /// since Run.NewEvents is a single-pass sequence that would otherwise be exhausted by the
        /// first caller to enumerate it.
        /// </summary>
        private static RequestInfoEvent? TryGetPendingRequest(IReadOnlyList<WorkflowEvent> events)
        {
            foreach (WorkflowEvent evt in events)
            {
                if (evt is RequestInfoEvent requestEvent)
                {
                    return requestEvent;
                }
            }

            return null;
        }
    }
}
```

## Walkthrough

1. **`MagenticWorkflowFactory.Build(...)` is the piece that must be perfectly reproducible.** It's called once in `RunUntilPausedAsync` and again, completely independently, in `RestoreAndCompleteAsync` — same agent ids, same names, same instructions, same builder settings, just two separate calls with two separate `IChatClient`/agent object instances behind them.
2. **`AgentFactory.CreateAgent` takes an explicit `id`, not just a `name`, because the id is what has to survive.** The `ChatClientAgentOptions.Id` value becomes part of the workflow's executor graph; the display `Name` does not need to be unique or stable in the same way, but this lesson keeps both consistent for clarity.
3. **Phase 1 runs, prints proof that checkpointing is active, and shows the paused plan.** `run.IsCheckpointingEnabled`, `run.Checkpoints.Count`, and `run.LastCheckpoint` are printed directly from the live `Run` object — nothing here is inferred from the console output, it's read straight from the API.
4. **The "restart" is enforced by scope, not by magic.** `RunUntilPausedAsync` and `RestoreAndCompleteAsync` share no variables, no fields, no static state — the only thing connecting them is the `checkpointManager` parameter (representing the durable store surviving on disk) and the `SessionId` constant (representing a value a real app would have persisted elsewhere). Every object either method touches is local to that method.
5. **`RestoreAndCompleteAsync` never looks at anything Phase 1 produced in memory.** It calls `checkpointManager.GetLatestCheckpointAsync(SessionId)` — a lookup by string — builds a brand new workflow, and calls `InProcessExecution.ResumeAsync`. The fact that the restored run's plan-review request matches Phase 1's is proof the round trip worked, not an assumption.
6. **Approval and completion use Lesson 17's pause-and-resume mechanism.** `planReview.Approve()`, `pending.Request.CreateResponse(...)`, and `run.ResumeAsync([response])` are the plan-review answer Lesson 17's Core Concepts describes — durability was the only new variable this lesson introduced.

## Expected Output

```
=== Phase 1: start a checkpointed run, let it pause for plan review ===
IsCheckpointingEnabled: True
Checkpoints captured so far: 1
Latest checkpoint: CheckpointInfo(SessionId: saturn-report-session, CheckpointId: 775c0596add64165ad2b82a552cea833)

--- Paused, awaiting plan review ---
*   ResearchAgent: Find two little-known facts about the planet Saturn.
*   WriterAgent: Take the two facts provided by the ResearchAgent and craft them into one short, engaging paragraph.

=== Simulating a process restart ===
(the Run, Workflow, and every agent object from Phase 1 are now gone;
 only checkpointManager and the session id "saturn-report-session" remain)

=== Phase 2: restore from disk and complete the run ===
GetLatestCheckpointAsync("saturn-report-session") -> CheckpointInfo(SessionId: saturn-report-session, CheckpointId: 775c0596add64165ad2b82a552cea833)

--- Restored plan-review request, identical to Phase 1's pause ---
*   ResearchAgent: Find two little-known facts about the planet Saturn.
*   WriterAgent: Take the two facts provided by the ResearchAgent and craft them into one short, engaging paragraph.

Approving and resuming...

--- Final answer ---
  [ManagerAgent] Here is the short, engaging paragraph containing two little-known facts about Saturn:

While Saturn's majestic rings appear ancient, they are surprisingly young, possibly forming a mere 10 to 100 million years ago, long after the gas giant itself coalesced. Adding to the planet's eccentricities, one of its moons, Hyperion, tumbles unpredictably through space with a truly chaotic rotation, its spin axis and rate constantly shifting in an astronomical dance that defies prediction.
```

The exact plan wording and final paragraph will vary between runs — every step is a live model call. What will not vary: Phase 1 always reports `IsCheckpointingEnabled: True` and pauses with a plan; Phase 2 always restores the identical pending plan from disk alone and completes it after approval. After running, open the `checkpoints` folder next to the built executable — `index.jsonl` and several `saturn-report-session_*.json` files will be sitting there, one per superstep the workflow passed through, not just one at the pause point.
