# Compact dashboard — 2026-10-20

Owner screenshot prompted a focused responsive polish correction after the
repository restructure. Reloaded governance/current plan and layout sources;
used ECC make-interfaces-feel-better. No business/auth/source-layout change.

RED: fresh browser regression measured sidebar+account header277px at390px,
exceeding the200px compactness bound. The screenshot-width mobile statistic grid
could fit four narrow cards because its8.5rem auto-fit minimum had no column cap.

Changed narrow-screen CSS: brand and labelled language selector share a wrapping
row; scrollable navigation follows; account bar uses62px instead of72px minimum.
Desktop language selector stays in the bottom sidebar footer. The mobile label
remains accessible through its existing associated label. Statistics cap at two
columns and collapse to one as enlarged text needs more width. Existing control
targets, focus, colors and navigation scrolling are retained.

Production UI build and16 focused browser executions passed: language persistence,
theme/contrast, navigation/badge visibility, fallback fonts and200% text reflow.
The focused regression was then strengthened to reproduce the screenshot's admin
role at390/684/760px in both languages, capture synthetic visual evidence, and
assert single-column statistics/no document overflow at320px with200% text.
Final admin regression/type/lint results follow below.

Final: both workspace type checks and ESLint PASS. Strengthened admin regression
passes2/2 projects, including both languages/all target widths and320px200% text.
Synthetic684px screenshot visually reviewed: compact aligned brand/language row,
scrollable nav, account actions, readable2x2 statistic cards. Existing16-case
layout suite passed before the strengthened admin fixture; assertions retained.
Localhost serves the rebuilt production CSS. Existing desktop layout and product
logic remain unchanged. No backend rerun needed for this CSS-only product change.
ECC self-check: screenshot concern resolved; labels stay programmatically named,
44px targets retained, font enlargement reflows. No commit/push performed.
