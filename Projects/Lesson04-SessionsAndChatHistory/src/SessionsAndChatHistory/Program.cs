// Lesson 4 - Sessions & Chat History
// Part 1 demonstrates multi-turn conversation using an AgentSession, so the agent remembers
// earlier turns. Part 2 inspects the InMemoryChatHistoryProvider that backs that session, to
// show what is actually being stored. Part 3 proves that a session can be serialized to JSON,
// written to disk, and restored into a brand-new agent instance - simulating what a real
// process restart looks like - and that the restored session still remembers the conversation.

using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SessionsAndChatHistory
{
    /// <summary>
    /// Entry point that runs a multi-turn demo, a chat-history-inspection demo, and a
    /// session-persistence-across-restart demo, in sequence.
    /// </summary>
    internal sealed class Program
    {
        private const string SessionFilePath = "lesson04-session.json";

        /// <summary>
        /// Runs all three Lesson 4 demos in sequence, printing results to the console.
        /// </summary>
        private static async Task Main(string[] args)
        {
            await RunMultiTurnDemoAsync();
            Console.WriteLine();
            await RunPersistenceAcrossRestartDemoAsync();
        }

        /// <summary>
        /// Creates a single AgentSession and runs two turns against it, showing that the second
        /// turn can refer back to information given in the first without repeating it, then
        /// inspects the InMemoryChatHistoryProvider backing that session to show the raw
        /// messages it is storing.
        /// </summary>
        private static async Task RunMultiTurnDemoAsync()
        {
            IChatClient chatClient = ChatClientFactory.Create();
            ChatClientAgent tripAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "TripPlanner",
                instructions: "You are a concise travel planning assistant. Keep answers to one or two sentences.");

            AgentSession session = await tripAgent.CreateSessionAsync();

            Console.WriteLine("=== Multi-Turn Conversation ===");
            AgentResponse firstResponse = await tripAgent.RunAsync(
                "My name is Avery and I'm planning a trip to Kyoto next spring.", session);
            Console.WriteLine($"Turn 1: {firstResponse.Text}");

            AgentResponse secondResponse = await tripAgent.RunAsync(
                "What's my name, and which city am I visiting?", session);
            Console.WriteLine($"Turn 2: {secondResponse.Text}");

            Console.WriteLine();
            Console.WriteLine("=== Chat History Provider Contents ===");
            InMemoryChatHistoryProvider? provider = tripAgent.GetService<InMemoryChatHistoryProvider>();
            List<ChatMessage>? storedMessages = provider?.GetMessages(session);
            if (storedMessages is not null)
            {
                foreach (ChatMessage message in storedMessages)
                {
                    Console.WriteLine($"[{message.Role}] {message.Text}");
                }
            }
        }

        /// <summary>
        /// Runs a short conversation, serializes the session to a JSON file, then discards the
        /// agent and chat client entirely and builds brand-new ones - simulating a process
        /// restart - before deserializing the file back into a session and continuing the
        /// conversation, to prove the history survived outside of process memory.
        /// </summary>
        private static async Task RunPersistenceAcrossRestartDemoAsync()
        {
            Console.WriteLine("=== Session Persistence Across a Simulated Restart ===");

            IChatClient firstChatClient = ChatClientFactory.Create();
            ChatClientAgent firstAgent = AgentFactory.CreateAgent(
                firstChatClient,
                name: "TripPlanner",
                instructions: "You are a concise travel planning assistant. Keep answers to one or two sentences.");

            AgentSession firstSession = await firstAgent.CreateSessionAsync();
            AgentResponse beforeRestart = await firstAgent.RunAsync(
                "My name is Priya and I'm planning a trip to Lisbon in the fall.", firstSession);
            Console.WriteLine($"Before restart: {beforeRestart.Text}");

            JsonElement serializedSession = await firstAgent.SerializeSessionAsync(firstSession);
            await File.WriteAllTextAsync(SessionFilePath, serializedSession.GetRawText());
            Console.WriteLine($"Session serialized to '{SessionFilePath}'.");

            // Everything above this line is discarded here to stand in for a real process exit.
            // Nothing from firstAgent, firstChatClient, or firstSession carries forward - only the
            // JSON file on disk does.

            IChatClient secondChatClient = ChatClientFactory.Create();
            ChatClientAgent secondAgent = AgentFactory.CreateAgent(
                secondChatClient,
                name: "TripPlanner",
                instructions: "You are a concise travel planning assistant. Keep answers to one or two sentences.");

            string savedJson = await File.ReadAllTextAsync(SessionFilePath);
            using JsonDocument savedDocument = JsonDocument.Parse(savedJson);
            AgentSession restoredSession = await secondAgent.DeserializeSessionAsync(savedDocument.RootElement);

            AgentResponse afterRestart = await secondAgent.RunAsync(
                "What's my name, and which city am I visiting?", restoredSession);
            Console.WriteLine($"After restart:  {afterRestart.Text}");

            File.Delete(SessionFilePath);
        }
    }
}
