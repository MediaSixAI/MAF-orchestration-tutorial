# Lesson 4 — Sessions & Chat History

## Project Overview

In this lesson we move from single-shot prompts to real conversations: an `AgentSession` that lets an agent remember earlier turns, the `ChatHistoryProvider` mechanism working behind the scenes to make that possible, and — critically — proof that a session can be serialized to JSON, written to disk, and restored into a completely new agent instance, so conversations can survive a real process restart rather than living only in memory.

## Prerequisites

Lessons 1–3 — this lesson assumes familiarity with `IChatClient`, `ChatClientAgent`, `RunAsync`, and `AgentResponse`. This lesson is otherwise standalone: it does not reference any prior project.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AgentSession`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

**Environment variables:**

```powershell
$env:MAF_LLM = "OPENAI"
$env:MAF_MODEL = "gpt-4.1-mini"
$env:OPENAI_API_KEY = "your-openai-api-key"
```

`MAF_LLM` (the provider) and `MAF_MODEL` (the model; on Azure, the deployment name) are both required — `ChatClientFactory` has no built-in defaults and stops with an error naming whichever variable is missing. Get a key at [OpenAI's API keys page](https://platform.openai.com/api-keys), and never hardcode it. Every provider goes through the same OpenAI SDK against its OpenAI-compatible endpoint, so switching provider changes configuration, not agent code:

| `MAF_LLM` | Recommended `MAF_MODEL` | Also set |
|---|---|---|
| `OPENAI` | `gpt-4.1-mini` | `OPENAI_API_KEY` |
| `AZURE` | your `gpt-4.1-mini` deployment name | `AZURE_OPENAI_ENDPOINT`, `AZURE_OPENAI_API_KEY` |
| `GEMINI` | `gemini-2.5-flash` (`ChatClientFactory` rejects `gemini-3*` models: Gemini 3 tool calls fail on the OpenAI-compatible endpoint) | `GEMINI_API_KEY` |
| `OLLAMA` | `qwen3:8b`, served at `http://localhost:11434/v1/` | nothing — no key needed |
| `LMSTUDIO` | the same Qwen3 8B model (Q4_K_M), by the id LM Studio shows for it — e.g. `qwen/qwen3-8b` — served at `http://localhost:1234/v1/` | nothing — no key needed |

The demos and Expected Output in this series use `MAF_LLM=OPENAI` with `MAF_MODEL=gpt-4.1-mini`.

## Core Concepts

- **`AgentSession`** — the abstraction that carries conversation state across multiple `RunAsync` calls. Every session is created by the agent that will use it via `agent.CreateSessionAsync()`, because the agent attaches whatever behaviors (history storage, reducers, etc.) it needs to that specific session. Pass the same `AgentSession` into successive `RunAsync` calls and the agent has access to everything said so far.
- **Without a session, there is no memory.** Every lesson through Lesson 3 called `RunAsync(prompt)` with no session argument — each call was a fresh, stateless turn. That default hasn't changed; a session is something you opt into per conversation, not automatic.
- **`ChatHistoryProvider`** — the pluggable mechanism that actually decides *how* history is stored and replayed. When an agent is backed by a plain `IChatClient` (like the Chat Completions-based client `ChatClientFactory` builds, which has no server-side "conversation" concept of its own), MAF defaults to an `InMemoryChatHistoryProvider`: it keeps the full message list inside the session's state and prepends it to every new request. You can retrieve that provider from the agent via `agent.GetService<InMemoryChatHistoryProvider>()` and call `.GetMessages(session)` to see exactly what's being stored and replayed — there's no hidden magic.
- **Some providers manage history server-side instead.** If the underlying service has its own conversation/thread concept (this doesn't apply to the Chat Completions-based client this lesson uses), the session instead stores an opaque service-side identifier and `ChatClientAgentSession.ConversationId` exposes it, rather than the raw message list living in your process.
- **Serialization is what makes "restart" possible.** `agent.SerializeSessionAsync(session)` returns a `JsonElement` you can write anywhere durable (a file, a database row, blob storage). Later — potentially in an entirely different process — `agent.DeserializeSessionAsync(jsonElement)` rebuilds an equivalent `AgentSession` from that JSON, and passing it into `RunAsync` continues the conversation as if nothing happened. This is what actually persists a conversation past a process restart; keeping the raw session *object* alive in a static field or singleton is not persistence, it just delays the problem.
- **Treat serialized sessions as sensitive, untrusted-input-shaped data.** They can contain full conversation content and identifiers. Store them the way you'd store any other user data at rest, and don't restore one from a source you don't trust — a tampered session could smuggle adversarial content back into the conversation.

## Complete C# Code

**`src/SessionsAndChatHistory/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace SessionsAndChatHistory
{
    /// <summary>
    /// Builds the <see cref="IChatClient"/> for the provider and model chosen by the MAF_LLM and
    /// MAF_MODEL environment variables. Every provider uses the OpenAI SDK against its
    /// OpenAI-compatible endpoint.
    /// </summary>
    internal static class ChatClientFactory
    {
        /// <summary>
        /// Creates the chat client. Throws if a required environment variable is missing or empty,
        /// or if the provider is not recognised.
        /// </summary>
        internal static IChatClient Create()
        {
            Func<string, string> requiredVariable = name =>
                Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                    ? value
                    : throw new InvalidOperationException($"{name} is not set. Set it before running this lesson.");

            string provider = requiredVariable("MAF_LLM");
            string modelId = requiredVariable("MAF_MODEL");

            Uri endpoint;
            string apiKey;

            switch (provider.ToUpperInvariant())
            {
                case "OPENAI":
                    endpoint = new Uri("https://api.openai.com/v1/");
                    apiKey = requiredVariable("OPENAI_API_KEY");
                    break;
                case "AZURE":
                    endpoint = new Uri(new Uri(requiredVariable("AZURE_OPENAI_ENDPOINT")), "/openai/v1/");
                    apiKey = requiredVariable("AZURE_OPENAI_API_KEY");
                    break;
                case "GEMINI":
                    // Gemini 3 tool calls need thought signatures, which this endpoint doesn't carry.
                    if (modelId.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException(
                            $"MAF_MODEL is '{modelId}'. Gemini 3 models fail tool calls over the OpenAI-compatible endpoint; use gemini-2.5-flash.");
                    }

                    endpoint = new Uri("https://generativelanguage.googleapis.com/v1beta/openai/");
                    apiKey = requiredVariable("GEMINI_API_KEY");
                    break;
                case "OLLAMA":
                    endpoint = new Uri("http://localhost:11434/v1/");
                    apiKey = "ollama"; // Ollama ignores the key, but the SDK refuses an empty one
                    break;
                case "LMSTUDIO":
                    endpoint = new Uri("http://localhost:1234/v1/");
                    apiKey = "lm-studio"; // LM Studio ignores the key, but the SDK refuses an empty one
                    break;
                default:
                    throw new InvalidOperationException(
                        $"MAF_LLM is '{provider}'. Set it to OPENAI, AZURE, GEMINI, OLLAMA or LMSTUDIO.");
            }

            return new OpenAIClient(new ApiKeyCredential(apiKey), new OpenAIClientOptions { Endpoint = endpoint })
                .GetChatClient(modelId)
                .AsIChatClient();
        }
    }
}
```

**`src/SessionsAndChatHistory/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SessionsAndChatHistory
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>, optionally wiring in function tools.
    /// This factory shape is reintroduced across lessons so that assembling the Lesson 22-24
    /// capstone is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name, system
        /// instructions that define its persona, and an optional set of function tools the
        /// agent may call while responding.
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
```

**`src/SessionsAndChatHistory/Program.cs`**

```csharp
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
```

**`src/SessionsAndChatHistory/SessionsAndChatHistory.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>SessionsAndChatHistory</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.24.0" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.10.1" />
    <PackageReference Include="OpenAI" Version="2.14.0" />
  </ItemGroup>

</Project>
```

**`SessionsAndChatHistory.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/SessionsAndChatHistory/SessionsAndChatHistory.csproj" />
  </Folder>
</Solution>
```

## Walkthrough

1. **`RunMultiTurnDemoAsync`** calls `tripAgent.CreateSessionAsync()` once, then passes that same `session` into two successive `RunAsync` calls. The second call's prompt — "What's my name, and which city am I visiting?" — has no information in it on its own; the agent can only answer correctly because the session carried the first turn's content forward.
2. **`tripAgent.GetService<InMemoryChatHistoryProvider>()`** retrieves the default history provider MAF attached to this agent automatically, since the Chat Completions-based `IChatClient` from `ChatClientFactory` has no server-side conversation concept of its own. `.GetMessages(session)` returns the exact `List<ChatMessage>` being stored and replayed — both user turns and both assistant replies, in order. This is the "no hidden magic" moment: whatever memory the agent has lives in this plain list.
3. **`RunPersistenceAcrossRestartDemoAsync`** starts a second, independent conversation, then calls `firstAgent.SerializeSessionAsync(firstSession)` to get a `JsonElement`, and writes its raw text to `lesson04-session.json`.
4. **The comment block in the middle is the important part conceptually** — everything built above it (`firstAgent`, `firstChatClient`, `firstSession`) is never referenced again. A brand-new `IChatClient` and `ChatClientAgent` are constructed from scratch, exactly as if this were a fresh process that had never seen the earlier conversation.
5. **`secondAgent.DeserializeSessionAsync(savedDocument.RootElement)`** rebuilds a working `AgentSession` purely from the JSON that was written to disk. Passing that restored session into `RunAsync` and asking the same follow-up question proves the memory came from the file, not from anything still resident in process memory.
6. **Cleanup** deletes the demo file at the end so repeated runs of this lesson start clean; in a real application you'd keep this file (or database row, or blob) around for as long as the conversation should remain resumable.

## Expected Output

Exact wording will vary by run, but the shape will look like:

```
=== Multi-Turn Conversation ===
Turn 1: Okay, Avery. To help plan your Kyoto trip next spring, please tell me what kind of experiences you're looking for or any specific interests you have.
Turn 2: Your name is Avery, and you are visiting Kyoto.

=== Chat History Provider Contents ===
[user] My name is Avery and I'm planning a trip to Kyoto next spring.
[assistant] Okay, Avery. To help plan your Kyoto trip next spring, please tell me what kind of experiences you're looking for or any specific interests you have.
[user] What's my name, and which city am I visiting?
[assistant] Your name is Avery, and you are visiting Kyoto.

=== Session Persistence Across a Simulated Restart ===
Before restart: Lisbon in the fall is a wonderful choice, Priya! How can I assist you in planning your trip?
Session serialized to 'lesson04-session.json'.
After restart:  You are Priya and you are visiting Lisbon.
```

The last line is the payoff: `secondAgent` never received "Priya" or "Lisbon" directly — it only knew because `DeserializeSessionAsync` rebuilt that context from a file on disk.
