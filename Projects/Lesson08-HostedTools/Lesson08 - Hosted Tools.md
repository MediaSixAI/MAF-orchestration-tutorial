# Lesson 8 — Hosted Tools

## Project Overview

In this lesson, three single-purpose agents each get exactly one kind of tool: a local C# function (`OpsAgent`), OpenAI's hosted web search (`ResearchAgent`), and OpenAI's hosted code execution (`CodeAgent`). The point isn't the tools themselves — it's what's fundamentally different about where each one runs, and what you can and cannot see or control as a result. A fourth agent then holds a local tool and a hosted tool at once, and every demo prints the tool activity recorded in its response, so the output proves which tools actually ran instead of asking you to trust the answer text.

## Prerequisites

Lesson 2 — this lesson assumes familiarity with local function tools (`AITool`, `AIFunctionFactory.Create`, the function-calling loop). This lesson is otherwise standalone: it does not reference any prior project's code.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter — here over the OpenAI SDK's `ResponsesClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

`HostedWebSearchTool` and `HostedCodeInterpreterTool` — the provider-agnostic hosted-tool markers this lesson uses — live in `Microsoft.Extensions.AI`, already a transitive dependency of `Microsoft.Agents.AI`. So do `WebSearchToolCallContent`, `CodeInterpreterToolCallContent` and `CitationAnnotation`, the content types `ToolActivityReporter` reads to show what each hosted tool did.

**Environment variables:**

```powershell
$env:MAF_LLM = "OPENAI"
$env:MAF_MODEL = "gpt-4.1-mini"
$env:OPENAI_API_KEY = "your-openai-api-key"
```

All three are required, as in every lesson: `MAF_LLM` must be `OPENAI` here, and `MAF_MODEL` names the model — the series records its demos with `gpt-4.1-mini`, and there is no built-in default. Get a key at [OpenAI's API keys page](https://platform.openai.com/api-keys), and never hardcode it.

**⚠️ This lesson is OpenAI-only, and uses the Responses API.** Every other lesson builds its `IChatClient` with `ChatClientFactory`, which talks to OpenAI's Chat Completions API and can switch provider through `MAF_LLM`. Hosted tools don't fit that shape. OpenAI runs web search and code execution through its **Responses API** — on Chat Completions, `gpt-4.1-mini` can't use web search and code execution isn't offered at all — and the other providers' OpenAI-compatible endpoints don't run them either. So this lesson replaces `ChatClientFactory` with `OpenAIResponsesClientFactory`, which builds the client with `GetResponsesClient().AsIChatClient(model)` and refuses to start unless `MAF_LLM` is `OPENAI`.

**`OPENAI001`.** OpenAI marks the Responses API (`GetResponsesClient()` and the `AsIChatClient` overload that wraps it) as experimental, so using it is a compile error until you opt in. `OpenAIResponsesClientFactory` opts in by suppressing `OPENAI001` around those two calls only, with a matching `#pragma warning restore`.

**⚠️ Hosted tools have their own costs and limits.** Provider-side tool calls such as web search and code execution are billed separately from ordinary chat tokens. If `ResearchAgent` fails, or answers that it is unable to search, check usage and limits on your OpenAI account before assuming anything in the code is broken.

## Core Concepts

- **A local function tool's implementation lives in your process.** Lesson 2's `AIFunctionFactory.Create(...)` pattern — reused here as `OpsTools.GetOnCallEngineer` — means the model asks your process to run a specific method, your process runs it, and your process hands the result back. Every step happens on hardware you control.
- **A hosted tool's implementation lives on the provider's infrastructure — you write zero code for it.** `Microsoft.Extensions.AI.HostedWebSearchTool` and `HostedCodeInterpreterTool` aren't implementations of anything; they're markers. Attaching one to an agent tells the underlying `IChatClient` "you're allowed to use your own built-in capability for this," and OpenAI does the actual searching or code-running entirely on its own servers. There's no C# method anywhere in this project that performs a web search or executes Python.
- **These markers are provider-agnostic, but support is not.** `HostedWebSearchTool()` doesn't name OpenAI anywhere; the `IChatClient` adapter translates the generic marker into its provider's own built-in capability. Over the Responses API, `Microsoft.Extensions.AI.OpenAI` sends `web_search` and `code_interpreter` tool entries. Over Chat Completions, the same markers are translated differently or dropped — which is why the API you build the client on matters as much as the provider.
- **You see what a hosted tool did only through what the provider reports back.** A local tool shows up in the response as a `FunctionCallContent` your own process answered. A hosted tool runs out of sight; the adapter surfaces it as `WebSearchToolCallContent` or `CodeInterpreterToolCallContent`, and web results as `CitationAnnotation`s on the answer text. An answer alone proves nothing — the model could have answered from memory — so `ToolActivityReporter` prints that evidence after every run.
- **Hosted tools never reach Lesson 6's function-calling middleware.** `FunctionCallLoggingMiddleware` from Lesson 6 only fires for `AIFunction`-typed tools flowing through `FunctionInvokingChatClient`'s local invocation loop. Hosted tools are plain `AITool` (not `AIFunction`) and never touch that loop — there's no local invocation to intercept, log, or gate with an approval check (Lesson 9).
- **Local and hosted tools can share one agent.** On OpenAI's Responses API, a single request can carry both function declarations and built-in tools, and the model can use both in one run — `MixedAgent` does exactly that. Not every provider allows the combination, so check before you design around it.
- **When to reach for each.** Prefer a **local tool** when the capability is your own (internal APIs, your data), when you need to log, gate or approve each call, or when the work must stay inside your boundary. Prefer a **hosted tool** when the capability is generic and the provider already runs it well (live web search, sandboxed code execution), and you accept that it runs on the provider's infrastructure, is billed by the provider, and is visible to you only through what the provider reports.

## Complete C# Code

**`src/HostedTools/OpenAIResponsesClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace HostedTools
{
    /// <summary>
    /// Builds the <see cref="IChatClient"/> for this lesson over OpenAI's Responses API, wrapped by
    /// the Microsoft.Extensions.AI.OpenAI package's AsIChatClient() adapter. Every other lesson uses
    /// ChatClientFactory, which talks to the Chat Completions API and can switch provider via
    /// MAF_LLM. Hosted tools are different: web search and code execution are run by OpenAI itself,
    /// and the Responses API is where OpenAI runs them, so this lesson is OpenAI-only.
    /// </summary>
    internal static class OpenAIResponsesClientFactory
    {
        /// <summary>
        /// Creates the Responses API chat client for the model named by MAF_MODEL. Throws if MAF_LLM,
        /// MAF_MODEL or OPENAI_API_KEY is missing or empty, or if MAF_LLM is not OPENAI.
        /// </summary>
        internal static IChatClient Create()
        {
            Func<string, string> requiredVariable = name =>
                Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
                    ? value
                    : throw new InvalidOperationException($"{name} is not set. Set it before running this lesson.");

            string provider = requiredVariable("MAF_LLM");
            if (!provider.Equals("OPENAI", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"MAF_LLM is '{provider}', but hosted tools run only on OpenAI's Responses API. " +
                    "Set MAF_LLM to OPENAI for this lesson.");
            }

            string modelId = requiredVariable("MAF_MODEL");
            string apiKey = requiredVariable("OPENAI_API_KEY");

            // The Responses client and its IChatClient adapter are experimental OpenAI APIs; this opts in.
#pragma warning disable OPENAI001
            return new OpenAIClient(new ApiKeyCredential(apiKey))
                .GetResponsesClient()
                .AsIChatClient(modelId);
#pragma warning restore OPENAI001
        }
    }
}
```

**`src/HostedTools/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedTools
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>, optionally wiring in tools - whether
    /// local function tools, hosted tools, or a mix. This factory shape is reintroduced across
    /// lessons so that assembling the Lesson 22-24 capstone is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name, system
        /// instructions that define its persona, and an optional set of tools the agent may use
        /// while responding.
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

**`src/HostedTools/OpsTools.cs`**

```csharp
using System.ComponentModel;

namespace HostedTools
{
    /// <summary>
    /// A local C# function tool exposed to the agent, used in this lesson to contrast with the
    /// hosted tools below - this one's entire implementation lives in this process.
    /// </summary>
    internal static class OpsTools
    {
        private static readonly Dictionary<string, string> OnCallByTeam = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Platform"] = "Jordan Lee",
            ["Payments"] = "Priya Patel",
        };

        /// <summary>
        /// Looks up the current on-call engineer for a known team. Unknown teams produce a
        /// clear error string instead of a guess.
        /// </summary>
        /// <param name="team">The team name to look up, e.g. 'Platform' or 'Payments'.</param>
        [Description("Gets the name of the current on-call engineer for a given team.")]
        internal static string GetOnCallEngineer(
            [Description("The team name, e.g. 'Platform' or 'Payments'.")] string team)
        {
            if (!OnCallByTeam.TryGetValue(team, out string? engineer))
            {
                return $"No on-call data is available for the '{team}' team.";
            }

            return engineer;
        }
    }
}
```

**`src/HostedTools/ToolActivityReporter.cs`**

```csharp
using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedTools
{
    /// <summary>
    /// Prints the evidence of tool use that an agent run leaves in its response messages: local
    /// function calls, hosted web searches, hosted code executions and the sources a web search
    /// cited. An answer alone cannot prove a tool ran - the model could have answered from memory -
    /// so every demo in this lesson prints this report under the answer.
    /// </summary>
    internal static class ToolActivityReporter
    {
        /// <summary>
        /// Writes one line per tool call and per cited source found in the response, or a single
        /// line saying no tool was used.
        /// </summary>
        internal static void Print(AgentResponse response)
        {
            int found = 0;
            foreach (AIContent content in response.Messages.SelectMany(message => message.Contents))
            {
                string? activity = content switch
                {
                    FunctionCallContent call => $"local function call: {call.Name}",
                    WebSearchToolCallContent search => $"hosted web search: {string.Join(", ", search.Queries ?? [])}",
                    CodeInterpreterToolCallContent code => $"hosted code execution:{Environment.NewLine}{DescribeInputs(code.Inputs)}",
                    _ => null,
                };

                if (activity is not null)
                {
                    Console.WriteLine($"  [tool] {activity}");
                    found++;
                }

                foreach (CitationAnnotation citation in content.Annotations?.OfType<CitationAnnotation>() ?? [])
                {
                    Console.WriteLine($"  [source] {citation.Title} - {citation.Url}");
                    found++;
                }
            }

            if (found == 0)
            {
                Console.WriteLine("  [tool] none - the model answered on its own");
            }
        }

        /// <summary>
        /// Renders the code a hosted code execution ran, which arrives as text or as a text-typed
        /// data payload depending on the provider.
        /// </summary>
        private static string DescribeInputs(IList<AIContent>? inputs)
        {
            IEnumerable<string> parts = (inputs ?? []).Select(input => input switch
            {
                TextContent text => text.Text,
                DataContent data when data.MediaType.StartsWith("text/", StringComparison.Ordinal)
                    => Encoding.UTF8.GetString(data.Data.Span),
                _ => $"({input.GetType().Name})",
            });

            return string.Join(Environment.NewLine, parts);
        }
    }
}
```

**`src/HostedTools/Program.cs`**

```csharp
// Lesson 8 - Hosted Tools
// Contrasts a local C# function tool (this process does all the work) with two of OpenAI's
// hosted tools - web search and code execution (OpenAI's infrastructure does all the work,
// with no local implementation at all) - then puts a local tool and a hosted tool on the same
// agent. Every demo prints the tool activity found in the response, so the output proves which
// tools actually ran rather than trusting the answer text.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedTools
{
    /// <summary>
    /// Entry point that runs three single-purpose agents - local function tool, hosted web
    /// search, hosted code execution - then a fourth agent that combines a local tool with a
    /// hosted tool, printing the evidence of tool use after each answer.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds and runs each agent in turn against one Responses API client, printing each
        /// answer followed by the tool calls and cited sources found in its response.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = OpenAIResponsesClientFactory.Create();

            IList<AITool> localOnlyTools = [AIFunctionFactory.Create(OpsTools.GetOnCallEngineer)];

            ChatClientAgent opsAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "OpsAgent",
                instructions: "You are an on-call ops assistant. Use the on-call lookup tool " +
                    "whenever asked who is on call - never guess a name.",
                tools: localOnlyTools);

            Console.WriteLine("=== Local function tool (runs in this process) ===");
            AgentResponse opsResponse = await opsAgent.RunAsync("Who is currently on call for the Payments team?");
            Console.WriteLine(opsResponse.Text);
            ToolActivityReporter.Print(opsResponse);

            IList<AITool> webSearchOnlyTools = [new HostedWebSearchTool()];

            ChatClientAgent researchAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "ResearchAgent",
                instructions: "You are a research assistant. Use web search for anything " +
                    "requiring current, real-world information.",
                tools: webSearchOnlyTools);

            Console.WriteLine();
            Console.WriteLine("=== Hosted web search tool (runs on OpenAI's infrastructure) ===");
            AgentResponse researchResponse = await researchAgent.RunAsync(
                "What is one real, current news headline about .NET from the past week? Cite what you found.");
            Console.WriteLine(researchResponse.Text);
            ToolActivityReporter.Print(researchResponse);

            IList<AITool> codeInterpreterOnlyTools = [new HostedCodeInterpreterTool()];

            ChatClientAgent codeAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "CodeAgent",
                instructions: "You are a math assistant. Use the code execution tool to " +
                    "actually run code for any calculation - never compute it in your head.",
                tools: codeInterpreterOnlyTools);

            Console.WriteLine();
            Console.WriteLine("=== Hosted code execution tool (runs on OpenAI's infrastructure) ===");
            AgentResponse codeResponse = await codeAgent.RunAsync(
                "Compute the 15th Fibonacci number by actually running code.");
            Console.WriteLine(codeResponse.Text);
            ToolActivityReporter.Print(codeResponse);

            IList<AITool> mixedTools = [AIFunctionFactory.Create(OpsTools.GetOnCallEngineer), new HostedWebSearchTool()];

            ChatClientAgent mixedAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "MixedAgent",
                instructions: "You are an incident assistant. Use the on-call lookup tool for who is " +
                    "on call, and web search for anything requiring current, real-world information.",
                tools: mixedTools);

            Console.WriteLine();
            Console.WriteLine("=== A local tool and a hosted tool on one agent ===");
            AgentResponse mixedResponse = await mixedAgent.RunAsync(
                "Who is on call for the Platform team, and is there any current public news of an " +
                "Azure outage they should know about?");
            Console.WriteLine(mixedResponse.Text);
            ToolActivityReporter.Print(mixedResponse);
        }
    }
}
```

## Walkthrough

1. **`OpenAIResponsesClientFactory.Create()`** reads `MAF_LLM`, `MAF_MODEL` and `OPENAI_API_KEY` through the same `requiredVariable` delegate `ChatClientFactory` uses, so a missing one stops the run with a message naming it. It refuses any `MAF_LLM` other than `OPENAI`, then returns `new OpenAIClient(...).GetResponsesClient().AsIChatClient(model)`. All four agents share this one client.
2. **`OpsAgent` is a plain Lesson 2-style agent.** `AIFunctionFactory.Create(OpsTools.GetOnCallEngineer)` produces an `AIFunction`, wired in through the same `AgentFactory.CreateAgent` used across the series. Its report line is `local function call: GetOnCallEngineer` — the call your own process answered.
3. **`ResearchAgent` gets a marker, not an implementation.** `new HostedWebSearchTool()` is added to its tools list exactly like a function tool would be, but it carries no method to call. OpenAI performs the search; the answer comes back with `CitationAnnotation`s, which the report prints as `[source]` lines — the proof that the headline came from a search rather than from the model's training data.
4. **`CodeAgent` follows the identical shape with `HostedCodeInterpreterTool()`.** OpenAI runs the code in a sandbox container it manages. The report prints the code it ran from the `CodeInterpreterToolCallContent`, so you can see the calculation was executed, not narrated.
5. **`MixedAgent` holds both kinds of tool.** Its tools list combines `AIFunctionFactory.Create(OpsTools.GetOnCallEngineer)` with `new HostedWebSearchTool()`, and the prompt needs both: who is on call (local data) and whether there is current outage news (live web). Its report shows the local function call and the web search side by side.
6. **`ToolActivityReporter.Print`** walks every `AIContent` in the response's messages, matching `FunctionCallContent`, `WebSearchToolCallContent` and `CodeInterpreterToolCallContent`, and reading each content's `Annotations` for citations. If it finds nothing it says the model answered on its own.
7. **The `ChatClientAgent`/`AgentFactory` code never changes shape.** Every agent is built through the same `AgentFactory.CreateAgent(chatClient, name, instructions, tools)` call. The only thing that differs between "local," "hosted," and "mixed" is which kinds of `AITool` end up in the `tools` list.

## Expected Output

```
=== Local function tool (runs in this process) ===
Priya Patel is currently on call for the Payments team.
  [tool] local function call: GetOnCallEngineer

=== Hosted web search tool (runs on OpenAI's infrastructure) ===
[A current .NET headline from the past week, with a short summary and its source.]
  [source] [Article title] - [https://...]

=== Hosted code execution tool (runs on OpenAI's infrastructure) ===
The 15th Fibonacci number is 610.
  [tool] hosted code execution:
[The Python the model wrote and ran, e.g. a loop computing the sequence.]

=== A local tool and a hosted tool on one agent ===
[Jordan Lee is on call for the Platform team, plus a summary of any current Azure outage news.]
  [tool] local function call: GetOnCallEngineer
  [source] [Article title] - [https://...]
```

Bracketed lines are placeholders: headlines and sources change daily, and the generated code varies between runs. What to look for is the evidence under each answer — a `local function call` line for the local tool, `[source]` lines for web search, the executed code for code execution, and both kinds together for `MixedAgent`. Depending on what the provider reports for a run, a web search may also show a `hosted web search:` line with the queries it ran. If a hosted demo prints `[tool] none - the model answered on its own`, the model chose not to use the tool for that prompt; the answer above it is then the model's own, not a tool result.
