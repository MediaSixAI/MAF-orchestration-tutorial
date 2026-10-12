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
