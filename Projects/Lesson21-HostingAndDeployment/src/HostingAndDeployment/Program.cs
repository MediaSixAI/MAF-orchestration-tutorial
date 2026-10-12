// Lesson 21 - Hosting & Deployment
// Self-hosts a two-agent workflow behind Microsoft Foundry Agent Service's hosting contract, the
// Responses protocol. ResearchAgent and WriterAgent are composed into a sequential workflow,
// workflow.AsAIAgent() turns that workflow into a single AIAgent, and AddFoundryResponses /
// MapFoundryResponses serve it as POST /responses from a plain ASP.NET Core app. Nothing here
// talks to Azure: the same code runs on a developer machine and inside a Foundry Hosted Agent
// container deployed with `azd provision` / `azd deploy`.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HostingAndDeployment
{
    /// <summary>
    /// Entry point that builds a ResearchAgent to WriterAgent workflow, wraps it as an
    /// <see cref="AIAgent"/>, and hosts it behind the Foundry Responses protocol.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Fixed local port this lesson listens on. It matches the port `azd ai agent run` uses for
        /// local testing, and setting it in code means launchSettings.json cannot pick a different
        /// one (the same choice Lesson 19 made for A2A hosting).
        /// </summary>
        private const string ListenUrl = "http://localhost:8088";

        /// <summary>
        /// Builds the two agents, composes them into a sequential workflow, exposes the workflow
        /// as an AIAgent, registers it with the Foundry Responses protocol handler, and starts
        /// listening for requests.
        /// </summary>
        private static void Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "ResearchAgent",
                instructions: "You are a researcher. Answer the user's latest question with two or three " +
                    "short, factual bullet points. Use the earlier conversation to resolve words like " +
                    "'there' or 'it'.");

            ChatClientAgent writerAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "WriterAgent",
                instructions: "You are a writer. Rewrite the researcher's bullet points from the previous " +
                    "message as one concise, friendly sentence. Output only that sentence.");

            Workflow workflow = AgentWorkflowBuilder.BuildSequential("ResearchThenWrite", [researchAgent, writerAgent]);

            AIAgent workflowAgent = workflow.AsAIAgent(
                id: "research-then-write",
                name: "ResearchThenWrite",
                description: "Researches a question, then answers it in one friendly sentence.");

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(ListenUrl);
            builder.Services.AddFoundryResponses(workflowAgent);

            WebApplication app = builder.Build();
            app.MapFoundryResponses();

            app.Run();
        }
    }
}
