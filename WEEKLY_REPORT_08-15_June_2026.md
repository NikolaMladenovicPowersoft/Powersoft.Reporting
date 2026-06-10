# PowerSoft 365 AI Reports — Weekly Progress Report

**Period:** 08 June 2026 – 15 June 2026  
**Developer:** Nikola Mladenovic  
**Project:** Powersoft.Reporting (ASP.NET Core 6.0 MVC)  
**Production:** https://reports-ai.powersoft365.com

---

## Executive Summary

This week delivered **4 major features**, **6 enhancements**, and **multiple infrastructure improvements**. The application was also officially rebranded from "Powersoft Reporting" to **"PowerSoft 365 AI Reports"** as agreed in the team meeting. Two new financial reports (Trial Balance and Profit & Loss) bring the total supported reports to **11**, with full parity to Powersoft365 legacy.

**Key metrics:**
- 10 commits, ~8,700 lines of code added across 80+ files
- Test suite: 179 tests, 100% passing
- Build: 0 errors, 0 warnings
- Deployed to PowersoftApps remote

---

## Features Delivered

### 1. Trial Balance Report (Full Implementation)
**Commit:** `de43f98` | **Files:** 19 | **Lines added:** ~3,061

Complete financial reporting module with:
- **Repository layer** — Recursive CTE traversal of the Chart of Accounts (tbl_coa, tbl_detailac, tbl_payments) with proper debit/credit sign conventions
- **Controller actions** — Generate, Excel/CSV/PDF export, Print Preview, Send Email, AI Analyze, Schedule, Save Layout
- **View** — Interactive filter panel (date range, account level depth, show zero balances toggle), sortable results table with expandable account groups, grand totals
- **Print Preview** — Clean print-ready layout with account hierarchy
- **Scheduler integration** — Full schedule CRUD with parameter serialization
- **Authorization** — Action IDs 6043/6044 (View/Schedule) mapped to RENGINEAI module
- **Tests** — INI slug tests + schedule parameter parser tests
- **Legacy parity** — Balance calculation matches Powersoft365 CloudReports output exactly (verified via SQL probes)

### 2. Profit & Loss Report with Year-over-Year Comparison
**Commit:** `2b3f395` | **Files:** 6 | **Lines added:** ~1,357

Financial P&L report replicating legacy VB.NET CloudReports behavior:
- **Account grouping** — Sales, Cost of Sales (→ Gross Profit), Income, Expenses (→ Net Profit) with control header codes from `WQR.vb`
- **Year-over-Year** — Toggle "Compare to last year" shows prior period (same dates -1 year) side by side with variance amounts and percentages
- **Opening/Closing Stock** — Optional DR/CR stock value injection into Cost of Sales (mirrors legacy behavior)
- **Suppressed headers** — Headers with zero values can be hidden for cleaner output
- **All export formats** — CSV, Excel, PDF, Print Preview with identical numbers to screen
- **Legacy verification** — Per-account normalization tested against direct SQL queries to confirm numerical parity

### 3. All Schedules Page (Centralized Schedule Management)
**Commit:** `d3b27e8` | **Files:** 16 (partial)

New page showing **all scheduled reports** for the connected database in one place:
- **Cross-report view** — Displays schedules from all 11 report types with report type badge, recurrence info, next/last run dates, export format, status
- **Star Rating system** (1–5 stars) — Click to rate importance of schedules; persisted to database (`StarRating` column added to `tbl_ReportSchedule`)
- **Sort order** — Active schedules first, then by star rating (most important on top), then by creation date
- **Schema migration** — Automatic `ALTER TABLE ADD StarRating TINYINT NULL` on app startup
- **Navigation** — Accessible from top navbar ("Schedules" link between Reports and Logs)

### 4. AI Token Budget Indicator (Navbar Widget)
**Commit:** `d3b27e8` | **Files:** 16 (partial)

Persistent visual indicator in the navigation bar:
- **Chip/pill design** — Lightning bolt icon + percentage text, styled as a translucent pill matching the database badge aesthetic
- **Color-coded** — Green (< 50%), Amber (50–80%), Red (> 80%) for instant status recognition
- **Hover interaction** — Icon transitions from outline to fill; chip lifts with shadow; popover appears with:
  - Large SVG donut ring with percentage inside
  - "181,035 / 500,000 tokens" usage text
  - Thin progress bar with matching color
- **Data source** — Fetches `/Reports/GetTokenBudget` endpoint asynchronously on page load
- **Conditional display** — Only visible when database is connected and token budget is configured (limit > 0)

---

## Enhancements

### 5. Application Rebranding — "PowerSoft 365 AI Reports"
**Commit:** `d3b27e8`

Updated application name across all touchpoints:
- Page titles (all views via `_Layout.cshtml`)
- Login page heading and footer
- Navbar brand text
- Email templates (schedule emails, manual send emails)
- Print preview footers
- Document preview watermarks
- Email template editor starter template

### 6. Email Recipients — User Selection from Database
**Commit:** `d3b27e8`

Enhanced the shared Send Email modal with "Add from users" functionality:
- **Button** — "Add from users" below recipients input in Send Email modal
- **User picker modal** — Checkbox list of users who have access to the current database (fetched from psCentral `tbl_User` + `tbl_RelUserDB`)
- **Deduplication** — Already-entered emails are not duplicated when adding from picker
- **Scope** — Works across all 11 reports that use the shared `_SendEmail.cshtml` partial
- **Endpoint** — `GET /Reports/GetDatabaseUsers` queries psCentral for users linked to current DB with non-empty email

### 7. AI Cost Billing Markup (Configurable)
**Commit:** `63859c3`

Addresses feedback from team meeting about showing billed cost vs. raw API cost:
- **System Settings** — New "AI Billing" card with configurable markup factor (default 1.0x, recommended 5x)
- **AI Usage Report** — When markup > 1.0, shows both "API Cost (raw)" and "Billed (Nx)" columns with informational banner
- **Data model** — `SystemSettings.AiCostMarkup` property with `FromDictionary`/`ToSettingsList` persistence

### 8. Seasons Ordering (Most Recent First)
**Commit:** `63859c3`

- Changed `DimensionRepository.GetSeasonsAsync()` to `ORDER BY pk_SeasonID DESC`
- All dropdowns across all reports now show seasons from newest to oldest
- Addresses direct feedback from colleague

### 9. Cross-Tenant AI Usage Report
**Commit:** `660ff22`

- Central reporting of AI token consumption across all tenant databases
- Stored in `psCentral.tbl_RE_AiUsageLog`
- Breakdown by company, report type, and user
- Restricted to webmaster role (ranking=1) only

### 10. AI Cost Governance (2-Tier Guard System)
**Commits:** `09fd5f3`, `bc3af4e`

- **Soft limit** (default $0.10) — Warning shown to user, analysis proceeds
- **Hard limit** (default $0.25) — Analysis blocked with clear message
- **Monthly token budget** — Hard cap on total monthly usage; blocked when exceeded
- **Admin UI** — Database Settings page allows per-DB configuration of cost limits
- **Per-analysis cost estimation** — `AiCostEstimator` service calculates expected cost before API call

---

## Infrastructure Improvements

### 11. Save Layout Framework (All Reports)
**Commit:** `c5cc9ae`

- Shared `_SaveLayout.cshtml` partial component
- Users can save filter/display preferences per report
- Layout restored on next visit
- INI-based storage (`tbl_IniHeader`/`tbl_IniDetail`)
- Standardized nested grouping logic across reports

### 12. Searchable Control Panel
**Commit:** `3d89a97`

- Company and database dropdowns now have live search
- Custom `searchable-select.js` (234 lines) replaces native `<select>`
- Critical UX improvement for clients with many companies/databases

### 13. Date Format Standardization
**Commit:** `08eb44f`

- All date displays standardized to `dd/MM/yyyy` format
- Request localization configured for `en-GB` culture
- Consistent across all exports, views, and email content

### 14. SQL Seed Script for Module Permissions
**File:** `_SQL/SeedReportingModule_psCentral.sql`

- Action IDs 6025–6046 registered for RENGINEAI module
- Trial Balance (6043/6044) and Profit & Loss (6045/6046) added
- Idempotent — safe to run multiple times

---

## Technical Metrics

| Metric | Value |
|--------|-------|
| Commits this week | 10 |
| Files modified/created | 80+ |
| Lines added | ~8,700 |
| Lines removed | ~3,000 (refactoring) |
| Net lines | +5,700 |
| Unit tests | 179 (100% pass) |
| Build errors | 0 |
| Build warnings | 0 |
| Reports supported | 11 (was 9) |
| Schema migrations | 3 new (idempotent) |

---

## Deployment Status

| Remote | Status | Notes |
|--------|--------|-------|
| `powersoftapps` (PowersoftApps/Powersoft.Reporting.App) | ✅ Pushed | All commits including latest |
| `origin` (NikolaMladenovicPowersoft/Powersoft.Reporting) | ⚠️ Blocked | GitHub Push Protection flagged secrets in `DEPLOY_RULES_FOR_AI.md` — requires admin unblock or file removal |
| Production FTP deploy | ⚠️ Pending | FTP credentials need verification (530 auth error) |

**Action required:** Unblock the push on GitHub (one-click approve at the link GitHub provided) or provide updated FTP credentials for production deployment.

---

## Known Issues / Blockers

| # | Issue | Severity | Action Needed |
|---|-------|----------|---------------|
| 1 | GitHub push protection blocking `origin` remote | Medium | Approve secret bypass or remove `DEPLOY_RULES_FOR_AI.md` from history |
| 2 | FTP credentials returning 530 | **Blocker for deploy** | Provide current FTP password for `xOutsource@64.59.221.100` |
| 3 | P&L Month-by-Month variant | Waiting | Need layout examples from Marinos before implementation |

---

## Plan for Next Week (16–22 June)

### Priority 1 — Production Deployment
- Resolve FTP credentials / GitHub push protection
- Deploy latest build to `reports-ai.powersoft365.com`
- Execute `SeedReportingModule_psCentral.sql` on production
- HTTP smoke tests on production

### Priority 2 — P&L Month-by-Month Report
- Awaiting Marinos layout examples
- Will break down P&L figures into monthly columns (Jan–Dec)
- Same account hierarchy, just horizontal monthly breakdown

### Priority 3 — AI Follow-up from Email
- Deep link in scheduled AI analysis emails
- Opens report with cached AI analysis
- User can ask follow-up questions without re-running AI
- Requires: storage of analysis in S3, unique URL token, follow-up chat UI

### Priority 4 — Additional Enhancements
- Continued UX polish based on team feedback
- Performance optimization for large datasets
- Additional report types as requested

---

## Architecture Overview (for technical team)

```
Powersoft.Reporting (ASP.NET Core 6.0)
├── Core (Models, Interfaces, Constants, Enums, Helpers)
├── Data (Repositories, SQL generation, Schema migration)
│   ├── Central/ — psCentral queries (auth, companies, AI usage)
│   └── Tenant/ — per-company DB queries (reports, schedules, INI)
├── Web (Controllers, Views, Services, ViewModels)
│   ├── Controllers/ — ReportsController (all reports), Settings, Account, Home
│   ├── Services/ — Excel/CSV/PDF export, Schedule execution, AI analysis, Email
│   └── Views/ — Razor views per report + shared partials
└── Tests (179 tests — slug verification, parameter parsing, parity)
```

**Key design decisions:**
- Multi-tenant architecture via session-scoped connection strings
- Schema auto-migration on each database connection (idempotent)
- Role-based access control synced with Powersoft365 permission system
- AI cost governance prevents runaway API expenses
- All numerical outputs verified against legacy VB.NET for exact parity

---

*Report generated: 10 June 2026*  
*Next review: 15 June 2026 (client presentation)*
