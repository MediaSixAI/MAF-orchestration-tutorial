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
