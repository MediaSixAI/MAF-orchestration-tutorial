// Lesson 19 - A2A Protocol (client)
// Discovers the ResearchAgent hosted by the companion A2AProtocol project purely from its
// well-known agent card, then calls it exactly like a local AIAgent - RunAsync and
// RunStreamingAsync behave identically to every prior lesson's in-process agent, even though every
// call now travels over HTTP to a separate process. Start A2AProtocol first; this client
// will fail to connect if the host is not already running.

using A2A;
using Microsoft.Agents.AI;

namespace A2AProtocolClient
{
    /// <summary>
    /// Entry point that discovers the ResearchAgent hosted by A2AProtocol and exercises both
    /// its non-streaming and streaming call paths.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Base address of the running A2AProtocol host. Its agent card is expected at
        /// {HostUrl}/.well-known/agent-card.json.
        /// </summary>
        private const string HostUrl = "http://localhost:5100";

        /// <summary>
        /// Resolves the remote agent card, wraps it as a local AIAgent, and runs one
        /// non-streaming and one streaming request against it.
        /// </summary>
        private static async Task Main(string[] args)
        {
            Console.WriteLine($"Discovering agent card at {HostUrl}/.well-known/agent-card.json ...");
            A2ACardResolver resolver = new(new Uri(HostUrl));

            // GetAIAgentAsync fetches the agent card and wraps the remote endpoint as a standard
            // AIAgent in one call - from here on, this is indistinguishable from an in-process agent.
            AIAgent researchAgent = await resolver.GetAIAgentAsync();
            Console.WriteLine($"Discovered remote agent: {researchAgent.Name}");
            Console.WriteLine();

            Console.WriteLine("=== Non-streaming call ===");
            AgentResponse response = await researchAgent.RunAsync(
                "What galaxy is Earth located in? Answer in one sentence.");
            Console.WriteLine(response.Text);
            Console.WriteLine();

            Console.WriteLine("=== Streaming call ===");
            await foreach (AgentResponseUpdate update in researchAgent.RunStreamingAsync(
                "Name three inner planets of the solar system, one per line."))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    Console.Write(update.Text);
                }
            }
            Console.WriteLine();
        }
    }
}
