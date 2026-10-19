# CI corrections — 2026-10-19

Owner requested fixing failed PR51 checks and rerunning. Remote current head:
709a48f had the failures below. Follow-up validation resumed after the usage limit
was restored; local results are recorded below. Fresh Linux CI remains required.

## Actual CI evidence

- Frontend run39251828129/job118057246140:86 pass,4 fail. The two theme cases
  fail at initial Dashboard fits() with356px scroll width against320px viewport
  at200% text. Two project-team cases retain performer43 instead of empty after
  choosing project73 of another team.
- Backend run39251828195/job118057247233, container run39251828088/job118057245675
  and live-stack run39251828073/job118057447039 all fail at restore, before product
  tests, on newly reported package advisories. A neutral test report means no
  tests ran after failed restore; it is not successful full-stack verification.

## Corrections

- Constrain page-header action groups/buttons to available width, permitting
  text wrapping rather than a flex-shrink:0 intrinsic-width overflow with wider
  fallback fonts. Add a320px/200% portable-font regression with diagnostics.
- Replace passive URL-to-filter effect with render-time source synchronization
  plus explicit submitted-source tracking, preserving newer edits while Apply
  navigation commits. Strengthen the cross-team test with an immediate post-submit
  edit and a held submitted-response fixture; retain Mine/same-team/history checks.
- Add explicit patched package overrides: PKCS10.0.13 at DAL and SQL bootstrap,
  SignalR.Common10.0.13 at Client/Notifier, XML10.0.13 at Tests. Project graphs need
  a real NuGet restore to regenerate all affected committed lockfiles. Nine locks
  were regenerated with audit enabled; locked-mode restore then succeeded.

Official Microsoft advisories, published October13 and checked October19:
https://github.com/advisories/GHSA-846f-f764-76mx
https://github.com/advisories/GHSA-2fmv-rv2c-59x7
https://github.com/advisories/GHSA-334f-vvp4-7g6p
https://github.com/advisories/GHSA-7hqp-7c5m-4vq6
https://github.com/advisories/GHSA-c399-9853-6jmj
https://github.com/advisories/GHSA-r955-8q5j-3h83
All list10.0.13 as patched in the10.0 line. Audit error gates stay enabled.

## Execution blocker and continuation

The attempted Playwright reproduction command did NOT execute: automatic approval
review failed because its usage limit was exhausted (service message: retry8:03PM).
This is a reviewer-capacity failure, not an unsafe-action decision. Both delegated
agents subsequently failed on the same usage limit. Do not bypass approval using
an alternate execution path. No tests/build/restore ran before the limit reset.

## Resumed validation

- Old production dist plus the deterministic immediate-submit/edit fixture:
  **RED2/2** project-team cases; selected project was reverted73->72. This reproduces
  the lost-newer-edit cause independent of Linux timing. Original Windows font
  cases still pass; actual Linux metrics are a separate CI gate.
- New production UI: **34/34 PASS** focused browser cases, desktop/mobile, zero
  skips/retries. Includes original theme, new Arial/Verdana fallback-font reflow,
  deterministic filter race, history/Mine, focus and pointer/touch Board checks.
- ESLint and TypeScript/Vite production build pass. Existing chunk advisory remains.
- Force-evaluate restore regenerated9 locks; subsequent locked restore with
  NU1901-4 as errors passes. Fresh NuGet audit:11 projects,0 vulnerable packages.
- Full solution Debug build:0 errors; existing28 nullable/analyzer warnings remain.
  Debug outputs avoid overwriting the running native Release DLLs.
- **53/53 backend regressions PASS**, zero skips, on the new Debug dependencies.
  These are focused state/authorization/HTTP checks, not a full native-stack rerun.

Evidence: `.local/ci-fixes-20261019/`. The UI dist is updated; native Release
processes/database remain intact. Publish the verified corrections to PR51 and
inspect fresh Linux frontend/backend/container/full-stack CI to completion.
