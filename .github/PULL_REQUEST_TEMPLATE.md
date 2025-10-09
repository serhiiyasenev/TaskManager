# 🧩 Pull Request — AI-Assisted Change (ACE-FCA Framework)

---

## 🧠 Summary

Describe **what** this PR changes and **why** it was made.  
If generated or assisted by AI, specify which step of `AI/plan.md` it fulfills.

> **Example:**  
> Implements step #3 from `AI/plan.md` — adds retry logic and Dead-Letter Exchange (DLX) configuration for the Notifier service.

---

## 📄 Related AI Artifacts

| Artifact | Purpose |
|-----------|----------|
| [`AI/plan.md`](../AI/plan.md) | Defines the goal and step-by-step roadmap for this change. |
| [`AI/traces/`](../AI/traces/) | Contains log(s) for this iteration, e.g. `step-20251010-1835.md`. |
| [`AI/decisions.md`](../AI/decisions.md) | Holds the ADRs (Architecture Decision Records) that constrain or justify this change. |
| [`AI/AGENTS.md`](../AI/AGENTS.md) | Describes the behavior rules and context utilization policy for AI agents. |
| [`AI/AI_GUIDE.md`](../AI/AI_GUIDE.md) | Integration guide for AI tools, defining reset and compaction behavior. |

---

## 🧾 Implementation Details

**Scope of Change:**  
- [ ] Code update  
- [ ] Configuration / settings  
- [ ] Documentation improvement  
- [ ] Test coverage  
- [ ] CI / workflow update  

**Module(s) affected:**  
> _List relevant modules or components (e.g., `Notifier`, `WebAPI`, `DAL`, `BLL`, etc.)_

**Summary of Key Changes:**  
-  
-  
-  

---

## ✅ Checklist Before Review

- [ ] `AI/plan.md` updated — completed step(s) marked ✅  
- [ ] New trace file created under `AI/traces/` for this iteration  
- [ ] No direct modifications to `AI/research.md` or `AI/decisions.md`  
- [ ] All tests pass locally (`dotnet test` or equivalent)  
- [ ] Naming conventions and code style followed  
- [ ] PR title is descriptive and consistent with the change  
- [ ] Related ADRs reviewed and still valid  

---

## 🧭 How Reviewers Should Evaluate

- **Focus on plan adherence:** confirm that implemented code aligns with the active step in `AI/plan.md`.  
- **ADR compliance:** ensure all changes respect accepted architectural decisions (`AI/decisions.md`).  
- **Trace completeness:** verify that a new `AI/traces/step-XX.md` was added summarizing the work.  
- **Clarity:** confirm that rationale and scope are well-explained within this PR.  
- **Avoid low-value comments:** do not review line-by-line AI-generated code unless logic deviates from the plan.

---

## 🚀 Merge Guidelines

Once approved:
1. Merge the PR into `main`.
2. The workflow `ai-auto-trace.yml` will automatically:
   - Generate a new trace file in `AI/traces/`.
   - Commit it under `main` as an audit log of this change.
3. Ensure the `ai-validation.yml` check is ✅ **green** before merging.

---

## 🧩 Example Pull Request Flow

```text
Agent completes step #3 in AI/plan.md  ➜
Creates AI/traces/step-03.md           ➜
Commits code & documentation           ➜
Opens PR using this template           ➜
Reviewer verifies plan + ADR alignment ➜
Upon merge, ai-auto-trace.yml adds audit trace
