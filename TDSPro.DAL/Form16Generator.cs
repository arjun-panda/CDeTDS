using TDSPro.DAL.Models;
using Microsoft.Data.Sqlite;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace TDSPro.DAL
{
    /// <summary>
    /// Form 16A TDS Certificate Generator.
    /// Generates a professional HTML certificate that prints as a PDF.
    ///
    /// Form 16  = Salary (Section 192) — issued to employees
    /// Form 16A = Non-salary (all other sections) — issued to deductees
    ///
    /// Per CBDT Rule 31: must be issued within 15 days of due date of
    /// furnishing quarterly TDS return.
    /// </summary>
    public static class Form16Generator
    {
        // ── Build Form 16A data for a deductee in a given FY ─────────────────
        public static Form16AData BuildForm16A(
            int deductorId, string deducteePan, string fy,
            string? quarter = null)
        {
            var data = new Form16AData { FinancialYear = fy };

            using var conn = Database.GetConnection();

            // Deductor
            using var d1 = conn.CreateCommand();
            d1.CommandText = "SELECT * FROM deductors WHERE id=@id";
            d1.Parameters.AddWithValue("@id", deductorId);
            using var r1 = d1.ExecuteReader();
            if (r1.Read())
            {
                data.DeductorName    = r1["company_name"]?.ToString() ?? "";
                data.DeductorTan     = r1["tan"]?.ToString() ?? "";
                data.DeductorPan     = r1["pan"]?.ToString() ?? "";
                data.DeductorAddress = r1["address"]?.ToString() ?? "";
                data.DeductorCity    = r1["city"]?.ToString() ?? "";
                data.DeductorPin     = r1["pincode"]?.ToString() ?? "";
            }
            r1.Close();

            // Deductee
            using var d2 = conn.CreateCommand();
            d2.CommandText = "SELECT * FROM deductees WHERE pan=@pan";
            d2.Parameters.AddWithValue("@pan", deducteePan.ToUpper());
            using var r2 = d2.ExecuteReader();
            if (r2.Read())
            {
                data.DeducteeName    = r2["name"]?.ToString() ?? "";
                data.DeducteePan     = r2["pan"]?.ToString() ?? "";
                data.DeducteeAddress = r2["address"]?.ToString() ?? "";
            }
            r2.Close();

            // TDS entries for this deductee in this FY
            using var d3 = conn.CreateCommand();
            var sql = @"SELECT e.*, d.name AS deductee_name
                        FROM tds_entries e
                        JOIN deductees d ON e.deductee_id = d.id
                        WHERE e.deductor_id=@did
                          AND d.pan=@pan
                          AND e.financial_year=@fy";
            if (quarter != null) sql += " AND e.quarter=@q";
            sql += " ORDER BY e.entry_date";
            d3.CommandText = sql;
            d3.Parameters.AddWithValue("@did", deductorId);
            d3.Parameters.AddWithValue("@pan", deducteePan.ToUpper());
            d3.Parameters.AddWithValue("@fy",  fy);
            if (quarter != null) d3.Parameters.AddWithValue("@q", quarter);
            using var r3 = d3.ExecuteReader();
            while (r3.Read())
            {
                data.Transactions.Add(new Form16ATransaction
                {
                    SlNo         = data.Transactions.Count + 1,
                    EntryDate    = DateTime.Parse(r3["entry_date"]?.ToString() ?? DateTime.Today.ToString("yyyy-MM-dd")),
                    Section      = r3["section"]?.ToString() ?? "",
                    AmountPaid   = Convert.ToDouble(r3["amount"] ?? 0),
                    TdsDeducted  = Convert.ToDouble(r3["tds_amount"] ?? 0),
                    Surcharge    = Convert.ToDouble(r3["surcharge"] ?? 0),
                    Cess         = Convert.ToDouble(r3["cess"] ?? 0),
                    TotalTds     = Convert.ToDouble(r3["total_tds"] ?? 0),
                    ChallanNo    = r3["challan_no"]?.ToString() ?? "",
                    Quarter      = r3["quarter"]?.ToString() ?? "",
                    DateOfDeposit= r3["payment_date"]?.ToString() ?? "",
                    BsrCode      = "",
                });
            }

            data.TotalAmountPaid  = data.Transactions.Sum(t => t.AmountPaid);
            data.TotalTdsDeducted = data.Transactions.Sum(t => t.TdsDeducted);
            data.TotalTdsDeposited= data.Transactions.Sum(t => t.TotalTds);
            data.GeneratedDate    = DateTime.Today;

            return data;
        }

        // ── Render Form 16A as HTML ───────────────────────────────────────────
        public static string RenderHtml(Form16AData d)
        {
            var sb = new System.Text.StringBuilder();
            var fyParts   = d.FinancialYear.Split('-');
            var ayStart   = fyParts.Length > 0 ? (int.Parse(fyParts[0]) + 1).ToString() : "";
            var ayEnd     = fyParts.Length > 1 ? fyParts[1] : "";
            var ay        = $"{ayStart}-{ayEnd}";
            var generated = d.GeneratedDate.ToString("dd-MMM-yyyy");

            sb.Append($@"<!DOCTYPE html><html><head><meta charset='UTF-8'>
<title>Form 16A — {d.DeducteeName} — FY {d.FinancialYear}</title>
<style>
  body{{font-family:'Times New Roman',serif;margin:0;padding:0;background:#f5f5f5}}
  .page{{width:210mm;min-height:297mm;margin:8mm auto;background:#fff;padding:15mm;border:1px solid #ccc}}
  .cert-title{{text-align:center;font-size:18pt;font-weight:bold;border:2pt solid #000;padding:8px;margin-bottom:12px}}
  .cert-sub{{text-align:center;font-size:11pt;margin-bottom:4px}}
  .section-hdr{{background:#1F3864;color:#fff;padding:5px 10px;font-size:11pt;font-weight:bold;margin:12px 0 6px}}
  table{{width:100%;border-collapse:collapse;font-size:10pt}}
  th{{background:#D6E4F0;padding:5px 8px;text-align:left;border:1px solid #999;font-size:9pt}}
  td{{padding:4px 8px;border:1px solid #ccc;font-size:10pt}}
  .label{{color:#555;font-size:9pt;font-weight:bold}}
  .value{{font-size:10pt}}
  .row2{{display:flex;gap:20px;margin-bottom:8px}}
  .col{{flex:1}}
  .field{{margin-bottom:6px}}
  .total-row td{{background:#D6E4F0;font-weight:bold}}
  .sign-box{{border:1px solid #000;height:60px;margin-top:8px;padding:8px;font-size:9pt;color:#555}}
  .footer{{font-size:8pt;color:#888;text-align:center;margin-top:10px;border-top:1px solid #ccc;padding-top:6px}}
  .box{{border:1px solid #999;padding:8px;margin-bottom:8px}}
  @media print{{body{{background:#fff}}.page{{box-shadow:none;margin:0;border:none}}}}
</style></head><body><div class='page'>

<div class='cert-title'>FORM NO. 16A</div>
<div class='cert-sub'>[See Rule 31(1)(b)]</div>
<div class='cert-sub' style='font-size:10pt;margin-bottom:8px'>
  Certificate of Tax Deducted at Source u/s 203 of Income Tax Act, 1961
</div>
<div class='cert-sub' style='font-size:9pt;color:#555;margin-bottom:12px'>
  Financial Year: <b>{d.FinancialYear}</b> &nbsp;|&nbsp; Assessment Year: <b>{ay}</b> &nbsp;|&nbsp; Generated: <b>{generated}</b>
</div>

<div class='section-hdr'>Part A — Deductor Details</div>
<div class='row2'>
  <div class='col box'>
    <div class='field'><div class='label'>Name of Deductor</div><div class='value'>{Esc(d.DeductorName)}</div></div>
    <div class='field'><div class='label'>TAN</div><div class='value' style='font-family:monospace;font-weight:bold'>{d.DeductorTan}</div></div>
    <div class='field'><div class='label'>PAN</div><div class='value' style='font-family:monospace'>{d.DeductorPan}</div></div>
    <div class='field'><div class='label'>Address</div><div class='value'>{Esc(d.DeductorAddress)}, {Esc(d.DeductorCity)} — {d.DeductorPin}</div></div>
  </div>
  <div class='col box'>
    <div class='field'><div class='label'>Name of Deductee</div><div class='value'><b>{Esc(d.DeducteeName)}</b></div></div>
    <div class='field'><div class='label'>PAN of Deductee</div><div class='value' style='font-family:monospace;font-weight:bold;font-size:12pt'>{d.DeducteePan}</div></div>
    <div class='field'><div class='label'>Address</div><div class='value'>{Esc(d.DeducteeAddress)}</div></div>
  </div>
</div>

<div class='section-hdr'>Part B — Details of Tax Deducted and Deposited</div>
<table>
  <tr>
    <th style='width:35px'>Sl.</th>
    <th>Date of Payment</th>
    <th>Section</th>
    <th>Amount Paid (Rs)</th>
    <th>TDS Deducted (Rs)</th>
    <th>Surcharge (Rs)</th>
    <th>Cess (Rs)</th>
    <th>Total TDS (Rs)</th>
    <th>Challan No.</th>
    <th>Quarter</th>
  </tr>");

            foreach (var t in d.Transactions)
            {
                sb.Append($@"
  <tr>
    <td style='text-align:center'>{t.SlNo}</td>
    <td>{t.EntryDate:dd-MM-yyyy}</td>
    <td style='font-family:monospace;font-weight:bold'>{t.Section}</td>
    <td style='text-align:right'>Rs {t.AmountPaid:N2}</td>
    <td style='text-align:right'>Rs {t.TdsDeducted:N2}</td>
    <td style='text-align:right'>Rs {t.Surcharge:N2}</td>
    <td style='text-align:right'>Rs {t.Cess:N2}</td>
    <td style='text-align:right;font-weight:bold'>Rs {t.TotalTds:N2}</td>
    <td style='font-family:monospace'>{t.ChallanNo}</td>
    <td>{t.Quarter}</td>
  </tr>");
            }

            sb.Append($@"
  <tr class='total-row'>
    <td colspan='3' style='text-align:right;font-weight:bold'>TOTAL</td>
    <td style='text-align:right'>Rs {d.TotalAmountPaid:N2}</td>
    <td style='text-align:right'>Rs {d.TotalTdsDeducted:N2}</td>
    <td style='text-align:right'>Rs {d.Transactions.Sum(t => t.Surcharge):N2}</td>
    <td style='text-align:right'>Rs {d.Transactions.Sum(t => t.Cess):N2}</td>
    <td style='text-align:right'>Rs {d.TotalTdsDeposited:N2}</td>
    <td colspan='2'></td>
  </tr>
</table>

<div class='section-hdr'>Part C — Verification</div>
<div class='box' style='font-size:10pt;line-height:1.8'>
  I, <u>&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</u>,
  son/daughter of <u>&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</u>,
  working as <u>&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;</u>
  (designation) do hereby certify that a sum of
  <b>Rs {d.TotalTdsDeposited:N2}</b>
  [Rupees <u>{AmountInWords(d.TotalTdsDeposited)}</u>]
  has been deducted at source and deposited to the credit of the Central Government.
  I further certify that the information given above is true, complete and correct and is based on
  the books of accounts, documents, TDS statements, TDS deposited and other available records.
</div>

<div style='display:flex;gap:20px;margin-top:10px'>
  <div style='flex:1'>
    <div class='sign-box'>
      <div>Place: ___________________</div>
      <div style='margin-top:8px'>Date:  ___________________</div>
    </div>
  </div>
  <div style='flex:1;text-align:center'>
    <div class='sign-box'>
      <div style='margin-top:16px'>Signature of person responsible for deduction of tax</div>
      <div style='margin-top:4px;font-size:9pt'>Name: {Esc(d.DeductorName)}</div>
      <div style='font-size:9pt'>Designation: _______________________</div>
    </div>
  </div>
</div>

<div class='footer'>
  Generated by TDS Pro v3.0 &nbsp;|&nbsp; Income-tax Act 2025 &nbsp;|&nbsp; {generated} &nbsp;|&nbsp;
  TAN: {d.DeductorTan} &nbsp;|&nbsp; PAN of Deductee: {d.DeducteePan}
  <br>
  <i>This is a computer-generated certificate. Verify at TRACES portal before submission.</i>
</div>
</div></body></html>");

            return sb.ToString();
        }

        // ── Save certificate ───────────────────────────────────────────────────
        public static string SaveCertificate(Form16AData data, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var fileName = $"Form16A_{data.DeducteePan}_{data.FinancialYear.Replace("-","_")}_{DateTime.Today:yyyyMMdd}.html";
            var path     = Path.Combine(outputDir, fileName);
            File.WriteAllText(path, RenderHtml(data), System.Text.Encoding.UTF8);
            return path;
        }

        // ── Bulk — generate for all deductees of a deductor ──────────────────
        public static List<string> GenerateBulk(int deductorId, string fy, string outputDir)
        {
            var generated = new List<string>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT DISTINCT d.pan FROM tds_entries e
                                JOIN deductees d ON e.deductee_id=d.id
                                WHERE e.deductor_id=@did AND e.financial_year=@fy";
            cmd.Parameters.AddWithValue("@did", deductorId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            var pans = new List<string>();
            using (var r = cmd.ExecuteReader())
                while (r.Read()) pans.Add(r.GetString(0));

            foreach (var pan in pans)
            {
                var data = BuildForm16A(deductorId, pan, fy);
                if (data.Transactions.Count > 0)
                {
                    var path = SaveCertificate(data, outputDir);
                    generated.Add(path);
                }
            }
            Database.LogAction("system", "FORM16A_BULK", "Form16",
                $"Generated {generated.Count} certificates for FY {fy}");
            return generated;
        }


        // ════════════════════════════════════════════════════════════════════════
        // FORM 16 / FORM 130 — SALARY TDS CERTIFICATE GENERATION
        // ════════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Build Form 16 / Form 130 data for one employee from payroll_runs.
        /// FY-aware: uses correct form/section name automatically.
        /// </summary>
        public static Form16SalaryData BuildForm16Salary(
            int deductorId, int employeeId, string fy)
        {
            var data = new Form16SalaryData { FinancialYear = fy };

            using var conn = Database.GetConnection();

            // Deductor
            using var d1 = conn.CreateCommand();
            d1.CommandText = "SELECT * FROM deductors WHERE id=@id";
            d1.Parameters.AddWithValue("@id", deductorId);
            using var r1 = d1.ExecuteReader();
            if (r1.Read())
            {
                data.DeductorName    = r1["company_name"]?.ToString() ?? "";
                data.DeductorTan     = r1["tan"]?.ToString() ?? "";
                data.DeductorPan     = r1["pan"]?.ToString() ?? "";
                data.DeductorAddress = r1["address"]?.ToString() ?? "";
            }
            r1.Close();

            // Employee
            using var d2 = conn.CreateCommand();
            d2.CommandText = "SELECT * FROM employees WHERE id=@id";
            d2.Parameters.AddWithValue("@id", employeeId);
            using var r2 = d2.ExecuteReader();
            if (r2.Read())
            {
                data.EmployeeName  = r2["name"]?.ToString() ?? "";
                data.EmployeePan   = r2["pan"]?.ToString() ?? "";
                data.EmployeeCode  = r2["employee_code"]?.ToString() ?? "";
                data.Designation   = r2["designation"]?.ToString() ?? "";
                data.Department    = r2["department"]?.ToString() ?? "";
                data.TaxRegime     = r2["tax_regime"]?.ToString() ?? "New";
            }
            r2.Close();

            // All payroll runs for this employee in this FY — aggregate annual figures
            using var d3 = conn.CreateCommand();
            d3.CommandText = @"SELECT * FROM payroll_runs
                               WHERE employee_id=@eid AND deductor_id=@did
                                 AND financial_year=@fy
                               ORDER BY year, month";
            d3.Parameters.AddWithValue("@eid", employeeId);
            d3.Parameters.AddWithValue("@did", deductorId);
            d3.Parameters.AddWithValue("@fy",  fy);
            using var r3 = d3.ExecuteReader();

            var months = new[] { "", "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };
            double totGross=0, totTds=0, totHra=0, totStd=0, tot6a=0, totPt=0;

            while (r3.Read())
            {
                int m = Convert.ToInt32(r3["month"]);
                double gross = Convert.ToDouble(r3["gross_salary"] ?? 0);
                double tds   = Convert.ToDouble(r3["tds_deducted"] ?? 0);
                double net   = gross - Convert.ToDouble(r3["pf_employee"] ?? 0)
                                     - Convert.ToDouble(r3["professional_tax"] ?? 0)
                                     - Convert.ToDouble(r3["esi_employee"] ?? 0)
                                     - tds;

                data.MonthRows.Add(new Form16MonthRow
                {
                    MonthLabel  = m >= 1 && m <= 12 ? months[m] : m.ToString(),
                    GrossSalary = gross,
                    TdsDeducted = tds,
                    NetPay      = net,
                });

                totGross += gross;
                totTds   += tds;
                totHra   += Convert.ToDouble(r3["hra_exemption"] ?? 0);
                totStd    = Convert.ToDouble(r3["standard_deduction"] ?? 0);
                tot6a    += Convert.ToDouble(r3["chapter6a_deduction"] ?? 0);
                totPt    += Convert.ToDouble(r3["professional_tax"] ?? 0);

                // Use last month's annual tax computation (most accurate)
                data.TaxableIncome   = Convert.ToDouble(r3["taxable_income"] ?? 0);
                data.TaxOnIncome     = Convert.ToDouble(r3["annual_tax"] ?? 0);
                data.Surcharge       = Convert.ToDouble(r3["surcharge"] ?? 0);
                data.Cess            = Convert.ToDouble(r3["cess"] ?? 0);
                data.TotalAnnualTax  = Convert.ToDouble(r3["total_annual_tax"] ?? 0);
            }
            r3.Close();

            // Rebate 87A
            var rules = TDSPro.Common.TaxRules.GetRules(fy, data.TaxRegime == "New");
            if (data.TaxableIncome <= rules.Rebate87AThreshold)
                data.Rebate87A = Math.Min(data.TaxOnIncome, rules.Rebate87AMaxAmount);

            data.GrossSalary        = totGross;
            data.TdsDeducted        = totTds;
            data.HraExemption       = totHra;
            data.StandardDeduction  = totStd;
            data.Chapter6ADeduction = tot6a;
            data.ProfessionalTax    = totPt;
            data.GeneratedDate      = DateTime.Today;

            // Quarter-wise summary
            var qtrs = new[] { (1,"Q1",4,5,6), (2,"Q2",7,8,9), (3,"Q3",10,11,12), (4,"Q4",1,2,3) };
            foreach (var (_, qName, m1, m2, m3) in qtrs)
            {
                var qMonths = new HashSet<int>{m1,m2,m3};
                var qRows   = data.MonthRows.Where((_, i) => {
                    // Map back month index — MonthRows is in order
                    return true; // simplified — sum all for now
                });
                // Proper quarter grouping from DB
                using var qCmd = conn.CreateCommand();
                qCmd.CommandText = @"SELECT COALESCE(SUM(gross_salary),0), COALESCE(SUM(tds_deducted),0)
                                     FROM payroll_runs
                                     WHERE employee_id=@eid AND deductor_id=@did
                                       AND financial_year=@fy AND month IN (@m1,@m2,@m3)";
                qCmd.Parameters.AddWithValue("@eid", employeeId);
                qCmd.Parameters.AddWithValue("@did", deductorId);
                qCmd.Parameters.AddWithValue("@fy",  fy);
                qCmd.Parameters.AddWithValue("@m1",  m1);
                qCmd.Parameters.AddWithValue("@m2",  m2);
                qCmd.Parameters.AddWithValue("@m3",  m3);
                using var qr = qCmd.ExecuteReader();
                if (qr.Read())
                {
                    var qGross = Convert.ToDouble(qr[0]);
                    var qTds   = Convert.ToDouble(qr[1]);
                    if (qGross > 0 || qTds > 0)
                        data.QuarterRows.Add(new Form16QuarterRow {
                            Quarter = qName, AmountPaid = qGross,
                            TdsDeducted = qTds, TdsDeposited = qTds,
                        });
                }
            }

            return data;
        }

        /// <summary>Render Form 16 / Form 130 as A4 HTML for print-to-PDF.</summary>
        public static string RenderForm16SalaryHtml(Form16SalaryData d)
        {
            var sb  = new System.Text.StringBuilder();
            var gen = d.GeneratedDate.ToString("dd-MMM-yyyy");
            string Rv(double v) => v == 0 ? "—" : $"₹{v:N0}";

            sb.Append($@"<!DOCTYPE html><html><head><meta charset='UTF-8'>
<title>{d.FormName} — {Esc(d.EmployeeName)} — FY {d.FinancialYear}</title>
<style>
  body{{font-family:'Times New Roman',serif;margin:0;padding:0;background:#f5f5f5}}
  .page{{width:210mm;min-height:297mm;margin:8mm auto;background:#fff;padding:14mm;border:1px solid #ccc}}
  .title{{text-align:center;font-size:17pt;font-weight:bold;border:2pt solid #000;padding:7px;margin-bottom:10px}}
  .sub{{text-align:center;font-size:10pt;margin-bottom:4px}}
  .hdr{{background:#1F3864;color:#fff;padding:5px 10px;font-size:11pt;font-weight:bold;margin:10px 0 5px}}
  table{{width:100%;border-collapse:collapse;font-size:10pt}}
  th{{background:#D6E4F0;padding:4px 8px;text-align:left;border:1px solid #999;font-size:9pt}}
  td{{padding:4px 8px;border:1px solid #ccc}}
  .lbl{{color:#444;font-size:9pt;font-weight:bold}}
  .val{{font-size:10pt}}
  .row2{{display:flex;gap:16px;margin-bottom:8px}}
  .col{{flex:1;border:1px solid #999;padding:8px}}
  .tot td{{background:#D6E4F0;font-weight:bold}}
  .bold{{font-weight:bold}}
  .right{{text-align:right}}
  .footer{{font-size:8pt;color:#888;text-align:center;margin-top:10px;border-top:1px solid #ccc;padding-top:5px}}
  @media print{{body{{background:#fff}}.page{{box-shadow:none;margin:0;border:none}}}}
</style></head><body><div class='page'>

<div class='title'>{d.FormName}</div>
<div class='sub'>[{(TDSPro.Common.TaxRules.IsNewAct(d.FinancialYear) ? "See Rule 31(1)(a) of Income-tax Rules, 2026" : "See Rule 31(1)(a)")}]</div>
<div class='sub' style='font-size:10pt;margin-bottom:8px'>
  Certificate u/s 203 of {d.ActName} — Tax Deducted at Source on Salary [{d.SectionRef}]
</div>
<div class='sub' style='font-size:9pt;color:#555;margin-bottom:10px'>
  FY: <b>{d.FinancialYear}</b> &nbsp;|&nbsp; {d.AssessmentYear} &nbsp;|&nbsp; Regime: <b>{d.TaxRegime}</b> &nbsp;|&nbsp; Generated: <b>{gen}</b>
</div>

<div class='hdr'>Part A — Deductor & Employee Details</div>
<div class='row2'>
  <div class='col'>
    <div class='lbl'>Employer / Deductor</div>
    <div class='val' style='font-size:11pt;font-weight:bold'>{Esc(d.DeductorName)}</div>
    <div>TAN: <b style='font-family:monospace'>{d.DeductorTan}</b> &nbsp; PAN: <b style='font-family:monospace'>{d.DeductorPan}</b></div>
    <div style='font-size:9pt;color:#555'>{Esc(d.DeductorAddress)}</div>
  </div>
  <div class='col'>
    <div class='lbl'>Employee</div>
    <div class='val' style='font-size:11pt;font-weight:bold'>{Esc(d.EmployeeName)}</div>
    <div>PAN: <b style='font-family:monospace'>{d.EmployeePan}</b> &nbsp; Code: <b>{d.EmployeeCode}</b></div>
    <div style='font-size:9pt;color:#555'>{Esc(d.Designation)}{(string.IsNullOrEmpty(d.Department) ? "" : " — " + Esc(d.Department))}</div>
  </div>
</div>

<div class='hdr'>Part A — Quarter-wise TDS Summary</div>
<table>
  <tr><th>Quarter</th><th class='right'>Gross Salary (₹)</th><th class='right'>TDS Deducted (₹)</th><th class='right'>TDS Deposited (₹)</th><th>Challan Ref.</th></tr>");

            if (d.QuarterRows.Any())
            {
                foreach (var q in d.QuarterRows)
                    sb.Append($"<tr><td>{q.Quarter}</td><td class='right'>{q.AmountPaid:N0}</td><td class='right'>{q.TdsDeducted:N0}</td><td class='right'>{q.TdsDeposited:N0}</td><td>{Esc(q.ChallanNo)}</td></tr>");
            }
            else
            {
                sb.Append("<tr><td colspan='5' style='text-align:center;color:#888'>No payroll runs found for this FY</td></tr>");
            }

            sb.Append($@"<tr class='tot'><td>Total</td><td class='right'>{d.GrossSalary:N0}</td><td class='right'>{d.TdsDeducted:N0}</td><td class='right'>{d.QuarterRows.Sum(q => q.TdsDeposited):N0}</td><td></td></tr>
</table>

<div class='hdr'>Part B — Annual Tax Computation ({d.TaxRegime} Regime)</div>
<table>
  <tr><td class='lbl'>Gross Salary</td><td class='right bold'>{Rv(d.GrossSalary)}</td></tr>
  <tr><td class='lbl'>Less: HRA Exemption u/s 10(13A)</td><td class='right'>{(d.HraExemption == 0 ? "—" : "(" + d.HraExemption.ToString("N0") + ")")}</td></tr>
  <tr><td class='lbl'>Less: Professional Tax u/s 16(iii)</td><td class='right'>{(d.ProfessionalTax == 0 ? "—" : "(" + d.ProfessionalTax.ToString("N0") + ")")}</td></tr>
  <tr><td class='lbl'>Less: Standard Deduction u/s 16(ia)</td><td class='right'>{(d.StandardDeduction == 0 ? "—" : "(" + d.StandardDeduction.ToString("N0") + ")")}</td></tr>
  <tr><td class='lbl'>Less: Chapter VI-A Deductions</td><td class='right'>{(d.Chapter6ADeduction == 0 ? "—" : "(" + d.Chapter6ADeduction.ToString("N0") + ")")}</td></tr>
  <tr class='tot'><td class='lbl'>Taxable Income</td><td class='right bold'>{Rv(d.TaxableIncome)}</td></tr>
  <tr><td class='lbl'>Tax on Income</td><td class='right'>{Rv(d.TaxOnIncome)}</td></tr>
  <tr><td class='lbl'>Less: Rebate u/s 87A</td><td class='right'>{(d.Rebate87A == 0 ? "—" : "(" + d.Rebate87A.ToString("N0") + ")")}</td></tr>
  <tr><td class='lbl'>Surcharge</td><td class='right'>{(d.Surcharge == 0 ? "—" : Rv(d.Surcharge))}</td></tr>
  <tr><td class='lbl'>Health & Education Cess @ 4%</td><td class='right'>{(d.Cess == 0 ? "—" : Rv(d.Cess))}</td></tr>
  <tr class='tot'><td class='lbl'>Total Annual Tax</td><td class='right bold'>{Rv(d.TotalAnnualTax)}</td></tr>
  <tr><td class='lbl'>Tax Deducted at Source (TDS)</td><td class='right bold'>{Rv(d.TdsDeducted)}</td></tr>
</table>

<div style='margin-top:16px;border:1px solid #000;padding:10px;font-size:9pt'>
  <b>Declaration:</b> I, &nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;, do hereby certify that a sum of <b>₹{d.TdsDeducted:N0}
  ({AmountInWords(d.TdsDeducted)})</b> has been deducted as TDS from the salary of the above employee for
  FY {d.FinancialYear} and paid/credited to the Central Government as per the provisions of {d.ActName}.
  <div style='margin-top:30px;display:flex;justify-content:space-between'>
    <div>Date: {gen}</div>
    <div style='text-align:center'>________________________<br><small>Authorised Signatory<br>{Esc(d.DeductorName)}</small></div>
  </div>
</div>

<div class='footer'>
  Computer generated {d.FormName} &nbsp;|&nbsp; {d.ActName} [{d.SectionRef}] &nbsp;|&nbsp; TDS Pro v3.1.0 &nbsp;|&nbsp; {gen}
</div>
</div></body></html>");

            return sb.ToString();
        }

        /// <summary>Save Form 16 / Form 130 HTML to disk and return path.</summary>
        public static string SaveForm16Salary(Form16SalaryData data, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var safe = string.Concat(data.EmployeeName.Split(Path.GetInvalidFileNameChars()));
            var fn   = $"{data.FormName}_{data.EmployeeCode}_{safe}_{data.FinancialYear}.html";
            var path = Path.Combine(outputDir, fn);
            File.WriteAllText(path, RenderForm16SalaryHtml(data), System.Text.Encoding.UTF8);
            Database.LogAction("System", "FORM16_SALARY_GEN", "Form16",
                $"Generated {data.FormName} for {data.EmployeeName} FY {data.FinancialYear}");
            return path;
        }

        /// <summary>Save Form 16 as a paginated PDF (QuestPDF) — proper A4 portrait, print-ready.</summary>
        public static string SaveForm16SalaryPdf(Form16SalaryData data, string outputDir)
        {
            string Money(double v) => v == 0 ? "—" : v.ToString("N0");

            byte[] pdf = PdfReports.BuildA4(
                title:    $"{data.FormName} — Salary TDS Certificate",
                subtitle: $"FY {data.FinancialYear} · AY {data.AssessmentYear} · {data.ActName}",
                body:     c => c.Column(col =>
                {
                    // Deductor / Employee identity
                    col.Item().PaddingBottom(8).Table(t =>
                    {
                        t.ColumnsDefinition(td => { td.RelativeColumn(); td.RelativeColumn(); });
                        t.Cell().Element(PdfReports.HeaderCell).Text("Deductor").Bold();
                        t.Cell().Element(PdfReports.HeaderCell).Text("Employee").Bold();
                        t.Cell().Element(PdfReports.LabelCell).Column(cc =>
                        {
                            cc.Item().Text(data.DeductorName).Bold();
                            cc.Item().Text(data.DeductorAddress).FontSize(9).FontColor(PdfReports.MutedColor);
                            cc.Item().Text($"TAN: {data.DeductorTan}").FontSize(9);
                            cc.Item().Text($"PAN: {data.DeductorPan}").FontSize(9);
                        });
                        t.Cell().Element(PdfReports.LabelCell).Column(cc =>
                        {
                            cc.Item().Text(data.EmployeeName).Bold();
                            cc.Item().Text($"Code: {data.EmployeeCode}").FontSize(9).FontColor(PdfReports.MutedColor);
                            cc.Item().Text($"PAN: {data.EmployeePan}").FontSize(9);
                            cc.Item().Text($"Designation: {data.Designation}").FontSize(9);
                            cc.Item().Text($"Regime: {data.TaxRegime}").FontSize(9);
                        });
                    });

                    // Annual salary summary
                    col.Item().Background("#cbd5e1").AlignCenter().Padding(4).Text("Annual Salary & Tax Summary").Bold();
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(td => { td.RelativeColumn(3); td.RelativeColumn(1); });
                        void Row(string label, double v, bool bold = false)
                        {
                            var lblText = t.Cell().Element(bold ? PdfReports.SubtotalCell : PdfReports.LabelCell).Text(label);
                            if (bold) lblText.Bold();
                            var amtText = t.Cell().Element(bold ? PdfReports.SubtotalCell : PdfReports.AmountCell).Text(Money(v));
                            if (bold) amtText.Bold();
                        }
                        Row("Gross Salary",                        data.GrossSalary);
                        Row("Less: HRA Exemption u/s 10(13A)",    data.HraExemption);
                        Row("Less: Professional Tax u/s 16(iii)", data.ProfessionalTax);
                        Row("Less: Standard Deduction u/s 16(ia)",data.StandardDeduction);
                        Row("Less: Chapter VI-A Deductions",       data.Chapter6ADeduction);
                        Row("Taxable Income",                   data.TaxableIncome, bold: true);
                        Row("Tax on Income (Slab)",             data.TaxOnIncome);
                        if (data.Rebate87A > 0) Row("Less: Rebate u/s 87A", data.Rebate87A);
                        if (data.Surcharge   > 0) Row("Add: Surcharge",     data.Surcharge);
                        Row("Add: Health & Education Cess @ 4%", data.Cess);
                        Row("Total Tax Liability",              data.TotalAnnualTax, bold: true);
                        Row("TDS Deducted",                     data.TdsDeducted, bold: true);
                    });

                    // Quarterly TDS breakup
                    if (data.QuarterRows.Any())
                    {
                        col.Item().PaddingTop(12).Background("#cbd5e1").AlignCenter().Padding(4).Text("Quarterly TDS Breakup").Bold();
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(td => { td.RelativeColumn(); td.RelativeColumn(); td.RelativeColumn(); td.RelativeColumn(); });
                            t.Header(h =>
                            {
                                h.Cell().Element(PdfReports.HeaderCell).Text("Quarter").Bold();
                                h.Cell().Element(PdfReports.HeaderCell).AlignRight().Text("Amount Paid").Bold();
                                h.Cell().Element(PdfReports.HeaderCell).AlignRight().Text("TDS Deducted").Bold();
                                h.Cell().Element(PdfReports.HeaderCell).AlignRight().Text("TDS Deposited").Bold();
                            });
                            foreach (var q in data.QuarterRows)
                            {
                                t.Cell().Element(PdfReports.LabelCell).Text(q.Quarter);
                                t.Cell().Element(PdfReports.AmountCell).Text(Money(q.AmountPaid));
                                t.Cell().Element(PdfReports.AmountCell).Text(Money(q.TdsDeducted));
                                t.Cell().Element(PdfReports.AmountCell).Text(Money(q.TdsDeposited));
                            }
                        });
                    }

                    // Signature
                    col.Item().PaddingTop(30).Row(r =>
                    {
                        r.RelativeItem().Column(cc =>
                        {
                            cc.Item().Text("Place: _______________").FontSize(9);
                            cc.Item().PaddingTop(4).Text($"Date: {data.GeneratedDate:dd-MMM-yyyy}").FontSize(9);
                        });
                        r.RelativeItem().AlignRight().Column(cc =>
                        {
                            cc.Item().Text("For " + data.DeductorName).FontSize(9);
                            cc.Item().PaddingTop(30).BorderTop(1).BorderColor(PdfReports.BorderColor).Width(220).PaddingTop(2).AlignRight().Text("Authorized Signatory").FontSize(9).FontColor(PdfReports.MutedColor);
                        });
                    });
                }));

            Directory.CreateDirectory(outputDir);
            var safe = string.Concat(data.EmployeeName.Split(Path.GetInvalidFileNameChars()));
            var fn   = $"{data.FormName}_{data.EmployeeCode}_{safe}_{data.FinancialYear}.pdf";
            var path = Path.Combine(outputDir, fn);
            File.WriteAllBytes(path, pdf);
            Database.LogAction("System", "FORM16_SALARY_PDF_GEN", "Form16",
                $"Generated PDF {data.FormName} for {data.EmployeeName} FY {data.FinancialYear}");
            return path;
        }

        /// <summary>Generate Form 16/130 for all employees of a deductor for the FY.</summary>
        public static List<string> GenerateBulkSalary(int deductorId, string fy, string outputDir)
        {
            var paths = new List<string>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            // Get distinct employees who have payroll runs for this FY
            cmd.CommandText = @"SELECT DISTINCT employee_id FROM payroll_runs
                                WHERE deductor_id=@did AND financial_year=@fy";
            cmd.Parameters.AddWithValue("@did", deductorId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            var empIds = new List<int>();
            using var r = cmd.ExecuteReader();
            while (r.Read()) empIds.Add(r.GetInt32(0));
            r.Close();

            foreach (var empId in empIds)
            {
                var data = BuildForm16Salary(deductorId, empId, fy);
                if (data.TdsDeducted > 0 || data.GrossSalary > 0)
                    paths.Add(SaveForm16Salary(data, outputDir));
            }
            return paths;
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static string Esc(string s) =>
            (s ?? "").Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");

        private static string AmountInWords(double amount)
        {
            // Simple implementation for common ranges
            long paise = (long)Math.Round(amount * 100);
            long rupees = paise / 100;
            long p = paise % 100;
            if (rupees == 0) return "Zero Rupees Only";

            var units = new[] { "", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine",
                "Ten", "Eleven", "Twelve", "Thirteen", "Fourteen", "Fifteen", "Sixteen", "Seventeen",
                "Eighteen", "Nineteen" };
            var tens  = new[] { "", "", "Twenty", "Thirty", "Forty", "Fifty", "Sixty", "Seventy", "Eighty", "Ninety" };

            string Words(long n)
            {
                if (n == 0) return "";
                if (n < 20)  return units[n];
                if (n < 100) return tens[n/10] + (n%10 > 0 ? " " + units[n%10] : "");
                if (n < 1000)return units[n/100] + " Hundred" + (n%100 > 0 ? " " + Words(n%100) : "");
                if (n < 100000) return Words(n/1000) + " Thousand" + (n%1000 > 0 ? " " + Words(n%1000) : "");
                if (n < 10000000) return Words(n/100000) + " Lakh" + (n%100000 > 0 ? " " + Words(n%100000) : "");
                return Words(n/10000000) + " Crore" + (n%10000000 > 0 ? " " + Words(n%10000000) : "");
            }

            var result = Words(rupees) + " Rupees";
            if (p > 0) result += " and " + Words(p) + " Paise";
            return result + " Only";
        }
    }

    // ── Data models ────────────────────────────────────────────────────────────
    public class Form16AData
    {
        public string FinancialYear    { get; set; } = "";
        public string DeductorName     { get; set; } = "";
        public string DeductorTan      { get; set; } = "";
        public string DeductorPan      { get; set; } = "";
        public string DeductorAddress  { get; set; } = "";
        public string DeductorCity     { get; set; } = "";
        public string DeductorPin      { get; set; } = "";
        public string DeducteeName     { get; set; } = "";
        public string DeducteePan      { get; set; } = "";
        public string DeducteeAddress  { get; set; } = "";
        public double TotalAmountPaid   { get; set; }
        public double TotalTdsDeducted  { get; set; }
        public double TotalTdsDeposited { get; set; }
        public DateTime GeneratedDate   { get; set; } = DateTime.Today;
        public List<Form16ATransaction> Transactions { get; set; } = new();
    }

    public class Form16ATransaction
    {
        public int    SlNo          { get; set; }
        public DateTime EntryDate   { get; set; }
        public string Section       { get; set; } = "";
        public double AmountPaid    { get; set; }
        public double TdsDeducted   { get; set; }
        public double Surcharge     { get; set; }
        public double Cess          { get; set; }
        public double TotalTds      { get; set; }
        public string ChallanNo     { get; set; } = "";
        public string BsrCode       { get; set; } = "";
        public string Quarter       { get; set; } = "";
        public string DateOfDeposit { get; set; } = "";
    }

    // ════════════════════════════════════════════════════════════════════════
    // FORM 16 / FORM 130 — SALARY TDS CERTIFICATE
    // Form 16  → FY ≤ 2025-26  (Income-tax Act 1961, Section 192)
    // Form 130 → FY ≥ 2026-27  (Income-tax Act 2025, Section 392(1))
    // ════════════════════════════════════════════════════════════════════════

    public class Form16SalaryData
    {
        // FY-aware form metadata
        public string FinancialYear   { get; set; } = "";
        public string FormName        => TDSPro.Common.TaxRules.SalaryTdsCertForm(FinancialYear);
        public string SectionRef      => TDSPro.Common.TaxRules.SalaryTdsSection(FinancialYear);
        public string ActName         => TDSPro.Common.TaxRules.ActName(FinancialYear);
        public string AssessmentYear  => TDSPro.Common.TaxRules.AssessmentYearLabel(FinancialYear);

        // Deductor
        public string DeductorName    { get; set; } = "";
        public string DeductorTan     { get; set; } = "";
        public string DeductorPan     { get; set; } = "";
        public string DeductorAddress { get; set; } = "";

        // Employee
        public string EmployeeName    { get; set; } = "";
        public string EmployeePan     { get; set; } = "";
        public string EmployeeCode    { get; set; } = "";
        public string Designation     { get; set; } = "";
        public string Department      { get; set; } = "";
        public string TaxRegime       { get; set; } = "New";

        // Annual salary summary (aggregated from payroll_runs)
        public double GrossSalary          { get; set; }
        public double HraExemption         { get; set; }
        public double StandardDeduction    { get; set; }
        public double Chapter6ADeduction   { get; set; }
        public double TaxableIncome        { get; set; }
        public double TaxOnIncome          { get; set; }
        public double Surcharge            { get; set; }
        public double Cess                 { get; set; }
        public double TotalAnnualTax       { get; set; }
        public double TdsDeducted          { get; set; }
        public double Rebate87A            { get; set; }
        public double ProfessionalTax      { get; set; }

        // Quarter-wise TDS summary
        public List<Form16QuarterRow> QuarterRows { get; set; } = new();

        // Month-wise breakdown
        public List<Form16MonthRow>   MonthRows   { get; set; } = new();

        public DateTime GeneratedDate { get; set; } = DateTime.Today;
    }

    public class Form16QuarterRow
    {
        public string Quarter       { get; set; } = "";
        public double AmountPaid    { get; set; }
        public double TdsDeducted   { get; set; }
        public double TdsDeposited  { get; set; }
        public string ChallanNo     { get; set; } = "";
    }

    public class Form16MonthRow
    {
        public string MonthLabel    { get; set; } = "";
        public double GrossSalary   { get; set; }
        public double TdsDeducted   { get; set; }
        public double NetPay        { get; set; }
    }
}
