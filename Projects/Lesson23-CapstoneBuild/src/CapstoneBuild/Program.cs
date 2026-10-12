// Lesson 23 - Capstone Build
// Builds the capstone as one graph workflow - Researcher (with MCP tool access) -> Analyst
// (structured extraction) -> human-approval request port -> Writer - and runs it with file-based
// checkpointing. "dotnet run" runs the graph until it pauses at the approval port, then exits;
// the pending approval lives only in the checkpoint on disk. "dotnet run -- resume" starts a
// fresh process, restores that checkpoint, asks for the decision at the console, and finishes
// the run.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace CapstoneBuild
{
    /// <summary>
    /// Entry point with two modes: "start" (the default) runs the workflow until it pauses for
    /// approval; "resume" restores the paused run from its checkpoint and completes it.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// The checkpoint session id. Both processes use it to find the same run.
        /// </summary>
        private const string SessionId = "capstone-run";

        /// <summary>
        /// Folder the checkpoints are written to, next to the built executable.
        /// </summary>
        private static readonly DirectoryInfo CheckpointDirectory =
            new(Path.Combine(AppContext.BaseDirectory, "checkpoints"));

        /// <summary>
        /// Dispatches to the start or resume mode based on the first command-line argument.
        /// </summary>
        private static async Task Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "start";

            switch (mode)
            {
                case "start":
                    await StartAsync();
                    break;
                case "resume":
                    await ResumeAsync();
                    break;
                default:
                    Console.WriteLine($"Unknown mode '{mode}'. Use 'dotnet run' to start a run, or " +
                        "'dotnet run -- resume' to review and finish a paused one.");
                    break;
            }
        }

        /// <summary>
        /// Starts a fresh checkpointed run on the configured topic, lets it run until the approval
        /// port raises its request, shows the request, and exits with the run paused on disk.
        /// </summary>
        private static async Task StartAsync()
        {
            PipelineConfig config = PipelineConfig.Load();

            if (CheckpointDirectory.Exists)
            {
                CheckpointDirectory.Delete(recursive: true);
            }

            CheckpointDirectory.Create();

            Console.WriteLine("=== Start: Researcher -> Analyst -> approval port -> Writer ===");
            Console.WriteLine($"Research topic: {config.ResearchTopic}");

            using FileSystemJsonCheckpointStore store = new(CheckpointDirectory);
            CheckpointManager checkpointManager = CheckpointManager.CreateJson(store, customOptions: null);

            await using McpClient mcpClient = await McpServerConnectionFactory.ConnectAsync(config.McpServerPath);
            Workflow workflow = await BuildWorkflowAsync(mcpClient);

            // The start node speaks the chat protocol: send the topic as a user message, then a
            // TurnToken to tell it to take its turn.
            await using Run run = await InProcessExecution.RunAsync(
                workflow, new ChatMessage(ChatRole.User, config.ResearchTopic), checkpointManager, SessionId);
            await run.ResumeAsync(CancellationToken.None, [new TurnToken(emitEvents: false)]);

            RequestInfoEvent? pending = ReportErrors(run.NewEvents).OfType<RequestInfoEvent>().LastOrDefault();
            if (pending is null || !pending.Request.TryGetDataAs(out ToolApprovalRequestContent? request))
            {
                Console.WriteLine($"The run did not pause for approval. Status: {await run.GetStatusAsync()}");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== Paused for human approval ===");
            Console.WriteLine(ReportApprovalRequest.Describe(request));
            Console.WriteLine();
            Console.WriteLine($"Status: {await run.GetStatusAsync()}");
            Console.WriteLine($"Checkpoint saved: {run.LastCheckpoint?.CheckpointId}");
            Console.WriteLine("This process now exits. To review and finish the run, use:");
            Console.WriteLine("  dotnet run -- resume");
        }

        /// <summary>
        /// Restores the latest checkpoint into a freshly built graph, shows the pending approval
        /// request, reads the decision from the console, and resumes the run to completion.
        /// </summary>
        private static async Task ResumeAsync()
        {
            PipelineConfig config = PipelineConfig.Load();

            if (!CheckpointDirectory.Exists)
            {
                Console.WriteLine("No paused run found. Start one first with 'dotnet run'.");
                return;
            }

            CheckpointInfo? latest;
            using (FileSystemJsonCheckpointStore store = new(CheckpointDirectory))
            {
                CheckpointManager checkpointManager = CheckpointManager.CreateJson(store, customOptions: null);
                latest = await checkpointManager.GetLatestCheckpointAsync(SessionId);
                if (latest is null)
                {
                    Console.WriteLine("No paused run found. Start one first with 'dotnet run'.");
                    return;
                }

                Console.WriteLine($"=== Resume: restoring checkpoint {latest.CheckpointId} in a new process ===");

                await using McpClient mcpClient = await McpServerConnectionFactory.ConnectAsync(config.McpServerPath);
                Workflow workflow = await BuildWorkflowAsync(mcpClient);

                await using Run run = await InProcessExecution.ResumeAsync(workflow, latest, checkpointManager);

                RequestInfoEvent? pending = ReportErrors(run.NewEvents).OfType<RequestInfoEvent>().LastOrDefault();
                if (pending is null || !pending.Request.TryGetDataAs(out ToolApprovalRequestContent? request))
                {
                    Console.WriteLine("Nothing is waiting for approval in this run.");
                    return;
                }

                Console.WriteLine(ReportApprovalRequest.Describe(request));
                Console.WriteLine();
                Console.Write("Approve this analysis for the final report? [y/N] ");
                bool approved = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine();

                ToolApprovalResponseContent decision = request.CreateResponse(
                    approved, approved ? null : "Rejected at the console.");
                await run.ResumeAsync([pending.Request.CreateResponse(decision)]);

                foreach (WorkflowOutputEvent output in ReportErrors(run.NewEvents).OfType<WorkflowOutputEvent>())
                {
                    Console.WriteLine(approved ? "=== Final report ===" : "=== Result ===");
                    Console.WriteLine(output.Data);
                }

                Console.WriteLine();
                Console.WriteLine($"Status: {await run.GetStatusAsync()}");
            }

            // The run is finished, so its checkpoints have nothing left to resume.
            CheckpointDirectory.Delete(recursive: true);
        }

        /// <summary>
        /// Lists the research server's tools and builds the three agents and the capstone graph.
        /// Both modes build the identical graph, so a checkpoint from one restores onto the other.
        /// </summary>
        private static async Task<Workflow> BuildWorkflowAsync(McpClient mcpClient)
        {
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

            return CapstoneWorkflowFactory.Build(researcher, analyst, writer);
        }

        /// <summary>
        /// Prints any executor failure or workflow error among the given events, and returns the
        /// events so the caller can keep reading them.
        /// </summary>
        private static List<WorkflowEvent> ReportErrors(IEnumerable<WorkflowEvent> events)
        {
            List<WorkflowEvent> list = [.. events];
            foreach (WorkflowEvent evt in list)
            {
                switch (evt)
                {
                    case ExecutorFailedEvent failed:
                        Console.WriteLine($"Executor '{failed.ExecutorId}' failed: {failed.Data}");
                        break;
                    case WorkflowErrorEvent error:
                        Console.WriteLine($"Workflow error: {error.Exception?.Message}");
                        break;
                }
            }

            return list;
        }
    }
}
