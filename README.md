# Applied Agentic Orchestration with Microsoft Agent Framework

A 24-lesson C# video series on building, orchestrating, and shipping AI agents with
**Microsoft Agent Framework (MAF)** — the successor to Semantic Kernel and AutoGen.

## What the series builds

The series starts with a single `ChatClientAgent` and ends with a multi-agent
research-and-report pipeline:

```text
Researcher (MCP tool access) → Analyst (structured extraction) → Writer (report drafting)
        orchestrated as one checkpointed graph workflow, with a human-approval port,
        exposed as a single agent over the Responses protocol via Foundry hosting
```

Reusable pieces — agent factory helpers, shared instruction patterns, a config loader — are
introduced early, so the Lesson 22–24 capstone is mostly composition rather than new code.

## Stack

- **Microsoft Agent Framework** (`Microsoft.Agents.AI` and related packages) on **.NET 10**.
- **OpenAI** as the primary provider, via the first-party `Microsoft.Extensions.AI.OpenAI` `IChatClient`. Each
  lesson's `ChatClientFactory` can switch to Azure OpenAI, Gemini or a local Ollama or LM Studio model instead. Two
  environment variables are **required**: `MAF_LLM` picks the provider (`OPENAI`, `AZURE`, `GEMINI`, `OLLAMA` or
  `LMSTUDIO`)
  and `MAF_MODEL` picks the model (on Azure, the deployment name). There are no built-in defaults.
- Recommended chat models: **`gpt-4.1-mini`** on OpenAI and Azure (used for every demo and Expected Output),
  `gemini-2.5-flash` on Gemini (`ChatClientFactory` rejects `gemini-3*` models: Gemini 3 tool calls fail on the
  OpenAI-compatible endpoint), `qwen3:8b` on Ollama (`http://localhost:11434/v1/`), the same Qwen3 8B model
  (Q4_K_M, e.g. `qwen/qwen3-8b`) on LM Studio (`http://localhost:1234/v1/`, no key); embedding model **`text-embedding-3-small`** where relevant (always OpenAI).
- API keys come from environment variables (`OPENAI_API_KEY`, or the selected provider's own) — never hardcoded.

## Course Structure

| Phase | Lessons | Focus |
|---|---|---|
| 1 — Foundations | 1–9 | One capable, governed agent: personas, tools, structured output, sessions, context providers, middleware, MCP, hosted tools, approvals |
| 2 — Orchestration | 10–19 | Workflows as graphs, executors, sequential / concurrent / handoff / group chat / Magentic-One patterns, human-in-the-loop, checkpointing, A2A |
| 3 — Production | 20–24 | Observability and evaluation, hosting and deployment, then the capstone design, build, and deploy |
| Bonus | Skills 01–02 | MAF Agent Skills: a single file-based skill, then several skill sources composed together |

`syllabus.md` is the single source of truth for Lesson numbering, titles, and scope.

## Repository Layout

Every lesson folder is a **complete, standalone, runnable solution**. There is no shared
class library and no solution at the repository root.

```text
Projects/
    LessonNN-TopicInPascalCase/
        LessonNN - Title.md                 the lesson
        Topic.slnx                       the lesson's solution — open this in VS2026
        src/Topic/                       the lesson's console (or web) project
        src/OtherProject/                any companion project (MCP server, A2A client)

    SkillsNN-TopicInPascalCase/          bonus lessons, same shape

verification/                            private build-and-test harness — not lesson content
docs/                                    series introduction, slides, planning notes
syllabus.md, README.md, CLAUDE.md        repository root
```

## Running a lesson

1. Set your key: `$env:OPENAI_API_KEY = "your-openai-api-key"`. To use another provider, also set
   `MAF_LLM` (and that provider's key) — see any lesson's Setup section.
2. Open `Projects/LessonNN-Topic/Topic.slnx` in Visual Studio 2026 and press F5, or from a
   terminal run `dotnet run` inside `Projects/LessonNN-Topic/src/Topic/`.
3. Lessons with companion projects (Lessons 7, 19, 23, 24) describe the start-up order in
   their Setup section.

## Verification

`verification/` holds a private harness used to trust the presented code; it is never shown
in a lesson. `verification/Verify-All.ps1` builds every lesson solution from clean, and
`dotnet test --solution Maf.Verify.slnx` (run from inside `verification/`) runs a small set of tests over the capstone's
deterministic helpers.
