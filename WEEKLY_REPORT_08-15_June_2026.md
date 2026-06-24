# PowerSoft 365 AI Reports — Weekly Progress Report

**Period:** 08 June 2026 – 15 June 2026  
**Developer:** Nikola Mladenovic  
**Project:** Powersoft.Reporting (ASP.NET Core 6.0 MVC)  
**Production:** https://reports-ai.powersoft365.com  
**Status:** Live and deployed

---

## Executive Summary

This week delivered **4 major features**, **7 enhancements**, and **1 critical permission fix** deployed same-day upon client report. The application was officially rebranded to **"PowerSoft 365 AI Reports"** as agreed in the team meeting. Two new financial reports (Trial Balance and Profit & Loss) bring the total supported reports to **11**, all with full parity to Powersoft365 legacy calculations.

**Key metrics:**
- 8 commits, ~9,000 lines of code added across 80+ files
- Test suite: 179 tests, 100% passing
- Build: 0 errors, 0 warnings
- Deployed to production on 10 June 2026

---

## 1. New Financial Reports

### 1.1 Trial Balance Report
**Commit:** `de43f98` | **~3,061 lines added across 19 files**

Complete financial reporting module:
- **Data Layer** — Recursive CTE traversal of Chart of Accounts (`tbl_coa`, `tbl_detailac`, `tbl_payments`) with proper debit/credit sign conventions and account level depth filtering
- **All export formats** — Excel, CSV, PDF, Print Preview — numbers identical to screen
- **Scheduling** — Full schedule CRUD, automated execution via background service
- **AI Analysis** — Integrated with the AI analyzer for insights on balance patterns
- **Save Layout** — User preferences persisted and restored on next visit
- **Authorization** — Action IDs 6043/6044 (View/Schedule) mapped to RENGINEAI module
- **Tests** — INI slug verification + schedule parameter parser tests
- **Legacy parity** — Balance calculations verified against Powersoft365 CloudReports output via direct SQL probes

### 1.2 Profit & Loss Report with Year-over-Year Comparison
**Commit:** `2b3f395` | **~1,357 lines added across 6 files**

Financial P&L report replicating legacy VB.NET CloudReports behavior:
- **Account grouping** — Sales, Cost of Sales (→ Gross Profit), Income, Expenses (→ Net Profit) using control header codes from legacy `WQR.vb` (`SALESHEA`, `COSTOSHEA`, `INCHEA`, `EXPHEA`)
- **Year-over-Year comparison** — Toggle shows prior period (same dates -1 year) side by side with variance amounts and percentages
- **Opening/Closing Stock** — Optional DR/CR stock value injection into Cost of Sales (mirrors legacy behavior exactly)
- **Suppressed headers** — Headers with zero values can be hidden
- **All exports** — CSV, Excel, PDF, Print Preview with identical figures
- **Legacy verification** — Per-account normalization tested against direct SQL queries, confirmed numerical parity with original VB.NET implementation

---

## 2. Application Rebranding

### 2.1 "PowerSoft 365 AI Reports"
**Commit:** `8b0f00d`

Updated application identity across all touchpoints:
- Browser page titles (all views)
- Login page heading and footer
- Navigation bar brand text
- Scheduled email templates (header, footer, plain-text signature)
- Print preview footers (all reports)
- Document preview watermarks
- Email template editor starter template
- Manual send-email default preview

---

## 3. New UI Features

### 3.1 All Schedules Page (Centralized Schedule Management)
**Commit:** `8b0f00d`

New page showing all scheduled reports for the connected database:
- **Cross-report view** — All 11 report types in one table with report type badges, recurrence info, next/last run, export format, status (active/inactive)
- **Star Rating system** (1–5 stars) — Click to rate schedule importance; persisted to DB
- **Sort order** — Active first, then by star rating (most important on top), then creation date
- **Schema migration** — Automatic `ALTER TABLE ADD StarRating TINYINT NULL` on app startup
- **Navigation** — "Schedules" link in top navbar

### 3.2 AI Token Budget Indicator (Navbar Widget)
**Commit:** `8b0f00d`

Persistent visual indicator in the navigation bar showing AI usage:
- **Design** — Lightning bolt chip/pill (⚡ 36%) matching the database badge style
- **Color-coded** — Green (< 50%), Amber (50–80%), Red (> 80%)
- **Hover interaction** — Icon fills, chip lifts with shadow, popover appears with SVG donut ring showing percentage, token counts, and progress bar
- **Data** — Fetches current month usage vs. limit asynchronously on page load
- **Conditional** — Only shows when database connected and budget configured

### 3.3 Email Recipients — User Picker
**Commit:** `8b0f00d`

Enhanced the Send Email modal across all 11 reports:
- **"Add from users" button** — Opens modal with checkbox list of database users
- **Data source** — Queries psCentral for users linked to current DB who have email addresses
- **Deduplication** — Already-entered emails not duplicated
- **Scope** — Shared component, works for all reports automatically

---

## 4. AI Governance & Reporting

### 4.1 Cross-Tenant AI Usage Report
**Commit:** `660ff22`

Central dashboard for monitoring AI costs across all tenant databases:
- Stored in `psCentral.tbl_RE_AiUsageLog`
- Breakdown by company, report type, and user
- Restricted to webmaster (ranking=1) only
- Configurable date range

### 4.2 AI Cost Billing Markup
**Commit:** `22347ed`

Addresses team feedback about showing billed cost vs. raw API cost:
- System Settings → new "AI Billing" card with configurable markup factor (e.g. 5x)
- AI Usage Report conditionally shows both "API Cost (raw)" and "Billed (Nx)" columns
- Informational banner explains the markup to viewers

### 4.3 Date Format Standardization
**Commit:** `08eb44f`

- All dates standardized to `dd/MM/yyyy`
- Request localization configured for `en-GB` culture

---

## 5. UX Improvements

### 5.1 Seasons Ordering
**Commit:** `22347ed`

- Seasons now display from most recent to oldest in all dropdowns (was alphabetical)
- Addresses direct feedback from team

### 5.2 Accounting Navigation
**Commit:** `22347ed`

- New "Accounting" dropdown in top navbar with Trial Balance + Profit & Loss
- Dashboard cards for both reports (replacing "Coming Soon" placeholder)
- Permission-gated visibility

---

## 6. Critical Bug Fix — Permission System

### 6.1 Legacy "View PowerReports" Action Recognition
**Commit:** `3dff26b` | **Deployed same-day upon client report**

**Issue reported by:** Stelios Argyrou (Splash) via Christina Trofin  
**Symptom:** User could only see Average Basket despite having "View PowerReports" permission  

**Root cause:** Our application checked only granular per-report action IDs (6025–6046) but did not recognize the legacy generic action "View PowerReports" (ID 5100) that Christina had correctly assigned via the Powersoft365 admin UI.

**Fix:** Added fallback in `IsActionAuthorizedAsync` — if a role has action 5100, all reports are accessible. No database changes needed on client side.

**Resolution time:** Reported → diagnosed → fixed → deployed → confirmed within same day.

---

## Technical Metrics

| Metric | Value |
|--------|-------|
| Commits this period | 8 |
| Files modified/created | 80+ |
| Lines added | ~9,000 |
| Lines removed | ~3,000 (refactoring) |
| Net new lines | +6,000 |
| Unit tests | 179 (100% pass) |
| Build errors | 0 |
| Build warnings | 0 |
| Total reports supported | 11 (was 9) |
| Schema migrations (auto) | 3 new (idempotent) |
| Production deploys | 2 (10 June) |

---

## Deployment Status

| Environment | Status |
|-------------|--------|
| Production (reports-ai.powersoft365.com) | ✅ Live — deployed 10 June 2026 |
| GitHub (origin) | ✅ Pushed |
| GitHub (powersoftapps) | ✅ Pushed |
| SQL Seed (psCentral) | ⚠️ Pending on production — needed for new action IDs on fresh installs |

---

## Plan: Next Step### Priority 1 — Cash Flow Report (per George's request)
- Awaiting Power BI layout/screenshots from Marinos
- Will use existing infrastructure: COA control header `CASHEA`, `tbl_detailac`, `tbl_payments`
- Estimated: 2–3 days after receiving reference layout
- Monthly breakdown variant included

### Priority 2 — P&L Month-by-Month Variant
- Awaiting layout example from Marinos
- Same account hierarchy as current P&L, with monthly column breakdown (Jan–Dec)

### Priority 3 — Per-User AI Usage View
- Allow each user to see their own AI consumption
- Currently only webmaster sees global usage; regular users see nothing
- Will add "My AI Usage" accessible to all authenticated users

### Priority 4 — AI Follow-up from Email
- Deep link in scheduled AI analysis emails
- Opens report with cached analysis for follow-up questions
- Requires: S3 analysis storage, unique URL token, follow-up chat UI

---

---
