namespace ContextProviders
{
    /// <summary>
    /// Computes cosine similarity between two embedding vectors - the standard measure of how
    /// semantically close two pieces of text are once embedded into the same vector space.
    /// </summary>
    internal static class CosineSimilarity
    {
        /// <summary>
        /// Returns a value between -1 and 1 indicating how similar two vectors are in direction,
        /// independent of their magnitude. Values close to 1 mean "very semantically similar."
        /// </summary>
        internal static float Compute(ReadOnlySpan<float> first, ReadOnlySpan<float> second)
        {
            float dotProduct = 0f;
            float firstMagnitude = 0f;
            float secondMagnitude = 0f;

            for (int i = 0; i < first.Length; i++)
            {
                dotProduct += first[i] * second[i];
                firstMagnitude += first[i] * first[i];
                secondMagnitude += second[i] * second[i];
            }

            float denominator = MathF.Sqrt(firstMagnitude) * MathF.Sqrt(secondMagnitude);
            return denominator == 0f ? 0f : dotProduct / denominator;
        }
    }
}
