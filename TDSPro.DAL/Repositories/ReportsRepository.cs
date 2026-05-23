using Microsoft.Data.Sqlite;
using TDSPro.DAL.Models;

namespace TDSPro.DAL.Repositories
{
    public class ReportsRepository
    {
        // ── Quarter Summary ───────────────────────────────────────────────────
        public List<QuarterSummary> GetQuarterSummary(string fy, int? deductorId = null)
        {
            var list = new List<QuarterSummary>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            var didF = deductorId.HasValue && deductorId.Value > 0 ? " AND deductor_id=@did" : "";
            cmd.CommandText = $@"
                SELECT quarter,
                       COUNT(*)                              AS entries,
                       COALESCE(SUM(amount),0)               AS gross,
                       COALESCE(SUM(tds_amount),0)           AS tds,
                       COALESCE(SUM(surcharge),0)            AS sc,
                       COALESCE(SUM(cess),0)                 AS cess,
                       COALESCE(SUM(interest),0)             AS interest,
                       COALESCE(SUM(total_tds),0)            AS total,
                       SUM(CASE WHEN status='Paid'    THEN 1 ELSE 0 END) AS paid,
                       SUM(CASE WHEN status='Pending' THEN 1 ELSE 0 END) AS pending
                FROM tds_entries
                WHERE financial_year = @fy{didF}
                GROUP BY quarter
                ORDER BY quarter";
            cmd.Parameters.AddWithValue("@fy", fy);
            if (deductorId.HasValue && deductorId.Value > 0) cmd.Parameters.AddWithValue("@did", deductorId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new QuarterSummary
                {
                    Quarter      = r.GetString(r.GetOrdinal("quarter")),
                    Entries      = Convert.ToInt32(r["entries"]),
                    GrossAmount  = Convert.ToDouble(r["gross"]),
                    TdsAmount    = Convert.ToDouble(r["tds"]),
                    Surcharge    = Convert.ToDouble(r["sc"]),
                    Cess         = Convert.ToDouble(r["cess"]),
                    Interest     = Convert.ToDouble(r["interest"]),
                    TotalTds     = Convert.ToDouble(r["total"]),
                    PaidCount    = Convert.ToInt32(r["paid"]),
                    PendingCount = Convert.ToInt32(r["pending"]),
                });
            }
            return list;
        }

        // ── Deductee-wise ──────────────────────────────────────────────────────
        public List<DeducteeReport> GetDeducteeReport(string fy, string? quarter = null, int? deductorId = null)
        {
            var list = new List<DeducteeReport>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            var qc = quarter != null ? " AND e.quarter=@qt" : "";
            var didF = deductorId.HasValue && deductorId.Value > 0 ? " AND e.deductor_id=@did" : "";
            cmd.CommandText = $@"
                SELECT d.name, d.pan, d.deductee_type,
                       GROUP_CONCAT(DISTINCT e.section) AS sections,
                       COUNT(e.id)                       AS entries,
                       COALESCE(SUM(e.amount),0)         AS gross,
                       COALESCE(SUM(e.tds_amount),0)     AS tds,
                       COALESCE(SUM(e.interest),0)       AS interest,
                       COALESCE(SUM(e.total_tds),0)      AS total,
                       SUM(CASE WHEN e.status='Paid'    THEN 1 ELSE 0 END) AS paid,
                       SUM(CASE WHEN e.status='Pending' THEN 1 ELSE 0 END) AS pending
                FROM tds_entries e
                JOIN deductees d ON e.deductee_id = d.id
                WHERE e.financial_year = @fy {qc}{didF}
                GROUP BY e.deductee_id
                ORDER BY total DESC";
            cmd.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) cmd.Parameters.AddWithValue("@qt", quarter);
            if (deductorId.HasValue && deductorId.Value > 0) cmd.Parameters.AddWithValue("@did", deductorId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new DeducteeReport
                {
                    Name         = r.GetString(r.GetOrdinal("name")),
                    Pan          = r.GetString(r.GetOrdinal("pan")),
                    DeducteeType = r.IsDBNull(r.GetOrdinal("deductee_type")) ? "" : r.GetString(r.GetOrdinal("deductee_type")),
                    Section      = r.IsDBNull(r.GetOrdinal("sections")) ? "" : r.GetString(r.GetOrdinal("sections")),
                    Entries      = Convert.ToInt32(r["entries"]),
                    GrossAmount  = Convert.ToDouble(r["gross"]),
                    TdsAmount    = Convert.ToDouble(r["tds"]),
                    Interest     = Convert.ToDouble(r["interest"]),
                    TotalTds     = Convert.ToDouble(r["total"]),
                    PaidCount    = Convert.ToInt32(r["paid"]),
                    PendingCount = Convert.ToInt32(r["pending"]),
                });
            }
            return list;
        }

        // ── Section-wise ───────────────────────────────────────────────────────
        public List<SectionReport> GetSectionReport(string fy, string? quarter = null, int? deductorId = null)
        {
            var list = new List<SectionReport>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            var qc = quarter != null ? " AND e.quarter=@qt" : "";
            var didF = deductorId.HasValue && deductorId.Value > 0 ? " AND e.deductor_id=@did" : "";
            cmd.CommandText = $@"
                SELECT e.section,
                       CASE e.section
                           WHEN '192'   THEN 'Salary payments'
                           WHEN '193'   THEN 'Interest on securities'
                           WHEN '194'   THEN 'Dividends'
                           WHEN '194A'  THEN 'Interest other than securities'
                           WHEN '194B'  THEN 'Winnings from lottery'
                           WHEN '194C'  THEN 'Payment to contractors'
                           WHEN '194D'  THEN 'Insurance commission'
                           WHEN '194H'  THEN 'Commission or brokerage'
                           WHEN '194I'  THEN 'Rent'
                           WHEN '194J'  THEN 'Professional / technical fees'
                           WHEN '194K'  THEN 'Income from mutual funds'
                           WHEN '194LA' THEN 'Compensation on acquisition'
                           WHEN '194Q'  THEN 'Purchase of goods'
                           WHEN '195'   THEN 'NRI payments'
                           WHEN '206AA' THEN 'Higher rate - no PAN'
                           WHEN '206AB' THEN 'Higher rate - non-filer'
                           ELSE 'Other'
                       END AS description,
                       COUNT(*)                     AS entries,
                       COALESCE(SUM(e.amount),0)    AS gross,
                       COALESCE(SUM(e.tds_amount),0)AS tds,
                       COALESCE(SUM(e.surcharge),0) AS sc,
                       COALESCE(SUM(e.cess),0)      AS cess,
                       COALESCE(SUM(e.interest),0)  AS interest,
                       COALESCE(SUM(e.total_tds),0) AS total
                FROM tds_entries e
                WHERE e.financial_year = @fy {qc}{didF}
                GROUP BY e.section
                ORDER BY total DESC";
            cmd.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) cmd.Parameters.AddWithValue("@qt", quarter);
            if (deductorId.HasValue && deductorId.Value > 0) cmd.Parameters.AddWithValue("@did", deductorId.Value);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SectionReport
                {
                    Section     = r.GetString(r.GetOrdinal("section")),
                    Description = r.GetString(r.GetOrdinal("description")),
                    Entries     = Convert.ToInt32(r["entries"]),
                    GrossAmount = Convert.ToDouble(r["gross"]),
                    TdsAmount   = Convert.ToDouble(r["tds"]),
                    Surcharge   = Convert.ToDouble(r["sc"]),
                    Cess        = Convert.ToDouble(r["cess"]),
                    Interest    = Convert.ToDouble(r["interest"]),
                    TotalTds    = Convert.ToDouble(r["total"]),
                });
            }
            return list;
        }

        // ── Challan Reconciliation ─────────────────────────────────────────────
        public ChallanReconciliation GetChallanReconciliation(string fy, string? quarter = null, int? deductorId = null)
        {
            using var conn = Database.GetConnection();
            var qc = quarter != null ? " AND quarter=@qt" : "";
            var didF = deductorId.HasValue && deductorId.Value > 0 ? " AND deductor_id=@did" : "";

            using var c1 = conn.CreateCommand();
            c1.CommandText = $"SELECT COALESCE(SUM(total_tds),0) FROM tds_entries WHERE financial_year=@fy{qc}{didF}";
            c1.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) c1.Parameters.AddWithValue("@qt", quarter);
            if (deductorId.HasValue && deductorId.Value > 0) c1.Parameters.AddWithValue("@did", deductorId.Value);
            var payable = Convert.ToDouble(c1.ExecuteScalar() ?? 0.0);

            using var c2 = conn.CreateCommand();
            c2.CommandText = $"SELECT COALESCE(SUM(tds_amount),0) FROM challans WHERE financial_year=@fy{qc}{didF}";
            c2.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) c2.Parameters.AddWithValue("@qt", quarter);
            if (deductorId.HasValue && deductorId.Value > 0) c2.Parameters.AddWithValue("@did", deductorId.Value);
            var deposited = Convert.ToDouble(c2.ExecuteScalar() ?? 0.0);

            var challanList = new ChallanRepository().GetAll(deductorId: deductorId, fy: fy);
            if (quarter != null)
                challanList = challanList.Where(c => c.Quarter == quarter).ToList();

            return new ChallanReconciliation
            {
                TdsPayable       = payable,
                ChallanDeposited = deposited,
                Challans         = challanList,
            };
        }

        // ── Return data builder ────────────────────────────────────────────────
        public ReturnData BuildReturnData(int deductorId, string fy, string quarter, string formType)
        {
            using var conn = Database.GetConnection();

            // Deductor
            var dr = new DeductorRepository().GetById(deductorId);
            if (dr == null) throw new Exception("Deductor not found.");

            var header = new ReturnHeader
            {
                FormType        = formType,
                FinancialYear   = fy,
                Quarter         = quarter,
                TanOfDeductor   = dr.Tan,
                PanOfDeductor   = dr.Pan,
                DeductorName    = dr.CompanyName,
                DeductorAddress = dr.Address,
                DeductorCity    = dr.City,
                DeductorState   = dr.State,
                DeductorPin     = dr.Pincode,
                ContactPerson   = dr.ContactPerson,
                Phone           = dr.Phone,
                Email           = dr.Email,
                FilingDate      = DateTime.Today,
                ResponsiblePan  = string.IsNullOrEmpty(dr.ResponsiblePan)  ? dr.Pan           : dr.ResponsiblePan,
                ResponsibleName = string.IsNullOrEmpty(dr.ResponsibleName) ? dr.ContactPerson : dr.ResponsibleName,
                Designation     = string.IsNullOrEmpty(dr.Designation)     ? "Director"       : dr.Designation,
                Gstin           = dr.Gstin,
            };

            // Challans for quarter — filtered by form type
            // 24Q=salary only (192/192A/392), 26Q=non-salary non-TCS, 27EQ=TCS (206C family)
            bool challanIs24Q  = formType == "24Q";
            bool challanIs27EQ = formType == "27EQ";
            string chSecFilter = challanIs24Q
                ? "AND (section IN ('192','192A','392','392(1)','392(2)') OR section LIKE '192%' OR section LIKE '392%' OR section IS NULL OR section='')"
                : challanIs27EQ
                ? "AND (section LIKE '206C%' OR section IS NULL OR section='')"
                : "AND (section IS NULL OR section='' OR (section NOT LIKE '192%' AND section NOT LIKE '392%' AND section NOT LIKE '206C%'))";
            using var cc = conn.CreateCommand();
            cc.CommandText = $@"SELECT * FROM challans
                               WHERE deductor_id=@did
                               AND financial_year=@fy
                               AND quarter=@qt
                               {chSecFilter}
                               ORDER BY challan_date";
            cc.Parameters.AddWithValue("@did", deductorId);
            cc.Parameters.AddWithValue("@fy",  fy);
            cc.Parameters.AddWithValue("@qt",  quarter);
            var challans = new List<ReturnChallanDetail>();
            int slNo = 1;
            using (var r = cc.ExecuteReader())
            {
                while (r.Read())
                {
                    challans.Add(new ReturnChallanDetail
                    {
                        SlNo          = slNo++,
                        BsrCode       = r.GetString(r.GetOrdinal("bsr_code")),
                        ChallanDate   = DateTime.Parse(r.GetString(r.GetOrdinal("challan_date"))),
                        ChallanNo     = r.GetString(r.GetOrdinal("challan_no")),
                        TdsDeposited  = Convert.ToDouble(r["tds_amount"]),
                        Surcharge     = Convert.ToDouble(r["surcharge"]),
                        Cess          = Convert.ToDouble(r["cess"]),
                        Interest      = Convert.ToDouble(r["interest"]),
                        LateFee       = Convert.ToDouble(r["late_fee"]),
                        TotalDeposited= Convert.ToDouble(r["total_amount"]),
                        Section       = r.IsDBNull(r.GetOrdinal("section")) ? "" : r.GetString(r.GetOrdinal("section")),
                        Quarter       = quarter,
                    });
                }
            }

            // TDS/TCS entries — filter by form type:
            // 24Q  = salary only (192, 192A, 392)
            // 26Q  = non-salary, non-TCS
            // 27EQ = TCS only (206C family)
            bool is24Q  = formType == "24Q";
            bool is27EQ = formType == "27EQ";
            string sectionFilter = is24Q
                ? "AND (e.section IN ('192','192A','392','392(1)','392(2)') OR e.section LIKE '192%' OR e.section LIKE '392%')"
                : is27EQ
                ? "AND (e.section LIKE '206C%')"
                : "AND (e.section IS NULL OR e.section='' OR (e.section NOT LIKE '192%' AND e.section NOT LIKE '392%' AND e.section NOT LIKE '206C%'))";
            using var ec = conn.CreateCommand();
            ec.CommandText = $@"SELECT e.*, d.name AS dname, d.pan AS dpan,
                                      d.deductee_type AS dtype,
                                      d.is_resident AS dis_resident
                               FROM tds_entries e
                               JOIN deductees d ON e.deductee_id = d.id
                               WHERE e.deductor_id=@did
                               AND e.financial_year=@fy
                               AND e.quarter=@qt
                               {sectionFilter}
                               ORDER BY e.entry_date";
            ec.Parameters.AddWithValue("@did", deductorId);
            ec.Parameters.AddWithValue("@fy",  fy);
            ec.Parameters.AddWithValue("@qt",  quarter);
            var deductees = new List<ReturnDeducteeDetail>();
            slNo = 1;
            using (var r = ec.ExecuteReader())
            {
                while (r.Read())
                {
                    var dtype = r.IsDBNull(r.GetOrdinal("dtype")) ? "Individual" : r.GetString(r.GetOrdinal("dtype"));
                    var entrySection = r.GetString(r.GetOrdinal("section"));
                    // Sec 192 salary TDS: cess is baked into tds_amount by the payroll engine.
                    // Never emit cess as a separate DD field — zero it out to avoid double-counting.
                    bool isSalarySection = entrySection is "192" or "192A" or "192B";
                    deductees.Add(new ReturnDeducteeDetail
                    {
                        SlNo            = slNo++,
                        Pan             = r.GetString(r.GetOrdinal("dpan")),
                        Name            = r.GetString(r.GetOrdinal("dname")),
                        Section         = entrySection,
                        PaymentDate     = r.IsDBNull(r.GetOrdinal("payment_date")) || r.GetString(r.GetOrdinal("payment_date")) == ""
                                          ? DateTime.Parse(r.GetString(r.GetOrdinal("entry_date")))
                                          : DateTime.Parse(r.GetString(r.GetOrdinal("payment_date"))),
                        AmountPaid      = Convert.ToDouble(r["amount"]),
                        TdsDeducted     = Convert.ToDouble(r["tds_amount"]),
                        TdsDeposited    = isSalarySection
                                          ? Convert.ToDouble(r["tds_amount"])   // salary: tds_amount is the correct total (cess already inside)
                                          : Convert.ToDouble(r["total_tds"]),
                        Surcharge       = isSalarySection ? 0 : Convert.ToDouble(r["surcharge"]),
                        Cess            = isSalarySection ? 0 : Convert.ToDouble(r["cess"]),
                        ChallanNo       = r.IsDBNull(r.GetOrdinal("challan_no")) ? "" : r.GetString(r.GetOrdinal("challan_no")),
                        Rate            = Convert.ToDouble(r["rate"]),
                        DeducteeType    = dtype.Equals("Company", StringComparison.OrdinalIgnoreCase) ? "01" : "02",
                        IsResidentIndian= !r.IsDBNull(r.GetOrdinal("dis_resident")) && r.GetInt32(r.GetOrdinal("dis_resident")) == 1,
                        Remarks         = r.IsDBNull(r.GetOrdinal("remarks")) ? "" : r.GetString(r.GetOrdinal("remarks")),
                        LowerDeductionCertNo = TryGetString(r, "lower_cert_no"),
                    });
                }
            }

            // Drop zero-TDS entries — FVU rejects DD records with no tax deducted/deposited
            deductees = deductees.Where(d => d.TdsDeducted > 0 || d.TdsDeposited > 0).ToList();
            for (int i = 0; i < deductees.Count; i++) deductees[i].SlNo = i + 1;

            // Link challan BSR to deductee entries
            // Rule: match by ChallanNo first; fallback to single challan only if there is exactly one
            for (int i = 0; i < deductees.Count; i++)
            {
                if (string.IsNullOrEmpty(deductees[i].BsrCode) && challans.Count > 0)
                {
                    var match = challans.FirstOrDefault(c =>
                        !string.IsNullOrEmpty(deductees[i].ChallanNo) &&
                        c.ChallanNo == deductees[i].ChallanNo);
                    if (match != null)
                        deductees[i].BsrCode = match.BsrCode;
                    else if (challans.Count == 1)
                        deductees[i].BsrCode = challans[0].BsrCode;  // safe only when single challan
                    // else: leave blank — FVU will flag as E020 so CA can correct it
                }
            }

            // Set NoOfDeductees per challan (linked + unlinked assigned to first challan)
            bool unlinkedCounted = false;
            foreach (var ch in challans)
            {
                ch.NoOfDeductees = deductees.Count(d => d.ChallanNo == ch.ChallanNo);
                if (!unlinkedCounted)
                {
                    ch.NoOfDeductees += deductees.Count(d => string.IsNullOrEmpty(d.ChallanNo));
                    unlinkedCounted = true;
                }
            }

            var data = new ReturnData { Header = header, Challans = challans, Deductees = deductees };

            // For 24Q Q4: build Annexure II (SD records) from payroll_runs — annual aggregates per employee
            if (formType == "24Q" && quarter == "Q4")
            {
                data.SalaryDetails = BuildSalaryDetails(conn, deductorId, fy, challans, deductees);
            }

            return data;
        }

        private static List<ReturnSalaryDetail> BuildSalaryDetails(
            Microsoft.Data.Sqlite.SqliteConnection conn, int deductorId, string fy,
            List<ReturnChallanDetail> challans, List<ReturnDeducteeDetail> deductees)
        {
            // Get all distinct employees with EITHER payroll_runs OR monthly_salary_entries for this FY
            using var empCmd = conn.CreateCommand();
            empCmd.CommandText = @"
                SELECT employee_id, pan, name, sex, tax_regime, join_date, leaving_date FROM (
                    SELECT DISTINCT pr.employee_id, e.pan, e.name, e.sex, e.tax_regime,
                           e.join_date, e.leaving_date
                    FROM payroll_runs pr
                    JOIN employees e ON pr.employee_id = e.id
                    WHERE pr.deductor_id=@did AND pr.financial_year=@fy
                    UNION
                    SELECT DISTINCT m.employee_id, e.pan, e.name, e.sex, e.tax_regime,
                           e.join_date, e.leaving_date
                    FROM monthly_salary_entries m
                    JOIN employees e ON m.employee_id = e.id
                    WHERE m.deductor_id=@did AND m.financial_year=@fy
                ) AS u
                ORDER BY name";
            empCmd.Parameters.AddWithValue("@did", deductorId);
            empCmd.Parameters.AddWithValue("@fy",  fy);

            var employees = new List<(int Id, string Pan, string Name, string Gender, string TaxRegime, string DateJoin, string DateLeave)>();
            using (var r = empCmd.ExecuteReader())
            {
                while (r.Read())
                    employees.Add((
                        Convert.ToInt32(r["employee_id"]),
                        r["pan"]?.ToString() ?? "",
                        r["name"]?.ToString() ?? "",
                        r["sex"]?.ToString() ?? "Male",
                        r["tax_regime"]?.ToString() ?? "N",
                        r["join_date"]?.ToString() ?? "",
                        r["leaving_date"]?.ToString() ?? ""
                    ));
            }

            var result = new List<ReturnSalaryDetail>();

            foreach (var (empId, pan, name, gender, regime, joinStr, leaveStr) in employees)
            {
                // Aggregate annual payroll figures from all runs in this FY
                using var agg = conn.CreateCommand();
                agg.CommandText = @"
                    SELECT COALESCE(SUM(gross_salary),0)        AS gross,
                           COALESCE(SUM(hra_exemption),0)       AS hra,
                           COALESCE(SUM(chapter6a_deduction),0) AS ch6a,
                           MAX(taxable_income)                   AS taxable,
                           MAX(annual_tax)                       AS tax,
                           MAX(surcharge)                        AS sur,
                           MAX(cess)                             AS cess,
                           MAX(total_annual_tax)                 AS total_tax,
                           MAX(standard_deduction)               AS std_ded,
                           MAX(rebate87a)                        AS rebate
                    FROM payroll_runs
                    WHERE employee_id=@eid AND deductor_id=@did AND financial_year=@fy";
                agg.Parameters.AddWithValue("@eid", empId);
                agg.Parameters.AddWithValue("@did", deductorId);
                agg.Parameters.AddWithValue("@fy",  fy);

                double gross=0, hra=0, ch6a=0, taxable=0, tax=0, sur=0, cess=0, stdDed=75000, rebate87A=0;
                using (var r = agg.ExecuteReader())
                {
                    if (r.Read())
                    {
                        gross    = Convert.ToDouble(r["gross"]);
                        hra      = Convert.ToDouble(r["hra"]);
                        ch6a     = Convert.ToDouble(r["ch6a"]);
                        taxable  = Convert.ToDouble(r["taxable"]);
                        tax      = Convert.ToDouble(r["tax"]);
                        sur      = Convert.ToDouble(r["sur"]);
                        cess     = Convert.ToDouble(r["cess"]);
                        var sd   = r["std_ded"];
                        stdDed   = (sd == DBNull.Value || sd == null) ? 75000 : Convert.ToDouble(sd);
                        if (stdDed <= 0) stdDed = 75000;
                        var rb   = r["rebate"];
                        rebate87A = (rb == DBNull.Value || rb == null) ? 0 : Convert.ToDouble(rb);
                    }
                }

                // If rebate87A not stored in payroll_runs, compute from TaxRules
                if (rebate87A == 0 && tax > 0)
                {
                    bool isNewRegime = regime.Equals("New", StringComparison.OrdinalIgnoreCase)
                                    || regime.Equals("O", StringComparison.OrdinalIgnoreCase);
                    var rules = TDSPro.Common.TaxRules.GetRules(fy, isNewRegime);
                    rebate87A = taxable <= rules.Rebate87AThreshold
                        ? Math.Min(tax, rules.Rebate87AMaxAmount) : 0;
                }

                // ── Prefer monthly_salary_entries when populated (single source of truth) ──
                using var mse = conn.CreateCommand();
                mse.CommandText = @"
                    SELECT COALESCE(SUM(gross_payment),0)   AS gross_pay,
                           COALESCE(SUM(gross_taxable),0)   AS gross_taxable,
                           COALESCE(SUM(perq_exempted),0)   AS exempted,
                           COALESCE(SUM(tds_deducted),0)    AS tds_total,
                           COUNT(*)                          AS row_count
                    FROM monthly_salary_entries
                    WHERE employee_id=@eid AND deductor_id=@did AND financial_year=@fy";
                mse.Parameters.AddWithValue("@eid", empId);
                mse.Parameters.AddWithValue("@did", deductorId);
                mse.Parameters.AddWithValue("@fy",  fy);
                int mseCount = 0; double mseGross = 0, mseExempted = 0, mseTds = 0, mseTaxable = 0;
                using (var r = mse.ExecuteReader())
                {
                    if (r.Read())
                    {
                        mseCount    = Convert.ToInt32(r["row_count"]);
                        mseGross    = Convert.ToDouble(r["gross_pay"]);
                        mseExempted = Convert.ToDouble(r["exempted"]);
                        mseTaxable  = Convert.ToDouble(r["gross_taxable"]);
                        mseTds      = Convert.ToDouble(r["tds_total"]);
                    }
                }
                if (mseCount > 0)
                {
                    gross    = mseGross;
                    hra      = mseExempted;             // all exempted heads (HRA + LTA + perq exempt + components-paid)
                    // Recompute tax from monthly_salary_entries — mseTaxable = SUM(gross_taxable_salary)
                    // which already has bills-reimbursement exemptions stripped.
                    // Still need to subtract: standard deduction, HRA (in mseExempted), PT (in ch6a from payroll_runs), chap6a.
                    bool isNewRegime = regime.Equals("New", StringComparison.OrdinalIgnoreCase);
                    var rules = TDSPro.Common.TaxRules.GetRules(fy, isNewRegime);
                    stdDed = rules.StandardDeduction;
                    // For new regime: only std deduction applies (no HRA, no chap6a)
                    // For old regime: subtract HRA (mseExempted), PT (annualPt from payroll_runs), chap6a
                    double annualPtFromRuns = 0;
                    using var ptCmd = conn.CreateCommand();
                    ptCmd.CommandText = "SELECT COALESCE(SUM(professional_tax),0) AS pt FROM payroll_runs WHERE employee_id=@eid AND deductor_id=@did AND financial_year=@fy";
                    ptCmd.Parameters.AddWithValue("@eid", empId);
                    ptCmd.Parameters.AddWithValue("@did", deductorId);
                    ptCmd.Parameters.AddWithValue("@fy",  fy);
                    using (var r = ptCmd.ExecuteReader()) { if (r.Read()) annualPtFromRuns = Convert.ToDouble(r["pt"]); }

                    taxable = isNewRegime
                        ? Math.Max(0, mseTaxable - stdDed)
                        : Math.Max(0, mseTaxable - stdDed - hra - annualPtFromRuns - ch6a);
                    tax     = TDSPro.Common.TaxRules.ComputeSlabTax(taxable, rules);
                    var (afterRebate, reb) = TDSPro.Common.TaxRules.Apply87A(tax, taxable, rules);
                    rebate87A = reb;
                    tax = afterRebate;
                    sur  = TDSPro.Common.TaxRules.CalcSurcharge(tax, taxable, rules);
                    cess = Math.Round((tax + sur) * 0.04);
                }

                // TDS current employer = sum of DD TDS for this employee from Annexure I (section 192)
                double tdsFromDD = deductees
                    .Where(d => d.Pan.Equals(pan, StringComparison.OrdinalIgnoreCase))
                    .Sum(d => d.TdsDeducted);
                // Fallback: if no DD records yet, use monthly_salary_entries' TDS sum
                if (tdsFromDD == 0 && mseTds > 0) tdsFromDD = mseTds;

                // Employment period: FY Apr 1 to Mar 31, clamped by joining/leaving dates
                var fyYear = int.Parse(fy.Split('-')[0]);
                var from   = new DateTime(fyYear, 4, 1);
                var to     = new DateTime(fyYear + 1, 3, 31);
                if (!string.IsNullOrEmpty(joinStr)  && DateTime.TryParse(joinStr,  out var j) && j > from) from = j;
                if (!string.IsNullOrEmpty(leaveStr) && DateTime.TryParse(leaveStr, out var l) && l < to)   to   = l;

                // Map gender: M/F/T → G/W/G  (NSDL codes: G=male/unspecified W=female)
                string nsdlGender = gender?.ToUpper() switch { "F" => "W", "FEMALE" => "W", "W" => "W", "S" => "S", _ => "G" };

                // Map tax regime to NSDL code: Old=N, New=O (in FY<2025-26) / New=N (FY>=2025-26 new default)
                string nsdlRegime = regime.Equals("New", StringComparison.OrdinalIgnoreCase) ? "O" : "N";

                // Find the last challan this employee's TDS entries are linked to (for challanNo field)
                string empChallanNo = challans.LastOrDefault()?.ChallanNo ?? "";

                result.Add(new ReturnSalaryDetail
                {
                    Pan              = pan,
                    Name             = name,
                    Gender           = nsdlGender,
                    EmployeeCategory = "A",
                    TaxRegime        = nsdlRegime,
                    EmploymentFrom   = from,
                    EmploymentTo     = to,
                    Salary17_1       = gross,
                    ExemptU10        = hra,
                    ExemptU10Count   = hra > 0 ? 1 : 0,
                    StandardDeduction= stdDed,
                    GrossTotalIncome = Math.Max(0, gross - hra),
                    TaxableIncome    = taxable,
                    TaxPayable       = tax,
                    Surcharge        = sur,
                    Cess             = cess,
                    Rebate87A        = rebate87A,
                    TotalTaxPayable  = Math.Max(0, tax + sur + cess - rebate87A),
                    TdsDeducted      = tdsFromDD,  // sum of DD records for this employee
                    Chapter6ATotal   = ch6a,
                    Chapter6ACount   = ch6a > 0 ? 1 : 0,
                    ChallanNo        = empChallanNo,
                });
            }

            return result;
        }

        private static string TryGetString(Microsoft.Data.Sqlite.SqliteDataReader r, string col)
        {
            try {
                int idx = r.GetOrdinal(col);
                return r.IsDBNull(idx) ? "" : r.GetString(idx);
            } catch { return ""; }
        }
    }
}
