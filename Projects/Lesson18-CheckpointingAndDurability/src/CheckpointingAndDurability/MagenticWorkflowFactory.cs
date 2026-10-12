using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CheckpointingAndDurability
{
    /// <summary>
    /// Builds the ManagerAgent/ResearchAgent/WriterAgent Magentic-One workflow used by this
    /// lesson. Deliberately called twice - once before a simulated restart, once after - so it
    /// must build an identical executor graph (same agent ids) both times for a saved checkpoint
    /// to reattach correctly to the second, entirely separate instance.
    /// </summary>
    internal static class MagenticWorkflowFactory
    {
        /// <summary>
        /// Builds a fresh ManagerAgent, ResearchAgent, and WriterAgent from the given chat client
        /// and wires them into a Magentic-One workflow with plan sign-off required and a bounded
        /// round count.
        /// </summary>
        internal static Workflow Build(IChatClient chatClient)
        {
            ChatClientAgent managerAgent = AgentFactory.CreateAgent(
                chatClient, "manager-agent", "ManagerAgent",
                "You are the orchestrator that plans and delegates tasks to your team.");

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient, "research-agent", "ResearchAgent",
                "You are a trivia researcher. When asked, list exactly two little-known, " +
                    "verifiable facts about the given topic as a short bullet list. Never write " +
                    "prose paragraphs, only bullets.");

            ChatClientAgent writerAgent = AgentFactory.CreateAgent(
                chatClient, "writer-agent", "WriterAgent",
                "You are a copywriter. When given a list of facts, weave them into exactly one " +
                    "short, engaging prose paragraph. Never produce bullet lists, only flowing prose.");

            List<AIAgent> team = [researchAgent, writerAgent];

            return new MagenticWorkflowBuilder(managerAgent)
                .AddParticipants(team)
                .WithMaxRounds(8)
                .WithMaxStalls(2)
                .RequirePlanSignoff(true)
                .Build();
        }
    }
}
