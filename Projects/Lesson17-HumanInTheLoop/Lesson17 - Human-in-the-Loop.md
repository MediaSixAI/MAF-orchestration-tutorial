# Lesson 17 — Human-in-the-Loop

## Project Overview

This lesson puts a human approval checkpoint inside a running multi-agent workflow. `ResearchAgent` gathers facts about a topic, a request port pauses the graph and hands those facts to a person at the console, and only an approved decision moves on to `WriterAgent`. Rejected facts go back to `ResearchAgent` together with the reviewer's feedback, so the run loops until a person is satisfied. Lesson 9 gated one tool call inside one agent's turn. This checkpoint gates the flow of work between agents, and it is the same Researcher → approval → Writer shape the Lesson 22–24 capstone is built on.

## Prerequisites

- **Lesson 9.** `ToolApprovalRequestContent` and `ToolApprovalResponseContent`, the approval checkpoint for a single tool call. This lesson moves the checkpoint up to the workflow level.
- **Lessons 10–11.** `WorkflowBuilder`, edges, conditional routing, and `partial` executors with `[MessageHandler]` methods.
- **Lesson 16.** `MagenticWorkflowBuilder` and `RequirePlanSignoff`, which Lesson 16 turned off. Core Concepts shows how its plan review uses the same pause-and-resume mechanism taught here.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package: `AIAgent`, `ChatClientAgent` |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model: `WorkflowBuilder`, `RequestPort`, `RequestInfoEvent`, `ExternalRequest`/`ExternalResponse`, `Run.ResumeAsync`, `RunStatus` |
| `Microsoft.Agents.AI.Workflows.Generators` | 1.24.0 | Source generator that wires up the `[MessageHandler]` methods on the two `partial` executors (build-time only, referenced with `PrivateAssets="all"`) |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

```powershell
dotnet add package Microsoft.Agents.AI --version 1.24.0
dotnet add package Microsoft.Agents.AI.Workflows --version 1.24.0
dotnet add package Microsoft.Agents.AI.Workflows.Generators --version 1.24.0
dotnet add package Microsoft.Extensions.AI.OpenAI --version 10.10.1
dotnet add package OpenAI --version 2.14.0
```

**This lesson is interactive.** The run pauses at a console prompt and waits for real keyboard input: type `y` to approve the researcher's facts, or type feedback to send them back for another round.

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

- **A request port is a graph node that asks the outside world a question.** `RequestPort.Create<TRequest, TResponse>(id)` creates a node you wire into a `WorkflowBuilder` graph with `AddEdge`, like any executor. When a message of type `TRequest` reaches it, the port does not process it. It raises the message outside the workflow and the run halts at the end of that step. When a `TResponse` comes back, the port sends it along its outgoing edges as an ordinary message. To the executors on either side, the human is just another node in the graph.

  ```text
  topic ──► ResearchExecutor ──FactsForReview──► [FactReview port] ──ReviewDecision──► WriterExecutor ──► output
                   ▲                                       │            (Approved)
                   └──────────── ReviewDecision ───────────┘
                                (not Approved)
  ```

- **The request and the response are your own types.** This lesson's port is `RequestPort<FactsForReview, ReviewDecision>`. `FactsForReview` carries the topic, the facts and the round number out to the reviewer. Its `Approve()` and `Revise(feedback)` methods build the `ReviewDecision` that comes back, so the decision carries everything the next executor needs. Any type works in-process. Lesson 9's `ToolApprovalRequestContent` and `ToolApprovalResponseContent` also work as port types, and they are the natural choice when the workflow is exposed to a remote client.

- **Conditional edges route the decision.** Two edges leave the port, both typed `ReviewDecision`: `AddEdge<ReviewDecision>(review, writer, d => d is { Approved: true })` and `AddEdge<ReviewDecision>(review, research, d => d is { Approved: false })`. The second edge creates a loop. A rejected decision goes back to `ResearchExecutor`, which has a second `[MessageHandler]` for `ReviewDecision` that asks the agent for a revised list. The revised facts flow into the port again and raise a new request.

- **`RequestInfoEvent` is how the pause surfaces.** When the port fires, the run emits a `RequestInfoEvent`. Its `Request` property is an `ExternalRequest` that holds the request id, the port it came from (`PortInfo.PortId`), and the payload. `request.TryGetDataAs(out FactsForReview? review)` reads the payload back as your own type.

- **`RunStatus.PendingRequests` tells you the run is waiting on a person.** `Run.GetStatusAsync()` returns a `RunStatus`: `NotStarted`, `Running`, `Idle`, `PendingRequests` or `Ended`. `PendingRequests` means the run has halted with at least one unanswered `ExternalRequest`. `Idle` means it halted with nothing outstanding, which for this graph means the writer has finished. The console loop runs while the status is `PendingRequests`. A web app or a chat bot would check the same status to decide whether to show a review screen.

- **Resume by answering the request.** `request.CreateResponse(decision)` wraps the decision in an `ExternalResponse` that is tied to that request's id and port. There are two ways to deliver it:
  - With a non-streaming `Run` from `InProcessExecution.RunAsync`, call `await run.ResumeAsync([response])`. It runs the workflow until it halts again and returns. `run.NewEvents` then holds the events from that stretch, and the loop checks the status again. This lesson uses this form.
  - With a `StreamingRun` from `InProcessExecution.RunStreamingAsync`, call `await run.SendResponseAsync(response)` from inside the `await foreach (WorkflowEvent evt in run.WatchStreamAsync())` loop when a `RequestInfoEvent` arrives. The response is processed in the next step, and the same stream keeps delivering events.

- **The same mechanism powers Magentic-One's plan sign-off.** With `RequirePlanSignoff(true)`, which is the default, `MagenticWorkflowBuilder` pauses after the manager drafts its plan. The pause is a `RequestInfoEvent` whose payload is a `MagenticPlanReviewRequest`. You answer it with `planReview.Approve()` or `planReview.Revise(feedback)`, wrapped by `request.CreateResponse(...)` and passed to `Run.ResumeAsync`. That is the loop in this lesson's `Main`, with a different payload type. Magentic's port is built into the orchestration and reviews the plan. A `RequestPort` is one you place yourself, anywhere in your own graph, to review whatever data passes through it.

- **This is a different level from Lesson 9.** Lesson 9 paused one agent before it ran one function, and resumed by continuing that agent's `AgentSession`. Here no agent is paused: `ResearchAgent` has finished its turn, and the workflow holds its output until a person decides where it goes. The pause belongs to the workflow's `Run`, not to any agent's session, so Lesson 18 can checkpoint it and resume it in a different process.

## Complete C# Code

**`src/HumanInTheLoop/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HumanInTheLoop
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

**`src/HumanInTheLoop/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HumanInTheLoop
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

**`src/HumanInTheLoop/FactsForReview.cs`**

```csharp
namespace HumanInTheLoop
{
    /// <summary>
    /// The request a human reviewer receives from the workflow's "FactReview" request port: the
    /// topic, the facts ResearchAgent produced for it, and which research round they came from.
    /// The reviewer answers by calling <see cref="Approve"/> or <see cref="Revise"/>, so the
    /// response always carries everything the next executor needs.
    /// </summary>
    /// <param name="Topic">The topic the workflow was started with.</param>
    /// <param name="Facts">The facts ResearchAgent produced, as a short bullet list.</param>
    /// <param name="Round">1 for the first research pass, incremented on every revision.</param>
    internal sealed record FactsForReview(string Topic, string Facts, int Round)
    {
        /// <summary>
        /// Builds the decision that releases these facts to WriterAgent.
        /// </summary>
        internal ReviewDecision Approve()
        {
            return new ReviewDecision(this.Topic, this.Facts, this.Round, Approved: true, Feedback: null);
        }

        /// <summary>
        /// Builds the decision that sends these facts back to ResearchAgent, together with the
        /// reviewer's feedback explaining what to change.
        /// </summary>
        internal ReviewDecision Revise(string feedback)
        {
            return new ReviewDecision(this.Topic, this.Facts, this.Round, Approved: false, Feedback: feedback);
        }
    }
}
```

**`src/HumanInTheLoop/ReviewDecision.cs`**

```csharp
namespace HumanInTheLoop
{
    /// <summary>
    /// The response a human reviewer sends back through the workflow's "FactReview" request port.
    /// The workflow's conditional edges read <see cref="Approved"/> to decide where it goes next:
    /// to WriterExecutor when approved, or back to ResearchExecutor for another round.
    /// </summary>
    /// <param name="Topic">The topic the workflow was started with.</param>
    /// <param name="Facts">The facts that were reviewed.</param>
    /// <param name="Round">The research round the reviewed facts came from.</param>
    /// <param name="Approved">True to release the facts to WriterAgent; false to send them back.</param>
    /// <param name="Feedback">The reviewer's revision feedback, or null when approved.</param>
    internal sealed record ReviewDecision(string Topic, string Facts, int Round, bool Approved, string? Feedback);
}
```

**`src/HumanInTheLoop/ResearchExecutor.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace HumanInTheLoop
{
    /// <summary>
    /// The workflow's first node. It runs ResearchAgent on the incoming topic and sends the facts
    /// on to the review port. It also handles a rejected <see cref="ReviewDecision"/> coming back
    /// from that port, asking ResearchAgent for a revised list that addresses the reviewer's
    /// feedback. The class is <c>partial</c> so the workflow source generator can wire up its
    /// <c>[MessageHandler]</c> methods.
    /// </summary>
    internal sealed partial class ResearchExecutor : Executor
    {
        /// <summary>
        /// The agent that produces the facts.
        /// </summary>
        private readonly AIAgent researchAgent;

        /// <summary>
        /// Registers this executor under a fixed ID and keeps the agent it delegates to.
        /// </summary>
        internal ResearchExecutor(AIAgent researchAgent) : base(nameof(ResearchExecutor))
        {
            this.researchAgent = researchAgent;
        }

        /// <summary>
        /// Handles the workflow's starting input: asks ResearchAgent for facts about the topic and
        /// returns them as round 1 of the review. The return value travels along this executor's
        /// outgoing edge to the request port.
        /// </summary>
        [MessageHandler]
        private async ValueTask<FactsForReview> ResearchAsync(string topic, IWorkflowContext context, CancellationToken cancellationToken)
        {
            AgentResponse response = await this.researchAgent.RunAsync(
                $"Topic: {topic}",
                cancellationToken: cancellationToken);

            return new FactsForReview(topic, response.Text, Round: 1);
        }

        /// <summary>
        /// Handles a rejected review: gives ResearchAgent its previous facts plus the reviewer's
        /// feedback, and returns the revised facts as the next review round.
        /// </summary>
        [MessageHandler]
        private async ValueTask<FactsForReview> ReviseAsync(ReviewDecision decision, IWorkflowContext context, CancellationToken cancellationToken)
        {
            string prompt =
                $"Topic: {decision.Topic}\n\n" +
                $"Your previous facts:\n{decision.Facts}\n\n" +
                $"A human reviewer sent them back with this feedback: {decision.Feedback}\n\n" +
                "Produce a revised list that addresses the feedback.";

            AgentResponse response = await this.researchAgent.RunAsync(prompt, cancellationToken: cancellationToken);

            return new FactsForReview(decision.Topic, response.Text, decision.Round + 1);
        }
    }
}
```

**`src/HumanInTheLoop/WriterExecutor.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace HumanInTheLoop
{
    /// <summary>
    /// The workflow's last node. It only ever receives an approved <see cref="ReviewDecision"/>,
    /// runs WriterAgent on the approved facts, and yields the paragraph as the workflow's output.
    /// The class is <c>partial</c> so the workflow source generator can wire up its
    /// <c>[MessageHandler]</c>, and <c>[YieldsOutput]</c> declares the output type it produces.
    /// </summary>
    [YieldsOutput(typeof(string))]
    internal sealed partial class WriterExecutor : Executor
    {
        /// <summary>
        /// The agent that turns approved facts into prose.
        /// </summary>
        private readonly AIAgent writerAgent;

        /// <summary>
        /// Registers this executor under a fixed ID and keeps the agent it delegates to.
        /// </summary>
        internal WriterExecutor(AIAgent writerAgent) : base(nameof(WriterExecutor))
        {
            this.writerAgent = writerAgent;
        }

        /// <summary>
        /// Asks WriterAgent to turn the approved facts into one paragraph and yields that text as
        /// the workflow's final output.
        /// </summary>
        [MessageHandler]
        private async ValueTask WriteAsync(ReviewDecision decision, IWorkflowContext context, CancellationToken cancellationToken)
        {
            string prompt = $"Topic: {decision.Topic}\n\nApproved facts:\n{decision.Facts}";

            AgentResponse response = await this.writerAgent.RunAsync(prompt, cancellationToken: cancellationToken);

            await context.YieldOutputAsync(response.Text, cancellationToken);
        }
    }
}
```

**`src/HumanInTheLoop/ReviewWorkflowFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

namespace HumanInTheLoop
{
    /// <summary>
    /// Builds the research-review-write workflow: ResearchExecutor feeds a request port that
    /// pauses the run for a human, and the human's decision either releases the facts to
    /// WriterExecutor or sends them back to ResearchExecutor. This is the same shape as the
    /// capstone's Researcher, approval, Writer pipeline.
    /// </summary>
    internal static class ReviewWorkflowFactory
    {
        /// <summary>
        /// The request port's ID. It appears on every <see cref="ExternalRequest"/> the port raises.
        /// </summary>
        internal const string ReviewPortId = "FactReview";

        /// <summary>
        /// Wires the two agents and the review port into a graph with one approval checkpoint and
        /// a revision loop, and returns the built workflow.
        /// </summary>
        internal static Workflow Build(AIAgent researchAgent, AIAgent writerAgent)
        {
            ResearchExecutor research = new(researchAgent);
            WriterExecutor writer = new(writerAgent);
            RequestPort<FactsForReview, ReviewDecision> review =
                RequestPort.Create<FactsForReview, ReviewDecision>(ReviewPortId);

            return new WorkflowBuilder(research)
                .AddEdge(research, review)
                .AddEdge<ReviewDecision>(review, writer, decision => decision is { Approved: true })
                .AddEdge<ReviewDecision>(review, research, decision => decision is { Approved: false })
                .WithOutputFrom(writer)
                .Build();
        }
    }
}
```

**`src/HumanInTheLoop/Program.cs`**

```csharp
// Lesson 17 - Human-in-the-Loop
// Puts a human approval checkpoint inside a running multi-agent workflow. ResearchAgent gathers
// facts, a RequestPort pauses the graph and hands those facts to a human, and only an approved
// decision reaches WriterAgent; rejected facts go back to ResearchAgent with the reviewer's
// feedback. Lesson 9 gated one tool call inside one agent's turn; this checkpoint gates the flow
// of work between agents.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HumanInTheLoop
{
    /// <summary>
    /// Entry point that builds the research-review-write workflow, runs it until it pauses for
    /// review, and keeps answering the pending request from console input until the reviewer
    /// approves and WriterAgent produces the final paragraph.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds ResearchAgent and WriterAgent, wires them into the review workflow, and drives
        /// the pause, review and resume loop: while the run reports
        /// <see cref="RunStatus.PendingRequests"/>, it shows the pending facts, reads the
        /// reviewer's decision and resumes the same run with it.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient, "ResearchAgent",
                "You are a trivia researcher. Given a topic, list exactly two little-known, " +
                    "verifiable facts about it as a short bullet list. Never write prose " +
                    "paragraphs, only bullets.");

            ChatClientAgent writerAgent = AgentFactory.CreateAgent(
                chatClient, "WriterAgent",
                "You are a copywriter. Given a topic and a list of approved facts, weave every " +
                    "fact into exactly one short, engaging prose paragraph. Never produce bullet " +
                    "lists, only flowing prose.");

            Workflow workflow = ReviewWorkflowFactory.Build(researchAgent, writerAgent);

            Console.WriteLine("=== Human-in-the-Loop: review the researcher's facts before the writer runs ===");
            const string topic = "the planet Saturn";
            Console.WriteLine($"Topic: {topic}");
            Console.WriteLine();

            await using Run run = await InProcessExecution.RunAsync(workflow, topic);
            List<WorkflowEvent> events = run.NewEvents.ToList();

            while (await run.GetStatusAsync() == RunStatus.PendingRequests)
            {
                ExternalRequest request = GetPendingRequest(events);
                if (!request.TryGetDataAs(out FactsForReview? review))
                {
                    throw new InvalidOperationException($"Unexpected request on port '{request.PortInfo.PortId}'.");
                }

                Console.WriteLine($"--- Run status: {RunStatus.PendingRequests}. Round {review.Round} facts from ResearchAgent ---");
                Console.WriteLine(review.Facts);
                Console.WriteLine();
                Console.Write("Type 'y' to approve, or type feedback to send the facts back: ");
                string input = ReadNonEmptyLine();
                Console.WriteLine();

                ReviewDecision decision = input.Equals("y", StringComparison.OrdinalIgnoreCase)
                    ? review.Approve()
                    : review.Revise(input);

                await run.ResumeAsync([request.CreateResponse(decision)]);
                events = run.NewEvents.ToList();
            }

            PrintResult(events);
            Console.WriteLine();
            Console.WriteLine($"Final run status: {await run.GetStatusAsync()}");
        }

        /// <summary>
        /// Returns the most recent request the workflow raised in this batch of events. Throws if
        /// there is none, because the caller only asks while the run reports pending requests.
        /// </summary>
        private static ExternalRequest GetPendingRequest(IEnumerable<WorkflowEvent> events)
        {
            RequestInfoEvent? requestEvent = events.OfType<RequestInfoEvent>().LastOrDefault();
            return requestEvent?.Request
                ?? throw new InvalidOperationException("The run reports pending requests, but no RequestInfoEvent was raised.");
        }

        /// <summary>
        /// Reads a trimmed line from the console, prompting again until the reviewer types
        /// something.
        /// </summary>
        private static string ReadNonEmptyLine()
        {
            while (true)
            {
                string input = (Console.ReadLine() ?? string.Empty).Trim();
                if (input.Length > 0)
                {
                    return input;
                }

                Console.Write("Please type 'y' or your feedback: ");
            }
        }

        /// <summary>
        /// Prints the workflow's final output, or the failure that stopped it, from the last
        /// batch of events.
        /// </summary>
        private static void PrintResult(IEnumerable<WorkflowEvent> events)
        {
            foreach (WorkflowEvent evt in events)
            {
                switch (evt)
                {
                    case WorkflowOutputEvent output:
                        Console.WriteLine($"--- Approved. Output from {output.ExecutorId} ---");
                        Console.WriteLine(output.Data);
                        break;
                    case ExecutorFailedEvent failed:
                        Console.WriteLine($"--- {failed.ExecutorId} failed: {failed.Data} ---");
                        break;
                    case WorkflowErrorEvent error:
                        Console.WriteLine($"--- Workflow error: {error.Exception?.Message} ---");
                        break;
                }
            }
        }
    }
}
```

## Walkthrough

1. **Two agents, built the usual way.** `ResearchAgent` returns exactly two facts as bullets. `WriterAgent` turns a list of approved facts into one paragraph. Both come from the series' `ChatClientFactory` and `AgentFactory`.
2. **Each agent runs inside an executor.** `ResearchExecutor` and `WriterExecutor` are `partial` classes with `[MessageHandler]` methods, and each holds the agent it delegates to. `ResearchExecutor.ResearchAsync` handles the starting `string` topic and returns a `FactsForReview` for round 1. The return value travels along the executor's outgoing edge to the port.
3. **`ReviewWorkflowFactory.Build` wires the graph.** `RequestPort.Create<FactsForReview, ReviewDecision>("FactReview")` creates the port. `AddEdge(research, review)` feeds it. The two conditional `AddEdge<ReviewDecision>` calls send an approved decision to `WriterExecutor` and a rejected one back to `ResearchExecutor`. `WithOutputFrom(writer)` marks the writer's yielded text as the workflow's output.
4. **`InProcessExecution.RunAsync(workflow, topic)` runs until the first pause.** `ResearchAgent` produces its facts, they reach the port, and the call returns with the run halted. `run.NewEvents` is read once into a list, because each read returns only the events since the previous read.
5. **The loop runs while the status is `PendingRequests`.** `GetPendingRequest` takes the latest `RequestInfoEvent` and returns its `ExternalRequest`. `TryGetDataAs` reads the `FactsForReview` payload, and the console shows the round number and the facts.
6. **The reviewer's input becomes the decision.** `y` calls `review.Approve()`. Anything else is passed verbatim to `review.Revise(input)` as the feedback. `ReadNonEmptyLine` re-prompts on an empty line, so an accidental Enter is not sent as empty feedback.
7. **`request.CreateResponse(decision)` and `run.ResumeAsync(...)` continue the same run.** The port emits the `ReviewDecision`, and the edge conditions pick its route. A rejection reaches `ResearchExecutor.ReviseAsync`, which gives the agent its previous facts and the feedback, then sends round 2 to the port, and the loop prompts again. An approval reaches `WriterExecutor.WriteAsync`, which runs `WriterAgent` and yields the paragraph with `YieldOutputAsync`.
8. **The run ends `Idle`.** With no request outstanding, the loop exits. `PrintResult` prints the `WorkflowOutputEvent` from `WriterExecutor`, or the failure event if an executor threw, and the final status is printed.

## Expected Output

<!-- EXPECTED OUTPUT PENDING: re-run with OPENAI_API_KEY set -->

The run prints the banner and topic, then pauses with `--- Run status: PendingRequests. Round 1 facts from ResearchAgent ---` followed by two bullet facts about Saturn and the review prompt. Typing feedback (for example, `Also mention one fact about Saturn's moons.`) produces a `Round 2` pause with a revised list that addresses it. Typing `y` releases those facts to `WriterAgent`, prints `--- Approved. Output from WriterExecutor ---` with one paragraph that uses the approved facts, and ends with `Final run status: Idle`.
