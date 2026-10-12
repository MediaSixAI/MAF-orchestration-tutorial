// Lesson 24 - Capstone Deploy & Review
// Hosts Lesson 23's capstone graph - Researcher -> Analyst -> human-approval request port ->
// Writer - as one agent over the Responses protocol. Workflow.AsAIAgent() turns the graph into an
// AIAgent, and AddFoundryResponses/MapFoundryResponses serve it at POST /responses. When the run
// reaches the approval port, the response carries an approval request; the caller answers it in
// a follow-up request and the same run continues to the Writer. Hosting checkpoints the run, so
// a pending approval survives a restart of this process. Nothing here talks to Azure. Run this
// project first (after building the companion ResearchMcpServer), then run
// CapstoneDeployAndReviewClient.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Entry point that connects to the research MCP server, builds the capstone graph, wraps it
    /// as an agent, and serves it over the Responses protocol.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Address this host listens on, set explicitly in code so launchSettings.json cannot
        /// move it. Overridable via CAPSTONE_LISTEN_URL, because inside a container the host must
        /// bind "http://+:5200" (all interfaces); "http://localhost:5200" is unreachable from
        /// outside the container. See this lesson's Dockerfile.
        /// </summary>
        private static readonly string ListenUrl =
            Environment.GetEnvironmentVariable("CAPSTONE_LISTEN_URL") ?? "http://localhost:5200";

        /// <summary>
        /// Connects to the research MCP server, builds the graph and its agent, registers it with
        /// the Responses protocol handler, and starts listening.
        /// </summary>
        private static async Task Main(string[] args)
        {
            PipelineConfig config = PipelineConfig.Load();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(ListenUrl);

            // Connect once and keep the connection for the life of the host: unlike Lesson 23's
            // short-lived console runs, this process serves many requests.
            await using McpClient mcpClient = await McpServerConnectionFactory.ConnectAsync(config.McpServerPath);
            IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
            Console.WriteLine($"Research tools: {string.Join(", ", mcpTools.Select(t => t.Name))}");

            IChatClient chatClient = ChatClientFactory.Create();
            ChatClientAgent researcher = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.ResearcherName, AgentInstructions.ResearcherPersona,
                tools: [.. mcpTools.Cast<AITool>()]);
            ChatClientAgent analyst = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.AnalystName, AgentInstructions.AnalystPersona);
            ChatClientAgent writer = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.WriterName, AgentInstructions.WriterPersona);

            Workflow workflow = CapstoneWorkflowFactory.Build(researcher, analyst, writer);

            // One agent for the whole graph. The Writer's yielded report becomes the agent's reply
            // text, and the approval port's request becomes an approval request in that reply.
            AIAgent pipeline = workflow.AsAIAgent(
                id: "capstone-pipeline",
                name: "CapstonePipeline",
                description: "Researches a topic, extracts a structured analysis, asks for approval, then writes a report.");

            builder.Services.AddFoundryResponses(pipeline);

            WebApplication app = builder.Build();
            app.MapFoundryResponses();

            Console.WriteLine($"CapstonePipeline is served over the Responses protocol at {ListenUrl}/responses");
            Console.WriteLine("Waiting for requests. Run CapstoneDeployAndReviewClient in another terminal, or press Ctrl+C to stop.");

            await app.RunAsync();
        }
    }
}
