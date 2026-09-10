# Donor list replacement

Replace the contents of your existing `donor-list.ts`, `donor-list.html`, and `donor-list.css` in their current project directory. Do not move the component. These download copies are deliverables; your application's files were not available in this workspace and have not been edited.

The component selector, exported component name, `./donor-list.html`, `./donor-list.css`, `../../../../Service/workflow-state.service`, `/assets/data/donors.json`, all six navigation destinations and their query parameters are preserved. No new package, font, or image asset is required. The illustration is a detailed, self-contained vector reconstruction of the supplied close-up, with no blur or reduced opacity. It is not the original source artwork. The artwork sits inside the final Follow-Ups Due widget at every breakpoint, with reserved space beside the text.

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

Desktop has one summary band and the combined contact table. The summary uses four columns on desktop, two columns on tablet, and one column on phones. At 1280px and below the table becomes stacked donor cards, with separate sorting controls and all contact fields and actions retained. No horizontal-scroll wrapper is used. Touch controls, focus outlines and reduced-motion styles are included.

Validated using the locally available Angular 21.2.16 template parser, TypeScript 5.9 transpilation, and isolated checks for search, period and verification filtering, selection, preview, six navigation actions, pagination, empty dates, unchanged paths, and original button-handler coverage. Browser visual checks used a fixture rendering of the actual parsed template at desktop and phone widths. This is not a full Angular 22 build or an end-to-end test of your application; your package manifest, workflow service, app shell and router configuration were not supplied. Run your project's normal build after replacing the files.

## Summary and pagination

Consent Review Due and Verification Pending are removed from the summary only. Consent and verification filters and donor actions remain available. The four widgets are Total Donors, New Donors, Active Donors, and Follow-Ups Due. The illustration stays inside Follow-Ups Due at all screen widths with reserved space beside the text.

Pagination is fixed to 10 donors per page. Previous/next controls and the displayed range use the current valid page even after refreshed data reduces the record count. The last page can contain fewer than 10 rows. Tests with 23 records verify page sizes of 10, 10, and 3, page boundaries, and clamping after data shrinkage.

## Shared UI common check

- Existing command buttons use small primary/outline-secondary classes; Filter and Close use `btn btn-outline-secondary btn-sm`.
- Dropdown menu and offcanvas command buttons include icons. Icon buttons have accessible labels and native hover titles. Eye stays first, followed by existing communication, follow-up and overflow actions.
- Typography consumes the application's `--font-heading`, `--font-other`, `--font-number`, and `--font-size-base` variables. Define these in your existing global theme. No font asset or global theme file was moved.
- Owner, Campaign and Region selectors show an associated option-search input when their original option count exceeds 20. A selected value remains available while searching. Clearing filters clears dropdown searches. Smaller lists keep their normal select control.
- Clicking the offcanvas backdrop closes it; clicking inside does not. The existing Escape close is retained. The donor name and ID each appear once in the drawer.
- This supplied component has no create/edit page, Add/Edit/Save/Cancel workflow, or modal popup. Those rules are not applicable here; no unsupported routes or actions have been invented. Dropdown menus are not modal popups and keep their usual outside-click dismissal.

Responsive fixture checked at 320, 390, 600, 768, 1024, 1280, 1440 and 1740 pixels: no document or table horizontal overflow. Your full application shell was not supplied and remains outside this isolated validation.

## Heading and right-edge correction

The page title uses `var(--font-heading)` and a responsive size derived from `--font-size-base` (28–40px). This is the requested heading-size exception; body text retains `font-size:var(--font-size-base)`. Overview and the empty trend dashes are removed. Four summary items use aligned content rows. The full illustration now occupies a normal grid column in the last widget, with no negative positioning, clipping, blur, or reduced opacity.

The component-owned wrappers reset inherited margin, padding, sizing and positioning so generic app-wrapper/page-wrapper sidebar styles do not widen this page. The original class names, file references, paths, routes, common-check controls, and fixed 10-row pagination remain intact. Parent application layout outside this component still requires integration checking.

The screenshot also shows “Unable to load donors.” This is the existing data-load error from `/assets/data/donors.json`; the visual changes do not repair or replace that data endpoint.
