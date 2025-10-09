# ⚙️ GitHub Configuration — TaskManager

This directory contains GitHub-level configuration files and workflow definitions
that support both **standard CI/CD pipelines** and **AI Governance automation**.

---

## 🧩 Directory Structure

| File / Folder | Purpose |
|----------------|----------|
| `PULL_REQUEST_TEMPLATE.md` | Standardized PR template for AI-assisted and manual contributions. |
| `workflows/` | Contains all CI/CD and AI Governance GitHub Actions workflows. |

---

## 🤖 AI Governance Workflows

Located under [`.github/workflows/`](./workflows/):

| Workflow | Purpose |
|-----------|----------|
| `ai-validation.yml` | Validates AI artifacts (`AI/` folder) on each Pull Request. |
| `ai-auto-trace.yml` | Automatically generates new trace entries after each merge. |
| `test.yml` | Standard build and run tests workflow. |

All AI-related workflows adhere to the **ACE-FCA** (Advanced Context Engineering) specification
defined in [`AI/AI_GUIDE.md`](../AI/AI_GUIDE.md).

---

## 🧾 Pull Request Governance

The PR template enforces AI-context alignment:

- Requires references to `plan.md`, `traces/`, and ADRs.  
- Prevents direct edits to `research.md` or `decisions.md`.  
- Ensures traceability between code, reasoning, and actions.

See [`PULL_REQUEST_TEMPLATE.md`](./PULL_REQUEST_TEMPLATE.md) for full details.

---

## 📄 Standards

- Maintainer: **Serhii Yasenev**  
- Version: 1.0 (October 2025)  
- Framework: **ACE-FCA — Advanced Context Engineering, Frequent Compaction Approach**
