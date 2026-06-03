# New Report Implementation — Mega Prompt

> **Uputstvo:** Kopiraj ceo prompt u novi Cursor chat.
> Zameni `{{REPORT_NAME}}` sa nazivom reporta (npr. `OffersReport`).
> Popuni `{{PLACEHOLDERS}}` sekciju na dnu pre paste-ovanja.
> Prompt radi sa thinking-partner.mdc i reporting-engine.mdc pravilima.

---

## ZADATAK

Implementiraj novi report **{{REPORT_NAME}}** u Powersoft.Reporting aplikaciji.
Report mora pratiti identičnu arhitekturu, UI/UX pattern i kod konvencije kao postojeći reporti.

---

## KONTEKST

### Solution
- **Nova app (implementacija):** `C:\p\Powersoft.Reporting\Powersoft.Reporting.sln`
- **Legacy app (referenca):** `C:\p\Powersoft.CloudAccounting`

### Projekti u Solution-u

| Projekat | Svrha |
|---|---|
| `Powersoft.Reporting.Core` | Enums, Models, Interfaces, Constants — nema zavisnosti na SQL/web |
| `Powersoft.Reporting.Data` | Repository implementacije (ADO.NET / SqlClient) |
| `Powersoft.Reporting.Web` | ASP.NET Core 6.0 MVC — Controllers, Views, Services |
| `Powersoft.Reporting.Tests` | xUnit testovi |

### Tech Stack
- C# / .NET 6 / ASP.NET Core MVC / Razor Views
- Bootstrap 5 + Bootstrap Icons (`bi-*`)
- ADO.NET (`Microsoft.Data.SqlClient`) — **NEMA Entity Framework**
- Dynamic SQL sa `StringBuilder` + `SqlParameter` (parametrizovano)
- Repository pattern: interfejsi u Core, implementacije u Data
- Session-based tenant connection string

### Baza za verifikaciju
```
Server:   ANDREASPS\SQLDEVELOPER17PS
User:     sa
Password: SQLADMIN123!
DB:       pswaDEMO365MODAPRO1
```
Koristi `sqlcmd` za proveru. NIKAD ne piši SQL bez verifikacije kolona iz baze.

---

## FAZA 0 — DEEP DIVE (obavezno pre pisanja koda)

### 0.1 Legacy source analiza
1. Pronađi original u `C:\p\Powersoft.CloudAccounting`:
   - WQR.vb metoda: `grep "Function Get{{REPORT_NAME}}" WQR.vb`
   - SQL template: `Powersoft.CloudQueries\Resources\RAS_Get{{REPORT_NAME}}.txt`
   - ASPX code-behind: `*.aspx.vb` fajl za taj report
2. Pročitaj celu SQL template — sve JOINove, WHERE uslove, grouping izraze, CASE logiku.
3. Pročitaj VB.NET metodu — svi parametri, IN liste, date filtere, order by logiku.

### 0.2 Schema verifikacija iz baze
Za SVAKU tabelu koja se koristi:
```sql
SELECT COLUMN_NAME, DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS 
WHERE TABLE_NAME = '{{TABLE}}' ORDER BY ORDINAL_POSITION
```
Plus `SELECT TOP 5 * FROM {{TABLE}}` za sample data.

Verifikuj FK veze, data types, NULL-abilnost. Ako kolona ne postoji u bazi — NE koristi je.

### 0.3 Reference report u novoj app
Pročitaj **ProspectClients** ili **CancelLog** kao pattern za non-sales report (ViewBag-based, lista zapisa):
- View: `Views/Reports/ProspectClients.cshtml` (JS-driven, fetch POST za data)
- Repository: `Data/Tenant/ProspectClientsRepository.cs`
- Controller: grep `ProspectClients` u `ReportsController.cs`

Za sales-based report, koristi **AverageBasket** kao pattern (Model-based, form POST).

---

## FAZA 1 — CORE LAYER (4 fajla)

### 1.1 Row Model: `Core/Models/{{REPORT_NAME}}Row.cs`
```csharp
namespace Powersoft.Reporting.Core.Models;
public class {{REPORT_NAME}}Row
{
    // Level1/Level2 za grouping
    public string Level1Code { get; set; } = "";
    public string Level1Descr { get; set; } = "";
    public string Level2Code { get; set; } = "";
    public string Level2Descr { get; set; } = "";
    // Data kolone — iz legacy SQL + INFORMATION_SCHEMA verifikacije
}
```

### 1.2 Filter Model: `Core/Models/{{REPORT_NAME}}Filter.cs`
```csharp
namespace Powersoft.Reporting.Core.Models;
public class {{REPORT_NAME}}Filter
{
    public DateTime DateFrom { get; set; } = new(DateTime.Today.Year, 1, 1);
    public DateTime DateTo { get; set; } = DateTime.Today;
    public string DateField { get; set; } = "{{DEFAULT_DATE_COLUMN}}";
    public string PrimaryGroup { get; set; } = "NONE";
    public string SecondaryGroup { get; set; } = "NONE";
    public int MaxRecords { get; set; } = 50000;
    public string SortColumn { get; set; } = "{{DEFAULT_SORT}}";
    public string SortDirection { get; set; } = "DESC";
    // Report-specific filtere dodaj ovde
}
```

### 1.3 Interface: `Core/Interfaces/I{{REPORT_NAME}}Repository.cs`
```csharp
namespace Powersoft.Reporting.Core.Interfaces;
public interface I{{REPORT_NAME}}Repository
{
    Task<(List<{{REPORT_NAME}}Row> rows, int totalRecords)> GetDataAsync({{REPORT_NAME}}Filter filter);
}
```

### 1.4 Constants — dodaj u `Core/Constants/SessionKeys.cs` (klasa `ModuleConstants`):
```csharp
public const int ActionView{{REPORT_NAME}} = {{NEXT_ACTION_ID}};      // sledeci slobodan ID
public const int ActionSchedule{{REPORT_NAME}} = {{NEXT_ACTION_ID+1}};
public const string IniHeader{{REPORT_NAME}} = "{{INI_CODE}}";        // max 10 char
public const string IniDescription{{REPORT_NAME}} = "{{REPORT_NAME}} Report Layout";
```

I u `Core/Constants/ReportTypeConstants.cs`:
```csharp
public const string {{REPORT_NAME}} = "{{REPORT_NAME}}";
// + dodaj u Schedulable HashSet
```

---

## FAZA 2 — DATA LAYER (2 fajla)

### 2.1 Repository: `Data/Tenant/{{REPORT_NAME}}Repository.cs`

Struktura (prati ProspectClientsRepository pattern):
```
GetDataAsync(filter) → BuildWhereAndParams → BuildSelect → [BuildHistorySelect ako treba UNION ALL]
                     → COUNT query → DATA query → Map to Row objects
```

**SQL pravila (OBAVEZNA):**
- `CONVERT(DATE, t.{{DateColumn}}) BETWEEN @dFrom AND @dTo` — date filter
- `SqlParameter` za SVE user input vrednosti, NIKAD string interpolation
- `LEFT JOIN` za opcione dimenzije (brand, category, agent, attributes)
- `INNER JOIN` za obavezne veze (header → detail)
- Return/credit transakcije: množiti sa -1
- `ISNULL(col, '')` za string kolone, `ISNULL(col, 0)` za numeričke
- GroupBy/OrderBy kolone iz whitelisted enum-a, NIKAD raw user input

**ResolveGroupExpr pattern:**
```csharp
private static (string code, string descr) ResolveGroupExpr(string group) => group?.ToUpperInvariant() switch
{
    "STATUS" => ("ISNULL(s.StatusCode,'')", "ISNULL(s.StatusName,'(No Status)')"),
    "CATEGORY" => ("CAST(ISNULL(t.fk_CategoryID,0) AS VARCHAR(20))", "ISNULL(c.CategoryDescr,'(No Category)')"),
    _ => ("''", "''")
};
```

**Helper metode (copy iz ProspectClientsRepository):**
- `CloneParams(List<SqlParameter>)` — za reuse istih parametara u COUNT i DATA query
- `GetStr(reader, col)`, `GetInt(reader, col)`, `GetDecimal(reader, col)`, `GetNullableDateTime(reader, col)`

### 2.2 DI registracija — 2 fajla:

**`Core/Interfaces/ITenantRepositoryFactory.cs`** — dodaj:
```csharp
I{{REPORT_NAME}}Repository Create{{REPORT_NAME}}Repository(string connectionString);
```

**`Data/Factories/TenantRepositoryFactory.cs`** — dodaj:
```csharp
public I{{REPORT_NAME}}Repository Create{{REPORT_NAME}}Repository(string connectionString)
{
    if (string.IsNullOrWhiteSpace(connectionString)) throw new ArgumentNullException(nameof(connectionString));
    return new {{REPORT_NAME}}Repository(connectionString);
}
```

---

## FAZA 3 — WEB LAYER (4 fajla)

### 3.1 Export Services — dodaj metode u svaki:

**`Web/Services/ExcelExportService.cs`:**
```csharp
public byte[] Generate{{REPORT_NAME}}Excel(List<{{REPORT_NAME}}Row> rows, {{REPORT_NAME}}Filter filter) { ... }
```

**`Web/Services/CsvExportService.cs`:**
```csharp
public byte[] Generate{{REPORT_NAME}}Csv(List<{{REPORT_NAME}}Row> rows, {{REPORT_NAME}}Filter filter) { ... }
public string Generate{{REPORT_NAME}}CsvString(List<{{REPORT_NAME}}Row> rows, {{REPORT_NAME}}Filter filter) { ... }
```

**`Web/Services/PdfExportService.cs`:**
```csharp
public byte[] Generate{{REPORT_NAME}}Pdf(List<{{REPORT_NAME}}Row> rows, {{REPORT_NAME}}Filter filter) { ... }
```

KRITICNO: `colCount` u PDF MORA odgovarati broju header + data ćelija. Prebroji ručno.

### 3.2 Controller — dodaj u `Web/Controllers/ReportsController.cs`

**Obaveznih 11+ actions:**

| # | Action | HTTP | Svrha |
|---|--------|------|-------|
| 1 | `{{REPORT_NAME}}()` | GET | Renderuje stranicu, čita saved layout |
| 2 | `Get{{REPORT_NAME}}Data(...)` | POST | Vraća JSON data |
| 3 | `Get{{REPORT_NAME}}Lookups()` | GET | Vraća dropdown opcije (agenti, kategorije...) |
| 4 | `Export{{REPORT_NAME}}Csv(...)` | GET | CSV download |
| 5 | `Export{{REPORT_NAME}}Excel(...)` | GET | Excel download |
| 6 | `Export{{REPORT_NAME}}Pdf(...)` | GET | PDF download |
| 7 | `{{REPORT_NAME}}PrintPreview(...)` | GET | Print preview view |
| 8 | `Send{{REPORT_NAME}}ReportEmail(...)` | POST | Email sa attachment-om |
| 9 | `Analyze{{REPORT_NAME}}Report(...)` | POST | AI analiza |
| 10 | `Save{{REPORT_NAME}}Schedule(...)` | POST | Schedule CRUD |
| 11 | `Get{{REPORT_NAME}}Schedules()` | GET | Lista schedule-a |

**Plus Layout CRUD actions (6):**
- `Save{{REPORT_NAME}}Layout` (POST, default)
- `Reset{{REPORT_NAME}}Layout` (POST)
- `List{{REPORT_NAME}}Layouts` (GET)
- `Save{{REPORT_NAME}}LayoutAs` (POST, named)
- `Load{{REPORT_NAME}}Layout` (POST, by headerCode)
- `Delete{{REPORT_NAME}}Layout` (POST)

**Private helper:**
```csharp
private async Task<(List<{{REPORT_NAME}}Row> rows, {{REPORT_NAME}}Filter filter)?> Run{{REPORT_NAME}}Query(
    DateTime dateFrom, DateTime dateTo, string dateField, ...all filter params...)
```

**KRITIČNO — Parameter Sync:**
Svaki filter parametar MORA biti u potpisu SVIH 8+ akcija koje zovu `Run{{REPORT_NAME}}Query`.
Ako dodaš novi filter (npr. `categoryFilter`), moraš ga dodati u:
1. Filter model
2. `Run{{REPORT_NAME}}Query` potpis + filter assignment
3. `Get{{REPORT_NAME}}Data` potpis
4. `Export{{REPORT_NAME}}Csv` potpis + pass-through
5. `Export{{REPORT_NAME}}Excel` potpis + pass-through
6. `Export{{REPORT_NAME}}Pdf` potpis + pass-through
7. `{{REPORT_NAME}}PrintPreview` potpis + pass-through
8. `Send{{REPORT_NAME}}ReportEmail` potpis + pass-through
9. `Analyze{{REPORT_NAME}}Report` potpis + pass-through
Ako preskočiš jedan — export vraća drugačije podatke od ekrana.

---

## FAZA 4 — RAZOR VIEW (`Views/Reports/{{REPORT_NAME}}.cshtml`)

Ovo je najobimniji fajl. Prati ProspectClients.cshtml kao template.

### View struktura (redosled):
```
@{ ViewData, connectedDb, hasSavedLayout, savedLayout }
<style> ... sortable, status-badge, stat-card, group-header </style>
<div> Breadcrumb + Title </div>
<div class="card"> Report Parameters
  - Quick Date Presets (8 radio buttons)
  - Date From/To, DateField dropdown
  - Report-specific filter dropdowns
  - Include History toggle (ako treba)
  - Primary/Secondary Group dropdowns
  - Action bar: Generate | Schedule | Layouts | Save | Save As | Reset
</div>
<div id="reportAlert"> (error display) </div>
<div id="reportLoading"> (spinner) </div>
<div id="kpiCards"> (summary cards) </div>
<div id="reportResults"> Results card
  - Toolbar: Preview | CSV | Excel | PDF | Email | AI Analyze
  - <table> sa sortable headers
</div>
<div id="previewPanel"> (iframe preview) </div>
<!-- Schedule Modal -->
<!-- Send Email Modal -->
<!-- AI Analysis Modal (sa chat follow-up) -->
<!-- Save Layout As Modal -->
<script> ... sve JS funkcije ... </script>
```

### JS funkcije (SVE su obavezne):

**Core:**
- `init()` — date defaults, event listeners, loadFilterDropdowns, AI status check
- `loadFilterDropdowns()` — fetch lookups → populate selects → apply saved layout
- `getFilterParams()` — kolekcija SVIH filter vrednosti u objekat
- `buildExportUrl(action)` — gradi query string URL za export/preview
- `generateReport()` — validacija → POST → renderKpis + renderTable
- `sortBy(col)` — client-side sort sa date/numeric/string tipovima
- `renderTable(data)` — header sa `sth()` helper za sortable `<th>`, body rendering
- `renderGroupedRows(data)` / `renderRows(items)` — sa tačnim `colCount`
- `renderKpis(data)` — summary kartice
- `escapeHtml(s)`, `fmtDate(d)`, `fmtInt(v)`, `formatNumber(v)`, `groupBy(arr, key)`

**Exports:**
- `exportServerCsv()`, `exportServerExcel()`, `exportServerPdf()` — guard za prazne podatke
- `togglePreview()` — iframe toggle sa auto-resize

**Email:**
- `openSendEmailModal()`, `loadEmailTemplates()`, `onEmailTemplateChange()`, `sendReportEmail()`
- `validateEmailList(input)` — split po , i ; sa regex validacijom

**AI:**
- `analyzeReport()`, `renderAiAnalysis(a)`, `initAiConversation()`, `sendAiChat()`
- `appendChatBubble(role, content, id)`, `removeChatBubble(id)`
- `togglePromptEditor()`, `loadAiPromptTemplates()`, `saveAiPromptTemplate()`
- `saveAiLang(v)`, `getAiLang()`

**Schedule:**
- `loadSchedules()`, `saveSchedule()`, `loadScheduleIntoForm(id)`, `deleteSchedule(id)`
- `resetScheduleForm()`

**Layout:**
- `collectCurrentLayout()` — MORA uključiti SVE filtere + DatePreset
- `saveCurrentLayout()`, `resetSavedLayout()`
- `loadLayoutsList()`, `renderLayoutsDropdown(layouts)`, `loadNamedLayout(headerCode)`
- `applyLayoutParameters(parms)` — MORA setovati SVE filtere
- `saveLayoutAsConfirm()`, `deleteNamedLayout(headerCode, displayName)`
- `showLayoutToast(message, type)`, `isAuthRedirect(response)`, `showSessionExpiredToast()`

### Sortable headers pattern:
```javascript
function sth(label, col, align) {
    var cls = 'sortable' + (align ? ' text-end' : '');
    if (_sortCol === col) cls += _sortDir === 'ASC' ? ' sort-asc' : ' sort-desc';
    var icon = _sortCol === col ? (_sortDir === 'ASC' ? 'bi-caret-up-fill' : 'bi-caret-down-fill') : 'bi-caret-up-fill';
    return '<th class="'+cls+'" onclick="sortBy(\''+col+'\')">'+label+'<i class="bi '+icon+' sort-icon"></i></th>';
}
```

### colCount formula:
```
colCount = FIXED_COLUMNS + (hasPrimary?1:0) + (hasSecondary?1:0) + (inclHistory?1:0)
```
Prebroji FIXED_COLUMNS ručno iz renderRows — svaki `html += '<td>...'` je 1.

---

## FAZA 5 — UI REGISTRACIJA (3 fajla)

### 5.1 Reports/Index.cshtml
**Sidebar** — dodaj u CRM (ili Stock Control) nav sekciju:
```html
<a class="nav-link" asp-action="{{REPORT_NAME}}">
    <i class="bi bi-{{ICON}} me-2"></i>{{DISPLAY_NAME}}
</a>
```

**Dashboard card** — dodaj card sa border bojom, ikonom, opisom, "Open Report" dugmetom.

### 5.2 _Layout.cshtml (opciono)
Dodaj `<li class="nav-item">` u navbar ako report zaslužuje top-level prečicu.

---

## FAZA 6 — VERIFIKACIJA

### 6.1 Build
```powershell
dotnet build "C:\p\Powersoft.Reporting\Powersoft.Reporting.sln" --no-restore
```
Mora biti **0 Error(s)**. Warnings su OK ako su pre-existing.

### 6.2 ReadLints
Proveri lints na SVIM izmenjenim fajlovima. Mora biti clean.

### 6.3 SQL verifikacija
Pokreni finalni SQL direktno protiv baze i uporedi sa legacy rezultatima.

### 6.4 12-point filter sync checklist
Za SVAKI filter parametar proveri da prolazi kroz:
1. ✅ Filter model property
2. ✅ Repository `BuildWhereAndParams`
3. ✅ Repository `ResolveGroupExpr` (ako je groupable)
4. ✅ Controller `Get{{REPORT_NAME}}Data` potpis + filter set
5. ✅ Controller `Run{{REPORT_NAME}}Query` potpis + filter set
6. ✅ Controller svih export akcija (CSV/Excel/PDF) potpis + pass-through
7. ✅ Controller PrintPreview potpis + pass-through
8. ✅ Controller SendEmail potpis + pass-through
9. ✅ Controller AIAnalyze potpis + pass-through
10. ✅ JS `getFilterParams()` — čita vrednost iz DOM elementa
11. ✅ JS `buildExportUrl()` — uključen u query string
12. ✅ JS `collectCurrentLayout()` — uključen za save
13. ✅ JS `applyLayoutParameters()` — setuje vrednost nazad
14. ✅ Schedule `filterJson` — automatski preko `getFilterParams()`

---

## POZNATI PITFALLS (naučeno na grešci)

| # | Zamka | Objašnjenje |
|---|-------|-------------|
| 1 | **tbl_OrderStatus.TableName filter** | OrderStatus tabela čuva statuse za više entiteta (Orders, Invoices, Leads). UVEK dodaj `AND s.TableName = '{{TABLE}}'` na JOIN. |
| 2 | **OrderStatusHTML, ne StatusHTML** | Kolona se zove `OrderStatusHTML`. `StatusHTML` ne postoji — runtime SQL error. |
| 3 | **tbl_CustCategory kolone** | Kolone su `pk_CategoryID`, `CategoryDescr`, `CategoryCode`, `Filter` — NE `pk_CustCategoryID`. Uvek proveri INFORMATION_SCHEMA. |
| 4 | **PDF colCount mismatch** | Ako header ima 22 kolone a data 21 — PDF se raspada. Prebroji ručno oba. |
| 5 | **UNION ALL parametri** | SQL Server koristi iste `@paramName` za oba dela UNION ALL. NE treba duplirane parametre. |
| 6 | **String Replace za history aliase** | `whereClause.Replace("t.", "h.")` može zameni delove koji nisu alias. OK za SQL kolone ali pazi na edge case-ove. |
| 7 | **PowerShell sintaksa** | Koristi `;` umesto `&&`. Heredoc: `$msg = @"..."@`. |
| 8 | **PdfExportService helper** | Metoda je `AddDataCell`, NE `AddPdfCell`. |
| 9 | **IEmailSender API** | Metoda je `SendAsync` sa `IEnumerable<EmailAttachment>`, NE `SendWithAttachmentAsync`. |
| 10 | **ReportSchedule properties** | `ScheduleId` (ne `Id`), `scheduleTime` je string → `TimeSpan.Parse()`. Nema `DatabaseCode` na modelu. |
| 11 | **IScheduleRepository metode** | `CreateScheduleAsync` / `UpdateScheduleAsync` / `GetSchedulesForReportAsync` — NE `SaveScheduleAsync` / `GetSchedulesAsync`. |
| 12 | **buildExportUrl** | Koristi `@Url.Action("Index","Reports").replace('Index', action)`. Ovo radi jer ASP.NET Core generiše `/Reports/Index`. |
| 13 | **Saved layout timing** | Dropdown opcije moraju biti loaded (DOM populated) PRE `applyLayoutParameters` poziva. Stavi apply u `.then()` callback od fetch lookups. |
| 14 | **Dupli applyLayout poziv** | Ako imaš `@if(hasSavedLayout)` blok na dnu `<script>` + poziv u loadFilterDropdowns callback — obriši jedan. |
| 15 | **credentials:'same-origin'** | Dodaj na fetch pozive koji koriste session cookie. Moderni browseri šalju cookies za same-origin po default-u, ali budi konzistentan. |

---

## {{PLACEHOLDERS}} — POPUNI PRE PASTE-OVANJA

### Report Name
`{{REPORT_NAME}}` = _________________________________

### Original Legacy Location
- WQR.vb metoda: `Function Get___________`
- SQL template: `Powersoft.CloudQueries\Resources\RAS_Get___________.txt`
- ASPX code-behind: `_________________________________.aspx.vb`

### Primarne tabele
- `tbl_____________` — opis
- `tbl_____________` — opis

### Lookup tabele
- `tbl_____________` — opis
- `tbl_____________` — opis

### Svrha reporta
___________________________________________________________

### Filteri
- Date range (polje: _____________)
- Status (tabela: _____________)
- ________________________
- ________________________

### Kolone u reportu
- ________________________
- ________________________
- ________________________

### Grouping opcije
- NONE, STATUS, _____________, _____________

### Specijalni zahtevi
- History tabela? Da/Ne → `tbl______________`
- KPI kartice: _____________
- Boja za card na Index stranici: _____________
- Bootstrap ikona: `bi-___________`
