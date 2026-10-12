using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>. Same shape as Lesson 1's factory, plus a
    /// tools-accepting overload for the Researcher, the one capstone agent with tool access.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent with the supplied display name and
        /// system instructions, and no tools.
        /// </summary>
        internal static ChatClientAgent CreateAgent(IChatClient chatClient, string name, string instructions)
        {
            return new ChatClientAgent(chatClient, name: name, instructions: instructions);
        }

        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent with the supplied display name,
        /// system instructions, and set of callable tools.
        /// </summary>
        internal static ChatClientAgent CreateAgent(
            IChatClient chatClient, string name, string instructions, IList<AITool> tools)
        {
            return new ChatClientAgent(chatClient, name: name, instructions: instructions, tools: tools);
        }
    }
}
