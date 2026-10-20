# AI task extraction provider

AI produces editable drafts. Creating an extraction does not create a task.
Confirmation uses the SQL draft/version/expiry and current authorization checks;
the model response is never a task write request.

## Configuration

The `AI` section defaults to `Provider=Disabled`. Manual task creation remains
available when AI is disabled or unavailable. Do not configure a fake production
fallback. `Fake` is permitted only in the explicit `Testing` environment with an
injected fixture provider/transport.

For Azure, configure `AI__Provider=Azure`, `AI__Endpoint` as the HTTPS Azure OpenAI
resource root, and `AI__Deployment` as an actual deployment supporting strict
structured outputs. Credentials and provisioning are independent of these
settings. The provider status indicates configuration availability, not a live
authorization or quota probe.

Production uses an explicit managed identity. Optional `AI__ManagedIdentityClientId`
selects a user-assigned identity; otherwise the system-assigned identity is used.
Give that identity [Cognitive Services OpenAI User](https://learn.microsoft.com/azure/ai-foundry/openai/how-to/role-based-access-control)
at the product's Azure OpenAI resource. Local development can use
`DefaultAzureCredential` or an explicitly supplied `AI__ApiKey` from user secrets
or a private environment. Production API keys are rejected. Never put credential
values in tracked JSON, Compose defaults, browser bundles, or evaluation reports.

The pinned official packages are [Azure.AI.OpenAI 2.1.0](https://www.nuget.org/packages/Azure.AI.OpenAI/2.1.0)
and [Azure.Identity 1.21.0](https://www.nuget.org/packages/Azure.Identity/1.21.0).
The Azure SDK selects its supported `2024-10-21` API version; an arbitrary version
override is not exposed. Deployment/model compatibility must be verified by an
opt-in real inference run before cloud acceptance.

The pinned Azure SDK maps `MaxOutputTokenCount` to the stable `max_tokens` wire
field, as documented in its [version 2.1.0 chat client source](https://github.com/Azure/azure-sdk-for-net/blob/Azure.AI.OpenAI_2.1.0/sdk/openai/Azure.AI.OpenAI/src/Custom/Chat/AzureChatClient.cs).
Use a deployment compatible with that API and strict outputs. Reasoning models
that require `max_completion_tokens` need a separately verified SDK/API contract;
the application does not silently remove the output ceiling or use a preview
serialization override.

| Setting | Default | Supported bound |
|---|---:|---|
| `MaxInputCharacters` | 20000 | 1–20000 UTF-16 code units |
| `MaxItems` | 25 | 1–25 |
| `MaxAllowedAssignees` | 200 | 1–1000 |
| `MaxPromptUtf8Bytes` | 128000 | 4096–1000000 |
| `MaxOutputTokens` | 4000 | 256–8000 |
| `TimeoutSeconds` | 30 | 1–120, total inference time |
| `MaxInferenceAttempts` | 1 | 1–2 |
| `RequestsPerMinute` | 6 | 1–120 per authenticated user |
| `UserDailyTokenBudget` | 100000 | Positive, SQL enforced |
| `ProductDailyTokenBudget` | 1000000 | Positive, SQL enforced |
| `DraftLifetimeHours` | 24 | 1–168 |
| `ReceiptLifetimeDays` | 7 | 1–30 |

The complete system prompt, serialized input, and schema are counted as UTF-8
bytes, plus a 1024-token framing margin and the full output ceiling, for every
permitted attempt. This conservative reservation is made against both SQL user
and product daily counters before inference. A request may hit its token budget
before the character limit. Unknown usage after an error or failed attempt keeps
the reservation. The application keeps the full daily SQL reservation even when
usage is known; the evaluation runner uses only positive-input, complete usage
to refine its explicit token/cost estimate.

## Input and output boundary

The inference input contains only notes, a server-fixed reference calendar date,
an explicit time zone/locale, and permitted assignee IDs/display names. It omits
credentials, other teams, user contact details, database access, tools, and chat
history. Notes are untrusted data even when they contain instructions.

The SDK requests strict JSON schema with every field required; nullable values
are explicitly `null`. The server additionally checks item/string limits,
duplicate and additional properties, exact contiguous source quotes and UTF-16
spans, permitted names, and supported date interpretations. JSON validity is not
proof of correct extraction.

The model-selected assignee ID is ignored as authority. A full display name or a
unique first name must match the recorded source and the permitted list. Unknown
or duplicate names remain unresolved. Commit checks current membership again.

Dates are recomputed from `originalDueText` and the fixed local reference date.
Supported unambiguous inputs include `today`/`сьогодні`, `tomorrow`/`завтра`, a bare
weekday, ISO dates, and explicit ISO clock timestamps. A bare weekday selects its
first occurrence on or after the reference date. Date-only deadlines mean
**23:59 local**, marked `date_only_end_of_day`. Ambiguous text, unsupported natural
clock phrases, and invalid/ambiguous DST local times remain unresolved. The user
can edit these values before confirming.

Paraphrased titles/descriptions are visibly marked as interpretations. A
conservative commitment check marks unagreed ideas and unsupported obligation
wording as suggestions. Review is required: deterministic source checks do not
establish the semantic correctness of every paraphrase.

## Failures and privacy

Disabled/unconfigured AI reports `Ai.Unavailable`, with HTTP 503 at the API.
Refusal, content filtering, truncation, malformed schema, input limits,
authentication failures, quota/rate limits, and timeouts are explicit failures.
They never become an empty successful extraction.

SDK retries are disabled. Only explicit transient network/5xx failures may use a
second configured attempt, within the same total timeout. 429 returns a bounded
retry hint without an inference retry. Caller cancellation propagates. Exception
messages and logs omit provider bodies, prompts, raw notes, API keys, and token
material; SDK message/content logging and distributed tracing are disabled for
this inference boundary.

Task confirmation writes the task and its stable `TaskCreated` outbox record in
one transaction. During step 25 those entries remain `Pending`; the legacy
reminder scheduler handles `Kind=Reminder` only. Durable inbox delivery and the
compatible created-event consumer are the step 26 gate.

## Draft privacy and confirmation

Drafts are private to their author, including when another user is a global
administrator. They expire after24h by default. Reads and new commits of an
expired draft erase both notes and candidate/source snippets before returning410;
the author can also erase a draft after losing project access. Erasure preserves
already created tasks. Bounded periodic cleanup runs in the notifier worker
(step26), never a scale-to-zero API timer.

Confirmation requires the current draft version and an `Idempotency-Key`.
All selected rows, current project access and current assignees are validated
before task writes and again before the transaction commits. Same-key replay
returns the saved receipt for the same normalized payload; changed payload,
stale versions and repeated materialization conflict. Minimal receipt IDs/hashes
are retained for7d by default, independently of private source erasure. Replay
still requires current owner/project rights. No inference or broker operation
runs inside the confirmation transaction.

## Evaluation

See [the 30-case Ukrainian/English evaluation](../evaluation/task-extraction/README.md).
Report strict schema validity, source grounding, source task precision/recall,
raw and validated assignee/deadline accuracy, inaccessible IDs, failures,
latency, and usage separately. Deterministic fixtures are not real model quality
evidence. No real inference scores are claimed without a configured Azure
deployment, authorization, bounded budget, and explicit opt-in execution.
