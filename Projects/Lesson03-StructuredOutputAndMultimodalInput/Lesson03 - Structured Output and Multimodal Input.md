# Lesson 3 — Structured Output & Multimodal Input

## Project Overview

In this lesson we push an agent past plain text in two directions: first, forcing it to return a strongly-typed `RecipeIdea` object instead of free-form prose via `RunAsync<T>`; second, sending it an image alongside a text prompt via a multi-content `ChatMessage` so it can reason about what it sees. Both are one-call features on the exact same agent type we've used since Lesson 1 — no new abstractions, just richer inputs and outputs.

## Prerequisites

Lesson 1 (Hello Agent & Personas) and Lesson 2 (Function Tools) — this lesson assumes familiarity with `IChatClient`, `ChatClientAgent`, `RunAsync`, and `AgentResponse`. This lesson is otherwise standalone: it does not reference either prior project.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AgentResponse<T>`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

**⚠️ A note on the embedded test image.** The multimodal demo in this lesson embeds a tiny generated PNG (`SampleImage.cs`) and sends its bytes as `DataContent`, rather than pointing the model at a picture on the web. Two cautions are worth knowing if you build your own multimodal demos:

1. **Don't rely on `UriContent` (a remote image URL).** Some providers cannot fetch arbitrary remote URLs themselves and reject the request. Downloading the bytes yourself and sending them as `DataContent` works everywhere.
2. **A model's image decoder can be pickier than an image viewer.** A hand-built PNG that browsers and image viewers open without complaint can still be rejected by a provider's API with a generic "unable to process image" error. Re-encoding the same picture with a mainstream image library usually fixes it. **Takeaway: if a provider rejects an image that opens fine everywhere else, re-save it with a mainstream image library/tool before assuming your code is broken.**

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

- **`RunAsync<T>(string message, ...)`** — a generic overload on `AIAgent` that asks the agent to produce a result of type `T` instead of free text. It returns `Task<AgentResponse<T>>`. MAF derives a JSON schema from `T` at the wire level, and the model is instructed to conform to it.
- **`AgentResponse<T>.Result`** — the deserialized `T` instance. `AgentResponse<T>` inherits from `AgentResponse`, so `.Text` (the raw JSON the model produced) and `.Messages` are still available if you need them.
- **When to reach for `RunAsync<T>` vs. `ChatOptions.ResponseFormat`.** `RunAsync<T>` is the right tool when the target type is known at compile time — exactly our case with `RecipeIdea`. MAF also supports configuring `ResponseFormat` directly on `AgentRunOptions` (via `ChatResponseFormat.ForJsonSchema<T>()` or a raw JSON schema string) for cases where the schema isn't known until runtime, or where inter-agent collaboration needs the raw JSON text rather than a deserialized object. This lesson only needs the simpler `RunAsync<T>` path.
- **Multimodal input is just a richer `ChatMessage`.** A `ChatMessage` has a `Contents` list of `AIContent` items. Mixing a `TextContent` and a `DataContent` (or `UriContent`) in the same message is how you send "a question about this picture" as a single, ordinary turn — there's no separate "vision agent" type.
- **`DataContent` vs. `UriContent`.** `UriContent` tells the model "here's a link, go fetch it yourself" — support for this varies by provider, so, as this lesson's Setup section explains, it isn't something to rely on. `DataContent(byte[], mediaType)` embeds the raw bytes directly in the request, which is the more portable choice and what this lesson uses.

## Complete C# Code

**`src/StructuredOutputAndMultimodalInput/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace StructuredOutputAndMultimodalInput
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

**`src/StructuredOutputAndMultimodalInput/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace StructuredOutputAndMultimodalInput
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

**`src/StructuredOutputAndMultimodalInput/RecipeIdea.cs`**

```csharp
namespace StructuredOutputAndMultimodalInput
{
    /// <summary>
    /// The typed shape requested from the agent via <c>RunAsync&lt;RecipeIdea&gt;</c>. MAF derives
    /// a JSON schema from this class and asks the model to conform to it, then deserializes the
    /// model's JSON response back into an instance of this class - no manual parsing required.
    /// </summary>
    internal sealed class RecipeIdea
    {
        /// <summary>
        /// The name of the suggested dish.
        /// </summary>
        public string? DishName { get; set; }

        /// <summary>
        /// The cuisine the dish belongs to, e.g. "Italian" or "Thai".
        /// </summary>
        public string? CuisineType { get; set; }

        /// <summary>
        /// The estimated total time to cook the dish, in minutes.
        /// </summary>
        public int EstimatedMinutesToCook { get; set; }

        /// <summary>
        /// The list of main ingredients needed for the dish.
        /// </summary>
        public List<string> Ingredients { get; set; } = [];
    }
}
```

**`src/StructuredOutputAndMultimodalInput/SampleImage.cs`**

```csharp
namespace StructuredOutputAndMultimodalInput
{
    /// <summary>
    /// A small, self-contained test image (a yellow sun over a blue sky and a green field)
    /// embedded directly as base64 so this lesson has no network dependency on an external
    /// image host to demonstrate multimodal input.
    /// </summary>
    internal static class SampleImage
    {
        /// <summary>
        /// A 256x256 RGBA PNG, base64-encoded.
        /// </summary>
        internal const string Base64Png =
            "iVBORw0KGgoAAAANSUhEUgAAAQAAAAEACAYAAABccqhmAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8" +
            "YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAATFSURBVHhe7dSxtexEAETBnxMJEAAhkBhZkBAREMTHwGsT" +
            "pF2NbhnlPOOtZs70/fH7n3//BJp+7B+ADgGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGA" +
            "MAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAGAMAHgKD//+uV/2/9ZJgA83g74SvtbNQLA" +
            "I+1QP2G/oUAAeJwd5iftt7ydAPAYO8Zv2m97KwHg63Z8T7Lf+jYCwFft4J5ov/lNBICv2JGdYM/wBgLA" +
            "x+2wTrJnOZ0A8FE7qBPtmU4mAHzMDulke7ZTCQAfsQN6gz3jiQSA2+1w3mTPehoB4HY7mjfZs55GALjV" +
            "DuaN9swnEQBus0N5sz37KQSA2+xI3mzPfgoB4BY7kIK9gxMIALfYcRTsHZxAALjcDqNk7+LpBIBL7SCK" +
            "9k6eTAC41I6haO/kyQSAS+0YivZOnkwAuMwOoWzv5qkEgMvsCMr2bp5KALjMjqBs7+apBIBL7AA4IwIC" +
            "wCX28SMAhOzjRwAI2cePABCyjx8BIGQfPwJAyD5+BICQffwIABH78PnX3tMTCQCX2MePABCyjx8BIGQf" +
            "PwJAyD5+BICQffwIACH7+BEAQvbxIwCE7ONHAIjZAZTt3TyVAHCZHUHZ3s1TCQCX2RGU7d08lQBwqR1C" +
            "0d7JkwkAl9oxFO2dPJkAcKkdQ9HeyZMJAJfbQZTsXTydAHCLHUbB3sEJBIBb7DgK9g5OIADcZgfyZnv2" +
            "UwgAt9mRvNme/RQCwK12KG+0Zz6JAHC7Hcyb7FlPIwDcbkfzJnvW0wgAH7HDeYM944kEgI/ZAZ1sz3Yq" +
            "AeCjdkgn2jOdTAD4uB3USfYspxMAvmKHdYI9wxsIAF+1I3ui/eY3EQC+bgf3JPutbyMAPMaO75v2295K" +
            "AHicHeMn7be8nQDwSDvMT9hvKBAAHm+HeqX9rRoB4Cg74P9i/2eZAECYAECYAECYAECYAECYAECYAECY" +
            "AECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECY" +
            "AECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECY" +
            "AECYAECYAECYAECYAECYAECYAECYAEDYj9/++PUn0CQAECYAECYAECYAECYAECYAECYAECYAECYAECYA" +
            "ECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYA" +
            "ECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYA" +
            "ECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYA" +
            "ECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYA" +
            "ECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAECYAEPYPqYyaEX0l45UAAAAA";

        /// <summary>
        /// Decodes <see cref="Base64Png"/> into raw PNG bytes.
        /// </summary>
        internal static byte[] GetBytes() => Convert.FromBase64String(Base64Png);
    }
}
```

**`src/StructuredOutputAndMultimodalInput/Program.cs`**

```csharp
// Lesson 3 - Structured Output & Multimodal Input
// Part 1 demonstrates forcing an agent to return a typed, parseable result instead of free
// text, using RunAsync<T>. Part 2 demonstrates sending image input alongside text in a single
// ChatMessage and having the agent reason about the image.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace StructuredOutputAndMultimodalInput
{
    /// <summary>
    /// Entry point that runs a structured-output demo followed by a multimodal-input demo.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Runs the recipe-suggestion structured-output demo and the image-analysis
        /// multimodal-input demo in sequence, printing both results to the console.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            await RunStructuredOutputDemoAsync(chatClient);
            Console.WriteLine();
            await RunMultimodalInputDemoAsync(chatClient);
        }

        /// <summary>
        /// Asks a chef agent for a dinner idea and requests the response as a typed
        /// <see cref="RecipeIdea"/> instead of free-form text.
        /// </summary>
        private static async Task RunStructuredOutputDemoAsync(IChatClient chatClient)
        {
            ChatClientAgent chefAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "ChefAgent",
                instructions: "You are a creative chef who suggests simple weeknight dinner recipes.");

            AgentResponse<RecipeIdea> response = await chefAgent.RunAsync<RecipeIdea>(
                "Suggest a quick weeknight dinner idea using chicken and rice.");

            RecipeIdea recipe = response.Result;
            Console.WriteLine("=== Structured Output: RecipeIdea ===");
            Console.WriteLine($"Dish: {recipe.DishName} ({recipe.CuisineType})");
            Console.WriteLine($"Estimated time: {recipe.EstimatedMinutesToCook} minutes");
            Console.WriteLine($"Ingredients: {string.Join(", ", recipe.Ingredients)}");
        }

        /// <summary>
        /// Sends a single ChatMessage containing both a text question and inline image bytes to
        /// a vision-capable agent, so it can describe the contents of the image. The image comes
        /// from <see cref="SampleImage"/> - a small embedded PNG - so this lesson has no network
        /// dependency on an external image host and stays reproducible for every viewer.
        /// </summary>
        private static async Task RunMultimodalInputDemoAsync(IChatClient chatClient)
        {
            ChatClientAgent visionAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "VisionAgent",
                instructions: "You are a helpful assistant that can analyze images and describe them concisely.");

            ChatMessage message = new(ChatRole.User,
            [
                new TextContent("What do you see in this image? Answer in two sentences."),
                new DataContent(SampleImage.GetBytes(), "image/png"),
            ]);

            AgentResponse response = await visionAgent.RunAsync(message);
            Console.WriteLine("=== Multimodal Input: Image Analysis ===");
            Console.WriteLine(response.Text);
        }
    }
}
```

**`src/StructuredOutputAndMultimodalInput/StructuredOutputAndMultimodalInput.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>StructuredOutputAndMultimodalInput</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.24.0" />
    <PackageReference Include="Microsoft.Extensions.AI.OpenAI" Version="10.10.1" />
    <PackageReference Include="OpenAI" Version="2.14.0" />
  </ItemGroup>

</Project>
```

**`StructuredOutputAndMultimodalInput.slnx`**

```xml
<Solution>
  <Folder Name="/src/">
    <Project Path="src/StructuredOutputAndMultimodalInput/StructuredOutputAndMultimodalInput.csproj" />
  </Folder>
</Solution>
```

## Walkthrough

1. **`RunStructuredOutputDemoAsync`** builds an ordinary `ChefAgent` — nothing about its construction is special. The typing happens entirely at the call site: `chefAgent.RunAsync<RecipeIdea>(prompt)` instead of the plain `RunAsync(prompt)` we've used every other lesson.
2. **`response.Result`** is already a fully-populated `RecipeIdea` — `DishName`, `CuisineType`, `EstimatedMinutesToCook`, and `Ingredients` are read directly as strongly-typed members, no `JsonSerializer.Deserialize` call in sight.
3. **`RunMultimodalInputDemoAsync`** builds a `ChatMessage` by hand instead of calling `RunAsync(string)`. The message's content list holds a `TextContent` (the question) followed by a `DataContent` (the image bytes and their MIME type) — order doesn't matter to the model, but keeping the question first reads naturally.
4. **`SampleImage.GetBytes()`** decodes a small embedded base64 PNG. This is here purely so the lesson has zero external dependencies — in your own code, this would typically be `File.ReadAllBytes(path)` for a local file, or downloaded bytes for a remote one (see the Setup section for why sending bytes is more portable than a plain URL reference).
5. **`visionAgent.RunAsync(message)`** uses the `RunAsync(ChatMessage, ...)` overload rather than the `RunAsync(string, ...)` one we've used before — same method family, just accepting a richer input.

## Expected Output

Exact wording will vary by run, but the shape will look like:

```
=== Structured Output: RecipeIdea ===
Dish: One-Pan Lemon Herb Chicken and Rice (Mediterranean)
Estimated time: 35 minutes
Ingredients: 1 lb boneless, skinless chicken thighs or breast, cut into 1-inch pieces, 1 cup long-grain white rice, 2 cups chicken broth, 1 lemon, half sliced, half juiced, 2 cloves garlic, minced, 1/2 onion, chopped, 1 tbsp olive oil, 1 tsp dried oregano, 1/2 tsp dried thyme, Salt and black pepper to taste, Fresh parsley, chopped, for garnish (optional)

=== Multimodal Input: Image Analysis ===
This image displays a very simple, almost cartoon-like depiction of a landscape. The upper
half features a bright blue sky, while the lower half shows a solid green field, with a
large yellow sun positioned in the upper right.
```

The first block is a real `RecipeIdea` object printed field-by-field — never a raw JSON string we had to parse. The second block proves the agent genuinely looked at the embedded image: it correctly names the sky, the field, and the sun without any of those words appearing in the prompt.
