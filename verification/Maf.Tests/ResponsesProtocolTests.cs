using System.Text.Json;
using CapstoneDeployAndReviewClient;
using Xunit;

namespace Maf.Tests
{
    /// <summary>
    /// Verifies the Lesson 24 client's Responses protocol request bodies and reply reading against
    /// the shapes the host produces. Ours, not the course's.
    /// </summary>
    public sealed class ResponsesProtocolTests
    {
        /// <summary>
        /// A reply shaped like the host's first answer: graph steps, then an approval request.
        /// </summary>
        private const string PausedReply = """
            {
              "id": "caresp_1",
              "status": "completed",
              "output": [
                { "type": "workflow_action", "action_id": "research", "status": "in_progress" },
                { "type": "workflow_action", "action_id": "research", "status": "completed" },
                { "type": "workflow_action", "action_id": "analysis", "status": "completed" },
                { "type": "mcp_approval_request", "id": "mcpr_1", "server_label": "agent_framework", "name": "publish_report",
                  "arguments": "{\"topic\":\"Octopus\",\"keyFindings\":\"- A\\n- B\"}" }
              ]
            }
            """;

        /// <summary>
        /// A reply shaped like the host's second answer: graph steps, then the report message.
        /// </summary>
        private const string FinishedReply = """
            {
              "id": "caresp_2",
              "output": [
                { "type": "workflow_action", "action_id": "writer", "status": "completed" },
                { "type": "message", "role": "assistant", "content": [ { "type": "output_text", "text": "The report." } ] }
              ]
            }
            """;

        /// <summary>
        /// The start body is the topic as plain input.
        /// </summary>
        [Fact]
        public void StartRequestCarriesTopicAsInput()
        {
            Assert.Equal("""{"input":"octopus"}""", ResponsesRequests.CreateStart("octopus"));
        }

        /// <summary>
        /// The approval reply quotes the previous response and the request id, and carries the decision.
        /// </summary>
        [Fact]
        public void ApprovalReplyQuotesPreviousResponseAndRequest()
        {
            using JsonDocument body = JsonDocument.Parse(ResponsesRequests.CreateApprovalReply("caresp_1", "mcpr_1", approve: true));

            Assert.Equal("caresp_1", body.RootElement.GetProperty("previous_response_id").GetString());
            JsonElement item = Assert.Single(body.RootElement.GetProperty("input").EnumerateArray());
            Assert.Equal("mcp_approval_response", item.GetProperty("type").GetString());
            Assert.Equal("mcpr_1", item.GetProperty("approval_request_id").GetString());
            Assert.True(item.GetProperty("approve").GetBoolean());
        }

        /// <summary>
        /// The paused reply yields its id, its completed steps in order, and its approval request.
        /// </summary>
        [Fact]
        public void ReadsPausedReply()
        {
            using JsonDocument reply = JsonDocument.Parse(PausedReply);

            Assert.Equal("caresp_1", ResponsesReader.ReadResponseId(reply.RootElement));
            Assert.Equal(["research", "analysis"], ResponsesReader.ReadCompletedSteps(reply.RootElement));
            ApprovalRequestItem request = Assert.IsType<ApprovalRequestItem>(ResponsesReader.FindApprovalRequest(reply.RootElement));
            Assert.Equal("mcpr_1", request.Id);
            Assert.Equal("publish_report", request.Name);
        }

        /// <summary>
        /// Describe prints the action, then each argument; a multi-line value starts on its own line.
        /// </summary>
        [Fact]
        public void DescribesApprovalRequest()
        {
            ApprovalRequestItem request = new("mcpr_1", "publish_report", """{"topic":"Octopus","keyFindings":"- A\n- B"}""");

            Assert.Equal(
                "Action awaiting approval: publish_report\ntopic: Octopus\nkeyFindings:\n- A\n- B",
                ResponsesReader.Describe(request));
        }

        /// <summary>
        /// The finished reply has no approval request, and its output text is the report.
        /// </summary>
        [Fact]
        public void ReadsFinishedReply()
        {
            using JsonDocument reply = JsonDocument.Parse(FinishedReply);

            Assert.Null(ResponsesReader.FindApprovalRequest(reply.RootElement));
            Assert.Equal("The report.", ResponsesReader.ReadOutputText(reply.RootElement));
        }
    }
}
