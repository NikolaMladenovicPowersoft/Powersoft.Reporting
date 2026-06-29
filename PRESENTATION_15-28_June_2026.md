# PowerSoft 365 AI Reports — Progress Presentation
## Period: 15 June – 28 June 2026
### Developer: Nikola Mladenovic
### Production: https://reports-ai.powersoft365.com

---

## How to Use This Document

This is your **presentation script**. It follows the exact order you should present in the meeting. Each section has:
- **SAY** — what to tell George (talking points)
- **SHOW** — what to demonstrate live on the app
- **WHY IT MATTERS** — the business justification

Estimated presentation time: **25–35 minutes**

---

## OPENING (2 minutes)

### SAY:

> George, before we go into what's new, I want to note that I sent a detailed progress report on Monday June 15th covering the work from June 8th to 15th. I'm not sure if you had a chance to review it, so I'll briefly touch on the highlights from that period as well before diving into the new work.
>
> The big picture: since we last met on June 8th, the application went from 9 reports to 11, we did a complete rebranding, added centralized schedule management, fixed a critical permission bug reported by a client, and — most recently — I conducted a thorough end-to-end verification of every single functionality across all 11 reports plus all admin pages. Every button, every export, every filter, every API endpoint.
>
> Let me walk you through everything.

---

## SECTION 1: QUICK RECAP OF JUNE 8–15 WORK (5 minutes)

*If George says he already read the report, skip to Section 2.*

### SAY:

> In the first week after our meeting, four major things happened:

#### 1.1 Two New Financial Reports — Trial Balance & Profit & Loss

> We added two financial reports that bring us to 11 total reports. Trial Balance does a full recursive traversal of the Chart of Accounts with proper debit/credit conventions. Profit & Loss groups accounts into Sales, Cost of Sales, Income, and Expenses — calculating Gross Profit and Net Profit automatically. It also has a year-over-year comparison toggle that shows prior period side by side with variance amounts and percentages.
>
> Both reports have all the standard features: Excel, CSV, PDF export, Print Preview, scheduling, AI analysis, and saved layouts. The numbers are verified against the original Powersoft365 calculations.

### SHOW:
1. Open **https://reports-ai.powersoft365.com**
2. Login, connect to DEMO365MODAPRO1
3. Navigate to **Accounting → Trial Balance** — show the page, generate with default dates
4. Navigate to **Accounting → Profit & Loss** — generate, then toggle "Compare to Last Year" to show the side-by-side view

#### 1.2 Application Rebranding

> The application was rebranded to "PowerSoft 365 AI Reports" as agreed in our meeting. This is reflected everywhere — the login page, the navigation bar, page titles, email templates, print footers.

### SHOW:
- Point to the login page title and navbar brand text

#### 1.3 All Schedules Page + AI Token Budget + Email User Picker

> Three UI features: a centralized schedule management page where you can see all scheduled reports in one place with a star rating system; an AI token budget indicator in the navbar that shows usage percentage with color coding; and an "Add from users" button in the Send Email modal that lets you pick recipients from database users.

### SHOW:
1. Click **Schedules** in the top navbar — show the All Schedules page with ratings
2. Point to the **AI token budget indicator** (lightning bolt chip) in the navbar, hover to show the donut chart
3. Open any report → click **Send to Email** → click "Add from users" to show the user picker

#### 1.4 Permission Fix (Client-Reported Bug)

> Stelios from Splash reported that his user could only see Average Basket despite having the "View PowerReports" permission in Powersoft365. We diagnosed it, fixed it, and deployed it the same day. The issue was that we only checked granular per-report action IDs but didn't recognize the legacy generic "View PowerReports" action that Christina had assigned.

### SAY:
> That covers the June 8-15 period. Now let me show you what's new since then.

---

## SECTION 2: COMPREHENSIVE QUALITY VERIFICATION (8 minutes)

*This is the most important section for George — it directly addresses his April 21st mandate about thorough evaluation and testing.*

### SAY:

> George, I want to address something you raised in April — the importance of thoroughly evaluating and testing everything that AI tools help produce. I took that very seriously. Before writing any new features, I invested significant time in a **systematic end-to-end verification** of the entire application.
>
> This wasn't casual testing. I wrote automated test scripts that programmatically test every single functionality — logging in, connecting to the database, and then going through every report, every button, every filter, every export, every API endpoint. Let me show you the scope.

### SHOW:
- Open a terminal or notepad with these results prepared:

### SAY (while showing):

> **Phase 1 — Average Basket Deep Dive**: I tested all 30 UI controls on the Average Basket report. Every filter combination, every grouping option, every checkbox. All 10 sortable columns in both ascending and descending. Pagination with different page sizes. Excel, CSV, PDF exports. Print Preview with different groupings. Schedule API. Email functionality. AI analysis status. Layout save/load. All dimension endpoints — categories, departments, brands, seasons, suppliers, models, colours, size groups, fabric. Item search. Store filtering. Column visibility. **Result: 122 out of 122 tests passed.**
>
> **Phase 2 — Purchases vs Sales Deep Dive**: Similar depth — all generation modes, all grouping combinations, all display options, exports, print preview, schedule, email. **Result: 105 out of 105 tests passed.**
>
> **Phase 3 — All Remaining Reports**: Batch verification of Catalogue, Pareto, Charts, Cancel Log, Below Min Stock, Prospect Clients, Offers Report, Trial Balance, and Profit & Loss. Page loads, data generation, exports, print previews, schedule management, layout persistence, email functionality. This is where I discovered the three issues I subsequently fixed.
>
> **Phase 4 — Full Application Verification**: After the fixes, I ran a comprehensive test across the entire application — not just reports, but all admin pages, settings, email templates, schedule management, session handling, all API endpoints. **106 endpoints tested, all passing.**

### WHY IT MATTERS:
> This verification process is exactly what you asked for. Every AI-generated piece of code has been verified against the database, against the UI, and against expected behavior. I have the test scripts and results to prove it. This isn't "I clicked around and it looked okay" — it's systematic, repeatable verification.

---

## SECTION 3: BUGS FOUND AND FIXED (5 minutes)

### SAY:

> The verification uncovered three issues, all of which are now fixed and deployed.

#### 3.1 Purchases vs Sales Export Staleness Bug (Critical)

> **What was wrong**: When a user generates the Purchases vs Sales report, then changes filters — say, changes the date range or toggles the VAT checkbox — and then clicks Export to Excel, CSV, or PDF, the export would use the **old** filter values, not the ones currently showing on screen. The same issue affected Print Preview, Send to Email, and the AI analysis. Essentially, the JavaScript was reading server-rendered values instead of live form values.
>
> **Impact**: Users could get exports with different data than what they see on screen. This is a data integrity issue.
>
> **Fix**: I created a centralized `collectPsParams()` JavaScript helper function that reads all filter values from the live DOM — dates, report mode, grouping, checkboxes, store selections, item selections. All six affected functions now use this helper instead of the stale server values.

### SHOW:
1. Open **Purchases vs Sales**
2. Generate with dates Jan–Dec 2024
3. Change the date to Jan–Jun 2025
4. Click **Export CSV** — show that the downloaded file reflects the NEW dates
5. Point out: "Before the fix, this would have exported Jan–Dec 2024 data"

#### 3.2 Charts Print Preview Button Missing

> **What was wrong**: The Charts report had a working Print Preview endpoint on the server — the controller action existed and worked. But there was no button in the UI to trigger it. Every other report had a Print button, Charts didn't.
>
> **Fix**: Added the Print button and the JavaScript function to open the preview in a new tab.

### SHOW:
1. Open **Charts** → Generate a chart
2. Click the new **Print** button — show the print preview opens

#### 3.3 Below Min Stock — Missing Export Formats

> **What was wrong**: Below Min Stock only had a client-side CSV export. No Excel, no PDF, and the CSV was generated in the browser rather than on the server like all other reports.
>
> **Fix**: Added three server-side export endpoints (Excel, CSV, PDF) with proper formatting — the Excel has color-coded rows for items with negative differences, the PDF uses landscape layout for all the columns. Replaced the single CSV button with an Excel/CSV/PDF button group matching all other reports.

### SHOW:
1. Open **Below Min Stock** → the data loads automatically
2. Show the **Excel / CSV / PDF** button group
3. Click **Excel** — open the downloaded file, show formatted headers and color coding
4. Click **PDF** — show the landscape PDF with all columns

#### 3.4 AI Analysis Report Type in Scheduler (Background Fix)

> **What was wrong**: When scheduled reports include AI analysis, the scheduler was generating the wrong data for P&L, Trial Balance, and Catalogue. It was falling back to Average Basket data, so the AI insights were based on completely wrong numbers.
>
> **Fix**: Added explicit handling for each report type in the scheduler's AI analysis pipeline.

### SAY:
> This one wasn't visible in the UI but would have produced incorrect AI insights in scheduled emails. Caught it during code review.

---

## SECTION 4: TECHNICAL METRICS (2 minutes)

### SAY:

> Let me give you the numbers for this period:

| Metric | Value |
|--------|-------|
| **Commits (June 15–28)** | 2 targeted commits |
| **Files modified** | 11 |
| **Lines added** | ~650 |
| **Lines removed/refactored** | ~234 |
| **Unit tests** | 179 (100% passing) |
| **Build** | 0 errors, 0 warnings |
| **E2E tests run** | 106 endpoints verified |
| **Report-specific tests** | 227+ individual checks (AB: 122, PS: 105) |
| **Production** | Deployed and verified June 24 |
| **Push targets** | Both GitHub repos (origin + powersoftapps) |

> The lower line count compared to previous weeks is intentional. This period was focused on **quality assurance and verification** rather than new features. Writing 650 lines of fixes is worth more than writing 5,000 lines of untested features.

---

## SECTION 5: LIVE DEMO — FULL WALKTHROUGH (10 minutes)

### SAY:
> Let me do a quick live walkthrough of the entire application as it stands today.

### SHOW (in this order):

**1. Login Page**
- Show branding "PowerSoft 365 AI Reports"
- Login with credentials

**2. Database Selection**
- Show company dropdown, database list
- Connect to demo database

**3. Reports Dashboard**
- Show all 11 report cards
- Point out the Accounting section (Trial Balance, P&L)

**4. Average Basket Report**
- Generate with default filters
- Change breakdown to Weekly, generate again
- Sort by a column
- Change page size
- Export to Excel
- Show Print Preview

**5. Purchases vs Sales Report**
- Generate with Category grouping
- Toggle Show Profit checkbox
- Export CSV (demonstrate the staleness fix works — filters respected)

**6. Catalogue**
- Generate with default

**7. Charts & Dashboards**
- Generate a chart
- Change chart type (bar → pie)
- Click the new **Print** button

**8. Below Min Stock**
- Show data auto-loading
- Click **Excel** export (new!)
- Click **PDF** export (new!)

**9. Trial Balance**
- Generate with current year dates
- Show account hierarchy

**10. Profit & Loss**
- Generate
- Toggle "Compare to Last Year" — show variance columns

**11. Settings (Admin)**
- **Database Settings** — show scheduler toggle, token budget, retention
- **System Settings** — show SMTP config, global scheduler, AI markup
- **Email Templates** — show template list, click to edit one
- **AI Usage Report** — show cross-tenant usage

**12. Schedules**
- Click **Schedules** in navbar
- Show All Schedules page with star ratings
- Click **Logs** — show execution history

**13. AI Token Budget**
- Point to the navbar indicator
- Hover to show the donut chart popup

---

## SECTION 6: CURRENT APPLICATION STATUS (2 minutes)

### SAY:

> To summarize where we stand:

| Aspect | Status |
|--------|--------|
| **Total reports** | 11 (9 operational + 2 financial) |
| **All reports have** | Generate, Excel, CSV, PDF, Print, Schedule, Email, AI Analysis |
| **Admin pages** | Database Settings, System Settings, Email Templates, AI Usage |
| **Schedule system** | All 11 reports schedulable, centralized management, execution logs |
| **Permission system** | Role-based + legacy fallback, per-report granularity |
| **AI integration** | Analysis on all reports, token budgeting, cost tracking |
| **Production** | Live at reports-ai.powersoft365.com |
| **Test coverage** | 179 unit tests + 106 E2E endpoint tests |
| **Code quality** | 0 errors, 0 warnings on build |

---

## SECTION 7: WHAT'S NEXT (2 minutes)

### SAY:

> Looking ahead, the priorities remain as we discussed:

1. **Cash Flow Report** — Waiting for the Power BI layout/screenshots from Marinos. Infrastructure is ready — we have the COA control headers and payment tables mapped. Estimated 2–3 days once I have the reference layout.

2. **P&L Month-by-Month Variant** — Also waiting for Marinos' layout example. Same account hierarchy as current P&L but with monthly column breakdown (Jan–Dec).

3. **Per-User AI Usage** — Allow regular users to see their own AI consumption. Currently only webmaster sees the global report.

4. **AI Follow-up from Email** — Deep links in scheduled AI analysis emails that let users continue the conversation.

> Is there anything specific you'd like me to prioritize, or any feedback on what you've seen today?

---

## CLOSING

### SAY:

> To wrap up — the focus this period was deliberately on quality over quantity. Your feedback in April about thorough evaluation and testing shaped this approach. Every report, every button, every export has been systematically verified. The three bugs I found and fixed prove that this verification process works and catches real issues before they reach end users.
>
> The application is stable, fully deployed, and ready for the next phase of feature development.

---

## APPENDIX A: Full Verification Results Summary

### Average Basket — 122/122 PASS
Covers: 30 UI controls, all filter combos, 10 sortable columns (ASC+DESC), pagination (25/50/100/200), Excel/CSV/PDF exports, Print Preview, Schedule API, Email, AI status, Layout save/load, 10 dimension endpoints, Store filtering, Column visibility, Item search.

### Purchases vs Sales — 105/105 PASS
Covers: All generation modes, all grouping levels (Primary/Secondary/Third), 7 display option checkboxes, exports, print preview, schedule, email, transaction drill-down, stock position dialog, document preview.

### All Other Reports — Batch Verified
Catalogue, Pareto 80/20, Charts, Cancel Log, Below Min Stock, Prospect Clients, Offers Report, Trial Balance, Profit & Loss — page load, data generation, all export formats, print preview, schedule management, email.

### Full Application — 106/106 Endpoints PASS
- Authentication (7/7): Login, DB selection, Connect, Disconnect, AccessDenied, Privacy
- Dashboard (12/12): Reports Index + all 11 report cards
- All Report Pages (11/11): All load correctly with expected UI elements
- Settings (15/15): DB Settings, System Settings, Email Templates, AI Usage, Edit Template
- Schedule Management (15/15): AllSchedules page, Logs, 11 per-report schedule APIs
- Email Templates (3/3): Template list, filtered list, recipients
- Shared APIs (20/20): Stores, 9 dimensions, search, AI status, budget, layouts, presets, entity detail
- Print Previews (7/7): All report print preview endpoints
- Session Management (2/2): Disconnect + redirect
- Data Generation (14/14): All report data endpoints return valid data

---

## APPENDIX B: Commits Detail (June 15–28)

### Commit `de06b55` — June 24, 2026
**fix: PS export staleness bug, add Charts print button, add BMS Excel/CSV/PDF exports**

Files: 7 | +307 / -61 lines

- `PurchasesSales.cshtml` — New `collectPsParams()` helper, refactored 6 functions to read live DOM
- `Charts.cshtml` — Added Print Preview button + `printChartPreview()` JS function
- `BelowMinStock.cshtml` — Replaced single CSV button with Excel/CSV/PDF button group
- `ReportsController.cs` — Added `RunBmsExportQuery()`, `ExportBmsCsv`, `ExportBmsExcel`, `ExportBmsPdf`
- `CsvExportService.cs` — Added `GenerateBelowMinStockCsv()`
- `ExcelExportService.cs` — Added `GenerateBelowMinStockExcel()` with color-coded rows
- `PdfExportService.cs` — Added `GenerateBelowMinStockPdf()` in landscape layout

### Commit `bb95b06` — June 24, 2026
**fix: correct AI analysis report type in scheduler for P&L, TrialBalance, Catalogue**

Files: 4 | +343 / -173 lines

- `ScheduleExecutionService.cs` — Added explicit AI analysis branches for ProfitLoss, TrialBalance, Catalogue
- `AverageBasketRepository.cs` — Minor diagnostic change
- `WEEKLY_REPORT_08-15_June_2026.md` — Report formatting
- `_SQL/DIAG_AB_LY_Store.sql` — Diagnostic SQL for LY comparison investigation
