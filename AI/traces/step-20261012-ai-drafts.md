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

## Continuation evidence (2026-10-12)

- Current checkpoint438c42e recorded actual directory3 and pricing10 RED.
  Directory refresh/never-retarget fix and pricing implementation now pass their
  focused cases. Latest focused run:18total,17PASS,1expectedFAIL for new zero-input
  token accounting; RED checkpoint3c75c44. Normalization is in progress.
- Native SDK wire16, contradictory name/date2, evaluator10 and retention3 passed
  in the preceding aggregate. SDK2.1.0 sends max_tokens by its verified Azure
  compatibility mapping; its ceiling remains4000. No live inference was called.
- Additive migration20261011215856_AddAiTaskDrafts was scaffolded successfully and
  inspected: five AI tables, owner/key and materialization uniqueness, GUID
  concurrency versions, existing outbox Kind default0/Reminder. No old migration
  modified. Extended realSQL previous-schema test preserves prior task versions,
  users/hashes and an existing pending outbox occurrence; execution awaits CI.
- Full frontend local gate:68PASS, line91.10%, statement81.90%, branch73.48%,
  function79.77%; lint/types/production build PASS, no lowered thresholds.
  Rendered Chrome preliminary AI run14PASS (synthetic intercepted API only).
  Review then found duplicate landmark labels and nowrap badge overlap; distinct
  human proposal-table name and scoped130px/wrapping fix are implemented.
  Fresh build PASS; browser recheck now also exercises revoked commit rights.
- RealSQL concurrent independent-context same-key/different-key materialization
  and shared daily quota tests are being added. No SQLite result is presented as
  evidence for SQL lock/range/deadlock behavior.

## Exact next actions

Latest local full gate is GREEN:620PASS,6realSQLskip,0fail/626total,40s,
line89.27%(4280/4794),branch72.53%(1920/2647). Two preceding full runs aborted
with nofailedcases; owned Windows .NET Runtime events proved an unhandled
IOException in the pre-existing SelfLog File.AppendAllText callback under
parallel host startup/Loki timers. Replaced the unsafe append and later override
with one synchronized stderr diagnostic writer. Full run then completed; no test,
collector, analyzer or production module was disabled/excluded.

Locked restore/audit and full Release solution build PASS (27historicalwarnings,
0errors). Evaluation CLI without live opt-in printed SKIP and exited2 as intended;
no inference/network cost was authorized. AI browser14PASS, ECC evidencechecker
exit0, selected11synthetic screenshots and scoped limits committed8e952ec.

Backend/provider/transaction/model sources now committed7d883f0/7d1a447 and
additive migration/realSQL gates c2e48d1. Root is resolving the concrete UI400
interoperability finding before push: actual business-validation ProblemDetails
does not contain MVC row errors. Narrow tests validRED d50b4f4; generic400 must
offer current draft/directory review preserving edits, while existing field-error
400 remains directly correctable. Known membership-change explanation is added.
The final UI400 fix now passes18focused/69full frontend tests. Full final
coverage: line91.12%(626/687),statement81.92%(979/1195),branch73.52%(836/1137),
function79.77%(288/361); types/lint/build PASS. UI400 is component-only evidence,
explicitly distinguished from browser14. Await actual626no-skipSQL/38browser/
five-image CI before accepting25 and advancing26. Draft PR51 remains unmerged.

Root must commit final UI400 recovery and docs, push the complete GREEN head,
inspect its actual workflow runs/jobs/logs, repair concrete CI failures if any,
then record exact evidence and accept25. Source owners remain frozen for C#.

Root DI/controller exist; Disabled uses unavailable transport, Azure uses
AzureSdkTaskExtractionChatTransport(options,environment); test fixtures override
ITaskExtractionProvider. Rate policy current authenticated ID, configured6/min;
global SQL quota bounds multi-replica spend. New Error statuses410/428/429/503.
AINotesPage route/nav/styles, partial batches, exact unknown replay and live
evaluation token/cost bounds are implemented and verified in their stated local
scopes. Do not make cloud calls absent deployment/credentials/budget.

After full local gates, real SQL/frontend/container CI and trace, mark25 complete
then implement26 durable inbox/worker transports. TaskCreated outbox is deliberately
pending until compatible durable consumer26; never treat it as reminder. Move
scheduler and TTL cleanup into worker26. Azure27 and full-stack/live28 remain
required. Local az/bicep/docker/auth/deploy parameters are absent; prepare all
reproducible code before asking for concrete live configuration.
