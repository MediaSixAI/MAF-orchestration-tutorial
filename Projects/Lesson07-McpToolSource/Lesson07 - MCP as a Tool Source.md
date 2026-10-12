# Lesson 7 — MCP as a Tool Source

## Project Overview

In this lesson a `GameMasterAgent` gets its tools from an entirely external source: a small Model Context Protocol (MCP) server, launched as a separate process and spoken to over stdio, exposing a `roll_dice` and a `flip_coin` tool. The agent never sees these tools' C# implementations — it discovers them at runtime from the server and calls them through the exact same function-calling pipeline as Lesson 2's local tools. This is the mechanism that lets an agent use tools it wasn't explicitly coded against, which is the whole point of treating MCP as a universal tool interface.

## Prerequisites

Lessons 1–2 — this lesson assumes familiarity with `IChatClient`, `ChatClientAgent`, `RunAsync`, and function tools (`AITool`, the function-calling loop). This lesson is otherwise standalone: it does not reference any prior project's code, though it does ship a second, companion project (the MCP server) alongside the main lesson project — see Setup below.

## Setup

This lesson is **two projects**, both under the lesson's `src/` folder:

- **`DiceRollerMcpServer`** — the MCP server. A small console app that exposes two tools (`RollDice`, `FlipCoin`) over stdio. It is not run directly; the client launches it as a subprocess.
- **`McpToolSource`** — the lesson proper. Connects to the server, discovers its tools, and runs an agent (on the provider and model chosen by `MAF_LLM` and `MAF_MODEL`, via `ChatClientFactory`) against them.

Opening `McpToolSource.slnx` in VS2026 loads both projects (its solution file references the sibling server project too), so **Build Solution** builds both in one step.

**Build the server before running the client.** The client launches the server by running its built DLL (`dotnet <path-to-dll>`) rather than `dotnet run --project ...`. That is the usual way to launch a stdio MCP server: `dotnet run` can print restore and build progress to standard output, and standard output is where the MCP JSON-RPC stream lives. Build once first:

```powershell
dotnet build src/DiceRollerMcpServer
```

Then run the client as usual (`dotnet run` from inside `src/McpToolSource`, or F5 in VS2026 with `McpToolSource` set as the startup project).

**Target framework:** `net10.0` (both projects)

**NuGet packages — `DiceRollerMcpServer`:**

| Package | Version | Purpose |
|---|---|---|
| `ModelContextProtocol` | 2.2.0 | Official MCP C# SDK — server hosting (`AddMcpServer`, `WithStdioServerTransport`, tool attributes) |
| `Microsoft.Extensions.Hosting` | 10.0.12 | Generic host (`Host.CreateApplicationBuilder`) used to run the MCP server |

**NuGet packages — `McpToolSource`:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |
| `ModelContextProtocol` | 2.2.0 | Official MCP C# SDK — client (`McpClient`, `StdioClientTransport`, `McpClientTool`) |

**Connecting to the server.** The MCP client is created with the static **`McpClient.CreateAsync(transport, ...)`** method, which starts the transport, performs the MCP handshake, and returns a connected `McpClient`. Each tool the server advertises comes back from `ListToolsAsync()` as an `McpClientTool`, which can be handed to an agent as an `AITool`.

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

- **MCP is a protocol, not a MAF feature.** The Model Context Protocol is an open standard for how any application exposes tools (and other context) to any LLM-calling client — Microsoft didn't invent it, MAF just knows how to speak it. That's why the client and server projects in this lesson reference the exact same `ModelContextProtocol` package that any other language or framework's MCP integration would use.
- **A server and a client are two independent processes.** Unlike Lesson 2's tools, which lived in the same process as the agent, `DiceRollerMcpServer` runs as its own subprocess, spoken to over standard input/output. This is the normal MCP shape — the transport could just as easily be HTTP/SSE for a remote server; stdio is simply the local case.
- **`McpClientTool` is already an `AIFunction`.** `mcpClient.ListToolsAsync()` returns `IList<McpClientTool>`, and `McpClientTool` derives from `Microsoft.Extensions.AI.AIFunction` — the exact same base type `AIFunctionFactory.Create(...)` produced in Lesson 2. Once cast to `AITool` and handed to `AgentFactory.CreateAgent(...)`, the agent cannot tell an MCP-sourced tool apart from a hand-written local one; both flow through the identical function-calling loop.
- **Tool discovery is dynamic, not hand-coded.** This project never references `RollDice` or `FlipCoin` by name anywhere in its own source. `ListToolsAsync()` returns whatever the server happens to expose right now — the agent's tool list is built from that response, not from a fixed list the developer wrote. Add a third tool to the server and this client picks it up with zero code changes.
- **Tool names get normalized to the server's convention, not the method's.** `DiceTools.RollDice` and `DiceTools.FlipCoin` are C# `PascalCase` method names, but the MCP server advertises them as `roll_dice` and `flip_coin` — as this lesson's console output shows. The model sees and calls the protocol name, not the C# identifier.
- **Standard output is sacred on the server side.** MCP's JSON-RPC messages ride on stdout when using the stdio transport. `DiceRollerMcpServer`'s `Program.cs` redirects all logging to standard error specifically so nothing else ever writes to stdout — a single stray `Console.WriteLine` in the server would corrupt the protocol stream from the client's point of view. This is also why the client launches the server's *built* DLL rather than `dotnet run` (see the Setup note above).
- **`await using` matters.** `McpClient` owns a live subprocess and open stdio streams for as long as it's connected. `await using McpClient mcpClient = ...` guarantees that subprocess gets torn down when `Main` exits, even if an exception is thrown partway through.
- **Locate the server's DLL from `AppContext.BaseDirectory`, not the current directory.** `Directory.GetCurrentDirectory()` depends on how the app was launched: `dotnet run` from inside `McpToolSource` sets it to the *project* folder, while running the built EXE/DLL directly sets it to wherever you happened to be. `McpServerConnectionFactory.ConnectAsync()` builds the server path from **`AppContext.BaseDirectory`**, the folder containing the running assembly, so the same path resolves whether you start the lesson with `dotnet run`, the built DLL, or VS2026.

## Complete C# Code

### Project 1 — `DiceRollerMcpServer` (the MCP server)

**`src/DiceRollerMcpServer/DiceTools.cs`**

```csharp
using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DiceRollerMcpServer
{
    /// <summary>
    /// MCP tools exposed by this server. Every method here is discovered automatically by
    /// <c>WithToolsFromAssembly</c> via reflection and advertised to any connecting MCP client -
    /// the class and its methods must be public for that reflection-based scan to find them.
    /// </summary>
    [McpServerToolType]
    public static class DiceTools
    {
        private static readonly Random Rng = new();

        /// <summary>
        /// Rolls one or more dice with the given number of sides and returns each individual
        /// result along with the total.
        /// </summary>
        /// <param name="sides">The number of sides on each die. Defaults to 6.</param>
        /// <param name="count">The number of dice to roll. Defaults to 1.</param>
        [McpServerTool, Description("Rolls one or more dice with a given number of sides and returns the results.")]
        public static string RollDice(
            [Description("The number of sides on each die, e.g. 6 or 20.")] int sides = 6,
            [Description("The number of dice to roll.")] int count = 1)
        {
            int[] rolls = Enumerable.Range(0, count).Select(_ => Rng.Next(1, sides + 1)).ToArray();
            return $"Rolled {count}d{sides}: [{string.Join(", ", rolls)}], total = {rolls.Sum()}.";
        }

        /// <summary>
        /// Flips a single coin and returns whether it landed on heads or tails.
        /// </summary>
        [McpServerTool, Description("Flips a coin and returns whether it landed on heads or tails.")]
        public static string FlipCoin()
        {
            return Rng.Next(2) == 0 ? "Heads" : "Tails";
        }
    }
}
```

**`src/DiceRollerMcpServer/Program.cs`**

```csharp
// Lesson 7 - MCP as a Tool Source
// A minimal Model Context Protocol server exposing two simple tools - RollDice and FlipCoin -
// over standard input/output. This process is launched as a subprocess by the companion
// McpToolSource client project; a human does not run it directly.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DiceRollerMcpServer
{
    /// <summary>
    /// Entry point that hosts an MCP server over standard input/output, exposing every tool
    /// method found in <see cref="DiceTools"/>.
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

### Project 2 — `McpToolSource` (the lesson client)

**`src/McpToolSource/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace McpToolSource
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

**`src/McpToolSource/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace McpToolSource
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

**`src/McpToolSource/McpServerConnectionFactory.cs`**

```csharp
using ModelContextProtocol.Client;

namespace McpToolSource
{
    /// <summary>
    /// Builds the MCP client connection to the companion DiceRollerMcpServer process,
    /// launched as a subprocess and spoken to over standard input/output.
    /// </summary>
    internal static class McpServerConnectionFactory
    {
        /// <summary>
        /// Locates the built DiceRollerMcpServer.dll in the sibling project folder,
        /// launches it as a subprocess over stdio, and returns a connected MCP client. Throws a
        /// clear error if the companion server project has not been built yet.
        /// </summary>
        internal static async Task<McpClient> ConnectAsync()
        {
            // AppContext.BaseDirectory is the folder containing this running assembly (always
            // bin/<Config>/net10.0, regardless of how the app was launched), unlike
            // Directory.GetCurrentDirectory() - which is the project directory itself under
            // `dotnet run`, but the bin output folder under other launch methods. Anchoring to
            // BaseDirectory instead makes this path resolution work the same way no matter how
            // the lesson is started.
            string serverProjectDirectory = Path.GetFullPath(
                Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "DiceRollerMcpServer"));
            string serverDllPath = Path.Combine(
                serverProjectDirectory, "bin", "Debug", "net10.0", "DiceRollerMcpServer.dll");

            if (!File.Exists(serverDllPath))
            {
                throw new InvalidOperationException(
                    "The companion MCP server has not been built yet. Run 'dotnet build' inside " +
                    $"DiceRollerMcpServer before running this lesson. Expected to find: {serverDllPath}");
            }

            return await McpClient.CreateAsync(new StdioClientTransport(new()
            {
                Name = "DiceRollerServer",
                Command = "dotnet",
                Arguments = [serverDllPath],
            }));
        }
    }
}
```

**`src/McpToolSource/Program.cs`**

```csharp
// Lesson 7 - MCP as a Tool Source
// Connects to the companion DiceRollerMcpServer process over stdio, discovers its tools at
// runtime via the Model Context Protocol, and hands them straight to an OpenAI-powered agent as
// AITool instances - no tool was hand-coded into this project the way Lesson 2's tools were.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace McpToolSource
{
    /// <summary>
    /// Entry point that connects to a local MCP server, discovers its tools, and runs a
    /// game-master agent that uses those tools to answer two prompts.
    /// </summary>
    internal sealed class Program
    {
        /// <summary>
        /// Connects to the DiceRollerMcpServer subprocess, lists its tools, wires them into an
        /// agent, and asks it to roll dice and flip a coin.
        /// </summary>
        private static async Task Main(string[] args)
        {
            await using McpClient mcpClient = await McpServerConnectionFactory.ConnectAsync();

            IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
            Console.WriteLine($"Discovered {mcpTools.Count} tool(s) from the MCP server: " +
                string.Join(", ", mcpTools.Select(t => t.Name)));
            Console.WriteLine();

            IChatClient chatClient = ChatClientFactory.Create();

            ChatClientAgent gameMasterAgent = AgentFactory.CreateAgent(
                chatClient,
                name: "GameMasterAgent",
                instructions: "You are a tabletop game master. Use the dice and coin tools " +
                    "available to you whenever a request depends on a random outcome - never " +
                    "make up a roll or a coin flip yourself.",
                tools: [.. mcpTools.Cast<AITool>()]);

            Console.WriteLine("=== Prompt 1: roll dice ===");
            AgentResponse diceResponse = await gameMasterAgent.RunAsync(
                "Roll two six-sided dice for our game and tell me the total.");
            Console.WriteLine(diceResponse.Text);

            Console.WriteLine();
            Console.WriteLine("=== Prompt 2: flip a coin ===");
            AgentResponse coinResponse = await gameMasterAgent.RunAsync(
                "Flip a coin to decide who goes first.");
            Console.WriteLine(coinResponse.Text);
        }
    }
}
```

## Walkthrough

1. **The server declares its tools with attributes, not manual registration.** `[McpServerToolType]` on `DiceTools` marks it as a tool container; `[McpServerTool]` plus `[Description]` on each method is enough for `WithToolsFromAssembly()` to find and advertise both methods via reflection at startup. No code anywhere lists `RollDice` and `FlipCoin` by name — the assembly scan does that.
2. **The server's `Program.cs` is a generic host, not a `ChatClientAgent`.** `Host.CreateApplicationBuilder(args)` plus `AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` builds and runs a small ASP.NET Core-style host whose only job is to speak MCP over stdin/stdout. There is no model provider, no `IChatClient`, and no agent anywhere in this project — an MCP server is model-agnostic by design.
3. **The client locates and launches the server's built DLL.** `McpServerConnectionFactory.ConnectAsync()` computes the sibling project's output path, checks the DLL actually exists (with a clear error pointing at the fix if not), and passes `Command = "dotnet"`, `Arguments = [serverDllPath]` into `StdioClientTransport`. `McpClient.CreateAsync(...)` then starts that process and performs the MCP handshake over its stdio streams.
4. **`ListToolsAsync()` is the dynamic-discovery step.** `Program.cs` calls it and prints exactly what came back — this is the line to watch when running the lesson: the tool names logged (`roll_dice`, `flip_coin`) come from the server's own advertisement, not from any list written in this project.
5. **Casting to `AITool` is all the "conversion" that's needed.** `tools: [.. mcpTools.Cast<AITool>()]` is the entire bridge between "tools discovered over a network protocol" and "tools a `ChatClientAgent` can call" — because `McpClientTool` already inherits from `AIFunction`, there's no manual JSON-schema wiring or invocation glue to write.
6. **From here, it's the ordinary Lesson 2 function-calling loop.** Once `gameMasterAgent` is built, `RunAsync` behaves exactly as it did with local tools: the model decides to call `roll_dice` or `flip_coin`, `ChatClientAgent`'s built-in `FunctionInvokingChatClient` executes the call — which, transparently, round-trips over stdio to the subprocess — and feeds the result back in to produce the final answer.
7. **`await using` on `mcpClient` ensures cleanup.** When `Main` finishes (or throws), the `McpClient`'s `DisposeAsync` shuts down the stdio connection and the subprocess along with it, so no orphaned `dotnet` process is left running after the lesson exits.

## Expected Output

```
Discovered 2 tool(s) from the MCP server: flip_coin, roll_dice

=== Prompt 1: roll dice ===
You rolled a 1 and a 3, for a total of 4.

=== Prompt 2: flip a coin ===
The coin landed on Heads. You go first!
```

The dice values and coin result come from `Random` in the live server process, so the exact numbers and the heads/tails outcome will vary between runs — what won't vary is the shape: two tools discovered by name, and both prompts answered by an actual tool call rather than the model inventing a number itself.
