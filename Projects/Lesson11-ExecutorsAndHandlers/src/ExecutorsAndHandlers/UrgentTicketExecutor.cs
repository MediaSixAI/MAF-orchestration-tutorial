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
