// Lesson 3 - Structured Output & Multimodal Input
// Part 1 demonstrates forcing an agent to return a typed, parseable result instead of free
// text, using RunAsync<T>. Part 2 demonstrates sending image input alongside text in a single
// ChatMessage and having the agent reason about the image.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace StructuredOutputAndMultimodalInput
{
    /// <summary>
    /// Entry point that runs a structured-output demo followed by a multimodal-input demo.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Runs the recipe-suggestion structured-output demo and the image-analysis
        /// multimodal-input demo in sequence, printing both results to the console.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            await RunStructuredOutputDemoAsync(chatClient);
            Console.WriteLine();
            await RunMultimodalInputDemoAsync(chatClient);
        }

        /// <summary>
        /// Asks a chef agent for a dinner idea and requests the response as a typed
        /// <see cref="RecipeIdea"/> instead of free-form text.
        /// </summary>
        private static async Task RunStructuredOutputDemoAsync(IChatClient chatClient)
        {
            ChatClientAgent chefAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "ChefAgent",
                instructions: "You are a creative chef who suggests simple weeknight dinner recipes.");

            AgentResponse<RecipeIdea> response = await chefAgent.RunAsync<RecipeIdea>(
                "Suggest a quick weeknight dinner idea using chicken and rice.");

            RecipeIdea recipe = response.Result;
            Console.WriteLine("=== Structured Output: RecipeIdea ===");
            Console.WriteLine($"Dish: {recipe.DishName} ({recipe.CuisineType})");
            Console.WriteLine($"Estimated time: {recipe.EstimatedMinutesToCook} minutes");
            Console.WriteLine($"Ingredients: {string.Join(", ", recipe.Ingredients)}");
        }

        /// <summary>
        /// Sends a single ChatMessage containing both a text question and inline image bytes to
        /// a vision-capable agent, so it can describe the contents of the image. The image comes
        /// from <see cref="SampleImage"/> - a small embedded PNG - so this lesson has no network
        /// dependency on an external image host and stays reproducible for every viewer.
        /// </summary>
        private static async Task RunMultimodalInputDemoAsync(IChatClient chatClient)
        {
            ChatClientAgent visionAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "VisionAgent",
                instructions: "You are a helpful assistant that can analyze images and describe them concisely.");

            ChatMessage message = new(ChatRole.User,
            [
                new TextContent("What do you see in this image? Answer in two sentences."),
                new DataContent(SampleImage.GetBytes(), "image/png"),
            ]);

            AgentResponse response = await visionAgent.RunAsync(message);
            Console.WriteLine("=== Multimodal Input: Image Analysis ===");
            Console.WriteLine(response.Text);
        }
    }
}
