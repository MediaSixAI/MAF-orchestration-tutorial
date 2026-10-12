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
