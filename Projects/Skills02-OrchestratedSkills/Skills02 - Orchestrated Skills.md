# Skills 02 — Orchestrated Skills

## Project Overview

This lesson equips one agent with **three Agent Skills authored three different ways** — a
file-based skill (a `SKILL.md` folder), a class-based skill (a C# class with attributed members),
and an inline/code-defined skill (built directly in code with a dynamic resource) — composed into
a single pipeline with `AgentSkillsProviderBuilder`. It matters because real applications rarely
get their skills from one place: some come from a shared folder your team maintains, some are
distributed as NuGet packages, and some need to be generated from application state at runtime.
This lesson also shows how to selectively automate the approval gate from Skills 1, so read-only
skill lookups stop interrupting the agent while script execution still does.

## Prerequisites

Assumes the Skills 01 lesson (the `SKILL.md` format, progressive disclosure, and the basic
approval loop) plus Lesson 9 (Approvals). This project is otherwise standalone — it does not
reference Skills 01's code and ships its own copy of the `code-review-checklist` skill folder.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `AgentSkillsProviderBuilder`, `AgentClassSkill<T>`, `AgentInlineSkill`, `AIAgentBuilder.UseToolApproval`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No separate skills package is required for any of the three sources used here. (Only an
MCP-based skill source would need `Microsoft.Agents.AI.Mcp`, an alpha prerelease — latest
`1.24.0-alpha.261006.1` — not used in this lesson.)

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

**Skill folder:** the project ships its own `skills/code-review-checklist/` folder (copied to the
build output directory via the `.csproj`), used as the file-based source in the composed pipeline.

## Core Concepts

- **`AgentSkillsProviderBuilder` composes sources, it doesn't replace them.** `.UseFileSkill(path)`
  adds a file-based source; `.UseSkill(...)` adds either a class-based (`AgentClassSkill<T>`) or
  inline (`AgentInlineSkill`) skill instance; `.Build()` aggregates all of them (with automatic
  deduplication and caching) into one `AgentSkillsProvider`. The agent sees one merged skill list —
  it has no idea which source any given skill came from.
- **A class-based skill packages everything a file-based skill has, in code.** `UnitConverterSkill`
  derives from `AgentClassSkill<UnitConverterSkill>`, overrides `Frontmatter`/`Instructions`, and
  marks a property `[AgentSkillResource("conversion-table")]` and a method
  `[AgentSkillScript("convert")]`. This is the shape teams use to ship a skill as a NuGet package —
  `dotnet add package`, then one `.UseSkill(new TheirSkill())` call.
- **An inline skill is built with plain method calls, not attributes.** `ProjectInfoSkillFactory`
  constructs an `AgentInlineSkill` and calls `.AddResource("environment", () => ...)` with a
  factory delegate — useful when a skill needs to close over runtime state (a database, a config
  value, a per-session value) rather than serve static content.
- **A file script runner decides how a file-based skill's scripts execute.** A file-based skill
  can ship executable files in a `scripts/` folder (Python, JavaScript, bash, PowerShell). The
  builder has no built-in way to run them, so any pipeline with a file-based source registers a
  runner with `.UseFileScriptRunner(...)` — `Build()` throws without one. This lesson's
  `SubprocessScriptRunner` starts each script as a local process, picks the interpreter from the
  extension, passes the model's JSON-array arguments as command-line arguments, and returns
  stdout, stderr and the exit code. Class-based and inline scripts (`UnitConverterSkill.Convert`)
  are C# delegates, so calling them is just a method call and never touches the runner. The
  `code-review-checklist` file skill has no `scripts/` folder, so the runner is registered but
  never invoked in this demo — it is in place for the first file skill that ships a script.
- **`.UseFilter(...)` lets a provider exclude skills without removing them from the source.** This
  lesson's filter (`skill.Frontmatter.Name != "experimental-skill"`) is a deliberate no-op — none
  of the three skills are named that — included to show the mechanism: filtering by name,
  metadata, or any other predicate over the composed set before it reaches the agent.
- **Approval can be automated per tool, not just per call.** `AIAgentBuilder.UseToolApproval(new
  ToolApprovalAgentOptions { AutoApprovalRules = [AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule] })`
  wraps the agent so `load_skill`/`read_skill_resource` never pause, while `run_skill_script` still
  does. This is the layered policy Lesson 9 previewed (`AIAgentBuilder.UseToolApproval(...)` with
  `AutoApprovalRules`) now shown for real, applied specifically to skill tools.

> **Coming from SK:** as in Skills 01, Semantic Kernel has no direct equivalent to Agent Skills —
> this composition model is new to MAF, not a port of SK's plugin system. The practical takeaway
> for anyone migrating a large SK plugin collection: `AgentSkillsProviderBuilder` is the pattern
> that lets a team ship each skill independently (as its own file-based folder or its own NuGet
> package) and combine them per-agent with one `.UseFileSkill(...)`/`.UseSkill(...)` call each,
> rather than registering every plugin function on every agent up front.

## Complete C# Code

**`src/OrchestratedSkills/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace OrchestratedSkills
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

**`src/OrchestratedSkills/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// Reusable helper for constructing named, persona-scoped <see cref="ChatClientAgent"/>
    /// instances. This factory shape is reintroduced across lessons so that assembling the
    /// Lesson 22-24 capstone is mostly composition.
    /// </summary>
    internal static class AgentFactory
    {
        /// <summary>
        /// Wraps the given chat client in a ChatClientAgent configured with a name, system
        /// instructions, and one or more AIContextProvider instances that inject additional
        /// context - memory, retrieved documents, or in this lesson, Agent Skills - into every run.
        /// </summary>
        internal static ChatClientAgent CreateAgentWithContextProviders(
            IChatClient chatClient,
            string name,
            string instructions,
            IList<AIContextProvider> contextProviders)
        {
            ChatClientAgentOptions options = new()
            {
                Name = name,
                ChatOptions = new ChatOptions { Instructions = instructions },
                AIContextProviders = contextProviders,
            };

            return new ChatClientAgent(chatClient, options);
        }
    }
}
```

**`src/OrchestratedSkills/UnitConverterSkill.cs`**

```csharp
using System.ComponentModel;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// A class-based Agent Skill that converts between common units using a lookup table of
    /// multiplication factors. Packaging it as a class - rather than a SKILL.md folder - shows
    /// how a skill can ship inside a normal C# assembly, and eventually a NuGet package, with its
    /// resource and script bundled alongside it as attributed members.
    /// </summary>
    internal sealed class UnitConverterSkill : AgentClassSkill<UnitConverterSkill>
    {
        /// <summary>
        /// The name and description advertised to the agent before the skill is loaded.
        /// </summary>
        public override AgentSkillFrontmatter Frontmatter { get; } = new(
            "unit-converter",
            "Convert between common units (miles, kilometers, pounds, kilograms) using a " +
                "multiplication factor. Use when asked to convert distances or weights between these units.");

        /// <summary>
        /// The step-by-step instructions the agent receives after calling load_skill for this skill.
        /// </summary>
        protected override string Instructions => """
            Use this skill when the user asks to convert between miles, kilometers, pounds, or kilograms.

            1. Review the conversion-table resource to find the correct factor for the requested units.
            2. Call the convert script, passing the value and factor from the table.
            3. Present the result clearly, showing both the original and converted values with their units.
            """;

        /// <summary>
        /// Lookup table of multiplication factors for the unit pairs this skill supports.
        /// </summary>
        [AgentSkillResource("conversion-table")]
        [Description("Lookup table of multiplication factors for common unit conversions.")]
        public string ConversionTable => """
            # Conversion Table
            Formula: result = value x factor

            | From       | To         | Factor   |
            |------------|------------|----------|
            | miles      | kilometers | 1.60934  |
            | kilometers | miles      | 0.621371 |
            | pounds     | kilograms  | 0.453592 |
            | kilograms  | pounds     | 2.20462  |
            """;

        /// <summary>
        /// Multiplies a value by a conversion factor and returns the result as JSON. Runs
        /// in-process as a direct delegate call - no external script runner is needed.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <param name="factor">The multiplication factor from the conversion table.</param>
        [AgentSkillScript("convert")]
        [Description("Multiplies a value by a conversion factor and returns the result as JSON.")]
        private static string Convert(double value, double factor)
        {
            double result = Math.Round(value * factor, 4);
            return JsonSerializer.Serialize(new { value, factor, result });
        }
    }
}
```

**`src/OrchestratedSkills/ProjectInfoSkillFactory.cs`**

```csharp
using Microsoft.Agents.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// Builds an inline, code-defined Agent Skill that reports the current process's runtime
    /// environment. Its resource is computed at read time rather than served from a static file,
    /// showing the third way to author a skill alongside the file-based and class-based skills
    /// also composed into this lesson's provider.
    /// </summary>
    internal static class ProjectInfoSkillFactory
    {
        /// <summary>
        /// Creates the "project-info" inline skill, with a dynamic "environment" resource that
        /// re-reads its environment variables every time the agent calls read_skill_resource.
        /// </summary>
        internal static AgentInlineSkill Create()
        {
            return new AgentInlineSkill(
                name: "project-info",
                description: "Reports the current build configuration and target framework this " +
                    "tutorial series runs on. Use when asked what environment or runtime this project targets.",
                instructions: """
                    Use this skill when the user asks about the runtime environment this project targets.
                    1. Read the environment resource for the current configuration details.
                    2. Answer using only the values from that resource - do not guess.
                    """)
                .AddResource("environment", () =>
                {
                    string configuration = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Development";
                    return $"Target framework: net10.0, Configuration: {configuration}, Machine: {Environment.MachineName}";
                });
        }
    }
}
```

**`src/OrchestratedSkills/SubprocessScriptRunner.cs`**

```csharp
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Agents.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// Runs a file-based skill's scripts (the files in its <c>scripts/</c> folder) as local
    /// subprocesses. The script's extension picks the interpreter, the JSON array of arguments the
    /// model sends becomes positional command-line arguments, and the script's standard output,
    /// standard error and exit code come back as the tool result. Class-based and inline skill
    /// scripts are C# delegates and never pass through this runner.
    /// </summary>
    internal static class SubprocessScriptRunner
    {
        /// <summary>
        /// Starts the script in its own folder with the interpreter that matches its extension
        /// (.py python3, .js node, .sh bash, .ps1 pwsh; anything else is started directly), waits
        /// for it to exit, and returns its standard output followed by any standard error and a
        /// non-zero exit code. Matches the <see cref="AgentFileSkillScriptRunner"/> delegate so it
        /// can be passed to <c>UseFileScriptRunner</c>.
        /// </summary>
        public static async Task<object?> RunAsync(
            AgentFileSkill skill,
            AgentFileSkillScript script,
            JsonElement? arguments,
            IServiceProvider? serviceProvider,
            CancellationToken cancellationToken)
        {
            if (!File.Exists(script.FullPath))
            {
                return $"Error: Script file not found: {script.FullPath}";
            }

            string? interpreter = Path.GetExtension(script.FullPath) switch
            {
                ".py" => "python3",
                ".js" => "node",
                ".sh" => "bash",
                ".ps1" => "pwsh",
                _ => null,
            };

            ProcessStartInfo startInfo = new()
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(script.FullPath) ?? ".",
            };

            if (interpreter is not null)
            {
                startInfo.FileName = interpreter;
                startInfo.ArgumentList.Add(script.FullPath);
            }
            else
            {
                startInfo.FileName = script.FullPath;
            }

            if (arguments is { ValueKind: JsonValueKind.Array } argumentArray)
            {
                foreach (JsonElement element in argumentArray.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.String)
                    {
                        throw new InvalidOperationException(
                            $"Script arguments must be JSON strings, but received a {element.ValueKind}.");
                    }

                    startInfo.ArgumentList.Add(element.GetString()!);
                }
            }
            else if (arguments is { ValueKind: not JsonValueKind.Null and not JsonValueKind.Undefined } other)
            {
                throw new InvalidOperationException(
                    $"Script arguments must be a JSON array of strings, but received a {other.ValueKind}.");
            }

            Process? process = null;
            try
            {
                process = Process.Start(startInfo);
                if (process is null)
                {
                    return $"Error: Failed to start a process for script '{script.Name}'.";
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                Task<string> errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);

                string output = await outputTask.ConfigureAwait(false);
                string error = await errorTask.ConfigureAwait(false);

                if (!string.IsNullOrEmpty(error))
                {
                    output += $"\nStderr:\n{error}";
                }

                if (process.ExitCode != 0)
                {
                    output += $"\nScript exited with code {process.ExitCode}";
                }

                return string.IsNullOrEmpty(output) ? "(no output)" : output.Trim();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                process?.Kill(entireProcessTree: true);
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return $"Error: Failed to run script '{script.Name}': {ex.Message}";
            }
            finally
            {
                process?.Dispose();
            }
        }
    }
}
```

**`src/OrchestratedSkills/Program.cs`**

```csharp
// Skills 02 - Orchestrated Skills (Multi-Source Agent Skills)
// Composes three Agent Skills of different authoring styles - file-based, class-based, and
// inline/code-defined - into a single AgentSkillsProviderBuilder pipeline on one agent, then
// selectively automates approval: load_skill and read_skill_resource auto-approve via
// AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule, while run_skill_script still pauses for an
// explicit decision, exactly like Lesson 9's ToolApprovalRequestContent / ToolApprovalResponseContent
// exchange.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace OrchestratedSkills
{
    /// <summary>
    /// Entry point that builds an agent backed by three composed skills, then runs one prompt
    /// against the auto-approved file-based skill and one prompt against the still-gated
    /// class-based skill's script.
    /// </summary>
    internal sealed class Program
    {
        private static readonly string CodeSnippet = """
            public async void SaveOrder(Order order)
            {
                try
                {
                    await _repository.SaveAsync(order);
                }
                catch (Exception)
                {
                }
            }
            """;

        /// <summary>
        /// Builds the multi-source skills provider, wraps the agent with selective tool-approval
        /// automation, and runs the two demonstration prompts in turn.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            AgentSkillsProvider skillsProvider = new AgentSkillsProviderBuilder()
                .UseFileSkill(Path.Combine(AppContext.BaseDirectory, "skills"))
                .UseFileScriptRunner(SubprocessScriptRunner.RunAsync)
                .UseSkill(new UnitConverterSkill())
                .UseSkill(ProjectInfoSkillFactory.Create())
                .UseFilter((skill, context) => skill.Frontmatter.Name != "experimental-skill")
                .Build();

            ChatClientAgent baseAgent = AgentFactory.CreateAgentWithContextProviders(
                chatClient,
                name: "SkillsAgent",
                instructions: "You are a helpful assistant. Use the available skills when they match the task.",
                contextProviders: [skillsProvider]);

            AIAgent skillsAgent = baseAgent
                .AsBuilder()
                .UseToolApproval(new ToolApprovalAgentOptions
                {
                    // Auto-approve read-only skill tools (load_skill, read_skill_resource).
                    // run_skill_script still requires an explicit decision below.
                    AutoApprovalRules = [AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule],
                })
                .Build();

            AgentSession session = await skillsAgent.CreateSessionAsync();

            Console.WriteLine("=== Prompt 1: file-based skill (read-only, auto-approved) ===");
            AgentResponse checklistResponse = await skillsAgent.RunAsync(
                $"Review this method against our code review checklist:\n\n{CodeSnippet}", session);
            checklistResponse = await ApproveAllPendingAsync(skillsAgent, session, checklistResponse);
            Console.WriteLine(checklistResponse.Text);

            Console.WriteLine();
            Console.WriteLine("=== Prompt 2: class-based skill script (still requires approval) ===");
            AgentResponse conversionResponse = await skillsAgent.RunAsync(
                "Convert 26.2 miles to kilometers using the unit-converter skill.", session);
            conversionResponse = await ApproveAllPendingAsync(skillsAgent, session, conversionResponse);
            Console.WriteLine(conversionResponse.Text);
        }

        /// <summary>
        /// Repeatedly collects any pending <see cref="ToolApprovalRequestContent"/> items from the
        /// given response, approves each one, and resumes the same session - looping until the
        /// agent stops asking for approval and returns a final answer. For this lesson's first
        /// prompt, the auto-approval rule means this loop finds nothing pending at all.
        /// </summary>
        private static async Task<AgentResponse> ApproveAllPendingAsync(
            AIAgent agent, AgentSession session, AgentResponse response)
        {
            List<ToolApprovalRequestContent> pending = CollectApprovalRequests(response);

            while (pending.Count > 0)
            {
                List<ChatMessage> decisions = [];
                foreach (ToolApprovalRequestContent request in pending)
                {
                    if (request.ToolCall is FunctionCallContent functionCall)
                    {
                        Console.WriteLine($"Approval requested to call '{functionCall.Name}'.");
                    }

                    Console.WriteLine("Decision: APPROVED");
                    decisions.Add(new ChatMessage(
                        ChatRole.User,
                        [request.CreateResponse(true, reason: "Approved by reviewer.")]));
                }

                response = await agent.RunAsync(decisions, session);
                pending = CollectApprovalRequests(response);
            }

            return response;
        }

        /// <summary>
        /// Extracts every pending tool approval request from a response's messages.
        /// </summary>
        private static List<ToolApprovalRequestContent> CollectApprovalRequests(AgentResponse response)
        {
            return response.Messages
                .SelectMany(message => message.Contents)
                .OfType<ToolApprovalRequestContent>()
                .ToList();
        }
    }
}
```

**`src/OrchestratedSkills/skills/code-review-checklist/SKILL.md`**

```markdown
---
name: code-review-checklist
description: Review C# code against a short internal checklist covering async safety, exception handling, nullability, naming, and single responsibility. Use when asked to review, critique, or check C# code snippets or pull requests.
license: MIT
metadata:
  author: maf-tutorial-series
  version: "1.0"
---

# Code Review Checklist Skill

Use this skill whenever the user asks you to review, critique, or check a piece of C# code.

1. Read the `checklist` resource for the full list of review criteria.
2. Compare the supplied code against every item on the checklist, one at a time.
3. For each item, report **Pass** or **Fail** with a one-sentence justification pointing at the
   specific line or construct.
4. Finish with a one-sentence overall verdict: **Ready to merge** or **Needs changes**.

Do not invent checklist items that are not in the resource, and do not skip an item just because
the code looks fine at a glance.
```

**`src/OrchestratedSkills/skills/code-review-checklist/references/checklist.md`**

```markdown
# C# Code Review Checklist

- **Async naming and safety** - async methods end in `Async`, return `Task`/`Task<T>` (never
  `async void` except for event handlers), and every awaitable is actually awaited.
- **Exception handling** - no empty `catch` blocks; caught exceptions are logged, rethrown, or
  otherwise acted on, never silently swallowed.
- **Nullability** - reference types that can be null are annotated `?`, and callers check before
  dereferencing.
- **Naming conventions** - types and public members use PascalCase; locals and parameters use
  camelCase.
- **Single responsibility** - each method does one clearly describable thing; extract a helper if
  a method mixes unrelated concerns.
```

## Walkthrough

1. **The builder composes three heterogeneous sources into one provider.** `.UseFileSkill(...)`
   adds `code-review-checklist`; `.UseSkill(new UnitConverterSkill())` adds the class-based skill;
   `.UseSkill(ProjectInfoSkillFactory.Create())` adds the inline skill. `.UseFilter(...)` runs over
   the aggregated set (a deliberate no-op here) before `.Build()` wraps everything in caching and
   deduplication.
2. **`.UseFileScriptRunner(SubprocessScriptRunner.RunAsync)` tells the builder how file-based
   scripts run.** The file-based source makes the runner mandatory. `RunAsync` matches the
   `AgentFileSkillScriptRunner` delegate: it maps the script's extension to an interpreter, starts
   the process in the script's folder with the model's arguments, and hands back the output. The
   checklist skill has no scripts, so nothing in this run reaches it; `UnitConverterSkill`'s
   `convert` script is a C# method and runs in-process instead.
3. **The agent is built exactly like Skills 01**, via `AgentFactory.CreateAgentWithContextProviders`
   — the multi-source provider slots into the same single-item `AIContextProviders` list a
   single-source provider would.
4. **`.AsBuilder().UseToolApproval(...)` is layered on *after* the agent exists.** This mirrors the
   note Lesson 9 left for later: `AIAgentBuilder.UseToolApproval(...)` with `AutoApprovalRules` adds a
   policy layer on top of the same `ToolApprovalRequestContent`/`ToolApprovalResponseContent`
   primitive, rather than requiring you to hand-roll auto-approval logic yourself.
5. **Prompt 1 produces no console approval output at all.** `load_skill` and `read_skill_resource`
   both match `AgentSkillsProvider.ReadOnlyToolsAutoApprovalRule`, so they execute immediately —
   `ApproveAllPendingAsync`'s `while` loop finds zero pending requests and returns the final answer
   on the first call.
6. **Prompt 2 still pauses — exactly once, for `run_skill_script`.** The rule only covers the two
   read-only tools; asking the agent to actually run `UnitConverterSkill`'s `convert` script
   triggers a real approval round, visible in the console output, before the numeric result comes
   back.
7. **Both skills came from different sources, but the agent never distinguished between them.**
   The file-based checklist skill and the class-based converter skill were invoked with identical
   mechanics from the model's point of view — the difference only shows up in how *you* authored
   and registered each one.

## Expected Output

```
=== Prompt 1: file-based skill (read-only, auto-approved) ===
- **Async naming and safety**: Fail - `SaveOrder` is declared `async void` instead of returning
  `Task`, so callers cannot await it or observe exceptions.
- **Exception handling**: Fail - the `catch (Exception)` block is empty; the exception is
  silently swallowed instead of being logged or rethrown.
- **Nullability**: Pass - no nullable reference types are involved in this snippet.
- **Naming conventions**: Pass - `SaveOrder` and `order` follow PascalCase/camelCase correctly.
- **Single responsibility**: Pass - the method does one thing (save an order).

Overall verdict: Needs changes.

=== Prompt 2: class-based skill script (still requires approval) ===
Approval requested to call 'run_skill_script'.
Decision: APPROVED
26.2 miles is approximately 42.1846 kilometers.
```

The exact wording may vary between runs, and the model may occasionally batch `load_skill` and
`read_skill_resource` differently across the two prompts - but Prompt 1 will not print any
approval lines, and Prompt 2 will print exactly one, demonstrating the selective automation.
