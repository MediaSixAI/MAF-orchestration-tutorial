# Lesson 24 — Capstone Deploy & Review

## Project Overview

This final lesson takes Lesson 23's graph — Researcher → Analyst → approval port → Writer — and serves it as one agent over the Responses protocol: `Workflow.AsAIAgent()` turns the graph into an `AIAgent`, and `AddFoundryResponses` / `MapFoundryResponses` expose it at `POST /responses` in an ASP.NET Core host that runs locally with no Azure resources. A plain `HttpClient` console client starts a run, receives the pending approval as an `mcp_approval_request`, answers it, and gets the report — and because hosting checkpoints the run, the host can be restarted while the approval is pending. The lesson then packages the host for a container and reviews where every lesson in the series lands in the finished system.

## Prerequisites

Lesson 23 (this lesson hosts its graph unchanged: the same executors, port, agents and shared pieces). Lesson 21 (`AddFoundryResponses` / `MapFoundryResponses` and the Responses protocol). Lesson 19 (A2A, the other way the series exposes an agent). The solution itself is standalone.

## Setup

**Three projects, in this order:**

1. `ResearchMcpServer` — identical to Lesson 23's companion server. Building the solution builds it.
2. `CapstoneDeployAndReview` — the Responses host. Run this second.
3. `CapstoneDeployAndReviewClient` — a console client. Run this last, in a separate terminal.

**`ResearchMcpServer` packages:**

| Package | Version | Purpose |
|---|---|---|
| `ModelContextProtocol` | 2.2.0 | MCP server SDK |
| `Microsoft.Extensions.Hosting` | 10.0.12 | Generic host for the MCP server process |

**`CapstoneDeployAndReview` packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | The graph, and `Workflow.AsAIAgent()` |
| `Microsoft.Agents.AI.Workflows.Generators` | 1.24.0 | `[MessageHandler]` source generation (`PrivateAssets="all"`) |
| `Microsoft.Agents.AI.Foundry.Hosting` | 1.24.0-preview.261006.1 | `AddFoundryResponses` / `MapFoundryResponses` — preview, same package as Lesson 21 |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `ModelContextProtocol` | 2.2.0 | MCP client SDK |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

**`CapstoneDeployAndReviewClient` packages:** none. The Responses protocol is JSON over HTTP, so the client uses `HttpClient` and `System.Text.Json` from the base library.

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

The host reads the provider settings; the client needs none.

**Lesson-specific settings (host):**

```powershell
# Optional - defaults to "http://localhost:5200"; the Dockerfile sets "http://+:5200":
$env:CAPSTONE_LISTEN_URL = "http://localhost:5200"

# Optional - path to ResearchMcpServer.dll; defaults to the sibling project's build output:
$env:CAPSTONE_MCP_SERVER_PATH = "C:\path\to\ResearchMcpServer.dll"
```

The topic comes from the client: its command-line arguments, or `the deep sea anglerfish` when there are none.

**Running it — two terminals, each starting in this lesson's folder:**

```powershell
# Terminal 1 (build once, then start the host)
dotnet build CapstoneDeployAndReview.slnx
cd src/CapstoneDeployAndReview; dotnet run

# Terminal 2 (after Terminal 1 prints "Waiting for requests")
cd src/CapstoneDeployAndReviewClient; dotnet run
```

**To see the durability:** when the client stops at `Approve this analysis for the final report? [y/N]`, press Ctrl+C in Terminal 1, run `dotnet run` there again, wait for `Waiting for requests`, then answer `y` in Terminal 2. The restarted host resumes the same run.

**The host listens on a fixed address, set in code with `UseUrls`.** A new ASP.NET Core project's `launchSettings.json` otherwise picks its own port (the same behaviour Lessons 19 and 21 set explicitly). `CAPSTONE_LISTEN_URL` changes it, which the container needs.

## Core Concepts

- **`Workflow.AsAIAgent()` turns the whole graph into one agent.** `workflow.AsAIAgent(id, name, description)` returns an `AIAgent` whose runs drive the workflow: each run sends the incoming messages to the start node, followed by a `TurnToken`. Lesson 23's `ResearchExecutor` is a `ChatProtocolExecutor` for exactly this reason, so the graph is hosted without changes. Whatever the Writer yields becomes the agent's reply text.

- **`AddFoundryResponses(agent)` + `MapFoundryResponses()` serve that agent over the Responses protocol.** This is Lesson 21's hosting, given a workflow agent instead of a `ChatClientAgent`. `POST /responses` with `{"input": "..."}` starts a run; nothing here calls Azure.

- **The approval travels as a standard Responses item.** When the run reaches the approval port, the `ToolApprovalRequestContent` it carries comes back as an output item of type `mcp_approval_request`, with an `id`, a `name` (`publish_report`), `server_label` `agent_framework`, and the action's `arguments` as a JSON string — the topic, summary and findings `ReportApprovalRequest.Create` put there. The client answers with a second `POST /responses` whose `previous_response_id` is the first reply's `id` and whose `input` is one `mcp_approval_response` item: `{"type": "mcp_approval_response", "approval_request_id": "<the request item's id>", "approve": true}`. The hosting layer turns that back into a `ToolApprovalResponseContent`, delivers it to the port, and the same run continues to the Writer. Neither the graph nor the host contains any code for this translation.

- **The reply shows the graph running.** Every executor invocation appears as a `workflow_action` output item with the node id in `action_id` and a status of `in_progress` or `completed`. In the first reply `research` completes twice — once when it receives the chat messages, once when it takes its turn. In the second reply `research` appears once more: after delivering the approval response, the session sends the start node a turn with no new messages, which `ResearchExecutor` skips.

- **Hosting checkpoints the run.** `AddFoundryResponses` gives a workflow agent checkpointing automatically and stores the agent session between requests: on a developer machine in the local state store under your user profile's `.agentserver` folder (`%USERPROFILE%\.agentserver` on Windows), and in Foundry's durable state store when deployed as a Foundry Hosted Agent (Lesson 21). The pending approval is part of that saved state, so a restarted host continues the run from the `previous_response_id` alone.

- **A plain HTTP client is enough.** `ResponsesRequests` builds the two request bodies, and `ResponsesReader` pulls out what the client needs: the reply `id`, the completed steps, the `mcp_approval_request`, and the `output_text` of assistant messages. Any client that speaks the OpenAI Responses API format can drive the same endpoint.

- **Lesson 19's A2A hosting is the other way to expose an agent.** A2A hosting also accepts any `AIAgent`. This lesson serves the capstone over the Responses protocol because its approval pause maps directly onto the standard `mcp_approval_request` / `mcp_approval_response` items that any Responses client already understands.

- **"Deploy it" means one image with two programs.** The host launches the MCP server as a subprocess over stdio (Lesson 7's pattern), so the Dockerfile publishes both projects into one image and points the host at the server with `CAPSTONE_MCP_SERVER_PATH`, because the dev-time sibling-project path (`AppContext.BaseDirectory` plus a fixed number of `..`) does not exist in a container layout. It also sets `CAPSTONE_LISTEN_URL` to `http://+:5200`, because a host bound to `localhost` inside a container is unreachable from outside it even with the port published. Inside a container the host's local state lives in the container's filesystem, so a pending approval survives a host restart but not the container being replaced; deployed as a Foundry Hosted Agent (Lesson 21), Foundry's durable store holds it instead.

## Complete C# Code

**`src/ResearchMcpServer/ResearchMcpServer.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>ResearchMcpServer</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="ModelContextProtocol" Version="2.2.0" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.12" />
  </ItemGroup>

</Project>
```

**`src/ResearchMcpServer/ResearchTools.cs`**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace ResearchMcpServer
{
    /// <summary>
    /// MCP tools exposed by this server. Every method here is discovered automatically by
    /// <c>WithToolsFromAssembly</c> via reflection and advertised to any connecting MCP client -
    /// the class and its methods must be public for that reflection-based scan to find them.
    /// </summary>
    [McpServerToolType]
    public static class ResearchTools
    {
        /// <summary>
        /// A small, fixed knowledge base of demo topics, keyed by a lowercase substring the
        /// caller's topic is matched against. A real capstone deployment would replace this with
        /// a call to an actual search index or document store, behind this exact same tool
        /// signature.
        /// </summary>
        private static readonly Dictionary<string, string[]> Notes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["anglerfish"] =
            [
                "Female deep-sea anglerfish use a bioluminescent lure called an esca, powered by symbiotic light-producing bacteria housed in a specialized organ, to attract prey in the pitch-black deep sea. (Source: Encyclopedia of Marine Biology)",
                "In many anglerfish species, the much smaller male permanently fuses to the female's body after mating, becoming a parasitic sperm donor sustained by her bloodstream for the rest of his life. (Source: Encyclopedia of Marine Biology)",
            ],
            ["octopus"] =
            [
                "Octopuses have three hearts: two pump blood through the gills, while the third circulates it to the rest of the body. (Source: Cephalopod Biology Handbook)",
                "An octopus's arms contain roughly two-thirds of its neurons, allowing each arm to process sensory information and react semi-independently of the brain. (Source: Cephalopod Biology Handbook)",
            ],
            ["narwhal"] =
            [
                "A narwhal's tusk is an elongated left canine tooth that can grow up to 3 meters long and is believed to serve sensory as well as display functions. (Source: Arctic Marine Mammal Survey)",
                "Narwhals can dive to depths beyond 1,500 meters, among the deepest of any marine mammal, in search of squid and fish. (Source: Arctic Marine Mammal Survey)",
            ],
        };

        /// <summary>
        /// Searches the internal knowledge base for factual notes matching the given topic. If
        /// no dedicated notes exist for that topic, returns a message telling the caller so,
        /// rather than fabricating an answer.
        /// </summary>
        [McpServerTool, Description("Searches a small internal research knowledge base for factual notes about a topic.")]
        public static string SearchResearchNotes(
            [Description("The topic to search for, e.g. 'anglerfish'.")] string topic)
        {
            foreach ((string key, string[] notes) in Notes)
            {
                if (topic.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    return string.Join("\n", notes);
                }
            }

            return $"No dedicated notes on file for '{topic}'. Draw on general knowledge instead, " +
                "and clearly note that these facts are not independently sourced.";
        }
    }
}
```

**`src/ResearchMcpServer/Program.cs`**

```csharp
// Lesson 24 - Capstone Deploy & Review
// A minimal Model Context Protocol server exposing one research tool - SearchResearchNotes - over
// standard input/output. Identical to Lesson 23's companion server: the Researcher keeps its MCP
// tool access once the pipeline is hosted. This process is launched as a
// subprocess by the companion CapstoneDeployAndReview host project; a human does not run it
// directly.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ResearchMcpServer
{
    /// <summary>
    /// Entry point that hosts an MCP server over standard input/output, exposing every tool
    /// method found in <see cref="ResearchTools"/>.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds and runs the MCP server host. Logging is redirected to standard error, since
        /// standard output is reserved exclusively for the MCP JSON-RPC protocol stream - any
        /// stray write to standard output would corrupt that stream from the client's point of
        /// view.
        /// </summary>
        private static async Task Main(string[] args)
        {
            HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

            builder.Logging.AddConsole(options =>
            {
                options.LogToStandardErrorThreshold = LogLevel.Trace;
            });

            builder.Services
                .AddMcpServer()
                .WithStdioServerTransport()
                .WithToolsFromAssembly();

            await builder.Build().RunAsync();
        }
    }
}
```

**`src/CapstoneDeployAndReview/CapstoneDeployAndReview.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CapstoneDeployAndReview</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.24.0" />
    <PackageReference Include="Microsoft.Agents.AI.Workflows" Version="1.24.0" />
    <PackageReference Include="Microsoft.Agents.AI.Workflows.Generators" Version="1.24.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Agents.AI.Foundry.Hosting" Version="1.24.0-preview.261006.1" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.10.1" />
    <PackageReference Include="ModelContextProtocol" Version="2.2.0" />
    <PackageReference Include="OpenAI" Version="2.14.0" />
  </ItemGroup>

</Project>
```

**`src/CapstoneDeployAndReview/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CapstoneDeployAndReview
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

**`src/CapstoneDeployAndReview/AgentFactory.cs`**

```csharp
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
```

**`src/CapstoneDeployAndReview/AgentInstructions.cs`**

```csharp
namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Shared persona names and system instructions for the capstone's three agent roles -
    /// Researcher, Analyst, Writer. Lesson 22 defines them; Lessons 23 and 24 add the
    /// Researcher's instruction to use its MCP research tool.
    /// </summary>
    internal static class AgentInstructions
    {
        /// <summary>
        /// Display name for the Researcher role.
        /// </summary>
        internal const string ResearcherName = "ResearcherAgent";

        /// <summary>
        /// Researcher persona: gathers factual notes about a topic using the MCP research tool
        /// it is given, and reports only what that tool returned.
        /// </summary>
        internal const string ResearcherPersona =
            "You are a meticulous researcher. Given a topic, use the SearchResearchNotes tool to " +
            "look up factual notes about it - never invent facts yourself. Report back two or " +
            "three concise notes, each with its source, based only on what the tool returned.";

        /// <summary>
        /// Display name for the Analyst role.
        /// </summary>
        internal const string AnalystName = "AnalystAgent";

        /// <summary>
        /// Analyst persona: extracts the Researcher's raw notes into strict JSON matching the
        /// <see cref="AnalysisResult"/> shape - the prompt contract AnalysisParser reads. The
        /// wording forbids markdown fences, and JsonFenceStripper removes one if a model adds it.
        /// </summary>
        internal const string AnalystPersona = """
            You are an analyst. You will receive raw research notes about a topic.
            Extract the information into STRICT JSON matching exactly this shape, with no
            markdown code fences, no commentary, and no text before or after the JSON:
            {"Topic": string, "Summary": string, "KeyFindings": [{"Fact": string, "Source": string}]}
            Include exactly two items in KeyFindings.
            """;

        /// <summary>
        /// Display name for the Writer role.
        /// </summary>
        internal const string WriterName = "WriterAgent";

        /// <summary>
        /// Writer persona: turns an approved, structured analysis into a short prose report for
        /// a general audience.
        /// </summary>
        internal const string WriterPersona =
            "You are a writer. Given an approved analysis (a topic, a summary, and key findings, " +
            "provided to you as JSON), turn it into a short, engaging report of two or three short " +
            "paragraphs for a general audience. Do not mention that your input was JSON.";
    }
}
```

**`src/CapstoneDeployAndReview/ResearchFinding.cs`**

```csharp
namespace CapstoneDeployAndReview
{
    /// <summary>
    /// One discrete fact the Researcher agent surfaced, as extracted and structured by the
    /// Analyst agent. Part of the shared data contract defined in Lesson 22's design.
    /// </summary>
    internal sealed record ResearchFinding(string Fact, string Source);
}
```

**`src/CapstoneDeployAndReview/AnalysisResult.cs`**

```csharp
namespace CapstoneDeployAndReview
{
    /// <summary>
    /// The Analyst agent's structured extraction of the Researcher agent's raw notes - the
    /// contract the human-approval request presents for review and the Writer agent consumes once
    /// it is approved. Defined in Lesson 22's design and used unchanged by every capstone lesson.
    /// </summary>
    internal sealed record AnalysisResult(string Topic, string Summary, List<ResearchFinding> KeyFindings);
}
```

**`src/CapstoneDeployAndReview/ResearchNotes.cs`**

```csharp
namespace CapstoneDeployAndReview
{
    /// <summary>
    /// The message the research step sends along the graph's first edge: the topic that was
    /// researched and the Researcher agent's raw, unstructured notes about it.
    /// </summary>
    internal sealed record ResearchNotes(string Topic, string Notes);
}
```

**`src/CapstoneDeployAndReview/JsonFenceStripper.cs`**

```csharp
namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Strips a leading/trailing Markdown code fence (```json ... ```) from a model response, if
    /// present, so the remaining text can be parsed as plain JSON. Part of the capstone's
    /// defensive parsing: the Analyst is told not to use fences, and the parser accepts its reply
    /// either way, whichever provider MAF_LLM selects.
    /// </summary>
    internal static class JsonFenceStripper
    {
        /// <summary>
        /// Returns the given text with a single leading and trailing Markdown code fence removed,
        /// if present; otherwise returns the trimmed text unchanged.
        /// </summary>
        internal static string Strip(string text)
        {
            string trimmed = text.Trim();
            if (!trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                return trimmed;
            }

            int firstNewline = trimmed.IndexOf('\n');
            string withoutOpeningFence = firstNewline >= 0 ? trimmed[(firstNewline + 1)..] : trimmed;

            int closingFenceIndex = withoutOpeningFence.LastIndexOf("```", StringComparison.Ordinal);
            string withoutClosingFence = closingFenceIndex >= 0
                ? withoutOpeningFence[..closingFenceIndex]
                : withoutOpeningFence;

            return withoutClosingFence.Trim();
        }
    }
}
```

**`src/CapstoneDeployAndReview/AnalysisParser.cs`**

```csharp
using System.Text.Json;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Turns the Analyst agent's reply text into a typed <see cref="AnalysisResult"/>. The
    /// Analyst's instructions are the contract (the exact JSON shape to emit); this class is the
    /// receiving side of it: strip a code fence if one is present, then deserialize with
    /// case-insensitive property names.
    /// </summary>
    internal static class AnalysisParser
    {
        /// <summary>
        /// Case-insensitive options, so "topic" and "Topic" both bind to the same property.
        /// </summary>
        private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

        /// <summary>
        /// Parses the Analyst's reply into an AnalysisResult. Throws InvalidOperationException,
        /// quoting the reply, if it is not JSON of the agreed shape.
        /// </summary>
        internal static AnalysisResult Parse(string analystReply)
        {
            string json = JsonFenceStripper.Strip(analystReply);

            AnalysisResult? result;
            try
            {
                result = JsonSerializer.Deserialize<AnalysisResult>(json, Options);
            }
            catch (JsonException exception)
            {
                throw new InvalidOperationException($"The Analyst's reply is not valid JSON: {json}", exception);
            }

            if (result?.Topic is null || result.Summary is null || result.KeyFindings is null)
            {
                throw new InvalidOperationException($"The Analyst's reply does not match AnalysisResult: {json}");
            }

            return result;
        }
    }
}
```

**`src/CapstoneDeployAndReview/ReportApprovalRequest.cs`**

```csharp
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Builds and reads the capstone's human-approval request. The request is the standard
    /// <see cref="ToolApprovalRequestContent"/> from Microsoft.Extensions.AI: it names the action
    /// awaiting sign-off ("publish_report") and carries the analysis as that action's arguments,
    /// so whoever reviews it - a console prompt, or a remote client over the Responses protocol -
    /// sees exactly what will be written up.
    /// </summary>
    internal static class ReportApprovalRequest
    {
        /// <summary>
        /// The name of the action the reviewer approves or rejects.
        /// </summary>
        internal const string ActionName = "publish_report";

        /// <summary>
        /// Creates an approval request for the given analysis, with fresh request and call ids.
        /// Every argument is a plain string, so it reads the same after a checkpoint round trip
        /// and on any wire format.
        /// </summary>
        internal static ToolApprovalRequestContent Create(AnalysisResult analysis)
        {
            Dictionary<string, object?> arguments = new()
            {
                ["topic"] = analysis.Topic,
                ["summary"] = analysis.Summary,
                ["keyFindings"] = string.Join(
                    "\n", analysis.KeyFindings.Select(finding => $"- {finding.Fact} (source: {finding.Source})")),
            };

            FunctionCallContent action = new($"call_{Guid.NewGuid():N}", ActionName, arguments);
            return new ToolApprovalRequestContent($"approval_{Guid.NewGuid():N}", action);
        }

        /// <summary>
        /// Formats an approval request as the text a reviewer reads: the topic, the summary, and
        /// the key findings with their sources.
        /// </summary>
        internal static string Describe(ToolApprovalRequestContent request)
        {
            if (request.ToolCall is not FunctionCallContent action)
            {
                return $"Approval requested (id {request.RequestId}) with no details attached.";
            }

            return $"Topic: {ReadArgument(action, "topic")}\n" +
                $"Summary: {ReadArgument(action, "summary")}\n" +
                $"Key findings:\n{ReadArgument(action, "keyFindings")}";
        }

        /// <summary>
        /// Reads one argument as text. An argument is a string when the request is first created
        /// and a JSON string element after a checkpoint round trip; ToString() returns the plain
        /// text for both.
        /// </summary>
        private static string ReadArgument(FunctionCallContent action, string name)
        {
            return action.Arguments is not null && action.Arguments.TryGetValue(name, out object? value)
                ? value?.ToString() ?? string.Empty
                : string.Empty;
        }
    }
}
```

**`src/CapstoneDeployAndReview/PipelineConfig.cs`**

```csharp
namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Loads the small set of environment-driven settings the capstone pipeline needs beyond
    /// the model provider settings (which ChatClientFactory already owns). Declared in Lesson 22's
    /// design; Lessons 23 and 24 read McpServerPath to locate the companion MCP server.
    /// </summary>
    internal sealed class PipelineConfig
    {
        /// <summary>
        /// The topic the Researcher agent investigates. Defaults to a fixed demo topic so the
        /// capstone runs without any extra setup.
        /// </summary>
        internal required string ResearchTopic { get; init; }

        /// <summary>
        /// Filesystem path to the companion ResearchMcpServer.dll, following Lesson 7's
        /// pattern. A container sets it (see Lesson 24's Dockerfile). Null means "compute the default sibling-project path automatically" - see
        /// <see cref="McpServerConnectionFactory"/>.
        /// </summary>
        internal string? McpServerPath { get; init; }

        /// <summary>
        /// Reads CAPSTONE_RESEARCH_TOPIC and CAPSTONE_MCP_SERVER_PATH from the environment,
        /// falling back to a fixed demo topic and null respectively when unset.
        /// </summary>
        internal static PipelineConfig Load()
        {
            string topic = Environment.GetEnvironmentVariable("CAPSTONE_RESEARCH_TOPIC")
                ?? "the deep sea anglerfish";
            string? mcpServerPath = Environment.GetEnvironmentVariable("CAPSTONE_MCP_SERVER_PATH");

            return new PipelineConfig { ResearchTopic = topic, McpServerPath = mcpServerPath };
        }
    }
}
```

**`src/CapstoneDeployAndReview/McpServerConnectionFactory.cs`**

```csharp
using ModelContextProtocol.Client;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Builds the MCP client connection to the companion ResearchMcpServer process,
    /// launched as a subprocess and spoken to over standard input/output. Same pattern as Lesson 7's
    /// McpServerConnectionFactory, extended with an optional explicit path override sourced from
    /// PipelineConfig.
    /// </summary>
    internal static class McpServerConnectionFactory
    {
        /// <summary>
        /// Launches the research MCP server as a subprocess over stdio and returns a connected
        /// client. If overridePath is null, the companion server's built DLL is located via the
        /// same sibling-project, AppContext.BaseDirectory-anchored resolution Lesson 7 established -
        /// never Directory.GetCurrentDirectory(), which resolves differently depending on launch
        /// method. Throws a clear error if the companion server project has not been built yet.
        /// </summary>
        internal static async Task<McpClient> ConnectAsync(string? overridePath)
        {
            string serverDllPath = overridePath ?? ComputeDefaultServerDllPath();

            if (!File.Exists(serverDllPath))
            {
                throw new InvalidOperationException(
                    "The companion MCP server has not been built yet. Run 'dotnet build' inside " +
                    $"ResearchMcpServer before running this lesson. Expected to find: {serverDllPath}");
            }

            return await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Name = "ResearchMcpServer",
                Command = "dotnet",
                Arguments = [serverDllPath],
            }));
        }

        /// <summary>
        /// Computes the companion server's expected build output path relative to this running
        /// assembly's own folder.
        /// </summary>
        private static string ComputeDefaultServerDllPath()
        {
            string serverProjectDirectory = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ResearchMcpServer"));

            return Path.Combine(
                serverProjectDirectory, "bin", "Debug", "net10.0", "ResearchMcpServer.dll");
        }
    }
}
```

**`src/CapstoneDeployAndReview/ResearchExecutor.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// The graph's start node. It speaks MAF's chat protocol: incoming chat messages are collected,
    /// and a <see cref="TurnToken"/> tells it to take its turn. That is the input shape
    /// <c>Workflow.AsAIAgent()</c> drives, so the same graph runs in a console app and behind a
    /// hosted agent endpoint. On its turn it hands the user's topic to the Researcher agent and
    /// sends the agent's notes along the next edge.
    /// </summary>
    internal sealed class ResearchExecutor : ChatProtocolExecutor
    {
        /// <summary>
        /// The Researcher agent, with its MCP research tool.
        /// </summary>
        private readonly AIAgent researcher;

        /// <summary>
        /// Registers this executor under its fixed id. AutoSendTurnToken is off because the next
        /// node is a plain executor that takes a <see cref="ResearchNotes"/> message, not a turn.
        /// </summary>
        internal ResearchExecutor(AIAgent researcher)
            : base(CapstoneWorkflowFactory.ResearchExecutorId, new ChatProtocolExecutorOptions { AutoSendTurnToken = false })
        {
            this.researcher = researcher;
        }

        /// <summary>
        /// Declares the extra message type this executor sends, on top of the chat protocol.
        /// </summary>
        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder)
        {
            return base.ConfigureProtocol(protocolBuilder).SendsMessage<ResearchNotes>();
        }

        /// <summary>
        /// Runs the Researcher agent on the user's topic and sends its notes to the analysis step.
        /// A turn with no user text is skipped: a hosted session takes one such turn after it
        /// delivers the approval response, and there is nothing to research in it.
        /// </summary>
        protected override async ValueTask TakeTurnAsync(
            List<ChatMessage> messages, IWorkflowContext context, bool? emitEvents, CancellationToken cancellationToken = default)
        {
            string topic = string.Join(" ", messages.Where(m => m.Role == ChatRole.User).Select(m => m.Text)).Trim();
            if (topic.Length == 0)
            {
                return;
            }

            AgentResponse response = await this.researcher.RunAsync(topic, cancellationToken: cancellationToken);
            await context.SendMessageAsync(new ResearchNotes(topic, response.Text), cancellationToken: cancellationToken);
        }
    }
}
```

**`src/CapstoneDeployAndReview/AnalysisExecutor.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// The analysis node. It asks the Analyst agent to extract the research notes into the
    /// <see cref="AnalysisResult"/> JSON contract, parses the reply, keeps the result in workflow
    /// state for the Writer, and returns the approval request that the next edge carries to the
    /// approval port. <c>partial</c> so the workflow source generator can wire up its
    /// <c>[MessageHandler]</c>.
    /// </summary>
    internal sealed partial class AnalysisExecutor : Executor
    {
        /// <summary>
        /// The Analyst agent, with no tools.
        /// </summary>
        private readonly AIAgent analyst;

        /// <summary>
        /// Registers this executor under its fixed id.
        /// </summary>
        internal AnalysisExecutor(AIAgent analyst) : base(CapstoneWorkflowFactory.AnalysisExecutorId)
        {
            this.analyst = analyst;
        }

        /// <summary>
        /// Runs the Analyst on the notes, parses its reply, stores the analysis in workflow state
        /// (so it is part of every checkpoint), and returns the request for human approval.
        /// </summary>
        [MessageHandler]
        private async ValueTask<ToolApprovalRequestContent> AnalyzeAsync(
            ResearchNotes notes, IWorkflowContext context, CancellationToken cancellationToken)
        {
            AgentResponse response = await this.analyst.RunAsync(
                $"Topic: {notes.Topic}\n\nResearch notes:\n{notes.Notes}", cancellationToken: cancellationToken);

            AnalysisResult analysis = AnalysisParser.Parse(response.Text);

            await context.QueueStateUpdateAsync(
                CapstoneWorkflowFactory.AnalysisStateKey, analysis, CapstoneWorkflowFactory.StateScope, cancellationToken);

            return ReportApprovalRequest.Create(analysis);
        }
    }
}
```

**`src/CapstoneDeployAndReview/WriterExecutor.cs`**

```csharp
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// The final node. It receives the reviewer's decision from the approval port. On approval
    /// it reads the analysis back from workflow state, has the Writer agent draft the report, and
    /// yields the report as the workflow's output; on rejection it yields a short notice instead.
    /// <c>partial</c> so the workflow source generator can wire up its <c>[MessageHandler]</c>.
    /// </summary>
    [YieldsOutput(typeof(string))]
    internal sealed partial class WriterExecutor : Executor
    {
        /// <summary>
        /// The Writer agent, with no tools.
        /// </summary>
        private readonly AIAgent writer;

        /// <summary>
        /// Registers this executor under its fixed id.
        /// </summary>
        internal WriterExecutor(AIAgent writer) : base(CapstoneWorkflowFactory.WriterExecutorId)
        {
            this.writer = writer;
        }

        /// <summary>
        /// Acts on the reviewer's decision: drafts and yields the report when approved, or yields
        /// a rejection notice (with the reviewer's reason, if one was given) when not.
        /// </summary>
        [MessageHandler]
        private async ValueTask WriteAsync(
            ToolApprovalResponseContent decision, IWorkflowContext context, CancellationToken cancellationToken)
        {
            if (!decision.Approved)
            {
                string reason = string.IsNullOrWhiteSpace(decision.Reason) ? string.Empty : $" Reason: {decision.Reason}";
                await context.YieldOutputAsync($"The analysis was rejected, so no report was written.{reason}", cancellationToken);
                return;
            }

            AnalysisResult analysis = await context.ReadStateAsync<AnalysisResult>(
                CapstoneWorkflowFactory.AnalysisStateKey, CapstoneWorkflowFactory.StateScope, cancellationToken)
                ?? throw new InvalidOperationException("No analysis was found in workflow state.");

            AgentResponse response = await this.writer.RunAsync(
                $"Write a short, engaging report from this approved analysis:\n{JsonSerializer.Serialize(analysis)}",
                cancellationToken: cancellationToken);

            await context.YieldOutputAsync(response.Text, cancellationToken);
        }
    }
}
```

**`src/CapstoneDeployAndReview/CapstoneWorkflowFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Builds the capstone graph: Research -> Analysis -> approval port -> Writer. The ids below
    /// are fixed so a checkpoint written by one process restores onto the graph another process
    /// builds.
    /// </summary>
    internal static class CapstoneWorkflowFactory
    {
        /// <summary>
        /// Id of the start node, which runs the Researcher agent.
        /// </summary>
        internal const string ResearchExecutorId = "research";

        /// <summary>
        /// Id of the node that runs the Analyst agent and raises the approval request.
        /// </summary>
        internal const string AnalysisExecutorId = "analysis";

        /// <summary>
        /// Id of the request port where the workflow pauses for a human decision.
        /// </summary>
        internal const string ApprovalPortId = "report-approval";

        /// <summary>
        /// Id of the node that runs the Writer agent.
        /// </summary>
        internal const string WriterExecutorId = "writer";

        /// <summary>
        /// Workflow-state scope that holds the analysis between the Analysis and Writer nodes.
        /// </summary>
        internal const string StateScope = "capstone";

        /// <summary>
        /// Workflow-state key of the approved analysis.
        /// </summary>
        internal const string AnalysisStateKey = "analysis";

        /// <summary>
        /// Wires the three agents into the graph. The approval port's request and response types
        /// are the standard tool-approval contents, which every hosting surface knows how to carry.
        /// </summary>
        internal static Workflow Build(AIAgent researcher, AIAgent analyst, AIAgent writer)
        {
            ResearchExecutor research = new(researcher);
            AnalysisExecutor analysis = new(analyst);
            RequestPort<ToolApprovalRequestContent, ToolApprovalResponseContent> approval =
                RequestPort.Create<ToolApprovalRequestContent, ToolApprovalResponseContent>(ApprovalPortId);
            WriterExecutor write = new(writer);

            return new WorkflowBuilder(research)
                .AddEdge(research, analysis)
                .AddEdge(analysis, approval)
                .AddEdge(approval, write)
                .WithOutputFrom(write)
                .Build();
        }
    }
}
```

**`src/CapstoneDeployAndReview/Program.cs`**

```csharp
// Lesson 24 - Capstone Deploy & Review
// Hosts Lesson 23's capstone graph - Researcher -> Analyst -> human-approval request port ->
// Writer - as one agent over the Responses protocol. Workflow.AsAIAgent() turns the graph into an
// AIAgent, and AddFoundryResponses/MapFoundryResponses serve it at POST /responses. When the run
// reaches the approval port, the response carries an approval request; the caller answers it in
// a follow-up request and the same run continues to the Writer. Hosting checkpoints the run, so
// a pending approval survives a restart of this process. Nothing here talks to Azure. Run this
// project first (after building the companion ResearchMcpServer), then run
// CapstoneDeployAndReviewClient.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry.Hosting;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace CapstoneDeployAndReview
{
    /// <summary>
    /// Entry point that connects to the research MCP server, builds the capstone graph, wraps it
    /// as an agent, and serves it over the Responses protocol.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Address this host listens on, set explicitly in code so launchSettings.json cannot
        /// move it. Overridable via CAPSTONE_LISTEN_URL, because inside a container the host must
        /// bind "http://+:5200" (all interfaces); "http://localhost:5200" is unreachable from
        /// outside the container. See this lesson's Dockerfile.
        /// </summary>
        private static readonly string ListenUrl =
            Environment.GetEnvironmentVariable("CAPSTONE_LISTEN_URL") ?? "http://localhost:5200";

        /// <summary>
        /// Connects to the research MCP server, builds the graph and its agent, registers it with
        /// the Responses protocol handler, and starts listening.
        /// </summary>
        private static async Task Main(string[] args)
        {
            PipelineConfig config = PipelineConfig.Load();

            WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
            builder.WebHost.UseUrls(ListenUrl);

            // Connect once and keep the connection for the life of the host: unlike Lesson 23's
            // short-lived console runs, this process serves many requests.
            await using McpClient mcpClient = await McpServerConnectionFactory.ConnectAsync(config.McpServerPath);
            IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
            Console.WriteLine($"Research tools: {string.Join(", ", mcpTools.Select(t => t.Name))}");

            IChatClient chatClient = ChatClientFactory.Create();
            ChatClientAgent researcher = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.ResearcherName, AgentInstructions.ResearcherPersona,
                tools: [.. mcpTools.Cast<AITool>()]);
            ChatClientAgent analyst = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.AnalystName, AgentInstructions.AnalystPersona);
            ChatClientAgent writer = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.WriterName, AgentInstructions.WriterPersona);

            Workflow workflow = CapstoneWorkflowFactory.Build(researcher, analyst, writer);

            // One agent for the whole graph. The Writer's yielded report becomes the agent's reply
            // text, and the approval port's request becomes an approval request in that reply.
            AIAgent pipeline = workflow.AsAIAgent(
                id: "capstone-pipeline",
                name: "CapstonePipeline",
                description: "Researches a topic, extracts a structured analysis, asks for approval, then writes a report.");

            builder.Services.AddFoundryResponses(pipeline);

            WebApplication app = builder.Build();
            app.MapFoundryResponses();

            Console.WriteLine($"CapstonePipeline is served over the Responses protocol at {ListenUrl}/responses");
            Console.WriteLine("Waiting for requests. Run CapstoneDeployAndReviewClient in another terminal, or press Ctrl+C to stop.");

            await app.RunAsync();
        }
    }
}
```

**`src/CapstoneDeployAndReview/Dockerfile`**

```dockerfile
# Lesson 24 - Capstone Deploy & Review
# Builds and packages both the Responses host and its companion MCP server into a single image,
# since the host launches the MCP server as a subprocess over stdio rather than calling it over a
# network - both need to be inside the same container. Standard multi-stage .NET container
# pattern; the parts specific to this lesson are the CAPSTONE_MCP_SERVER_PATH and
# CAPSTONE_LISTEN_URL environment variables set at the bottom.
#
# Build from this lesson's src folder (the parent of both project folders):
#   docker build -f CapstoneDeployAndReview/Dockerfile -t capstone-pipeline .
# Run:
#   docker run --rm -p 5200:5200 -e OPENAI_API_KEY=$env:OPENAI_API_KEY capstone-pipeline

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY CapstoneDeployAndReview/CapstoneDeployAndReview.csproj CapstoneDeployAndReview/
COPY ResearchMcpServer/ResearchMcpServer.csproj ResearchMcpServer/
RUN dotnet restore CapstoneDeployAndReview/CapstoneDeployAndReview.csproj
RUN dotnet restore ResearchMcpServer/ResearchMcpServer.csproj

COPY CapstoneDeployAndReview/ CapstoneDeployAndReview/
COPY ResearchMcpServer/ ResearchMcpServer/

RUN dotnet publish CapstoneDeployAndReview/CapstoneDeployAndReview.csproj -c Release -o /app/host
RUN dotnet publish ResearchMcpServer/ResearchMcpServer.csproj -c Release -o /app/mcp-server

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/host ./host
COPY --from=build /app/mcp-server ./mcp-server

# The host resolves the MCP server via this override instead of its dev-time
# AppContext.BaseDirectory-relative sibling-project lookup, which does not apply once both
# projects' build outputs sit side by side inside one image rather than in a source checkout.
ENV CAPSTONE_MCP_SERVER_PATH=/app/mcp-server/ResearchMcpServer.dll

# Bind all interfaces, not just localhost - "http://localhost:5200" is unreachable from outside
# the container even with the port published via -p.
ENV CAPSTONE_LISTEN_URL=http://+:5200

EXPOSE 5200

ENTRYPOINT ["dotnet", "host/CapstoneDeployAndReview.dll"]
```

**`src/CapstoneDeployAndReviewClient/CapstoneDeployAndReviewClient.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CapstoneDeployAndReviewClient</RootNamespace>
  </PropertyGroup>

</Project>
```

**`src/CapstoneDeployAndReviewClient/ApprovalRequestItem.cs`**

```csharp
namespace CapstoneDeployAndReviewClient
{
    /// <summary>
    /// An <c>mcp_approval_request</c> output item from a Responses protocol reply: the item id the
    /// answer must quote, the name of the action awaiting approval, and that action's arguments
    /// as a JSON string.
    /// </summary>
    internal sealed record ApprovalRequestItem(string Id, string Name, string Arguments);
}
```

**`src/CapstoneDeployAndReviewClient/ResponsesRequests.cs`**

```csharp
using System.Text.Json.Nodes;

namespace CapstoneDeployAndReviewClient
{
    /// <summary>
    /// Builds the two JSON request bodies this client posts to the host's /responses endpoint:
    /// one that starts a run on a topic, and one that answers the run's approval request.
    /// </summary>
    internal static class ResponsesRequests
    {
        /// <summary>
        /// The body that starts a new run: the topic as plain text input.
        /// </summary>
        internal static string CreateStart(string topic)
        {
            return new JsonObject { ["input"] = topic }.ToJsonString();
        }

        /// <summary>
        /// The body that answers an approval request. previous_response_id continues the same
        /// conversation, and the single mcp_approval_response input item quotes the request's id
        /// and carries the decision.
        /// </summary>
        internal static string CreateApprovalReply(string previousResponseId, string approvalRequestId, bool approve)
        {
            return new JsonObject
            {
                ["previous_response_id"] = previousResponseId,
                ["input"] = new JsonArray(new JsonObject
                {
                    ["type"] = "mcp_approval_response",
                    ["approval_request_id"] = approvalRequestId,
                    ["approve"] = approve,
                }),
            }.ToJsonString();
        }
    }
}
```

**`src/CapstoneDeployAndReviewClient/ResponsesReader.cs`**

```csharp
using System.Text;
using System.Text.Json;

namespace CapstoneDeployAndReviewClient
{
    /// <summary>
    /// Reads the parts of a Responses protocol reply this client needs: the response id, the
    /// progress of each workflow step, any approval request, and the assistant's output text.
    /// </summary>
    internal static class ResponsesReader
    {
        /// <summary>
        /// Returns the reply's id, which a follow-up request quotes as previous_response_id.
        /// </summary>
        internal static string ReadResponseId(JsonElement response)
        {
            return response.GetProperty("id").GetString()
                ?? throw new InvalidOperationException("The reply has no id.");
        }

        /// <summary>
        /// Returns the id of each graph step that completed in this reply, in order. The host
        /// reports every step as a workflow_action output item.
        /// </summary>
        internal static List<string> ReadCompletedSteps(JsonElement response)
        {
            return [.. Items(response)
                .Where(item => Text(item, "type") == "workflow_action" && Text(item, "status") == "completed")
                .Select(item => Text(item, "action_id"))];
        }

        /// <summary>
        /// Returns the reply's approval request, or null if the run did not pause for one.
        /// </summary>
        internal static ApprovalRequestItem? FindApprovalRequest(JsonElement response)
        {
            return Items(response)
                .Where(item => Text(item, "type") == "mcp_approval_request")
                .Select(item => new ApprovalRequestItem(Text(item, "id"), Text(item, "name"), Text(item, "arguments")))
                .LastOrDefault();
        }

        /// <summary>
        /// Joins the text of every assistant message in the reply.
        /// </summary>
        internal static string ReadOutputText(JsonElement response)
        {
            StringBuilder text = new();
            foreach (JsonElement item in Items(response).Where(item => Text(item, "type") == "message"))
            {
                if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (JsonElement part in content.EnumerateArray().Where(part => Text(part, "type") == "output_text"))
                {
                    text.AppendLine(Text(part, "text"));
                }
            }

            return text.ToString().Trim();
        }

        /// <summary>
        /// Formats an approval request for the reviewer: the action name, then each argument as
        /// "name: value", with a multi-line value starting on the line after its name.
        /// </summary>
        internal static string Describe(ApprovalRequestItem request)
        {
            StringBuilder text = new($"Action awaiting approval: {request.Name}\n");
            using JsonDocument arguments = JsonDocument.Parse(request.Arguments.Length > 0 ? request.Arguments : "{}");
            foreach (JsonProperty argument in arguments.RootElement.EnumerateObject())
            {
                string value = argument.Value.ValueKind == JsonValueKind.String
                    ? argument.Value.GetString() ?? string.Empty
                    : argument.Value.GetRawText();
                text.Append(value.Contains('\n') ? $"{argument.Name}:\n{value}\n" : $"{argument.Name}: {value}\n");
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Enumerates the reply's output items, or nothing if it has none.
        /// </summary>
        private static IEnumerable<JsonElement> Items(JsonElement response)
        {
            return response.TryGetProperty("output", out JsonElement output) && output.ValueKind == JsonValueKind.Array
                ? output.EnumerateArray()
                : [];
        }

        /// <summary>
        /// Reads a string property, or returns an empty string if it is missing or not a string.
        /// </summary>
        private static string Text(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }
    }
}
```

**`src/CapstoneDeployAndReviewClient/Program.cs`**

```csharp
// Lesson 24 - Capstone Deploy & Review (client)
// Drives the hosted capstone over plain HTTP, the way any Responses protocol client can. The first
// POST /responses starts a run on a topic; the reply shows each graph step that ran and ends with
// an mcp_approval_request. After the decision is read from the console, a second POST quotes the
// first reply's id as previous_response_id and answers with an mcp_approval_response, and the same
// run continues to the Writer. Start CapstoneDeployAndReview (and build its companion
// ResearchMcpServer) first; this client fails to connect if the host is not running.

using System.Text;
using System.Text.Json;

namespace CapstoneDeployAndReviewClient
{
    /// <summary>
    /// Entry point that starts a hosted run, shows the approval request, sends the console
    /// decision back, and prints the final report.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Base address of the running CapstoneDeployAndReview host.
        /// </summary>
        private const string HostUrl = "http://localhost:5200/";

        /// <summary>
        /// The topic researched when none is given on the command line.
        /// </summary>
        private const string DefaultTopic = "the deep sea anglerfish";

        /// <summary>
        /// Starts a run, prints the approval request it pauses on, asks for the decision, sends
        /// it back in a follow-up request, and prints the result.
        /// </summary>
        private static async Task Main(string[] args)
        {
            string topic = args.Length > 0 ? string.Join(" ", args) : DefaultTopic;

            // A run calls the model several times, so allow more than HttpClient's 100-second default.
            using HttpClient http = new() { BaseAddress = new Uri(HostUrl), Timeout = TimeSpan.FromMinutes(5) };

            Console.WriteLine($"=== POST /responses: research '{topic}' ===");
            using JsonDocument first = await PostAsync(http, ResponsesRequests.CreateStart(topic));
            PrintSteps(first.RootElement);

            ApprovalRequestItem? request = ResponsesReader.FindApprovalRequest(first.RootElement);
            if (request is null)
            {
                Console.WriteLine("The run did not ask for approval. Output:");
                Console.WriteLine(ResponsesReader.ReadOutputText(first.RootElement));
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== Paused for approval ===");
            Console.WriteLine(ResponsesReader.Describe(request));
            Console.WriteLine();
            Console.WriteLine("The pending run is checkpointed by the host: you can stop and restart the");
            Console.WriteLine("host now, and this answer still resumes the same run.");
            Console.Write("Approve this analysis for the final report? [y/N] ");
            bool approve = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);

            Console.WriteLine();
            Console.WriteLine($"=== POST /responses: answer the approval request ({(approve ? "approve" : "reject")}) ===");
            string reply = ResponsesRequests.CreateApprovalReply(
                ResponsesReader.ReadResponseId(first.RootElement), request.Id, approve);
            using JsonDocument second = await PostAsync(http, reply);
            PrintSteps(second.RootElement);

            Console.WriteLine();
            Console.WriteLine("=== Result ===");
            Console.WriteLine(ResponsesReader.ReadOutputText(second.RootElement));
        }

        /// <summary>
        /// Posts a JSON body to /responses and returns the parsed reply. Throws with the host's
        /// error text if the request fails.
        /// </summary>
        private static async Task<JsonDocument> PostAsync(HttpClient http, string body)
        {
            using StringContent content = new(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await http.PostAsync("responses", content);
            string json = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"POST /responses returned {(int)response.StatusCode}: {json}");
            }

            return JsonDocument.Parse(json);
        }

        /// <summary>
        /// Prints the graph steps that completed in a reply, in order.
        /// </summary>
        private static void PrintSteps(JsonElement response)
        {
            Console.WriteLine($"Steps completed: {string.Join(" -> ", ResponsesReader.ReadCompletedSteps(response))}");
        }
    }
}
```

## Walkthrough

1. **Everything from `ChatClientFactory` to `CapstoneWorkflowFactory` is Lesson 23's code**, in this project's namespace. The graph, the executors, the port and the agents are the same; only the program around them changes.
2. **The host connects to the MCP server once, in `Main`, before it starts listening**, and keeps the connection for the life of the process with `await using` — a host serves many runs, unlike Lesson 23's one-run console processes.
3. **`CapstoneWorkflowFactory.Build` produces the graph, and `workflow.AsAIAgent(...)` wraps it** with an id, a name and a description. That single `AIAgent` is all `AddFoundryResponses` needs; `MapFoundryResponses` maps `POST /responses` (and the `/readiness` probe hosting uses).
4. **The client's first `POST /responses` sends `{"input": "<topic>"}`.** The host runs the graph until the approval port and replies with the `workflow_action` items and the `mcp_approval_request`. `PrintSteps` lists the completed steps; `FindApprovalRequest` returns the request item, and `Describe` prints its action name and each argument.
5. **The client asks for the decision at the console**, and while it waits the run is saved by the host — this is the moment to restart the host.
6. **The second `POST /responses` answers the request.** `CreateApprovalReply` quotes the first reply's id as `previous_response_id` and the request item's id as `approval_request_id`, with `approve` set from the console answer. The host restores the session, delivers the decision to the port as a `ToolApprovalResponseContent`, and the Writer runs (or, on a rejection, yields the rejection notice).
7. **`ReadOutputText` collects the `output_text` of the reply's assistant message** — the Writer's report — and the client prints it.
8. **The Dockerfile builds both projects in an SDK stage and copies both outputs into one ASP.NET runtime image.** Its two environment variables are the only container-specific settings; the code is identical locally and in a container.

## Expected Output

**Host (`CapstoneDeployAndReview`), on startup.** These three lines are fixed; ASP.NET Core and the Responses server then log their own `info:` lines, including `Now listening on: http://localhost:5200`:

```
Research tools: search_research_notes
CapstonePipeline is served over the Responses protocol at http://localhost:5200/responses
Waiting for requests. Run CapstoneDeployAndReviewClient in another terminal, or press Ctrl+C to stop.
```

**Client (`CapstoneDeployAndReviewClient`).** The fixed lines, with the model-written parts marked in angle brackets:

```
=== POST /responses: research 'the deep sea anglerfish' ===
Steps completed: research -> research -> analysis -> report-approval

=== Paused for approval ===
Action awaiting approval: publish_report
topic: <the Analyst's topic>
summary: <the Analyst's summary>
keyFindings:
- <fact> (source: <source>)
- <fact> (source: <source>)

The pending run is checkpointed by the host: you can stop and restart the
host now, and this answer still resumes the same run.
Approve this analysis for the final report? [y/N] y

=== POST /responses: answer the approval request (approve) ===
Steps completed: report-approval -> research -> writer

=== Result ===
<the Writer's report>
```

<!-- EXPECTED OUTPUT PENDING: re-run with OPENAI_API_KEY set -->

With a live model, the findings are the MCP server's anglerfish facts in the Analyst's words, each citing the Encyclopedia of Marine Biology, and the result is two or three short paragraphs of prose with no visible JSON. Restarting the host at the approval prompt does not change the client's output: the second request resumes the checkpointed run on the new host process. Answering anything other than `y` sends `"approve": false`, the steps are the same, and the result is `The analysis was rejected, so no report was written.`

## Series Review

Every lesson in the series lands somewhere in the finished system — most as code it runs, a few as the next step it is ready for.

| Lesson | Topic | Where it landed |
|---|---|---|
| 1 | Hello Agent & Personas | `ChatClientAgent` plus persona instructions for all three roles, built through `AgentFactory` on `ChatClientFactory` — the one place the model provider and model are chosen, via the required `MAF_LLM` and `MAF_MODEL` |
| 2 | Function Tools | The tools-on-`ChatClientAgent` mechanism the Researcher's MCP tool plugs into |
| 3 | Structured Output & Multimodal | The Analyst's structured extraction: a prompt contract for the `AnalysisResult` shape, read by `AnalysisParser` |
| 4 | Sessions & Chat History | The hosted workflow agent's session — what the host saves between the two requests, selected by `previous_response_id` |
| 5 | Context Providers | Ready for the Researcher: user profile or retrieved documents injected into its runs |
| 6 | Middleware | Ready for cross-cutting behaviour on any agent's chat client — logging, guardrails on the Writer's output |
| 7 | MCP as a Tool Source | The Researcher's `SearchResearchNotes` tool, from the companion `ResearchMcpServer` over stdio |
| 8 | Hosted Tools | Ready for the Researcher: a provider-hosted web search alongside its MCP tool |
| 9 | Approvals (Function Invocation Gating) | `ToolApprovalRequestContent` / `ToolApprovalResponseContent` — the same approval contents, now carried by a workflow port |
| 10 | Workflows 101 | The `WorkflowBuilder` graph: four nodes, typed edges, `WithOutputFrom` |
| 11 | Executors & Handlers | `ResearchExecutor`, `AnalysisExecutor` and `WriterExecutor` — custom nodes, two of them `partial` with `[MessageHandler]` |
| 12 | Sequential Pattern | The pipeline's shape: each step consumes the previous step's output, in a fixed order |
| 13 | Concurrent Pattern | Ready for a wider pipeline: several Researchers on different angles, merged before the Analyst |
| 14 | Handoff Pattern | Not needed — the roles are fixed, not delegated at run time |
| 15 | Group Chat Pattern | Not needed — the agents hand off typed results rather than sharing a conversation |
| 16 | Magentic-One Pattern | Not needed — a planner suits open-ended goals; this pipeline's steps are known in advance |
| 17 | Human-in-the-Loop | The `report-approval` `RequestPort`, where the run pauses for a person |
| 18 | Checkpointing & Durability | `CheckpointManager` + `FileSystemJsonCheckpointStore` in Lesson 23; the host's automatic checkpoints here |
| 19 | A2A Protocol | The other way to expose an `AIAgent` across processes; the capstone uses the Responses protocol because its approval maps onto standard Responses items |
| 20 | Observability & Evaluation | The next production step: OpenTelemetry on the shared chat client, and quality evaluators on the Analyst's and Writer's output |
| 21 | Hosting & Deployment | `AddFoundryResponses` / `MapFoundryResponses` — the hosting this lesson uses, now with a workflow agent |
| 22 | Capstone Design | The graph shape, the approval port's place and contents, and the shared contracts every capstone lesson reuses |
| 23 | Capstone Build | The graph itself, run and resumed in-process — hosted here unchanged |

The capstone is the series in miniature: agents with personas and tools (Lessons 1–9), composed into a graph of custom executors (Lessons 10–12), paused for a person and made durable (Lessons 17–18), and served to clients over a standard protocol (Lessons 19–21). Each layer was added without rewriting the one beneath it — Lesson 23's graph runs in a console, and Lesson 24 hosts the same graph by wrapping it, not by changing it.
