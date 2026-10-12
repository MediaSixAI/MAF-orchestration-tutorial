using System.Text.Json;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Turns the Analyst agent's reply text into a typed <see cref="AnalysisResult"/>. The
    /// Analyst's instructions are the contract (the exact JSON shape to emit); this class is the
    /// receiving side of it: strip a code fence if one is present, then deserialize with
    /// case-insensitive property names.
    /// </summary>
    internal static class AnalysisParser
    {
        /// <summary>
        /// Case-insensitive options, so "topic" and "Topic" both bind to the same property.
        /// </summary>
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Parses the Analyst's reply into an AnalysisResult. Throws InvalidOperationException,
        /// quoting the reply, if it is not JSON of the agreed shape.
        /// </summary>
        internal static AnalysisResult Parse(string analystReply)
        {
            string json = JsonFenceStripper.Strip(analystReply);

            AnalysisResult? result;
            try
            {
                result = JsonSerializer.Deserialize<AnalysisResult>(json, Options);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"The Analyst's reply is not valid JSON: {json}", exception);
            }

            if (result?.Topic is null || result.Summary is null || result.KeyFindings is null)
            {
                throw new InvalidOperationException($"The Analyst's reply does not match AnalysisResult: {json}");
            }

            return result;
        }
    }
}
