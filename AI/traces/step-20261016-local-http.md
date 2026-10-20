# Additional local HTTP access —2026-10-16

Owner explicitly requested plain HTTP for the in-app browser and economical
token use. The native supervisor now binds both `http://localhost:7150` and
`https://localhost:7151`; both readiness endpoints return200. The HTTP site was
opened visibly in-app and its labelled sign-in page verified.

`AllowedBrowserOrigins` permits skipping HTTPS redirection only in Development,
for an explicitly configured loopback HTTP origin and loopback remote address.
Production/Testing defaults remain HTTPS. In this explicit Development mode API
and notifier share distinct `TaskManager.Local.Session` cookies and API uses a
distinct local antiforgery cookie, with SameAsRequest securing HTTPS responses.
HttpOnly, SameSite, exact origin/CSRF checks and the persistent key ring remain.
Normal HTTPS configuration retains the existing `__Host-` names/Secure Always.

Release build passes.72 focused backend cases pass without skips, including14
new local HTTP/security-default cases plus existing authentication, gateway and
origin topology regressions. Actual HTTP admin authentication/API reads pass:
20 tasks,7 projects,7 teams,14 users. Full UI results remain78/78 from the preceding
redesign; no fresh full backend or cloud verification is claimed here.
Actual HTTP browser login, dashboard/directories and mobile user list also pass.

Task-owned services were stopped/rebuilt/restarted through the existing
supervisor; SQL data and credentials were preserved. Private logs are under
`.local/ui-theme-20261015/http-{build,tests,browser}.log` and HTTP TRX under
`http-tests/`. Supervisor status includes `frontendHttp`. No commit/push/deploy.
