# Step 25 — private AI drafts, review and atomic commit (active)

Branch `feature/ai-step-21-modernization`, draft PR51. User authorized full brief,
agents/ECC and preservation of Consensia deletion. Sole roadmap `AI/plan.md`;
accepted decisions/research and old migrations remain untouched. Step24 final
functional CI head cb524a5 passed SQL495/no skips, frontend44/browser24 and all
five HTTPS/non-root containers. Its evidence is in the preceding trace.

Owner's main b3ff3aa contains only the same approved Consensia deletion via PR52;
merged without rewriting history in8f3e77e. Last pushed head is dbb05b3; subsequent
step25 checkpoints are local until the feature is GREEN. Never merge PR51.

## Contract and ownership

- Routes: authenticated GET `/api/ai/status`; POST `/api/ai/task-extractions`
  (201), GET/DELETE own `{draftId}` (200/204), POST `/{draftId}/commit` (200).
  Cookie mutations retain global CSRF. Input notes/project/optional noteDate/IANA
  zone/uk|en; owner/roles are server-derived. Fixed TimeProvider local reference.
- Draft GUID/version, owner/project, 24h default TTL; stable item IDs, editable
  proposals, exact UTF16 quote spans, original assignee/date text, suggestion,
  clarification and interpretation labels. No tasks at inference. Unique current
  allowed-name resolution and deterministic date resolution, ambiguous values
  stay null. Date-only convention is local23:59 and visibly labelled/editable.
- Commit selected edited items with expectedVersion + Idempotency-Key; scope,
  assignees and all rows revalidated. Serializable task+materialization+pending
  TaskCreated outbox+minimal receipt+draftVersion commits together, final access
  check before commit, bounded deadlock recovery. Unique item materialization
  survives deleting its task through historical ID/nullable live FK.
- Same key+canonical typed payload returns identical persisted receipt before
  current version check, after fresh ownership/project rights. Different payload
  conflicts. Partial batches rotate version. Receipt replay default7d survives
  draft expiry/deletion and stores identifiers/hashes only. Delete/TTL erases
  notes AND candidate JSON; later purge removes private metadata, not tasks.
- SQL quota (ScopeKey product/userID + UTCday) reserves conservative full UTF8
  prompt/schema/input bytes+output ceiling+framing for each allowed inference
  attempt before calling provider; unknown outcomes keep reservation.
- Provider Disabled truthful unavailable/manual usable. Azure SDK2.1.0 +
  Identity1.21.0 stable verified; official GA API2024-10-21, strict structured
  schema, no tools, bounded input/items/output/time/retries. Fake Testing-only
  explicit injection; no production silent fallback. Real model/region/quota is
  unverified. Thirty synthetic UA/EN gold cases exist; no live quality score.

Agents: runtime owns DAL entities/BLL draft service/contracts+SQL/race tests;
delivery owns provider/parser/date resolution/SDK wire/evaluation/TaskCreated
compatibility; security owns AI page/tests/dictionary. Root owns model/context,
migration/controller/DI/rate limits/error statuses, shared schemas/routes/styles,
shared builds/tests/Git. Freeze ALL backend C# during shared test/build, including
new files in otherwise disjoint agent scopes. Agents do not run shared Git/builds.

## Verified checkpoints and current gaps

- SDK locked restore + NU1901–4 errors PASS. Runtime RED7fddf9b: **73 failed,
  1 passed, 74 total**; missing AI routes/new behavior, not import/build errors.
- Provider/parser + recovery statuses: **48 PASS**, no failed cases in that
  scope. Separate pending-TaskCreated scheduler case failed (claimed/canceled as
  reminder); RED448ed8f, Reminder-only selection/claim guard now implemented.
- Actual AI HTTP/SQLite relational core: **28 PASS** (private draft, no task/inference writes,
  scoped directory, UTC/name handling, partial edits, idempotency/canonical hash,
  stale/foreign/expired/revoked access, rollback and product budget); periodic
  cleanup1 and pending-created outbox1 also PASS. TestResults/ai-core-sdk.
- Valid remaining RED37dcb6e: official SDKwire16 throws explicit unimplemented
  adapter; expiry read/commit2 return410 but leave private notes; manual scalar
  reminder bounds2 accept offsets beyond actual scheduler's7d cap. These are
  being fixed. Initial retention Task ambiguity/import errors were repaired and
  do not count as behavioral evidence.
- Evaluation grader10 runtime RED27d9ff2 (schema/source/quality/unknown IDs are
  separate; duplicate predictions cannot boost recall); implementation pending.
- Front AI page17 behavioral test sources checkpoint8a57ab0; baseline failed
  because implementation module was absent, no runtime-case count claimed.
  Page now exists: focused **17 PASS** plus domain4 PASS. Full type/lint/coverage
  and rendered QA pending; source-marker codes are being translated into human
  Ukrainian/English explanations.
- Cross-layer review found legacy JSON nullable omissions: three actual-shape
  schema tests RED→GREEN; normalized absent nulls in shared frontend schemas and
  profile. Added shared AI schemas. These correct real onboarding/open-task
  interoperability; synthetic UI fixtures previously emitted null explicitly.
- Scalar reminder cap/UI domain changed to10080, matching accepted validators
  and scheduler; focused frontend RED1/backend RED2 reproduced the discrepancy.
  No existing scheduler guarantee widened or old endpoint removed.

## Exact next actions

Await delivery SDK/grader/runner source READY and runtime expiry/race source READY;
run focused GREEN and new races RED, fix proven failures. Add actual SQL concurrent
receipt/materialization/quota lifecycle gate. Root must scaffold ONE additive
AI drafts/Kind migration after model freezes, inspect SQL constraints and upgrade
from previous schema with existing data. No old migration edits.

Root DI/controller exist; Disabled uses unavailable transport, Azure uses
AzureSdkTaskExtractionChatTransport(options,environment); test fixtures override
ITaskExtractionProvider. Rate policy current authenticated ID, configured6/min;
global SQL quota bounds multi-replica spend. New Error statuses410/428/429/503.
Integrate AINotesPage route/nav/styles; verify source snippets, partial batches,
unknown network replay and full coverage without excluding production code.
Finish opt-in live evaluation runner with explicit token/cost limit and honest
SKIP; do not make cloud calls absent deployment/credentials/budget.

After full local gates, real SQL/frontend/container CI and trace, mark25 complete
then implement26 durable inbox/worker transports. TaskCreated outbox is deliberately
pending until compatible durable consumer26; never treat it as reminder. Move
scheduler and TTL cleanup into worker26. Azure27 and full-stack/live28 remain
required. Local az/bicep/docker/auth/deploy parameters are absent; prepare all
reproducible code before asking for concrete live configuration.
