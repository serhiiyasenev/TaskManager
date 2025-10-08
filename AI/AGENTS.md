# AGENTS.md — TaskManager Project

## Purpose
This file defines the operational contract for any AI or automation agent interacting with this repository.

## Core Principles
1. **Source of Truth**
   - `research.md` — captures technical facts, dependencies, and discovered details.
   - `plan.md` — defines the step-by-step roadmap for upcoming changes.
   - `decisions.md` — logs key architecture decisions (ADRs).
   - `traces/` — contains brief logs of progress for each step.

2. **Context Utilization Policy**
   - Always aim to keep active context utilization under **40 %**.
   - Compact logs and tool outputs aggressively.
   - Keep only goals, active plan steps, and constraints in memory.

3. **Phase Workflow**
   1. **Research phase:** gather findings → update `research.md`.
   2. **Plan phase:** outline concrete changes → update `plan.md`.
   3. **Implement phase:** execute in small steps, after each — compact context and update traces.

4. **Human Review Focus**
   - Review **plans and decisions**, not raw diffs.
   - Never proceed to implementation without an accepted `plan.md`.

5. **Trace Logging**
   - For each sub-step, create a new file under `traces/step-XX.md`.
   - Each trace includes: what was done, findings, next actions.

6. **Purge / Reset**
   - When context becomes cluttered, perform a full purge and reload from the artifacts above.
   - Before any major refactor, feature addition, or change of project scope,
     the agent must perform a **context reset**.
   - Steps:
     1. Discard the current working memory (chat, scratchpad, transient context).
     2. Reload from the following artifacts:
        - `research.md` — technical facts, architecture, dependencies.
        - `plan.md` — current or upcoming steps.
        - `decisions.md` — ADRs (constraints and accepted design).
        - Latest files under `traces/` — current progress summary.
     3. Start a new working session using this reconstructed context only.

## Example Agent Packet
GOAL: Implement resilient message retry and DLX in Notifier.
CONSTRAINTS: Keep REST API intact, maintain test coverage.
RESEARCH: Summary from `research.md`.
PLAN: Active steps from `plan.md`.

CONTEXT RELOADED:
- research.md loaded
- plan.md active step: “Implement retry logic with limited attempts”
- decisions.md constraints: maintain REST API integrity
NEXT ACTION: continue implementation step 3.