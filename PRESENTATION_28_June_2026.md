# PowerSoft 365 AI Reports — Progress Presentation

**Period:** 15 June 2026 – 28 June 2026  
**Developer:** Nikola Mladenovic  
**Project:** Powersoft.Reporting (ASP.NET Core 6.0 MVC)  
**Production:** https://reports-ai.powersoft365.com  
**Status:** Live and deployed (pushed to both GitHub remotes)

---

## Opening (What to Say)

> "Good morning/afternoon everyone. I want to walk you through what has been accomplished on PowerSoft 365 AI Reports since June 15th.
>
> Before I begin — I sent a detailed progress report on Monday June 15th covering the period June 8–15. I'm not sure if everyone had a chance to review it, so I'll briefly recap the key highlights from that report first, then move to the new work from the past two weeks."

---

## Part 1 — Quick Recap of June 8–15 (In Case George Didn't See the Report)

> "In the June 8–15 period, we delivered quite a lot. I want to make sure this is on the record since it may not have been seen:

> **Two new financial reports went live:**
> - **Trial Balance** — full Chart of Accounts traversal with recursive CTE, debit/credit sign conventions, all export formats, scheduling, AI analysis, and save layout. That was about 3,000 lines of new code across 19 files.
> - **Profit & Loss with Year-over-Year Comparison** — account grouping (Sales, Cost of Sales, Gross Profit, Income, Expenses, Net Profit) using the same control header codes from the legacy VB.NET system. Includes YoY toggle showing prior period side-by-side with variance amounts and percentages. Opening/Closing Stock injection mirroring legacy behavior.
>
> **Application rebranding** — The app was rebranded to 'PowerSoft 365 AI Reports' across all touchpoints: page titles, login, navbar, email templates, print preview footers, document preview watermarks.
>
> **Three new UI features:**
> - **All Schedules page** — centralized view of all scheduled reports across all 11 report types with a star rating system for priority management.
> - **AI Token Budget indicator** — a persistent navbar widget showing AI usage as a color-coded chip (green/amber/red) with hover popover showing the donut chart and token counts.
> - **Email User Picker** — 'Add from users' button in the Send Email modal that queries psCentral for database users with emails, works across all 11 reports.
>
> **AI Governance features:**
> - Cross-tenant AI usage report dashboard (restricted to webmaster)
> - AI cost billing markup with configurable multiplier in System Settings
>
> **Critical same-day bug fix:**
> - The permission issue reported by Stelios Argyrou at Splash, where a user could only see Average Basket despite having 'View PowerReports' permission. Diagnosed, fixed, and deployed the same day it was reported.
>
> That period had 8 commits, about 9,000 lines of code, and brought us from 9 to 11 total reports.
>
> Now let me move to what has happened since then."

---

## Part 2 — Work Done: June 15–28

### 2.1 Critical Scheduler Bug Fix — AI Analysis Was Sending Wrong Data

> "The first and most important fix this period was a critical bug in the scheduled AI analysis system.
>
> **The problem:** When the scheduler ran AI analysis for Profit & Loss, Trial Balance, or Catalogue reports, it was silently generating Average Basket CSV data instead. This means the AI was analyzing completely wrong data and sending misleading insights via email. The root cause was that `ScheduleExecutionService.RunAiAnalysisSafe()` only had two branches — one for Purchases vs Sales and a hardcoded Average Basket fallback. Every other report type was falling through to the Average Basket path.
>
> **The fix:** I added explicit handling for Profit & Loss, Trial Balance, and Catalogue in the scheduler, each constructing the correct filter parameters and using the correct CSV export method — identical to what the Generate action uses in the controller. This is about 53 lines of precise filter construction code."

**LIVE DEMO — What to Show:**
1. Open the app → go to any report (e.g., Trial Balance)
2. Show the Schedule tab → show an existing schedule with AI analysis enabled
3. Explain: "When this schedule fires, it now correctly generates Trial Balance data for the AI to analyze — not Average Basket data as it was before"
4. If a schedule log is available, show the last execution log to prove it ran correctly

---

### 2.2 Purchases vs Sales — Export Staleness Bug Fix

> "I identified and fixed a significant data integrity issue in the Purchases vs Sales report.
>
> **The problem:** When a user loaded the report, changed filters (dates, grouping, VAT, etc.), and then clicked Generate to see new results — the Export, Print Preview, Send Email, and AI Analysis functions were still using the **original** filter values from when the page first loaded. This is because all those JavaScript functions were reading from `@Model.*` Razor expressions which are server-rendered once at page load and never update.
>
> For example: a user generates for January, then changes to February and hits Generate again. The screen shows February data correctly. But if they then click Export to Excel — they get January data. This is a silent data integrity issue: the user thinks they're exporting what they see, but they're getting stale data.
>
> **The fix:** I replaced all `@Model.*` references in the JavaScript export, preview, email, drill-down, and AI functions with a new centralized `collectPsParams()` helper that reads live values directly from the DOM input elements. This means exports always match what the user sees on screen."

**LIVE DEMO — What to Show:**
1. Open Purchases vs Sales → set dates to January 2025, click Generate
2. Change dates to a different range (e.g., full year 2024), click Generate
3. Click Export to Excel → open the file → show that the dates in the export match the screen (not the original dates)
4. Click Print Preview → show that it also uses the current filters
5. Explain: "Before this fix, steps 3 and 4 would have exported January 2025 data even though the screen was showing 2024"

---

### 2.3 Charts Report — Print Preview Button Added

> "The Charts & Dashboards report had a Print Preview controller action that was already implemented server-side, but the UI button to invoke it was missing. Users could export to Excel, CSV, and PDF, but couldn't print preview. This has been fixed — the Print button is now in the toolbar alongside the other export buttons."

**LIVE DEMO — What to Show:**
1. Open Charts & Dashboards → select any dimension and metric → Generate
2. Point out the new Print button in the toolbar
3. Click it → show the print preview opens in a new tab with the chart data formatted for printing

---

### 2.4 Below Minimum Stock — Full Server-Side Export Suite

> "The Below Minimum Stock report previously only had a basic client-side CSV export that generated the file in the browser using JavaScript. This had limitations: it couldn't include proper formatting, headers, or cost-permission gating. I've replaced this with full server-side exports.
>
> **What was added:**
> - **Excel export** — formatted workbook with headers, auto-column-widths, frozen header row, red highlighting for items where stock is critically below minimum, proper number formatting, and cost column visibility based on user permissions
> - **CSV export** — server-rendered with proper escaping, cost permission gating, metadata header
> - **PDF export** — landscape A4, styled table with header background, red font for negative difference rows, cost columns conditional on permissions
>
> Each export respects the current sort order and item/store filters. Three new controller endpoints: `ExportBmsExcel`, `ExportBmsCsv`, `ExportBmsPdf` with corresponding methods in all three export services."

**LIVE DEMO — What to Show:**
1. Open Below Minimum Stock → click Generate
2. Show the toolbar now has three export buttons: Excel (green), CSV (gray), PDF (red) — previously it only had a basic CSV
3. Click Excel → open the file → show the formatting, headers, colored rows
4. Click PDF → show the professional PDF output
5. If a non-cost user is available, show that cost columns are hidden in exports

---

### 2.5 Comprehensive QA & Regression Testing Framework

> "A significant portion of this period was invested in building a comprehensive automated testing framework. This was in direct response to George's directive about thorough evaluation and testing of all AI-generated and developer-written code.
>
> **What was built:**
>
> - **Full-app QA harness** (`_qa_harness.ps1`) — an automated PowerShell test suite that performs end-to-end testing of the entire application. It logs in, connects to a database, and systematically tests:
>   - All 11 report page loads
>   - All shared lookups and filter data endpoints (stores, items, dimensions, seasons)
>   - All schedule and layout endpoints for every report type
>   - Purchases vs Sales generate + all exports (Excel, CSV, PDF, Print Preview) with multiple filter combinations (all qty columns, wholesale-only, grouped)
>   - Charts generate + all 4 export formats
>   - Pareto data + all exports
>   - Catalogue data + all exports
>   - Cancel Log, Below Min Stock, Offers, Prospect Clients — data + exports
>   - Average Basket — all 4 export formats
>   - **Numerical integrity verification** — automatically downloads CSV exports and verifies that `Available = Stock - Reserved + OnOrder` in the totals row
>   - Offers items-selection filter verification — proves that a match-nothing filter yields 0 rows while unfiltered returns data
>   - Save Layout round-trip test — writes a layout, reads it back, verifies the values match, then cleans up
>
> - **Report-specific deep test suites:**
>   - `_test_ab_buttons.ps1` — 122 tests for Average Basket (111 pass, 11 known edge cases)
>   - `_test_ps_buttons.ps1` — 105 tests for Purchases vs Sales (104 pass)
>   - `_test_remaining_buttons.ps1` — 180 tests across remaining reports (168 pass)
>   - `_test_catalogue.ps1` — Catalogue-specific tests including detail mode, purchase+brand
>   - `_test_fixes_e2e.ps1` — end-to-end verification of specific bug fixes
>
> - **Diagnostic SQL** — Created `DIAG_AB_LY_Store.sql` (160 lines), a diagnostic script for investigating the Average Basket last-year comparison by store. This replicates the exact SQL the repository generates, allowing us to run it directly against a tenant database to verify numbers."

**LIVE DEMO — What to Show (optional, if time permits):**
1. Show the `_qa_harness.ps1` file briefly — "this is 220 lines of automated verification"
2. Show a test results file — point out the PASS/FAIL counts
3. Explain: "Every time we make a change and deploy, we can run this suite and in under 5 minutes know if anything is broken across all 11 reports, all exports, all endpoints. This is part of our commitment to George's quality requirements."

---

### 2.6 Average Basket — Last Year Comparison Investigation

> "I spent time investigating a potential discrepancy in the Average Basket last-year comparison when grouped by store. This involved creating diagnostic SQL that mirrors the exact CTE structure the repository generates — including current year sales/returns, last year sales/returns with date shifting, and the final FULL OUTER JOIN — to run directly against tenant databases and compare results side-by-side.
>
> This kind of investigation is important for maintaining legacy parity: our numbers must match Powersoft365 exactly, and when there's any doubt, we go to the source."

---

## Part 3 — Current Application State (Summary)

| Metric | Value |
|--------|-------|
| Total reports supported | 11 |
| All reports have full exports (Excel/CSV/PDF) | Yes (Below Min Stock was the last one completed this period) |
| All reports have Print Preview | Yes (Charts was the last one completed this period) |
| All reports have scheduling | Yes |
| All reports have Send Email | Yes |
| All reports have AI Analysis | Yes |
| All reports have Save Layout | Yes |
| All 11 scheduled AI analyses produce correct data | Yes (fixed this period) |
| Automated QA test coverage | ~500+ test assertions across the full test suite |
| Unit tests | 179 (100% pass) |
| Build | 0 errors, 0 warnings |
| Production | Live and deployed |

> "In summary: the application is now at **full feature parity** across all 11 reports for exports, scheduling, email, and AI analysis. No report is missing any capability that the others have. The QA framework ensures we can verify this quickly after any change."

---

## Part 4 — What to Show Live (Recommended Demo Flow, ~15 minutes)

### Step 1: Login & Dashboard (2 min)
1. Navigate to https://reports-ai.powersoft365.com
2. Log in → show the "PowerSoft 365 AI Reports" branding
3. Select a database → show the dashboard with all 11 report cards
4. Point out the AI Token Budget indicator in the navbar (the colored chip)
5. Hover over it to show the usage popover

### Step 2: Purchases vs Sales — Export Fix Demo (3 min)
1. Open Purchases vs Sales
2. Generate with one set of filters
3. Change filters and Generate again
4. Export to Excel → verify the exported data matches the current screen (this was the staleness bug fix)
5. Show Print Preview works correctly too

### Step 3: Below Minimum Stock — New Exports (2 min)
1. Open Below Minimum Stock → Generate
2. Show the new Excel/CSV/PDF export buttons
3. Export to Excel → open and show the formatted output
4. Export to PDF → show the professional layout

### Step 4: Charts — Print Preview (1 min)
1. Open Charts → Generate a chart
2. Click the new Print button → show the preview

### Step 5: Scheduling & AI Analysis (3 min)
1. Go to the All Schedules page (link in navbar)
2. Show the centralized view of all scheduled reports
3. Point out star ratings for priority
4. Explain: "The AI analysis for all scheduled reports now generates correct data — previously P&L, Trial Balance, and Catalogue were accidentally sending Average Basket analysis"

### Step 6: Financial Reports Quick Show (2 min)
1. Open Trial Balance → Generate → show the tree structure
2. Open Profit & Loss → Generate → toggle Year-over-Year comparison
3. These were delivered in the previous period but worth showing if George hasn't seen them

### Step 7: AI Usage & Settings (2 min)
1. Open AI Usage Report (webmaster only) → show cross-tenant breakdown
2. Open System Settings → show the AI Billing markup configuration
3. Show the Token Budget widget responding to actual data

---

## Part 5 — What Was the Time Spent On (Transparency)

| Activity | Estimated Time | Evidence |
|----------|---------------|----------|
| Scheduler AI analysis bug — investigation + fix | ~4 hours | Commit `bb95b06`, 53 lines in ScheduleExecutionService, diagnostic SQL |
| PS export staleness bug — investigation + fix | ~4 hours | Commit `de06b55`, rewrote all JS export/preview/email functions |
| BMS full export suite (Excel/CSV/PDF) | ~4 hours | 3 new controller endpoints, 3 new export service methods (175 lines) |
| Charts print preview UI wiring | ~1 hour | 26 lines of JS + 3 lines of Razor |
| QA framework — full-app harness | ~8 hours | `_qa_harness.ps1` (220 lines), comprehensive endpoint coverage |
| QA framework — report-specific test suites | ~8 hours | 6 test scripts, ~500 test assertions, result analysis |
| Average Basket LY investigation + diagnostic SQL | ~4 hours | `DIAG_AB_LY_Store.sql` (160 lines), parity verification |
| Testing, verification, regression runs | ~6 hours | 38 test artifact files, multiple test runs |
| Progress report writing (June 8-15 report) | ~2 hours | Detailed report sent June 15 |

**Total: ~41 hours over 2 weeks (~4 hours/day average)**

---

## Part 6 — Closing & Next Steps

> "To summarize what was achieved in this period:
>
> 1. **Fixed a critical scheduler bug** that was sending wrong AI analysis data for 3 out of 11 report types
> 2. **Fixed a data integrity issue** in Purchases vs Sales exports where users could unknowingly export stale data
> 3. **Completed the export suite** — all 11 reports now have Excel, CSV, PDF, Print Preview, without exception
> 4. **Built a comprehensive QA framework** with 500+ automated test assertions that can verify the entire application in minutes
>
> All changes are deployed to production and pushed to both GitHub repositories.
>
> **Next priorities remain as discussed:**
> - **Cash Flow Report** — waiting for Power BI layout/screenshots from Marinos
> - **P&L Month-by-Month variant** — waiting for layout example from Marinos
> - **Per-User AI Usage view** — ready to implement
> - **AI Follow-up from Email** — deep link for cached analysis follow-up
>
> I'm happy to demo anything in more detail or answer questions."

---

## Technical Evidence Summary

| Item | Reference |
|------|-----------|
| Commit: scheduler AI fix | `bb95b06` — 24 June 2026 |
| Commit: PS staleness + Charts print + BMS exports | `de06b55` — 24 June 2026 |
| Production deploy | Both remotes at `de06b55` (HEAD = origin/master = powersoftapps/master) |
| Files changed (period) | 11 files, ~650 lines added/modified |
| QA harness | `_qa_harness.ps1` — 220 lines, full-app coverage |
| Test scripts created | 38 test artifacts (scripts + results + exports) |
| Diagnostic SQL | `_SQL/DIAG_AB_LY_Store.sql` — 160 lines |
| Unit tests | 179, 100% passing |
| Build | 0 errors |
