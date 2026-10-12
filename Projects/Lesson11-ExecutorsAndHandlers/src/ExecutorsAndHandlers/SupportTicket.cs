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
