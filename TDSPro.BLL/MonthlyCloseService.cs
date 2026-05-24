using TDSPro.DAL;
using TDSPro.DAL.Models;

namespace TDSPro.BLL
{
    /// <summary>
    /// Monthly Close — the single workflow for one company's payroll close.
    /// Seeds draft rows for every active employee, allows per-row adjustments
    /// (days worked, LOP, bonus, arrears), computes TDS, then locks + auto-creates
    /// Section 192 TDS entries.
    /// </summary>
    public class MonthlyCloseService
    {
        private readonly PayrollRepository _empRepo = new();
        private readonly SalaryRepository  _salRepo = new();
        private readonly SalaryService     _salSvc  = new();

        /// <summary>
        /// Build the close grid: one row per active employee for (deductor, fy, month).
        /// Existing entries are returned as-is; missing employees get a fresh Draft row
        /// seeded from their salary structure.
        /// </summary>
        public List<MonthlyCloseRow> BuildGrid(int deductorId, string fy, int month)
        {
            var rows = new List<MonthlyCloseRow>();
            int fyStart = SalaryService.FyStartYear(fy);
            int year    = month >= 4 ? fyStart : fyStart + 1;
            int wd      = DateTime.DaysInMonth(year, month);
            var monthStart = new DateTime(year, month, 1);
            var monthEnd   = new DateTime(year, month, wd);

            var employees = _empRepo.GetAllEmployees(deductorId)
                .Where(e => e.IsActive)
                .Where(e =>
                {
                    if (!string.IsNullOrEmpty(e.JoinDate) && DateTime.TryParse(e.JoinDate, out var jd) && jd > monthEnd)
                        return false;
                    if (!string.IsNullOrEmpty(e.LeavingDate) && DateTime.TryParse(e.LeavingDate, out var ld) && ld < monthStart)
                        return false;
                    return true;
                })
                .OrderBy(e => e.Name)
                .ToList();

            foreach (var emp in employees)
            {
                var existing = _salRepo.Get(emp.Id, fy, month);
                if (existing != null)
                {
                    rows.Add(MonthlyCloseRow.FromEntry(existing, emp));
                    continue;
                }

                // Seed Draft from salary structure
                var ss = emp.Salary ?? new SalaryStructure();
                // Pro-rate days: mid-month joiner = days from join to month end
                int daysWorked = wd;
                if (!string.IsNullOrEmpty(emp.JoinDate) && DateTime.TryParse(emp.JoinDate, out var jdate)
                    && jdate.Month == month && jdate.Year == year)
                {
                    daysWorked = wd - jdate.Day + 1;
                }
                if (!string.IsNullOrEmpty(emp.LeavingDate) && DateTime.TryParse(emp.LeavingDate, out var ldate)
                    && ldate.Month == month && ldate.Year == year)
                {
                    // If also joined this month, compute days from join to leave (inclusive).
                    // Otherwise cap at the leaving day.
                    DateTime jd2 = default;
                    bool joinedSameMonth = !string.IsNullOrEmpty(emp.JoinDate)
                        && DateTime.TryParse(emp.JoinDate, out jd2)
                        && jd2.Month == month && jd2.Year == year;
                    daysWorked = joinedSameMonth
                        ? ldate.Day - jd2.Day + 1
                        : Math.Min(daysWorked, ldate.Day);
                }

                rows.Add(new MonthlyCloseRow
                {
                    EmployeeId    = emp.Id,
                    EmployeeName  = emp.Name,
                    EmployeeCode  = emp.EmployeeCode,
                    Pan           = emp.Pan,
                    Month         = month,
                    Year          = year,
                    FinancialYear = fy,
                    WorkingDays   = wd,
                    DaysWorked    = daysWorked,
                    LopDays       = Math.Max(0, 30 - daysWorked),
                    Basic         = ss.Basic,
                    Hra           = ss.Hra,
                    Da            = ss.Da,
                    Special       = ss.SpecialAllowance,
                    Lta           = ss.Lta,
                    Other         = ss.ComponentsReceived(),   // sum of Custom Components
                    Bonus         = 0,
                    Arrears       = 0,
                    Status        = daysWorked == 0 ? "Skip" : "Draft",
                });
            }
            return rows;
        }

        /// <summary>
        /// Compute TDS for one row using current adjustments. Does NOT persist —
        /// caller decides when to Save / Approve.
        /// </summary>
        public MonthlyCloseRow Compute(MonthlyCloseRow row, int deductorId)
        {
            if (row.Status == "Skip") { row.Tds = 0; row.Gross = 0; row.NetPay = 0; return row; }
            var emp = _empRepo.GetAllEmployees(deductorId).FirstOrDefault(e => e.Id == row.EmployeeId);
            if (emp == null) return row;

            // Pro-rate fixed earnings using standard 30-day method (Indian payroll convention).
            // WorkingDays = calendar days in the month (shown in UI); denominator is always 30.
            // daysWorked=30 in any month = full salary; daysWorked=15 = 50%. Capped at 1.0.
            const int StdDays = 30;
            double f = row.DaysWorked > 0 ? Math.Min(1.0, (double)row.DaysWorked / StdDays) : 1.0;
            double basic = Math.Round(row.Basic * f);
            double hra   = Math.Round(row.Hra   * f);
            double da    = Math.Round(row.Da    * f);
            double spec  = Math.Round(row.Special * f);
            double lta   = Math.Round(row.Lta   * f);
            double other = Math.Round(row.Other * f);

            row.Gross = basic + hra + da + spec + lta + other + row.Bonus + row.Arrears;

            // Load saved entry to carry over LineItems / PerqExempted into the transient entry
            var saved = _salRepo.Get(emp.Id, row.FinancialYear, row.Month);

            // Build a transient MonthlySalaryEntry just to run the tax engine via SalaryService
            var entry = new MonthlySalaryEntry
            {
                EmployeeId = emp.Id, DeductorId = deductorId, EmployeeName = emp.Name, Pan = emp.Pan,
                Month = row.Month, Year = row.Year, FinancialYear = row.FinancialYear,
                Basic = basic, HRA = hra, DaAmount = da,
                SpecialAllowance = spec, Lta = lta, OtherAllowances = other,
                Bonus = row.Bonus, Arrears = row.Arrears,
                DaysWorked = row.DaysWorked, LopDays = row.LopDays, WorkingDays = row.WorkingDays,
                Status = row.Status,
                PerqExempted    = saved?.PerqExempted ?? 0,
                PerqTotal       = saved?.PerqTotal    ?? 0,
                LeaveEncTotal   = saved?.LeaveEncTotal   ?? 0,
                LeaveEncExempted= saved?.LeaveEncExempted ?? 0,
                LineItems       = saved?.LineItems ?? new(),
            };
            entry.RecalcGross();

            // PF / ESI / PT — auto from salary structure (pro-rated)
            var ss = emp.Salary;
            if (ss != null)
            {
                double pfMo = ss.PfApplicable
                    ? (ss.PfFixedAmount > 0 ? ss.PfFixedAmount : Math.Round(basic * 0.12))
                    : 0;
                entry.PfEmployee = pfMo;
                if (ss.EsiApplicable && entry.GrossPayment <= 21000)
                    entry.EsiEmployee = Math.Round(entry.GrossPayment * 0.0075);
            }
            entry.RecalcGross();

            // Project annual TDS via SalaryService
            var decl = new TaxDeclaration { EmployeeId = emp.Id, FinancialYear = row.FinancialYear };
            var ann  = _salSvc.ComputeMonth(entry, emp, row.FinancialYear, decl);
            entry.TdsDeducted = ann.ThisMonthTds;
            entry.TaxComputed = ann.ThisMonthTds;
            entry.RecalcGross();

            row.Pf      = entry.PfEmployee;
            row.Esi     = entry.EsiEmployee;
            row.Tds     = entry.TdsDeducted;
            row.NetPay  = entry.NetSalary;
            row.Gross   = entry.GrossPayment;
            row.Taxable = entry.GrossTaxableSalary;
            return row;
        }

        /// <summary>Save row as Draft (editable). Re-saving overwrites until Locked.</summary>
        public void SaveDraft(MonthlyCloseRow row, int deductorId)
        {
            var entry = row.ToEntry(deductorId);
            entry.Status   = row.Status == "Skip" ? "Skip" : "Draft";
            entry.IsLocked = false;
            // Preserve existing line items (perqs/reimbursements entered in Salary Data tab)
            if (entry.LineItems.Count == 0)
            {
                var existing = _salRepo.Get(row.EmployeeId, row.FinancialYear, row.Month);
                if (existing != null) entry.LineItems = existing.LineItems;
            }
            _salRepo.Save(entry);
            row.EntryId = entry.Id;
        }

        /// <summary>
        /// Approve + Lock: persists, locks (blocks edits), auto-creates Sec 192 TDS Entry.
        /// </summary>
        public (bool ok, string msg) Approve(MonthlyCloseRow row, int deductorId, string approvedBy = "user")
        {
            if (row.Status == "Skip")
            {
                row.Status   = "Skip";
                var skipEntry = row.ToEntry(deductorId);
                skipEntry.Status = "Skip"; skipEntry.IsLocked = true;
                skipEntry.ApprovedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                skipEntry.ApprovedBy = approvedBy;
                var existingSkip = _salRepo.Get(row.EmployeeId, row.FinancialYear, row.Month);
                if (existingSkip != null) skipEntry.LineItems = existingSkip.LineItems;
                _salRepo.Save(skipEntry);
                return (true, "Skipped.");
            }

            var entry = row.ToEntry(deductorId);
            entry.Status     = "Locked";
            entry.IsLocked   = true;
            entry.ApprovedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            entry.ApprovedBy = approvedBy;
            if (entry.LineItems.Count == 0)
            {
                var existing = _salRepo.Get(row.EmployeeId, row.FinancialYear, row.Month);
                if (existing != null) entry.LineItems = existing.LineItems;
            }
            _salRepo.Save(entry);
            row.EntryId = entry.Id;

            // Auto-create Sec 192 TDS entry — if this fails, roll back the lock so the DB stays consistent.
            if (entry.TdsDeducted > 0)
            {
                var emp = _empRepo.GetAllEmployees(deductorId).FirstOrDefault(e => e.Id == row.EmployeeId);
                if (emp != null)
                {
                    var r = _salSvc.UpsertSec192Entry(entry, emp, deductorId);
                    if (!r.ok)
                    {
                        // Undo the lock — restore to Draft so the user can retry
                        entry.Status   = "Draft";
                        entry.IsLocked = false;
                        entry.ApprovedAt = ""; entry.ApprovedBy = "";
                        _salRepo.Save(entry);
                        return (false, $"Approve failed — Sec 192 entry could not be created: {r.msg}. Row has been unlocked.");
                    }
                }
            }
            return (true, "Locked & Sec 192 entry created.");
        }

        /// <summary>
        /// Unlock a locked row + remove the auto-created Sec 192 TDS entry.
        /// Returns (ok, message). If the TDS entry is already linked to a challan,
        /// unlock is refused — caller must unlink the challan first.
        /// </summary>
        public (bool ok, string msg) Unlock(int employeeId, string fy, int month, int deductorId = 0)
        {
            var entry = _salRepo.Get(employeeId, fy, month);
            if (entry == null) return (false, "Entry not found.");
            // Try to remove the linked Sec 192 entry first — if it's challan-linked, refuse the unlock
            if (deductorId > 0)
            {
                var r = _salSvc.RemoveSec192Entry(employeeId, fy, month, deductorId);
                if (!r.ok) return (false, $"Cannot unlock: {r.msg}");
            }
            entry.IsLocked = false;
            entry.Status   = "Draft";
            entry.ApprovedAt = ""; entry.ApprovedBy = "";
            _salRepo.Save(entry);
            return (true, "Unlocked. Sec 192 entry removed.");
        }
    }

    /// <summary>One row in the Monthly Close grid — flat editable view of MonthlySalaryEntry.</summary>
    public class MonthlyCloseRow
    {
        public int    EntryId       { get; set; }
        public int    EmployeeId    { get; set; }
        public string EmployeeName  { get; set; } = "";
        public string EmployeeCode  { get; set; } = "";
        public string Pan           { get; set; } = "";
        public int    Month         { get; set; }
        public int    Year          { get; set; }
        public string FinancialYear { get; set; } = "";

        public int    WorkingDays   { get; set; } = 30;
        public int    DaysWorked    { get; set; } = 30;
        public int    LopDays       { get; set; }

        public double Basic         { get; set; }
        public double Hra           { get; set; }
        public double Da            { get; set; }
        public double Special       { get; set; }
        public double Lta           { get; set; }
        public double Other         { get; set; }

        public double Bonus         { get; set; }
        public double Arrears       { get; set; }

        public double Pf            { get; set; }
        public double Esi           { get; set; }
        public double Tds           { get; set; }
        public double Gross         { get; set; }
        public double Taxable       { get; set; }
        public double NetPay        { get; set; }

        public string Status        { get; set; } = "Draft";  // Draft / Locked / Skip
        public bool   IsLocked      { get; set; }
        public string ApprovedAt    { get; set; } = "";

        public string FlagReason    { get; set; } = "";       // e.g. "TDS jumped 5x"

        public static MonthlyCloseRow FromEntry(MonthlySalaryEntry e, Employee emp) => new MonthlyCloseRow
        {
            EntryId = e.Id, EmployeeId = e.EmployeeId, EmployeeName = emp.Name,
            EmployeeCode = emp.EmployeeCode, Pan = emp.Pan,
            Month = e.Month, Year = e.Year, FinancialYear = e.FinancialYear,
            WorkingDays = e.WorkingDays > 0 ? e.WorkingDays : 30,
            DaysWorked  = e.DaysWorked  > 0 ? e.DaysWorked  : (e.WorkingDays > 0 ? e.WorkingDays : 30),
            LopDays     = e.LopDays,
            Basic       = e.Basic, Hra = e.HRA, Da = e.DaAmount,
            Special     = e.SpecialAllowance, Lta = e.Lta, Other = e.OtherAllowances,
            Bonus       = e.Bonus, Arrears = e.Arrears,
            Pf          = e.PfEmployee, Esi = e.EsiEmployee,
            Tds         = e.TdsDeducted,
            Gross       = e.GrossPayment, Taxable = e.GrossTaxableSalary, NetPay = e.NetSalary,
            Status      = string.IsNullOrEmpty(e.Status) ? "Draft" : e.Status,
            IsLocked    = e.IsLocked,
            ApprovedAt  = e.ApprovedAt,
        };

        public MonthlySalaryEntry ToEntry(int deductorId) => new MonthlySalaryEntry
        {
            Id = EntryId, EmployeeId = EmployeeId, DeductorId = deductorId,
            EmployeeName = EmployeeName, Pan = Pan,
            Month = Month, Year = Year, FinancialYear = FinancialYear,
            Basic = Basic, HRA = Hra, DaAmount = Da,
            SpecialAllowance = Special, Lta = Lta, OtherAllowances = Other,
            Bonus = Bonus, Arrears = Arrears,
            PfEmployee = Pf, EsiEmployee = Esi, TdsDeducted = Tds, TaxComputed = Tds,
            DaysWorked = DaysWorked, LopDays = LopDays, WorkingDays = WorkingDays,
            Status = Status, IsLocked = IsLocked,
            ApprovedAt = ApprovedAt,
        };
    }
}
