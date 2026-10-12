# Lesson 6 — Middleware

## Project Overview

In this lesson a `SupportAgent` gets wrapped in four layers of middleware — a chat-client-level request logger, an agent-run guardrail that can block a request before the agent ever sees it, an agent-run logger, and a function-calling logger — without changing a single line of the agent's own instructions or tools. This is how you add cross-cutting behavior (logging, guardrails, request shaping) to any agent in MAF: as decorators wrapped around the pipeline, not as code baked into the agent itself.

## Prerequisites

Lessons 1–2 — this lesson assumes familiarity with `IChatClient`, `ChatClientAgent`, `RunAsync`, and function tools (`AIFunctionFactory.Create`, the `[Description]`-driven tool schema). This lesson is otherwise standalone: it does not reference any prior project.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AIAgentBuilder`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No new packages are required for middleware. `AIAgentBuilder` and the chat-client `.Use()` builder both ship in the core `Microsoft.Agents.AI` / `Microsoft.Extensions.AI` packages already referenced.

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

- **Three distinct middleware types.** MAF's agent pipeline can be customized at three separate layers, and this lesson exercises all three: **agent-run middleware** (intercepts every call to `RunAsync`/`RunStreamingAsync`), **function-calling middleware** (intercepts each individual tool invocation inside the agent's function-calling loop), and **`IChatClient` middleware** (intercepts each raw call to the model, below the agent layer entirely).
- **Middleware is a decorator, not a modification.** Every middleware type is added via a builder pattern — `agent.AsBuilder().Use(...).Build()` for agent-run and function-calling middleware, `chatClient.AsBuilder().Use(...).Build()` for chat-client middleware — that wraps the original object in a new one implementing the same interface (`AIAgent` or `IChatClient`). The agent's own instructions, tools, and persona are never touched.
- **Each middleware is a function, not necessarily a class.** All three types are implemented as callback delegates that receive the request, an `innerAgent`/`innerChatClient`/`next` continuation representing "the rest of the pipeline," and a cancellation token. A middleware function decides whether, when, and how to call that continuation — logging around it, transforming its input or output, or in a guardrail's case, never calling it at all.
- **Different layers fire at different frequencies.** This is the single most important thing to build correct intuition for: agent-run middleware fires exactly **once** per `RunAsync` call. `IChatClient` middleware fires **once per model call** — and a single `RunAsync` call that involves a tool invocation makes at least two model calls (one that requests the tool, one that consumes the tool's result). Function-calling middleware fires once per tool invocated. This lesson's live output shows this directly: the run logger logs once, but the chat-client logger logs twice around a single tool call.
- **Guardrails work by not calling `next`.** An agent-run middleware that detects a policy violation can simply return its own `AgentResponse` without ever calling `innerAgent.RunAsync(...)`. Nothing downstream — not the agent, not its tools, not the model — ever runs. This is the mechanism this lesson's `ContentGuardrailMiddleware` uses to block a request outright.
- **Registration order determines wrapping order.** `.Use(A).Use(B).Use(C).Build()` produces a pipeline where `A` is outermost (runs first, can short-circuit before `B` or `C` ever execute), `B` wraps `C`, and `C` wraps the original agent. This lesson registers the guardrail first specifically so it can veto a request before the run logger even logs that a run started.

## Complete C# Code

**`src/Middleware/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace Middleware
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

**`src/Middleware/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
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

**`src/Middleware/OrderTools.cs`**

```csharp
using System.ComponentModel;

namespace Middleware
{
    /// <summary>
    /// A single local C# function tool exposed to the agent. It exists in this lesson to give
    /// the function-calling middleware something real to intercept - the middleware itself is
    /// the point of the lesson, not this lookup logic.
    /// </summary>
    internal static class OrderTools
    {
        private static readonly Dictionary<string, string> OrderStatusesById = new(StringComparer.OrdinalIgnoreCase)
        {
            ["A1001"] = "Shipped, arriving in 2 days.",
            ["A1002"] = "Processing, has not yet shipped.",
            ["A1003"] = "Delivered.",
        };

        /// <summary>
        /// Looks up the shipping status for a known order ID. Unknown order IDs produce a clear
        /// error string instead of a guess.
        /// </summary>
        /// <param name="orderId">The order ID to look up, e.g. 'A1001'.</param>
        [Description("Gets the current shipping status for a given order ID.")]
        internal static string GetOrderStatus(
            [Description("The order ID to look up, e.g. 'A1001'.")] string orderId)
        {
            if (!OrderStatusesById.TryGetValue(orderId, out string? status))
            {
                return $"No order found with ID '{orderId}'.";
            }

            return status;
        }
    }
}
```

**`src/Middleware/ChatRequestLoggingMiddleware.cs`**

```csharp
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// IChatClient-level middleware that logs every raw request and response exchanged with the
    /// underlying model. Because it sits below the agent's function-invocation loop, this
    /// middleware fires once per model call - which can be more than once per RunAsync call
    /// whenever the agent works through a multi-step tool-calling turn.
    /// </summary>
    internal static class ChatRequestLoggingMiddleware
    {
        /// <summary>
        /// Logs the outgoing message count before forwarding the call to the inner chat client,
        /// then logs the returned message count before handing the response back up the chain.
        /// </summary>
        internal static async Task<ChatResponse> LogChatRequestAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options,
            IChatClient innerChatClient,
            CancellationToken cancellationToken)
        {
            Console.WriteLine($"  [ChatClient] -> sending {messages.Count()} message(s) to the model.");
            ChatResponse response = await innerChatClient.GetResponseAsync(messages, options, cancellationToken);
            Console.WriteLine($"  [ChatClient] <- received {response.Messages.Count} message(s) from the model.");
            return response;
        }
    }
}
```

**`src/Middleware/ContentGuardrailMiddleware.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// Agent-run middleware that inspects the newest user message before it ever reaches the
    /// agent and blocks the run outright if it looks like it is asking for sensitive
    /// information such as a password or secret. This is the "guardrail" middleware type: unlike
    /// logging middleware, it can refuse to call the next stage in the pipeline at all.
    /// </summary>
    internal static class ContentGuardrailMiddleware
    {
        private static readonly string[] BlockedTerms = ["password", "secret", "api key", "api_key"];

        /// <summary>
        /// Short-circuits the pipeline with a fixed refusal response when the latest user
        /// message contains a blocked term; otherwise forwards the call unchanged to the next
        /// agent in the pipeline, which may be more middleware or the underlying agent itself.
        /// </summary>
        internal static async Task<AgentResponse> EnforceGuardrailAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            AIAgent innerAgent,
            CancellationToken cancellationToken)
        {
            ChatMessage? lastUserMessage = messages.LastOrDefault(m => m.Role == ChatRole.User);
            string text = lastUserMessage?.Text ?? string.Empty;

            if (BlockedTerms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase)))
            {
                Console.WriteLine("  [Guardrail] Blocked request - detected a sensitive term in the user message.");
                return new AgentResponse(new ChatMessage(
                    ChatRole.Assistant,
                    "I can't help with requests that involve passwords, secrets, or API keys."));
            }

            Console.WriteLine("  [Guardrail] Passed - no blocked terms detected.");
            return await innerAgent.RunAsync(messages, session, options, cancellationToken);
        }
    }
}
```

**`src/Middleware/AgentRunLoggingMiddleware.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// Agent-run middleware that logs the size of the request going into a run and the size of
    /// the response coming out of it. Unlike <see cref="ChatRequestLoggingMiddleware"/>, this
    /// fires exactly once per RunAsync call, regardless of how many internal model calls the
    /// agent makes while resolving tool calls along the way.
    /// </summary>
    internal static class AgentRunLoggingMiddleware
    {
        /// <summary>
        /// Logs the inbound message count, forwards the call to the next agent in the pipeline,
        /// then logs the outbound message count before returning the response unchanged.
        /// </summary>
        internal static async Task<AgentResponse> LogRunAsync(
            IEnumerable<ChatMessage> messages,
            AgentSession? session,
            AgentRunOptions? options,
            AIAgent innerAgent,
            CancellationToken cancellationToken)
        {
            Console.WriteLine($"  [Run] -> starting run with {messages.Count()} input message(s).");
            AgentResponse response = await innerAgent.RunAsync(messages, session, options, cancellationToken);
            Console.WriteLine($"  [Run] <- run completed with {response.Messages.Count} output message(s).");
            return response;
        }
    }
}
```

**`src/Middleware/FunctionCallLoggingMiddleware.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// Function-calling middleware that logs the name of every tool the agent decides to
    /// invoke, along with the result it gets back. This sits inside the agent's function
    /// invocation loop, so it only fires when a tool call actually happens.
    /// </summary>
    internal static class FunctionCallLoggingMiddleware
    {
        /// <summary>
        /// Logs the function name before invoking it via <paramref name="next"/>, then logs the
        /// result before returning it back up the chain.
        /// </summary>
        internal static async ValueTask<object?> LogFunctionCallAsync(
            AIAgent agent,
            FunctionInvocationContext context,
            Func<FunctionInvocationContext, CancellationToken, ValueTask<object?>> next,
            CancellationToken cancellationToken)
        {
            Console.WriteLine($"  [Tool] -> calling '{context.Function.Name}'.");
            object? result = await next(context, cancellationToken);
            Console.WriteLine($"  [Tool] <- '{context.Function.Name}' returned: {result}");
            return result;
        }
    }
}
```

**`src/Middleware/Program.cs`**

```csharp
// Lesson 6 - Middleware
// Wraps a SupportAgent with three distinct layers of middleware: an IChatClient-level logger
// that fires on every raw model call, an agent-run guardrail that can block a request outright
// before it ever reaches the agent, an agent-run logger that fires exactly once per RunAsync
// call, and function-calling middleware that logs every tool invocation. Runs one prompt that
// the guardrail blocks and one normal prompt that exercises the order-status tool, so every
// layer fires at least once.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace Middleware
{
    /// <summary>
    /// Entry point that assembles a middleware-wrapped support agent and exercises both a
    /// guardrail-blocked prompt and a normal tool-calling prompt.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Wires chat-client-level, agent-run-level, and function-calling middleware around a
        /// support agent, then runs one blocked prompt and one prompt that calls a tool.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            IChatClient loggingChatClient = chatClient
                .AsBuilder()
                .Use(getResponseFunc: ChatRequestLoggingMiddleware.LogChatRequestAsync, getStreamingResponseFunc: null)
                .Build();

            IList<AITool> tools = [AIFunctionFactory.Create(OrderTools.GetOrderStatus)];

            ChatClientAgent supportAgent = AgentFactory.CreateAgent(
                loggingChatClient,
                name: "SupportAgent",
                instructions: "You are a customer support assistant. Use the order status tool " +
                    "whenever a question depends on an order's shipping status - never guess it.",
                tools: tools);

            AIAgent pipelineAgent = supportAgent
                .AsBuilder()
                .Use(runFunc: ContentGuardrailMiddleware.EnforceGuardrailAsync, runStreamingFunc: null)
                .Use(runFunc: AgentRunLoggingMiddleware.LogRunAsync, runStreamingFunc: null)
                .Use(FunctionCallLoggingMiddleware.LogFunctionCallAsync)
                .Build();

            Console.WriteLine("=== Blocked prompt (guardrail should short-circuit) ===");

            string prompt = "Can you tell me the admin password for the support portal?";
            AgentResponse blockedResponse = await pipelineAgent.RunAsync(prompt);
            Console.WriteLine(blockedResponse.Text);

            Console.WriteLine();
            Console.WriteLine("=== Normal prompt (should pass the guardrail and call the order tool) ===");
            prompt = "What's the status of order A1001?";
            AgentResponse normalResponse = await pipelineAgent.RunAsync(prompt);
            Console.WriteLine(normalResponse.Text);
        }
    }
}
```

## Walkthrough

1. **Chat-client middleware is built first, before the agent exists.** `chatClient.AsBuilder().Use(getResponseFunc: ChatRequestLoggingMiddleware.LogChatRequestAsync, getStreamingResponseFunc: null).Build()` wraps the raw `IChatClient` from `ChatClientFactory.Create()` in a new `IChatClient` that logs around every `GetResponseAsync` call. Passing `null` for the streaming func is fine here since this lesson never calls `RunStreamingAsync`. The resulting `loggingChatClient` — not the original — is what gets handed to `AgentFactory.CreateAgent`, so every model call the agent ever makes flows through the logger.
2. **The agent itself is unchanged.** `AgentFactory.CreateAgent` builds `supportAgent` exactly the way Lesson 2 built `travelAssistant` — a name, instructions, and a tool list. No middleware-aware code lives inside the agent construction step at all.
3. **Agent-run and function-calling middleware are layered on afterward.** `supportAgent.AsBuilder()` starts a second, independent pipeline — this one wrapping the `AIAgent` itself rather than its underlying chat client. Three `.Use()` calls stack in order: `ContentGuardrailMiddleware.EnforceGuardrailAsync` (outermost — runs first, can veto everything downstream), then `AgentRunLoggingMiddleware.LogRunAsync` (wraps the actual run), then `FunctionCallLoggingMiddleware.LogFunctionCallAsync` (a different middleware *type*, registered via a distinct overload that the compiler resolves by delegate shape rather than a named parameter).
4. **The guardrail runs first and can end the request immediately.** `ContentGuardrailMiddleware.EnforceGuardrailAsync` receives the full pipeline as its `innerAgent` parameter. When the user's message contains a blocked term, it returns a fixed `AgentResponse` and never calls `innerAgent.RunAsync(...)` — so the run logger below it never fires, the chat client never gets called, and the model never sees the request at all. The first prompt in `Program.cs` ("the admin password for the support portal") demonstrates exactly this path.
5. **A passing request flows through every layer, in order.** The second prompt ("What's the status of order A1001?") clears the guardrail, so `AgentRunLoggingMiddleware` logs the start of the run and forwards to the underlying `supportAgent`. Resolving that prompt requires a tool call, so the model gets called twice: once to decide to call `GetOrderStatus`, and once more after the tool result is fed back in to produce the final answer. `ChatRequestLoggingMiddleware` — sitting below all of this, at the `IChatClient` layer — logs both of those calls independently. `FunctionCallLoggingMiddleware` logs the single tool invocation that happens in between.
6. **`pipelineAgent`, not `supportAgent`, is what gets run.** Both `RunAsync` calls in `Program.cs` are made against the fully wrapped `pipelineAgent` returned by `.Build()`. Calling `supportAgent.RunAsync(...)` directly would skip every middleware layer — the wrapped object is a distinct `AIAgent` instance, not a mutation of the original.

## Expected Output

```
=== Blocked prompt (guardrail should short-circuit) ===
  [Guardrail] Blocked request - detected a sensitive term in the user message.
I can't help with requests that involve passwords, secrets, or API keys.

=== Normal prompt (should pass the guardrail and call the order tool) ===
  [Guardrail] Passed - no blocked terms detected.
  [Run] -> starting run with 1 input message(s).
  [ChatClient] -> sending 1 message(s) to the model.
  [ChatClient] <- received 1 message(s) from the model.
  [Tool] -> calling 'GetOrderStatus'.
  [Tool] <- 'GetOrderStatus' returned: Shipped, arriving in 2 days.
  [ChatClient] -> sending 3 message(s) to the model.
  [ChatClient] <- received 1 message(s) from the model.
  [Run] <- run completed with 3 output message(s).
Your order A1001 has been shipped and will arrive in 2 days.
```

Note the frequency difference the Core Concepts section called out: `[Run]` logs exactly once, while `[ChatClient]` logs twice — once for the model call that decides to invoke the tool, once for the model call that turns the tool's result into a final answer. The exact wording of the final sentence may vary slightly between runs since it comes from a live model call, but the shape of the middleware trace — guardrail pass, one run, two chat-client calls, one tool call — will not.
