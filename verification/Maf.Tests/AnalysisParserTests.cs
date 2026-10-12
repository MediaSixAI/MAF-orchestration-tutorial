using CapstoneDeployAndReview;
using Xunit;

namespace Maf.Tests
{
    /// <summary>
    /// Verifies the capstone's parsing of the Analyst's reply into AnalysisResult. Ours, not the
    /// course's.
    /// </summary>
    public sealed class AnalysisParserTests
    {
        /// <summary>
        /// A reply in the exact contract shape parses into a populated AnalysisResult.
        /// </summary>
        [Fact]
        public void ParsesContractShape()
        {
            AnalysisResult result = AnalysisParser.Parse(
                """{"Topic":"T","Summary":"S","KeyFindings":[{"Fact":"F1","Source":"U1"},{"Fact":"F2","Source":"U2"}]}""");

            Assert.Equal("T", result.Topic);
            Assert.Equal("S", result.Summary);
            Assert.Equal(["U1", "U2"], result.KeyFindings.Select(f => f.Source));
        }

        /// <summary>
        /// A fenced, camel-cased reply parses too: the fence is stripped and names match case-insensitively.
        /// </summary>
        [Fact]
        public void ParsesFencedCamelCaseReply()
        {
            AnalysisResult result = AnalysisParser.Parse(
                "```json\n{\"topic\":\"T\",\"summary\":\"S\",\"keyFindings\":[{\"fact\":\"F\",\"source\":\"U\"}]}\n```");

            Assert.Equal("F", Assert.Single(result.KeyFindings).Fact);
        }

        /// <summary>
        /// A reply that is not JSON throws InvalidOperationException quoting the reply.
        /// </summary>
        [Fact]
        public void RejectsNonJson()
        {
            InvalidOperationException error = Assert.Throws<InvalidOperationException>(
                () => AnalysisParser.Parse("Here is the analysis you asked for."));

            Assert.Contains("Here is the analysis", error.Message);
        }

        /// <summary>
        /// JSON missing a required member throws rather than returning a half-filled result.
        /// </summary>
        [Fact]
        public void RejectsMissingMembers()
        {
            Assert.Throws<InvalidOperationException>(() => AnalysisParser.Parse("""{"Topic":"T"}"""));
        }
    }
}
