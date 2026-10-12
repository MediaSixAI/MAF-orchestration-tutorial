using System.Text.Json;
using CapstoneDeployAndReview;
using Microsoft.Extensions.AI;
using Xunit;

namespace Maf.Tests
{
    /// <summary>
    /// Verifies the capstone's approval request: what it carries and how it reads back. Ours, not
    /// the course's.
    /// </summary>
    public sealed class ReportApprovalRequestTests
    {
        /// <summary>
        /// A small, fixed analysis used by every test.
        /// </summary>
        private static readonly AnalysisResult Analysis = new(
            "Octopus", "Three hearts.", [new ResearchFinding("Fact one", "Source A"), new ResearchFinding("Fact two", "Source B")]);

        /// <summary>
        /// The request names the publish_report action and carries the analysis as string arguments.
        /// </summary>
        [Fact]
        public void CarriesAnalysisAsActionArguments()
        {
            ToolApprovalRequestContent request = ReportApprovalRequest.Create(Analysis);

            FunctionCallContent action = Assert.IsType<FunctionCallContent>(request.ToolCall);
            Assert.Equal(ReportApprovalRequest.ActionName, action.Name);
            Assert.Equal("Octopus", action.Arguments!["topic"]);
            Assert.Equal("- Fact one (source: Source A)\n- Fact two (source: Source B)", action.Arguments["keyFindings"]);
        }

        /// <summary>
        /// Each request gets its own request id.
        /// </summary>
        [Fact]
        public void IssuesFreshRequestIds()
        {
            Assert.NotEqual(ReportApprovalRequest.Create(Analysis).RequestId, ReportApprovalRequest.Create(Analysis).RequestId);
        }

        /// <summary>
        /// Describe lists topic, summary and findings.
        /// </summary>
        [Fact]
        public void DescribesTheAnalysis()
        {
            string text = ReportApprovalRequest.Describe(ReportApprovalRequest.Create(Analysis));

            Assert.Equal(
                "Topic: Octopus\nSummary: Three hearts.\nKey findings:\n- Fact one (source: Source A)\n- Fact two (source: Source B)",
                text);
        }

        /// <summary>
        /// After a JSON round trip (as in a checkpoint) the arguments are JSON elements, and
        /// Describe still reads them as plain text.
        /// </summary>
        [Fact]
        public void DescribesTheAnalysisAfterJsonRoundTrip()
        {
            ToolApprovalRequestContent original = ReportApprovalRequest.Create(Analysis);
            string json = JsonSerializer.Serialize<AIContent>(original, AIJsonUtilities.DefaultOptions);
            ToolApprovalRequestContent restored = Assert.IsType<ToolApprovalRequestContent>(
                JsonSerializer.Deserialize<AIContent>(json, AIJsonUtilities.DefaultOptions));

            Assert.IsType<JsonElement>(((FunctionCallContent)restored.ToolCall).Arguments!["topic"]);
            Assert.Equal(ReportApprovalRequest.Describe(original), ReportApprovalRequest.Describe(restored));
        }
    }
}
