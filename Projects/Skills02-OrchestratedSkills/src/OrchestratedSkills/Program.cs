// Skills 02 - Orchestrated Skills (Multi-Source Agent Skills)
// Composes three Agent Skills of different authoring styles - file-based, class-based, and
// inline/code-defined - into a single AgentSkillsProviderBuilder pipeline on one agent, then
// selectively automates approval: load_skill and read_skill_resource auto-approve via
// AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule, while run_skill_script still pauses for an
// explicit decision, exactly like Lesson 9's ToolApprovalRequestContent / ToolApprovalResponseContent
// exchange.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// Entry point that builds an agent backed by three composed skills, then runs one prompt
    /// against the auto-approved file-based skill and one prompt against the still-gated
    /// class-based skill's script.
    /// </summary>
    internal sealed class Program
    {
        private static readonly string CodeSnippet = """
            public async void SaveOrder(Order order)
            {
                try
                {
                    await _repository.SaveAsync(order);
                }
                catch (Exception)
                {
                }
            }
            """;

        /// <summary>
        /// Builds the multi-source skills provider, wraps the agent with selective tool-approval
        /// automation, and runs the two demonstration prompts in turn.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            AgentSkillsProvider skillsProvider = new AgentSkillsProviderBuilder()
                .UseFileSkill(Path.Combine(AppContext.BaseDirectory, "skills"))
                .UseFileScriptRunner(SubprocessScriptRunner.RunAsync)
                .UseSkill(new UnitConverterSkill())
                .UseSkill(ProjectInfoSkillFactory.Create())
                .UseFilter((skill, context) => skill.Frontmatter.Name != "experimental-skill")
                .Build();

            ChatClientAgent baseAgent = AgentFactory.CreateAgentWithContextProviders(
                chatClient,
                name: "SkillsAgent",
                instructions: "You are a helpful assistant. Use the available skills when they match the task.",
                contextProviders: [skillsProvider]);

            AIAgent skillsAgent = baseAgent
                .AsBuilder()
                .UseToolApproval(new ToolApprovalAgentOptions
                {
                    // Auto-approve read-only skill tools (load_skill, read_skill_resource).
                    // run_skill_script still requires an explicit decision below.
                    AutoApprovalRules = [AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule],
                })
                .Build();

            AgentSession session = await skillsAgent.CreateSessionAsync();

            Console.WriteLine("=== Prompt 1: file-based skill (read-only, auto-approved) ===");
            AgentResponse checklistResponse = await skillsAgent.RunAsync(
                $"Review this method against our code review checklist:\n\n{CodeSnippet}", session);
            checklistResponse = await ApproveAllPendingAsync(skillsAgent, session, checklistResponse);
            Console.WriteLine(checklistResponse.Text);

            Console.WriteLine();
            Console.WriteLine("=== Prompt 2: class-based skill script (still requires approval) ===");
            AgentResponse conversionResponse = await skillsAgent.RunAsync(
                "Convert 26.2 miles to kilometers using the unit-converter skill.", session);
            conversionResponse = await ApproveAllPendingAsync(skillsAgent, session, conversionResponse);
            Console.WriteLine(conversionResponse.Text);
        }

        /// <summary>
        /// Repeatedly collects any pending <see cref="ToolApprovalRequestContent"/> items from the
        /// given response, approves each one, and resumes the same session - looping until the
        /// agent stops asking for approval and returns a final answer. For this lesson's first
        /// prompt, the auto-approval rule means this loop finds nothing pending at all.
        /// </summary>
        private static async Task<AgentResponse> ApproveAllPendingAsync(
            AIAgent agent, AgentSession session, AgentResponse response)
        {
            List<ToolApprovalRequestContent> pending = CollectApprovalRequests(response);

            while (pending.Count > 0)
            {
                List<ChatMessage> decisions = [];
                foreach (ToolApprovalRequestContent request in pending)
                {
                    if (request.ToolCall is FunctionCallContent functionCall)
                    {
                        Console.WriteLine($"Approval requested to call '{functionCall.Name}'.");
                    }

                    Console.WriteLine("Decision: APPROVED");
                    decisions.Add(new ChatMessage(
                        ChatRole.User,
                        [request.CreateResponse(true, reason: "Approved by reviewer.")]));
                }

                response = await agent.RunAsync(decisions, session);
                pending = CollectApprovalRequests(response);
            }

            return response;
        }

        /// <summary>
        /// Extracts every pending tool approval request from a response's messages.
        /// </summary>
        private static List<ToolApprovalRequestContent> CollectApprovalRequests(AgentResponse response)
        {
            return response.Messages
                .SelectMany(message => message.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();
        }
    }
}
