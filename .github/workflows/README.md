# 🤖 AI Governance System — GitHub Workflows

This directory contains all automation workflows responsible for maintaining
**AI-assisted development integrity** within the TaskManager repository.

The workflows implement the **ACE-FCA (Advanced Context Engineering – Frequent Compaction Approach)** standard
and enforce transparency, traceability, and context hygiene for AI agents.

---

## 🧩 1. `ai-validation.yml`

### 🎯 Purpose
Validates that all **AI integration artifacts** (ACE-FCA files) are present, structured, and up-to-date
whenever a Pull Request targets the `main` branch.

### ✅ Checks performed
| Category | Description |
|-----------|-------------|
| **File presence** | Verifies `AI/AGENTS.md`, `AI/research.md`, `AI/plan.md`, `AI/decisions.md`, `AI/AI_GUIDE.md`, and the `AI/traces/` directory exist. |
| **AI_GUIDE validity** | Confirms YAML preamble in `AI_GUIDE.md` contains `ai_integration`, `context_files`, and `reset_policy`. |
| **plan.md integrity** | Ensures `plan.md` includes a `## GOAL` section and a step table. |
| **Trace freshness** | Checks that the newest file in `AI/traces/` is no older than 7 days. |
| **Result summary** | Prints a full validation report in the workflow logs. |

### 🧠 When it runs
- Automatically on every **Pull Request → main**.
- Manually via **“Run workflow”** in GitHub UI.

---

## 🧾 2. `ai-auto-trace.yml`

### 🎯 Purpose
Automatically generates a new **trace log file** under `AI/traces/`
after each successful merge into the `main` branch — creating a persistent audit trail
of all AI-assisted or automated commits.

### 🛠️ What it does
| Step | Action |
|------|---------|
| 1️⃣ | Detects merge commit into `main`. |
| 2️⃣ | Collects PR metadata — title, author, commit SHA, timestamp, and URL. |
| 3️⃣ | Creates a new Markdown file `AI/traces/step-YYYYMMDD-HHMMSS.md`. |
| 4️⃣ | Commits and pushes the file back to `main` using `github-actions[bot]`. |

### 🧩 Example generated trace
```md
# Trace Auto Entry — 2025-10-10 18:44 UTC

**Date:** 2025-10-10T18:44:00Z  
**Commit SHA:** `f27a4b88e15d4f84c22a31b0d8a98d3e1b10bca7`  
**Author:** Serhii Yasenev  
**Summary:** Introduce ACE-FCA framework and validation workflow  
**Source:** [View Commit](https://github.com/serhiiyasenev/TaskManager/commit/f27a4b88e15d4f84c22a31b0d8a98d3e1b10bca7)

**Auto-generated trace entry after merge to main.**
