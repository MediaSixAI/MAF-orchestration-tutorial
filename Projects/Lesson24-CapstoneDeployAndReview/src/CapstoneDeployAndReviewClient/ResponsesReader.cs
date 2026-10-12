using System.Text;
using System.Text.Json;

namespace CapstoneDeployAndReviewClient
{
    /// <summary>
    /// Reads the parts of a Responses protocol reply this client needs: the response id, the
    /// progress of each workflow step, any approval request, and the assistant's output text.
    /// </summary>
    internal static class ResponsesReader
    {
        /// <summary>
        /// Returns the reply's id, which a follow-up request quotes as previous_response_id.
        /// </summary>
        internal static string ReadResponseId(JsonElement response)
        {
            return response.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("The reply has no id.");
        }

        /// <summary>
        /// Returns the id of each graph step that completed in this reply, in order. The host
        /// reports every step as a workflow_action output item.
        /// </summary>
        internal static List<string> ReadCompletedSteps(JsonElement response)
        {
            return [.. Items(response)
                .Where(item => Text(item, "type") == "workflow_action" && Text(item, "status") == "completed")
                .Select(item => Text(item, "action_id"))];
        }

        /// <summary>
        /// Returns the reply's approval request, or null if the run did not pause for one.
        /// </summary>
        internal static ApprovalRequestItem? FindApprovalRequest(JsonElement response)
        {
            return Items(response)
                .Where(item => Text(item, "type") == "mcp_approval_request")
                .Select(item => new ApprovalRequestItem(Text(item, "id"), Text(item, "name"), Text(item, "arguments")))
                .LastOrDefault();
        }

        /// <summary>
        /// Joins the text of every assistant message in the reply.
        /// </summary>
        internal static string ReadOutputText(JsonElement response)
        {
            StringBuilder text = new();
            foreach (JsonElement item in Items(response).Where(item => Text(item, "type") == "message"))
            {
                if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement part in content.EnumerateArray().Where(part => Text(part, "type") == "output_text"))
                {
                    text.AppendLine(Text(part, "text"));
                }
            }

            return text.ToString().Trim();
        }

        /// <summary>
        /// Formats an approval request for the reviewer: the action name, then each argument as
        /// "name: value", with a multi-line value starting on the line after its name.
        /// </summary>
        internal static string Describe(ApprovalRequestItem request)
        {
            StringBuilder text = new($"Action awaiting approval: {request.Name}\n");
            using JsonDocument arguments = JsonDocument.Parse(request.Arguments.Length > 0 ? request.Arguments : "{}");
            foreach (JsonProperty argument in arguments.RootElement.EnumerateObject())
            {
                string value = argument.Value.ValueKind == JsonValueKind.String
                    ? argument.Value.GetString() ?? string.Empty
                    : argument.Value.GetRawText();
                text.Append(value.Contains('\n') ? $"{argument.Name}:\n{value}\n" : $"{argument.Name}: {value}\n");
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Enumerates the reply's output items, or nothing if it has none.
        /// </summary>
        private static IEnumerable<JsonElement> Items(JsonElement response)
        {
            return response.TryGetProperty("output", out JsonElement output) && output.ValueKind == JsonValueKind.Array
                ? output.EnumerateArray()
                : [];
        }

        /// <summary>
        /// Reads a string property, or returns an empty string if it is missing or not a string.
        /// </summary>
        private static string Text(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }
}
