using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CheckpointingAndDurability
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>. Unlike prior lessons' AgentFactory,
    /// this one requires an explicit, caller-chosen agent id rather than letting one be
    /// auto-generated - see this lesson's Core Concepts section for why that distinction is the
    /// whole point of a checkpoint surviving a rebuilt workflow graph.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a stable id, a
        /// display name, and system instructions that define its persona. The id must be the
        /// same every time this agent is reconstructed - including after a simulated process
        /// restart - for a saved checkpoint to correctly reattach to it.
        /// </summary>
        internal static ChatClientAgent CreateAgent(IChatClient chatClient, string id, string name, string instructions)
        {
            return new ChatClientAgent(chatClient, new ChatClientAgentOptions
            {
                Id = id,
                Name = name,
                ChatOptions = new ChatOptions { Instructions = instructions },
            });
        }
    }
}
