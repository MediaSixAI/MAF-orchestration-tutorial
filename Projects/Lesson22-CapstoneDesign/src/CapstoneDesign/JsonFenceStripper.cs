namespace CapstoneDesign
{
    /// <summary>
    /// Strips a leading/trailing Markdown code fence (```json ... ```) from a model response, if
    /// present, so the remaining text can be parsed as plain JSON. Part of the capstone's
    /// defensive parsing: the Analyst is told not to use fences, and the parser accepts its reply
    /// either way, whichever provider MAF_LLM selects.
    /// </summary>
    internal static class JsonFenceStripper
    {
        /// <summary>
        /// Returns the given text with a single leading and trailing Markdown code fence removed,
        /// if present; otherwise returns the trimmed text unchanged.
        /// </summary>
        internal static string Strip(string text)
        {
            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int firstNewline = trimmed.IndexOf('\n');
            string withoutOpeningFence = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;

            int closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
            string withoutClosingFence = closingFenceIndex >= 0
                ? withoutOpeningFence[..closingFenceIndex]
                : withoutOpeningFence;

            return withoutClosingFence.Trim();
        }
    }
}
