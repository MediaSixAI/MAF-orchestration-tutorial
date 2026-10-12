using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SimpleSkill
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances. This factory shape is reintroduced across lessons so that assembling the
    /// Lesson 22-24 capstone is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name, system
        /// instructions, and one or more AIContextProvider instances that inject additional
        /// context - memory, retrieved documents, or in this lesson, Agent Skills - into every run.
        /// </summary>
        internal static ChatClientAgent CreateAgentWithContextProviders(
            IChatClient chatClient,
            string name,
            string instructions,
            IList<AIContextProvider> contextProviders)
        {
            ChatClientAgentOptions options = new()
            {
                Name = name,
                ChatOptions = new ChatOptions { Instructions = instructions },
                AIContextProviders = contextProviders,
            };

            return new ChatClientAgent(chatClient, options);
        }
    }
}
