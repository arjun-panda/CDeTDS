# CDeTDS — TDS Compliance Desktop Software

**Income-tax Act 1961 & 2025 | NSDL FVU 9.4 | Windows 10/11 (x64)**

CDeTDS is a Windows desktop application (WPF + Blazor hybrid on .NET 8) for end-to-end
TDS/TCS compliance: deductee management, TDS entries, challans, salary payroll with
Sec 192 computation, Form 16, and 24Q / 26Q / 27EQ / NIL return generation with
bundled NSDL FVU validation.

---

## Build & Run (from source)

### Prerequisites
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Windows 10 / 11 (x64)

```bat
dotnet build CDeTDS.sln
dotnet run --project CDeTDS.App
```

Default login: `admin` / `admin@123` — change it in the **Users** page immediately.

### Run tests
```bat
dotnet test CDeTDS.Tests\CDeTDS.Tests.csproj
```

### Build the installer
```bat
BUILD_INSTALLER.bat
```
Publishes a self-contained win-x64 build (`publish_out\`) and compiles
`CDeTDS_Installer.iss` (Inno Setup) into a single setup .exe. The installer bundles
the .NET runtime, Java 8 JRE, NSDL FVU 9.4 and installs WebView2 automatically —
nothing else is needed on the target machine.

> Version lives in `CDeTDS.App\CDeTDS.App.csproj` (`<Version>`). A build-time guard
> requires the first entry in `CHANGELOG.txt` to match it.

---

## Solution Structure

```
CDeTDS.sln
├── CDeTDS.Common/    Constants, validators, FY-aware TaxRules (slabs, 87A, surcharge)
├── CDeTDS.DAL/       SQLite layer, repositories, FVU generator, Excel/PDF/HTML exports
├── CDeTDS.BLL/       Business services (payroll, salary computation, returns, monthly close)
├── CDeTDS.App/       WPF host + Blazor WebView2 UI (MudBlazor pages)
└── CDeTDS.Tests/     xUnit test suite
```

---

## Key Features

- **Returns**: 24Q (salary, Annexure II in Q4), 26Q, 27EQ, NIL — caret-delimited FVU
  files validated through the bundled Java FVU utility
- **Payroll**: monthly salary entries, Old vs New regime annual computation,
  auto-synced Sec 192 TDS entries, salary slips / computation / statement exports
  (PDF, Excel), Form 16
- **Dynamic TDS rules**: rates and thresholds live in the `tds_rules` table —
  FY-aware, editable in the TDS Rules page, no hardcoded rates
- **Excel import/export**: deductees, entries, challans, employees (ClosedXML),
  plus Tally journal format
- **Compliance helpers**: 206AA/206AB higher-rate handling, Sec 234 interest
  calculator, due-date reminders, challan reconciliation
- **Security**: SHA-256 passwords, login lockout, role-based access, full audit log
- **Reliability**: daily auto-backup (last 30 kept), crash log, DB integrity checks

---

## Data Locations

| What | Where |
|------|-------|
| Database | `%APPDATA%\CDeTDS\cdetds.db` |
| Daily backup (on close) | `%APPDATA%\CDeTDS\Backup\` |
| Auto/manual backups | `Documents\CDeTDS\Backup\` |
| Returns, reports, slips | `Documents\CDeTDS\Companies\{TAN}_{COMPANY}\…` |
| Logs | `%APPDATA%\CDeTDS\Logs\` |

---

## Libraries

| Library | License | Purpose |
|---------|---------|---------|
| Microsoft.Data.Sqlite | MIT | Local database |
| ClosedXML | MIT | Excel import/export |
| QuestPDF | MIT (Community) | PDF reports |
| MudBlazor | MIT | UI components |

---

## Support

- Email: admin@capitaldesk.co.in
- Website: https://capitaldesk.co.in
- Hours: Mon–Sat, 10 AM – 6 PM IST
