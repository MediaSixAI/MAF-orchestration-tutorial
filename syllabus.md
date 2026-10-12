# Applied Agentic Orchestration with Microsoft Agent Framework
### A 24-Lesson C# Developer Series

---

## Course Overview

**Framework:** Microsoft Agent Framework (MAF) — the successor to Semantic Kernel and AutoGen
**Language / Runtime:** C# / .NET 10
**Model Provider:** OpenAI as the primary provider, via the first-party `Microsoft.Extensions.AI.OpenAI` package's `IChatClient` implementation (`Microsoft.Extensions.AI` abstraction). Each lesson's `ChatClientFactory` can target Azure OpenAI, Gemini, Ollama or LM Studio instead through their OpenAI-compatible endpoints, chosen by the required `MAF_LLM` environment variable (`OPENAI`, `AZURE`, `GEMINI`, `OLLAMA`, `LMSTUDIO`); the required `MAF_MODEL` names the model (on Azure, the deployment name). Neither has a built-in default
**Chat Model:** set by `MAF_MODEL`; recommended `gpt-4.1-mini` on OpenAI and Azure (used for all demos), `gemini-2.5-flash` on Gemini (`ChatClientFactory` rejects `gemini-3*` models: Gemini 3 tool calls fail on the OpenAI-compatible endpoint), `qwen3:8b` on Ollama, the same Qwen3 8B model (Q4_K_M, e.g. `qwen/qwen3-8b`) on LM Studio
**Embedding Model:** `text-embedding-3-small` (where relevant — Context Providers / memory lessons)
**Core NuGet Packages:** `Microsoft.Agents.AI` and related packages — verify current package names, namespaces, and API signatures before writing code for any given lesson, as MAF is under active development

**API Keys:** Always retrieved from environment variables (e.g., `OPENAI_API_KEY`). Never hardcoded.

**Capstone Deliverable (Lessons 22–24):** A multi-agent research-and-report pipeline — Researcher agent with MCP tool access → Analyst agent for structured extraction → Writer agent for report drafting — orchestrated as a single checkpointed graph workflow with an in-graph human-approval request port between the Analyst and the Writer, exposed as one agent over the Responses protocol via Foundry hosting.

**Series relationship:** This is a sequel to the presenter's Semantic Kernel series. Where a lesson's topic has a documented migration relationship to Semantic Kernel (e.g., `ChatCompletionAgent` → `ChatClientAgent`), the lesson includes a "Coming from SK" callout box.

**Lesson structure:** Every lesson follows this exact section structure:
1. Project Overview (2–3 sentences: what we're building and why it matters)
2. Prerequisites (what from prior lessons this lesson assumes)
3. Setup (packages, config, env vars)
4. Core Concepts (the MAF abstractions/patterns being taught, explained plainly before any code)
5. Complete C# Code (full, runnable, no ellipses/omissions)
6. Walkthrough (step-by-step explanation of the code just shown)
7. Expected Output

Each lesson's code introduces reusable pieces (agent factory helpers, shared instructions patterns, config loader, etc.) early enough that the Lesson 22–24 capstone assembly is mostly composition, not new code.

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

---

## Notes for Course Maintenance

- **Fast-moving framework:** MAF is under active development. Verify current package names, namespaces, and API signatures immediately before writing code for each lesson — don't batch-verify at the start of the series. Flag any breaking changes found versus older samples.
- **OpenAI connector:** OpenAI is the primary provider, via the first-party `Microsoft.Extensions.AI.OpenAI` package's `IChatClient` implementation (`OpenAIClientExtensions.AsIChatClient`). `ChatClientFactory` reaches the other providers (`MAF_LLM` = `AZURE`, `GEMINI`, `OLLAMA`, `LMSTUDIO`) through the same SDK; `MAF_LLM` and `MAF_MODEL` are both required. Verify its current version and API surface before writing code for each lesson.
- **Fixed models:** The recommended chat model is `gpt-4.1-mini` (set through `MAF_MODEL`; there is no built-in default); embedding model (where relevant) is always `text-embedding-3-small`. Demos and Expected Output use `MAF_LLM=OPENAI` with `MAF_MODEL=gpt-4.1-mini`. Don't substitute other models without an explicit instruction to do so.
- **SK migration framing:** Include a "Coming from SK" callout box wherever a lesson's topic has a documented migration relationship to Semantic Kernel — this series is a sequel to the SK series and many viewers are migrating.
- **Capstone build-up:** Introduce reusable pieces (agent factory helpers, shared instructions patterns, config loader, etc.) early enough that the Lesson 22–24 capstone assembly is mostly composition, not new code.
- **Generation workflow:** Lessons are generated one lesson at a time on request ("Generate Lesson N"). Do not generate multiple lessons unless explicitly asked.
