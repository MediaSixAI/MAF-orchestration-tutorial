# Lesson 10 — Workflows 101 (Graph Model)

## Project Overview

This lesson introduces MAF Workflows: a directed-graph model for orchestrating multiple steps, distinct from the single dynamic agent conversation used in every prior lesson. Three plain C# executors are wired into a linear pipeline — normalize text, annotate it with a word count, format a final report — and run both non-streaming and streaming. A short bonus section then shows that an agent can be a workflow node too, using the graph mental model this whole series builds on for Lessons 10–19.

## Prerequisites

Lessons 1–2 — this lesson assumes familiarity with `IChatClient`, `ChatClientAgent`, and `RunAsync`, used only in the bonus section. The graph/executor material introduced here is entirely new; nothing about it depends on Lessons 3–9.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model — `WorkflowBuilder`, `Executor`, `IWorkflowContext`, etc. |
| `Microsoft.Agents.AI.Workflows.Generators` | 1.24.0 | Roslyn source generator that wires up `[MessageHandler]` methods at compile time (build-time only, referenced with `PrivateAssets="all"`) |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` (bonus section only) |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK (bonus section only) |

```powershell
dotnet add package Microsoft.Agents.AI --version 1.24.0
dotnet add package Microsoft.Agents.AI.Workflows --version 1.24.0
dotnet add package Microsoft.Agents.AI.Workflows.Generators --version 1.24.0
dotnet add package Microsoft.Extensions.AI.OpenAI --version 10.10.1
dotnet add package OpenAI --version 2.14.0
```

The Generators package is what makes `[MessageHandler]` work. Without it, a `partial` executor class compiles with `CS0534: does not implement inherited abstract member 'Executor.ConfigureProtocol'`, because nothing generates that method for you.

The environment variables below are needed for the bonus section only.

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

- **A workflow is a predefined graph; an agent is a dynamic conversation.** Every prior lesson's `ChatClientAgent` decides its own next step turn by turn, driven by the model. A `Workflow` is the opposite: you declare the exact steps and connections up front, and MAF executes precisely that graph — better control, at the cost of the model no longer choosing the path.
- **Executors are the nodes; edges are the connections.** An `Executor` receives a typed message, does something with it, and optionally produces output. `WorkflowBuilder.AddEdge(source, target)` draws a directed edge saying "whatever `source` produces gets routed to `target`." This lesson's graph is a straight line — `TextNormalizerExecutor → WordCountAnnotatorExecutor → ReportFormatterExecutor` — but edges support far more: `AddFanOutEdge` sends one executor's output to several targets at once, `AddFanInBarrierEdge` waits for multiple sources before firing a single target, and `AddEdge` has an overload taking a `Func<TMessage, bool>` condition for routing based on message content. Lessons 13–16 build parallel and dynamic shapes like these with `AgentWorkflowBuilder`.
- **An executor is a `partial` class with `[MessageHandler]` methods.** Derive from `Executor`, pass a unique ID to the base constructor, mark the class `partial`, and put `[MessageHandler]` on each method that handles a message type. The method's first parameter is the input type, followed by `IWorkflowContext` and a `CancellationToken`; its return type (`ValueTask<TOut>` here) is the output type. At compile time the Generators package reads those signatures and writes the executor's `ConfigureProtocol` override for you: which message types it accepts, and what it can send onward. The handler methods can stay `private`, because only the generated code inside the same class calls them.
- **`WithOutputFrom` decides what counts as the workflow's answer.** Every executor can pass messages to the next one via its handler's return value, but that alone doesn't make something a *workflow output*. `WorkflowBuilder.WithOutputFrom(formatter)` explicitly designates `ReportFormatterExecutor` as a source of terminal results — only its output shows up as a `WorkflowOutputEvent`, regardless of how many other executors ran along the way.
- **Execution proceeds in supersteps, not free-running concurrency.** MAF's workflow runner uses a Bulk-Synchronous-Parallel (Pregel-style) model: each superstep collects pending messages, routes them, runs every targeted executor concurrently, and waits for all of them to finish before starting the next superstep. This lesson's streaming output shows exactly three completions in sequence because each stage depends on the previous one's output — but independent branches in a fan-out would run within the *same* superstep, in parallel.
- **Non-streaming and streaming are two ways to observe the same run.** `InProcessExecution.RunAsync` runs a workflow to completion and hands back every event that occurred, all at once, via `Run.NewEvents`. `InProcessExecution.RunStreamingAsync` returns a `StreamingRun` whose `WatchStreamAsync()` yields `WorkflowEvent`s as they happen — the same underlying execution, observed live instead of after the fact.
- **Agents and functions are both just executors.** `WorkflowBuilder` accepts an `AIAgent` directly wherever it accepts an `Executor`: in this lesson's bonus section a `ChatClientAgent` becomes the workflow's sole node with no wrapping code at all. An agent node collects the chat messages it receives and only calls the model once it receives a `TurnToken`. A `TurnToken` is an ordinary workflow message, so it goes wherever messages go. `run.TrySendMessageAsync(new TurnToken(emitEvents: true))` delivers it to the workflow's starting executor, which here is the agent itself. When an agent sits further down a graph, the executor feeding it sends the `ChatMessage` and then the `TurnToken` with `context.SendMessageAsync(...)`, and both travel along the same edge. Lesson 12 introduces `AgentWorkflowBuilder`, which builds whole agent pipelines this way for you.

## Complete C# Code

**`src/Workflows101/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Workflows101
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

**`src/Workflows101/TextNormalizerExecutor.cs`**

```csharp
using Microsoft.Agents.AI.Workflows;

namespace Workflows101
{
    /// <summary>
    /// First stage of the pipeline: trims surrounding whitespace and lowercases the input so
    /// every downstream executor works with a consistent, normalized string. The class is
    /// <c>partial</c> so the workflow source generator can wire up its <c>[MessageHandler]</c>.
    /// </summary>
    internal sealed partial class TextNormalizerExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal TextNormalizerExecutor() : base(nameof(TextNormalizerExecutor))
        {
        }

        /// <summary>
        /// Trims and lowercases the incoming text. The returned value is automatically routed
        /// to whichever executor this one is connected to via an edge.
        /// </summary>
        [MessageHandler]
        private ValueTask<string> NormalizeAsync(string message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(message.Trim().ToLowerInvariant());
        }
    }
}
```

**`src/Workflows101/WordCountAnnotatorExecutor.cs`**

```csharp
using Microsoft.Agents.AI.Workflows;

namespace Workflows101
{
    /// <summary>
    /// Second stage of the pipeline: counts the words in the normalized text and appends the
    /// count, producing a single annotated string for the final formatting stage.
    /// </summary>
    internal sealed partial class WordCountAnnotatorExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal WordCountAnnotatorExecutor() : base(nameof(WordCountAnnotatorExecutor))
        {
        }

        /// <summary>
        /// Counts whitespace-separated words in the incoming text and returns an annotated
        /// string carrying both the original text and its word count.
        /// </summary>
        [MessageHandler]
        private ValueTask<string> AnnotateAsync(string message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            int wordCount = message.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            return ValueTask.FromResult($"{message} ({wordCount} words)");
        }
    }
}
```

**`src/Workflows101/ReportFormatterExecutor.cs`**

```csharp
using Microsoft.Agents.AI.Workflows;

namespace Workflows101
{
    /// <summary>
    /// Final stage of the pipeline: wraps the annotated text in a fixed report format. This is
    /// the executor the workflow is configured to treat as a source of terminal output.
    /// </summary>
    internal sealed partial class ReportFormatterExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal ReportFormatterExecutor() : base(nameof(ReportFormatterExecutor))
        {
        }

        /// <summary>
        /// Wraps the incoming annotated text in a fixed report header/footer.
        /// </summary>
        [MessageHandler]
        private ValueTask<string> FormatAsync(string message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult($"=== Workflow Report ===\n{message}\n========================");
        }
    }
}
```

**`src/Workflows101/Program.cs`**

```csharp
// Lesson 10 - Workflows 101 (Graph Model)
// Introduces MAF workflows as directed graphs: three plain C# executors are wired into a linear
// pipeline with WorkflowBuilder and run both non-streaming and streaming. A short bonus section
// then shows that an AIAgent can be a workflow node too, with the agent as the workflow's own
// starting executor.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Workflows101
{
    /// <summary>
    /// Entry point that builds and runs a three-stage text-processing workflow twice (once
    /// non-streaming, once streaming), then runs a minimal single-agent-node workflow.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Runs the function-executor pipeline in both execution modes, then a minimal
        /// agent-as-executor workflow.
        /// </summary>
        private static async Task Main(string[] args)
        {
            const string inputText = "  Hello WORLD from a Workflow  ";

            Console.WriteLine("=== Non-streaming execution ===");
            Workflow nonStreamingWorkflow = BuildTextPipelineWorkflow();
            Run result = await InProcessExecution.RunAsync(nonStreamingWorkflow, inputText);
            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvt)
                {
                    Console.WriteLine(outputEvt.Data);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== Streaming execution ===");
            Workflow streamingWorkflow = BuildTextPipelineWorkflow();
            StreamingRun run = await InProcessExecution.RunStreamingAsync(streamingWorkflow, inputText);
            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is ExecutorCompletedEvent executorCompleted)
                {
                    Console.WriteLine($"  {executorCompleted.ExecutorId} completed.");
                }
                if (evt is WorkflowOutputEvent outputEvt)
                {
                    Console.WriteLine(outputEvt.Data);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== Bonus: a single agent as a workflow node ===");
            await RunSingleAgentWorkflowAsync();
        }

        /// <summary>
        /// Builds a fresh three-stage workflow instance. A new instance is built for every run
        /// because a <see cref="Workflow"/> can only be owned by one runner at a time.
        /// </summary>
        private static Workflow BuildTextPipelineWorkflow()
        {
            TextNormalizerExecutor normalizer = new();
            WordCountAnnotatorExecutor annotator = new();
            ReportFormatterExecutor formatter = new();

            return new WorkflowBuilder(normalizer)
                .AddEdge(normalizer, annotator)
                .AddEdge(annotator, formatter)
                .WithOutputFrom(formatter)
                .Build();
        }

        /// <summary>
        /// Builds and runs a one-node workflow whose sole executor is an OpenAI-backed
        /// ChatClientAgent, demonstrating that agents and plain functions are interchangeable
        /// as workflow nodes.
        /// </summary>
        private static async Task RunSingleAgentWorkflowAsync()
        {
            IChatClient chatClient = ChatClientFactory.Create();
            ChatClientAgent greeterAgent = new(
                chatClient,
                name: "GreeterAgent",
                instructions: "Respond with one short, friendly sentence greeting the user by " +
                    "restating their message back cheerfully.");

            Workflow agentWorkflow = new WorkflowBuilder(greeterAgent)
                .WithOutputFrom(greeterAgent)
                .Build();

            await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
                agentWorkflow, new ChatMessage(ChatRole.User, "Hello World!"));

            // Agents wrapped as workflow executors cache incoming messages and only start
            // processing once they receive a TurnToken.
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is AgentResponseUpdateEvent update)
                {
                    Console.Write(update.Update.Text);
                }
            }

            Console.WriteLine();
        }
    }
}
```

## Walkthrough

1. **`BuildTextPipelineWorkflow` assembles the graph from scratch every time it's called.** `new WorkflowBuilder(normalizer)` designates `normalizer` as the entry point; two `.AddEdge(...)` calls draw the straight-line path to `annotator` and then `formatter`; `.WithOutputFrom(formatter)` marks the final stage's output as the workflow's terminal result; `.Build()` produces an immutable `Workflow`. A fresh set of executor instances is built each call because a single `Workflow` object can only be claimed by one runner — reusing the same instance across two runs throws at runtime.
2. **Each executor's `[MessageHandler]` return value is the entire routing mechanism.** `TextNormalizerExecutor.NormalizeAsync` returns a lowercased, trimmed string; that string becomes `WordCountAnnotatorExecutor`'s input automatically because of the edge between them, with no manual "send to next" call anywhere. The same return value is what `WithOutputFrom` picks up: when `ReportFormatterExecutor.FormatAsync` returns the finished report, the workflow yields it as a `WorkflowOutputEvent`, because that executor was registered as an output source. None of the three executors writes any routing code — the source generator builds each one's `ConfigureProtocol` from the handler signature (`string` in, `string` out).
3. **The non-streaming run reads results after the fact.** `InProcessExecution.RunAsync(workflow, inputText)` blocks until the whole graph finishes, then `Run.NewEvents` contains every event the run produced. Filtering for `WorkflowOutputEvent` picks out just the final report, ignoring the bookkeeping events (superstep boundaries, individual executor invocations) also present in that list.
4. **The streaming run observes the identical execution live.** `InProcessExecution.RunStreamingAsync` returns immediately with a `StreamingRun`; `WatchStreamAsync()` is an `IAsyncEnumerable<WorkflowEvent>` that yields events as the graph actually executes. This lesson prints a line each time an executor completes, which is what makes the three-stage progression visible in the console output rather than appearing all at once.
5. **`RunSingleAgentWorkflowAsync` swaps a function executor for an `AIAgent` with zero adapter code.** `new WorkflowBuilder(greeterAgent)` accepts the `ChatClientAgent` directly — no wrapping class, no interface to implement. The only new step is the `TurnToken`: agent-backed executors buffer whatever message they receive and wait for that signal before actually calling the model, so `run.TrySendMessageAsync(new TurnToken(emitEvents: true))` is what triggers the agent to run and start emitting `AgentResponseUpdateEvent`s.
6. **`emitEvents: true` on the `TurnToken` is what makes the agent's streaming text visible at all.** Without it, the agent still runs, but no per-token `AgentResponseUpdateEvent` is emitted for this lesson's loop to print — the response would complete silently as far as the workflow's event stream is concerned.

## Expected Output

```
=== Non-streaming execution ===
=== Workflow Report ===
hello world from a workflow (5 words)
========================

=== Streaming execution ===
  TextNormalizerExecutor completed.
  WordCountAnnotatorExecutor completed.
  ReportFormatterExecutor completed.
=== Workflow Report ===
hello world from a workflow (5 words)
========================

=== Bonus: a single agent as a workflow node ===
Well, hello there, World!
```

The greeter agent's exact phrasing will vary between runs — it's a live model response, not a fixed string — but the report's text is deterministic (no model call involved in the main pipeline), and the shape of both sections (three completions in order, then one streamed agent sentence) will not change.
