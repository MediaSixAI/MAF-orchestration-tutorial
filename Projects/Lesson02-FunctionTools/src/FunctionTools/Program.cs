// Lesson 2 - Function Tools
// Gives an agent two local C# function tools - GetCurrentTimeInCity and ConvertCurrency -
// and lets the model decide when to call them while answering a single prompt that needs
// both. Demonstrates ChatClientAgent's built-in function-calling loop: no manual dispatch
// code is written to detect a tool call, run it, and feed the result back to the model.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FunctionTools
{
    /// <summary>
    /// Entry point that runs a travel-assistant agent equipped with function tools.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Creates a travel-assistant agent with two function tools attached, then asks it a
        /// question that requires calling both tools to answer fully.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            IList<AITool> tools =
            [
                AIFunctionFactory.Create(TravelTools.GetCurrentTimeInCity),
                AIFunctionFactory.Create(TravelTools.ConvertCurrency),
            ];

            ChatClientAgent travelAssistant = AgentFactory.CreateAgent(
                chatClient,
                name: "TravelAssistant",
                instructions: "You are a helpful travel assistant. Use the tools available to you " +
                    "whenever a question depends on the current time or a currency conversion - " +
                    "never guess a time or an exchange rate yourself.",
                tools: tools);

            const string prompt = "I'm in New York and need to call a colleague in Tokyo, and I also want " +
                "to know how much 250 USD is in EUR for my trip. What's the local time in both cities, and " +
                "what is that converted amount?";

            AgentResponse response = await travelAssistant.RunAsync(prompt);
            Console.WriteLine(response.Text);
        }
    }
}
