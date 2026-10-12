# Lesson 9 — Approvals (Function Invocation Gating)

## Project Overview

In this lesson, a `WeatherAgent`'s only tool is wrapped so that every call to it pauses and waits for approval before it actually runs, instead of executing automatically the moment the model decides to call it. This is the first governance layer in the series: a structured pause-and-resume mechanism the caller must satisfy before a tool's side effects happen, and the direct foundation for the richer multi-step Human-in-the-Loop pattern in Lesson 17.

## Prerequisites

Lesson 2 — this lesson assumes familiarity with local function tools (`AIFunctionFactory.Create`, the function-calling loop). Lesson 4's `AgentSession` is also used here, since the approve/reject exchange spans two `RunAsync` calls that must share state. This lesson is otherwise standalone: it does not reference any prior project's code.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AgentSession`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No new packages this lesson. The approval types below live in `Microsoft.Extensions.AI`, already a transitive dependency of `Microsoft.Agents.AI`.

**The approval API.** `ApprovalRequiredAIFunction` wraps a tool so it needs approval before it runs. A pending call comes back as **`ToolApprovalRequestContent`**, whose **`.ToolCall`** property (typed as the general `ToolCallContent`, since approval applies to any tool, not only functions) describes the proposed call. You answer it with a **`ToolApprovalResponseContent`**, created by `CreateResponse(approved, reason)`.

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

- **Gating is applied to the tool, not the agent.** `new ApprovalRequiredAIFunction(weatherFunction)` wraps an ordinary `AIFunction` — the same kind Lesson 2 built with `AIFunctionFactory.Create` — in a decorator that intercepts invocation. `WeatherTools.GetWeather` itself has no idea it's gated; nothing about its implementation changes.
- **An approval-pending run doesn't fail — it pauses.** When the model decides to call a gated tool, `RunAsync` returns normally, but its `AgentResponse` contains a `ToolApprovalRequestContent` instead of a final answer and instead of the tool having actually executed. Checking for this content type after every run is how your code tells "the agent is done" apart from "the agent is waiting on you."
- **`ToolApprovalRequestContent.ToolCall` carries the specific proposed call.** Its runtime type is `FunctionCallContent` (a subtype of the more general `ToolCallContent`) for a function tool specifically, giving you `.Name` and `.Arguments` — exactly what a human reviewer, or an automated policy, needs to decide whether the call should proceed.
- **Resolving the request means building a response and continuing the same session.** `requestContent.CreateResponse(approved, reason)` produces a `ToolApprovalResponseContent`, which gets wrapped in a new `ChatMessage(ChatRole.User, [...])` and passed into another `RunAsync` call — critically, on the **same `AgentSession`** the original request came from, so the agent has the context to actually resume rather than starting over. The session also records every approval request it hands out, and an approval response only counts on the session that recorded the matching request: a response sent on a different session is ignored and the tool never runs.
- **Rejection doesn't crash anything — the model just finds out its tool call didn't happen.** This lesson's second scenario rejects the call outright, and the agent responds with a plain-language apology instead of weather data. The rejection is ordinary conversational context to the model, not an exception your code has to handle specially.
- **This is a foundation, not the finished feature.** This lesson hand-rolls the simplest possible loop: one gated tool, one pending request, one decision. MAF also ships `AIAgentBuilder.UseToolApproval(...)` with `ToolApprovalAgentOptions.AutoApprovalRules`, which layers "don't ask again for this tool" / "don't ask again with these exact arguments" policies on top of the same underlying request/response primitive shown here — useful once you have more than one gated tool or want to avoid re-prompting for identical, already-trusted calls. Lesson 17 moves the pause into a workflow graph with a request port, and the capstone (Lessons 22–24) carries this same `ToolApprovalRequestContent`/`ToolApprovalResponseContent` shape through its approval port.

## Complete C# Code

**`src/Approvals/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Approvals
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

**`src/Approvals/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Approvals
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>, optionally wiring in tools. This
    /// factory shape is reintroduced across lessons so that assembling the Lesson 22-24 capstone
    /// is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name, system
        /// instructions that define its persona, and an optional set of tools the agent may
        /// use while responding.
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

**`src/Approvals/WeatherTools.cs`**

```csharp
using System.ComponentModel;

namespace Approvals
{
    /// <summary>
    /// A local C# function tool exposed to the agent. This lesson wraps it in an
    /// <see cref="Microsoft.Extensions.AI.ApprovalRequiredAIFunction"/> so every call to it
    /// pauses for approval before it runs - the tool itself has no idea it is gated.
    /// </summary>
    internal static class WeatherTools
    {
        private static readonly Dictionary<string, string> ConditionsByCity = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Amsterdam"] = "cloudy with a high of 15C",
            ["Tokyo"] = "clear with a high of 28C",
        };

        /// <summary>
        /// Gets a fixed, fake weather condition for a known city. Unknown cities produce a
        /// clear error string instead of a guess.
        /// </summary>
        /// <param name="location">The city to get the weather for.</param>
        [Description("Gets the weather for a given location.")]
        internal static string GetWeather(
            [Description("The location to get the weather for.")] string location)
        {
            if (!ConditionsByCity.TryGetValue(location, out string? condition))
            {
                return $"No weather data is available for '{location}'.";
            }

            return $"The weather in {location} is {condition}.";
        }
    }
}
```

**`src/Approvals/Program.cs`**

```csharp
// Lesson 9 - Approvals (Function Invocation Gating)
// Wraps a local weather tool in ApprovalRequiredAIFunction so the agent must pause and wait for
// approval before the tool actually runs. Demonstrates both outcomes on separate sessions: a
// human (or a policy) approving the call, and one rejecting it - the first governance layer in
// the series, and the direct setup for the multi-step Human-in-the-Loop pattern in Lesson 17.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Approvals
{
    /// <summary>
    /// Entry point that runs the same approval-gated weather tool through both an approval and
    /// a rejection, each on its own session.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Builds a weather agent whose only tool requires approval, then walks through the
        /// approve path and the reject path in turn.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            AIFunction weatherFunction = AIFunctionFactory.Create(WeatherTools.GetWeather);
            AIFunction approvalRequiredWeatherFunction = new ApprovalRequiredAIFunction(weatherFunction);

            ChatClientAgent weatherAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "WeatherAgent",
                instructions: "You are a helpful weather assistant. Use the weather tool for any " +
                    "question about current conditions - never guess.",
                tools: [approvalRequiredWeatherFunction]);

            Console.WriteLine("=== Scenario 1: approving the tool call ===");
            await RunApprovalScenarioAsync(weatherAgent, "What is the weather like in Amsterdam?", approve: true);

            Console.WriteLine();
            Console.WriteLine("=== Scenario 2: rejecting the tool call ===");
            await RunApprovalScenarioAsync(weatherAgent, "What is the weather like in Tokyo?", approve: false);
        }

        /// <summary>
        /// Runs a prompt on a fresh session, surfaces any pending tool approval request exactly
        /// as a human reviewer would see it, then resolves it with the given decision and prints
        /// the agent's final response.
        /// </summary>
        private static async Task RunApprovalScenarioAsync(ChatClientAgent agent, string prompt, bool approve)
        {
            AgentSession session = await agent.CreateSessionAsync();
            AgentResponse response = await agent.RunAsync(prompt, session);

            List<ToolApprovalRequestContent> approvalRequests = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();

            if (approvalRequests.Count == 0)
            {
                Console.WriteLine("No approval was requested - unexpected for this lesson.");
                Console.WriteLine(response.Text);
                return;
            }

            ToolApprovalRequestContent requestContent = approvalRequests[0];
            if (requestContent.ToolCall is FunctionCallContent functionCall)
            {
                string arguments = string.Join(", ", functionCall.Arguments?.Select(kv => $"{kv.Key}={kv.Value}") ?? []);
                Console.WriteLine($"Approval requested to call '{functionCall.Name}' with arguments: {arguments}");
            }

            Console.WriteLine(approve ? "Decision: APPROVED" : "Decision: REJECTED");

            ToolApprovalResponseContent approvalResponse = requestContent.CreateResponse(
                approve, reason: approve ? "Approved by reviewer." : "Rejected by reviewer.");
            ChatMessage approvalMessage = new(ChatRole.User, [approvalResponse]);

            AgentResponse finalResponse = await agent.RunAsync(approvalMessage, session);
            Console.WriteLine(finalResponse.Text);
        }
    }
}
```

## Walkthrough

1. **The tool is built exactly like any Lesson 2 tool, then wrapped once.** `AIFunctionFactory.Create(WeatherTools.GetWeather)` produces a plain `AIFunction`; `new ApprovalRequiredAIFunction(weatherFunction)` decorates it. Only the wrapped version goes into the agent's `tools` list — the agent never sees the unwrapped function at all.
2. **The first `RunAsync` call ends in a pause, not an answer.** `RunApprovalScenarioAsync` calls `agent.RunAsync(prompt, session)` and immediately checks `response.Messages` for any `ToolApprovalRequestContent`. For this lesson's prompts, there always is one — the model wants to call `GetWeather`, but the wrapper stops it before it runs.
3. **The pending call's details come from casting `.ToolCall`.** `requestContent.ToolCall` is typed as the general `ToolCallContent`, but for a function tool it's actually a `FunctionCallContent` underneath, so the pattern-matching `is FunctionCallContent functionCall` cast unlocks `.Name` and `.Arguments` — exactly what gets printed as the "approval requested" line, standing in for what a real reviewer would see in a UI.
4. **The decision is simulated with a plain `bool` parameter, not a UI.** In a real application, `approve` would come from an interactive prompt, a review queue, or a programmatic policy function — the mechanics of *getting* the decision are outside MAF's concern. This lesson hardcodes `true` for Amsterdam and `false` for Tokyo specifically so both outcomes are visible in one run.
5. **`CreateResponse` and the second `RunAsync` call are what actually resume the agent.** `requestContent.CreateResponse(approve, reason: ...)` builds the typed response; wrapping it in a `ChatMessage(ChatRole.User, [...])` and calling `agent.RunAsync(approvalMessage, session)` on the *same* session is what lets the agent pick back up with full context — either running the now-approved tool and answering, or acknowledging the rejection and explaining it couldn't get the data.
6. **Two independent sessions keep the two scenarios from interfering.** `AgentSession session = await agent.CreateSessionAsync();` is called fresh inside `RunApprovalScenarioAsync` for both the approval and the rejection scenario, so Tokyo's rejected call has no leftover context from Amsterdam's approved one.

## Expected Output

```
=== Scenario 1: approving the tool call ===
Approval requested to call 'GetWeather' with arguments: location=Amsterdam
Decision: APPROVED
The weather in Amsterdam is cloudy with a high of 15C.

=== Scenario 2: rejecting the tool call ===
Approval requested to call 'GetWeather' with arguments: location=Tokyo
Decision: REJECTED
I'm sorry, I cannot fulfill this request. My tool for getting the weather is not working at the moment. Please try again later.
```

The model's exact rejection wording may vary slightly between runs — it's the model choosing its own phrasing for "I couldn't use my tool," not a fixed string this project outputs — but the shape (a printed approval request, an explicit decision, then either real weather data or a graceful non-answer) will not.
