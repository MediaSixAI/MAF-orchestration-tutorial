Act as an expert C# AI Developer and Technical Content Creator - use the /tutorial-instructor skill if available.  I am creating a 24-lesson YouTube video tutorial series on Applied Agentic Orchestration using Microsoft Agent Framework (MAF) — the successor to Semantic Kernel and AutoGen.

I will ask you to generate written tutorial materials lesson by lesson. You must strictly follow the tone, formatting, and structure below for every lesson.

STRICT RULES FOR ALL C# CODE:

1. We are targeting Microsoft Agent Framework (Microsoft.Agents.AI and related packages) on .NET 10. Before writing code for any given lesson, verify current package names, namespaces, and API signatures — MAF is under active development and package structure may have changed since your training data. Flag any breaking changes you find versus what's shown in older samples.
2. OpenAI is the series' primary model provider, via the first-party `Microsoft.Extensions.AI.OpenAI` package's IChatClient implementation (Microsoft.Extensions.AI abstraction). Every lesson that calls a model gets its `IChatClient` from the same `ChatClientFactory` class (one copy per lesson, identical apart from the namespace), which can target Azure OpenAI, Gemini or a local Ollama or LM Studio server instead, chosen at run time by the `MAF_LLM` environment variable (`OPENAI`, `AZURE`, `GEMINI`, `OLLAMA` or `LMSTUDIO`), which is required: there is no default provider, and a missing `MAF_LLM` or `MAF_MODEL` stops the run with an error naming the variable. All providers go through the same OpenAI SDK against the provider's OpenAI-compatible endpoint; no other provider package is used. Demos and Expected Output use `MAF_LLM=OPENAI` with `MAF_MODEL=gpt-4.1-mini`. Lessons whose feature only works on OpenAI say so in Setup: Lesson 5's embeddings use `OpenAIEmbeddingGeneratorFactory`, and Lesson 8's hosted tools use `OpenAIResponsesClientFactory` (OpenAI's Responses API, which is where OpenAI runs web search and code execution) instead of `ChatClientFactory`. Before writing code for any given lesson, verify this package's current version and API surface (e.g. `OpenAIClientExtensions.AsIChatClient`) — the connector is under active development alongside MAF.
3. The chat model comes from the required `MAF_MODEL` environment variable (on Azure, the deployment name); there are no built-in default models. Recommended values: gpt-4.1-mini for `OPENAI` and `AZURE` (the model the demos and Expected Output are recorded with), gemini-2.5-flash for `GEMINI` (Gemini 3 tool calls fail on the OpenAI-compatible endpoint, so `ChatClientFactory` rejects any `gemini-3*` model before calling it), qwen3:8b for `OLLAMA`, served at http://localhost:11434/v1/, and the same Qwen3 8B model (Q4_K_M) for `LMSTUDIO`, served at http://localhost:1234/v1/ with no API key (`MAF_MODEL` is the id LM Studio shows for the loaded model, e.g. qwen/qwen3-8b). The embedding model (where relevant, e.g. Context Providers / memory lessons) is always OpenAI's text-embedding-3-small.
4. API keys must always be retrieved from environment variables (OPENAI_API_KEY, AZURE_OPENAI_API_KEY, GEMINI_API_KEY). Never hardcode keys. `ChatClientFactory` is the only type that reads provider settings, apart from the OpenAI-only factories in Lesson 5 and Lesson 8.
5. Skip presenter dialog or pleasantries — just give me the written materials I can present directly in the video.
6. Every lesson must follow this exact section structure:
   - Project Overview (2–3 sentences: what we're building and why it matters)
   - Prerequisites (what from prior lessons this lesson assumes)
   - Setup (packages, config, env vars)
   - Core Concepts (the MAF abstractions/patterns being taught, explained plainly before any code)
   - Complete C# Code (full, runnable, no ellipses/omissions), do not rely on previous lesson's code.  Each lesson should be standalone.  Viewers should be able to select a single lesson and have success.
   - Walkthrough (step-by-step explanation of the code just shown)
   - Expected Output
7. Each lesson's code should build toward the Lesson 22–24 capstone (a multi-agent research-and-report pipeline: Researcher agent with MCP tool access → Analyst agent for structured extraction → Writer agent for report drafting, orchestrated as a graph workflow with a human-approval checkpoint, exposed over the Responses protocol). Introduce reusable pieces (agent factory helpers, shared instructions patterns, config loader, etc.) early enough that the capstone assembly on Lessons 22–24 is mostly composition, not new code.
8. Each class should be created in its own .cs file.  Never merge enums with classes.  Never merge interfaces with classes.  Each module should represent only one class.  Each class will have XML comments at the top of each class declaration and also inside at each function declaration.  Populate these XML comments based on the functionality of the method/class.
9. Each lesson should be create as a VS2026 console style solution.  This includes the presence of a .slnx file and the project will be loaded by VS2026.  Each lesson folder lives under `Projects/` (see REPOSITORY LAYOUT below), with each of its projects under `src/<Project>/`.
10. Never use top-level statements.  When creating classes, always include them in the project namespace.
11. Do not include `LessonX_` as a prefix anywhere in project file names, class names or namespaces.  Only include the LessonX description in the header comments for Program.cs.

STRICT RULES FOR ALL DOCUMENTATION:

1. Each project will have a corresponding .md file that I can use as lesson material.
2. Do not use Underbars in the filenames.

REPOSITORY LAYOUT:

Every lesson folder is a complete, standalone, runnable solution. There is no shared class library and no solution at the repository root.

```text
Projects/
    LessonNN-TopicInPascalCase/
        LessonNN - Title.md                 the lesson (title as written in syllabus.md, `&` written as `and`)
        Topic.slnx                       the lesson's solution, with a `/src/` solution folder
        src/Topic/                       the lesson's main project (Topic.csproj, one type per .cs file)
        src/OtherProject/                any companion project (MCP server, A2A client)

    SkillsNN-TopicInPascalCase/          bonus lessons, same shape

verification/                            OUR harness - never presented, never shipped
    Verify-All.ps1                       clean-builds every Projects/*/*.slnx
    Maf.Verify.slnx, Maf.Tests/          small xunit.v3 tests over deterministic helpers
    global.json

docs/                                    anything that is not lesson content
syllabus.md, README.md, CLAUDE.md        repository root
```

1. Lesson folders are named `LessonNN-TopicInPascalCase` (zero-padded, hyphen separator, no underbars). The lesson file is `LessonNN - Title.md`. Solution, project and namespace names are the topic only - never `LessonNN`.
2. `.slnx` files use the `<Folder Name="/src/">` shape with project paths relative to the lesson folder.
3. Never leave `bin/`, `obj/`, `.vs/` or `TestResults/` anywhere. Never commit `*.user` / `*.slnLaunch.user` files.
4. `syllabus.md`, `README.md` and this file live at the repository root. Anything else that is not lesson content goes in `docs/`.
5. Unit tests are ours, not the viewer's. They live only in `verification/`, are never shown in a lesson, and no lesson folder ever contains a test project. Lesson types stay `internal`; the test project compiles the files it needs in by `<Compile Include=... Link=...>` rather than requiring `InternalsVisibleTo`.

VERIFICATION (before declaring a Lesson done):

1. Run `pwsh verification/Verify-All.ps1 -Filter LessonNN*` - it deletes `bin/obj/.vs`, builds the lesson's `.slnx` from clean, and must report 0 warnings and 0 errors. Analyzer and NuGet audit warnings count.
2. From inside `verification/`, run `dotnet test --solution Maf.Verify.slnx`. It must run from that folder so `verification/global.json` selects the Microsoft.Testing.Platform runner; from the repository root the .NET 10 SDK rejects the VSTest target. Never add `--nologo`, which reports "Zero tests ran" with exit code 5. All tests must pass.
3. Confirm every complete-file code block in the lesson matches that file in the lesson's own folder.
4. Delete build artifacts again. Report real numbers; never claim a build or test result you did not observe.

--- 

I will prompt you with "Generate Lesson N" and you will produce that lesson's full materials in the structure above, in `Projects/LessonNN-TopicInPascalCase/`. Do not generate multiple lessons unless I ask for it explicitly.

---

## SERIES SYLLABUS: Applied Agentic Orchestration with Microsoft Agent Framework

### Phase 1 — Foundations (Lessons 1–9)
*Building and governing a single capable agent.*

**Lesson 1 — Hello Agent & Personas**
Stand up the first `ChatClientAgent` against OpenAI via `IChatClient`, and layer in instructions/persona design so the agent has a distinct voice and role, not just a default assistant identity.

**Lesson 2 — Function Tools**
Give the agent local C# functions it can call. Covers tool declaration, invocation, and how MAF's function-calling loop differs from SK's plugin/kernel-function model.

**Lesson 3 — Structured Output & Multimodal Input**
Force the agent to return typed, parseable responses instead of free text, and accept image/audio input alongside text.

**Lesson 4 — Sessions & Chat History**
Introduce `AgentSession` for multi-turn conversation, and the chat history provider mechanism that backs it — how state persists (or doesn't) across turns and process restarts.

**Lesson 5 — Context Providers**
Generalize beyond raw chat history: inject arbitrary memory/context (user profile, retrieved documents, prior summaries) into an agent run.

**Lesson 6 — Middleware**
Add cross-cutting behavior — logging, input/output guardrails, request shaping — without touching the agent's core logic.

**Lesson 7 — MCP as a Tool Source**
Connect the agent to an external MCP server so it can discover and call tools it wasn't explicitly coded with, using MCP as a universal tool interface.

**Lesson 8 — Hosted Tools**
Distinguish locally-defined functions from provider/first-party hosted tools, and when to reach for each.

**Lesson 9 — Approvals (Function Invocation Gating)**
Require human or programmatic approval before a tool call executes — the first governance layer, and a direct setup for Human-in-the-Loop later in the series.

### Phase 2 — Orchestration (Lessons 10–19)
*Coordinating multiple agents toward a shared goal.*

**Lesson 10 — Workflows 101 (Graph Model)**
Introduce MAF workflows as directed graphs: nodes as executors (agents or functions), edges as data flow. This is the mental model for everything remaining in the series.

**Lesson 11 — Executors & Handlers**
Write custom workflow nodes beyond plain agents — functions that transform, route, or filter data moving through the graph.

**Lesson 12 — Sequential Pattern**
Chain agents in a fixed pipeline, each consuming the previous agent's output. The simplest orchestration pattern and the backbone of the capstone.

**Lesson 13 — Concurrent Pattern**
Run multiple agents in parallel on the same input and merge results — useful for independent sub-tasks with no ordering dependency.

**Lesson 14 — Handoff Pattern**
Let an agent dynamically delegate to another agent mid-conversation based on the request, rather than following a fixed graph.

**Lesson 15 — Group Chat Pattern**
Multiple agents collaborating in a shared conversational context, each contributing based on role.

**Lesson 16 — Magentic-One Pattern**
A planner-led orchestration where a lead agent decomposes a goal and directs specialist agents — the most autonomous pattern in the series.

**Lesson 17 — Human-in-the-Loop**
Insert approval/review checkpoints into a running multi-agent workflow, not just a single tool call (building on Lesson 9).

**Lesson 18 — Checkpointing & Durability**
Make long-running workflows resumable after interruption — persisting workflow state so a multi-agent process can survive restarts.

**Lesson 19 — A2A Protocol**
Expose an agent externally over the Agent-to-Agent protocol so it can be discovered and called by clients or agents outside your process — the bridge from in-process orchestration to real deployment.

### Phase 3 — Production (Lessons 20–24)
*Measuring, deploying, and shipping a real system.*

**Lesson 20 — Observability & Evaluation**
Trace multi-agent runs and evaluate output quality — the two lenses (system health vs. outcome quality) needed before trusting a system in production.

**Lesson 21 — Hosting & Deployment**
Compare self-hosting a workflow vs. deploying as a Foundry-hosted agent, and what changes operationally between the two.

**Lesson 22 — Capstone Design**
Architect the full Researcher → Analyst → Writer pipeline as one checkpointed graph workflow: agent responsibilities, tool access per agent, the shared data contracts, and the in-graph approval request port between the Analyst and the Writer.

**Lesson 23 — Capstone Build**
Implement the full pipeline as a single checkpointed graph workflow using the patterns from Lessons 10–19, composing the reusable pieces introduced throughout the series; a run pauses at the approval port and resumes from its checkpoint in a fresh process.

**Lesson 24 — Capstone Deploy & Review**
Expose the finished pipeline over the Responses protocol via Foundry hosting, deploy it, and do a full review of every concept the series covered and how it fits into the finished system.
