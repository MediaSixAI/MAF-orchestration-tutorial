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
