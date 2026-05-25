namespace TDSPro.DAL.Models
{
    // ── Report models ─────────────────────────────────────────────────────────
    public class QuarterSummary
    {
        public string Quarter       { get; set; } = "";
        public int    Entries       { get; set; }
        public double GrossAmount   { get; set; }
        public double TdsAmount     { get; set; }
        public double Surcharge     { get; set; }
        public double Cess          { get; set; }
        public double Interest      { get; set; }
        public double TotalTds      { get; set; }
        public int    PaidCount     { get; set; }
        public int    PendingCount  { get; set; }
    }

    public class DeducteeReport
    {
        public string Name          { get; set; } = "";
        public string Pan           { get; set; } = "";
        public string Section       { get; set; } = "";
        public string DeducteeType  { get; set; } = "";
        public int    Entries       { get; set; }
        public double GrossAmount   { get; set; }
        public double TdsAmount     { get; set; }
        public double Interest      { get; set; }
        public double TotalTds      { get; set; }
        public int    PaidCount     { get; set; }
        public int    PendingCount  { get; set; }
    }

    public class SectionReport
    {
        public string Section       { get; set; } = "";
        public string Description   { get; set; } = "";
        public int    Entries       { get; set; }
        public double GrossAmount   { get; set; }
        public double TdsAmount     { get; set; }
        public double Surcharge     { get; set; }
        public double Cess          { get; set; }
        public double Interest      { get; set; }
        public double TotalTds      { get; set; }
    }

    public class ChallanReconRow
    {
        public string Quarter          { get; set; } = "";
        public string Section          { get; set; } = "";
        public double TdsPayable       { get; set; }
        public double ChallanDeposited { get; set; }
        public double Difference       => ChallanDeposited - TdsPayable;
    }

    public class ChallanReconciliation
    {
        public double TdsPayable      { get; set; }
        public double ChallanDeposited{ get; set; }
        public double Difference      => ChallanDeposited - TdsPayable;
        public bool   IsReconciled    => Math.Abs(Difference) < 1.0;
        public List<Challan> Challans { get; set; } = new();
        public List<ChallanReconRow> SectionBreakdown { get; set; } = new();
    }

    // ── Return / FVU models ────────────────────────────────────────────────────
    public class ReturnHeader
    {
        public string FormType       { get; set; } = "26Q";
        public string FinancialYear  { get; set; } = "2024-25";
        public string Quarter        { get; set; } = "Q1";
        public string TanOfDeductor  { get; set; } = "";
        public string PanOfDeductor  { get; set; } = "";
        public string DeductorName   { get; set; } = "";
        public string DeductorAddress{ get; set; } = "";
        public string DeductorCity   { get; set; } = "";
        public string DeductorState  { get; set; } = "";
        public string DeductorPin    { get; set; } = "";
        public string ContactPerson  { get; set; } = "";
        public string Phone          { get; set; } = "";
        public string Email          { get; set; } = "";
        public string DeductorType   { get; set; } = "C";
        public DateTime FilingDate   { get; set; } = DateTime.Today;
        public string ResponsiblePan { get; set; } = "";
        public string ResponsibleName{ get; set; } = "";
        public string Designation    { get; set; } = "";
        public string Gstin          { get; set; } = "";       // GST Identification Number (15 chars)
        // Correction return fields
        public bool   IsCorrection   { get; set; } = false;   // true = correction, false = original
        public string PreviousPrn    { get; set; } = "";      // PRN from the immediately preceding filing (original or last correction)
        public string OriginalPrn    { get; set; } = "";      // PRN of the very first original filing (C2/C3 need this in BH[8])
        public string CorrectionType { get; set; } = "C1";    // C1 / C2 / C3
    }

    public class ReturnChallanDetail
    {
        public int    SlNo           { get; set; }
        public string BsrCode        { get; set; } = "";
        public DateTime ChallanDate  { get; set; }
        public string ChallanNo      { get; set; } = "";
        public double TdsDeposited   { get; set; }
        public double Surcharge      { get; set; }
        public double Cess           { get; set; }
        public double Interest       { get; set; }
        public double LateFee        { get; set; }
        public double TotalDeposited { get; set; }
        public string Section        { get; set; } = "";
        public int    NoOfDeductees  { get; set; }
        public string Quarter        { get; set; } = "";
    }

    public class ReturnDeducteeDetail
    {
        public int    SlNo              { get; set; }
        public string Pan               { get; set; } = "";
        public string Name              { get; set; } = "";
        public string Section           { get; set; } = "";
        public DateTime PaymentDate     { get; set; }
        public double AmountPaid        { get; set; }
        public double TdsDeducted       { get; set; }
        public double TdsDeposited      { get; set; }
        public double Surcharge         { get; set; }
        public double Cess              { get; set; }
        public string ChallanNo         { get; set; } = "";
        public string BsrCode           { get; set; } = "";
        public string Remarks           { get; set; } = "";
        public string DeducteeType      { get; set; } = "01"; // 01=Company,02=Other
        public bool   IsResidentIndian  { get; set; } = true;
        public double Rate              { get; set; }
        public string LowerDeductionCertNo { get; set; } = "";  // Section 197 cert number (when Remarks="A")
    }

    public class ReturnSalaryDetail
    {
        public string Pan               { get; set; } = "";
        public string Name              { get; set; } = "";
        public DateTime DateOfBirth     { get; set; } = new DateTime(1980, 1, 1);
        public string EmployeeCategory  { get; set; } = "A"; // A/B/C/G/H/O/S/W per NSDL
        public string Gender            { get; set; } = "M"; // M/F/T
        public DateTime EmploymentFrom  { get; set; } = new DateTime(DateTime.Today.Year - 1, 4, 1);
        public DateTime EmploymentTo    { get; set; } = new DateTime(DateTime.Today.Year, 3, 31);
        public double Salary17_1        { get; set; }   // Salary u/s 17(1)
        public double Perquisites17_2   { get; set; }   // Perquisites u/s 17(2)
        public double ProfitSalary17_3  { get; set; }   // Profits in lieu of salary u/s 17(3)
        public double ExemptU10         { get; set; }   // Total exempt u/s 10
        public int    ExemptU10Count    { get; set; } = 0;   // Count of allowances exempt u/s 10
        public double GrossTotalIncome  { get; set; }   // Gross Total Income (after std deduction, before Ch VIA)
        public double Chapter6ATotal    { get; set; }   // Total Chapter VI-A deductions
        public double TaxableIncome     { get; set; }   // Total Taxable Income
        public double TaxPayable        { get; set; }   // Tax on total income
        public double Rebate87A         { get; set; }   // Rebate u/s 87A
        public double Surcharge         { get; set; }
        public double Cess              { get; set; }
        public double Relief89          { get; set; }   // Relief u/s 89
        public double TotalTaxPayable   { get; set; }   // Net tax payable after relief
        public double TdsDeducted       { get; set; }   // TDS by current employer
        public double PrevEmpSalary     { get; set; }   // Previous employer salary
        public double PrevEmpTds        { get; set; }   // Previous employer TDS
        public string TaxRegime         { get; set; } = "N"; // N=New, O=Old
        public int    Chapter6ACount    { get; set; } = 0;   // Count of C6A sub-records
        public double StandardDeduction { get; set; }        // Standard deduction u/s 16(ia)
        public string ChallanNo         { get; set; } = "";  // Links SD to CD/DD
    }

    public class ReturnData
    {
        public ReturnHeader              Header         { get; set; } = new();
        public List<ReturnChallanDetail> Challans       { get; set; } = new();
        public List<ReturnDeducteeDetail>Deductees      { get; set; } = new();
        public List<ReturnSalaryDetail>  SalaryDetails  { get; set; } = new();
        public double TotalTdsDeducted  => Deductees.Sum(d => d.TdsDeducted);
        public double TotalAmountPaid   => Deductees.Sum(d => d.AmountPaid);
        public double TotalGrossSalary  => SalaryDetails.Any()
            ? SalaryDetails.Sum(s => {
                // Must match BuildSD [22]: GTI = Max(0, (salary-stdDed) - Chapter6ATotal)
                // NSDL TaxRegime code: "O"=new regime (75K stdDed), "N"=old regime (50K)
                double totalSal = s.Salary17_1 + s.Perquisites17_2 + s.ProfitSalary17_3;
                bool isNewReg = s.TaxRegime?.Equals("O", StringComparison.OrdinalIgnoreCase) == true;
                double legalStdDed = isNewReg ? 75000 : 50000;
                double stdDed   = s.StandardDeduction > 0 ? Math.Min(s.StandardDeduction, legalStdDed) : legalStdDed;
                double bal      = Math.Max(0, totalSal - stdDed);
                return Math.Max(0, bal - s.Chapter6ATotal);
            })
            : TotalAmountPaid;
        public int    TotalDeductees    => Deductees.Count;
    }
}
