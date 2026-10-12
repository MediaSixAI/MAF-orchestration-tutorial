// Lesson 1 - Hello Agent & Personas
// Builds two ChatClientAgent instances from the same OpenAI-backed IChatClient but gives
// them different instructions, then runs an identical prompt against both so the effect
// of persona design is visible side by side.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelloAgentAndPersonas
{
    /// <summary>
    /// Entry point that runs the persona comparison demo.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Creates a default "helpful assistant" persona and a "pirate navigator" persona,
        /// runs the same prompt against each, and prints both responses to the console.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent helpfulAssistant = AgentFactory.CreateAgent(
                chatClient,
                name: "HelpfulAssistant",
                instructions: "You are a concise, helpful assistant. Answer plainly and factually in two or three sentences.");

            ChatClientAgent pirateNavigator = AgentFactory.CreateAgent(
                chatClient,
                name: "PirateNavigator",
                instructions: "You are a salty, superstitious 18th-century pirate ship's navigator. " +
                    "Speak in pirate dialect, reference the sea, the stars, and your charts in every answer, " +
                    "and never break character. Keep answers to two or three sentences.");

            const string prompt = "How do I stay on course when I'm not sure which direction to take?";
            //Console.Write("Enter a prompt: ");
            //string? prompt = Console.ReadLine() ?? "How do I stay on course when I'm not sure which direction to take?";
            //if (string.IsNullOrWhiteSpace(prompt))
            //{
            //    Console.WriteLine("No prompt provided. Exiting.");
            //    return;
            //}   

            AgentResponse assistantResponse = await helpfulAssistant.RunAsync(prompt);
            Console.WriteLine("=== HelpfulAssistant ===");
            Console.WriteLine(assistantResponse.Text);
            Console.WriteLine();

            AgentResponse pirateResponse = await pirateNavigator.RunAsync(prompt);
            Console.WriteLine("=== PirateNavigator ===");
            Console.WriteLine(pirateResponse.Text);
        }
    }
}
