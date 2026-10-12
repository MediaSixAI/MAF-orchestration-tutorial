# Lesson 1 — Hello Agent & Personas

## Project Overview

In this lesson we stand up our first agent using Microsoft Agent Framework (MAF): a `ChatClientAgent` wrapping OpenAI through the `IChatClient` abstraction from `Microsoft.Extensions.AI`. We then prove that an agent's personality is entirely a function of its `instructions` string by running the identical prompt through two agents — a plain helpful assistant and an in-character pirate navigator — built from the exact same underlying model connection. This is the foundation every later lesson in the series builds on.

## Prerequisites

None. This is Lesson 1 — the series starts here. You should have the .NET 10 SDK and Visual Studio 2026 installed.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AgentResponse`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

**First-party support, no community adapter needed.** Microsoft publishes `Microsoft.Extensions.AI.OpenAI` directly (part of the `dotnet/extensions` repo) — it wraps the official `OpenAI` SDK's `ChatClient` with an `AsIChatClient()` extension method. Because several other providers expose OpenAI-compatible endpoints, these same two packages reach them too, which is how `ChatClientFactory` keeps the choice of provider a run-time setting instead of a code change.

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

- **`IChatClient`** (`Microsoft.Extensions.AI`) — the model-agnostic abstraction for "something that can turn a list of chat messages into a response." MAF never talks to OpenAI, Azure, Gemini, Ollama or LM Studio directly — it only ever talks to an `IChatClient`. This is what lets the exact same agent code run against any provider.
- **`AIAgent`** — the abstract base type for every agent in MAF. It exposes `RunAsync` and `RunStreamingAsync`.
- **`ChatClientAgent`** — the concrete `AIAgent` implementation used in this lesson. It wraps an `IChatClient` and adds agent-level concerns on top: a `Name`, and `Instructions` that get sent as persistent system guidance on every run.
- **Instructions = persona.** There is no separate "persona" API in MAF. An agent's voice, role, and constraints are entirely expressed through its `instructions` string. Two agents pointed at the identical `IChatClient` can behave completely differently — as this lesson demonstrates directly.
- **`ChatClientFactory` — one place that chooses the provider.** Every lesson gets its `IChatClient` from this small static class. Two environment variables drive it, and both are required: `MAF_LLM` names the provider (`OPENAI`, `AZURE`, `GEMINI`, `OLLAMA` or `LMSTUDIO`) and `MAF_MODEL` names the model. There are no hidden defaults, so a missing setting fails fast with a message naming the variable instead of silently calling a model you did not choose. Because all five providers expose OpenAI-compatible endpoints, the factory only swaps the endpoint and key; the agent code never changes. The one model it refuses outright is Gemini 3: its tool calls need "thought signatures" that the OpenAI-compatible endpoint and SDK don't carry, so they fail with a 400 Bad Request — the factory stops a `gemini-3*` `MAF_MODEL` before any call and points you to `gemini-2.5-flash`.
- **`AgentResponse`** — the result of `RunAsync`. Its `.Text` property aggregates all text content from the run into a single string; `.Messages` gives you the full structured conversation turn if you need more than plain text.

## Complete C# Code

**`src/HelloAgentAndPersonas/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HelloAgentAndPersonas
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

**`src/HelloAgentAndPersonas/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelloAgentAndPersonas
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>. This factory shape is reintroduced
    /// as-is in later lessons so that assembling the Lesson 22-24 capstone is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name and
        /// system instructions that define its persona.
        /// </summary>
        internal static ChatClientAgent CreateAgent(IChatClient chatClient, string name, string instructions)
        {
            return new ChatClientAgent(chatClient, name: name, instructions: instructions);
        }
    }
}
```

**`src/HelloAgentAndPersonas/Program.cs`**

```csharp
// Lesson 1 - Hello Agent & Personas
// Builds two ChatClientAgent instances from the same OpenAI-backed IChatClient but gives
// them different instructions, then runs an identical prompt against both so the effect
// of persona design is visible side by side.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HelloAgentAndPersonas
{
    /// <summary>
    /// Entry point that runs the persona comparison demo.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Creates a default "helpful assistant" persona and a "pirate navigator" persona,
        /// runs the same prompt against each, and prints both responses to the console.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent helpfulAssistant = AgentFactory.CreateAgent(
                chatClient,
                name: "HelpfulAssistant",
                instructions: "You are a concise, helpful assistant. Answer plainly and factually in two or three sentences.");

            ChatClientAgent pirateNavigator = AgentFactory.CreateAgent(
                chatClient,
                name: "PirateNavigator",
                instructions: "You are a salty, superstitious 18th-century pirate ship's navigator. " +
                    "Speak in pirate dialect, reference the sea, the stars, and your charts in every answer, " +
                    "and never break character. Keep answers to two or three sentences.");

            const string prompt = "How do I stay on course when I'm not sure which direction to take?";
            //Console.Write("Enter a prompt: ");
            //string? prompt = Console.ReadLine() ?? "How do I stay on course when I'm not sure which direction to take?";
            //if (string.IsNullOrWhiteSpace(prompt))
            //{
            //    Console.WriteLine("No prompt provided. Exiting.");
            //    return;
            //}   

            AgentResponse assistantResponse = await helpfulAssistant.RunAsync(prompt);
            Console.WriteLine("=== HelpfulAssistant ===");
            Console.WriteLine(assistantResponse.Text);
            Console.WriteLine();

            AgentResponse pirateResponse = await pirateNavigator.RunAsync(prompt);
            Console.WriteLine("=== PirateNavigator ===");
            Console.WriteLine(pirateResponse.Text);
        }
    }
}
```

**`src/HelloAgentAndPersonas/HelloAgentAndPersonas.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>HelloAgentAndPersonas</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.24.0" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.10.1" />
    <PackageReference Include="OpenAI" Version="2.14.0" />
  </ItemGroup>

</Project>
```

**`HelloAgentAndPersonas.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/HelloAgentAndPersonas/HelloAgentAndPersonas.csproj" />
  </Folder>
</Solution>
```

## Walkthrough

1. **`ChatClientFactory.Create()`** starts with a small `requiredVariable` delegate: it returns an environment variable's value, or throws an `InvalidOperationException` saying `"<NAME> is not set. Set it before running this lesson."` when the variable is missing or empty. It reads `MAF_LLM` (the provider) and `MAF_MODEL` (the model; on Azure, the deployment name) through that delegate — neither has a built-in default. A `switch` on the upper-cased provider then picks the endpoint and API key: `OPENAI` uses `https://api.openai.com/v1/` and `OPENAI_API_KEY`; `AZURE` uses `AZURE_OPENAI_ENDPOINT` plus `/openai/v1/` and `AZURE_OPENAI_API_KEY`; `GEMINI` first rejects any `MAF_MODEL` starting with `gemini-3` (Gemini 3 tool calls fail over the OpenAI-compatible endpoint, so the message names `gemini-2.5-flash` instead), then uses Google's OpenAI-compatible endpoint and `GEMINI_API_KEY`; `OLLAMA` uses `http://localhost:11434/v1/` and `LMSTUDIO` uses LM Studio's local server at `http://localhost:1234/v1/`, each with a placeholder key, since neither server checks it but the SDK refuses an empty one. Any other value throws, naming the five accepted providers. Finally it builds an `OpenAIClient` for that endpoint, gets the `ChatClient` for `MAF_MODEL`, and adapts it to `IChatClient` via `.AsIChatClient()`. This is the single seam where "which model provider are we using" lives — change an environment variable to point the lesson at a different provider without touching any agent code.
2. **`AgentFactory.CreateAgent(...)`** takes that same `IChatClient` and wraps it in a `ChatClientAgent` twice, each time with a different `name` and `instructions`. Note that both agents share one `IChatClient` instance — the persona lives entirely in the agent, not the connection.
3. **`Program.Main`** builds `helpfulAssistant` and `pirateNavigator`, then calls `RunAsync(prompt)` on each with the exact same question string.
4. **`RunAsync`** wraps the plain string prompt in a user-role `ChatMessage` internally and sends it, plus the agent's instructions as system guidance, to OpenAI. It returns a `Task<AgentResponse>`.
5. **`response.Text`** aggregates all text content produced by the run into one string, which we print. Because no `AgentSession` is passed to `RunAsync`, each call is a stateless, one-off turn — multi-turn memory is Lesson 4's topic.

## Expected Output

Exact wording will vary by run since this calls a live model, but the shape will look like:

```
=== HelpfulAssistant ===
Focus on a fixed landmark or use a compass/GPS to establish a reliable bearing. Cross-check your position periodically against a map so small errors don't compound into a wrong direction.

=== PirateNavigator ===
Ye fix yer eye on the North Star, matey, and cross-check her against yer charts every glass of the hour. When the fog rolls in and the stars go dark, trust yer compass and the swell of the sea beneath the hull — she never lies.
```

The key thing to notice: identical question, identical model, identical `IChatClient` — completely different voice, purely from the `instructions` string passed at agent construction.
