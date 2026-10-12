// Lesson 12 - Sequential Pattern
// Chains three agents into a fixed blog-writing pipeline via AgentWorkflowBuilder.BuildSequential
// - each agent consumes what came before it and adds its own contribution. Runs the same
// three-agent pipeline twice: once with the default full-transcript chaining, once with
// chainOnlyAgentResponses set to true, to show how that one flag changes both what each agent
// can see and what the workflow's final output actually contains.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace SequentialPattern
{
    /// <summary>
    /// Entry point that builds a three-agent blog-writing pipeline and runs it twice against
    /// the same topic, contrasting the default chaining behavior with
    /// <c>chainOnlyAgentResponses: true</c>.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds three agents once, then chains them into two differently-configured
        /// sequential workflows and runs both against the same input topic.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent outlineAgent = AgentFactory.CreateAgent(
                chatClient, "OutlineAgent", "Write a concise 3-point outline for a blog post on the given topic.");
            ChatClientAgent draftAgent = AgentFactory.CreateAgent(
                chatClient, "DraftAgent", "Write a short two-sentence draft based on the outline you received.");
            ChatClientAgent editorAgent = AgentFactory.CreateAgent(
                chatClient, "EditorAgent", "Polish the draft you received into a single tight, punchy sentence.");

            const string topic = "sustainable travel";

            Console.WriteLine("=== Default chaining (each agent sees the full running transcript) ===");
            Workflow defaultWorkflow = AgentWorkflowBuilder.BuildSequential([outlineAgent, draftAgent, editorAgent]);
            await RunPipelineAsync(defaultWorkflow, topic);

            Console.WriteLine();
            Console.WriteLine("=== chainOnlyAgentResponses: true (each agent sees only the previous agent's reply) ===");
            Workflow chainOnlyWorkflow = AgentWorkflowBuilder.BuildSequential(
                chainOnlyAgentResponses: true, [outlineAgent, draftAgent, editorAgent]);
            await RunPipelineAsync(chainOnlyWorkflow, topic);
        }

        /// <summary>
        /// Runs a sequential agent workflow against a single topic and prints the resulting
        /// transcript, whatever its length turns out to be.
        /// </summary>
        private static async Task RunPipelineAsync(Workflow workflow, string topic)
        {
            Run result = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, topic));

            foreach (WorkflowEvent evt in result.NewEvents)
            {
                if (evt is WorkflowOutputEvent outputEvent && outputEvent.Data is List<ChatMessage> transcript)
                {
                    Console.WriteLine($"Final output contains {transcript.Count} message(s):");
                    foreach (ChatMessage message in transcript)
                    {
                        string author = string.IsNullOrEmpty(message.AuthorName) ? message.Role.ToString() : message.AuthorName;
                        Console.WriteLine($"  [{author}] {message.Text}");
                    }
                }
            }
        }
    }
}
