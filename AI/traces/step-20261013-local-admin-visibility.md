# Local administrator visibility — 2026-10-13

The owner explicitly requested all tasks, projects, teams and related users for
admin@example.com. Existing authorization already supports global admin scope.

- Granted only existing role (UserId11, RoleId1) in the preserved local product
  database, guarded by exact database/account/role and confirmed usable account.
  Password and workspace assignments were preserved. Security/concurrency stamps
  rotate for a fresh sign-in. Initial SQL client QUOTED_IDENTIFIER failure rolled
  back; explicit index-compatible SET options corrected the successful retry.
- Fresh authenticated API session confirms role admin and reads all 20 tasks,
  seven projects, seven teams and 14 users, matching SQL. Foreign assigned task
  and global analytics are readable.
- Admin dashboard uses global active-task queries and unfiltered navigation;
  ordinary members retain personal queries. Scope keys include role and teamId,
  covering refreshed role/membership changes without stale former-team rows.
- Added read-only admin /users directory and navigation. Non-admins do not mount
  its loader; role loss clears the directory. Existing public DTO schemas strip
  unrelated fields and render user text safely.
- ECC agents implemented dashboard/directory separately and reviewed access.
  Valid regression-first runs preceded the changes. Final checks: 126 frontend
  tests, lint, TypeScript/Vite build; 38 existing backend DataIsolation tests
  pass. Actual Chrome validates all task/project/team lists and /users; the
  mobile directory has all rows without page overflow.
- Task IDs1–19 are historical seed examples assigned to John/Kate/service.bot.
  Task20 belongs to Project1/Team1 and is assigned to the owner's user13.
  service.bot is seeded user12, currently locked/unconfirmed with no team; it
  is the placeholder assignee for tasks17–19.

Updated dist is served locally. No backend source, migration, password, task
assignment or Azure deployment changed. User must sign in again to receive
the new admin claim; the recovered password is unchanged. Changes remain local.
