# 🧠 AI Directory — TaskManager

This directory contains all **AI Governance & Context Engineering** artifacts
used by intelligent coding assistants and automation pipelines.

The project follows the **ACE-FCA (Advanced Context Engineering — Frequent Compaction Approach)** standard,
ensuring structured, reproducible, and reviewable AI-assisted development.

---

## 📁 Directory Structure

| File | Purpose |
|------|----------|
| `AGENTS.md` | Defines rules for AI context management, compaction, and workflow phases. |
| `AI_GUIDE.md` | Complete integration guide for AI agents, tools, and CI systems. |
| `decisions.md` | Architecture Decision Records (ADRs) — official reasoning for key design choices. |
| `plan.md` | Step-by-step roadmap of current AI-led implementation tasks. |
| `research.md` | Technical facts, dependencies, and project-specific findings. |
| `traces/` | Execution logs — each file documents one AI iteration or completed step. |

---

## 🔄 Workflow Summary

1. **Research phase:** update `research.md` with findings.  
2. **Planning phase:** define actionable steps in `plan.md`.  
3. **Implementation phase:** complete each step → log it under `traces/`.  
4. **Review phase:** human verifies `plan.md` and `decisions.md`.  
5. **Reset phase:** when context grows too large, rebuild from stored artifacts.

---

## 🧩 Governance Integration

This directory is monitored by GitHub Actions:

- **`ai-validation.yml`** — validates all AI artifacts for structure & freshness.  
- **`ai-auto-trace.yml`** — automatically creates trace entries after merges.  

For details, see [`.github/workflows/README.md`](../.github/workflows/README.md).

---

## 📄 Standards

- Framework: **ACE-FCA v1.0**  
- Owner: **Serhii Yasenev**  
- Created: **October 2025**  
- Principle: *Compact, Transparent, Human-Reviewed AI Contexts*
