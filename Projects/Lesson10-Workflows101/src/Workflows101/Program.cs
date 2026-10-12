// Lesson 10 - Workflows 101 (Graph Model)
// Introduces MAF workflows as directed graphs: three plain C# executors are wired into a linear
// pipeline with WorkflowBuilder and run both non-streaming and streaming. A short bonus section
// then shows that an AIAgent can be a workflow node too, with the agent as the workflow's own
// starting executor.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace Workflows101
{
    /// <summary>
    /// Entry point that builds and runs a three-stage text-processing workflow twice (once
    /// non-streaming, once streaming), then runs a minimal single-agent-node workflow.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Runs the function-executor pipeline in both execution modes, then a minimal
        /// agent-as-executor workflow.
        /// </summary>
        private static async Task Main(string[] args)
        {
            const string inputText = "  Hello WORLD from a Workflow  ";

            Console.WriteLine("=== Non-streaming execution ===");
            Workflow nonStreamingWorkflow = BuildTextPipelineWorkflow();
            Run result = await InProcessExecution.RunAsync(nonStreamingWorkflow, inputText);
            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvt)
                {
                    Console.WriteLine(outputEvt.Data);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== Streaming execution ===");
            Workflow streamingWorkflow = BuildTextPipelineWorkflow();
            StreamingRun run = await InProcessExecution.RunStreamingAsync(streamingWorkflow, inputText);
            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is ExecutorCompletedEvent executorCompleted)
                {
                    Console.WriteLine($"  {executorCompleted.ExecutorId} completed.");
                }
                if (evt is WorkflowOutputEvent outputEvt)
                {
                    Console.WriteLine(outputEvt.Data);
                }
            }

            Console.WriteLine();
            Console.WriteLine("=== Bonus: a single agent as a workflow node ===");
            await RunSingleAgentWorkflowAsync();
        }

        /// <summary>
        /// Builds a fresh three-stage workflow instance. A new instance is built for every run
        /// because a <see cref="Workflow"/> can only be owned by one runner at a time.
        /// </summary>
        private static Workflow BuildTextPipelineWorkflow()
        {
            TextNormalizerExecutor normalizer = new();
            WordCountAnnotatorExecutor annotator = new();
            ReportFormatterExecutor formatter = new();

            return new WorkflowBuilder(normalizer)
                .AddEdge(normalizer, annotator)
                .AddEdge(annotator, formatter)
                .WithOutputFrom(formatter)
                .Build();
        }

        /// <summary>
        /// Builds and runs a one-node workflow whose sole executor is an OpenAI-backed
        /// ChatClientAgent, demonstrating that agents and plain functions are interchangeable
        /// as workflow nodes.
        /// </summary>
        private static async Task RunSingleAgentWorkflowAsync()
        {
            IChatClient chatClient = ChatClientFactory.Create();
            ChatClientAgent greeterAgent = new(
                chatClient,
                name: "GreeterAgent",
                instructions: "Respond with one short, friendly sentence greeting the user by " +
                    "restating their message back cheerfully.");

            Workflow agentWorkflow = new WorkflowBuilder(greeterAgent)
                .WithOutputFrom(greeterAgent)
                .Build();

            await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
                agentWorkflow, new ChatMessage(ChatRole.User, "Hello World!"));

            // Agents wrapped as workflow executors cache incoming messages and only start
            // processing once they receive a TurnToken.
            await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

            await foreach (WorkflowEvent evt in run.WatchStreamAsync())
            {
                if (evt is AgentResponseUpdateEvent update)
                {
                    Console.Write(update.Update.Text);
                }
            }

            Console.WriteLine();
        }
    }
}
