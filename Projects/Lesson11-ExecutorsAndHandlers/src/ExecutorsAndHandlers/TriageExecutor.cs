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
