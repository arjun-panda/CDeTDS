namespace CDeTDS.DAL.Models
{
    public class Employee
    {
        public int     Id              { get; set; }
        public int     DeductorId      { get; set; }
        public bool    IsActive        { get; set; } = true;

        // ── Identity ──────────────────────────────────────────────────────────
        public string  EmployeeCode    { get; set; } = "";
        public string  Name            { get; set; } = "";
        public string  FathersName     { get; set; } = "";
        public string  Sex             { get; set; } = "Male";  // Male / Female / Other
        public string  DateOfBirth     { get; set; } = "";      // dd-MMM-yyyy
        public string  Pan             { get; set; } = "";
        public string  PfNumber        { get; set; } = "";      // PF account number
        public string  WardCircleRange { get; set; } = "";      // TDS ward for filing

        // ── Contact ───────────────────────────────────────────────────────────
        public string  Email           { get; set; } = "";
        public string  Phone           { get; set; } = "";
        public string  StdCode         { get; set; } = "";
        public string  TelephoneNo     { get; set; } = "";

        // ── Residential Address ───────────────────────────────────────────────
        public string  FlatDoorBlockNo         { get; set; } = "";
        public string  PremisesBuildingVillage { get; set; } = "";
        public string  RoadStreetPostOffice    { get; set; } = "";
        public string  AreaLocality            { get; set; } = "";
        public string  TownCityDistrict        { get; set; } = "";
        public string  PinCode                 { get; set; } = "";
        public string  State                   { get; set; } = "";

        // ── Employment ────────────────────────────────────────────────────────
        public string  Designation     { get; set; } = "";
        public string  Department      { get; set; } = "";
        public string  JoinDate        { get; set; } = "";
        public string  LeavingDate     { get; set; } = "";      // empty = still employed

        // ── Bank ──────────────────────────────────────────────────────────────
        public string  BankAccount     { get; set; } = "";
        public string  BankIfsc        { get; set; } = "";

        // ── Extended Identity ─────────────────────────────────────────────────
        public string  AadhaarNumber      { get; set; } = "";
        public string  ResidentialStatus  { get; set; } = "Resident"; // Resident / NRI / Foreign
        public string  MaritalStatus      { get; set; } = "Single";
        public string  BloodGroup         { get; set; } = "";
        public string  EmploymentType     { get; set; } = "Permanent";

        // ── Extended Contact ──────────────────────────────────────────────────
        public string  WorkEmail          { get; set; } = "";
        public string  EmergencyContact   { get; set; } = "";
        public string  EmergencyMobile    { get; set; } = "";

        // ── Extended Statutory ────────────────────────────────────────────────
        public string  Uan                { get; set; } = "";
        public string  EsiIpNumber        { get; set; } = "";

        // ── Extended Bank ─────────────────────────────────────────────────────
        public string  BankName           { get; set; } = "";
        public string  BankBranch         { get; set; } = "";
        public string  BankAccountType    { get; set; } = "Savings";

        // ── Previous Employer (Form 12B) ──────────────────────────────────────
        public string  PrevEmployerName   { get; set; } = "";
        public double  PrevEmployerIncome { get; set; } = 0;
        public double  PrevEmployerTds    { get; set; } = 0;

        // ── Salary / Tax settings ─────────────────────────────────────────────
        public string  TaxRegime       { get; set; } = "New";  // New / Old
        public string  HraCityType     { get; set; } = "Non-Metro"; // Metro = 50% of Basic; Non-Metro = 40%
        public bool    HraMonthlyBasis { get; set; } = true;   // Calculate HRA on monthly basis
        public bool    DaForRetirement { get; set; } = true;   // DA forms part of retirement salary
        public bool    IsDifferentlyAbled { get; set; } = false;

        // ── Salary structure (loaded with employee) ───────────────────────────
        public SalaryStructure? Salary { get; set; }

        public override string ToString() =>
            string.IsNullOrEmpty(EmployeeCode) ? Name : $"{EmployeeCode} — {Name}";
    }

    public class SalaryStructure
    {
        public int    Id              { get; set; }
        public int    EmployeeId     { get; set; }
        public double Basic          { get; set; }
        public double Hra            { get; set; }
        public double Da             { get; set; }
        public double SpecialAllowance   { get; set; }
        public double MedicalAllowance   { get; set; }
        public double Lta                { get; set; }
        public double OtherAllowance     { get; set; }
        public bool   PfApplicable       { get; set; } = true;
        /// <summary>
        /// 0 = auto (12% of Basic).  Any positive value = fixed monthly PF deduction chosen by employee.
        /// Common use: cap at ₹1,800 (12% of ₹15,000 statutory ceiling) when Basic > ₹15,000.
        /// </summary>
        public double PfFixedAmount  { get; set; } = 0;
        public bool   EsiApplicable  { get; set; } = false;
        public string PtState        { get; set; } = ""; // e.g. "Maharashtra"
        public string EffectiveFrom  { get; set; } = "";

        /// <summary>Target monthly CTC. When > 0, Special auto-balances = CTC − all other heads.</summary>
        public double TargetCtc      { get; set; } = 0;
        /// <summary>Include 4.81% Gratuity provision in CTC reconciliation + auto-balance.</summary>
        public bool   IncludeGratuity { get; set; } = true;

        // ── Reimbursements (bill-based, exempt heads — monthly) ──────────────
        public double ReimbTelephone { get; set; } = 0;
        public double ReimbFuel      { get; set; } = 0;
        public double ReimbBooks     { get; set; } = 0;
        public double ReimbMeal      { get; set; } = 0;  // food coupons ₹50/meal × 22 = ₹1,100/mo typical
        public double ReimbUniform   { get; set; } = 0;

        // ── Variable pay (annual, paid lump-sum) ─────────────────────────────
        public double AnnualBonus      { get; set; } = 0; // performance / retention / joining
        public double AnnualIncentive  { get; set; } = 0; // sales / commission target

        // ── Employer contributions (CTC reconciliation only, not in take-home) ─
        public double EmployerInsurance { get; set; } = 0; // monthly group insurance premium
        public double EmployerNps       { get; set; } = 0; // monthly NPS employer share

        // ── Named line items (replaces rigid Reimb*/AnnualBonus etc. as primary source) ──
        // Each component: name, category (allowance/reimbursement/perquisite/variable),
        // monthly received/paid/taxable amounts and an optional rule reference (e.g. "Sec 10(5)" for LTA).
        public List<SalaryComponent> Components { get; set; } = new();

        public double ComponentsReceived(string? category = null) =>
            (category == null ? Components : Components.Where(c => c.Category == category)).Sum(c => c.Received);
        public double ComponentsPaid(string? category = null) =>
            (category == null ? Components : Components.Where(c => c.Category == category)).Sum(c => c.Paid);
        public double ComponentsTaxable(string? category = null) =>
            (category == null ? Components : Components.Where(c => c.Category == category)).Sum(c => c.Taxable);

        // OtherAllowance is a UI mirror of ComponentsReceived (read-only). Lta is a dedicated editable field.
        // To avoid double-counting OtherAllowance, gross uses ComponentsReceived directly (not OtherAllowance).
        public double GrossSalary =>
            Basic + Hra + Da + SpecialAllowance + MedicalAllowance + Lta + ComponentsReceived();

        public double MonthlyReimbursements =>
            ReimbTelephone + ReimbFuel + ReimbBooks + ReimbMeal + ReimbUniform;

        /// <summary>Approx monthly CTC = Gross + Reimb + Variable/12 + Employer PF (12% Basic, capped) + Gratuity (4.81% Basic) + Insurance + Employer NPS.</summary>
        public double MonthlyCtcApprox
        {
            get
            {
                double employerPf = Basic > 15000 ? 1800 : Math.Round(Basic * 0.12);
                double gratuity   = IncludeGratuity ? Math.Round(Basic * 0.0481) : 0;
                return GrossSalary + MonthlyReimbursements
                     + Math.Round((AnnualBonus + AnnualIncentive) / 12.0)
                     + employerPf + gratuity + EmployerInsurance + EmployerNps;
            }
        }
    }

    /// <summary>
    /// A named monthly salary component with Received / Paid (bills) / Taxable amounts.
    /// Categories: "allowance" (e.g. LTA, Conveyance), "reimbursement" (Telephone, Fuel),
    /// "perquisite" (Car, ESOP), "variable" (Bonus, Incentive).
    /// </summary>
    public class SalaryComponent
    {
        public int    Id              { get; set; }
        public int    SalaryStructureId { get; set; }
        public string Category        { get; set; } = "allowance"; // allowance / reimbursement / perquisite / variable
        public int    Ordinal         { get; set; }
        public string Name            { get; set; } = "";
        public double Received        { get; set; } // monthly amount paid in salary
        public double Paid            { get; set; } // monthly amount substantiated with bills/proof
        public double Taxable         { get; set; } // typically Received - Paid (auto, but user-editable)
        public string RuleRef         { get; set; } = ""; // e.g. "Sec 10(5)", "Sec 17(2)"
    }

    public class TaxDeclaration
    {
        public int    Id              { get; set; }
        public int    EmployeeId     { get; set; }
        public string FinancialYear  { get; set; } = "";
        // HRA
        public double RentPaid       { get; set; }
        public string HraCityType    { get; set; } = "Non-Metro"; // Metro / Non-Metro
        // Chapter VI-A (Old regime)
        public double Sec80C         { get; set; }
        public double Sec80D_Self    { get; set; }
        public double Sec80D_Parents { get; set; }
        public double Sec80G         { get; set; }
        public double Sec80CCD_Employee { get; set; } // NPS employee
        public double Sec80CCD_Employer { get; set; } // NPS employer (both regimes)
        public double OtherDeductions      { get; set; }
        public double IncomeOtherSources    { get; set; }   // interest, rent, other income
        // Additional Chapter VI-A
        public double Sec80E                { get; set; }   // education loan interest
        public double Sec80EEA              { get; set; }   // housing loan interest (first home)
        public double Sec80TTA              { get; set; }   // savings interest (non-senior, max ₹10K)
        public double Sec80TTB              { get; set; }   // savings interest (senior, max ₹50K)
        public double Sec80DD               { get; set; }   // differently abled dependent
        public double Sec80U                { get; set; }   // self differently abled
        public double LtaExemption          { get; set; }   // LTA claimed
        // HRA details
        public string LandlordPan           { get; set; } = ""; // legacy single landlord (kept for compat)
        // 80D details
        public bool   IsParentSeniorCitizen { get; set; }   // 80D parent limit: ₹50K if true, ₹25K
        // Multiple landlords (loaded separately)
        [System.Text.Json.Serialization.JsonIgnore]
        public List<LandlordRecord> Landlords { get; set; } = new();
    }

    public class LandlordRecord
    {
        public int    Id           { get; set; }
        public int    EmployeeId   { get; set; }
        public string FinancialYear{ get; set; } = "";
        public string Name         { get; set; } = "";
        public string Pan          { get; set; } = "";
        public double AnnualRent   { get; set; }
        public string CityType     { get; set; } = "Non-Metro"; // Metro / Non-Metro
        public string FromDate     { get; set; } = ""; // dd-MM-yyyy
        public string ToDate       { get; set; } = ""; // dd-MM-yyyy
    }

    public class PayrollRun
    {
        public int    Id              { get; set; }
        public int    EmployeeId     { get; set; }
        public int    DeductorId     { get; set; }
        public int    Month          { get; set; }
        public int    Year           { get; set; }
        public string FinancialYear  { get; set; } = "";

        // Earnings
        public double Basic          { get; set; }
        public double Hra            { get; set; }
        public double Da             { get; set; }
        public double Special        { get; set; }
        public double Medical        { get; set; }
        public double Lta            { get; set; }
        public double Other          { get; set; }
        public double GrossSalary    { get; set; }

        // Deductions
        public double PfEmployee     { get; set; }
        public double EsiEmployee    { get; set; }
        public double ProfessionalTax { get; set; }
        public double TdsDeducted    { get; set; }
        public double OtherDeductions { get; set; }
        public double TotalDeductions => PfEmployee + EsiEmployee + ProfessionalTax + TdsDeducted + OtherDeductions;

        // Tax computation
        public string TaxRegimeUsed  { get; set; } = "New";
        public double HraExemption   { get; set; }
        // 0 = not computed (rows adapted from monthly_salary_entries carry no tax fields).
        // Never default to a real amount — reports must not mistake it for data.
        public double StandardDeduction { get; set; }
        public double Chapter6ADeduction { get; set; }
        public double TaxableIncome  { get; set; }
        public double AnnualTax      { get; set; }
        public double Surcharge      { get; set; }
        public double Cess           { get; set; }
        public double TotalAnnualTax { get; set; }
        public double YtdTds         { get; set; }  // TDS already deducted in earlier months

        // Net pay
        public double NetPay         => GrossSalary - TotalDeductions;
        public string Status         { get; set; } = "Draft"; // Draft / Processed / Paid
        public int?   TdsEntryId     { get; set; }  // FK to tds_entries after 24Q push
        public int    ProRataDays    { get; set; } = 0;   // 0 = full month, >0 = partial
        public int    ProRataTotal   { get; set; } = 0;   // total days in that month

        // For display
        public string EmployeeName   { get; set; } = "";
        public string EmployeeCode   { get; set; } = "";
        public string Pan            { get; set; } = "";
        public string MonthLabel     => new DateTime(Year, Month, 1).ToString("MMMM yyyy");
    }

    public class SalaryComputeResult
    {
        public PayrollRun Run        { get; set; } = new();
        public double TaxOldRegime   { get; set; }
        public double TaxNewRegime   { get; set; }
        public double TaxableOld     { get; set; }
        public double TaxableNew     { get; set; }
        public string RecommendedRegime { get; set; } = "New";
    }

    /// <summary>Full-year view for one employee — keyed by month number (4=Apr … 3=Mar).</summary>
    public class EmployeeYearSummary
    {
        public int    EmployeeId   { get; set; }
        public string EmployeeName { get; set; } = "";
        public string EmployeeCode { get; set; } = "";
        public string Pan          { get; set; } = "";

        // Key = month number (4–12, 1–3).  Null = payroll not yet run for that month.
        public Dictionary<int, PayrollRun> MonthlyRuns { get; set; } = new();

        public double TotalGross => MonthlyRuns.Values.Sum(r => r.GrossSalary);
        public double TotalTds   => MonthlyRuns.Values.Sum(r => r.TdsDeducted);
        public double TotalPf    => MonthlyRuns.Values.Sum(r => r.PfEmployee);
        public double TotalPt    => MonthlyRuns.Values.Sum(r => r.ProfessionalTax);
        public double TotalNet   => MonthlyRuns.Values.Sum(r => r.NetPay);
        public int    MonthsRun  => MonthlyRuns.Count;
    }
}
