# Lesson 20 — Observability & Evaluation

## Project Overview

Every prior lesson trusted that a model response was "good enough" just by eyeballing the console. This lesson adds the two lenses a production system actually needs before anyone trusts it: **system health** — is the agent behaving, how many tokens is it burning, how long is each call taking — via OpenTelemetry tracing and metrics; and **outcome quality** — is the *answer itself* actually good — via `Microsoft.Extensions.AI.Evaluation`'s LLM-judged quality evaluators. Neither lens substitutes for the other: a perfectly healthy, fast, cheap call can still return a wrong or incoherent answer, and a great answer can still come from a system quietly burning ten times the tokens it needs to.

## Prerequisites

Lesson 1 (`ChatClientAgent`, `ChatClientFactory`/`AgentFactory` pattern) and Lesson 6 (Middleware — this lesson's `MaxOutputTokensChatClient` is a `DelegatingChatClient`, the same middleware shape Lesson 6 introduced). No workflow-pattern lessons are required; this lesson runs a single agent.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, and the home of agent-level `UseOpenTelemetry` (`OpenTelemetryAgentBuilderExtensions`) |
| `Microsoft.Extensions.AI` | 10.10.0 | The non-Abstractions "core" package — home of `IChatClient`'s `AsBuilder()`/`UseOpenTelemetry` extension methods and `DelegatingChatClient` |
| `Microsoft.Extensions.AI.Evaluation` | 10.10.0 | Core evaluation abstractions — `ChatConfiguration`, `IEvaluator`, `EvaluationResult`, `NumericMetric` |
| `Microsoft.Extensions.AI.Evaluation.Quality` | 10.10.0 | LLM-judged quality evaluators — this lesson uses `CoherenceEvaluator` and `RelevanceEvaluator` |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |
| `OpenTelemetry` | 1.19.1 | The OpenTelemetry .NET SDK — `TracerProvider`/`MeterProvider` |
| `OpenTelemetry.Exporter.Console` | 1.19.1 | Prints spans and metrics straight to the console, so this lesson needs no external backend (Aspire Dashboard, Azure Monitor, Jaeger, etc.) to be followed along with |

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

This lesson makes three real model calls per run — one agent call, one Coherence judge call, one Relevance judge call. The agent under test and the LLM judge used by the evaluators both get their chat client from `ChatClientFactory`, so they run on the same provider and model.

**⚠️ Sensitive-data flag is development-only.** Both instrumentation call sites below pass `cfg.EnableSensitiveData = true`, which captures full prompt and response text into span attributes. Microsoft's guidance is explicit: only enable this in development or testing, since it can expose user content in production logs and traces.

## Core Concepts

- **Two lenses, not one.** OpenTelemetry tracing/metrics answer "is the system healthy" — latency, token counts, error rates — entirely independent of whether the text that came back is any good. `Microsoft.Extensions.AI.Evaluation`'s quality evaluators answer "is this specific answer good," using a second LLM as a judge. A dashboard full of green health metrics says nothing about answer quality, and a high coherence score says nothing about whether the call that produced it was slow or expensive. Production systems need both.

- **Observability is opt-in and applied at two separate layers.** The chat client and the agent are each wrapped independently:

  ```csharp
  IChatClient instrumentedChatClient = chatClient
      .AsBuilder()
      .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
      .Build();

  AIAgent instrumentedAgent = baseAgent
      .AsBuilder()
      .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
      .Build();
  ```

  Wrapping both produces two spans per call — a child `chat <model>` span (from the chat client) nested inside a parent `invoke_agent <name>(<id>)` span (from the agent). This is intentional, though it can look like duplication: wrap only the layer you actually need traced if you want a leaner trace.

- **Both layers use the same builder shape.** `chatClient.AsBuilder()` returns a `ChatClientBuilder`, and `UseOpenTelemetry(...)` from `Microsoft.Extensions.AI` adds an OpenTelemetry layer before `.Build()` produces the wrapped `IChatClient`. Agents follow the identical `AsBuilder()`/`Use...()`/`Build()` pattern: `AIAgentExtensions.AsBuilder(this AIAgent)` returns an `AIAgentBuilder`, and `OpenTelemetryAgentBuilderExtensions.UseOpenTelemetry(this AIAgentBuilder, string? sourceName, Action<OpenTelemetryAgent>? configure)`, both in `Microsoft.Agents.AI`, attach the instrumentation before `.Build()` produces the wrapped `AIAgent`.

- **Traces subscribe with `AddSource`, metrics with `AddMeter`.** Agent Framework emits its traces and its metrics under the same `sourceName` string, but the OpenTelemetry .NET SDK subscribes to them through two different builder methods. `TracerProviderBuilder.AddSource(SourceName)` listens to the `ActivitySource` that produces spans, and `MeterProviderBuilder.AddMeter(SourceName)` listens to the `Meter` that produces metrics. `TelemetryFactory` calls each one on its own provider.

- **Spans and metrics.** Every instrumented call emits a `chat <model>` span and (when the agent itself is instrumented too) a parent `invoke_agent <name>(<id>)` span, both carrying `gen_ai.*` attributes — `gen_ai.operation.name`, `gen_ai.request.model`, `gen_ai.usage.input_tokens`/`output_tokens`, and (only with `EnableSensitiveData = true`) the full `gen_ai.input.messages`/`gen_ai.output.messages` JSON. Two histogram metrics are emitted per call: `gen_ai.client.token.usage` (tagged by `gen_ai.token.type` — `input` or `output`) and `gen_ai.client.operation.duration`, in seconds.

- **Evaluators need an `IChatClient` judge, not a fixed rubric.** `ChatConfiguration` is a one-property wrapper (`ChatClient`) around whatever `IChatClient` should act as the LLM judge — this lesson builds its judge from the same `ChatClientFactory` as the agent, but a production setup could point the judge at a different, stronger model than the one being evaluated. `IEvaluator.EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse, ChatConfiguration, ...)` returns an `EvaluationResult`, a named bag of metrics; `result.Get<NumericMetric>(CoherenceEvaluator.CoherenceMetricName)` retrieves one, exposing `.Value`, `.Interpretation.Rating`/`.Failed`, `.Reason`, and any `.Diagnostics`.

- **`MaxOutputTokensChatClient` sets the judge's output-token budget.** The quality evaluators ask the judge model for a reply that ends in a tagged score, then parse that score out of the reply. `MaxOutputTokensChatClient` is a small `DelegatingChatClient` placed in front of the judge's `IChatClient`: on every request it sets `ChatOptions.MaxOutputTokens` to at least 8000 and then forwards the call, so the judge always has room to write its reasoning and finish with the score. A budget matters most for a reasoning model, which spends output tokens on internal reasoning before its visible answer (the `gen_ai.usage.reasoning.output_tokens` span attribute reports how many), so the same code keeps working whichever model `MAF_LLM` selects for the judge. When a judge reply carries no parseable score, the metric's `Value` is null, its `Rating` is `Inconclusive`, and its `Diagnostics` contain the reply the evaluator could not parse.

## Complete C# Code

**`src/ObservabilityAndEvaluation/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace ObservabilityAndEvaluation
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

**`src/ObservabilityAndEvaluation/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace ObservabilityAndEvaluation
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances from a shared <see cref="IChatClient"/>. Same shape as Lesson 1's factory, reused
    /// unchanged here since this lesson's new material is entirely about wrapping the resulting
    /// chat client and agent with observability and evaluation, not about how they're built.
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

**`src/ObservabilityAndEvaluation/TelemetryFactory.cs`**

```csharp
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ObservabilityAndEvaluation
{
    /// <summary>
    /// Builds the OpenTelemetry TracerProvider and MeterProvider this lesson uses to view Agent
    /// Framework's built-in gen_ai.* spans and metrics directly on the console, so the lesson runs
    /// end to end with no external backend (Aspire Dashboard, Azure Monitor, etc.) required. A
    /// production system would swap the console exporters here for an OTLP or Azure Monitor
    /// exporter without touching any other file in this project.
    /// </summary>
    internal static class TelemetryFactory
    {
        /// <summary>
        /// Creates a TracerProvider subscribed to the given ActivitySource name, printing every
        /// completed span to the console.
        /// </summary>
        internal static TracerProvider BuildTracerProvider(string sourceName, string serviceName)
        {
            return Sdk.CreateTracerProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                .AddSource(sourceName)
                .AddConsoleExporter()
                .Build();
        }

        /// <summary>
        /// Creates a MeterProvider subscribed to the given Meter name, printing every collected
        /// metric to the console. Note this calls AddMeter, not AddSource - traces and metrics are
        /// subscribed to by different builder methods in the OpenTelemetry .NET SDK even though
        /// Agent Framework emits both under the same sourceName string.
        /// </summary>
        internal static MeterProvider BuildMeterProvider(string sourceName, string serviceName)
        {
            return Sdk.CreateMeterProviderBuilder()
                .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService(serviceName))
                .AddMeter(sourceName)
                .AddConsoleExporter()
                .Build();
        }
    }
}
```

**`src/ObservabilityAndEvaluation/MaxOutputTokensChatClient.cs`**

```csharp
using Microsoft.Extensions.AI;

namespace ObservabilityAndEvaluation
{
    /// <summary>
    /// Delegating <see cref="IChatClient"/> that sets every outgoing request's MaxOutputTokens to
    /// at least a configured budget before forwarding it to the inner client. This lesson wraps
    /// the evaluators' "judge" chat client in it, so the judge always has room to write its
    /// reasoning and finish with the tagged score the quality evaluators parse.
    /// </summary>
    internal sealed class MaxOutputTokensChatClient : DelegatingChatClient
    {
        private readonly int _minimumMaxOutputTokens;

        /// <summary>
        /// Wraps innerClient, ensuring every outgoing request's MaxOutputTokens is at least
        /// minimumMaxOutputTokens.
        /// </summary>
        internal MaxOutputTokensChatClient(IChatClient innerClient, int minimumMaxOutputTokens)
            : base(innerClient)
        {
            _minimumMaxOutputTokens = minimumMaxOutputTokens;
        }

        /// <summary>
        /// Forwards a non-streaming request to the inner client after raising MaxOutputTokens if needed.
        /// </summary>
        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetResponseAsync(messages, Boosted(options), cancellationToken);

        /// <summary>
        /// Forwards a streaming request to the inner client after raising MaxOutputTokens if needed.
        /// </summary>
        public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => base.GetStreamingResponseAsync(messages, Boosted(options), cancellationToken);

        /// <summary>
        /// Clones the given options (or creates new ones) and raises MaxOutputTokens to the
        /// configured floor if it is unset or lower.
        /// </summary>
        private ChatOptions Boosted(ChatOptions? options)
        {
            ChatOptions boosted = options?.Clone() ?? new ChatOptions();
            if (boosted.MaxOutputTokens is null || boosted.MaxOutputTokens < _minimumMaxOutputTokens)
            {
                boosted.MaxOutputTokens = _minimumMaxOutputTokens;
            }
            return boosted;
        }
    }
}
```

**`src/ObservabilityAndEvaluation/Program.cs`**

```csharp
// Lesson 20 - Observability & Evaluation
// Runs one OpenAI-backed ChatClientAgent through two different lenses. Part 1 (system health)
// instruments the chat client and the agent with OpenTelemetry and prints the resulting gen_ai.*
// spans and metrics straight to the console - no Aspire Dashboard or Azure Monitor required to
// follow along. Part 2 (outcome quality) takes that same response and scores it with two
// Microsoft.Extensions.AI.Evaluation.Quality evaluators, using a second OpenAI client as the
// LLM judge. The two halves answer two different questions: is the system behaving (health), and
// is the answer actually good (quality) - neither one substitutes for the other.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace ObservabilityAndEvaluation
{
    /// <summary>
    /// Entry point that runs an instrumented agent, then evaluates the quality of its response.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// The OpenTelemetry ActivitySource/Meter name Agent Framework's instrumentation is told
        /// to emit under, and the name this lesson's TracerProvider/MeterProvider subscribe to.
        /// </summary>
        private const string SourceName = "ObservabilityAndEvaluation";

        /// <summary>
        /// The OpenTelemetry resource service.name attached to every span and metric this lesson emits.
        /// </summary>
        private const string ServiceName = "Lesson20ObservabilityAndEvaluation";

        /// <summary>
        /// The question asked of the instrumented agent in Part 1, then re-used as the query
        /// evaluated for quality in Part 2.
        /// </summary>
        private const string Question = "What is the capital of France, in one sentence?";

        /// <summary>
        /// Runs Part 1 (instrumented agent call, system health) followed by Part 2 (quality
        /// evaluation of that call's response).
        /// </summary>
        private static async Task Main(string[] args)
        {
            using TracerProvider tracerProvider = TelemetryFactory.BuildTracerProvider(SourceName, ServiceName);
            using MeterProvider meterProvider = TelemetryFactory.BuildMeterProvider(SourceName, ServiceName);

            Console.WriteLine("=== Part 1: system health (OpenTelemetry traces + metrics) ===");
            Console.WriteLine();
            string answer = await RunInstrumentedAgentAsync();

            // Force the console exporters to flush now, so Part 1's spans/metrics print before
            // Part 2 starts rather than interleaving with it or appearing only at process exit.
            tracerProvider.ForceFlush();
            meterProvider.ForceFlush();

            Console.WriteLine();
            Console.WriteLine("=== Part 2: outcome quality (Microsoft.Extensions.AI.Evaluation) ===");
            Console.WriteLine();
            await EvaluateResponseAsync(answer);
        }

        /// <summary>
        /// Builds an OpenAI chat client and agent with OpenTelemetry instrumentation attached to
        /// both, runs one question through the agent, and returns its answer text. The chat
        /// client and the agent are instrumented separately and both include sensitive data
        /// (prompts and responses) so the console output below is fully readable - see this
        /// lesson's Setup section for why that flag is development-only.
        /// </summary>
        private static async Task<string> RunInstrumentedAgentAsync()
        {
            IChatClient chatClient = ChatClientFactory.Create();

            IChatClient instrumentedChatClient = chatClient
                .AsBuilder()
                .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
                .Build();

            ChatClientAgent baseAgent = AgentFactory.CreateAgent(
                instrumentedChatClient,
                name: "ObservabilityDemoAgent",
                instructions: "You are a concise assistant.");

            AIAgent instrumentedAgent = baseAgent
                .AsBuilder()
                .UseOpenTelemetry(sourceName: SourceName, configure: cfg => cfg.EnableSensitiveData = true)
                .Build();

            AgentResponse response = await instrumentedAgent.RunAsync(Question);
            Console.WriteLine($"Agent response: {response.Text}");

            return response.Text;
        }

        /// <summary>
        /// Scores the given answer for Coherence and Relevance against this lesson's fixed
        /// question, using a second, independent OpenAI chat client as the LLM judge, and prints
        /// each metric's score, rating, and (for Coherence) the judge's reasoning.
        /// </summary>
        private static async Task EvaluateResponseAsync(string answer)
        {
            IChatClient judgeClient = new MaxOutputTokensChatClient(
                ChatClientFactory.Create(), minimumMaxOutputTokens: 8000);
            var chatConfiguration = new ChatConfiguration(judgeClient);

            var messages = new List<ChatMessage> { new(ChatRole.User, Question) };
            var chatResponse = new ChatResponse(new ChatMessage(ChatRole.Assistant, answer));

            IEvaluator coherenceEvaluator = new CoherenceEvaluator();
            EvaluationResult coherenceResult = await coherenceEvaluator.EvaluateAsync(messages, chatResponse, chatConfiguration);
            NumericMetric coherence = coherenceResult.Get<NumericMetric>(CoherenceEvaluator.CoherenceMetricName);
            Console.WriteLine($"Coherence: {coherence.Value} ({coherence.Interpretation?.Rating}) - {coherence.Reason}");

            IEvaluator relevanceEvaluator = new RelevanceEvaluator();
            EvaluationResult relevanceResult = await relevanceEvaluator.EvaluateAsync(messages, chatResponse, chatConfiguration);
            NumericMetric relevance = relevanceResult.Get<NumericMetric>(RelevanceEvaluator.RelevanceMetricName);
            Console.WriteLine($"Relevance: {relevance.Value} ({relevance.Interpretation?.Rating})");
        }
    }
}
```

## Walkthrough

1. **`Main` wires up telemetry before anything else runs.** Both `using`-scoped providers (`TracerProvider`, `MeterProvider`) live for the whole method so they can capture every span/metric from both Part 1 and Part 2, and are cleanly disposed (flushing anything not yet exported) when `Main` returns.
2. **`RunInstrumentedAgentAsync` wraps two separate objects, deliberately.** The raw chat client is wrapped with `.AsBuilder().UseOpenTelemetry(...)` first, then handed to `AgentFactory.CreateAgent`, and the *resulting agent* is wrapped with its own `.AsBuilder().UseOpenTelemetry(...)` call. This is what produces the parent/child `invoke_agent`/`chat` span pair described in Core Concepts — instrumenting only one of the two would produce only one span.
3. **A single, explicit `ForceFlush()` call on each provider marks the boundary between the two parts.** The OpenTelemetry console exporter also exports periodically on its own timer, so you may see metric output appear more than these two explicit points — that's expected.
4. **`EvaluateResponseAsync` builds a completely independent chat client for the judge role** — a fresh `ChatClientFactory.Create()` call, not the already-instrumented one from Part 1 — and wraps it in `MaxOutputTokensChatClient` before handing it to `ChatConfiguration`. The evaluators make their judge calls through that client, so every judge request carries the 8000-token budget.
5. **`CoherenceEvaluator`/`RelevanceEvaluator` are used identically** — construct, call `EvaluateAsync(messages, chatResponse, chatConfiguration)`, then pull a named `NumericMetric` out of the returned `EvaluationResult` by the evaluator's own published metric-name constant. Every quality evaluator in the `Microsoft.Extensions.AI.Evaluation.Quality` package (Fluency, Completeness, Groundedness, and the rest) follows this exact same three-step shape.

## Expected Output

<!-- EXPECTED OUTPUT PENDING: re-run with OPENAI_API_KEY set -->

Part 1 prints a `chat gpt-4.1-mini` span (with `gen_ai.*` tags, including the full input and output messages because sensitive data is enabled) nested under an `invoke_agent ObservabilityDemoAgent(...)` span, then the line `Agent response:` with the agent's one-sentence answer, then the `gen_ai.client.token.usage` and `gen_ai.client.operation.duration` histogram metrics. Part 2 prints one line each for Coherence and Relevance: a numeric score from 1 to 5 with its rating, and for Coherence the judge's reasoning. Trace ids, timings, token counts and scores vary between runs.
