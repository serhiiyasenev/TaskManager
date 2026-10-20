# Favicon and sidebar language — 2026-10-17

Owner requested a favicon and the language switch at the lower left.

- Added local SVG amber/check favicon matching the brand; index references it.
- Moved the workspace selector into the sidebar footer, above its existing copy.
- Kept its visible accessible label, persisted UK/EN selection and auth placement.
- Compact sidebar keeps the selector below navigation; obsolete header styles removed.
- No API, authentication, database or business-data changes.

Verification: lint and production build pass; 12 focused Playwright desktop/mobile
checks pass, including locale persistence, served favicon content type, footer
position, keyboard/contrast, navigation and 200% text reflow. Actual signed-in
native admin renders at 1440x900 and the default compact viewport inspected.
Published dist is served by the existing local runtime without restarting it.

Private logs and rendered proof: `.local/sidebar-language-20261017/`.
No further work required for this request.
