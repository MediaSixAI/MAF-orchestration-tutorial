namespace StructuredOutputAndMultimodalInput
{
    /// <summary>
    /// The typed shape requested from the agent via <c>RunAsync&lt;RecipeIdea&gt;</c>. MAF derives
    /// a JSON schema from this class and asks the model to conform to it, then deserializes the
    /// model's JSON response back into an instance of this class - no manual parsing required.
    /// </summary>
    internal sealed class RecipeIdea
    {
        /// <summary>
        /// The name of the suggested dish.
        /// </summary>
        public string? DishName { get; set; }

        /// <summary>
        /// The cuisine the dish belongs to, e.g. "Italian" or "Thai".
        /// </summary>
        public string? CuisineType { get; set; }

        /// <summary>
        /// The estimated total time to cook the dish, in minutes.
        /// </summary>
        public int EstimatedMinutesToCook { get; set; }

        /// <summary>
        /// The list of main ingredients needed for the dish.
        /// </summary>
        public List<string> Ingredients { get; set; } = [];
    }
}
