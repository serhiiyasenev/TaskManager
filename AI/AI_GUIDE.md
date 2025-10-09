# AI Integration Guide — TaskManager

```yaml
ai_integration:
  version: 1.0
  context_files:
    - AGENTS.md
    - AI_GUIDE.md
    - decisions.md
    - plan.md
    - research.md
  trace_dir: traces/
  reset_policy:
    trigger:
      - "before_major_refactor"
      - "after_long_session"
      - "module_switch"
    steps:
      - "save_progress_to_traces"
      - "clear_context"
      - "reload_research_plan_decisions"
      - "resume_from_active_goal"
  ci_checks:
    - "plan_has_goal"
    - "plan_has_active_step"
    - "recent_trace_updated"
    - "base_files_exist"
```

## 🎯 Purpose
This guide defines how any AI assistant, coding agent, or automation pipeline
should interact with the ACE-FCA files (`AGENTS.md`, `research.md`, `plan.md`, `decisions.md`, `traces/`).

The goal: **stable, reproducible, and compact AI context management**.

---

## 📘 1. Files Overview

| File | Purpose |
|------|----------|
| `AGENTS.md` | Core rules, phase workflow, context utilization policy |
| `research.md` | Technical knowledge: architecture, dependencies, and observations |
| `plan.md` | Current project roadmap and active tasks |
| `decisions.md` | Architecture Decision Records (ADRs) — accepted technical choices |
| `traces/` | Progress logs (each `step-XX.md` = one iteration) |

---

## ⚙️ 2. Standard AI Workflow

### 🔹 a) Initialization
At startup, the AI agent must:
1. Load `research.md`, `plan.md`, `decisions.md`, and `AGENTS.md`.
2. Identify the **active goal and current step** in `plan.md`.
3. Construct its internal state as:
GOAL: <from plan.md>
CONSTRAINTS: <from decisions.md or plan.md>
RESEARCH: <summary from research.md>
CURRENT STEP: <active plan step>

### 🔹 b) Execution Cycle
1. Perform only **one concrete step** from `plan.md` at a time.
2. After completing it:
- Write a short log to `traces/step-NN.md`.
- Mark the step as done in `plan.md` (add ✅).
3. Compact internal context — remove previous step data, keep only next actions.

### 🔹 c) Context Compaction Rules
- Keep context utilization ≤ 40 %.
- Compress long logs into short summaries.
- Drop irrelevant discussion, keep only:
- Active goal
- Current plan step
- Constraints from ADRs
- Findings summary

---

## 🔄 3. Reset Procedure

A **reset** must occur:
- Before a large refactor or architecture change.
- After a long session with heavy context usage (> 60 %).
- When switching project modules (e.g., from Notifier → WebAPI).

**Steps:**
1. Save current progress to `traces/step-NN.md`.
2. Clear the working memory or chat history.
3. Reload `research.md`, `plan.md`, `decisions.md`.
4. Rebuild a new working context with only relevant data.
5. Resume from the active goal.

Example internal message:
RESET INITIATED.
Context cleared.
Reloaded research.md, plan.md, decisions.md.
Active step: Implement retry logic (plan step #3).
Next action: create retry mechanism in Notifier service.

## 🧠 4. Collaboration with CI/CD

AI-generated changes should:
1. Always update `plan.md` and `traces/`.
2. Never modify `research.md` or `decisions.md` automatically (only humans approve those).
3. Follow branch naming convention: feature/ai-step-<number>-<short-description>

4. Use PR templates provided in `.github/PULL_REQUEST_TEMPLATE.md`.

CI validation checks (recommended):
- All four base files exist.
- `plan.md` includes a `GOAL` section.
- `plan.md` contains at least one active step.
- Last trace file timestamp ≤ 7 days.

---

## 🧩 5. Human-AI Collaboration Rules

- Human reviews **plan and decisions**, not raw code.
- AI follows these artifacts strictly.
- After each PR merge, AI must append new step logs under `traces/`.

---

## 🚀 6. Example Session Flow
Agent boot →
Reads research.md + plan.md →
Identifies active goal →
Implements small code diff →
Creates traces/step-04.md →
Marks plan step as done →
Compacts context →
Commits changes → PR → Review.

---

## ✅ Summary

By following this guide, any AI system (ChatGPT, GitHub Copilot, custom agents, CI tools)
can safely interact with the TaskManager repository — producing transparent, reviewable,
and context-efficient development cycles.
