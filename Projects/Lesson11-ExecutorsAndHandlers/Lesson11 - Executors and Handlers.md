# Lesson 11 — Executors & Handlers

## Project Overview

This lesson goes beyond Lesson 10's straight-line pipeline: a single `TriageExecutor` handles two entirely different message types through two separate handlers, and its outgoing edges route each resulting ticket to a different downstream executor based on a condition — or, when no condition matches, drop the message entirely. Four runs against the same workflow shape demonstrate routing, filtering, and multi-type dispatch: the three custom-node capabilities the syllabus calls out beyond what a plain agent gives you.

## Prerequisites

Lesson 10 — this lesson assumes the graph model, `WorkflowBuilder`, `Executor`, `IWorkflowContext`, and `InProcessExecution.RunAsync` introduced there. It reuses the same executor shape from that lesson — a `partial` class deriving from `Executor` with `[MessageHandler]` methods — and extends it to more than one handler per executor.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | Workflow graph model |
| `Microsoft.Agents.AI.Workflows.Generators` | 1.24.0 | Roslyn source generator that wires up `[MessageHandler]` methods at compile time (build-time only, referenced with `PrivateAssets="all"`) |

```powershell
dotnet add package Microsoft.Agents.AI --version 1.24.0
dotnet add package Microsoft.Agents.AI.Workflows --version 1.24.0
dotnet add package Microsoft.Agents.AI.Workflows.Generators --version 1.24.0
```

No `ChatClientFactory`, no model provider and no API key are needed for this lesson — every executor here is a plain C# function with no model call involved, and the routing/filtering/multi-type mechanics are worth seeing in isolation before Lesson 12 reintroduces agents into the graph.

## Core Concepts

- **One executor, multiple handlers, one type per handler.** `TriageExecutor` has two `[MessageHandler]` methods: `HandleTicketAsync(SupportTicket ...)` and `HandleFollowUpAsync(TicketFollowUp ...)`. At compile time the source generator registers one route per method, keyed on its first parameter's type; at run time the executor looks at the type of whatever message arrives and calls the matching method — there is no manual `if (message is X)` branching to write. Each handler method can have any name; the input type is what identifies it. This is the "Handlers" half of this lesson's title: a single node that legitimately speaks more than one message type.
- **Handlers can transform between types, not just pass data through.** The `SupportTicket` handler is a no-op pass-through; the `TicketFollowUp` handler *converts* an entirely different input type into a `SupportTicket` by inventing a fixed escalated priority. Both handlers' return type is the same (`SupportTicket`), which is exactly what lets the *same* downstream edges apply regardless of which handler produced the ticket.
- **Routing is a property of the edge, not the executor.** `WorkflowBuilder.AddEdge(triage, urgent, condition)` and `AddEdge(triage, standard, condition)` both originate from `TriageExecutor`, each guarded by a `Func<SupportTicket?, bool>`. `TriageExecutor` itself has no idea two downstream paths exist — it just returns a ticket, and the graph decides where that ticket goes based on the conditions evaluated against it.
- **Filtering is what happens when no edge condition matches.** This lesson's third scenario — a priority-1 ticket — satisfies neither `>= 4` nor `>= 2 and < 4`. Nothing catches it: no exception, no error event, just a workflow that quietly finishes after `TriageExecutor` runs, having produced no `WorkflowOutputEvent` at all. Filtering, in this graph model, is simply the absence of a matching path — not a distinct API to call.
- **A workflow's declared input types come from its starting executor's handlers.** Because `TriageExecutor` (the start executor) has handlers for both `SupportTicket` and `TicketFollowUp`, `InProcessExecution.RunAsync` accepts either type as the initial input to the exact same built workflow — this lesson's fourth scenario passes a `TicketFollowUp` to a workflow built the identical way as the first three.
- **Generic type erasure will break input dispatch — pass the concrete type through.** Early testing for this lesson wrapped the run call in a helper typed to accept `object`, and every single run silently produced zero events (not even a `WorkflowStartedEvent`). Switching the helper to a generic `RunOnceAsync<TInput>(TInput input, ...)` — preserving the compile-time type all the way into `InProcessExecution.RunAsync` — fixed it immediately. If your own workflow code seems to do nothing at all with no error, check whether the input's static type got erased to `object` somewhere on the way in.

## Complete C# Code

**`src/ExecutorsAndHandlers/SupportTicket.cs`**

```csharp
namespace ExecutorsAndHandlers
{
    /// <summary>
    /// A customer support ticket flowing through the triage workflow. Priority ranges from 1
    /// (lowest) to 5 (highest) and is what the workflow's routing edges branch on.
    /// </summary>
    /// <param name="CustomerName">The name of the customer who filed the ticket.</param>
    /// <param name="Message">The body of the ticket.</param>
    /// <param name="Priority">The ticket's priority, from 1 (lowest) to 5 (highest).</param>
    internal sealed record SupportTicket(string CustomerName, string Message, int Priority);
}
```

**`src/ExecutorsAndHandlers/TicketFollowUp.cs`**

```csharp
namespace ExecutorsAndHandlers
{
    /// <summary>
    /// A customer's follow-up on an existing ticket, with no priority of its own - a follow-up
    /// always signals that the original issue is unresolved and should be escalated.
    /// </summary>
    /// <param name="CustomerName">The name of the customer following up.</param>
    /// <param name="AdditionalInfo">The additional detail the customer provided.</param>
    internal sealed record TicketFollowUp(string CustomerName, string AdditionalInfo);
}
```

**`src/ExecutorsAndHandlers/TriageExecutor.cs`**

```csharp
using Microsoft.Agents.AI.Workflows;

namespace ExecutorsAndHandlers
{
    /// <summary>
    /// Entry point of the triage workflow. Handles two distinct message types - a fresh
    /// <see cref="SupportTicket"/> and a <see cref="TicketFollowUp"/> - with a separate
    /// <c>[MessageHandler]</c> method for each. Both handlers return the same output type, so
    /// the same downstream routing edges apply no matter which one produced the ticket.
    /// </summary>
    internal sealed partial class TriageExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal TriageExecutor() : base(nameof(TriageExecutor))
        {
        }

        /// <summary>
        /// Passes a fresh ticket through unchanged - the routing decision happens entirely in
        /// this executor's outgoing edges, not in its own logic.
        /// </summary>
        [MessageHandler]
        private ValueTask<SupportTicket> HandleTicketAsync(SupportTicket message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(message);
        }

        /// <summary>
        /// Transforms a customer follow-up into an escalated ticket: a follow-up always means
        /// the original issue is still open, so it is re-triaged at a fixed high priority.
        /// </summary>
        [MessageHandler]
        private ValueTask<SupportTicket> HandleFollowUpAsync(TicketFollowUp message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(new SupportTicket(message.CustomerName, $"[Follow-up] {message.AdditionalInfo}", Priority: 4));
        }
    }
}
```

**`src/ExecutorsAndHandlers/UrgentTicketExecutor.cs`**

```csharp
using Microsoft.Agents.AI.Workflows;

namespace ExecutorsAndHandlers
{
    /// <summary>
    /// Formats a final response for tickets routed here as high priority. This executor is
    /// registered as a source of terminal workflow output.
    /// </summary>
    internal sealed partial class UrgentTicketExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal UrgentTicketExecutor() : base(nameof(UrgentTicketExecutor))
        {
        }

        /// <summary>
        /// Formats an urgent-response string for the given ticket.
        /// </summary>
        [MessageHandler]
        private ValueTask<string> HandleAsync(SupportTicket message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult($"URGENT: {message.CustomerName} - {message.Message}");
        }
    }
}
```

**`src/ExecutorsAndHandlers/StandardTicketExecutor.cs`**

```csharp
using Microsoft.Agents.AI.Workflows;

namespace ExecutorsAndHandlers
{
    /// <summary>
    /// Formats a final response for tickets routed here as standard priority. This executor is
    /// registered as a source of terminal workflow output.
    /// </summary>
    internal sealed partial class StandardTicketExecutor : Executor
    {
        /// <summary>
        /// Registers this executor under a fixed, human-readable ID.
        /// </summary>
        internal StandardTicketExecutor() : base(nameof(StandardTicketExecutor))
        {
        }

        /// <summary>
        /// Formats a standard queued-response string for the given ticket.
        /// </summary>
        [MessageHandler]
        private ValueTask<string> HandleAsync(SupportTicket message, IWorkflowContext context, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult($"Queued: {message.CustomerName} - {message.Message}");
        }
    }
}
```

**`src/ExecutorsAndHandlers/Program.cs`**

```csharp
// Lesson 11 - Executors & Handlers
// Goes beyond Lesson 10's straight-line pipeline: TriageExecutor handles two different message
// types via two separate handlers, and its outgoing edges route tickets to different executors
// based on a condition evaluated against the message itself - or, when no condition matches,
// drop the message entirely. Four runs demonstrate routing, filtering, and multi-type dispatch.

using Microsoft.Agents.AI.Workflows;

namespace ExecutorsAndHandlers
{
    /// <summary>
    /// Entry point that builds the ticket-triage workflow and runs it against four inputs: an
    /// urgent ticket, a standard ticket, a low-priority ticket that gets filtered out entirely,
    /// and a follow-up message of a completely different type.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Runs the same workflow shape against four different inputs to show routing,
        /// filtering, and multi-type dispatch in turn.
        /// </summary>
        private static async Task Main(string[] args)
        {
            await RunOnceAsync(new SupportTicket("Alice", "My payment failed!", Priority: 5), "Urgent ticket (priority 5)");
            await RunOnceAsync(new SupportTicket("Bob", "Question about my invoice", Priority: 3), "Standard ticket (priority 3)");
            await RunOnceAsync(new SupportTicket("Carol", "Just saying hi", Priority: 1), "Low-priority ticket (priority 1) - expect no output");
            await RunOnceAsync(new TicketFollowUp("Dana", "Still not fixed, please help!"), "Follow-up message (a different input type)");
        }

        /// <summary>
        /// Builds a fresh workflow instance wiring TriageExecutor's output to
        /// UrgentTicketExecutor or StandardTicketExecutor based on the ticket's priority. A
        /// ticket matching neither condition has nowhere to go and is silently dropped.
        /// </summary>
        private static Workflow BuildTriageWorkflow()
        {
            TriageExecutor triage = new();
            UrgentTicketExecutor urgent = new();
            StandardTicketExecutor standard = new();

            return new WorkflowBuilder(triage)
                .AddEdge(triage, urgent, (SupportTicket? ticket) => ticket is { Priority: >= 4 })
                .AddEdge(triage, standard, (SupportTicket? ticket) => ticket is { Priority: >= 2 and < 4 })
                .WithOutputFrom(urgent)
                .WithOutputFrom(standard)
                .Build();
        }

        /// <summary>
        /// Runs a fresh workflow instance against a single input and prints its terminal
        /// output, or a clear message if the input was filtered out instead.
        /// </summary>
        private static async Task RunOnceAsync<TInput>(TInput input, string label) where TInput : notnull
        {
            Console.WriteLine($"=== {label} ===");

            Workflow workflow = BuildTriageWorkflow();
            Run result = await InProcessExecution.RunAsync(workflow, input);

            bool producedOutput = false;
            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent)
                {
                    Console.WriteLine($"  Output: {outputEvent.Data}");
                    producedOutput = true;
                }
            }

            if (!producedOutput)
            {
                Console.WriteLine("  (no output - filtered out, no routing condition matched)");
            }

            Console.WriteLine();
        }
    }
}
```

## Walkthrough

1. **`BuildTriageWorkflow` wires one start executor to two possible destinations.** `new WorkflowBuilder(triage)` designates the entry point; the two `.AddEdge(triage, target, condition)` calls each attach a `Func<SupportTicket?, bool>` guard. Both edges originate from the same executor — this is what makes it routing rather than a second linear stage.
2. **The conditions are mutually exclusive by construction, with a deliberate gap.** `>= 4` and `>= 2 and < 4` together cover priorities 2 through 5. Priority 1 falls through both — intentionally, to give this lesson a real, observable "filtered" case rather than an edge case that happens to never come up.
3. **`RunOnceAsync<TInput>` is generic specifically so the concrete input type survives to `InProcessExecution.RunAsync`.** Every call site (`RunOnceAsync(new SupportTicket(...), ...)`, `RunOnceAsync(new TicketFollowUp(...), ...)`) lets the compiler infer `TInput` from the argument, so `RunAsync` always receives a strongly-typed `SupportTicket` or `TicketFollowUp` — never a boxed `object` that would prevent the workflow from recognizing which handler to dispatch to (see the Core Concepts note on type erasure).
4. **The first and second scenarios take different edges out of the identical executor.** Alice's priority-5 ticket satisfies the first condition and reaches `UrgentTicketExecutor`; Bob's priority-3 ticket satisfies only the second and reaches `StandardTicketExecutor`. Both are printed as a `WorkflowOutputEvent`, because both terminal executors were registered via `.WithOutputFrom(...)`.
5. **Carol's priority-1 ticket produces zero `WorkflowOutputEvent`s.** `TriageExecutor` still runs and still returns a ticket — but with no outgoing edge willing to accept it, the message goes nowhere, and `RunOnceAsync`'s `producedOutput` flag stays `false`, printing the "filtered out" message instead of a blank line.
6. **Dana's `TicketFollowUp` proves the workflow's declared input types include both handled types.** `RunOnceAsync(new TicketFollowUp(...), ...)` builds the *exact same* `BuildTriageWorkflow()` graph used for the `SupportTicket` runs, and it still works — `TriageExecutor`'s second handler converts it into a priority-4 `SupportTicket`, which then takes the same urgent-routing edge as Alice's ticket did.
7. **Every executor is a `partial` class with `[MessageHandler]` methods, and none of them contains routing code.** `TriageExecutor` declares two handler methods, so the source generator emits two routes for it; `UrgentTicketExecutor` and `StandardTicketExecutor` each declare one (`SupportTicket` in, `string` out). Each handler's return value is sent along the executor's outgoing edges and, for the two terminal executors registered with `.WithOutputFrom(...)`, also yielded as the workflow output that `RunOnceAsync` prints.

## Expected Output

```
=== Urgent ticket (priority 5) ===
  Output: URGENT: Alice - My payment failed!

=== Standard ticket (priority 3) ===
  Output: Queued: Bob - Question about my invoice

=== Low-priority ticket (priority 1) - expect no output ===
  (no output - filtered out, no routing condition matched)

=== Follow-up message (a different input type) ===
  Output: URGENT: Dana - [Follow-up] Still not fixed, please help!
```

Every value here is deterministic — no model call is involved anywhere in this lesson — so this exact output should reproduce on every run.
