# CLAUDE.md — CDeTDS Project Context

## What this project is

CDeTDS is a commercial Indian TDS (Tax Deducted at Source) compliance desktop
application, intended for sale with a license/renewal model. The developer is a
novice programmer and expects complete, ready-to-run code — no partial snippets,
no "fill this in yourself" placeholders.

## Tech stack

- .NET 8 Windows desktop application (WPF host + Blazor WebView2 hybrid)
- UI: MudBlazor components (Razor pages in CDeTDS.App)
- Solution layout: CDeTDS.Common (constants, validators, TaxRules) ·
  CDeTDS.DAL (SQLite, repositories, FVU/report generators) ·
  CDeTDS.BLL (services) · CDeTDS.App (UI) · CDeTDS.Tests (xUnit)
- Database: SQLite via Microsoft.Data.Sqlite at %APPDATA%\CDeTDS\cdetds.db
- Target OS: Windows 10/11 x64
- File output: e-TDS return .txt files (caret `^` delimited, clean ASCII),
  validated externally by Protean's File Validation Utility (FVU, bundled JAR
  invoked via subprocess — see CDeTDS.DAL\FvuUtilityRunner.cs)

## CRITICAL DOMAIN RULES — never violate these

### 1. Two tax regimes coexist

India's Income Tax Act, 1961 was repealed on 31 March 2026 and replaced by the
Income Tax Act, 2025 (effective 1 April 2026). CDeTDS must support BOTH regimes
simultaneously. This is the single most important correctness requirement in
the entire application.

### 2. Regime selection rule (the trigger-date rule)

The governing Act is decided by the EARLIER of (a) date of credit in books, or
(b) date of payment — NOT by the filing date, NOT by the quarter the return is
filed in, NOT by the deposit date.

- Trigger date on or before 31 March 2026 → Income Tax Act, 1961 (old regime)
- Trigger date on or after 1 April 2026 → Income Tax Act, 2025 (new regime)

Examples that MUST work correctly:
- Professional fees credited March 2026, paid April 2026 → OLD Act (1961),
  TDS deducted in March 2026 under section 194J.
- Advance paid March 2026, credited in books April 2026 → OLD Act (1961).
- Invoice credited and paid April 2026 → NEW Act (2025), payment code under
  Section 393.
- TDS deducted Feb/Mar 2026 but deposited late in April/May 2026 → still OLD
  Act codes (deduction predates transition). Reconciliation must accept old
  codes appearing after 1 April 2026 for this reason.

### 3. Old regime references (1961 Act) — for trigger dates up to 31-03-2026

- Section numbers: 192 (salary), 194A, 194C, 194H, 194I, 194J, 194Q, 195, etc.
- Quarterly return forms: 24Q (salary), 26Q (resident non-salary),
  27Q (non-resident), 27EQ (TCS)
- Year referencing: Financial Year (FY) + Assessment Year (AY)
- Q4 FY 2025-26 (Jan–Mar 2026) returns: MUST use old forms + old section codes
  even though they are filed in May/June 2026. Due date was 31 May 2026.
- TDS certificates: Form 16 (salary), Form 16A (non-salary)

### 4. New regime references (2025 Act) — for trigger dates from 01-04-2026

- Parent sections: 392 (salary TDS), 393 (all non-salary TDS, table-driven),
  394 (TCS)
- Old 194-series section numbers NO LONGER EXIST for new transactions. They are
  replaced by 4-digit numeric payment codes (range approx. 1001–1067/1092 —
  verify the exact list against the official Protean data structure document,
  do not trust blog tables) referencing table entries within Sections 392/393/394.
- Quarterly return forms: 138 (salary, replaces 24Q), 140 (resident non-salary,
  replaces 26Q), 144 (non-resident, replaces 27Q), 143 (TCS, replaces 27EQ).
  Form 141 is the consolidated challan-cum-statement replacing 26QB/26QC/26QD/26QE.
- Year referencing: "Tax Year" (TY) replaces FY/AY. Tax Year = Financial Year.
  TY 2026-27 = 1 April 2026 to 31 March 2027. Assessment Year is abolished for
  new-regime transactions.
- TDS certificates: Form 130 (salary, replaces Form 16), Form 131 (replaces 16A).
- Tax audit report: Form 26 replaces Form 3CD; TDS/TCS disclosures in clauses
  49/50/51 require exact counts of unreported TDS transactions — CDeTDS should
  track these.
- First new-format filing: Q1 TY 2026-27, due 31 July 2026.
- Lower-deduction certificates: Section 395 (replaces old 197). Deductee
  annexure includes certificate number u/s 395 and UIN of Form 121.
- Rates and thresholds are largely UNCHANGED from the 1961 Act; the change is
  referencing/structure. Known substantive changes: manpower supply explicitly
  = "work" (contractor TDS applies); MACT interest to natural persons fully
  exempt; section 194LD removed; some TCS rates revised (e.g. overseas tour
  package flat 2%).

### 5. Mixing rules — hard validation constraints

- A single statement file must NEVER mix old-regime and new-regime section/
  payment codes.
- Old form numbers (24Q/26Q/27Q/27EQ) must never be used for TY 2026-27+ data.
- New form numbers (138/140/144/143) must never be used for FY 2025-26 data.
- Using an old section code in a new-regime return triggers FVU/portal
  validation rejection — CDeTDS's own pre-validation must catch this first
  with a friendly error message.

### 6. File generation & FVU

- Output is a flat .txt (ASCII, caret-delimited) with stacked record types:
  FH (file header), BH (batch header), CD (challan detail), DD (deductee
  detail), SD (salary detail — Form 138/24Q Q4 only).
- The authoritative field-level spec is the official data structure document
  published by Protean (protean-tinpan.com → Downloads → e-TDS). NEVER invent
  field positions or rely on memory/blogs for the layout.
- CDeTDS NEVER implements its own FVU. It bundles Protean's FVU .jar, invokes
  it via subprocess, parses the error report, and surfaces the resulting .fvu
  file for upload to the e-filing portal.
- Old-format files validate against the legacy FVU (e.g. 9.x line); new-format
  TY 2026-27 files validate against the new RPU/FVU line (Beta released;
  confirm and pin the final version before release).
- Correction statements: statements cannot be edited after submission. The DB
  must store the token/receipt number of every filed statement plus a full
  record-level snapshot, so correction files (add/update/delete flags) can be
  generated within the 2-year correction window.

### 7. Validation layer (CDeTDS's own, before FVU)

- PAN format: 5 letters + 4 digits + 1 letter (4th char = holder type).
- TAN format: 4 letters + 5 digits + 1 letter.
- Challan totals must reconcile with the sum of deductee rows under them.
- Threshold logic per payment type, including aggregate rules (e.g. contractor:
  single payment > ₹30,000 OR aggregate > ₹1,00,000 in the year).
- Higher rate (20% etc.) when deductee PAN missing/invalid (old 206AA logic;
  verify new-Act equivalent section number).
- Date sanity: deduction date within the statement quarter; deposit due dates
  (7th of next month; 30 April for March deductions by non-government deductors).

## Working preferences

- Deliver complete, runnable files — the developer is a novice and cannot
  stitch fragments together.
- Explain changes in plain language.
- When regulatory details are uncertain (exact payment-code list, final FVU
  field layout), say so explicitly and point to the official Protean/Income Tax
  Department source rather than guessing.
