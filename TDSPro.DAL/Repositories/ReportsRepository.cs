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
                  AND e.section NOT LIKE '192%' AND e.section NOT LIKE '392%'
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

            // Quarter+Section payable from entries
            using var c3 = conn.CreateCommand();
            c3.CommandText = $"SELECT quarter, section, COALESCE(SUM(total_tds),0) FROM tds_entries WHERE financial_year=@fy{qc}{didF} GROUP BY quarter, section ORDER BY quarter, section";
            c3.Parameters.AddWithValue("@fy", fy);
            if (quarter != null) c3.Parameters.AddWithValue("@qt", quarter);
            if (deductorId.HasValue && deductorId.Value > 0) c3.Parameters.AddWithValue("@did", deductorId.Value);
            var payableMap = new Dictionary<(string, string), double>();
            using (var r3 = c3.ExecuteReader())
                while (r3.Read()) payableMap[(r3.GetString(0), r3.GetString(1))] = r3.GetDouble(2);

            // Quarter+Section deposited from challans
            var depositedMap = challanList
                .GroupBy(c => (c.Quarter ?? "", c.Section ?? ""))
                .ToDictionary(g => g.Key, g => g.Sum(c => c.TdsAmount));

            var allKeys = payableMap.Keys.Union(depositedMap.Keys).OrderBy(k => k.Item1).ThenBy(k => k.Item2).ToList();
            var breakdown = allKeys.Select(k => new ChallanReconRow
            {
                Quarter          = k.Item1,
                Section          = k.Item2,
                TdsPayable       = payableMap.GetValueOrDefault(k),
                ChallanDeposited = depositedMap.GetValueOrDefault(k),
            }).ToList();

            return new ChallanReconciliation
            {
                TdsPayable       = payable,
                ChallanDeposited = deposited,
                Challans         = challanList,
                SectionBreakdown = breakdown,
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
                DeductorType    = dr.DeductorType,
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
                        BsrCode       = r.IsDBNull(r.GetOrdinal("bsr_code"))     ? "" : r.GetString(r.GetOrdinal("bsr_code")),
                        ChallanDate   = r.IsDBNull(r.GetOrdinal("challan_date"))  ? DateTime.Today : DateTime.Parse(r.GetString(r.GetOrdinal("challan_date"))),
                        ChallanNo     = r.IsDBNull(r.GetOrdinal("challan_no"))    ? "" : r.GetString(r.GetOrdinal("challan_no")),
                        TdsDeposited  = r.IsDBNull(r.GetOrdinal("tds_amount"))   ? 0.0 : Convert.ToDouble(r["tds_amount"]),
                        Surcharge     = r.IsDBNull(r.GetOrdinal("surcharge"))    ? 0.0 : Convert.ToDouble(r["surcharge"]),
                        Cess          = r.IsDBNull(r.GetOrdinal("cess"))         ? 0.0 : Convert.ToDouble(r["cess"]),
                        Interest      = r.IsDBNull(r.GetOrdinal("interest"))     ? 0.0 : Convert.ToDouble(r["interest"]),
                        LateFee       = r.IsDBNull(r.GetOrdinal("late_fee"))     ? 0.0 : Convert.ToDouble(r["late_fee"]),
                        TotalDeposited= r.IsDBNull(r.GetOrdinal("total_amount")) ? 0.0 : Convert.ToDouble(r["total_amount"]),
                        Section       = r.IsDBNull(r.GetOrdinal("section"))      ? ""  : r.GetString(r.GetOrdinal("section")),
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
                        AmountPaid      = r.IsDBNull(r.GetOrdinal("amount"))    ? 0.0 : Convert.ToDouble(r["amount"]),
                        TdsDeducted     = r.IsDBNull(r.GetOrdinal("tds_amount")) ? 0.0 : Convert.ToDouble(r["tds_amount"]),
                        TdsDeposited    = isSalarySection
                                          ? (r.IsDBNull(r.GetOrdinal("tds_amount")) ? 0.0 : Convert.ToDouble(r["tds_amount"]))
                                          : (r.IsDBNull(r.GetOrdinal("total_tds"))  ? 0.0 : Convert.ToDouble(r["total_tds"])),
                        Surcharge       = isSalarySection ? 0 : (r.IsDBNull(r.GetOrdinal("surcharge")) ? 0.0 : Convert.ToDouble(r["surcharge"])),
                        Cess            = isSalarySection ? 0 : (r.IsDBNull(r.GetOrdinal("cess"))      ? 0.0 : Convert.ToDouble(r["cess"])),
                        ChallanNo       = r.IsDBNull(r.GetOrdinal("challan_no")) ? "" : r.GetString(r.GetOrdinal("challan_no")),
                        Rate            = r.IsDBNull(r.GetOrdinal("rate"))       ? 0.0 : Convert.ToDouble(r["rate"]),
                        DeducteeType    = dtype.Equals("Company", StringComparison.OrdinalIgnoreCase) ? "01" : "02",
                        IsResidentIndian= !r.IsDBNull(r.GetOrdinal("dis_resident")) && r.GetInt32(r.GetOrdinal("dis_resident")) == 1,
                        Remarks         = r.IsDBNull(r.GetOrdinal("remarks")) ? "" : r.GetString(r.GetOrdinal("remarks")),
                        LowerDeductionCertNo = TryGetString(r, "lower_cert_no"),
                    });
                }
            }

            // Keep nil-TDS entries that have a payment amount — 24Q requires all salary payments.
            // Only drop entries that are truly empty (no payment and no TDS).
            deductees = deductees.Where(d => d.AmountPaid > 0 || d.TdsDeducted > 0 || d.TdsDeposited > 0).ToList();
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

        /// <summary>
        /// Collects employee list + employment periods for 24Q Q4 SD records.
        /// Returns shell ReturnSalaryDetail rows — BLL (ReturnService) fills in
        /// tax computation via SalaryService.ComputeAnnual after this returns.
        /// </summary>
        internal static List<(int EmpId, string Pan, string Name, string Gender, string DateJoin, string DateLeave, string ChallanNo)>
            GetSalaryEmployees(int deductorId, string fy, string lastChallanNo)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT m.employee_id, e.pan, e.name, e.sex, e.join_date, e.leaving_date
                FROM monthly_salary_entries m
                JOIN employees e ON m.employee_id = e.id
                WHERE m.deductor_id=@did AND m.financial_year=@fy
                ORDER BY e.name";
            cmd.Parameters.AddWithValue("@did", deductorId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            var list = new List<(int, string, string, string, string, string, string)>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((
                    Convert.ToInt32(r["employee_id"]),
                    r["pan"]?.ToString() ?? "",
                    r["name"]?.ToString() ?? "",
                    r["sex"]?.ToString() ?? "Male",
                    r["join_date"]?.ToString() ?? "",
                    r["leaving_date"]?.ToString() ?? "",
                    lastChallanNo
                ));
            return list;
        }

        private static List<ReturnSalaryDetail> BuildSalaryDetails(
            Microsoft.Data.Sqlite.SqliteConnection conn, int deductorId, string fy,
            List<ReturnChallanDetail> challans, List<ReturnDeducteeDetail> deductees)
        {
            // This stub is replaced by ReturnService.BuildSalaryDetailsFromComputation in BLL.
            // Returning empty here — BLL overrides SalaryDetails after BuildReturnData returns.
            return new List<ReturnSalaryDetail>();
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
