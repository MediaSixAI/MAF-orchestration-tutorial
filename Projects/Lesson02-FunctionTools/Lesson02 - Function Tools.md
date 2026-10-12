# Lesson 2 — Function Tools

## Project Overview

In this lesson we give an agent local C# functions it can call on its own initiative: a `TravelAssistant` agent equipped with two function tools — one that looks up the current time in a city, one that converts currency — and a single prompt that requires both. The point isn't the tools themselves; it's watching `ChatClientAgent` run the entire "model asks for a call → we run local code → the result goes back to the model → the model keeps going" loop for us, with zero dispatch code of our own.

## Prerequisites

Lesson 1 (Hello Agent & Personas) — this lesson assumes you're comfortable with `IChatClient`, `ChatClientAgent`, `instructions`, and `RunAsync`/`AgentResponse`. This lesson is otherwise standalone: it does not reference Lesson 1's project.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AgentResponse`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
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

## Core Concepts

- **`AITool` / `AIFunction`** (`Microsoft.Extensions.AI`) — `AITool` is the base abstraction for anything a model can be offered as a callable capability. `AIFunction` is the concrete subtype that wraps a .NET delegate. Every tool you hand an agent ends up as an `AITool` in a list.
- **`AIFunctionFactory.Create(...)`** — a reflection-based factory that turns any C# method (static or instance, via a delegate/method group) into an `AIFunction`. It inspects the method's parameters and return type and generates a JSON Schema describing the tool for the model.
- **`[Description]` metadata is not decoration.** The `System.ComponentModel.DescriptionAttribute` you put on a tool method and on each of its parameters is serialized into the schema sent to the model. This is how the model decides *which* tool to call and *what arguments* to pass — vague or missing descriptions directly hurt tool-selection accuracy.
- **Tools live on the agent, not the connection.** The `ChatClientAgent` constructor takes an optional `tools: IList<AITool>?` parameter (fourth positional/named parameter, after `instructions` and `name`). Tools passed here are available on every `RunAsync` call for that agent instance. You can also pass a run-specific `ChatOptions` with its own `Tools` list via `ChatClientAgentRunOptions` to add or override tools for a single call — not used in this lesson, but worth knowing it exists.
- **The function-calling loop is automatic.** When you give `ChatClientAgent` tools and the underlying `IChatClient` doesn't already wrap one, the agent transparently inserts a `FunctionInvokingChatClient` into the pipeline. That component is what detects a model's request to call a tool, executes the matching local `AIFunction`, sends the result back as a new turn, and repeats until the model produces a final answer — all inside a single `RunAsync` call. You never see or write that loop yourself.
- **Trust boundary.** By default, every tool you attach is invoked *without* asking for approval, and the model chooses the arguments — treat those arguments as untrusted input from the model, not from your own code. Lesson 9 (Approvals) adds a gate in front of this.

## Complete C# Code

**`src/FunctionTools/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace FunctionTools
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

**`src/FunctionTools/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FunctionTools
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

**`src/FunctionTools/TravelTools.cs`**

```csharp
using System.ComponentModel;

namespace FunctionTools
{
    /// <summary>
    /// Local C# functions exposed to the agent as function tools. Each public method here is
    /// wrapped with <c>AIFunctionFactory.Create</c> in Program.cs to become something the model
    /// can choose to call. The <see cref="DescriptionAttribute"/> on each method and parameter is
    /// not decoration - it is sent to the model as part of the tool's schema and is how the model
    /// decides when and how to call it.
    /// </summary>
    internal static class TravelTools
    {
        private static readonly Dictionary<string, double> CityUtcOffsetHours = new(StringComparer.OrdinalIgnoreCase)
        {
            ["London"] = 0,
            ["Tokyo"] = 9,
            ["New York"] = -4,
            ["Sydney"] = 10,
            ["Nairobi"] = 3,
        };

        private static readonly Dictionary<string, double> UsdExchangeRates = new(StringComparer.OrdinalIgnoreCase)
        {
            ["USD"] = 1.0,
            ["EUR"] = 0.92,
            ["GBP"] = 0.79,
            ["JPY"] = 149.5,
            ["KES"] = 129.0,
        };

        /// <summary>
        /// Returns the current local time for one of a small set of known cities, computed from
        /// a fixed UTC offset. Unknown cities produce a clear error string instead of a guess.
        /// </summary>
        /// <param name="city">The city to look up the local time for.</param>
        [Description("Gets the current local time for a known city.")]
        internal static string GetCurrentTimeInCity(
            [Description("The city name, e.g. 'Tokyo' or 'New York'.")] string city)
        {
            if (!CityUtcOffsetHours.TryGetValue(city, out double offsetHours))
            {
                return $"No time zone data is available for '{city}'.";
            }

            DateTime localTime = DateTime.UtcNow.AddHours(offsetHours);
            return $"The current local time in {city} is {localTime:yyyy-MM-dd HH:mm} (UTC{offsetHours:+0;-0}).";
        }

        /// <summary>
        /// Converts an amount between two currencies using a small fixed table of exchange rates
        /// against USD. Unknown currency codes produce a clear error string instead of a guess.
        /// </summary>
        /// <param name="amount">The amount to convert.</param>
        /// <param name="fromCurrencyCode">The three-letter currency code to convert from.</param>
        /// <param name="toCurrencyCode">The three-letter currency code to convert to.</param>
        [Description("Converts an amount of money from one currency to another.")]
        internal static string ConvertCurrency(
            [Description("The amount of money to convert.")] double amount,
            [Description("The three-letter currency code to convert from, e.g. 'USD'.")] string fromCurrencyCode,
            [Description("The three-letter currency code to convert to, e.g. 'EUR'.")] string toCurrencyCode)
        {
            if (!UsdExchangeRates.TryGetValue(fromCurrencyCode, out double fromRate) ||
                !UsdExchangeRates.TryGetValue(toCurrencyCode, out double toRate))
            {
                return $"No exchange rate data is available for '{fromCurrencyCode}' or '{toCurrencyCode}'.";
            }

            double amountInUsd = amount / fromRate;
            double converted = amountInUsd * toRate;
            return $"{amount:0.##} {fromCurrencyCode} is approximately {converted:0.##} {toCurrencyCode}.";
        }
    }
}
```

**`src/FunctionTools/Program.cs`**

```csharp
// Lesson 2 - Function Tools
// Gives an agent two local C# function tools - GetCurrentTimeInCity and ConvertCurrency -
// and lets the model decide when to call them while answering a single prompt that needs
// both. Demonstrates ChatClientAgent's built-in function-calling loop: no manual dispatch
// code is written to detect a tool call, run it, and feed the result back to the model.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace FunctionTools
{
    /// <summary>
    /// Entry point that runs a travel-assistant agent equipped with function tools.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Creates a travel-assistant agent with two function tools attached, then asks it a
        /// question that requires calling both tools to answer fully.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            IList<AITool> tools =
            [
                AIFunctionFactory.Create(TravelTools.GetCurrentTimeInCity),
                AIFunctionFactory.Create(TravelTools.ConvertCurrency),
            ];

            ChatClientAgent travelAssistant = AgentFactory.CreateAgent(
                chatClient,
                name: "TravelAssistant",
                instructions: "You are a helpful travel assistant. Use the tools available to you " +
                    "whenever a question depends on the current time or a currency conversion - " +
                    "never guess a time or an exchange rate yourself.",
                tools: tools);

            const string prompt = "I'm in New York and need to call a colleague in Tokyo, and I also want " +
                "to know how much 250 USD is in EUR for my trip. What's the local time in both cities, and " +
                "what is that converted amount?";

            AgentResponse response = await travelAssistant.RunAsync(prompt);
            Console.WriteLine(response.Text);
        }
    }
}
```

**`src/FunctionTools/FunctionTools.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>FunctionTools</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.24.0" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.10.1" />
    <PackageReference Include="OpenAI" Version="2.14.0" />
  </ItemGroup>

</Project>
```

**`FunctionTools.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/FunctionTools/FunctionTools.csproj" />
  </Folder>
</Solution>
```

## Walkthrough

1. **`TravelTools`** defines two plain static methods with no MAF-specific base class or interface — `GetCurrentTimeInCity` and `ConvertCurrency`. Each is decorated with `[Description]` on the method and on every parameter; this is the entire "declaration" step for a function tool.
2. **`Program.Main`** calls `AIFunctionFactory.Create(...)` once per method, passing the method group directly (no need to instantiate anything, since both are `static`). Each call returns an `AIFunction`, which is an `AITool`, and both go into a plain `IList<AITool>`.
3. **`AgentFactory.CreateAgent(...)`** passes that tools list straight to the `ChatClientAgent` constructor's `tools` parameter. From this point on, `travelAssistant` always has both tools available.
4. **The prompt is deliberately compound** — it needs a time lookup for two different cities and a currency conversion, forcing the model to make multiple tool calls (in whatever order it decides) before it can compose a final answer.
5. **`RunAsync` does all the work.** Internally: the model responds with one or more `FunctionCallContent` items → `ChatClientAgent`'s built-in `FunctionInvokingChatClient` matches each call to the corresponding `AIFunction`, invokes it with the model-chosen arguments, and sends a `FunctionResultContent` back → the model reads the results and continues → this repeats until the model has enough information to answer in plain text. We only see the final `AgentResponse`.
6. **`response.Text`** is that final aggregated answer — by the time we print it, both tools have already run behind the scenes.

## Expected Output

Exact wording will vary by run, but the shape will look like:

```
It's currently 09:14 in New York (UTC-4) and 22:14 in Tokyo (UTC+9) — so it's already
tomorrow evening there, worth keeping in mind before you call. As for your trip budget,
250 USD converts to approximately 230 EUR at current rates.
```

The model never received the current time or an exchange rate in its instructions — both numbers came from `TravelTools.GetCurrentTimeInCity` and `TravelTools.ConvertCurrency` actually executing as local C# code, mid-conversation, without any dispatch logic we wrote ourselves.
