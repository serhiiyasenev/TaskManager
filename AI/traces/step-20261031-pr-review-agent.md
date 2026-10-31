# Automated PR review — 2026-10-31

Owner request: implement the previously discussed automatic AI reviewer,
verification agent and low-noise visual change report in GitHub. This accepts
the conversation implementation plan; no production deployment/merge requested.

Context reloaded: root AGENTS.md, AI/AGENTS.md, research, plan, decisions,
repository-layout, TESTS.md, latest repository-structure trace and existing CI.
Baseline main: c42ba23cd30b06585508658d783e1e30e08cef07.

## Changes

- Trusted-base GitHub Actions runtime with immutable head/base/merge-base inputs.
- Azure Responses API (API key), independent review/verification/presentation
  conversations, bounded read-only file/path tools and strict JSON validation.
- Full supplied diff reviewed, including mappings. Presenter collapses only
  mechanical details; source-backed/uncertain mapping issues remain prominent.
- Ukrainian sticky PR comment plus self-contained escaped HTML before/after
  cards and JSON artifact. Existing test check-run states are shown explicitly
  as a snapshot; the agent does not run PR code or invent test outcomes.
- Draft/fork/untrusted-author guards, incomplete coverage, source quote checks,
  stale head/base protection, per-PR publication serialization and separate
  read-only analysis/write-only-publication capabilities.
- Setup runbook, offline tests and activation instructions.

## Verification

- Python 3.12.14: 30 offline tests PASS, including fake-service integration of all
  three stages, source evidence, uncertain/missing findings, noise promotion,
  output escaping, unsafe paths/hosts/redirects, pagination and collection races,
  stale publication, missing configuration and incomplete provider responses.
- actionlint 1.7.12 (release checksum verified): both new workflows PASS.
- YAML parsing succeeds; HTML generated from explicit synthetic fixture data.
- Browser rendering was attempted but no Chromium binary is installed; no
  rendered visual verification or application browser regression pass is claimed.
- Product source/CI/test commands were not changed; product suites were not run
  locally because this environment has no .NET runtime.

Live Azure model execution, quota/credentials and actual PR review comments are
not verified. GitHub connector cannot provision Azure resources or populate
repository secrets. Merge plus Azure endpoint/deployment/key configuration is
required for activation. Application Azure deployment remains deferred.
