namespace CapstoneDeployAndReviewClient
{
    /// <summary>
    /// An <c>mcp_approval_request</c> output item from a Responses protocol reply: the item id the
    /// answer must quote, the name of the action awaiting approval, and that action's arguments
    /// as a JSON string.
    /// </summary>
    internal sealed record ApprovalRequestItem(string Id, string Name, string Arguments);
}
