using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedTools
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>, optionally wiring in tools - whether
    /// local function tools, hosted tools, or a mix. This factory shape is reintroduced across
    /// lessons so that assembling the Lesson 22-24 capstone is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name, system
        /// instructions that define its persona, and an optional set of tools the agent may use
        /// while responding.
        /// </summary>
        internal static ChatClientAgent CreateAgent(
            IChatClient chatClient,
            string name,
            string instructions,
            IList<AITool>? tools = null)
        {
            return new ChatClientAgent(chatClient, name: name, instructions: instructions, tools: tools);
        }
    }
}
