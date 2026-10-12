using Microsoft.Extensions.AI;

namespace CapstoneBuild
{
    /// <summary>
    /// Builds and reads the capstone's human-approval request. The request is the standard
    /// <see cref="ToolApprovalRequestContent"/> from Microsoft.Extensions.AI: it names the action
    /// awaiting sign-off ("publish_report") and carries the analysis as that action's arguments,
    /// so whoever reviews it - a console prompt, or a remote client over the Responses protocol -
    /// sees exactly what will be written up.
    /// </summary>
    internal static class ReportApprovalRequest
    {
        /// <summary>
        /// The name of the action the reviewer approves or rejects.
        /// </summary>
        internal const string ActionName = "publish_report";

        /// <summary>
        /// Creates an approval request for the given analysis, with fresh request and call ids.
        /// Every argument is a plain string, so it reads the same after a checkpoint round trip
        /// and on any wire format.
        /// </summary>
        internal static ToolApprovalRequestContent Create(AnalysisResult analysis)
        {
            Dictionary<string, object?> arguments = new()
            {
                ["topic"] = analysis.Topic,
                ["summary"] = analysis.Summary,
                ["keyFindings"] = string.Join(
                    "\n", analysis.KeyFindings.Select(finding => $"- {finding.Fact} (source: {finding.Source})")),
            };

            FunctionCallContent action = new($"call_{Guid.NewGuid():N}", ActionName, arguments);
            return new ToolApprovalRequestContent($"approval_{Guid.NewGuid():N}", action);
        }

        /// <summary>
        /// Formats an approval request as the text a reviewer reads: the topic, the summary, and
        /// the key findings with their sources.
        /// </summary>
        internal static string Describe(ToolApprovalRequestContent request)
        {
            if (request.ToolCall is not FunctionCallContent action)
            {
                return $"Approval requested (id {request.RequestId}) with no details attached.";
            }

            return $"Topic: {ReadArgument(action, "topic")}\n" +
                $"Summary: {ReadArgument(action, "summary")}\n" +
                $"Key findings:\n{ReadArgument(action, "keyFindings")}";
        }

        /// <summary>
        /// Reads one argument as text. An argument is a string when the request is first created
        /// and a JSON string element after a checkpoint round trip; ToString() returns the plain
        /// text for both.
        /// </summary>
        private static string ReadArgument(FunctionCallContent action, string name)
        {
            return action.Arguments is not null && action.Arguments.TryGetValue(name, out object? value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }
    }
}
