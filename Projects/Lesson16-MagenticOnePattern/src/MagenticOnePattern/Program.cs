// Lesson 16 - Magentic-One Pattern
// A lead ManagerAgent decomposes a user request into a plan, tracks a progress ledger turn by
// turn, and delegates each step to whichever specialist agent (ResearchAgent, WriterAgent) it
// decides should act next - the most autonomous orchestration pattern in the series. Unlike every
// prior AgentWorkflowBuilder pattern (Sequential, Concurrent, Handoff, Group Chat), the manager
// itself both plans the work AND synthesizes the single final answer, rather than the workflow's
// output being the raw multi-agent transcript.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Specialized.Magentic;
using Microsoft.Extensions.AI;

namespace MagenticOnePattern
{
    /// <summary>
    /// Entry point that builds a manager agent and two specialists, wires them into a Magentic-One
    /// workflow, and runs a single research-and-write request through it while narrating the
    /// manager's plan and turn-by-turn progress ledger as the run proceeds.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds ManagerAgent, ResearchAgent, and WriterAgent, wires them into a Magentic-One
        /// workflow with an explicit round cap, and runs one request through it end to end.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent managerAgent = AgentFactory.CreateAgent(
                chatClient, "ManagerAgent",
                "You are the orchestrator that plans and delegates tasks to your team.");

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient, "ResearchAgent",
                "You are a trivia researcher. When asked, list exactly two little-known, " +
                    "verifiable facts about the given topic as a short bullet list. Never write " +
                    "prose paragraphs, only bullets.");

            ChatClientAgent writerAgent = AgentFactory.CreateAgent(
                chatClient, "WriterAgent",
                "You are a copywriter. When given a list of facts, weave them into exactly one " +
                    "short, engaging prose paragraph. Never produce bullet lists, only flowing prose.");

            List<AIAgent> team = [researchAgent, writerAgent];

            // RequirePlanSignoff defaults to true - the workflow would otherwise pause after the
            // plan is created and wait for an external approval response before delegating any
            // work. That pause/resume mechanism (RequestInfoEvent -> ExternalRequest/ExternalResponse
            // -> Run.ResumeAsync) is the same generic port Lesson 17 (Human-in-the-Loop) formalizes,
            // so it is disabled here to keep this lesson focused on the planning/delegation loop.
            Workflow workflow = new MagenticWorkflowBuilder(managerAgent)
                .AddParticipants(team)
                .WithMaxRounds(8)
                .WithMaxStalls(2)
                .RequirePlanSignoff(false)
                .Build();

            Console.WriteLine("=== Magentic-One: manager plans, delegates, and synthesizes the final answer ===");
            const string request = "Get two little-known facts about the planet Saturn from the " +
                "researcher, then have the writer turn them into one short engaging paragraph.";
            Console.WriteLine($"User: {request}");
            Console.WriteLine();

            Run run = await InProcessExecution.RunAsync(workflow, new ChatMessage(ChatRole.User, request));

            foreach (WorkflowEvent evt in run.NewEvents)
            {
                switch (evt)
                {
                    case MagenticPlanCreatedEvent planEvent:
                        Console.WriteLine("--- Manager's fact sheet and plan ---");
                        Console.WriteLine(planEvent.FullTaskLedger.Text);
                        Console.WriteLine();
                        break;

                    case MagenticProgressLedgerUpdatedEvent ledgerEvent:
                        MagenticProgressLedger ledger = ledgerEvent.ProgressLedger;
                        Console.WriteLine(
                            $"--- Progress ledger: next speaker = {ledger.NextSpeaker ?? "(none)"}, " +
                            $"satisfied = {ledger.IsRequestSatisfied} ---");
                        Console.WriteLine($"    {ledger.InstructionOrQuestion}");
                        Console.WriteLine();
                        break;

                    case WorkflowOutputEvent outputEvent when outputEvent.Data is List<ChatMessage> finalMessages:
                        Console.WriteLine("--- Final answer ---");
                        foreach (ChatMessage message in finalMessages)
                        {
                            string author = string.IsNullOrEmpty(message.AuthorName) ? message.Role.ToString() : message.AuthorName;
                            Console.WriteLine($"  [{author}] {message.Text}");
                        }
                        break;
                }
            }
        }
    }
}
