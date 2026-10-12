# Skills 01 — Simple Skill

## Project Overview

In this lesson, a `CodeReviewAgent` is equipped with a single **Agent Skill** — a portable package
of instructions and reference material, described by a `SKILL.md` file, that the agent discovers
and loads on demand rather than carrying in its base instructions all the time. This is the
smallest possible skill: one skill, no scripts, just instructions plus a bundled reference
document. It matters because packaged domain expertise (a review checklist, a policy, a style
guide) can be authored once, shipped as a folder, and reused across any agent that needs it —
without bloating that agent's system prompt or context window every single run.

## Prerequisites

This lesson is standalone and runnable on its own, but it builds on ideas from two earlier lessons:
Lesson 5 (Context Providers) — an `AgentSkillsProvider` is itself an `AIContextProvider`, wired in
exactly the same way the semantic-memory provider was — and Lesson 9 (Approvals) — the skill tools
this lesson calls (`load_skill`, `read_skill_resource`) require approval by default, resolved with
the same `ToolApprovalRequestContent` / `ToolApprovalResponseContent` exchange Lesson 9 introduced.

## Setup

**Target framework:** `net10.0`

**NuGet packages:**

| Package | Version | Purpose |
|---|---|---|
| `Microsoft.Agents.AI` | 1.24.0 | Core MAF package — `ChatClientAgent`, `AgentSkillsProvider`, etc. |
| `Microsoft.Extensions.AI.OpenAI` | 10.10.1 | First-party `IChatClient` adapter over the OpenAI SDK's `ChatClient` |
| `OpenAI` | 2.14.0 | Official OpenAI .NET SDK |

No separate skills package is required — `AgentSkillsProvider`, file-based sources, and the
`load_skill`/`read_skill_resource`/`run_skill_script` tools all ship inside `Microsoft.Agents.AI`
itself, with no `[Experimental]` gate. Only the MCP-based skill source needs a separate package
(`Microsoft.Agents.AI.Mcp`, an alpha prerelease — latest `1.24.0-alpha.261006.1`) — not used in
this lesson.

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

**Skill folder:** the project ships a `skills/code-review-checklist/` folder containing
`SKILL.md` and `references/checklist.md`. The `.csproj` copies this folder to the build output
directory (`CopyToOutputDirectory`) because the provider looks for it relative to
`AppContext.BaseDirectory` at runtime — if you add or edit skill content, rebuild before running.

## Core Concepts

- **A skill is a directory, not a class.** At minimum it's just `SKILL.md`: YAML frontmatter
  (`name`, `description` required; `license`, `compatibility`, `metadata` optional) followed by a
  markdown body containing the actual instructions. This lesson's skill also has a
  `references/checklist.md` file — supplementary content the agent fetches only if it decides it
  needs it.
- **`name` and `description` are the only two fields that matter for discovery.** Every skill's
  `name`/`description` pair is injected into the system prompt up front (~100 tokens per skill) so
  the agent knows what's available before it decides to use anything. Write the `description` the
  way you'd write a tool description: what it does *and* when to use it.
- **Progressive disclosure keeps the context window lean.** The agent doesn't receive the full
  `SKILL.md` body until it calls `load_skill`. It doesn't receive `checklist.md` until it calls
  `read_skill_resource`. A skill with ten reference documents costs nothing extra in context until
  the agent actually reaches for one of them.
- **`AgentSkillsProvider` is a context provider, wired in exactly like Lesson 5's memory provider.**
  `new AgentSkillsProvider(path)` scans the given directory for `SKILL.md` files (one level per
  subdirectory) and registers the skill tools on the agent (`load_skill` and `read_skill_resource`; `run_skill_script` only when a skill ships scripts). It plugs into
  `AgentFactory.CreateAgentWithContextProviders` unchanged — no new factory method was needed.
- **`load_skill` and `read_skill_resource` require approval by default — same as any gated tool.**
  This isn't a simplification for the lesson; it's the framework's actual default. A production
  agent would typically auto-approve these two read-only calls (Skills 2 shows how) — this lesson
  keeps every call manual so the full mechanic is visible once, end to end.

> **Coming from SK:** Semantic Kernel has no direct equivalent to Agent Skills. This is a new,
> framework-agnostic packaging format (the open [agentskills.io](https://agentskills.io)
> specification) that MAF adopted rather than something ported from SK's plugin/`KernelFunction`
> model. Because the format is open, a skill folder authored for a different Agent
> Skills-compatible product — including Claude and Claude Code — can often be dropped into a
> MAF project's `skills/` directory and consumed as-is, with no conversion step.

## Complete C# Code

**`src/SimpleSkill/ChatClientFactory.cs`**

```csharp
using System.ClientModel;
using Microsoft.Extensions.AI;
using OpenAI;

namespace SimpleSkill
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

**`src/SimpleSkill/AgentFactory.cs`**

```csharp
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SimpleSkill
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

**`src/SimpleSkill/Program.cs`**

```csharp
// Skills 01 - Simple Skill (Agent Skills, file-based, no scripts)
// Loads a single file-based Agent Skill - code-review-checklist - built from a SKILL.md file and
// one bundled reference document, and walks the agent through the full progressive-disclosure
// flow: advertise -> load_skill -> read_skill_resource. Both of those tools require approval by
// default, resolved here with the same ToolApprovalRequestContent / ToolApprovalResponseContent
// exchange Lesson 9 introduced for local function tools.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace SimpleSkill
{
    /// <summary>
    /// Entry point that builds a code-review agent equipped with a single file-based skill, asks
    /// it to review a snippet, and approves every pending skill-tool call along the way.
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
        /// Builds the skills-equipped agent, submits one review prompt, and drives the approval
        /// loop to completion before printing the agent's final review.
        /// </summary>
        private static async Task Main(string[] args)
        {
            IChatClient chatClient = ChatClientFactory.Create();

            AgentSkillsProvider skillsProvider = new(
                Path.Combine(AppContext.BaseDirectory, "skills"));

            ChatClientAgent reviewAgent = AgentFactory.CreateAgentWithContextProviders(
                chatClient,
                name: "CodeReviewAgent",
                instructions: "You are a C# code reviewer. Use the available skills when they match the task.",
                contextProviders: [skillsProvider]);

            AgentSession session = await reviewAgent.CreateSessionAsync();
            AgentResponse response = await reviewAgent.RunAsync(
                $"Review this method against our code review checklist:\n\n{CodeSnippet}", session);

            response = await ApproveAllPendingAsync(reviewAgent, session, response);

            Console.WriteLine(response.Text);
        }

        /// <summary>
        /// Repeatedly collects any pending <see cref="ToolApprovalRequestContent"/> items from the
        /// given response, approves each one, and resumes the same session - looping until the
        /// agent stops asking for approval and returns a final answer.
        /// </summary>
        private static async Task<AgentResponse> ApproveAllPendingAsync(
            ChatClientAgent agent, AgentSession session, AgentResponse response)
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

**`src/SimpleSkill/skills/code-review-checklist/SKILL.md`**

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

**`src/SimpleSkill/skills/code-review-checklist/references/checklist.md`**

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

1. **The provider scans the `skills/` folder at construction.** `new AgentSkillsProvider(path)`
   discovers `code-review-checklist/SKILL.md`, parses its frontmatter, and registers `load_skill`
   and `read_skill_resource` on the agent (`run_skill_script` is *not* registered — this skill has
   no scripts, so the tool isn't even advertised).
2. **The skill is wired in as a context provider, not a tool list.** `AgentFactory.CreateAgentWithContextProviders`
   takes the same `IList<AIContextProvider>` shape Lesson 5 used for the semantic-memory provider —
   `AgentSkillsProvider` slots into that list unchanged.
3. **The first `RunAsync` call almost certainly pauses on `load_skill`.** The model sees
   `code-review-checklist`'s name/description in the system prompt, matches it to the review
   request, and calls `load_skill` to retrieve the full `SKILL.md` body — which pauses for
   approval before it runs.
4. **`ApproveAllPendingAsync` loops instead of handling one request.** Unlike Lesson 9's single
   gated tool, this lesson doesn't know in advance how many approval rounds will occur (`load_skill`
   first, then likely `read_skill_resource` in a second round) — the `while (pending.Count > 0)`
   loop keeps resolving and resubmitting until the agent produces a final answer with none pending.
5. **Each decision reuses `CreateResponse` and resends on the same session**, exactly like Lesson 9 —
   the only difference is batching potentially multiple `ToolApprovalResponseContent` items into
   one `List<ChatMessage>` per round, since the model may request more than one approval at once.
6. **The final `response.Text` reflects the checklist content the agent actually read** — the
   review call out `async void` and the empty `catch` block specifically, because those are the
   items `checklist.md` calls out, not because the model already knew this project's conventions.

## Expected Output

```
Approval requested to call 'load_skill'.
Decision: APPROVED
Approval requested to call 'read_skill_resource'.
Decision: APPROVED
- **Async naming and safety**: Fail - `SaveOrder` is declared `async void` instead of returning
  `Task`, so callers cannot await it or observe exceptions.
- **Exception handling**: Fail - the `catch (Exception)` block is empty; the exception is
  silently swallowed instead of being logged or rethrown.
- **Nullability**: Pass - no nullable reference types are involved in this snippet.
- **Naming conventions**: Pass - `SaveOrder` and `order` follow PascalCase/camelCase correctly.
- **Single responsibility**: Pass - the method does one thing (save an order).

Overall verdict: Needs changes.
```

The exact wording and the number of approval rounds may vary slightly between runs — the model
decides whether to call `load_skill` and `read_skill_resource` in the same turn or across two
separate turns — but the shape (approval requests printed and approved, followed by a checklist-
grounded review ending in an explicit verdict) will not.
