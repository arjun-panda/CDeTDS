---
name: TDS Pro v2.0 Migration Project
description: Migrating TDS Pro from WinForms to Blazor/MudBlazor. App fully built; currently in bug-fix/polish phase. Last active work was payroll salary slip + FVU generation debugging.
type: project
---
TDS Pro is a production-grade .NET 8 Windows desktop TDS compliance app migrated to Blazor/MudBlazor UI.

**Stack:** WPF + BlazorWebView (.NET 8), MudBlazor 7.x, SQLite, DI via Microsoft.Extensions.DependencyInjection

**How to apply:** UI edits go in TDSPro.App only. Never touch TDSPro.BLL, TDSPro.DAL, TDSPro.Common directly unless fixing a DAL bug.

**Run command:** `dotnet run --project "c:\Users\anand\Desktop\New folder TDS Pro\TDSPro_CS\TDSPro.App"`
(Requires VS/terminal with admin elevation — app requests elevation at launch)
Build only (no installer) unless user explicitly asks to ship/publish.

**Dev shortcut:** Auto-login hardcoded as admin / admin@123. Auto-selects "Icons Projects India Pvt Ltd" / FY 26-27 on login.

**All pages complete:** Login, Dashboard, Deductors, Deductees, TDS Entries, Challans, Reports, Returns, Form16, TDS Rules, Users, Audit Log, Calculator, Settings (5 tabs), Portals, Excel Import/Export, Challan 281 Print, Activate, Payroll (5 tabs: Employees, Tax Declarations, Monthly Run, Salary Slips, Year Summary)

---

## Key model facts
- Deductor: no DeductorType; has `DefaultBsrCode`, `DefaultBankName`, `CpcPassword`, `ItPassword`
- Deductee: no ItrFiled, no Email
- TdsCalculationResult: SectionCode (not Section), CessAmount, SurchargeAmount
- QuarterSummary: Entries, GrossAmount, TdsAmount
- Form16Generator, ExcelEngine, SalarySlipExport, FvuGenerator are STATIC classes — cannot be DI-injected
- DueDate class (not DueDateAlert); ReportsService and ReturnService are BLL classes
- AppConstants.FinancialYears (property), AppConstants.ReturnFormTypes, AppConstants.QuarterCodes
- Challan: has `MinorHeadCode` (default "200")
- PayrollRun: has `Medical`, `Lta` fields (added alongside Special, Other)

---

## Licensing (as of May 2026)
- Trial: no key needed, 25 TDS entries max, 30-day validity, 1 deductor
- Pro: key required (ECDSA signed), unlimited entries, custom limits per contract
- Trial/Pro banner shown in app header and Activate page
- KeyGen tool: separate installer. No "Trial" option in KeyGen dropdown (trial is default without key)
- No "Reset to Trial" button exposed to users

---

## TDS Rules — critical cess logic
- Cess (4%) applies ONLY to Section 192 (salary) and Section 195 (non-resident)
- All other sections (194C, 194J, 194A, etc.): cess = 0
- Three-layer protection in code:
  1. DB startup migration zeros cess on non-192/195 entries (runs every launch)
  2. `AutoCalcTds()` in TdsEntries.razor hard-zeros cess when !cessApplicable
  3. `Save()` has final safety net: `if (section is not "192" or "195") cess = 0`
- `BuiltInTdsRules.CurrentVersion` = `"2026-27-20260507"` — bump to force re-seed

---

## TDS Entries page
- Replaced MudDataGrid with plain HTML table (`table-layout:fixed`, `<colgroup>`) for column alignment
- Single "Payment / Credit Date" field sets both EntryDate and PaymentDate
- MudAutocomplete for deductee (handles thousands of records)
- No Status, Interest, Late Fee fields in entry form
- 4th KPI card = "Linked to Challan"

---

## Challans page
- MinorHeadCode radio (200/400)
- Auto-fills BSR Code + Bank Name from deductor's DefaultBsrCode/DefaultBankName on Add
- Removed Status column and Ack No field
- Challan-entry link table: uncheck to unlink, Save to apply

---

## Payroll module — data flow
Three separate stores that must stay in sync:
1. `salary_structures` → defines employee pay components
2. `payroll_runs` → monthly computed payroll (Basic, HRA, DA, Special, Medical, Lta, Other, PF, ESI, PT, TDS)
3. `monthly_salary_entries` → what Salary Data tab shows/edits

`SyncRunToSalaryEntry(PayrollRun)` syncs run → salary entry on every SaveRun() and PushTo24Q().
`RecalcGross()` must be called on MonthlySalaryEntry before generating salary slips.
PF gate: `PfFixedAmount > 0 || PfApplicable` (not just PfApplicable).
`_otherAllowances` is UI-only state; flushed to `Salary.OtherAllowance` before save.
Salary Slips tab: auto-loads on month/year change (no Load button).

---

## Portals / Returns
- TRACES login URL: `https://traces.tdscpc.gov.in/auth/login/loginScreen`
- IT Portal: `https://eportal.incometax.gov.in/iec/foservices/#/login`
- TIN-NSDL: `https://onlineservices.tin.egov-nsdl.com/etaxnew/tdsnontds.jsp`
- Clipboard: use `System.Windows.Clipboard.SetText()` (WPF), NOT JS execCommand
- TRACES blocks paste in password field → password shown in large visible text (22px monospace green) for manual typing
- Auto-copies TAN to clipboard on "Open Browser"

---

## FVU Generation — ACTIVE BUG (priority task)
File: `TDSPro_CS/TDSPro.DAL/FvuGenerator.cs` — BuildSD() method
Test: `cd C:\fvutest && dotnet script --no-cache RunFvuTest.csx` → check `ver.txt`

**Root cause:** SD record field mapping wrong. FVU expects TDS by current employer at SD field 56 (local71[55]), but BuildSD() outputs it at field 71.

**Current SD output (broken):**
- Field 71 = TDS cur employer (WRONG — should be field 56)
- Fields 56-70 = blank (should have TDS, GTI, tax data)

**Confirmed field positions from bytecode analysis of k_tds_decompiled.txt:**
- local71[55] = field 56 = TDS by current employer ← CRITICAL
- local71[11] = field 12 = Salary u/s 17(1)
- local71[15] = field 16 = Balance after u/s 10
- local71[41] = field 42 = Chapter VI-A count
- SD record must have exactly 88 fields, split on `^`

**FVU errors to fix:**
- T-FV-4020: Gross Total Income not equal
- T-FV-4202: Net Income Tax payable
- T-FV-2092: Batch Total mismatch
- T-FV-4124: Salary Record Type (may auto-resolve once numeric fields correct)
- T-FV-1041: CSI file mandatory (known, blank .csi used for dev — ignore)

**Key files:**
- `C:\fvutest\k_tds_decompiled.txt` — decompiled FVU SD validator (source of truth for field positions)
- `C:\fvutest\ver.txt` — FVU error output
- `C:\fvutest\fvu.log` — FVU internal debug log
- `C:\fvutest\FORM24Q.txt` — generated output file

---

## Known warnings (harmless, do not fix)
- MUD0002: Illegal Attribute 'Title' on MudIconButton/MudButton — all pages have these, cosmetic only
- MUD0002: Illegal Attribute 'Dense' on MudDatePicker in TdsEntries.razor
- CS0414: _launchShowPass assigned but never used in Returns.razor
