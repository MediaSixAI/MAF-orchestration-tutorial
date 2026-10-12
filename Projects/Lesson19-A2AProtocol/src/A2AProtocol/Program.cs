// Lesson 19 - A2A Protocol
// Hosts a single OpenAI-backed ResearchAgent behind the Agent-to-Agent (A2A) protocol so that any
// A2A-compliant client - regardless of framework, language, or process - can discover it via its
// published agent card and call it exactly like a local AIAgent. This is the bridge from the
// in-process orchestration patterns of Lessons 10-18 to an agent that lives in its own process and is
// reachable over HTTP - the same hosting shape Lesson 24 uses when it serves the finished capstone
// pipeline as one agent over the Responses protocol. Run this project first, then run the companion
// A2AProtocolClient project.

using A2A;
using A2A.AspNetCore;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace A2AProtocol
{
    /// <summary>
    /// Entry point that builds and hosts an OpenAI-backed ResearchAgent as an A2A server, exposing
    /// both its request endpoint and its discoverable agent card over HTTP.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Fixed local address this lesson listens on, set explicitly in code (rather than left to
        /// an environment variable or a generated launchSettings.json profile) so the companion
        /// client project - and this lesson's own instructions - can rely on a reproducible
        /// address no matter how the app is launched.
        /// </summary>
        private const string ListenUrl = "http://localhost:5100";

        /// <summary>
        /// The DI key (and A2A endpoint segment) this lesson's single hosted agent is registered
        /// under.
        /// </summary>
        private const string AgentKey = "research-agent";

        /// <summary>
        /// Registers an OpenAI-backed ResearchAgent in the DI container, wires it up as an A2A
        /// server, maps its HTTP+JSON endpoint and agent card, and starts listening.
        /// </summary>
        private static async Task Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(ListenUrl);

            // 1. Register the agent itself - an ordinary OpenAI-backed ChatClientAgent, built with
            // the same factories every other lesson uses. Nothing about its construction "knows"
            // it is about to be exposed over A2A.
            builder.Services.AddKeyedSingleton<AIAgent>(AgentKey, (_, _) =>
            {
                IChatClient chatClient = ChatClientFactory.Create();
                return AgentFactory.CreateAgent(
                    chatClient,
                    name: "ResearchAgent",
                    instructions: "You are a concise research assistant. Answer factual questions " +
                        "in two sentences or fewer. Never say you cannot access the internet - " +
                        "just answer from what you already know.");
            });

            // 2. Register the A2A server for that agent - this is the actual protocol bridge,
            // resolving the keyed AIAgent above and wrapping it with request/session handling.
            builder.AddA2AServer(AgentKey);

            WebApplication app = builder.Build();

            // 3. Map the agent's HTTP+JSON request endpoint.
            app.MapA2AHttpJson(AgentKey, $"/a2a/{AgentKey}");

            // 4. Publish a discoverable agent card at the A2A well-known path so that a client
            // which only knows this host's base address can find and describe the agent on its own.
            app.MapWellKnownAgentCard(new AgentCard
            {
                Name = "ResearchAgent",
                Description = "A concise research assistant that answers factual questions.",
                Version = "1.0",
                DefaultInputModes = ["text"],
                DefaultOutputModes = ["text"],
                SupportedInterfaces =
                [
                    new AgentInterface
                    {
                        Url = $"{ListenUrl}/a2a/{AgentKey}",
                        ProtocolBinding = ProtocolBindingNames.HttpJson,
                        ProtocolVersion = "1.0",
                    }
                ]
            });

            Console.WriteLine($"ResearchAgent hosted over A2A at {ListenUrl}/a2a/{AgentKey}");
            Console.WriteLine($"Agent card discoverable at {ListenUrl}/.well-known/agent-card.json");
            Console.WriteLine("Waiting for requests. Run A2AProtocolClient in another terminal, or Ctrl+C to stop.");

            await app.RunAsync();
        }
    }
}
