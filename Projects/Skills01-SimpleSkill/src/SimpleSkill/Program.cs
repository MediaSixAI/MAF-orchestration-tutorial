// Skills 01 - Simple Skill (Agent Skills, file-based, no scripts)
// Loads a single file-based Agent Skill - code-review-checklist - built from a SKILL.md file and
// one bundled reference document, and walks the agent through the full progressive-disclosure
// flow: advertise -> load_skill -> read_skill_resource. Both of those tools require approval by
// default, resolved here with the same ToolApprovalRequestContent / ToolApprovalResponseContent
// exchange Lesson 9 introduced for local function tools.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SimpleSkill
{
    /// <summary>
    /// Entry point that builds a code-review agent equipped with a single file-based skill, asks
    /// it to review a snippet, and approves every pending skill-tool call along the way.
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
        /// Builds the skills-equipped agent, submits one review prompt, and drives the approval
        /// loop to completion before printing the agent's final review.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            AgentSkillsProvider skillsProvider = new(
                Path.Combine(AppContext.BaseDirectory, "skills"));

            ChatClientAgent reviewAgent = AgentFactory.CreateAgentWithContextProviders(
                chatClient,
                name: "CodeReviewAgent",
                instructions: "You are a C# code reviewer. Use the available skills when they match the task.",
                contextProviders: [skillsProvider]);

            AgentSession session = await reviewAgent.CreateSessionAsync();
            AgentResponse response = await reviewAgent.RunAsync(
                $"Review this method against our code review checklist:\n\n{CodeSnippet}", session);

            response = await ApproveAllPendingAsync(reviewAgent, session, response);

            Console.WriteLine(response.Text);
        }

        /// <summary>
        /// Repeatedly collects any pending <see cref="ToolApprovalRequestContent"/> items from the
        /// given response, approves each one, and resumes the same session - looping until the
        /// agent stops asking for approval and returns a final answer.
        /// </summary>
        private static async Task<AgentResponse> ApproveAllPendingAsync(
            ChatClientAgent agent, AgentSession session, AgentResponse response)
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
