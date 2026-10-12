# Lesson 19 — A2A Protocol

## Project Overview

This lesson moves a single model-backed `ResearchAgent` out of the console and into its own ASP.NET Core process, exposed over the open Agent-to-Agent (A2A) protocol. A separate client process then discovers that agent purely from its published agent card and calls it exactly like any in-process `AIAgent` — no shared code, no shared memory, just HTTP. This is the bridge from Lessons 10–18's in-process orchestration to a real deployment shape. The Lesson 24 capstone uses the same idea — any `AIAgent` can be hosted in a web process — to serve its finished Researcher → Analyst → Writer pipeline as one agent, over the Responses protocol (Lesson 21) rather than A2A.

## Prerequisites

Lessons 1–2 — this lesson assumes familiarity with `IChatClient`, `ChatClientAgent`, `RunAsync`, and `RunStreamingAsync`. It does not build on any workflow pattern from Lessons 10–18; A2A is an orthogonal, external-interoperability concern that happens to sit at the end of the orchestration phase in the syllabus, not another orchestration pattern itself.

## Setup

This lesson is **two projects**, both under the lesson's `src/` folder, the same shape as Lesson 7's MCP lesson:

- **`A2AProtocol`** — the lesson proper. An **ASP.NET Core** host (not a plain console app — this is a deliberate, necessary exception, since A2A hosting requires a real web server) that builds a `ResearchAgent` on `ChatClientFactory` (provider and model from `MAF_LLM` and `MAF_MODEL`) and exposes it over A2A.
- **`A2AProtocolClient`** — a plain console app that discovers the hosted agent from its agent card and calls it.

Opening `A2AProtocol.slnx` in VS2026 loads both projects (its solution file references the sibling client project too). **Run the host first, then run the client in a second terminal** — the client fails to connect if the host isn't already listening.

```powershell
# Terminal 1
dotnet run --project src/A2AProtocol

# Terminal 2, once you see "Application started"
dotnet run --project src/A2AProtocolClient
```

**Target framework:** `net10.0` (both projects)

**NuGet packages — `A2AProtocol` (the host):**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AIAgent` |
| `Microsoft.Agents.AI.Hosting.A2A.AspNetCore` | 1.24.0-preview.261006.1 | ASP.NET Core endpoint mapping for A2A — `AddA2AServer`, `MapA2AHttpJson`, plus `AgentCard` and `MapWellKnownAgentCard` from its dependencies |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

**NuGet packages — `A2AProtocolClient`:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `AIAgent`, `AgentResponse`, `AgentResponseUpdate` |
| `Microsoft.Agents.AI.A2A` | 1.24.0-preview.261006.1 | Client-side A2A — `A2ACardResolver`, `.GetAIAgentAsync()` |

**The A2A packages ship as preview** (`1.24.0-preview.261006.1`), so add them with `dotnet add package <name> --prerelease`, or reference that exact version in the `.csproj`.

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

Only the host project reads these — the client never talks to a model provider directly.

**The host's port is set in code.** An ASP.NET Core project's `Properties/launchSettings.json` can set its own `applicationUrl`, which takes precedence over an `ASPNETCORE_URLS` environment variable under `dotnet run`. This lesson calls `builder.WebHost.UseUrls("http://localhost:5100")` in `Program.cs`, so the host listens on the same port however it is launched (`dotnet run`, the built EXE, or VS2026's F5), which is the address the client expects.

## Core Concepts

- **A2A is a protocol, not a MAF invention — MAF just ships first-class support for it.** The Agent-to-Agent protocol (see [a2a-protocol.org](https://a2a-protocol.org/latest/)) defines a standard way for any agent, built with any framework or language, to be discovered and called by any other agent or client. MAF's contribution is a thin, purpose-built bridge in both directions: a hosting package that turns any `AIAgent` into an A2A-compliant service, and a client package that turns any A2A-compliant endpoint back into an ordinary `AIAgent`.
- **Hosting an agent over A2A is four explicit steps — server registration, protocol wiring, endpoint mapping, and card publishing are all separate calls, not one bundled helper.** `builder.Services.AddKeyedSingleton<AIAgent>("research-agent", factory)` registers the agent itself under a string key; `builder.AddA2AServer("research-agent")` resolves that keyed agent and wraps it with A2A request/session handling; `app.MapA2AHttpJson("research-agent", "/a2a/research-agent")` maps the actual HTTP+JSON request endpoint (a JSON-RPC binding is also available via `MapA2AJsonRpc`, and both can be mapped simultaneously at different paths); `app.MapWellKnownAgentCard(new AgentCard {...})` publishes a discovery document at `/.well-known/agent-card.json`. Only one agent's card can be served per host — other co-hosted agents are still reachable directly by URL, just not auto-discoverable.
- **The hosted agent is completely unaware it's being exposed over A2A.** `ResearchAgent` is built with the exact same `ChatClientFactory`/`AgentFactory.CreateAgent` pair every other lesson uses — nothing about its construction changes. `AddA2AServer` operates purely at the DI/hosting layer, wrapping whatever `AIAgent` was registered under the matching key.
- **On the client side, discovery and invocation collapse to two calls.** `A2ACardResolver(new Uri(hostBaseUrl))` points at a host; `.GetAIAgentAsync()` fetches its `/.well-known/agent-card.json` and returns a ready-to-use `AIAgent` in one step. From that point on, `RunAsync` and `RunStreamingAsync` behave **identically** to every in-process agent from Lessons 1–18 — this lesson's client calls both, live, against the model-backed host, and the calling code cannot tell the difference from a local agent beyond the extra discovery step up front.
- **Two other discovery strategies exist beyond the well-known URI, for when a card was obtained another way:** `agentCard.AsAIAgent()` wraps an `AgentCard` you already have (e.g. pulled from an enterprise registry) directly; `new A2AClient(new Uri(endpoint)).AsAIAgent(name:, description:)` skips card discovery entirely for tightly-coupled or development scenarios where the endpoint is already known. Not exercised in this lesson's code, but useful to know both exist.
- **A2A is a transport-level wrapper — the remote agent's tools are invisible to, and not configurable from, the calling side.** Whatever tools, MCP servers, or hosted capabilities `ResearchAgent` might use live entirely on the host side; extending its capabilities means changing the host's configuration, not anything in the client project. The same holds for any hosted agent, including the Lesson 24 capstone served over the Responses protocol: its client configures nothing about the Researcher/Analyst/Writer agents' own tool access — it only ever calls the exposed agent.
- **Streaming works over the wire exactly as it does in-process.** `researchAgent.RunStreamingAsync(...)` against the remote A2A agent returns an `IAsyncEnumerable<AgentResponseUpdate>` — Server-Sent Events under the hood — and this lesson's `await foreach` loop prints incremental text exactly the way an in-process streaming call would, with no A2A-specific code required at the call site.
- **Background responses exist for long-running tasks but are explicitly out of scope here.** The A2A spec supports returning a continuation token instead of blocking (`AgentRunOptions.AllowBackgroundResponses`, polling, and stream reconnection), and the default `AgentRunMode` on a hosted server is `DisallowBackground`. This lesson's `ResearchAgent` answers quickly enough that the default (synchronous) mode is the right fit — worth knowing the mechanism exists for a future agent that doesn't.
- **The default session and task stores are in-memory and explicitly documented as development-only.** `AddA2AServer` defaults to `InMemoryAgentSessionStore` and `InMemoryTaskStore` — state is lost on restart and isn't shared across instances. Both are overridable by registering a keyed `AgentSessionStore`/`ITaskStore` before calling `AddA2AServer`, the same DI-keyed pattern used to register the agent itself. Not needed for this lesson's single stateless request/response exchange, but the first thing to swap out before any real deployment.

## Complete C# Code

### Project 1 — `A2AProtocol` (the A2A host)

**`src/A2AProtocol/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace A2AProtocol
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

**`src/A2AProtocol/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace A2AProtocol
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

**`src/A2AProtocol/Program.cs`**

```csharp
// Lesson 19 - A2A Protocol
// Hosts a single OpenAI-backed ResearchAgent behind the Agent-to-Agent (A2A) protocol so that any
// A2A-compliant client - regardless of framework, language, or process - can discover it via its
// published agent card and call it exactly like a local AIAgent. This is the bridge from the
// in-process orchestration patterns of Lessons 10-18 to an agent that lives in its own process and is
// reachable over HTTP - the same hosting shape Lesson 24 uses when it serves the finished capstone
// pipeline as one agent over the Responses protocol. Run this project first, then run the companion
// A2AProtocolClient project.

using A2A;
using A2A.AspNetCore;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace A2AProtocol
{
    /// <summary>
    /// Entry point that builds and hosts an OpenAI-backed ResearchAgent as an A2A server, exposing
    /// both its request endpoint and its discoverable agent card over HTTP.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Fixed local address this lesson listens on, set explicitly in code (rather than left to
        /// an environment variable or a generated launchSettings.json profile) so the companion
        /// client project - and this lesson's own instructions - can rely on a reproducible
        /// address no matter how the app is launched.
        /// </summary>
        private const string ListenUrl = "http://localhost:5100";

        /// <summary>
        /// The DI key (and A2A endpoint segment) this lesson's single hosted agent is registered
        /// under.
        /// </summary>
        private const string AgentKey = "research-agent";

        /// <summary>
        /// Registers an OpenAI-backed ResearchAgent in the DI container, wires it up as an A2A
        /// server, maps its HTTP+JSON endpoint and agent card, and starts listening.
        /// </summary>
        private static async Task Main(string[] args)
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(ListenUrl);

            // 1. Register the agent itself - an ordinary OpenAI-backed ChatClientAgent, built with
            // the same factories every other lesson uses. Nothing about its construction "knows"
            // it is about to be exposed over A2A.
            builder.Services.AddKeyedSingleton<AIAgent>(AgentKey, (_, _) =>
            {
                IChatClient chatClient = ChatClientFactory.Create();
                return AgentFactory.CreateAgent(
                    chatClient,
                    name: "ResearchAgent",
                    instructions: "You are a concise research assistant. Answer factual questions " +
                        "in two sentences or fewer. Never say you cannot access the internet - " +
                        "just answer from what you already know.");
            });

            // 2. Register the A2A server for that agent - this is the actual protocol bridge,
            // resolving the keyed AIAgent above and wrapping it with request/session handling.
            builder.AddA2AServer(AgentKey);

            WebApplication app = builder.Build();

            // 3. Map the agent's HTTP+JSON request endpoint.
            app.MapA2AHttpJson(AgentKey, $"/a2a/{AgentKey}");

            // 4. Publish a discoverable agent card at the A2A well-known path so that a client
            // which only knows this host's base address can find and describe the agent on its own.
            app.MapWellKnownAgentCard(new AgentCard
            {
                Name = "ResearchAgent",
                Description = "A concise research assistant that answers factual questions.",
                Version = "1.0",
                DefaultInputModes = ["text"],
                DefaultOutputModes = ["text"],
                SupportedInterfaces =
                [
                    new AgentInterface
                    {
                        Url = $"{ListenUrl}/a2a/{AgentKey}",
                        ProtocolBinding = ProtocolBindingNames.HttpJson,
                        ProtocolVersion = "1.0",
                    }
                ]
            });

            Console.WriteLine($"ResearchAgent hosted over A2A at {ListenUrl}/a2a/{AgentKey}");
            Console.WriteLine($"Agent card discoverable at {ListenUrl}/.well-known/agent-card.json");
            Console.WriteLine("Waiting for requests. Run A2AProtocolClient in another terminal, or Ctrl+C to stop.");

            await app.RunAsync();
        }
    }
}
```

### Project 2 — `A2AProtocolClient` (the A2A client)

**`src/A2AProtocolClient/Program.cs`**

```csharp
// Lesson 19 - A2A Protocol (client)
// Discovers the ResearchAgent hosted by the companion A2AProtocol project purely from its
// well-known agent card, then calls it exactly like a local AIAgent - RunAsync and
// RunStreamingAsync behave identically to every prior lesson's in-process agent, even though every
// call now travels over HTTP to a separate process. Start A2AProtocol first; this client
// will fail to connect if the host is not already running.

using A2A;
using Microsoft.Agents.AI;

namespace A2AProtocolClient
{
    /// <summary>
    /// Entry point that discovers the ResearchAgent hosted by A2AProtocol and exercises both
    /// its non-streaming and streaming call paths.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Base address of the running A2AProtocol host. Its agent card is expected at
        /// {HostUrl}/.well-known/agent-card.json.
        /// </summary>
        private const string HostUrl = "http://localhost:5100";

        /// <summary>
        /// Resolves the remote agent card, wraps it as a local AIAgent, and runs one
        /// non-streaming and one streaming request against it.
        /// </summary>
        private static async Task Main(string[] args)
        {
            Console.WriteLine($"Discovering agent card at {HostUrl}/.well-known/agent-card.json ...");
            A2ACardResolver resolver = new(new Uri(HostUrl));

            // GetAIAgentAsync fetches the agent card and wraps the remote endpoint as a standard
            // AIAgent in one call - from here on, this is indistinguishable from an in-process agent.
            AIAgent researchAgent = await resolver.GetAIAgentAsync();
            Console.WriteLine($"Discovered remote agent: {researchAgent.Name}");
            Console.WriteLine();

            Console.WriteLine("=== Non-streaming call ===");
            AgentResponse response = await researchAgent.RunAsync(
                "What galaxy is Earth located in? Answer in one sentence.");
            Console.WriteLine(response.Text);
            Console.WriteLine();

            Console.WriteLine("=== Streaming call ===");
            await foreach (AgentResponseUpdate update in researchAgent.RunStreamingAsync(
                "Name three inner planets of the solar system, one per line."))
            {
                if (!string.IsNullOrEmpty(update.Text))
                {
                    Console.Write(update.Text);
                }
            }
            Console.WriteLine();
        }
    }
}
```

## Walkthrough

1. **The host's `Program.cs` builds a normal `WebApplication`, not a normal console `Main`.** `WebApplication.CreateBuilder(args)` replaces the plain `Host.CreateApplicationBuilder`/direct instantiation seen in earlier lessons — A2A hosting requires a real ASP.NET Core pipeline, which is why this project uses the `Microsoft.NET.Sdk.Web` SDK instead of `Microsoft.NET.Sdk`. Per this series' rule against top-level statements, everything still lives inside an explicit `Program` class with a `static async Task Main`, exactly like every other lesson — this compiles and runs identically to the more familiar top-level-statement style shown in Microsoft's own A2A samples.
2. **`builder.WebHost.UseUrls(ListenUrl)` pins the port in code.** As described in Setup, the host always listens on `http://localhost:5100`, regardless of how it's launched.
3. **Registering the agent and registering the A2A server are two separate calls, deliberately.** `AddKeyedSingleton<AIAgent>(AgentKey, factory)` is pure MAF — it would work identically in a lesson with no A2A at all. `builder.AddA2AServer(AgentKey)` is the one line that actually turns this into an A2A host, resolving that same keyed agent behind the scenes.
4. **`MapA2AHttpJson` and `MapWellKnownAgentCard` come from two different packages, but compose in one pipeline.** The former is MAF's hosting bridge; the latter (`MapWellKnownAgentCard`) is provided directly by the underlying `A2A.AspNetCore` SDK package, not by Microsoft's hosting package — both are mapped on the same `WebApplication` instance.
5. **The client never references OpenAI, `Microsoft.Extensions.AI.OpenAI`, `ChatClientFactory`, or any MAF chat-client type at all.** Its only MAF dependency is `Microsoft.Agents.AI.A2A` plus the core `Microsoft.Agents.AI` package for the `AIAgent`/`AgentResponse`/`AgentResponseUpdate` types — proof that A2A genuinely decouples the calling code from whatever model or tools the remote agent happens to use.
6. **`A2ACardResolver(new Uri(HostUrl)).GetAIAgentAsync()` is the entire discovery step.** One call fetches `/.well-known/agent-card.json` and returns a ready-to-use `AIAgent` — `researchAgent.Name` printed immediately afterward comes straight from the card's `Name` field, not from any local configuration.
7. **Both calls that follow are ordinary agent calls.** `RunAsync` and the `await foreach` over `RunStreamingAsync` are byte-for-byte the same shape used against in-process agents in Lessons 1–18 — the only visible difference in this lesson's code from a purely local agent is the two lines that constructed `researchAgent` in the first place.

## Expected Output

**Host terminal (`A2AProtocol`):**

```
ResearchAgent hosted over A2A at http://localhost:5100/a2a/research-agent
Agent card discoverable at http://localhost:5100/.well-known/agent-card.json
Waiting for requests. Run A2AProtocolClient in another terminal, or Ctrl+C to stop.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5100
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
```

**Client terminal (`A2AProtocolClient`):**

```
Discovering agent card at http://localhost:5100/.well-known/agent-card.json ...
Discovered remote agent: ResearchAgent

=== Non-streaming call ===
Earth is located in the Milky Way galaxy.

=== Streaming call ===
Mercury
Venus
Earth
```

The exact wording of both model responses will vary between runs — every call is a live model invocation, now additionally round-tripping over HTTP through the A2A protocol. What won't vary: the client discovers `ResearchAgent` purely from its agent card (no hardcoded model, persona, or endpoint knowledge beyond the host's base URL), and both the non-streaming and streaming call paths complete successfully against the separately-running host process.
