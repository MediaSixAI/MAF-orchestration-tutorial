using System.Text.Json.Nodes;

namespace CapstoneDeployAndReviewClient
{
    /// <summary>
    /// Builds the two JSON request bodies this client posts to the host's /responses endpoint:
    /// one that starts a run on a topic, and one that answers the run's approval request.
    /// </summary>
    internal static class ResponsesRequests
    {
        /// <summary>
        /// The body that starts a new run: the topic as plain text input.
        /// </summary>
        internal static string CreateStart(string topic)
        {
            return new JsonObject { ["input"] = topic }.ToJsonString();
        }

        /// <summary>
        /// The body that answers an approval request. previous_response_id continues the same
        /// conversation, and the single mcp_approval_response input item quotes the request's id
        /// and carries the decision.
        /// </summary>
        internal static string CreateApprovalReply(string previousResponseId, string approvalRequestId, bool approve)
        {
            return new JsonObject
            {
                ["previous_response_id"] = previousResponseId,
                ["input"] = new JsonArray(new JsonObject
                {
                    ["type"] = "mcp_approval_response",
                    ["approval_request_id"] = approvalRequestId,
                    ["approve"] = approve,
                }),
            }.ToJsonString();
        }
    }
}
