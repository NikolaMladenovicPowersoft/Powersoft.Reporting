# POWERSOFT.REPORTING – Master Context for Cursor AI

> **Last updated**: 2026-02-03 (post Meeting 1 with George + Meeting 2 with Christina + Teams chat)  
> **Repo**: `c:\p\Powersoft.Reporting` → `https://github.com/NikolaMladenovicPowersoft/Powersoft.Reporting.git` (branch: `master`)  
> **Legacy repo (read-only)**: `c:\p\Powersoft.CloudAccounting` (branch: `dev/RH_20250922_CustomAttributes`)

---

## 0A. OPERATING PROTOCOL (how to produce best results)

**Role**: You are a senior backend/full-stack developer with deep knowledge of the Powersoft365 legacy system. You've studied the legacy codebase (VB.NET WebForms, raw SQL, FormsAuth) and are building the next-generation Reporting module in C#/ASP.NET Core. You think like Christina (DBA/tech lead) when reviewing — you know the exact table schemas, ranking thresholds, and permission chains.

**Before every change:**
1. READ the file you're about to edit — never assume its current state
2. READ the legacy equivalent in `c:\p\Powersoft.CloudAccounting` if one exists — match their patterns
3. STATE what you're doing and why, in 1-2 sentences
4. If the change touches SQL, verify column names against the schema in Section 2

**After every change:**
1. BUILD — run `dotnet build` and fix any errors immediately
2. REVIEW as Christina would: Does the SQL match the schema? Are ranking thresholds correct? Are there edge cases with null/missing data? Would this break existing users?
3. CHECK for security: SQL injection, password exposure in responses, missing access checks, CSRF
4. If you find problems, fix them immediately — don't just note them

**Code style:**
- Write code like a human developer, not a demo. No unnecessary comments explaining obvious things.
- Variable names should match what the team uses (e.g., `Ranking` not `userLevel`, `DBCode` not `databaseIdentifier`)
- SQL should be readable, use aliases like the legacy code does (t1, t2 or meaningful aliases)
- Error messages should be user-facing quality, not developer debug messages

**What to avoid:**
- Don't add TODOs in code — either implement it or don't
- Don't create wrapper classes or abstractions that add no value
- Don't over-comment — the code should speak for itself
- Don't create documentation files unless explicitly asked
- Don't assume — if uncertain, check the legacy code or ask

---

## 0B. RULES FOR AI

- **Working directory**: `c:\p\Powersoft.Reporting`
- **Language**: C# — currently .NET 6.0. **Preferred**: .NET 8 (LTS, collection expressions, etc.). **Pending**: confirm on-prem runtime constraints and deployment baseline before committing to upgrade.
- **Shell**: PowerShell on Windows — use `;` not `&&` to chain commands
- **SQL**: Raw SQL via `Microsoft.Data.SqlClient` — NO Entity Framework, NO LINQ-to-SQL, NO ORM
- **Frontend**: Bootstrap 5 + vanilla JavaScript — NO React, NO Angular, NO jQuery
- **Export libs**: ClosedXML (Excel), QuestPDF (PDF)
- **Architecture**: 4-layer: Web → Core (interfaces/models) → Data (repositories) → Tests
- **Never modify** files in `c:\p\Powersoft.CloudAccounting` — that's the legacy system (read-only reference)
- **Parameterized queries ALWAYS** — never string-concatenate SQL parameters
- **Commit only when asked** — push only to `origin master` on `Powersoft.Reporting`
- **Actions must be coordinated with Christina** — do NOT invent new action IDs; reserved range is **6025–7000**
- **Module code is `RENGINEAI`** — exactly 9 chars, confirmed by Christina's SQL script
- **Action category ID is `1085`** — "REPORTS ENGINE AI"

---

## 1. PROJECT STRUCTURE

```
Powersoft.Reporting/
├── Powersoft.Reporting.Core/          # Interfaces, Models, Enums, Constants
│   ├── Constants/SessionKeys.cs
│   ├── Enums/BreakdownType.cs
│   ├── Interfaces/
│   │   ├── IAuthenticationService.cs
│   │   ├── IAverageBasketRepository.cs
│   │   ├── ICentralRepository.cs
│   │   ├── IItemRepository.cs
│   │   ├── IScheduleRepository.cs
│   │   ├── IStoreRepository.cs
│   │   └── ITenantRepositoryFactory.cs
│   └── Models/
│       ├── AppUser.cs, AverageBasketRow.cs, Company.cs, Database.cs
│       ├── Item.cs, PagedResult.cs, ReportFilter.cs
│       ├── ReportGrandTotals.cs, ReportSchedule.cs, Store.cs
├── Powersoft.Reporting.Data/          # Repository implementations
│   ├── Auth/AuthenticationService.cs
│   ├── Central/CentralRepository.cs
│   ├── Factories/TenantRepositoryFactory.cs
│   ├── Helpers/ConnectionStringBuilder.cs, Cryptography.cs
│   └── Tenant/
│       ├── AverageBasketRepository.cs
│       ├── ItemRepository.cs
│       ├── ScheduleRepository.cs
│       ├── StoreRepository.cs
│       └── IniRepository.cs
├── Powersoft.Reporting.Web/           # ASP.NET Core MVC
│   ├── Program.cs
│   ├── Controllers/AccountController.cs, HomeController.cs, ReportsController.cs
│   ├── ViewModels/AverageBasketViewModel.cs, DatabaseSelectionViewModel.cs, LoginViewModel.cs
│   ├── Views/Account/, Reports/, Shared/
│   ├── Services/ExcelExportService.cs, PdfExportService.cs
│   └── wwwroot/css/site.css
├── Powersoft.Reporting.Tests/         # Unit tests (xUnit)
├── _SQL/                              # SQL scripts
├── _DOCS/                             # Documentation
└── Powersoft.Reporting.sln
```

---

## 2. DATABASE ARCHITECTURE

### Central DB (`pscentral`)

| Table | Purpose |
|-------|---------|
| `tbl_Company` | Companies. Key: `pk_CompanyCode`. Filter: `CompanyActive = 1`. |
| `tbl_DB` | Tenant databases. Key: `pk_DBCode`. FK: `fk_CompanyCode`. Filter: `DBActive = 1`. |
| `tbl_User` | Users. Key: `pk_UserCode`. FK: `fk_RoleID`, `fk_CompanyCode`. Filter: `UserActive = 1`. |
| `tbl_Role` | Roles with `Ranking` (lower = more privileged). Key: `pk_RoleID`. |
| `tbl_RelUserDB` | User ↔ Database access mapping. FK: `fk_UserCode`, `fk_DBCode`. |
| `tbl_RelRoleAction` | Role ↔ Action permission mapping. FK: `fk_RoleID`, `fk_ActionID`. |
| `tbl_RelUserAction` | User-specific action overrides. FK: `fk_UserCode`, `fk_ActionID`. |
| `tbl_Action` | Defined actions (permissions). Key: `pk_ActionID` (integer). FK: `fk_ActionCategoryID`. |
| `tbl_ActionCategory` | Action categories. Key: `pk_ActionCategoryID`. |
| `tbl_RelModuleDb` | Database ↔ Module licensing. FK: `fk_ModuleCode`, `fk_DbCode`. |
| `tbl_RelModuleAction` | Module ↔ Action mapping. FK: `fk_ModuleCode`, `fk_ActionID`. |
| `tbl_Module` | Licensed modules. Key: `pk_ModuleCode` (varchar 10). |
| `tbl_Domain` | Server domains. Key: `pk_DomainCode`. |

### Tenant DB (e.g., `pswaDEMO365CAFE`)

| Table | Purpose |
|-------|---------|
| `tbl_InvoiceHeader` | Sales invoices. `DateTrans`, `pk_InvoiceID`, `fk_StoreCode`. |
| `tbl_InvoiceDetails` | Invoice lines. `fk_Invoice`, `fk_ItemID`, `Quantity`, `Amount`, `Discount`, `ExtraDiscount`, `VatAmount`. |
| `tbl_CreditHeader` | Sales returns. Same structure as InvoiceHeader. |
| `tbl_CreditDetails` | Credit lines. Same structure as InvoiceDetails. |
| `tbl_Item` | Items/products. `pk_ItemID` (BigInt!), `ItemCode`, `ItemNamePrimary`, `ItemActive`. |
| `tbl_Store` | Stores. `pk_StoreCode`, `StoreName`, `StoreArea`. |
| `tbl_ReportSchedule` | Report schedules (created by us). |
| `tbl_ReportScheduleLog` | Schedule execution history. |
| `tbl_IniModule` | User preference modules (auto-created). See section 5.6. |
| `tbl_IniHeader` | **User saved layouts/preferences** — header. See section 5.6. |
| `tbl_IniDetail` | **User saved layouts/preferences** — key-value details. See section 5.6. |

---

## 3. WHAT IS COMPLETE

### Authentication & Company/DB Selection (FUNCTIONAL — security hardened)
- Login with existing Powersoft users (same DB, same credentials)
- Company → Database selection (from PSCentral)
- Dynamic connection string building (decrypt password)
- Session management (tenant connection string + UserCode + RoleID + Ranking in session)
- **Role-based DB filtering**: system admin (Ranking < 15) sees all; client users filtered by `tbl_RelUserDB` + `tbl_RelModuleDb` (RENGINEAI)
- **Auto-login**: 1 company + 1 database → skip selection, connect directly
- **Access verification**: `Connect` action validates user-DB access before allowing connection
- **Action-based permission checks**: action 6025 (View Average Basket), action 6026 (Schedule)

### Average Basket Report
- Date filters: From/To + Quick presets (Today, Yesterday, Last 7/30 days, This/Last Month, YTD, Last Year)
- Breakdown: Daily, Weekly, Monthly
- Grouping: Primary + Secondary (None, Store, Category, Department, Brand, etc.)
- Store filter: Multi-select modal with search, Select All/Clear
- Item filter: Modal with API search, auto-load, live debounced search, Select All/Clear
- Options: Include VAT, Compare with Last Year (checkbox)
- Column filters: Per-column (text: contains/equals/starts, numeric: >=, =, >, <, <=, !=) — live debounced
- Sorting: Click header, ASC/DESC toggle
- Pagination: Server-side, 25/50/100/200 rows per page
- Grand Totals: Separate SQL query (independent of pagination)
- Summary cards: Net Transactions, Items Sold, Total Sales, Avg Basket, YoY Change
- Group subtotals in grid
- Exports: CSV (client-side), Excel (ClosedXML server-side), PDF (QuestPDF server-side)
- Print Preview: Inline iframe with print-optimized view
- Schedule UI: Modal for creating schedule (demo level, backend runner NOT implemented)
- No Data handling: Clear Filters + Reset buttons
- **Save/Restore Layout**: Save button persists IncludeVat, CompareLastYear, Breakdown, GroupBy, SecondaryGroupBy, PageSize, HiddenColumns to `tbl_IniHeader`/`tbl_IniDetail`. Auto-loads on page open. Reset to defaults. Column visibility toggle modal with immediate apply.
- **Connection string security**: SA password removed from tracked `appsettings.json` (placeholder only). Real credentials in gitignored `appsettings.Development.json`.

---

## 4. MEETING 1 DECISIONS (Mr. George Malekkos)

### Storage
| Decision | Detail |
|----------|--------|
| **Provider** | Start with **Digital Ocean** (Azure too expensive). May switch to AWS later. |
| **Architecture** | Configurable provider via Strategy pattern (`IReportStorageProvider`) |
| **Expiration** | Default **1 week**, configurable per customer in settings |
| **Cleanup** | Automatic background job to delete expired reports |
| **Scope** | Common storage for all customers, **segregated by DB code** |
| **Security (MUST)** | Links MUST be **signed + expiring** (pre-signed URL or token). Paths MUST be non-guessable (GUID-based, not sequential). Expiry timestamp MUST be included in notification email. |

### Report Delivery
| Decision | Detail |
|----------|--------|
| **Method** | **Link only** — no file attachments in email |

### Scheduling
| Decision | Detail |
|----------|--------|
| **Date ranges** | **RELATIVE**, not fixed (e.g., "last 7 days from run date", "this month to today", "YTD") |
| **Recurrence** | Outlook / MS SQL Agent style (see screenshots). Once, Daily, Weekly (multi-select days!), Monthly (day N or "first Monday" etc.) |
| **License limits** | **Working assumption**: system default = 5, per-DB override possible. Commercial tiers TBD (e.g., tier1=5, tier2=10, tier3=20). George mentioned "maybe 10", Christina said "by default 5". Final decision pending. |
| **Save preferences** | Checkboxes like "Compare with Last Year" should have a save/persist option |

### AI
| Decision | Detail |
|----------|--------|
| **Pricing** | **Paid feature** — enable/disable per customer |
| **Functionality** | AI analyzes report data (export CSV → send to AI API → get insights) |
| **Scheduling + AI** | When scheduling with AI, the AI describes what it sees in the report |
| **Next step** | Research which AI tool/API to use |

### Deployment
| Decision | Detail |
|----------|--------|
| **Domain** | Danny to create domain; publish so George can test with real data |
| **Test DB** | SHOEBUKS (110GB, too large for local) → use ESHOP instead on local; SHOEBUKS online after publish |
| **Server** | No special config needed — standard ASP.NET Core deployment |

---

## 5. MEETING 2 DECISIONS (Christina — Login, Roles, Actions, Layout)

### 5.1 Module Registration (CONFIRMED — already run by Christina)

```sql
-- Already executed on PSCentral by Christina:
INSERT INTO tbl_Module (pk_ModuleCode, ModuleDesc, ModuleComments, ModuleActive, ModuleOrder)
VALUES (N'RENGINEAI', N'Reports Engine AI', 
        N'Reports Engine AI with scheduler designed by Nikola Mladenovic', 1, 69)

INSERT INTO tbl_ActionCategory (pk_ActionCategoryID, ActionCategoryName, PowersoftSupport)
VALUES (1085, N'REPORTS ENGINE AI', 1)
```

### 5.2 Action IDs (CONFIRMED — already run by Christina)

```sql
-- Action 6025: View Average Basket
INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
VALUES (6025, 1085, 'VIEWAVGBASKET', 'View Average Basket Report', 0, 1)

INSERT INTO tbl_RelModuleAction (fk_ActionID, fk_ModuleCode)
VALUES (6025, 'RENGINEAI')

-- Action 6026: Schedule Average Basket
INSERT INTO tbl_Action (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
VALUES (6026, 1085, 'ALLOWSCHEDULEAVGBASKET', 'Allow Schedule Average Basket Report', 0, 1)

INSERT INTO tbl_RelModuleAction (fk_ActionID, fk_ModuleCode)
VALUES (6026, 'RENGINEAI')
```

- **Reserved range for us: 6025–7000.** More actions to be created together with Christina.
- Need to discuss: AI actions, settings actions, other report view actions.

### 5.3 Login Logic (MUST IMPLEMENT)

```
1. Authenticate user (existing AuthenticateUser365 against pscentral)
2. Check: UserActive = 1
3. Get user's fk_RoleID → get Ranking from tbl_Role

IF Ranking < 15 (system admin / webmaster / support):
   → Show ALL companies, ALL databases (no restrictions)
   → These are internal Powersoft users

IF Ranking >= 15 (client administrator = 15, client standard = 20, customized > 20):
   → Query: get databases WHERE:
     a) User is linked to DB (tbl_RelUserDB)
     b) DB is active (tbl_DB.DBActive = 1)
     c) Company is active (tbl_Company.CompanyActive = 1)
     d) DB is linked to module RENGINEAI (tbl_RelModuleDb)
   → Group by company → show company selector → show DB selector
   → If only 1 company + 1 database → AUTO-LOGIN (skip selection screen!)
```

**Critical SQL logic Christina demonstrated:**
```sql
SELECT t1.pk_UserCode, t1.fk_RoleID, t2.Ranking,
       t3.fk_DBCode, t4.pk_DBCode, t4.DBFriendlyName, t4.DBActive,
       t5.pk_CompanyCode, t5.CompanyName, t5.CompanyActive
FROM tbl_User t1
INNER JOIN tbl_Role t2 ON t1.fk_RoleID = t2.pk_RoleID
INNER JOIN tbl_RelUserDB t3 ON t1.pk_UserCode = t3.fk_UserCode
INNER JOIN tbl_DB t4 ON t3.fk_DBCode = t4.pk_DBCode
INNER JOIN tbl_Company t5 ON t4.fk_CompanyCode = t5.pk_CompanyCode
INNER JOIN tbl_RelModuleDb t6 ON t4.pk_DBCode = t6.fk_DbCode
WHERE t1.pk_UserCode = @UserCode
  AND t1.UserActive = 1
  AND t2.Ranking >= 15
  AND t4.DBActive = 1
  AND t5.CompanyActive = 1
  AND t6.fk_ModuleCode = 'RENGINEAI'
```

### 5.4 Role-Based Action Checks (MUST IMPLEMENT)

```
Ranking <= 20 (client admin = 15, client standard = 20):
   → ALL actions allowed by default (no action check needed)
   → EXCEPT: database settings → only Ranking <= 15 (client administrator)
   → EXCEPT: system settings → only Ranking <= 10 (webmaster/support)

Ranking > 20 (customized roles like "pharmacist", "cashier"):
   → CHECK tbl_RelRoleAction for the specific actionID
   → AND the action must be connected to module RENGINEAI via tbl_RelModuleAction
```

**Action check SQL:**
```sql
-- For roles with Ranking > 20: check if role has specific action
-- Legacy uses simple count check (from QR.vb IsAuthorized):
SELECT count(*)
FROM dbo.tbl_RelRoleAction
WHERE fk_RoleID = @fk_RoleID AND fk_ActionID = @fk_ActionID
-- Returns > 0 means authorized

-- Additionally verify action belongs to our module:
SELECT count(*)
FROM tbl_RelModuleAction
WHERE fk_ActionID = @ActionID AND fk_ModuleCode = 'RENGINEAI'
```

**Summary of permission levels:**
| Role Ranking | Access Level |
|-------------|-------------|
| < 15 (1, 10) | System admin — access everything, all companies, all databases |
| 15 | Client administrator — all actions + database settings |
| 20 | Client standard — all actions except settings |
| > 20 | Customized — check tbl_RelRoleAction per action |

### 5.5 Deactivation Rules

- **Company deactivated** → all databases under it become inaccessible (even if DB remains active)
- **Database deactivated** → that DB becomes inaccessible
- **Module unlinked from DB** → DB disappears from login list for this app
- All checks happen at login time (not mid-session)

### 5.6 Save/Restore Layout (User Preferences)

**IMPORTANT: Tables are `tbl_IniHeader` + `tbl_IniDetail`** (not LayoutHeader). Also requires `tbl_IniModule`.

#### Schema (in TENANT databases)

```sql
-- tbl_IniModule (auto-created if missing)
-- pk_IniModuleCode: NVARCHAR(50) — e.g., 'RENGINEAI'
-- IniModuleDesc: NVARCHAR(200)

-- tbl_IniHeader
-- pk_IniHeaderID: BIGINT (identity)
-- fk_IniModuleCode: NVARCHAR(50) — e.g., 'RENGINEAI'
-- IniHeaderCode: NVARCHAR(20) — e.g., 'AVGBASKET'
-- IniHeaderDescr: NVARCHAR(200) — e.g., 'Average Basket Report'
-- fk_UserCode: NVARCHAR(36) — user GUID (NULL = ALL users)
-- fk_StoreCode: NVARCHAR(3) — NULL for reports (not store-specific)
-- CreatedBy, LastModifiedBy, LastModifiedDateTime

-- tbl_IniDetail
-- pk_IniDetailID: BIGINT (identity)
-- fk_IniHeaderID: BIGINT — links to header
-- ParmCode: NVARCHAR(50) — parameter name, e.g., 'ShowColumnNetSales'
-- ParmValue: NVARCHAR(2000) — parameter value, e.g., '1' or '0'
```

#### Legacy Save Pattern (from ItemsList.aspx.vb)

```csharp
// 1. Build DataTable of parameters:
//    ParmCode = "ShowColumnOtherName", ParmValue = "1"
//    ParmCode = "ShowColumnSupplier", ParmValue = "0"
//    ParmCode = "ColumnsOrder", ParmValue = "FieldName1-0;FieldName2-1;..."
//    ParmCode = "ColumnsSorting", ParmValue = "FieldName-0-Ascending;..."

// 2. Call bulk save:
iniDetail.Add_Bulk_IniDetail(moduleCode, headerCode, description, userCode, "ALL", dt)
// This: finds/creates header → deletes all old details → inserts new details
```

#### Legacy Load Pattern

```csharp
// 1. Load all parameters:
DataTable dtParms = wqr.GetInitParameters(moduleCode, userCode, "ALL")

// 2. Set primary key and find individual params:
dtParms.PrimaryKey = new[] { dtParms.Columns["ParmCode"] };
var row = dtParms.Rows.Find("ShowColumnOtherName");
if (row != null) showOtherName = row["ParmValue"].ToString() == "1";
```

#### Our Implementation

- **Module code**: `RENGINEAI`
- **Header code**: `AVGBASKET` (max 20 chars)
- **ParmCode examples**: `ShowColumnCYSalesInvNo`, `ShowColumnCurrentNetSales`, `ColumnsOrder`, `ColumnsSorting`, `IncludeVat`, `CompareLastYear`, `DefaultBreakdown`
- **Default layout**: hardcoded in C# code; used when no `tbl_IniHeader` entry exists for user
- **Buttons**: Save (persist current state) + Restore (load saved state)

### 5.7 Schedule License Limits (Settings Architecture)

**Two levels of settings:**

1. **System Settings** (pscentral, global defaults):
   - `NumberOfScheduleAllowed` = 5 (default for all new databases)
   - Access: only Ranking <= 10

2. **Per-Database Settings** (overrides system default):
   - Each database can have its own `NumberOfScheduleAllowed`
   - Automatically inherits system default when DB is created
   - Can be increased/decreased per database
   - Access: only Ranking <= 15 (client administrator)

**Two dashboards needed:**
- System Settings dashboard (Ranking <= 10 only)
- Database Settings dashboard (Ranking <= 15 only)

### 5.8 Test Users Created by Christina (on dev server)

| User | Password | Role (Ranking) | Companies/DBs |
|------|----------|----------------|---------------|
| Demo User 1 | 123 | Client Administrator (15) | DEMO365CAFE, DEMO365MODAPRO, ESHOP |
| Demo User 2 | 123 | (assigned role) | DEMO FROUTARIA, DEMO GARAGE |
| Demo User 3 | 123 | (assigned role) | KIOSK only |
| Demo User 4 | 123 | (assigned role) | Multi-company test |
| Nicola | 123 | System Administrator (1) | ALL companies, ALL databases |

**DBs linked to module RENGINEAI:** DEMO365CAFE, DEMO365MODAPRO, ESHOP, DEMO FROUTARIA, DEMO GARAGE, DEMO HAIRSALON  
**NOT linked (for testing):** DEMO KIOSK (should NOT appear in login list)

---

## 6. TEAMS CONVERSATION DECISIONS

| Decision | Detail |
|----------|--------|
| **.NET 8 upgrade** | Preferred (colleague recommended). Pending: confirm on-prem runtime/deployment constraints before committing. |
| **Recurrence model** | Follow Outlook calendar + MS SQL Agent patterns (see screenshots below) |
| **v1 scope** | Core 80%: skip "every X hours during the day" and advanced exceptions for now |
| **Recurrence storage** | Proprietary JSON structure; can evolve to RRULE later |
| **API clarification** | We DO expose API endpoints (item search, schedules) — they're MVC endpoints, not a separate API project |
| **On-prem deployment** | Confirmed — no cloud dependency |

### Recurrence Reference (from screenshots)

**MS SQL Agent style (Screenshot 1):**
- Schedule type: Recurring / One-time
- Frequency: Daily / Weekly / Monthly
- Monthly options: "Day 7 of every 1 month(s)" OR "The first Monday of every 1 month(s)"
- Daily frequency: "Occurs once at 12:00 PM" OR "Occurs every 1 hour(s) starting at / ending at"
- Duration: Start date + End date OR No end date

**Outlook style (Screenshot 2):**
- Repeat every N weeks
- Day-of-week multi-select toggle buttons: M T W T F S S
- Until date (end date for recurrence)

---

## 7. NEXT IMPLEMENTATION PRIORITIES (Updated Post-Meetings)

### Priority 1: Login Logic Refactor (CRITICAL — blocks deployment)
1. Implement role-based login per Christina's spec (Section 5.3)
2. Auto-login when 1 company + 1 database
3. Module check: only show DBs linked to `RENGINEAI`
4. Company active + DB active checks

### Priority 2: Action-Based Permissions (CRITICAL — blocks deployment)
1. Implement action checks per Section 5.4
2. Check actionID 6025 (View Average Basket) before showing report
3. Check actionID 6026 (Schedule) before allowing schedule creation
4. Ranking <= 20: all actions; Ranking > 20: check tbl_RelRoleAction

### Priority 3: Save/Restore Layout — IMPLEMENTED
1. ~~Read/write `tbl_IniHeader` + `tbl_IniDetail` (tenant DB, existing tables)~~ DONE: `IIniRepository` + `IniRepository`
2. ~~Save button + Restore button on Average Basket~~ DONE: Save Layout, Reset Layout, Column Visibility modal
3. ~~Default hardcoded layout as fallback~~ DONE: server loads saved params on GET, JS applies column visibility
4. Saves: IncludeVat, CompareLastYear, Breakdown, GroupBy, SecondaryGroupBy, PageSize, HiddenColumns
5. Transaction-safe: DELETE + INSERT in same transaction (Christina review fix)
6. Auto-creates `RENGINEAI` in `tbl_IniModule` if not exists (legacy pattern)
7. UX: Clear Filters (preserves saved layout, shows message) vs Reset Layout (discards layout). Clear column filters button in results toolbar.

### Priority 4: Schedule Enhancement
1. Relative date ranges (not fixed dates)
2. Outlook/SQL Agent style recurrence UI
3. License limit enforcement (check NumberOfScheduleAllowed per DB)
4. JSON-based recurrence model

### Priority 5: Settings Dashboards
1. System Settings page (Ranking <= 10): default NumberOfScheduleAllowed
2. Database Settings page (Ranking <= 15): per-DB overrides

### Priority 6: Storage Foundation
1. `IReportStorageProvider` interface
2. `DigitalOceanStorageProvider` (S3-compatible)
3. `LocalFileStorageProvider` (for dev)

### Priority 7: Email + Background Runner
1. SMTP service (credentials from Danny)
2. Background job: read schedule → run report → store → email link

### Priority 8: AI Integration (paid feature)
1. Research AI API
2. Feature flag per customer

---

## 8. LEGACY SYSTEM REFERENCE

### Key files in `c:\p\Powersoft.CloudAccounting` (read-only)

| File | What it tells us |
|------|------------------|
| `Powersoft.Login\LogInfo.vb` | Session data model |
| `Powersoft.CloudAccounting\Login.aspx.vb` | Auth flow — AuthenticateUser365, OTP, company/DB routing |
| `Powersoft.CloudAccounting\PSBase.aspx.vb` | Authorization — IsAuthorized(actionID, pageMaxRank) |
| `Powersoft.Central\QR.vb` | Central DB queries — GetRelUserDB, IsModuleAuthorized, BuildConnString |
| `_SQL\REPORTING_APP_REFERENCE.md` | Data model reference for reporting queries |
| `_SQL\STRATEGIC_EVOLUTION_GUIDANCE.md` | Architectural analysis and migration strategy |

### Permission model (legacy — we replicate this)
```
IsAuthorized(actionID, pageMaxRank):
  1. User active? Role active?
  2. Company active? DB active?
  3. Module licensed? (tbl_RelModuleDb + tbl_RelModuleAction where fk_ModuleCode = 'RENGINEAI')
  4. User→DB access? (tbl_RelUserDB)
  5. Role→Action? (tbl_RelRoleAction) — only for Ranking > 20
  6. System admin (Ranking < 15) overrides — access everything
```

---

## 9. KNOWN ISSUES & TECHNICAL DEBT

1. **pk_ItemID is BigInt** in DB — cast with `(int)reader.GetInt64(0)` to avoid overflow
2. **Schedule model is basic** — needs relative dates, multi-day recurrence, Outlook-style options
3. ~~**Login does NOT check module or role**~~ — FIXED: login filters DBs by module + user-DB link + ranking + RoleActive
4. ~~**No action permission checks**~~ — FIXED: action 6025/6026 checks in ReportsController, claims fallback for session expiry
5. **No layout save/restore** — George and Christina both want this
6. **Excel export needs verification** — George asked to send file to Christina for QA
7. **.NET 6 → .NET 8 upgrade** — preferred but pending on-prem runtime confirmation
8. **PS Central connection string** — server name may differ per environment (ANDREASPS vs local)
9. ~~**Session repopulation**~~ — FIXED: GetRanking/GetRoleID fall back to claims when session expires
10. **No CSRF on AJAX endpoints** — Connect, SaveSchedule, etc. lack anti-forgery tokens (cross-cutting fix needed)
11. **Password hashing**: legacy uses `FormsAuthentication.HashPasswordForStoringInConfigFile("SHA1")`. Our code tries both UTF-8 and Unicode SHA1. Verified working with existing users.

### Schema verification (done 2026-02-03 against live pscentral)

| Column | Actual Type | Nullable | Notes |
|--------|------------|----------|-------|
| `tbl_User.fk_RoleID` | int | **NOT NULL** | INNER JOIN safe |
| `tbl_User.UserActive` | bit | **NOT NULL** | ISNULL unnecessary but harmless |
| `tbl_User.fk_CompanyCode` | nvarchar(20) | YES | Null check needed |
| `tbl_Role.Ranking` | int | **NOT NULL** | No null check needed |
| `tbl_Role.RoleActive` | bit | **NOT NULL** | **Must check — legacy does** |
| `tbl_RelModuleDB.fk_DbCode` | nvarchar(20) | NOT NULL | Column case: `fk_DbCode` |
| `tbl_RelModuleDB.fk_ModuleCode` | nvarchar(10) | NOT NULL | 'RENGINEAI' = 9 chars, fits |
| `tbl_DB.DBProviderInstanceName` | nvarchar(100) | NOT NULL | DB says not null but MapDatabase null-checks (safe) |

### Live data verified

| Item | Status |
|------|--------|
| Module `RENGINEAI` | Exists, active |
| Action 6025 (VIEWAVGBASKET) | Exists |
| Action 6026 (ALLOWSCHEDULEAVGBASKET) | Exists |
| DBs linked to RENGINEAI | DEMO365CAFE, DEMO365FROUTARIA, DEMO365HAIRSALON1, DEMO365MODAPRO1, DEMOGARAGE, ESHOPMODAPRODEMO |
| DEMOS.user1 (Ranking 15) | Sees 3 DBs: DEMO365CAFE, DEMO365MODAPRO1, ESHOPMODAPRODEMO |
| DEMOS.user2 (Ranking 20) | Sees 3 DBs: DEMO365FROUTARIA, DEMO365HAIRSALON1, DEMOGARAGE |
| DEMOS.user4 (Ranking 15) | Sees 1 DB: ESHOPMODAPRODEMO → **auto-login triggers** |
| Roles | Ranks: 1 (Webmaster), 5 (SysAdmin), 10 (Support), 15 (ClientAdmin), 20 (ClientStd), 21+ (Custom) |

---

## 10. PEOPLE

| Person | Role | Notes |
|--------|------|-------|
| **Mr. George Malekkos** | Director/Owner | Final decision maker. Approves architecture, features, licensing, pricing. |
| **Christina** | DBA / Technical lead | Creates SQL scripts, manages actions/modules/roles. ALL new actions go through her. |
| **Danny** | DevOps / Infra | Provides email credentials, domains, server access, publishing. |
| **Marinos** | Developer | Works on Power BI integration — avoid duplicating his work. |
| **Nikola (you)** | Developer | Building the Reporting module. |

---

## 11. COMMIT CONVENTIONS

- Short, human-like commit messages in English
- No doc files (`_DOCS/`, `_SQL/*.md`) in pushes unless explicitly asked
- Push only to `origin master` on `Powersoft.Reporting`
- Never push `Powersoft.CloudAccounting` (remote is restricted)

---

## 12. LEGACY SCREENSHOTS ANALYSIS (from Christina's demo)

### Screenshot: Items List (ItemsList.aspx)
- URL: `http://localhost:5454/restricted/StockControl/ItemsList.aspx`
- DB: ESHOPMODAPRODEMO (Eshop DEMO MODA)
- 50,061 items, paginated 25/page
- Columns: Item Code, Item Description, Barcode, Stock, On Reservation, On Order, RETAIL Incl, WHOLESALE Incl
- Toolbar: New, Modify, View, Copy, Stock Position, Transaction&Stock History, Print Barcodes, Advanced Search
- Column filters per column (text inputs in header row)

### Screenshot: Items Settings Dialog (Save Selection)
- Modal dialog with collapsible sections: Main Details, Prices, Others, Extra Fields
- "Others" section: checkboxes for optional columns (Weight Out, Weighted By Supplier, Piece, Storable, Total Value on Sale, Special Offer, Serial Number, Subitem, Loyalty, BRIDGECODE, etc.)
- Save + Cancel buttons
- **This is what we replicate**: Save/Restore user column preferences using `tbl_IniHeader` + `tbl_IniDetail`

### Screenshot: Modify Company Dialog
- URL: `sysCompanyList.aspx`
- Fields: Registration Number, Company Name, Active checkbox, Address, Telephone, Email, Website
- "Inactive/Active Comments" textarea — used when deactivating (reason required)
- **Key insight**: Company deactivation is soft-delete with reason tracking

### Screenshot: Roles and Actions
- URL: `sysRoleList.aspx`
- Roles hierarchy confirmed from screenshot:
  - Rank 5: SYSTEM ADMINISTRATOR
  - Rank 10: SYSTEM SUPPORT
  - Rank 15: CLIENT ADMINISTRATOR (highlighted)
  - Rank 20: CLIENT STANDARD (highlighted)
  - Rank 21: MODAPRO, PHARMACIST, POWERSOFT CHECKRIGHTS, POWERSOFT VIEW ONLY, SUPPORT DATABASES ADMIN
- Custom roles (21+) are per-company: CompanyCode + CompanyName shown

### Screenshot: Databases List
- URL: `sysDbList.aspx`
- Shows all databases with Code, Database name, DB Short Name, Date Created, Created By, Active, Company
- Active DBs: DEMO365CAFE, DEMO365FROUTARIA, DEMO365HAIRSALON1, DEMO365MODAPRO1, DEMOGARAGE, ESHOPMODAPRODEMO
- Inactive DBs (red rows): DEMO365KIOSK1, DEMOIMPORT365, DEMOIMPORTDATA, DEMOMATCH5
- **Key button: "Link Modules"** — this is where DBs are linked to RENGINEAI
- Other buttons: Link Users, Close/Open POS Prices, Create Bulk Databases

### Screenshot: Users List
- URL: `sysUserList.aspx`
- Shows: User Name, Full Name, Email, Active, Company, Active Company, Role, Rank, Powersoft Support
- Two users visible: TROFINC (Webmaster, Rank 1), CB4900.trofinc_admin (Client Administrator, Rank 15)
- **Key button: "Link to database"** — maps user to specific DBs (tbl_RelUserDB)
- **Key button: "Powersoft User Action"** — per-user action overrides (tbl_RelUserAction)

---

## 13. CHRISTINA'S SQL SCRIPTS (exact scripts she sent via Teams)

```sql
-- ============================================
-- SCRIPT 1: Module Registration (run on PSCentral)
-- ============================================
INSERT INTO tbl_Module (pk_ModuleCode, ModuleDesc, ModuleComments, ModuleActive, ModuleOrder)
VALUES (N'RENGINEAI', N'Reports Engine AI', 
        N'Reports Engine AI with scheduler designed by Nikola Mladenovic', 1, 69)

-- ============================================
-- SCRIPT 2: Action Category (run on PSCentral)
-- ============================================
INSERT INTO tbl_ActionCategory (pk_ActionCategoryID, ActionCategoryName, PowersoftSupport)
VALUES (1085, N'REPORTS ENGINE AI', 1)

-- ============================================
-- SCRIPT 3: Actions (run on PSCentral)
-- Range assigned: 6025 - 7000
-- ============================================
INSERT INTO [dbo].[tbl_Action] (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
VALUES (6025, 1085, 'VIEWAVGBASKET', 'View Average Basket Report', 0, 1)

INSERT INTO tbl_RelModuleAction (fk_ActionID, fk_ModuleCode)
VALUES (6025, 'RENGINEAI')

INSERT INTO [dbo].[tbl_Action] (pk_ActionID, fk_ActionCategoryID, ActionName, ActionDesc, PowersoftSupport, AllowCEO)
VALUES (6026, 1085, 'ALLOWSCHEDULEAVGBASKET', 'Allow Schedule Average Basket Report', 0, 1)

INSERT INTO tbl_RelModuleAction (fk_ActionID, fk_ModuleCode)
VALUES (6026, 'RENGINEAI')
```

---

## 14. RECURRENCE MODEL DESIGN (for implementation)

Based on Outlook + MS SQL Agent patterns, the JSON recurrence structure should support:

```json
{
  "type": "Weekly",
  "pattern": {
    "interval": 1,
    "daysOfWeek": ["Monday", "Wednesday", "Friday"],
    "dayOfMonth": null,
    "weekOfMonth": null
  },
  "time": "08:00",
  "range": {
    "startDate": "2026-02-03",
    "endDate": null,
    "noEndDate": true,
    "maxOccurrences": null
  },
  "reportDateRange": {
    "type": "LastNDays",
    "value": 7
  }
}
```

**Recurrence types:**
- `Once` — run once at specified date/time
- `Daily` — every N days
- `Weekly` — every N weeks on selected days (multi-select: M/T/W/T/F/S/S)
- `Monthly` — "Day X of every N months" OR "The Nth [Weekday] of every N months"

**Relative date range types (for ParametersJson):**
- `LastNDays` (e.g., last 7 days from run date)
- `ThisMonth` (1st of current month → today)
- `LastMonth` (1st → last day of previous month)
- `YearToDate` (Jan 1 → today)
- `LastYear` (Jan 1 → Dec 31 of previous year)
- `Custom` (fixed dates — legacy support)
