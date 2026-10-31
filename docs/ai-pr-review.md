# Automatic PR review with Azure OpenAI

This repository contains the same standalone review agent used in BookShop,
TaskManager and Scheduler. It runs on GitHub-hosted Ubuntu runners. No always-on
server, Container App, GitHub App installation or production deployment is needed.
The Python 3.12 worker uses only the standard library; it does not change the
application's .NET/Node dependencies.

## Enable it

1. Merge the installation PR into the repository's default branch. The runtime
   deliberately reads trusted base-branch code; it cannot review its own initial
   installation before merge. The separate offline test workflow does run on PRs.
2. In Azure, create/select an Azure OpenAI model deployment supporting the v1
   Responses API, function tools and strict structured JSON outputs. Check model
   availability and quota in your region. Deployment name is your chosen name,
   not necessarily the model identifier. Existing ChatGPT/Copilot subscriptions
   do not supply this API credential or Azure quota.
3. In GitHub **Settings → Secrets and variables → Actions → Variables**, set:

   | Variable | Value |
   | --- | --- |
   | `AZURE_OPENAI_ENDPOINT` | `https://YOUR-RESOURCE.openai.azure.com` |
   | `AZURE_OPENAI_DEPLOYMENT` | Your existing model deployment name |

4. In **Actions → Secrets**, add `AZURE_OPENAI_API_KEY` using a key from that
   Azure OpenAI resource. Never place it in code, a PR, a comment or chat.
   This first version uses an API key; OIDC/Entra authentication is not implemented.
   The endpoint must be reachable from GitHub-hosted runners. Private-network-only
   endpoints require a separately configured runner/network route.
5. Ensure repository/organization Actions policy allows the pinned official
   actions and the workflow's `pull-requests: write` permission for its publisher.
6. Open a same-repository, non-draft PR authored by an OWNER, MEMBER or
   COLLABORATOR, or run **Actions → AI PR review → Run workflow** from the default
   branch and enter the PR number. Forks/untrusted authors are deliberately skipped.

Each repository needs its own configuration, or appropriately scoped organization
variables/secrets. Azure requests incur usage on your Azure resource. No Azure
resource or paid model deployment is created by these workflows.

## What happens

- On PR opened/updated/reopened/ready, the trusted worker reads the current head
  and target base. Diffs use the actual merge-base; target-branch advancement is
  not incorrectly described as a PR change. GitHub's paginated file list is checked
  against the current PR file count.
- Reviewer analyzes the diff. Verifier gets a fresh conversation and bounded
  `list_files`/`read_file` tools at those immutable SHAs. Source quotes are checked
  at the cited line; unmatched evidence becomes uncertain. The verifier's
  agreement is not equivalent to executing or reproducing a test.
- Presenter generates Ukrainian before/after explanations, affected files and
  suggested tests. Mechanical mappings/formatting are collapsed in the report.
  All supplied patches still go through review; mappings with material risks or
  unresolved findings stay visible. Excluded formats and budget gaps are explicit.
- A single bot-owned PR comment is updated, not appended on each push. An HTML
  before/after report and JSON evidence are stored in the `ai-pr-review` artifact
  for seven days. HTML is self-contained, has no JavaScript or remote resources,
  and escapes source/model strings. It visualizes code behavior; it does NOT
  produce browser screenshots or prove UI rendering.
- The agent reads existing GitHub check-run states on the head and current merge
  commit. It never runs PR code with model credentials. Existing build/unit/API/UI
  test workflows remain responsible for executing tests. Completion of the
  `.NET Build and Test` or `Run Tests` workflow triggers a refreshed report for
  the current PR head. Other CI results are a snapshot; manual rerun refreshes them.
- Publishing rereads both current head and target base and skips stale/closed PRs.

## Outcome semantics

| State | Meaning |
| --- | --- |
| `reviewed` | All three model stages completed for the supplied diff; not a merge approval |
| `partial` | Model stages completed, but some files/CI/context were unavailable or limited |
| `configuration_required` | AI was not called; configure all three Azure values |
| `failed` | Model/API/schema/budget failure; no completed-review claim |

Missing Azure configuration and incomplete AI runs fail the analyze job while
still publishing a clear report when available. Keep this workflow advisory
until real runs are evaluated; do not make it a required branch check before
configuration. Existing required test checks remain unchanged. The agent cannot
approve, merge, deploy, edit source or automatically apply fixes.

## Boundaries and limits

`pull_request_target` runs only the trusted base implementation. No PR checkout,
package installation, build, shell execution or local imports from the PR occur
in the credentialed job. Python isolated mode ignores repository/PYTHONPATH
imports. PR files, titles, check names and model output are untrusted data. The
model has no arbitrary URL, GitHub write or shell tools. The publisher is a
separate job without Azure credentials; the analyzer has a read-only GitHub token.

Prompts live in `.github/review-agent/{reviewer,verifier,presenter}.md`. Updating
them takes effect after merge. Agent review is advisory and prompt injection
cannot be assumed fully solved; human review remains necessary.

Limits: 80 input patches, 80,000 total patch characters, 16,000 per patch,
200 KB per source file, 60,000 source-tool characters, 12 tool calls and 6 logical
model requests (each with at most 3 bounded HTTP attempts), 6,000 output tokens per
request and a 20-minute job timeout. Long/binary/credential-like files are recorded
as unreviewed. Obvious credential assignments are redacted; this is not a complete
secret scanner. Coverage limitations never mean the omitted code is safe.

## Offline verification

```sh
python3 -I -m unittest discover -s .github/review-agent -p 'test_*.py' -v
```

Tests use explicit fake GitHub/model responses and cover real orchestration,
pagination, stale-head/base guards, permissions boundaries, evidence handling,
noise promotion, output escaping and missing/failed AI. They do not prove Azure
credentials, quota, model quality or a successful live review. For live acceptance,
configure Azure, run on a small known PR, inspect all three stages and the actual
CI snapshot, push another commit and confirm one refreshed comment and a new SHA.

References: [Azure Responses API](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/responses),
[GitHub workflow events](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows).
