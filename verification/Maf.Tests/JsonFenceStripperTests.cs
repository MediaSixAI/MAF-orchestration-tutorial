using CapstoneDeployAndReview;
using Xunit;

namespace Maf.Tests
{
    /// <summary>
    /// Verifies the capstone's Markdown code-fence stripping. Ours, not the course's.
    /// </summary>
    public sealed class JsonFenceStripperTests
    {
        /// <summary>
        /// A fenced JSON reply has its opening and closing fences removed.
        /// </summary>
        [Fact]
        public void RemovesJsonFence()
        {
            string fenced = "```json\n{\"topic\":\"x\"}\n```";
            Assert.Equal("{\"topic\":\"x\"}", JsonFenceStripper.Strip(fenced));
        }

        /// <summary>
        /// Unfenced text is returned trimmed and otherwise unchanged.
        /// </summary>
        [Fact]
        public void LeavesUnfencedTextUnchanged()
        {
            Assert.Equal("{\"topic\":\"x\"}", JsonFenceStripper.Strip("  {\"topic\":\"x\"}  "));
        }
    }
}
