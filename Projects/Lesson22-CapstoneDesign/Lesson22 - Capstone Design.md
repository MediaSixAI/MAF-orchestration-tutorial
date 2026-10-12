# Lesson 22 — Capstone Design

## Project Overview

This lesson designs the Lessons 22–24 capstone: a Researcher → Analyst → Writer pipeline built as **one graph workflow**, with a human-approval request port between the Analyst and the Writer, checkpointed so a paused run survives a restart, and hosted as a single agent over the Responses protocol. It defines the shared pieces the graph is assembled from — personas, the `AnalysisResult` data contract, the Analyst's parser, the approval request, and a config loader — and tests the riskiest seam against a live model: the Analyst's free-text reply becoming a typed `AnalysisResult` and then the approval request a human will review. Settling these contracts now is what makes Lesson 23 composition rather than new problem-solving.

## Prerequisites

This lesson composes patterns from across the series: Lesson 1 (agent factory and personas), Lesson 3 (structured output), Lesson 7 (MCP tools), Lesson 9 (`ToolApprovalRequestContent` / `ToolApprovalResponseContent`), Lessons 10–11 (graphs, custom executors, `[MessageHandler]`), Lesson 17 (`RequestPort` for human-in-the-loop), Lesson 18 (checkpointing), and Lesson 21 (`AddFoundryResponses` hosting). The code in this lesson stands alone: it needs only an API key.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AgentResponse` |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient`; brings `ToolApprovalRequestContent` from `Microsoft.Extensions.AI` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

This lesson runs two agents directly, so it does not reference `Microsoft.Agents.AI.Workflows` yet. Lesson 23 adds it, along with `Microsoft.Agents.AI.Workflows.Generators` for the `[MessageHandler]` executors.

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

**Lesson-specific setting:**

```powershell
# Optional - defaults to "the deep sea anglerfish" if unset:
$env:CAPSTONE_RESEARCH_TOPIC = "your topic here"
```

## Core Concepts

- **The architecture is one graph.** Every step of the capstone — research, analysis, the human decision, and the write-up — is a node in a single `WorkflowBuilder` graph. One run carries a topic all the way to a report, and the pause for approval happens inside that run:

  ```
   user message: the research topic
        |
        v
   ┌──────────────────┐  ResearchExecutor (a ChatProtocolExecutor)
   │ research         │  Researcher agent + MCP tool SearchResearchNotes (Lesson 7)
   └────────┬─────────┘
            │ ResearchNotes (topic, raw notes)
            v
   ┌──────────────────┐  AnalysisExecutor
   │ analysis         │  Analyst agent -> AnalysisParser -> AnalysisResult
   └────────┬─────────┘  (AnalysisResult saved in workflow state)
            │ ToolApprovalRequestContent ("publish_report", the analysis as arguments)
            v
   ┌──────────────────┐  RequestPort<ToolApprovalRequestContent, ToolApprovalResponseContent>
   │ report-approval  │  the run pauses here until a person answers (Lesson 17)
   └────────┬─────────┘
            │ ToolApprovalResponseContent (approved or rejected)
            v
   ┌──────────────────┐  WriterExecutor
   │ writer           │  Writer agent reads AnalysisResult from state -> yields the report
   └──────────────────┘

   Checkpointed after every step (Lesson 18). Hosted as one agent with
   Workflow.AsAIAgent() + AddFoundryResponses (Lesson 21) in Lesson 24.
  ```

- **Agent responsibilities.** Each agent has one job, one input and one output:

  | Agent | Job | Input | Output | Tools |
  |---|---|---|---|---|
  | Researcher | Gather sourced facts | The topic | Raw notes, citing sources | `SearchResearchNotes` over MCP |
  | Analyst | Turn notes into structure | The topic and the notes | JSON in the `AnalysisResult` shape | None |
  | Writer | Turn approved structure into prose | The approved `AnalysisResult` as JSON | A short report | None |

- **Tool access per agent is least privilege.** Only the Researcher reaches outside the process, through the companion `ResearchMcpServer` (Lesson 7's pattern, added in Lesson 23). The Analyst and Writer are pure reasoning passes over text an earlier step already gathered, so they get no tools. In this lesson's seam test the Researcher has no tools yet either, and answers from the model's own knowledge so the test stays fast and dependency-free.

- **Graph shape: custom executors that call agents.** Each agent runs inside its own executor (`agent.RunAsync(...)` in a handler), so the edges carry typed contracts — `ResearchNotes`, then `ToolApprovalRequestContent`, then `ToolApprovalResponseContent` — rather than a chat transcript. `AgentWorkflowBuilder.BuildSequential` (Lesson 12) is the right tool for a pure agent chain; this pipeline needs a request port in the middle and typed hand-offs, so the graph is built by hand with `WorkflowBuilder` (Lessons 10–11). The ids `research`, `analysis`, `report-approval` and `writer` are fixed, so a checkpoint written by one process restores onto the graph another process builds. The roles are fixed and known in advance, so a planner-led Magentic orchestration (Lesson 16) is not needed.

- **The start node speaks the chat protocol.** `Workflow.AsAIAgent()` drives a workflow by sending it chat messages and then a `TurnToken`. Designing the start node as a `ChatProtocolExecutor` from the beginning means Lesson 23's console graph is hosted in Lesson 24 without changing a line of it.

- **Where the approval port sits, and what it carries.** The port sits between the Analyst and the Writer — the cheapest point to stop bad facts, after they have been structured and before any prose is generated from them. The reviewer sees the structured analysis, not a finished draft. The request is the standard `ToolApprovalRequestContent` from `Microsoft.Extensions.AI` (Lesson 9's type): it names the action awaiting sign-off, `publish_report`, and carries the topic, summary and findings as that action's arguments. The answer is the matching `ToolApprovalResponseContent`. Because these are the standard approval contents, every surface that hosts the workflow knows how to carry them: at the console in Lesson 23 they are read and answered directly, and over the Responses protocol in Lesson 24 they travel as an `mcp_approval_request` item and an `mcp_approval_response` input item.

- **Structured output is a prompt contract plus a parser — a design choice.** The Analyst's instructions spell out the exact JSON shape to emit. `AnalysisParser` is the receiving side: it runs `JsonFenceStripper` first, then deserializes with case-insensitive property names, and throws with the offending text if the reply does not match. Keeping the contract in the instructions makes the shape visible in one place and parses the same way on every provider `MAF_LLM` can select. The fence stripper is defensive parsing: the instructions forbid code fences, and the parser accepts a reply either way.

- **Data between nodes: messages for the hand-off, state for what must survive the pause.** Each node passes its result to the next as a message (a handler's return value, or `SendMessageAsync`). The `AnalysisResult` is also written to workflow state with `QueueStateUpdateAsync`, because the Writer needs it after the pause, and workflow state is part of every checkpoint.

- **Durability.** In Lesson 23 the run is checkpointed to disk with `CheckpointManager.CreateJson(new FileSystemJsonCheckpointStore(...))`. When the graph pauses at the approval port, the process can exit; a fresh process restores the latest checkpoint, sees the same pending request, and resumes the run once the decision arrives.

- **Why the finished graph is hosted over the Responses protocol.** `Workflow.AsAIAgent()` turns the whole graph into one `AIAgent`, and `AddFoundryResponses` / `MapFoundryResponses` (Lesson 21) serve it at `POST /responses`, locally, with no Azure resources. Two properties make it the right surface for this capstone: the approval request travels natively as an approval item a client answers in a follow-up request, and the hosting layer checkpoints the workflow agent's session automatically, so a pending approval survives a host restart.

- **Lesson plan.** Lesson 22 (this one) fixes the contracts and tests the Researcher → Analyst seam. Lesson 23 builds the graph as a console app, answers the approval at the console, and resumes a paused run in a fresh process. Lesson 24 hosts that same graph over the Responses protocol, drives it from an HTTP client, and reviews the series.

- **Reusable pieces this lesson introduces:** `AgentInstructions` (personas for all three roles), `ResearchFinding` / `AnalysisResult` (the contract between steps), `AnalysisParser` with `JsonFenceStripper` (the receiving side of the Analyst's contract), `ReportApprovalRequest` (builds and describes the approval request), and `PipelineConfig` (settings beyond the model provider, including `CAPSTONE_MCP_SERVER_PATH` for Lesson 23's MCP wiring). `ChatClientFactory` and `AgentFactory` are the series' shared helpers.

## Complete C# Code

**`src/CapstoneDesign/CapstoneDesign.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>CapstoneDesign</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.24.0" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.10.1" />
    <PackageReference Include="OpenAI" Version="2.14.0" />
  </ItemGroup>

</Project>
```

**`src/CapstoneDesign/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace CapstoneDesign
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

**`src/CapstoneDesign/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CapstoneDesign
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>. Same shape as Lesson 1's factory; Lesson 23
    /// adds a tools-accepting overload for the Researcher's MCP tools.
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

**`src/CapstoneDesign/AgentInstructions.cs`**

```csharp
namespace CapstoneDesign
{
    /// <summary>
    /// Shared persona names and system instructions for the capstone's three agent roles -
    /// Researcher, Analyst, Writer. Defined once here so the Lesson 23 build and the Lesson 24
    /// deployment reuse the same personas rather than redefining them.
    /// </summary>
    internal static class AgentInstructions
    {
        /// <summary>
        /// Display name for the Researcher role.
        /// </summary>
        internal const string ResearcherName = "ResearcherAgent";

        /// <summary>
        /// Researcher persona: gathers factual notes about a topic. This lesson gives it no
        /// tools - Lesson 23 will connect it to a real MCP server following Lesson 7's pattern, so for
        /// now it draws on the model's own background knowledge, keeping this lesson's seam
        /// test fast and dependency-free.
        /// </summary>
        internal const string ResearcherPersona =
            "You are a meticulous researcher. Given a topic, produce two or three concise, " +
            "factual notes about it, each attributed to a specific, plausible-sounding source. " +
            "Keep each note to one or two sentences.";

        /// <summary>
        /// Display name for the Analyst role.
        /// </summary>
        internal const string AnalystName = "AnalystAgent";

        /// <summary>
        /// Analyst persona: extracts the Researcher's raw notes into strict JSON matching the
        /// <see cref="AnalysisResult"/> shape. This wording (one exact JSON shape, no markdown
        /// fences, no commentary) is the prompt contract AnalysisParser reads.
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

**`src/CapstoneDesign/ResearchFinding.cs`**

```csharp
namespace CapstoneDesign
{
    /// <summary>
    /// One discrete fact the Researcher agent surfaced, as extracted and structured by the
    /// Analyst agent. Part of the shared data contract defined in Lesson 22's design.
    /// </summary>
    internal sealed record ResearchFinding(string Fact, string Source);
}
```

**`src/CapstoneDesign/AnalysisResult.cs`**

```csharp
namespace CapstoneDesign
{
    /// <summary>
    /// The Analyst agent's structured extraction of the Researcher agent's raw notes - the
    /// contract the human-approval request presents for review and the Writer agent consumes once
    /// it is approved. Defined in Lesson 22's design and used unchanged by every capstone lesson.
    /// </summary>
    internal sealed record AnalysisResult(string Topic, string Summary, List<ResearchFinding> KeyFindings);
}
```

**`src/CapstoneDesign/JsonFenceStripper.cs`**

```csharp
namespace CapstoneDesign
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

**`src/CapstoneDesign/AnalysisParser.cs`**

```csharp
using System.Text.Json;

namespace CapstoneDesign
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

**`src/CapstoneDesign/ReportApprovalRequest.cs`**

```csharp
using Microsoft.Extensions.AI;

namespace CapstoneDesign
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

**`src/CapstoneDesign/PipelineConfig.cs`**

```csharp
namespace CapstoneDesign
{
    /// <summary>
    /// Loads the small set of environment-driven settings the capstone pipeline needs beyond
    /// the model provider settings (which ChatClientFactory already owns). Reused by Lessons 23 and 24 -
    /// CAPSTONE_MCP_SERVER_PATH is not read by this lesson's code, but is declared here so the
    /// Researcher's tool wiring in Lesson 23 is composition, not new config plumbing.
    /// </summary>
    internal sealed class PipelineConfig
    {
        /// <summary>
        /// The topic the Researcher agent investigates. Defaults to a fixed demo topic so this
        /// lesson runs unattended.
        /// </summary>
        internal required string ResearchTopic { get; init; }

        /// <summary>
        /// Filesystem path or launch command for the Researcher's MCP server, following Lesson 7's
        /// pattern. Null in this lesson since the Researcher has no tools yet - see
        /// <see cref="AgentInstructions.ResearcherPersona"/>.
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

**`src/CapstoneDesign/Program.cs`**

```csharp
// Lesson 22 - Capstone Design
// Designs the Lesson 22-24 capstone - one graph workflow: Researcher -> Analyst -> human-approval
// request port -> Writer, checkpointed, and later hosted over the Responses protocol - and tests
// its riskiest seam before Lesson 23 builds the graph. The seam test runs the Researcher and the
// Analyst against a live model, parses the Analyst's reply into the AnalysisResult contract, and
// builds the approval request the graph's approval port will carry. It stops there: the pause
// itself and the Writer belong to the graph.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace CapstoneDesign
{
    /// <summary>
    /// Entry point that prints the capstone's graph, then runs the Researcher-to-Analyst seam
    /// test and shows the approval request its result produces.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Prints the design, runs the Researcher and Analyst on the configured topic, parses the
        /// analysis, and prints the approval request built from it.
        /// </summary>
        private static async Task Main(string[] args)
        {
            PipelineConfig config = PipelineConfig.Load();

            Console.WriteLine("=== Capstone graph ===");
            Console.WriteLine("research (Researcher + MCP tools) -> analysis (Analyst -> AnalysisResult)");
            Console.WriteLine("  -> report-approval (request port: ToolApprovalRequestContent / ToolApprovalResponseContent)");
            Console.WriteLine("  -> writer (Writer -> final report)        checkpointed at every step");
            Console.WriteLine($"Research topic: {config.ResearchTopic}");
            Console.WriteLine();

            IChatClient chatClient = ChatClientFactory.Create();
            ChatClientAgent researcher = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.ResearcherName, AgentInstructions.ResearcherPersona);
            ChatClientAgent analyst = AgentFactory.CreateAgent(
                chatClient, AgentInstructions.AnalystName, AgentInstructions.AnalystPersona);

            Console.WriteLine("=== Seam test: Researcher -> Analyst -> AnalysisResult ===");
            AgentResponse notes = await researcher.RunAsync(config.ResearchTopic);
            Console.WriteLine("Researcher notes:");
            Console.WriteLine(notes.Text);
            Console.WriteLine();

            AgentResponse reply = await analyst.RunAsync(
                $"Topic: {config.ResearchTopic}\n\nResearch notes:\n{notes.Text}");
            AnalysisResult analysis = AnalysisParser.Parse(reply.Text);
            Console.WriteLine($"Parsed AnalysisResult with {analysis.KeyFindings.Count} key finding(s).");
            Console.WriteLine();

            ToolApprovalRequestContent request = ReportApprovalRequest.Create(analysis);
            Console.WriteLine("=== What the approval port will carry ===");
            Console.WriteLine($"Action: {ReportApprovalRequest.ActionName}");
            Console.WriteLine(ReportApprovalRequest.Describe(request));
        }
    }
}
```

## Walkthrough

1. **`PipelineConfig.Load()` reads `CAPSTONE_RESEARCH_TOPIC`**, plus `CAPSTONE_MCP_SERVER_PATH`, which this lesson declares but does not read, so Lesson 23 adds MCP wiring without new config plumbing.
2. **`Main` prints the graph first** — the four node ids, the port's request and response types, and the fact that every step is checkpointed — so the run starts from the design it is testing.
3. **Two agents are built through `AgentFactory.CreateAgent`**, differing only in the name and instructions pulled from `AgentInstructions`. The Writer is not needed here: it sits after the approval port, and this test stops before the port.
4. **The Researcher runs on the topic and the Analyst runs on its notes**, with the same prompt shape `AnalysisExecutor` uses in Lesson 23: `Topic: ...` followed by `Research notes:` and the notes.
5. **`AnalysisParser.Parse` is the seam under test.** It strips a code fence if one is present, deserializes case-insensitively into `AnalysisResult`, and throws with the reply quoted if the JSON is missing or malformed — a failure here would surface now, not in the middle of Lesson 23's graph.
6. **`ReportApprovalRequest.Create` builds the request the approval port will carry**: a `ToolApprovalRequestContent` with a fresh request id, wrapping a `FunctionCallContent` named `publish_report` whose arguments are the topic, the summary and the findings as plain strings. `Describe` formats it the way a reviewer will read it in Lesson 23's console and, through the same arguments, in Lesson 24's HTTP client.

## Expected Output

The first five lines are fixed text from `Program.cs`:

```
=== Capstone graph ===
research (Researcher + MCP tools) -> analysis (Analyst -> AnalysisResult)
  -> report-approval (request port: ToolApprovalRequestContent / ToolApprovalResponseContent)
  -> writer (Writer -> final report)        checkpointed at every step
Research topic: the deep sea anglerfish
```

<!-- EXPECTED OUTPUT PENDING: re-run with OPENAI_API_KEY set -->

The rest comes from live model calls. Under `=== Seam test: Researcher -> Analyst -> AnalysisResult ===` the run prints `Researcher notes:` followed by two or three short notes on the topic, each attributed to a source, then `Parsed AnalysisResult with 2 key finding(s).` Under `=== What the approval port will carry ===` it prints `Action: publish_report`, then the reviewer's view of the request: `Topic:`, `Summary:`, and `Key findings:` with one `- fact (source: ...)` line per finding. The wording varies between runs; the shape does not — the Analyst's reply parses into `AnalysisResult` (its instructions ask for exactly two findings), or the run stops with the reply quoted in the error.
