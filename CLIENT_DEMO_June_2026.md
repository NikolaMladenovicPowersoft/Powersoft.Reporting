# Powersoft Reporting AI — Progress Demo (1–8 June 2026)

> **Napomena (Nikola):** struktura je **What I say** (šta kažeš klijentu) + **What I show** (šta klikćeš/pokazuješ). Sve je na engleskom jer je demo za GM. Na dnu je **Appendix** sa commit hash-evima po temi — to je tvoja "odbrana sa podacima" ako GM traži dokaz.
>
> **Legenda statusa:**
> - 🟢 **DEPLOYED** — pushovano na oba GitHub remota, live (do `cc86308`, 05.06).
> - 🟡 **IN PROGRESS (local)** — gotovo i build/lint clean lokalno, **još nije deployed**.
>
> **Ako demo ide na produkciji** (`reports-ai.powersoft365.com`) → vidiš samo 🟢 stvari. 🟡 stvari pokazuješ sa `dotnet run` lokalno, ili ih prvo deploy-uješ.

---

## 0. Opening (60 sec)

**What I say:**
> "Over the past week the focus was on three things: **correctness** (the numbers in every export match the screen and the database), **consistency** (every report now uses the same building blocks — filters, scheduling, email, AI), and **security** (cost and supplier data is now gated per user role). I'll walk through each, show it live, and at the end show what's still in progress on my machine."

**What I show:** Dashboard (`/`) — point out that the sidebar now shows only the reports the logged-in user is allowed to see.

---

## 1. Security & Permissions Layer 🟢

**Problem it solves:** Sensitive **cost / profit / margin** and **supplier** data was visible to every user and leaked into exports, print, scheduled emails and AI. In a multi-tenant business this is a confidentiality risk.

**What I say:**
> "We added a permission layer on two sensitive dimensions: **View Cost** and **View Supplier**. It's enforced **server-side** — not just hidden in the UI — so even a hand-crafted URL or a scheduled email can't leak cost to a restricted user. It covers the screen, all exports (CSV/Excel/PDF), print preview, Send-to-Email and the background scheduler."

**What I show:**
1. Login as a **full-access** user → open **Catalogue** → cost/profit/margin columns visible.
2. Login as a **restricted** user (no-cost) → same report → cost/profit/supplier columns gone; "Report On" forced to *Sale*; Supplier grouping option removed.
3. Export to Excel as the restricted user → open file → **no cost columns** in the file either.
4. Mention: dashboard/sidebar hides reports the role can't open (no more "visible link → Access Denied").

**Defend with data:** Server-side enforcement point per report; **173/173 tests pass** including 8 export-stripping tests proving cost/supplier/profit are removed from CSV.

---

## 2. Unified Scheduler — all 9 reports 🟢

**Problem it solves:** Each report had its own half-finished schedule modal. Two reports (**Pareto, Charts**) were **silently emailing an Average Basket export** instead of themselves. CancelLog produced **empty Excel**. Weekly schedules had **no day-of-week picker**. Default export was CSV.

**What I say:**
> "Scheduling is now a single shared component used by all nine reports. We took the Purchases-vs-Sales modal — which was the working one — and made it the standard. That gives every report the same full recurrence options: daily, **weekly with day-of-week selection**, monthly day-of-month, custom range, an AI toggle, and Excel as the default format. We also fixed a serious bug where scheduled Pareto and Charts were emailing the wrong report entirely."

**What I show:**
1. Open any report → **Schedule** → show recurrence dropdown → pick **Weekly** → the **day-of-week checkboxes appear** (Mon–Sun).
2. Point out **export format = Excel** by default.
3. Open a second, different report → **same modal, same options** (consistency).
4. (Optional) Schedule Logs page → show a past run completed.

**Defend with data:** ~1,000+ lines of duplicated modal/JS removed and replaced by one partial; scheduler now has dedicated handlers per report type (no fall-through to Average Basket); 15/15 schedule tests pass.

---

## 3. Reusable "Items Selection" Filter 🟢

**Problem it solves:** Filtering by item/category/brand/supplier etc. was inconsistent, and on several reports the filter **applied on screen but was dropped in exports** — so the downloaded file showed *different numbers than the screen*.

**What I say:**
> "There's now one reusable Items Selection filter shared across the reports. Critically, the same filter now flows through **every** path — screen, CSV, Excel, PDF, print, email, AI and scheduled runs — so the exported numbers always match what you see. We also made the fashion dimensions **data-driven**: a café or salon tenant no longer sees empty Model/Colour/Size pickers; they only appear when the data actually uses them."

**What I show:**
1. Open **Purchases vs Sales** → Items Selection → pick a Category → Generate → note the total.
2. Export to Excel → **same total** in the file.
3. Show Include/Exclude tags and the selected-items summary.
4. Mention fashion dimensions appear only for fashion tenants (Model/Colour/Size/Group Size/Fabric/Attributes).

**Defend with data:** Verified against live tenant DEMO365MODAPRO1 — include + exclude partitions sum exactly to the unfiltered total (diff = 0); category/supplier filtered totals match the database to the cent.

---

## 4. AI Analysis — unified + token budget per company 🟢

**Problem it solves:** Every report had its own copy of the AI modal (2,500+ duplicated lines). No visibility or control over AI spend — and **each client has a monthly token allowance**.

**What I say:**
> "AI Analysis is now a single shared component across all reports. On top of that we added **token accounting**: every analysis logs input/output tokens and an estimated cost, and each company has a **monthly token budget**. You can see usage live on the Schedule Logs page with a progress bar, and the budget is checked before each run."

**What I show:**
1. Open a report → **Analyze with AI** → run an analysis (same UI everywhere).
2. Open **Schedule Logs** → show the **"AI Tokens This Month"** card with progress bar + Input/Output/Cost columns per run.
3. Settings → Database → show the **Monthly Token Limit** field.

**Defend with data:** New `tbl_AiTokenBudget` + token columns on the run log; budget checked before AI runs (manual and scheduled); −2,575 lines of duplicated AI UI removed.

---

## 5. Send to Email — unified + per-company address book 🟢

**Problem it solves:** Duplicated email modals; you had to **retype the same recipient addresses every time**; Below-Min-Stock had no email at all; some templates/merge fields were ignored.

**What I say:**
> "Send-to-Email is one shared component now, on all nine reports including Below Min Stock which had none. And each company database has a **reusable email address book** — you save an address once, then pick it from the book instead of retyping. We also fixed templates being ignored and subject merge-fields not being replaced."

**What I show:**
1. Open a report → **Send to Email** → format defaults to **Excel**.
2. Open the **Address Book** panel → search, pick an address → it drops into the recipients field; add a new one → it's saved for next time.
3. (Optional) Settings → Email Templates → show templates exist for all 9 report types + "Load Starter Template".

**Defend with data:** New `tbl_EmailRecipientList` (idempotent, per tenant); ~1,600 lines of duplicated email modals removed.

---

## 6. CRM — Prospect Clients multi-select filters 🟢

**Problem it solves:** Client couldn't select **2+ categories** or multiple statuses — the filters were single-select.

**What I say:**
> "On the CRM / Prospect Clients report the Status, Priority and Category 1 & 2 filters are now **multi-select**. You can filter prospects and leads by several statuses at once — for example Lead + Prospect + Waiting — and by multiple categories."

**What I show:**
1. Open **Prospect Clients** → Status dropdown → tick **Lead + Prospect + Waiting** → Generate.
2. Category 1 → tick **two categories** → Generate → results reflect both.
3. Mention these selections are preserved in Save Layout and scheduled runs.

**Defend with data:** Filter model takes lists with an `IN` clause; backward compatible with old single-value filters.

---

## 7. Offers Report — brought up to standard 🟢

**Problem it solves:** Offers was built in a rush and missing options the other reports had.

**What I say:**
> "The Offers report now has the same toolkit as the rest: the shared Items Selection filter, a **third level of grouping**, multi-select Status / Store / Agent, and the unified schedule and email modals. It's no longer the odd one out."

**What I show:**
1. Open **Offers** → show Items Selection + **Third Group** dropdown + multi-select Status/Store/Agent.
2. Group by 2–3 levels → Generate → nested grouped result.
3. Schedule → same shared modal as everywhere.

---

## 8. Purchases vs Sales enhancements + correctness 🟢

**What I say:**
> "Purchases vs Sales gained a **Show Available** stock column and an **Include Additional Charges** (wholesale-cost) toggle, both wired all the way through exports, print and scheduling. We also fixed a grand-total aggregation bug so stock figures sum over distinct items, and fixed the supplier/customer pickers that were querying a non-existent column."

**What I show:** Open PS → toggle **Show Available** and **Include Additional Charges** → columns/numbers update → export → matches.

---

## 9. Quality bar (cross-cutting) 🟢

**What I say:**
> "Throughout, the rule was: **the numbers must match the legacy system and the database to the cent**. Every data change was verified against a live tenant, and the test suite grew to ~170 tests covering filters, permissions, scheduling and exports."

**What I show:** (Talking point — no click.) Mention live verification on DEMO365MODAPRO1 and the test count.

---

# IN PROGRESS — on my machine, not yet deployed 🟡

> **What I say:**
> "These are finished and tested locally and will go out in the next deploy."

## A. Save Layout — shared framework 🟡
Save and reload all your report settings as named layouts; one shared component across every report; the last-used layout is restored on refresh. (Also removed ~1,500 lines from the controller by consolidating duplicates.)

**Show (local):** Set filters → **Save Layout** as "My Weekly View" → refresh → it reloads automatically with the name shown.

## B. Standardised nested grouping + subtotals everywhere 🟡
Grouped reports now show **nested groups with subtotals** consistently across **screen, print preview, Excel, PDF and CSV** — using Purchases-vs-Sales as the reference. Applied to Catalogue, CancelLog, Prospect Clients, Offers (incl. 3rd level) and Average Basket exports. Subtotal math mirrors the screen exactly (percentages are recomputed, not summed).

**Show (local):** Offers grouped by 3 levels → screen shows subtotals at each level → export Excel/PDF/CSV → subtotals identical.

## C. Items Selection UX overhaul (Dani's feedback) 🟡
Auto-focus fix, include/exclude switch **without losing the selection**, "Clear All" + "Apply Filters" toggle, "x/300 selected" counter that updates as you scroll, cleaner colours.

**Show (local):** Open a dimension filter → select items → switch Include↔Exclude (selection kept) → Clear All.

---

# Closing (30 sec)

**What I say:**
> "So the week moved the product from 'nine reports that each did things their own way' to 'one consistent platform': shared filters, shared scheduling, shared email, shared AI — all permission-aware and all verified against the database. The remaining items — save layouts, fully standardised grouped exports, and the filter UX polish — are done locally and ready for the next deploy."

---

# Appendix — commit reference (for defending with data)

**Deployed (03–05 June), grouped by theme:**

| Theme | Commits |
|---|---|
| Permissions / Security | `559258e`, `25c0ecd`, `7107a77`, `da986d4`, `109d3eb`, `27f73cf`, `3f6cd16`, `9c1a0c4`, `bec0550`, `3dfa8b7`, `628f399` |
| Unified Scheduler | `af7980d`, `44172ab`, `bd5c085`, `8eeae42`, `dfe1c60`, `cf03289`, `158998f` |
| Items Selection / Fashion dims | `5fbbd34`, `f5bd96e`, `988c9c0`, `9dfe51c`, `cf69711` |
| AI unified + token budget | `0c7e77a`, `b7237b0`, `69231f8`, `c097680` |
| Send to Email + address book | `cc86308`, `724651b` |
| CRM multi-select | `d093557` |
| Offers parity | `e185d4a` |
| Purchases vs Sales + fixes | `4983aa7`, `9dfe51c`, `cf69711`, `1099570` |
| Pareto "Others" legacy parity | `621ec67` |
| Bug fixes | `fa5f0e8`, `c097680` |

**In progress (local, since 05.06 — not committed):**
- Save Layout framework: new `_SaveLayout.cshtml`, `SaveLayoutPartialModel.cs`; `ReportsController` −1,537 lines.
- Nested grouping standardization: new `CatalogueSubtotal.cs`; updates to `ExcelExportService.cs`, `CsvExportService.cs`, `PdfExportService.cs` + report views & print previews.
- Items Selection UX: `items-selection.js`, `_ItemsSelection.cshtml`, `_DimensionFilter.cshtml`.

> Last deployed commit: `cc86308` (2026-06-05 13:26) — confirmed on both `origin` and `powersoftapps`.
