# Donor list replacement

Replace the contents of your existing `donor-list.ts`, `donor-list.html`, and `donor-list.css` in their current project directory. Do not move the component. These download copies are deliverables; your application's files were not available in this workspace and have not been edited.

The component selector, exported component name, `./donor-list.html`, `./donor-list.css`, `../../../../Service/workflow-state.service`, `/assets/data/donors.json`, all six navigation destinations and their query parameters are preserved. No new package, font, or image asset is required. The illustration is a detailed, self-contained vector reconstruction of the supplied close-up, with no blur or reduced opacity. It is not the original source artwork. Below 1391px it is hidden so that it cannot overlap the metrics.

The component uses standalone Angular, signals, computed state, OnPush, host event bindings, and built-in `@if`, `@for`, and `@switch`. These are current Angular APIs; see [Angular control flow](https://angular.dev/guide/templates/control-flow) and [release information](https://angular.dev/reference/releases). This is component replacement code, not a workspace dependency upgrade.

## Behavior

- Refresh and retry retain the existing donor loading and workflow seeding.
- Export retains CSV, Excel-compatible `.xls`, and browser print-to-PDF behavior. Selected donors take priority over filtered donors, as before.
- Search, owner/campaign/region filters, engagement/verification/consent filters, sorting, page size and pagination remain connected to their original handlers.
- Selection checkboxes and select-page remain available. Individual selection opens the existing preview drawer.
- Eye, communication, follow-up, donation amount, verification, consent and per-donor export actions retain their original destinations/behavior. The previously commented donor overflow menu is now visible.
- The header overflow offers select/deselect page, reset search/filters and last-refresh information.
- The new period selector filters the list by **donor creation date**, inclusively from local midnight through now. No period means all records; the New Donors KPI then retains the original rolling 30-day count. The other KPIs retain their original all-record scope.
- Missing or invalid donation dates render as an em dash.

## Reference data differences

The screenshot displays fields absent from the supplied interface. Optional `donationCount`, `donationType` (`Recurring` or `One-time`), and `givingGrowthPercent` display only when supplied by your data. No count or percentage is inferred from donation amounts. KPI growth is shown as an em dash because comparative period data was not supplied. Verification badges use the actual `verificationStatus`; the screenshot's contradictory Verified/Pending labels are not hardcoded.

## Responsive layout and validation

Desktop has one summary band and the combined contact table. Tablet uses three summary columns with a horizontally scrollable table. At 760px and below, the summary uses two columns and donors become stacked cards, retaining all contact fields and actions. Touch controls, focus outlines and reduced-motion styles are included.

Validated using the locally available Angular 21.2.16 template parser, TypeScript 5.9 transpilation, and isolated checks for search, period and verification filtering, selection, preview, six navigation actions, pagination, empty dates, unchanged paths, and original button-handler coverage. Browser visual checks used a fixture rendering of the actual parsed template at desktop and phone widths. This is not a full Angular 22 build or an end-to-end test of your application; your package manifest, workflow service, app shell and router configuration were not supplied. Run your project's normal build after replacing the files.
