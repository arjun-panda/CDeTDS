namespace TDSPro.DAL.Models
{
    public class DeductionSchedule
    {
        public int    Id                 { get; set; }
        public int    EmployeeId         { get; set; }
        public int    DeductorId         { get; set; }
        public string Type               { get; set; } = "Other"; // Advance, Loan, Other
        public string Description        { get; set; } = "";
        public double TotalAmount        { get; set; }
        public double InstallmentAmt     { get; set; }  // fixed-amount mode (0 = use count mode)
        public int    TotalInstallments  { get; set; }  // fixed-count mode (0 = use amount mode)
        public double RecoveredAmt       { get; set; }
        public string StartFy            { get; set; } = "";
        public int    StartMonth         { get; set; } = 4;
        public bool   IsActive           { get; set; } = true;
        public string CreatedAt          { get; set; } = "";
        public string Notes              { get; set; } = "";

        // ── Computed ────────────────────────────────────────────────────────
        public double Balance => Math.Max(0, TotalAmount - RecoveredAmt);

        public double ThisInstallment
        {
            get
            {
                double raw = InstallmentAmt > 0
                    ? InstallmentAmt
                    : (TotalInstallments > 0 ? Math.Ceiling(TotalAmount / TotalInstallments) : 0);
                return Math.Min(raw, Balance);
            }
        }

        public int MonthsRemaining
        {
            get
            {
                double inst = InstallmentAmt > 0
                    ? InstallmentAmt
                    : (TotalInstallments > 0 ? Math.Ceiling(TotalAmount / TotalInstallments) : 0);
                if (inst <= 0) return 0;
                return (int)Math.Ceiling(Balance / inst);
            }
        }

        // True if this schedule has a due installment for the given FY + month
        public bool IsDue(string fy, int month)
        {
            if (!IsActive || Balance <= 0) return false;
            int cmp = string.Compare(StartFy, fy, StringComparison.Ordinal);
            if (cmp < 0) return true;          // started in an earlier FY
            if (cmp > 0) return false;         // starts in a future FY
            return StartMonth <= month;        // same FY: month must have reached start
        }
    }
}
