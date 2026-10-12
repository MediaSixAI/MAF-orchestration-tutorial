# Lesson 21 — Hosting & Deployment

## Project Overview

Until now every workflow ran inside the console process that started it. This lesson turns a workflow into a service. `ResearchAgent` and `WriterAgent` are composed into a sequential workflow, `workflow.AsAIAgent(...)` wraps the whole graph as a single `AIAgent`, and Microsoft Foundry Agent Service's hosting contract, the Responses protocol, serves it as `POST /responses`. The app runs on your own machine with nothing but `OPENAI_API_KEY`, and the same code is what a Foundry Hosted Agent container runs. That makes it a direct comparison of the two hosting models: self-hosting the workflow yourself, or deploying it to Foundry.

## Prerequisites

- **Lesson 1.** `ChatClientAgent` and the `ChatClientFactory` / `AgentFactory` pattern.
- **Lesson 12.** `AgentWorkflowBuilder.BuildSequential`, which chains agents so each one sees the previous agent's output.
- **Lesson 19.** Hosting an agent in ASP.NET Core, the `Microsoft.NET.Sdk.Web` project type, and pinning the port in code with `UseUrls`.

## Setup

**Target framework:** `net10.0`, **SDK:** `Microsoft.NET.Sdk.Web`. Like Lesson 19, this lesson is a web host rather than a console app, because it serves HTTP requests.

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package: `AIAgent`, `ChatClientAgent` |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | `AgentWorkflowBuilder.BuildSequential` and `Workflow.AsAIAgent` |
| `Microsoft.Agents.AI.Foundry.Hosting` | 1.24.0-preview.261006.1 | Foundry Agent Service hosting contract: `AddFoundryResponses` / `MapFoundryResponses` (preview package) |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

```powershell
dotnet add package Microsoft.Agents.AI --version 1.24.0
dotnet add package Microsoft.Agents.AI.Workflows --version 1.24.0
dotnet add package Microsoft.Agents.AI.Foundry.Hosting --version 1.24.0-preview.261006.1
dotnet add package Microsoft.Extensions.AI.OpenAI --version 10.10.1
dotnet add package OpenAI --version 2.14.0
```

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

**No Azure subscription is needed to build, run or test this lesson.** `AddFoundryResponses` takes any `AIAgent`, so the project has no Azure SDK reference and no Foundry project. Running locally, the hosting layer stores responses, sessions and workflow checkpoints as JSON files under **`%USERPROFILE%\.agentserver\`** (`~/.agentserver/` on macOS and Linux). The folder is created on first run, and you can delete it between runs to start fresh. The deployment steps under Core Concepts are the part that needs Azure.

## Core Concepts

- **A workflow becomes an agent with `AsAIAgent`.** `workflow.AsAIAgent(id, name, description)` wraps a `Workflow` in an `AIAgent`. Callers use it like any other agent: they send chat messages and get chat messages back, and the workflow runs underneath. The workflow's start executor must accept chat messages. A sequential workflow from `AgentWorkflowBuilder.BuildSequential(...)` starts with an agent, so it can be wrapped directly. A hand-built `WorkflowBuilder` graph needs a chat-protocol start executor (one deriving from `ChatProtocolExecutor`). Anything that hosts an `AIAgent` can now host a whole multi-agent workflow, including the Foundry Responses host in this lesson and the A2A host from Lesson 19.

- **`AddFoundryResponses` and `MapFoundryResponses` are the hosting contract.** `builder.Services.AddFoundryResponses(workflowAgent)` registers the Responses protocol handler for that one agent. `app.MapFoundryResponses()` maps `POST /responses` and the `GET /readiness` health probe that Foundry checks before it routes traffic to a container. These two calls are the same whether the app runs on your laptop or in Foundry.

- **The Responses protocol is OpenAI-compatible.**
  - `POST /responses` with `{"input": "..."}` returns a response object with an `id`, an `agent_session_id` and an `output` array.
  - For a workflow agent, `output` holds a `message` item for each agent that spoke (here, `ResearchAgent`'s bullets and then `WriterAgent`'s sentence). It also holds `workflow_action` items that trace each executor starting (`in_progress`) and finishing (`completed`).
  - `"stream": true` switches to Server-Sent Events: `response.created`, then `response.output_item.added` / `response.output_text.delta` / `response.output_item.done` for each item, then `response.completed`.
  - Passing a response's `id` as `previous_response_id` continues the same conversation. The follow-up turn keeps the same `agent_session_id`, and both agents see the earlier messages, so a question like "What language do people speak there?" resolves "there" from the first turn.

- **The host keeps the state for you.** Every turn is saved: the responses themselves, the agent session, and the workflow's checkpoints. If no session store is passed, `AddFoundryResponses` uses Foundry's durable state store when the app is deployed, and the local files under `%USERPROFILE%\.agentserver\` when it runs on your machine. The workflow is checkpointed automatically, so you do not need the `CheckpointManager` setup from Lesson 18. An `AddFoundryResponses(agent, agentSessionStore)` overload lets a self-hosted app plug in its own durable store, such as a database or blob storage.

- **Self-hosted vs Foundry-hosted.** The code is identical. What changes is who runs it and what you get for free:

  | | Self-hosted (you run this app) | Foundry-hosted (`azd deploy` to Foundry Agent Service) |
  |---|---|---|
  | Where it runs | Anywhere: your machine, a VM, App Service, any container platform, any cloud | A managed container in Microsoft Foundry Agent Service |
  | Protocol | Your choice: Responses (this lesson), A2A (Lesson 19), or both | Responses (OpenAI-compatible) or Invocations (custom) |
  | Session and workflow state | Local files by default, or a store you provide; backing it up and sharing it across instances is your job | Foundry's durable state store, kept across turns, idle periods and restarts |
  | Identity | Whatever you wire up | Each deployed agent gets its own Microsoft Entra identity |
  | Scaling | Your job | Managed by the platform |
  | Versioning and rollout | Your job | Each deployment is an immutable version; traffic can be split between versions for canary or blue-green rollout, with instant rollback |
  | Telemetry | Whatever exporter you configure (Lesson 20 used the console) | `APPLICATIONINSIGHTS_CONNECTION_STRING` is injected, so traces go to Application Insights |
  | Deployment | However you like | `azd provision` + `azd deploy` |

  Self-hosting gives total control over cloud, provider and protocol, and you build the production concerns yourself. Foundry hosting solves identity, scaling, state, versioning and telemetry for you, as long as the agent runs on Azure.

- **What deploying to Foundry adds.** This part needs an Azure subscription and the Azure Developer CLI (`azd`), and the lesson describes it rather than running it. `azd provision` creates a resource group with a Foundry resource and project, a model deployment, Application Insights and a container registry. `azd deploy` builds this project into a container image, pushes it to the registry and deploys it to Foundry Agent Service. At run time Foundry injects `FOUNDRY_PROJECT_ENDPOINT`, `AZURE_AI_MODEL_DEPLOYMENT_NAME` and `APPLICATIONINSIGHTS_CONNECTION_STRING` into the container. `azd ai agent run` runs the same container locally for testing, on port `8088`.

- **Pin the port in code.** `builder.WebHost.UseUrls("http://localhost:8088")` fixes the listening address, so `launchSettings.json` cannot pick a different port, the same choice Lesson 19 made. Port `8088` matches `azd ai agent run`, so local testing uses the same URL either way.

## Complete C# Code

**`src/HostingAndDeployment/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HostingAndDeployment
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

**`src/HostingAndDeployment/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostingAndDeployment
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>. Same shape as Lesson 1's factory, reused
    /// unchanged here since this lesson's new material is about how the agents' workflow gets
    /// hosted, not about how each agent is built.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent with the supplied display name and
        /// system instructions.
        /// </summary>
        internal static ChatClientAgent CreateAgent(IChatClient chatClient, string name, string instructions)
        {
            return new ChatClientAgent(chatClient, name: name, instructions: instructions);
        }
    }
}
```

**`src/HostingAndDeployment/Program.cs`**

```csharp
// Lesson 21 - Hosting & Deployment
// Self-hosts a two-agent workflow behind Microsoft Foundry Agent Service's hosting contract, the
// Responses protocol. ResearchAgent and WriterAgent are composed into a sequential workflow,
// workflow.AsAIAgent() turns that workflow into a single AIAgent, and AddFoundryResponses /
// MapFoundryResponses serve it as POST /responses from a plain ASP.NET Core app. Nothing here
// talks to Azure: the same code runs on a developer machine and inside a Foundry Hosted Agent
// container deployed with `azd provision` / `azd deploy`.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace HostingAndDeployment
{
    /// <summary>
    /// Entry point that builds a ResearchAgent to WriterAgent workflow, wraps it as an
    /// <see cref="AIAgent"/>, and hosts it behind the Foundry Responses protocol.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Fixed local port this lesson listens on. It matches the port `azd ai agent run` uses for
        /// local testing, and setting it in code means launchSettings.json cannot pick a different
        /// one (the same choice Lesson 19 made for A2A hosting).
        /// </summary>
        private const string ListenUrl = "http://localhost:8088";

        /// <summary>
        /// Builds the two agents, composes them into a sequential workflow, exposes the workflow
        /// as an AIAgent, registers it with the Foundry Responses protocol handler, and starts
        /// listening for requests.
        /// </summary>
        private static void Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "ResearchAgent",
                instructions: "You are a researcher. Answer the user's latest question with two or three " +
                    "short, factual bullet points. Use the earlier conversation to resolve words like " +
                    "'there' or 'it'.");

            ChatClientAgent writerAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "WriterAgent",
                instructions: "You are a writer. Rewrite the researcher's bullet points from the previous " +
                    "message as one concise, friendly sentence. Output only that sentence.");

            Workflow workflow = AgentWorkflowBuilder.BuildSequential("ResearchThenWrite", [researchAgent, writerAgent]);

            AIAgent workflowAgent = workflow.AsAIAgent(
                id: "research-then-write",
                name: "ResearchThenWrite",
                description: "Researches a question, then answers it in one friendly sentence.");

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(ListenUrl);
            builder.Services.AddFoundryResponses(workflowAgent);

            WebApplication app = builder.Build();
            app.MapFoundryResponses();

            app.Run();
        }
    }
}
```

## Walkthrough

1. **Two agents, built the usual way.** `ResearchAgent` answers the latest question with a few factual bullets and uses earlier turns to resolve words like "there". `WriterAgent` rewrites those bullets as one friendly sentence. Both come from `ChatClientFactory` and `AgentFactory`.
2. **`AgentWorkflowBuilder.BuildSequential("ResearchThenWrite", [researchAgent, writerAgent])` composes them.** The incoming conversation goes to `ResearchAgent`, its reply is appended, and the whole conversation goes to `WriterAgent`. This is Lesson 12's sequential pattern.
3. **`workflow.AsAIAgent(...)` turns the graph into one agent.** The `id`, `name` and `description` identify it to callers. From here on the hosting code treats it like any other `AIAgent`.
4. **`builder.WebHost.UseUrls(ListenUrl)` pins the port** to `http://localhost:8088` before anything else configures the host.
5. **`builder.Services.AddFoundryResponses(workflowAgent)` adopts the hosting contract.** It registers the Responses protocol handler, the session store and workflow checkpointing for this agent. No Azure types appear.
6. **`app.MapFoundryResponses()` maps the HTTP surface:** `POST /responses` for plain JSON, streaming and multi-turn calls, and `GET /readiness` for health probes.
7. **`app.Run()` starts an ordinary Kestrel server.** Nothing about the running process is Azure-specific. It becomes a Foundry Hosted Agent only when `azd deploy` packages this same project as a container.

**To try it yourself after `dotnet run`:**

```powershell
# Non-streaming
$first = Invoke-RestMethod -Uri http://localhost:8088/responses -Method POST -ContentType "application/json" `
    -Body '{"input": "What is the capital of France?"}'
$first.output | Where-Object type -eq "message" | ForEach-Object { $_.content.text }

# Follow-up in the same conversation
$body = @{ input = "What language do people speak there?"; previous_response_id = $first.id } | ConvertTo-Json
$second = Invoke-RestMethod -Uri http://localhost:8088/responses -Method POST -ContentType "application/json" -Body $body
$second.output | Where-Object type -eq "message" | ForEach-Object { $_.content.text }

# Streaming (Server-Sent Events)
curl.exe -N -X POST http://localhost:8088/responses -H "Content-Type: application/json" `
    -d '{"input": "Count from one to three.", "stream": true}'
```

## Expected Output

Starting the app with `dotnet run` prints the hosting layer's startup log. The `instance` value and the content root path depend on your machine:

```
info: Azure.AI.AgentServer.Tasks[11]
      TaskManager starting (owner=unknown-agent|session:local, instance=worker-44008-2655381f-1791400694, hosted=False).
info: Azure.AI.AgentServer.Responses.Internal.ResponsesStartupLogger[0]
      Responses protocol configuration: StorageProvider=FileResponsesProvider DefaultModel=(not set) DefaultFetchHistoryCount=100 EventStreamTtl=00:10:00
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:8088
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
info: Microsoft.Hosting.Lifetime[0]
      Content root path: D:\MAF\Projects\Lesson21-HostingAndDeployment\src\HostingAndDeployment
```

`hosted=False` and `StorageProvider=FileResponsesProvider` show the app running as a self-hosted process with its state in the local `.agentserver` folder.

<!-- EXPECTED OUTPUT PENDING: re-run with OPENAI_API_KEY set -->

The non-streaming call returns a `completed` response whose `output` holds `workflow_action` items for `ResearchAgent`, `WriterAgent` and the workflow's `OutputMessages` step, plus two `message` items: `ResearchAgent`'s bullet points about Paris and `WriterAgent`'s one-sentence answer. The follow-up call with `previous_response_id` returns the same `agent_session_id`, and its messages answer that people in France speak French. The streaming call emits `response.created`, a sequence of `response.output_item.added` / `response.output_text.delta` / `response.output_item.done` events, and ends with `response.completed`.
