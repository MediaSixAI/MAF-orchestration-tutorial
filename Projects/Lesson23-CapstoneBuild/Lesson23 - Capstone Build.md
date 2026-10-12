# Lesson 23 — Capstone Build

## Project Overview

This lesson builds Lesson 22's design as one running graph workflow: a Researcher agent with real MCP tool access, an Analyst agent that produces a typed `AnalysisResult`, a request port where the run pauses for a human decision, and a Writer agent that drafts the report once the analysis is approved. The run is checkpointed to disk, so `dotnet run` can stop at the approval port and exit, and `dotnet run -- resume` — a completely fresh process — restores the paused run, takes the decision at the console, and finishes it. A pause that survives the process ending is what makes a human-approval step usable in a real system, where review can take hours.

## Prerequisites

Lesson 22 (the design, and the shared pieces this lesson reuses: `AgentInstructions`, `ResearchFinding`, `AnalysisResult`, `AnalysisParser`, `JsonFenceStripper`, `ReportApprovalRequest`, `PipelineConfig`). Lesson 7 (MCP as a tool source — this lesson follows the same two-project host + server layout). Lessons 10–11 (custom executors and `[MessageHandler]`). Lesson 17 (`RequestPort`). Lesson 18 (`CheckpointManager` and `FileSystemJsonCheckpointStore`). The solution itself is standalone.

## Setup

**Two projects, following Lesson 7's layout:**

- `ResearchMcpServer` — a minimal MCP server exposing one tool, `SearchResearchNotes`, over a small fixed knowledge base. The pipeline launches it as a subprocess; you do not run it directly.
- `CapstoneBuild` — the pipeline itself. **Build the solution (or the server project) first**, so the server's DLL exists when the pipeline starts it.

**`ResearchMcpServer` packages:**

| Package | Version | Purpose |
|---|---|---|
| `ModelContextProtocol` | 2.2.0 | MCP server SDK — `AddMcpServer`, `WithStdioServerTransport`, `WithToolsFromAssembly` |
| `Microsoft.Extensions.Hosting` | 10.0.12 | Generic host for the MCP server process |

**`CapstoneBuild` packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent` |
| `Microsoft.Agents.AI.Workflows` | 1.24.0 | `WorkflowBuilder`, `ChatProtocolExecutor`, `RequestPort`, `InProcessExecution`, `CheckpointManager`, `FileSystemJsonCheckpointStore` |
| `Microsoft.Agents.AI.Workflows.Generators` | 1.24.0 | Source generator that wires up `[MessageHandler]` methods on `partial` executors (`PrivateAssets="all"`: build-time only) |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `ModelContextProtocol` | 2.2.0 | MCP client SDK — `McpClient`, `StdioClientTransport` |
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

**Lesson-specific settings:**

```powershell
# Optional - defaults to "the deep sea anglerfish" if unset:
$env:CAPSTONE_RESEARCH_TOPIC = "your topic here"

# Optional - path to ResearchMcpServer.dll; defaults to the sibling project's build output:
$env:CAPSTONE_MCP_SERVER_PATH = "C:\path\to\ResearchMcpServer.dll"
```

**The research tool has real notes for three demo topics** — anglerfish, octopus, and narwhal — because it is a small, fixed knowledge base rather than a live search API. Any other topic still runs end to end; the tool says it has no dedicated notes and the Researcher reports that.

**Running it — two commands, in order, from `src/CapstoneBuild`:**

```powershell
dotnet run                 # runs research and analysis, pauses at the approval port, exits
dotnet run -- resume       # a new process: restores the checkpoint, asks for the decision, writes the report
```

## Core Concepts

- **One graph, four nodes.** `CapstoneWorkflowFactory.Build` wires `research` → `analysis` → `report-approval` → `writer` with `WorkflowBuilder.AddEdge`, and `WithOutputFrom(writer)` marks the Writer's yielded text as the workflow's output. Each agent runs inside the executor that owns its step, so each edge carries one typed message: `ResearchNotes`, then `ToolApprovalRequestContent`, then `ToolApprovalResponseContent`.

- **The start node is a `ChatProtocolExecutor`.** It collects incoming chat messages and acts when it receives a `TurnToken`, the same input shape `Workflow.AsAIAgent()` sends in Lesson 24. Its `TakeTurnAsync` override joins the user text into the topic, runs the Researcher, and sends `ResearchNotes` with `context.SendMessageAsync`; `ConfigureProtocol` declares that extra message type with `SendsMessage<ResearchNotes>()`. `AutoSendTurnToken = false` because the next node takes `ResearchNotes`, not a turn. A turn with no user text is skipped — a hosted session takes one such turn after it delivers the approval response.

- **Agents inside executors.** `AnalysisExecutor` and `WriterExecutor` are `partial` classes whose `[MessageHandler]` methods call `agent.RunAsync(...)`. The `Microsoft.Agents.AI.Workflows.Generators` package generates the routing code that connects each handler's parameter type to the messages arriving on the executor's edges. A handler's return value is sent along the outgoing edge: `AnalysisExecutor` returns the `ToolApprovalRequestContent`, which is exactly what the approval port accepts.

- **The approval port.** `RequestPort.Create<ToolApprovalRequestContent, ToolApprovalResponseContent>("report-approval")` is a node that forwards whatever reaches it out of the workflow as a request, and sends the matching response on to the next node. When the request goes out, the run raises a `RequestInfoEvent` and its status becomes `PendingRequests`. The caller reads the request with `TryGetDataAs<ToolApprovalRequestContent>`, builds the decision with `request.CreateResponse(approved, reason)`, wraps it for the port with `pending.Request.CreateResponse(decision)`, and resumes with `run.ResumeAsync([...])`. The `WriterExecutor` handler then receives the `ToolApprovalResponseContent`.

- **What survives the pause lives in workflow state.** `AnalysisExecutor` stores the `AnalysisResult` with `context.QueueStateUpdateAsync(key, value, scope)`, and `WriterExecutor` reads it back with `context.ReadStateAsync<AnalysisResult>(key, scope)`. Workflow state is saved in every checkpoint, so the analysis is there for the Writer whether it runs a second later or in another process the next day.

- **File-based checkpointing.** `CheckpointManager.CreateJson(new FileSystemJsonCheckpointStore(directory))` writes a JSON checkpoint at the end of every superstep (each round of message delivery through the graph), keyed by the session id passed to `InProcessExecution.RunAsync(workflow, input, checkpointManager, sessionId)`. `run.LastCheckpoint` is the checkpoint taken when the run paused.

- **Resuming in a fresh process.** `dotnet run -- resume` builds the graph again from scratch — the same node ids, so the checkpoint fits — finds the newest checkpoint with `checkpointManager.GetLatestCheckpointAsync(sessionId)`, and restores it with `InProcessExecution.ResumeAsync(workflow, checkpoint, checkpointManager)`. The restored run re-raises the pending `RequestInfoEvent`, so the second process sees the same approval request the first one did. Nothing is carried over in memory; only the checkpoint folder connects the two processes.

- **One decision per run.** Starting a run deletes old checkpoints first, and a finished `resume` deletes them afterwards, so the same approval cannot be answered twice.

- **The Researcher's MCP tool access.** `ResearchMcpServer` follows Lesson 7's shape — a `[McpServerToolType]` static class, discovered by `WithToolsFromAssembly`, hosted over stdio — exposing one tool, `SearchResearchNotes`. `McpServerConnectionFactory` anchors to `AppContext.BaseDirectory`, never `Directory.GetCurrentDirectory()`, to find the sibling server's build output however the pipeline is launched, and accepts `CAPSTONE_MCP_SERVER_PATH` as an override. Both modes connect, because both build the full graph; the server is only called while the Researcher runs.

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
// Lesson 23 - Capstone Build
// A minimal Model Context Protocol server exposing one research tool - SearchResearchNotes - over
// standard input/output. This process is launched as a subprocess by the companion
// CapstoneBuild client project; a human does not run it directly.

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

**`src/CapstoneBuild/CapstoneBuild.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CapstoneBuild</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.24.0" />
    <PackageReference Include="Microsoft.Agents.AI.Workflows" Version="1.24.0" />
    <PackageReference Include="Microsoft.Agents.AI.Workflows.Generators" Version="1.24.0" PrivateAssets="all" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.10.1" />
    <PackageReference Include="ModelContextProtocol" Version="2.2.0" />
    <PackageReference Include="OpenAI" Version="2.14.0" />
  </ItemGroup>

</Project>
```

**`src/CapstoneBuild/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CapstoneBuild
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

**`src/CapstoneBuild/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CapstoneBuild
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

**`src/CapstoneBuild/AgentInstructions.cs`**

```csharp
namespace CapstoneBuild
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

**`src/CapstoneBuild/ResearchFinding.cs`**

```csharp
namespace CapstoneBuild
{
    /// <summary>
    /// One discrete fact the Researcher agent surfaced, as extracted and structured by the
    /// Analyst agent. Part of the shared data contract defined in Lesson 22's design.
    /// </summary>
    internal sealed record ResearchFinding(string Fact, string Source);
}
```

**`src/CapstoneBuild/AnalysisResult.cs`**

```csharp
namespace CapstoneBuild
{
    /// <summary>
    /// The Analyst agent's structured extraction of the Researcher agent's raw notes - the
    /// contract the human-approval request presents for review and the Writer agent consumes once
    /// it is approved. Defined in Lesson 22's design and used unchanged by every capstone lesson.
    /// </summary>
    internal sealed record AnalysisResult(string Topic, string Summary, List<ResearchFinding> KeyFindings);
}
```

**`src/CapstoneBuild/ResearchNotes.cs`**

```csharp
namespace CapstoneBuild
{
    /// <summary>
    /// The message the research step sends along the graph's first edge: the topic that was
    /// researched and the Researcher agent's raw, unstructured notes about it.
    /// </summary>
    internal sealed record ResearchNotes(string Topic, string Notes);
}
```

**`src/CapstoneBuild/JsonFenceStripper.cs`**

```csharp
namespace CapstoneBuild
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

**`src/CapstoneBuild/AnalysisParser.cs`**

```csharp
using System.Text.Json;

namespace CapstoneBuild
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

**`src/CapstoneBuild/ReportApprovalRequest.cs`**

```csharp
using Microsoft.Extensions.AI;

namespace CapstoneBuild
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

**`src/CapstoneBuild/PipelineConfig.cs`**

```csharp
namespace CapstoneBuild
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

**`src/CapstoneBuild/McpServerConnectionFactory.cs`**

```csharp
using ModelContextProtocol.Client;

namespace CapstoneBuild
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

**`src/CapstoneBuild/ResearchExecutor.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneBuild
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

**`src/CapstoneBuild/AnalysisExecutor.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneBuild
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

**`src/CapstoneBuild/WriterExecutor.cs`**

```csharp
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneBuild
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

**`src/CapstoneBuild/CapstoneWorkflowFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace CapstoneBuild
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

**`src/CapstoneBuild/Program.cs`**

```csharp
// Lesson 23 - Capstone Build
// Builds the capstone as one graph workflow - Researcher (with MCP tool access) -> Analyst
// (structured extraction) -> human-approval request port -> Writer - and runs it with file-based
// checkpointing. "dotnet run" runs the graph until it pauses at the approval port, then exits;
// the pending approval lives only in the checkpoint on disk. "dotnet run -- resume" starts a
// fresh process, restores that checkpoint, asks for the decision at the console, and finishes
// the run.

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace CapstoneBuild
{
    /// <summary>
    /// Entry point with two modes: "start" (the default) runs the workflow until it pauses for
    /// approval; "resume" restores the paused run from its checkpoint and completes it.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// The checkpoint session id. Both processes use it to find the same run.
        /// </summary>
        private const string SessionId = "capstone-run";

        /// <summary>
        /// Folder the checkpoints are written to, next to the built executable.
        /// </summary>
        private static readonly DirectoryInfo CheckpointDirectory =
            new(Path.Combine(AppContext.BaseDirectory, "checkpoints"));

        /// <summary>
        /// Dispatches to the start or resume mode based on the first command-line argument.
        /// </summary>
        private static async Task Main(string[] args)
        {
            string mode = args.Length > 0 ? args[0].ToLowerInvariant() : "start";

            switch (mode)
            {
                case "start":
                    await StartAsync();
                    break;
                case "resume":
                    await ResumeAsync();
                    break;
                default:
                    Console.WriteLine($"Unknown mode '{mode}'. Use 'dotnet run' to start a run, or " +
                        "'dotnet run -- resume' to review and finish a paused one.");
                    break;
            }
        }

        /// <summary>
        /// Starts a fresh checkpointed run on the configured topic, lets it run until the approval
        /// port raises its request, shows the request, and exits with the run paused on disk.
        /// </summary>
        private static async Task StartAsync()
        {
            PipelineConfig config = PipelineConfig.Load();

            if (CheckpointDirectory.Exists)
            {
                CheckpointDirectory.Delete(recursive: true);
            }

            CheckpointDirectory.Create();

            Console.WriteLine("=== Start: Researcher -> Analyst -> approval port -> Writer ===");
            Console.WriteLine($"Research topic: {config.ResearchTopic}");

            using FileSystemJsonCheckpointStore store = new(CheckpointDirectory);
            CheckpointManager checkpointManager = CheckpointManager.CreateJson(store, customOptions: null);

            await using McpClient mcpClient = await McpServerConnectionFactory.ConnectAsync(config.McpServerPath);
            Workflow workflow = await BuildWorkflowAsync(mcpClient);

            // The start node speaks the chat protocol: send the topic as a user message, then a
            // TurnToken to tell it to take its turn.
            await using Run run = await InProcessExecution.RunAsync(
                workflow, new ChatMessage(ChatRole.User, config.ResearchTopic), checkpointManager, SessionId);
            await run.ResumeAsync(CancellationToken.None, [new TurnToken(emitEvents: false)]);

            RequestInfoEvent? pending = ReportErrors(run.NewEvents).OfType<RequestInfoEvent>().LastOrDefault();
            if (pending is null || !pending.Request.TryGetDataAs(out ToolApprovalRequestContent? request))
            {
                Console.WriteLine($"The run did not pause for approval. Status: {await run.GetStatusAsync()}");
                return;
            }

            Console.WriteLine();
            Console.WriteLine("=== Paused for human approval ===");
            Console.WriteLine(ReportApprovalRequest.Describe(request));
            Console.WriteLine();
            Console.WriteLine($"Status: {await run.GetStatusAsync()}");
            Console.WriteLine($"Checkpoint saved: {run.LastCheckpoint?.CheckpointId}");
            Console.WriteLine("This process now exits. To review and finish the run, use:");
            Console.WriteLine("  dotnet run -- resume");
        }

        /// <summary>
        /// Restores the latest checkpoint into a freshly built graph, shows the pending approval
        /// request, reads the decision from the console, and resumes the run to completion.
        /// </summary>
        private static async Task ResumeAsync()
        {
            PipelineConfig config = PipelineConfig.Load();

            if (!CheckpointDirectory.Exists)
            {
                Console.WriteLine("No paused run found. Start one first with 'dotnet run'.");
                return;
            }

            CheckpointInfo? latest;
            using (FileSystemJsonCheckpointStore store = new(CheckpointDirectory))
            {
                CheckpointManager checkpointManager = CheckpointManager.CreateJson(store, customOptions: null);
                latest = await checkpointManager.GetLatestCheckpointAsync(SessionId);
                if (latest is null)
                {
                    Console.WriteLine("No paused run found. Start one first with 'dotnet run'.");
                    return;
                }

                Console.WriteLine($"=== Resume: restoring checkpoint {latest.CheckpointId} in a new process ===");

                await using McpClient mcpClient = await McpServerConnectionFactory.ConnectAsync(config.McpServerPath);
                Workflow workflow = await BuildWorkflowAsync(mcpClient);

                await using Run run = await InProcessExecution.ResumeAsync(workflow, latest, checkpointManager);

                RequestInfoEvent? pending = ReportErrors(run.NewEvents).OfType<RequestInfoEvent>().LastOrDefault();
                if (pending is null || !pending.Request.TryGetDataAs(out ToolApprovalRequestContent? request))
                {
                    Console.WriteLine("Nothing is waiting for approval in this run.");
                    return;
                }

                Console.WriteLine(ReportApprovalRequest.Describe(request));
                Console.WriteLine();
                Console.Write("Approve this analysis for the final report? [y/N] ");
                bool approved = string.Equals(Console.ReadLine()?.Trim(), "y", StringComparison.OrdinalIgnoreCase);
                Console.WriteLine();

                ToolApprovalResponseContent decision = request.CreateResponse(
                    approved, approved ? null : "Rejected at the console.");
                await run.ResumeAsync([pending.Request.CreateResponse(decision)]);

                foreach (WorkflowOutputEvent output in ReportErrors(run.NewEvents).OfType<WorkflowOutputEvent>())
                {
                    Console.WriteLine(approved ? "=== Final report ===" : "=== Result ===");
                    Console.WriteLine(output.Data);
                }

                Console.WriteLine();
                Console.WriteLine($"Status: {await run.GetStatusAsync()}");
            }

            // The run is finished, so its checkpoints have nothing left to resume.
            CheckpointDirectory.Delete(recursive: true);
        }

        /// <summary>
        /// Lists the research server's tools and builds the three agents and the capstone graph.
        /// Both modes build the identical graph, so a checkpoint from one restores onto the other.
        /// </summary>
        private static async Task<Workflow> BuildWorkflowAsync(McpClient mcpClient)
        {
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

            return CapstoneWorkflowFactory.Build(researcher, analyst, writer);
        }

        /// <summary>
        /// Prints any executor failure or workflow error among the given events, and returns the
        /// events so the caller can keep reading them.
        /// </summary>
        private static List<WorkflowEvent> ReportErrors(IEnumerable<WorkflowEvent> events)
        {
            List<WorkflowEvent> list = [.. events];
            foreach (WorkflowEvent evt in list)
            {
                switch (evt)
                {
                    case ExecutorFailedEvent failed:
                        Console.WriteLine($"Executor '{failed.ExecutorId}' failed: {failed.Data}");
                        break;
                    case WorkflowErrorEvent error:
                        Console.WriteLine($"Workflow error: {error.Exception?.Message}");
                        break;
                }
            }

            return list;
        }
    }
}
```

## Walkthrough

1. **The MCP server is Lesson 7's shape with one research tool**: `[McpServerToolType]` discovery, stdio hosting, and logging sent to standard error so it never corrupts the protocol stream on standard output.
2. **`BuildWorkflowAsync` assembles the graph.** It lists the server's tools, gives them to the Researcher through `AgentFactory`'s tools overload, builds the tool-free Analyst and Writer, and hands all three to `CapstoneWorkflowFactory.Build`. Both modes call it, so both build the identical graph.
3. **`StartAsync` clears old checkpoints and opens the checkpoint store**, then starts the run with the topic as a user message and the checkpoint manager attached. `run.ResumeAsync(CancellationToken.None, [new TurnToken(emitEvents: false)])` sends the `TurnToken` that tells the start node to take its turn.
4. **The graph runs until the approval port.** `ResearchExecutor` runs the Researcher (which calls `SearchResearchNotes`) and sends `ResearchNotes`; `AnalysisExecutor` runs the Analyst, parses the reply with `AnalysisParser`, stores the `AnalysisResult` in workflow state, and returns the `ToolApprovalRequestContent`; the port forwards it out of the workflow and the run stops with status `PendingRequests`.
5. **`StartAsync` finds the `RequestInfoEvent` in `run.NewEvents`**, prints the request through `ReportApprovalRequest.Describe`, shows the checkpoint id, and returns. The `using` and `await using` declarations dispose the run, the MCP connection and the store, and the process exits with the run paused on disk. `ReportErrors` prints any `ExecutorFailedEvent` or `WorkflowErrorEvent` along the way, so a malformed Analyst reply is reported rather than silently stopping the run.
6. **`ResumeAsync` runs in a new process.** It opens the same checkpoint folder, asks `GetLatestCheckpointAsync("capstone-run")` for the newest checkpoint, builds the graph again, and calls `InProcessExecution.ResumeAsync`. The restored run's `NewEvents` contains the pending `RequestInfoEvent`, carrying the same `ToolApprovalRequestContent` — its arguments now restored from JSON, which `Describe` reads the same way.
7. **The decision comes from the console.** `y` approves; anything else rejects with a reason. `request.CreateResponse` builds the `ToolApprovalResponseContent`, `pending.Request.CreateResponse` addresses it to the port, and `run.ResumeAsync` delivers it.
8. **`WriterExecutor` acts on the decision.** Approved: it reads the `AnalysisResult` from workflow state, runs the Writer on it, and yields the report. Rejected: it yields a notice with the reason. `ResumeAsync` prints the `WorkflowOutputEvent`, then deletes the checkpoint folder, because the finished run has nothing left to resume.

## Expected Output

**First command — `dotnet run`.** The first three lines are fixed (the tool name comes from the MCP server):

```
=== Start: Researcher -> Analyst -> approval port -> Writer ===
Research topic: the deep sea anglerfish
Research tools: search_research_notes
```

<!-- EXPECTED OUTPUT PENDING: re-run with OPENAI_API_KEY set -->

The run then prints `=== Paused for human approval ===` and the request: `Topic:`, `Summary:`, and `Key findings:` with one `- fact (source: Encyclopedia of Marine Biology)` line per finding — the facts come from the MCP server's anglerfish notes, worded by the Analyst. It ends with `Status: PendingRequests`, `Checkpoint saved:` and a 32-character checkpoint id, and the instruction to run `dotnet run -- resume`. The process exits.

**Second command, run afterward — `dotnet run -- resume`.** The first line names the checkpoint the first process saved:

```
=== Resume: restoring checkpoint <the id printed above> in a new process ===
Research tools: search_research_notes
```

<!-- EXPECTED OUTPUT PENDING: re-run with OPENAI_API_KEY set -->

It prints the same `Topic:` / `Summary:` / `Key findings:` block as the first process — restored from the checkpoint, not regenerated — then `Approve this analysis for the final report? [y/N]`. After `y`, it prints `=== Final report ===` followed by two or three short paragraphs of prose with no visible JSON, then `Status: Idle`. After any other answer it prints `=== Result ===` and `The analysis was rejected, so no report was written. Reason: Rejected at the console.`

Running `dotnet run -- resume` again prints `No paused run found. Start one first with 'dotnet run'.` — the finished run's checkpoints were deleted, so the decision cannot be applied twice.
