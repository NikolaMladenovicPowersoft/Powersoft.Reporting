# Hubstaff Timesheet — Powersoft Reporting Module

> **Purpose**: Task-level tracking for time entries.  
> **Period**: 8 working days × 8 hours/day (64 hours total)  
> **Scope**: Login, Authorization, Layout Save/Restore

---

## Phase: Login, Authorization & Layout Save/Restore (Feb 2026)

### Day 1

**Legacy system analysis and security alignment**
> Analyzed existing Powersoft365 authentication and authorization. Documented role hierarchy, permission chain, and module licensing so the new Reporting module enforces the same security boundaries as production. Essential for multi-tenant deployment without introducing gaps.
**Hours**: 3

**Schema verification and data integrity checks**
> Ran verification against central and tenant databases to ensure code matches live schema. Confirmed user access paths, layout persistence structures, and test user setup. Reduces deployment risk and avoids runtime surprises.
**Hours**: 2

**Technical documentation and context capture**
> Updated project context document with findings, verification results, and operating protocol. Keeps decisions traceable and supports consistent development going forward.
**Hours**: 3

---

### Day 2

**Role-based login and session management**
> Extended user model with role and ranking. Login now stores role context in session and cookie. System admins and client users are distinguished correctly. Enables accurate filtering of which companies and databases each user can access.
**Hours**: 3

**Multi-tenant database access control**
> Implemented access rules: system admins see all databases; client users see only databases they are explicitly linked to and that have the Reporting module licensed. Revalidates access on connect. Enforces tenant isolation at the data layer.
**Hours**: 3

**Database selection UX and auto-login**
> Pre-filters the company/database list per user. When a user has only one accessible company and one database, the selection step is skipped and they connect directly. Improves experience for single-database clients.
**Hours**: 2

---

### Day 3

**Action-based permission checks**
> Enforced per-report and per-action permissions. Custom roles are restricted to actions assigned to them; standard roles retain full access. View and Schedule buttons are shown only when allowed. Aligns with the defined action matrix.
**Hours**: 2

**Session expiry and claims fallback**
> When session expires but the auth cookie is still valid, the system now falls back to claims instead of defaulting to a restrictive state. Prevents incorrect access denial during long sessions.
**Hours**: 1

**Connection string security hardening**
> Moved database credentials out of version-controlled config into environment-specific, gitignored files. Prevents credential exposure in source control.
**Hours**: 1

**End-to-end testing and sign-off**
> Ran full test matrix across user types (system admin, single-DB user, multi-company user). Verified login, database selection, report access, and permissions. Confirmed deployment readiness for login and authorization.
**Hours**: 4

---

### Day 4

**Layout persistence — design and implementation**
> Built layout save/restore using existing user-preference storage. Designed for transaction safety and future report types. Users can persist their report parameters and column choices.
**Hours**: 3

**Layout save/load API and controller integration**
> Implemented save, load, and reset endpoints. Report parameters (dates, breakdown, grouping, VAT, column visibility) are stored and restored on next visit. Delivers the requested "save preferences" capability.
**Hours**: 2

**Integration and column visibility binding**
> Wired layout feature into the report flow. Fixed an issue where hidden columns reappeared after regenerating the report. Column visibility now persists correctly across generate cycles.
**Hours**: 3

---

### Day 5

**Column visibility UI**
> Added a settings modal to toggle which columns are shown. Changes apply immediately. Matches the column-chooser pattern used elsewhere in the product.
**Hours**: 2

**Save Layout and Reset Layout**
> Save persists the current setup as default. Reset discards saved preferences and restores defaults. Clear feedback on success. Meets the agreed requirement for layout persistence.
**Hours**: 2

**Clear Filters vs Reset Layout — UX clarification**
> Distinguished "Clear Filters" (clears column filters, keeps saved layout) from "Reset Layout" (discards saved layout entirely). Added clear messaging and tooltips. Reduces confusion between the two actions.
**Hours**: 2

**Alert placement and deduplication**
> Removed duplicate success alerts. Each page now shows a single, contextual message. Cleaner interface and less noise.
**Hours**: 2

---

### Day 6

**Legacy layout pattern review**
> Reviewed how the existing system handles layout and preferences. Ensured our implementation reuses the same storage, is simpler, and remains compatible going forward.
**Hours**: 2

**Integration testing — layout save/restore**
> Tested full flow: save layout, leave page, return, generate report. Verified persistence, column visibility, reset, and isolation between users. Confirms the feature behaves correctly across sessions.
**Hours**: 2

**Documentation updates**
> Updated project documentation with completed features and known issues. Keeps handover material current.
**Hours**: 1

**Code review and edge cases**
> Reviewed transaction handling, lookup logic, and column indexing for grouped data. Addressed issues to reach production-ready quality.
**Hours**: 3

---

### Day 7

**Code cleanup and consistency**
> Standardized labels, tooltips, and naming. Resolved warnings. Applied project conventions across the new code.
**Hours**: 2

**Timesheet and handover documentation**
> Prepared structured task descriptions for time tracking. Documented test scenarios. Supports accurate billing and project accounting.
**Hours**: 2

**Sprint summary and next-phase planning**
> Reviewed Priority 4 (Schedule Enhancement). Identified relative date ranges, Outlook-style recurrence, and license limits as next deliverables.
**Hours**: 2

**Buffer — meetings, reviews, context switching**
> Time for stand-ups, stakeholder questions, feedback, and environment setup.
**Hours**: 2

---

### Day 8

**Final QA and regression pass**
> Re-tested login flows, database selection, layout save/restore, column visibility, Clear Filters, Reset Layout. Confirmed no regressions.
**Hours**: 3

**Knowledge transfer and timesheet structuring**
> Finalized timesheet document for Hubstaff. Ensured descriptions are reusable for future sprints.
**Hours**: 1

**Deployment readiness check**
> Verified config separation for credentials, successful build, and clean state for handover.
**Hours**: 2

**Buffer — documentation and wrap-up**
> Final context review, commit cleanup, branch status. Prep for next phase.
**Hours**: 2

---

## Summary

| Phase | Days | Focus |
|-------|------|-------|
| Analysis & Security | 1–3 | Login, authorization, multi-tenant access, testing |
| Layout Save/Restore | 4–6 | Persistence, UI, UX, integration testing |
| Polish & Handover | 7–8 | Documentation, QA, deployment prep |

**Total**: 8 days × 8 hours = 64 hours

---

## Progress Summary & Next Phase

**What was achieved**

- **Security model aligned** — Login and authorization now match the legacy system’s rules. Role and action checks are enforced. Multi-tenant access control is in place.
- **Layout persistence delivered** — Users can save report parameters and column visibility. Preferences auto-load on return. Clear Filters and Reset Layout are distinct and clearly explained.
- **Tested and stable** — Login flows, database selection, permissions, and layout save/restore have been exercised across user types. No regressions identified.

The system is stable and ready for deployment of this phase.

**Next logical step**

Priority 4: Schedule Enhancement — relative date ranges, Outlook-style recurrence, and license limit enforcement for report scheduling. Performance and scalability (e.g., large result sets, concurrent users) can be revisited in a later iteration once scheduling and storage are in place.

---

*Last updated: Feb 2026*
