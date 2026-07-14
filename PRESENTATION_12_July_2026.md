# PowerSoft 365 AI Reports — Progress Presentation

**Period:** 29 June 2026 – 12 July 2026
**Developer:** Nikola Mladenovic
**Project:** Powersoft.Reporting (ASP.NET Core 6.0 MVC) + Powersoft.CloudAccounting (legacy VB.NET)
**Production:** https://reports-ai.powersoft365.com
**Status:** Commits through 9 July deployed & pushed to both GitHub remotes (`a58b835`).
Two client-requested features from 9–12 July are code-complete, **pending final verification & deploy** (see Section 6 and the INTERNO warnings).

---

## ⚠️ INTERNO — PROČITAJ PRE PREZENTACIJE (ne prikazivati klijentu)

**Šta je deployovano, a šta NIJE — ne sme da se pomeša u priči:**

| Stavka | Status | Smeš da demonstriraš na produkciji? |
|---|---|---|
| Schedule Today/Yesterday preseti + klik na red schedule-a | 🟢 Deployed (30.06) | DA |
| QA framework, 433 provere, QA_REPORT.html | 🟢 Committed (30.06) | DA (pokaži HTML lokalno) |
| Industry Template Packs (sve 3 faze) | 🟢 Deployed (01.07) | DA |
| **Cash Flow (Direct) report** | 🟢 Deployed (09.07) | DA |
| **Customer Not Purchased in X Days report** | 🟢 Deployed (09.07) | DA |
| Layout name collision fix (grčka imena / duga imena) | 🟢 Deployed (09.07) | DA |
| Sort Sizes by Sequence (SPLASH) — Reporting app | 🟡 Kod gotov, necommitovano | NE — samo lokalno ili najavi |
| Sort Sizes by Sequence (SPLASH) — legacy CloudAccounting (WQR.vb) | 🟡 Kod gotov, necommitovano | NE — samo lokalno ili najavi |
| Attribute captions "GENDER" umesto "Attribute 1" (George) | 🟡 Kod gotov, necommitovano | NE — samo lokalno ili najavi |

**Pre sastanka obavezno:**
1. Uloguj se na https://reports-ai.powersoft365.com i proveri da radi (302/200).
2. Otvori Cash Flow na demo bazi i generiši JEDNOM pre sastanka — prvi query može biti spor, nemoj da se desi live.
3. Pripremi `QA_REPORT.html` otvoren u tab-u (lokalni fajl iz repoa).
4. Ako planiraš da pokažeš Sort by Size Sequence ili GENDER kolone — podigni **lokalni** build unapred (`dotnet run` na Reporting.Web), NE pokazuj to na produkciji jer tamo ne postoji.
5. Imaj otvoren `_SQL/PBI_CASHFLOW_SECTION_MAPPING.md` (u CloudAccounting repou) za slučaj da Marinos/Olimpiu pitaju za detalje mapiranja.

**Šta NE tvrdiš ni u kom slučaju:**
- Ne tvrdi da je budget u Cash Flow-u identičan PBI-ju — NIJE, PBI čita ručni Excel sa SharePoint-a, mi čitamo `tbl_AccBudgetDetails`. To je **dokumentovano odstupanje** — reci to otvoreno ako se pomene (skripta ispod ima spreman odgovor).
- Ne tvrdi da su SPLASH/GENDER fičeri "done" — reci "code-complete, in final verification, deploying this week".
- Ne obećavaj datum za multi-tenant Cash Flow mapping UI — postoji tabela per tenant, ali admin UI za editovanje mapiranja još ne postoji.

---

## Opening (What to Say)

> "Good morning everyone. I'll walk you through the last two weeks — June 29th to July 12th — on PowerSoft 365 AI Reports.
>
> The headline items: we delivered the **Cash Flow report** that Marinos requested — ported one-to-one from the Power BI model, and verified against it to the cent. We also delivered a second new report, **Customer Not Purchased in X Days**. That brings us from 11 to **13 reports**. On top of that we shipped the **Industry Template Packs** system George asked for — packaged report bundles that can be applied to any company in a couple of clicks — plus a set of scheduler usability improvements, a big automated QA pass, and two client-specific requests from SPLASH and from George that are in final verification right now.
>
> Everything I show today is live on production unless I explicitly say otherwise. Let me go through it in order."

**INTERNO:** Vodi zaključkom — Cash Flow je zvezda ovog perioda, njega George i Marinos čekaju. Ne troši više od 2 minuta na uvod.

---

## Part 1 — Cash Flow (Direct) Report — NEW (deployed 9 July)

### What to Say

> "The biggest deliverable this period is the Cash Flow report. Let me explain what was actually involved, because this was not a normal report port.
>
> There was no specification document. The only source of truth was the Power BI file — `HE11901-ARVO-Accounting.pbix` — which I received from Olimpiu on July 9th. I extracted the internal data model from the PBIX file programmatically: the M queries, the DAX measures, the relationships, and most importantly the **section mapping table** that decides which ledger account falls into which cash-flow line — Operating Cash In, Operating Cash Out, Investing, Financing, and so on.
>
> Two important discoveries came out of that analysis:
>
> **First** — the account-to-section mapping in Power BI doesn't live in the database at all. It's maintained in an **Excel file on SharePoint**, per client. That means it's a per-client configuration. So in our implementation I created a proper database table — `tbl_CashFlowMapping` in our reporting schema — seeded with the complete 48-row Arvo mapping, created automatically by our schema migration on login. When another client needs different account ranges, it's a data change, not a code change.
>
> **Second** — I verified our engine against Power BI **exhaustively**, not by eyeballing. The account-to-section resolution reproduces the Power BI bridge table exactly — all **4,945 account-line pairs match**. And the displayed amounts match the Power BI matrix **to the cent** — I checked full month columns for two different years. One interesting detail: the statement always reconciles — Cash In plus Cash Out plus the other sections sum to exactly zero against the Bank movement, which is a built-in self-check that Power BI itself doesn't surface.
>
> The report supports: date range, **monthly breakdown** columns, **prior-year comparison**, and **budget comparison**. One honest caveat on budget: Power BI reads the budget from a manually maintained Excel sheet on SharePoint. We can't and shouldn't depend on someone's Excel file — so our budget column reads from the **budget tables in the tenant database**. That's a documented deviation, and I want it on the record.
>
> And as with every report in the app, Cash Flow has the complete feature set from day one: Excel, CSV and PDF export, print preview, send by email, AI analysis, scheduling, and saved layouts. The scheduler was verified with a live run."

### LIVE DEMO — What to Show (5 min)

1. Dashboard → otvori **Cash Flow** karticu (pokaži da postoji nova kartica).
2. Postavi period npr. Jan–Mar 2025 → **Generate**. Pokaži strukturu: Operating Cash In / Cash Out / Investing / Financing / Other / Bank, kategorije unutar grupa, i **net movement red koji se slaže sa Bank grupom** — "the statement reconciles with itself".
3. Uključi **Monthly breakdown** → Generate → pokaži kolone po mesecima.
4. Uključi **Prior year comparison** → pokaži uporedne kolone.
5. Klikni **Excel export** → otvori fajl na 5 sekundi ("same numbers, same structure").
6. Otvori **Print Preview** — čist izgled za štampu.
7. Pokaži **Schedule** tab — "this can land in your inbox every Monday morning with an AI summary attached."

**INTERNO:**
- Ako Marinos pita "zašto se brojevi u PBI drill-through ne slažu" — odgovor: PBI-jev account-level drill-through koristi drugačiji per-account sign i **ne slaže se ni sa sopstvenom matricom**; mi smo namerno svuda zadržali matrix sign. To je dokumentovano u kodu i u analizi.
- Ako pitaju za druge klijente: mapiranje je per-tenant tabela, seed je Arvo; za novog klijenta treba uneti njegove opsege konta — **admin UI za to još ne postoji**, unosi se SQL-om. Ne obećavaj UI bez dogovora o prioritetu.
- Nepoznati konti ne nestaju — idu u "(Unassigned)" red. PBI ih tiho ispušta, mi ih namerno prikazujemo ("no cash movement can hide").

---

## Part 2 — Customer Not Purchased in X Days — NEW (deployed 9 July)

### What to Say

> "The second new report is the one George labeled 'Report B' — **Customers who have not purchased in X days**. You pick a threshold — 30, 60, 90 days or a custom number — and you get the list of customers whose last purchase is older than that, with their contact details and last-purchase information. It's a retention and win-back tool: the obvious use case is scheduling it monthly to the sales team with AI analysis switched on, so the AI points out which lapsed customers historically had the highest value.
>
> Like Cash Flow, it shipped complete: exports, print preview, email, AI analysis, scheduling with the standard quick date presets, saved layouts."

### LIVE DEMO — What to Show (2 min)

1. Otvori **Customer Not Purchased** sa dashboarda.
2. Postavi 90 dana → Generate → pokaži listu (ime, kontakt, last purchase date/value).
3. Promeni na 30 dana → Generate → lista se skrati/produži.
4. Pokaži AI Analyze dugme — po mogućstvu klikni i pročitaj jednu rečenicu insight-a.

**INTERNO:** Kratko i efektno — ovo je jednostavan report, nemoj ga razvlačiti. Ako pitaju za "items not purchased by customer" varijantu (originalna formulacija Report B je imala i item ugao) — reci da je trenutna verzija customer-level, item-level drill je prirodna sledeća iteracija ako je žele.

---

## Part 3 — Industry Template Packs (deployed 1 July, three phases)

### What to Say

> "George asked for a way to give a new company a ready-made set of scheduled reports instead of configuring everything by hand. That's now live, and it went through three phases in one day, June 1st — sorry, July 1st:
>
> **Phase one — the packs themselves.** A company admin opens the dashboard, clicks 'Industry Templates', and picks a pack — the proof-of-concept packs are **Fashion** and **Supermarket**. Inside a pack you see the individual reports it contains — each with a sensible pre-configured schedule — and you choose exactly which ones to apply, with checkboxes. Applying is **idempotent per report**: you can apply three reports today, come back next month and apply two more, and nothing gets duplicated.
>
> **Phase two — the catalog moved to the central database.** Initially the packs were seeded in code, which would mean a redeploy every time we want to change a template. Now they live in central tables — `tbl_RE_TemplatePack` — so packs are authored and edited **without touching the code**. On top of that I built the three workflows George specifically asked for:
> - **Save as Template** — take any existing schedule a company has fine-tuned, and promote it into a portable template item, with tenant-specific selections automatically stripped out.
> - **Multi-company apply** — provision a pack, or a subset of it, into **many companies at once**, idempotent per company.
> - **Access control** — authoring templates is gated to the Powersoft webmaster account; applying a pack to your own company remains open to company admins.
>
> **Phase three — authoring safety.** When authoring templates for Catalogue, Pareto and Charts, the admin UI now exposes the correct filter controls for each report type, with **safe defaults enforced** — for example a Pareto template defaults to Category analysis, never an unfiltered Item scan that could time out a large tenant. Every dropdown value in the authoring UI is covered by unit tests that assert it maps to a real engine value, so a typo in a template can't silently produce a wrong schedule."

### LIVE DEMO — What to Show (4 min)

1. Dashboard → **Industry Templates** dugme → otvori modal.
2. Klikni na **Fashion** pack → pokaži per-report checkboxove, Select all / Clear, i da su već primenjeni reporti zaključani.
3. Primeni jedan report → pokaži da se pojavio u All Schedules.
4. (Kao webmaster) otvori **Template Admin** stranicu → pokaži katalog packova, edit forme, i multi-company apply ekran sa listom kompanija.
5. Na **All Schedules** pokaži **Save as Template** akciju na postojećem schedule-u.

**INTERNO:**
- Multi-company apply demonstriraj OPREZNO — na demo kompanijama, ne na živim. Prilikom E2E testa 1. jula sve kreirane test podatke sam očistio; nemoj sada da napraviš đubre na produkciji.
- Ako George pita "ko sme da pravi packove" — odgovor: samo webmaster nalog (Ranking 1), jedna centralna provera `CanAuthorTemplates()`, nije raspršeno po kodu.

---

## Part 4 — Scheduler Usability (deployed 30 June)

### What to Say

> "Three smaller quality-of-life items from the end of June:
>
> **First**, schedules now support **Today** and **Yesterday** as date presets. That closed a real gap: a daily report scheduled for 23:00 couldn't previously say 'today's numbers' — now 'send me today's sales every evening' is a first-class option.
>
> **Second**, on the All Schedules page, **clicking any schedule row opens the actual report with that schedule's filters pre-loaded and the report auto-generated**. Before, if you wanted to see what a scheduled report actually produces, you had to reconstruct the filters by hand. Now it's one click — and it's also the natural way to tweak a schedule: click it, adjust, re-save.
>
> **Third**, a few polish fixes: the Charts print preview now opens inline instead of a new tab, and the Purchases vs Sales subtotal and grand-total rows now show the quantity-percent, value-percent and stock figures that used to show as dashes."

### LIVE DEMO — What to Show (2 min)

1. Otvori bilo koji report → Schedule modal → pokaži **Today / Yesterday** u date range dropdownu.
2. Idi na **All Schedules** → klikni na red → report se otvara sa učitanim filterima i sam se generiše.
3. (Usput, bez posebne najave) na Purchases vs Sales pokaži da subtotal redovi imaju Qty%/Val% vrednosti.

---

## Part 5 — QA & Verification Work (committed 30 June)

### What to Say

> "Following up directly on George's directive about testing everything thoroughly — two things were committed to the repository at the end of June:
>
> **First, the full E2E test harness** — 15 PowerShell scripts, now version-controlled in the repo, covering **433 automated checks** across all reports: every page load, every generate, every export format, print previews, schedule endpoints, layout save/load round-trips, and numerical integrity checks — for example the harness downloads the CSV export and re-verifies that Available equals Stock minus Reserved plus On-Order in the totals row. Deep-dive suites exist for Average Basket — 122 checks — and Purchases vs Sales — 105 checks. After any change we can re-run the whole thing and know within minutes whether anything broke.
>
> **Second, a client-facing QA report** — `QA_REPORT.html` — a visual dashboard of those 433 results with a methodology section, which you're welcome to keep on file.
>
> And the number that matters most going forward: the unit test suite grew from 179 tests at the start of this period to **299 tests, all passing**, as of the Cash Flow commit — Cash Flow's statement builder alone added a dedicated test suite, and the template system added around 30 more."

### What to Show (1 min)

1. Otvori `QA_REPORT.html` lokalno → skroluj kroz PASS sekcije → "433 of 433".
2. Jedna rečenica: "This report and all 15 test scripts are in the repository — the QA process is reproducible, not a one-off."

**INTERNO:** Ako George pita da li se testovi vrte automatski na svaki commit (CI) — iskreno: ne još, vrte se ručno pre deploy-a; CI pipeline je kandidat za sledeći period. Ne izmišljaj CI koji ne postoji.

---

## Part 6 — Client Requests In Final Verification (9–12 July) — NOT YET DEPLOYED

**INTERNO — OVDE PAZI:** Ovo je kod završen 9–12. jula, ali NIJE commitovan ni deployovan u trenutku pisanja. Prezentuj kao "this week's work, in final verification, deploying in the coming days". Ako demo — SAMO lokalni build.

### 6.1 SPLASH — Sort Sizes by Size Sequence (both systems)

### What to Say

> "SPLASH reported a real-world annoyance in the Purchases vs Sales report: when you group by Size, sizes sort alphabetically — so 36.5 lands after 36 but also after 37 wherever the text sort puts it, and shoe-size sequences come out shuffled. The system already has a proper size ordering — the size sequence table that defines that 36.5 comes before 37 — it just wasn't used for sorting.
>
> I've implemented this in **both systems**, because SPLASH uses the legacy Powersoft365 report as well:
>
> - In the **legacy application**, the Power Purchases and Sales report got a new 'Size Sequence' option in its Sort By dropdown. This went into the shared query engine — the same 55,000-line module that builds all legacy report SQL — carefully, as an additive change: the new sort mode only activates when explicitly selected, everything else is untouched.
> - In the **new Reporting app**, Purchases vs Sales got a 'Sort Sizes by Sequence' checkbox that appears **only when one of the grouping levels is Size**. And it's wired through the complete pipeline — screen, all three exports, print preview, email, AI analysis, schedules and saved layouts — so an export can never disagree with the screen.
>
> One engineering detail worth mentioning: sizes that share the same display name are forced to share one sequence position, so a group can never be torn apart in the output; and sizes with no configured sequence sort to the end rather than disappearing or crashing."

### 6.2 George — Real Attribute Names Instead of "Attribute 1"

### What to Say

> "George's request: tenants can define custom item attributes — a fashion client renames Attribute 1 to 'GENDER', Attribute 2 to 'MATERIAL', and that's what legacy Powersoft365 shows. Our app was showing the generic 'Attribute 1/2/3' labels everywhere.
>
> Now the app reads the tenant's own attribute definitions — the same source legacy uses — and shows the real names **everywhere attributes appear**: the Catalogue column headers, the group-by dropdowns, the group header rows, the Charts dimension picker, the filter buttons and filter chips in the items selection panel, and the Excel, CSV and PDF exports — including reports generated by the scheduler. Where a tenant hasn't named an attribute, the generic label stays. If the lookup ever fails, the report still renders with generic labels — a cosmetic feature is never allowed to break a report.
>
> Both of these are code-complete with unit tests, going through final verification now, and I expect them deployed within days."

### What to Show (optional, LOCAL build only, 3 min)

1. Lokalno: Purchases vs Sales → Primary Group = Size → pojavi se "Sort Sizes by Sequence" checkbox → Generate sa i bez → pokaži redosled 36 / 36.5 / 37.
2. Lokalno: Catalogue na fashion demo bazi → kolone/dropdownovi pokazuju GENDER umesto Attribute 1; isto u Excel exportu.

**INTERNO:**
- Ako ne stigneš da podigneš lokalni build — samo ispričaj, ne pokazuj. Bolje bez demoa nego demo koji pukne.
- Legacy izmena (WQR.vb) je 127 linija u fajlu od 55k linija — naglasi da je aditivna (nova grana samo kad je SizeSequence izabran) jer je to odgovor na prirodno pitanje "did you risk the legacy engine".
- 12-point pipeline za novi filter parametar je ispoštovan u Reporting appu (ViewModel, filter model, SQL, GET, POST, Excel/CSV/PDF/Print, Email, AI, Schedule JSON, SaveLayout, JS collect, Razor input) — pomeni samo ako pitaju kako garantuješ da export = ekran.

---

## Part 7 — Current Application State (Summary Slide)

| Metric | Value |
|---|---|
| Total reports | **13** (was 11) — new: Cash Flow, Customer Not Purchased |
| Full feature set on all 13 (exports, print, email, AI, schedule, layouts) | Yes |
| Unit tests | **299/299 passing** (was 179 — +120 this period) |
| Automated E2E checks in repo | 433 (15 PowerShell scripts, version-controlled) |
| Cash Flow parity vs Power BI | 4,945/4,945 account mappings; matrix amounts to the cent |
| Template packs | Live: per-item apply, DB-backed catalog, save-as-template, multi-company apply |
| Commits this period | 7 deployed + 2 features in final verification |
| Code volume this period | ~12,300 lines committed (+ ~460 in verification) |
| Production | Live at reports-ai.powersoft365.com; both GitHub remotes in sync |

> "In summary: two new reports — one of them the most technically demanding so far — the template system George asked for, a permanent automated QA baseline, and two client-specific requests a few days from deploy."

---

## Part 8 — Recommended Live Demo Flow (~20 min total)

1. **Login + dashboard** (1 min) — pokaži 13 kartica, pomeni da su dve nove.
2. **Cash Flow** (5 min) — Section 1 demo koraci. Ovo je centralni deo.
3. **Customer Not Purchased** (2 min) — Section 2 koraci.
4. **Template Packs** (4 min) — Section 3 koraci.
5. **Scheduler UX** (2 min) — Today/Yesterday + klik na red.
6. **QA report** (1 min) — otvori HTML, jedna rečenica.
7. **SPLASH + GENDER najava** (2-3 min) — priča, opciono lokalni demo.
8. **Q&A** (ostatak).

**INTERNO — redosled je namerno ovakav:** Cash Flow odmah posle logina dok je pažnja najveća; template packs posle reporta jer je George lično tražio; "in verification" stavke IDU POSLEDNJE da se u pamćenju ne pomešaju sa deployovanim stvarima.

---

## Part 9 — Time Transparency (if asked)

| Activity | Estimate | Evidence |
|---|---|---|
| PBIX reverse engineering (pbixray ekstrakcija, mapping analiza, sign/bridge verifikacija) | ~2 dana | `_SQL/PBI_CASHFLOW_SECTION_MAPPING.md` (09.07), mapping CSV |
| Cash Flow engine + statement builder + full wiring + testovi | ~3 dana | Commit `a58b835`: 34 fajla, ~4,858 linija; 299/299 testova |
| Customer Not Purchased report (kompletan) | ~1 dan | Isti commit — repo, view, exports, scheduler |
| Template packs (3 faze: packs → DB catalog + 3 workflow-a → authoring guards) | ~1 dan intenzivno (01.07) | Commits `ca409f1`, `532ed0a`, `b7fb1f0`; ~3,000 linija |
| E2E QA harness + QA report + prezentacija za prošli period | ~1 dan | Commits `e53203b`, `408c967`; 433 checks |
| Scheduler UX (preseti, klik na red, polish fixevi) | ~0.5 dana | Commit `0b14db2` |
| Catalogue parity audit vs legacy (dokument, 245+ stavki poređenja) | ~1 dan | `_SQL/POWER_CATALOGUE_COMPARISON.md` (06.07) |
| SPLASH size sequence (legacy WQR.vb + Reporting app, 12-point wiring) | ~1.5 dana | Working tree: WQR.vb +127 linija; Reporting ~345 linija |
| Attribute captions (George) — svi touchpointi + testovi | ~1 dan | Working tree + `AttributeCaptionTests.cs` |

---

## Part 10 — Closing & Next Steps (What to Say)

> "To close — this period delivered:
> 1. **Cash Flow**, verified against Power BI to the cent, with the mapping turned into per-client configuration instead of a SharePoint Excel file;
> 2. **Customer Not Purchased in X Days** — the second new report;
> 3. **Industry Template Packs** end-to-end: DB-backed catalog, save-as-template, multi-company provisioning, safe authoring;
> 4. A **permanent automated QA baseline** — 433 E2E checks in the repo, unit tests up from 179 to 299;
> 5. Two client requests — SPLASH size ordering in both systems, and real attribute names — **days away from deploy**.
>
> Open items and proposed next steps:
> - Deploy and confirm the two in-verification features (this week).
> - **Cash Flow mapping admin UI** — the mapping is per-tenant data now; an editing screen would let us onboard non-Arvo clients without SQL. I'd like a priority call on this.
> - **P&L month-by-month variant** — still waiting on the layout example from Marinos.
> - Candidate from earlier discussions: per-user AI usage view, and AI follow-up deep-links from scheduled emails.
>
> Happy to go deeper on anything — particularly the Cash Flow verification methodology, if that's useful."

**INTERNO — očekivana pitanja i spremni odgovori:**

| Pitanje | Odgovor |
|---|---|
| "Zašto budget nije isti kao u PBI?" | PBI čita ručni Excel na SharePoint-u (CashFlowBudget sheet). Zavisiti od tuđeg Excel fajla je operativni rizik; čitamo budget tabele iz tenant baze. Dokumentovano odstupanje — ako insistiraju na Excel paritetu, to je odluka koju oni treba da donesu svesno. |
| "Može li Cash Flow za druge klijente odmah?" | Engine da; mapiranje sekcija je per-tenant podatak — za novog klijenta se unose njegovi opsezi konta (danas SQL-om, sutra admin UI ako se prioritetizuje). |
| "Kako znaš da su brojevi tačni?" | Tri nivoa: 4,945/4,945 bridge parova identično sa PBI; matrix vrednosti do centa za dve godine; statement se interno mora sabrati na nulu protiv Bank grupe — samoproveravajući izveštaj. Plus 299 unit testova i live scheduler run. |
| "Šta ako klijent nema tbl_SizeSequence podešen?" | Veličine bez sekvence idu na kraj liste — ništa ne puca, ništa ne nestaje. |
| "Da li AI piše kod?" (George AI-policy tema) | AI je produktivni alat; svaka izmena prolazi build + testove + E2E harness pre deploy-a — upravo je za to napravljen QA framework iz Part 5. Izvori logike su uvek legacy kod ili PBIX model, citirani u commit porukama. |

---

## Appendix — Technical Evidence

| Item | Reference |
|---|---|
| Scheduler UX commit | `0b14db2` — 30 Jun 2026 |
| QA docs commit | `e53203b` — 30 Jun 2026 (QA_REPORT.html, 433/433) |
| E2E harness commit | `408c967` — 30 Jun 2026 (15 skripti, ~3,353 linija) |
| Template packs, phase 1 | `ca409f1` — 1 Jul 2026 (per-item apply, sanitizer, 11 testova) |
| Template packs, phase 2 | `532ed0a` — 1 Jul 2026 (DB catalog, save-as-template, multi-company, TemplateAdmin) |
| Template packs, phase 3 | `b7fb1f0` — 1 Jul 2026 (authoring controls + parser guard testovi, 290/290) |
| Cash Flow + Customer Not Purchased | `a58b835` — 9 Jul 2026 (34 fajla, ~4,858 linija, 299/299 testova, E2E 31/31 core + 48/48 edge, live scheduler run Success/482 rows) |
| Remotes | origin/master = powersoftapps/master = `a58b835` (verified in sync 12 Jul) |
| PBIX analiza | `Powersoft.CloudAccounting\_SQL\PBI_CASHFLOW_SECTION_MAPPING.md` + `PBI_ChartCF_Direct_mapping.csv` (9 Jul) |
| Catalogue parity audit | `Powersoft.CloudAccounting\_SQL\POWER_CATALOGUE_COMPARISON.md` (6 Jul) |
| SPLASH legacy izmena | Working tree: `Powersoft.CloudQueries\WQR.vb` (+127 linija), `repPowerPurchasesAndSales.aspx/.vb` |
| SPLASH + captions (Reporting) | Working tree: 18 fajlova, ~345 linija + `AttributeCaptionTests.cs` |
| Layout slug collision fix | U commitu `a58b835` — grčka imena / truncation više ne prepisuju tuđe layoute (suffix -2/-3) |

### Verifikacija (za ovaj dokument)
- [x] Svi commitovi u periodu 29.06–12.07 pregledani iz git log-a oba repoa (7 u Reporting, 0 u CloudAccounting)
- [x] Necommitovan rad pregledan iz git diff-a oba repoa (working tree na dan 12.07)
- [x] Remote sync verifikovan: origin i powersoftapps oba na `a58b835`
- [x] Brojevi testova/linija/checkova citirani iz commit poruka i stat-ova, ne procenjeni
- [ ] Manuelni smoke pre prezentacije: login na produkciju, Cash Flow generate, QA_REPORT.html otvoren (Nikola, pre sastanka)
