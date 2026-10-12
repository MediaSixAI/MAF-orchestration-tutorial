using System.Text.Json;
using CapstoneDeployAndReview;
using Xunit;

namespace Maf.Tests
{
    /// <summary>
    /// Verifies the Analyst's structured-output contract deserializes from a fenced model reply.
    /// Ours, not the course's.
    /// </summary>
    public sealed class AnalysisResultTests
    {
        /// <summary>
        /// A fenced, camel-cased JSON reply parses into a populated AnalysisResult.
        /// </summary>
        [Fact]
        public void FencedReplyDeserializes()
        {
            string reply = """
                ```json
                {"topic":"MAF","summary":"S","keyFindings":[{"fact":"F","source":"U"}]}
                ```
                """;

            AnalysisResult? result = JsonSerializer.Deserialize<AnalysisResult>(
                JsonFenceStripper.Strip(reply),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));

            Assert.NotNull(result);
            Assert.Equal("MAF", result.Topic);
            Assert.Equal("U", Assert.Single(result.KeyFindings).Source);
        }
    }
}
