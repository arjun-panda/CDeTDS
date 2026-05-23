# TDS Pro v3.0 — Production TDS Compliance Desktop Software

**Income-tax Act 2025 | IT Rules 2026 | NSDL FVU v9.0 | Effective 1 April 2026**

---

## Quick Start

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 / 11 (x64)

### Run from Source
```bat
1. Extract TDSPro_PRODUCTION_FINAL.zip
2. Double-click  run.bat
3. Login:  admin  /  admin@123
```

### Build Single .exe (for distribution)
```bat
Double-click  publish_exe.bat
→  publish\TDSPro.exe   (~70 MB, self-contained, no .NET needed on client)
```

---

## Default Login
| Username | Password   | Role        |
|----------|------------|-------------|
| admin    | admin@123  | Super Admin |

Change in **Settings → General → Change Password**

---

## Architecture

```
TDSPro_CS/
├── TDSPro.Common/          AppConstants  Validators  (no hardcoded rates)
├── TDSPro.DAL/             Database layer
│   ├── Database.cs           SQLite schema + 38 TDS rules seeded
│   ├── TdsRulesEngine.cs     Dynamic rules engine (section/date/type aware)
│   ├── FvuGenerator.cs       NSDL FVU FH/BH/CD/DD/BC/FC pipe-delimited
│   ├── FvuUtilityRunner.cs   Java process launcher + error HTML parser
│   ├── ExcelEngine.cs        ClosedXML import/export + Tally format
│   ├── PdfExport.cs          HTML/PDF reports (no third-party library)
│   ├── FolderManager.cs      Auto folder structure + FY/Quarter detection
│   ├── DueDateService.cs     Filing deadlines + overdue alerts
│   ├── AesEncryption.cs      AES-256 credential storage
│   └── PanAutoComplete.cs    Live PAN search engine
├── TDSPro.BLL/             Business logic services
└── TDSPro.UI/              WinForms — 14 screens
    ├── Forms/
    │   ├── LoginForm.cs          SHA-256 auth + 5-attempt lockout
    │   ├── MainForm.cs           10-item sidebar nav + keyboard shortcuts
    │   ├── SplashForm.cs         Animated startup screen
    │   ├── ReturnForm.cs         3-step Return Wizard + FVU utility tab
    │   ├── SettingsForm.cs       7 tabs: General/Folders/DueDates/FVU/Backup/Shortcuts/About
    │   ├── PortalForm.cs         TRACES/IT Portal/Pay TDS + AES credentials
    │   ├── ChallanPrintForm.cs   GDI+ Challan 281 print
    │   ├── UserManagementForm.cs CRUD users + roles
    │   ├── AuditLogForm.cs       Audit trail viewer + CSV export
    │   └── InterestCalculatorForm.cs  234A interest + 234E late fee
    └── Controls/
        ├── DashboardControl.cs   KPI cards + bar chart + alert banners
        ├── DeductorControl.cs    Deductor CRUD + TAN/PAN validation
        ├── Controls.cs           Deductee/TDS Entry/Challan controls
        ├── ReportsControl.cs     4-tab reports + Excel/CSV/PDF export
        ├── TdsRulesControl.cs    Rules admin + live TDS calculator
        ├── PanSearchBox.cs       Live PAN autocomplete dropdown
        └── ReturnLaunchControl.cs  Return module overview
```

---

## Complete Feature List

### Core Compliance
- **Dynamic TDS Rules Engine** — all rates in `tds_rules` DB table, zero hardcoded
- 38 rules seeded for Income-tax Act 2025 (effective 1 April 2026)
- Sections: 192, 192A, 193, 194A–S, 194BA, 194O, 194Q, 195, 206AA, 206AB
- Auto-applies **206AA** (no PAN → 20%) and **206AB** (no ITR → higher rate)
- Edit any rate in **TDS Rules 2026** screen — instant effect, no code change

### FVU / Returns
- Protean RPU 5.6+ / FVU 9.4 format: **FH ^ BH ^ CD ^ DD ^ SD ^ S16 ^ C6A** caret-delimited records
- Amounts in rupees with `.00` suffix (no paise), dates in ddMMyyyy (no separators)
- 24Q (salary) and 26Q (non-salary) generators
- FVU validation: PAN, TAN, BSR, challan reconciliation
- **Java FVU utility runner**: async `Process.Start`, live terminal log, error HTML parser
- FVU config (java path, jar path, output dir) in Settings → FVU Settings

### Excel Integration (ClosedXML — MIT, free)
- **Import**: TDS entries + deductees, rate validated against rules engine
- **Export**: Entries, Challans, Deductee Master, Tally journal format
- **Reports**: Each of 4 report tabs exports formatted .xlsx (blue header, alternating rows, totals)
- Import error report with row-by-row validation

### PAN Auto-Master
- Live dropdown autocomplete — type PAN or name
- Auto-fills: Name, Section, Rate, Deductee Type, Lower Cert fields
- PAN 4th-char entity decode: P=Individual, C=Company, F=Firm, H=HUF etc.
- Lower cert expiry warning (red/amber/green in status bar)

### Reports (4 tabs)
| Tab | Contents |
|-----|---------|
| Quarter Summary | KPI cards + per-quarter breakdown + totals |
| Deductee-wise | PAN, type, entries, TDS, interest, pending count |
| Section-wise | Section breakup with gross/TDS/cess/interest |
| Challan Reconciliation | Payable vs deposited, green/amber reconciliation banner |
Each tab: **Export Excel** (.xlsx) + **Export CSV** + **PDF/HTML** + **Print**

### Portal Integration
- One-click: TRACES, IT Portal, Pay TDS (NSDL), Download FVU, Download RPU
- AES-256 encrypted credentials (machine-specific key, never plain text)
- Username copied to clipboard — **never bypasses CAPTCHA or OTP**
- Folder quick-access: current quarter, FVU, Reports, Challans, Backup, Base

### Folder Management
Auto-creates on startup:
```
Documents\TDSPro\
  {FY}\  Q1\  Returns\  FVU\  Reports\  Challans\  Justification\  Conso\
         Q2\  ...  Q3\  ...  Q4\
  Backup\    (auto-backup daily, keeps last 30)
  Temp\
```
- **Auto FY detection**: April–March cycle
- **Auto quarter detection**: Q1=Apr-Jun, Q2=Jul-Sep, Q3=Oct-Dec, Q4=Jan-Mar
- **Auto file naming**: `26Q_DELA12345A_202526_Q1.txt`

### Due Date Reminder System
- All TDS return deadlines stored in `due_dates` table
- Seeded for FY 2024-25 and 2025-26 (24Q + 26Q per quarter)
- **Startup popup** for overdue or due-within-30-days filings
- Dashboard alert banners
- Mark as Filed in **Settings → Due Dates**

### Calculators & Tools
- **Interest Calculator** (Ctrl+I): 234A late deposit interest + 234E late filing fee
- Auto-fill TDS entry due date from entry date (7th of next month rule)
- 194C aggregate threshold check on dashboard

### Security
- SHA-256 hashed passwords (never plain text)
- 5-attempt login lockout
- AES-256 for portal credentials (machine-specific key)
- Role-based access: Super Admin / Admin / Operator / View Only
- Audit log for all actions (Ctrl+L to view)

### Production Hardening
- **Global exception handler**: unhandled errors → crash.log, user prompt to continue
- **Splash screen** with animated progress bar
- **DB maintenance**: VACUUM + ANALYZE + integrity_check (background on startup)
- **Daily auto-backup** on startup, keeps last 30
- **0 errors, 0 warnings** clean build

---

## Keyboard Shortcuts

| Shortcut | Action |
|----------|--------|
| Alt+1..9 | Navigate Dashboard → TDS Rules |
| F5 | Refresh current screen |
| F1 | Settings / About |
| Ctrl+S | Save record |
| Ctrl+N | New record |
| Ctrl+E | Export |
| Ctrl+L | Open Audit Log |
| Ctrl+I | Interest & Late Fee Calculator |
| Enter | Move to next field |
| Escape | Clear form |

---

## FVU File Format (Protean RPU 5.6+ / FVU 9.4)
```
FH|T|26Q|9.0|1|1
BH|DELA12345A|202627|26Q|1|AAAPL1234C|C|12/04/2026|R|3|8|1248000|15000000
DE|DELA12345A|AAAPL1234C|ABC PRIVATE LIMITED|14 CONNAUGHT PLACE|110001|...
CD|1|0001234|15/04/2026|00123|4500000|0|180000|0|0|4680000|2|194C|0
DD|1|RAJKU1234A|RAJ KUMAR AND SONS|02|194C|12/04/2026|15000000|150000|150000|...
BC|3|8|1248000|15000000|0|248000|0|0|1496000
FC|1|3|8|1248000|15000000
```

---

## Database (SQLite — local, no internet required)

| Table | Purpose |
|-------|---------|
| deductors | Company TAN/PAN master |
| deductees | PAN-unique deductee master |
| tds_rules | Dynamic rates engine (38 rules) |
| tds_entries | All TDS transactions |
| challans | Challan 281 records |
| fvu_format_config | FVU/portal config + settings |
| due_dates | Filing deadline tracking |
| users | Login + roles |
| audit_log | All actions logged |

---

## Libraries Used

| Library | Version | License | Purpose |
|---------|---------|---------|---------|
| ClosedXML | 0.102.2 | MIT | Excel import/export |
| Microsoft.Data.Sqlite | 8.0.0 | MIT | SQLite database |
| .NET 8 | 8.0 | MIT | Framework |

No EPPlus. No iTextSharp. No Telerik. Fully open-source dependencies.

---

## Build Info
- **Files**: 41 .cs source files
- **Lines**: ~12,000 lines of C#
- **Build**: `dotnet build` — 0 errors, 0 warnings
- **Version**: 3.0.0
