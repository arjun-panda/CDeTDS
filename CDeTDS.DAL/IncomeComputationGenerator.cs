using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using CDeTDS.DAL.Models;
using CDeTDS.Common;

namespace CDeTDS.DAL
{
    /// <summary>
    /// "Computation of Income" sheet — CA-style derivation:
    ///   Gross Salary (with component breakdown) → Less: Exempted Allowances → Net Taxable Salary
    ///   → Less: Standard Deduction → Less: Chapter VI-A → Taxable Income → Tax + Cess
    /// Pulls salary structure (annualized) and tax declaration.
    /// </summary>
    public static class IncomeComputationGenerator
    {
        public static IncomeComputationData Build(int deductorId, int employeeId, string fy)
        {
            var d = new IncomeComputationData { FinancialYear = fy };
            using var conn = Database.GetConnection();

            using var d1 = conn.CreateCommand();
            d1.CommandText = "SELECT * FROM deductors WHERE id=@id";
            d1.Parameters.AddWithValue("@id", deductorId);
            using var r1 = d1.ExecuteReader();
            if (r1.Read())
                d.EmployerName = r1["company_name"]?.ToString() ?? "";
            r1.Close();

            // Employee + salary
            var repo = new PayrollRepository();
            var emps = repo.GetAllEmployees(deductorId);
            var emp = emps.FirstOrDefault(e => e.Id == employeeId);
            if (emp == null) return d;

            d.EmployeeName = emp.Name;
            d.EmployeePan  = emp.Pan;
            d.EmployeeDob  = emp.DateOfBirth;
            d.Regime       = emp.TaxRegime;

            // Income components — annualized from salary structure
            var ss = emp.Salary ?? new SalaryStructure();
            d.Lines = new List<IncomeLine>();
            if (ss.Basic > 0)            d.Lines.Add(new IncomeLine { Name = "Basic",             Annual = ss.Basic            * 12 });
            if (ss.Hra > 0)              d.Lines.Add(new IncomeLine { Name = "House Rent",        Annual = ss.Hra              * 12 });
            if (ss.Da > 0)               d.Lines.Add(new IncomeLine { Name = "Dearness Allowance",Annual = ss.Da               * 12 });
            if (ss.SpecialAllowance > 0) d.Lines.Add(new IncomeLine { Name = "Special Allowance", Annual = ss.SpecialAllowance * 12 });
            if (ss.MedicalAllowance > 0) d.Lines.Add(new IncomeLine { Name = "Medical Allowance", Annual = ss.MedicalAllowance * 12 });
            if (ss.Lta > 0)              d.Lines.Add(new IncomeLine { Name = "LTA",               Annual = ss.Lta              * 12 });
            // OtherAllowance is a UI mirror of Components (read-only) — Components are listed individually below.

            // Named components (LTA, Telephone, Meal, etc.)
            foreach (var c in ss.Components)
            {
                if (c.Received <= 0) continue;
                d.Lines.Add(new IncomeLine
                {
                    Name      = c.Name,
                    Annual    = c.Received * 12,
                    Exempted  = c.Paid * 12,        // bills-substantiated portion
                    RuleRef   = c.RuleRef
                });
            }
            // Variable pay
            if (ss.AnnualBonus > 0)     d.Lines.Add(new IncomeLine { Name = "Performance Bonus",   Annual = ss.AnnualBonus });
            if (ss.AnnualIncentive > 0) d.Lines.Add(new IncomeLine { Name = "Sales / Incentive",   Annual = ss.AnnualIncentive });

            d.GrossSalary = d.Lines.Sum(l => l.Annual);

            // HRA exemption (annual) — from declaration + city
            using var d2 = conn.CreateCommand();
            d2.CommandText = "SELECT * FROM tax_declarations WHERE employee_id=@e AND financial_year=@f LIMIT 1";
            d2.Parameters.AddWithValue("@e", employeeId);
            d2.Parameters.AddWithValue("@f", fy);
            double rent = 0; double sec80c=0,sec80d=0,sec80dParents=0,sec80g=0,sec80ccd1B=0,sec80ccd2=0,otherDed=0,ltaExempt=0;
            string declCityType = "";
            using var r2 = d2.ExecuteReader();
            if (r2.Read())
            {
                rent          = Convert.ToDouble(r2["rent_paid"] ?? 0);
                sec80c        = Convert.ToDouble(r2["sec_80c"] ?? 0);
                sec80d        = Convert.ToDouble(r2["sec_80d_self"] ?? 0);
                sec80dParents = Convert.ToDouble(r2["sec_80d_parents"] ?? 0);
                sec80g        = Convert.ToDouble(r2["sec_80g"] ?? 0);
                sec80ccd1B    = Convert.ToDouble(r2["sec_80ccd_employee"] ?? 0);
                sec80ccd2     = Convert.ToDouble(r2["sec_80ccd_employer"] ?? 0);
                otherDed      = Convert.ToDouble(r2["other_deductions"] ?? 0);
                try { ltaExempt   = Convert.ToDouble(r2["lta_exemption"] ?? 0); } catch { ltaExempt = 0; }
                try { declCityType = r2["hra_city_type"]?.ToString() ?? ""; } catch { }
            }
            r2.Close();

            // Prefer employee-level city type → declaration city type → Non-Metro
            string city = !string.IsNullOrEmpty(emp.HraCityType) ? emp.HraCityType
                        : !string.IsNullOrEmpty(declCityType)    ? declCityType
                        : "Non-Metro";
            // rent from DB is annual; CalcHraExemption expects monthly values → pass rent/12, then annualise
            d.HraExempt = CalcHraExemption(ss.Basic, ss.Hra, rent / 12, city) * 12;

            // Exempted total (annual) — HRA + components paid + LTA declaration
            double componentsPaid = ss.Components.Sum(c => c.Paid) * 12;
            d.LtaExempt = ltaExempt;
            d.ExemptedTotal = d.HraExempt + componentsPaid + ltaExempt;

            d.NetTaxableSalary = Math.Max(0, d.GrossSalary - d.ExemptedTotal);

            DateTime? dob = DateTime.TryParse(emp.DateOfBirth, out var dt) ? dt : (DateTime?)null;
            var ageCat = TaxRules.GetAgeCategory(dob, fy);

            bool useOld = emp.TaxRegime == "Old";
            var rules   = TaxRules.GetRules(fy, !useOld, ageCat);
            d.StdDeduction = rules.StandardDeduction;
            d.IncomeFromSalary = Math.Max(0, d.NetTaxableSalary - d.StdDeduction);

            // PF (annual employee)
            double pfMo = ss.PfFixedAmount > 0 ? ss.PfFixedAmount : (ss.PfApplicable ? Math.Round(ss.Basic * 0.12) : 0);
            d.AnnualPf = pfMo * 12;

            // Chapter VI-A (old regime only)
            d.Sec80C  = useOld ? Math.Min(sec80c + d.AnnualPf, 150000) : 0;
            d.Sec80D  = useOld ? Math.Min(sec80d, 25000) + Math.Min(sec80dParents, 25000) : 0;
            d.Sec80G  = useOld ? sec80g : 0;
            d.Sec80CCD1B = useOld ? Math.Min(sec80ccd1B, 50000) : 0;
            d.Sec80CCD2  = Math.Min(sec80ccd2, ss.Basic * 12 * TaxRules.Get80CCD2Rate(fy, !useOld));
            d.OtherDeductions = useOld ? otherDed : 0;
            d.Chapter6A = d.Sec80C + d.Sec80D + d.Sec80G + d.Sec80CCD1B + d.Sec80CCD2 + d.OtherDeductions;

            d.TaxableIncome = Math.Max(0, d.IncomeFromSalary - d.Chapter6A);
            d.TaxOnIncome   = TaxRules.ComputeSlabTax(d.TaxableIncome, rules);
            var (afterRebate, rebate) = TaxRules.Apply87A(d.TaxOnIncome, d.TaxableIncome, rules);
            d.Rebate87A = rebate;
            d.TaxAfterRebate = afterRebate;
            d.Surcharge   = TaxRules.CalcSurcharge(afterRebate, d.TaxableIncome, rules);
            d.Cess        = Math.Round((afterRebate + d.Surcharge) * 0.04);
            d.TotalTax    = d.TaxAfterRebate + d.Surcharge + d.Cess;
            return d;
        }

        public static string RenderHtml(IncomeComputationData d)
        {
            var grossRows = string.Join("", d.Lines.Select(l => $@"
                <tr>
                    <td style='padding-left:24px'>- {l.Name}{(string.IsNullOrEmpty(l.RuleRef) ? "" : $" <small style='color:#64748b'>[{l.RuleRef}]</small>")}</td>
                    <td style='text-align:right;font-family:monospace'>{l.Annual:N0}</td>
                    <td></td><td></td>
                </tr>"));

            return $@"<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Computation of Income — {d.EmployeeName}</title>
<style>
body{{font-family:Arial,sans-serif;font-size:11.5px;margin:18px;color:#0f172a}}
h2,h3{{text-align:center;margin:4px 0}}
table{{width:100%;border-collapse:collapse;margin:6px 0}}
th,td{{border:1px solid #cbd5e1;padding:5px 9px}}
th{{background:#e2e8f0;text-align:left}}
.hdr-tbl td{{border:1px solid #cbd5e1;padding:5px 9px}}
.section-band{{background:#cbd5e1;font-weight:700;text-align:center;padding:5px}}
</style></head><body>
<h2>{d.EmployerName}</h2>
<h3>Computation of Income</h3>

<table class='hdr-tbl'>
  <tr><td style='width:18%'><strong>Name</strong></td><td>{d.EmployeeName}</td><td style='width:18%'></td><td></td></tr>
  <tr><td><strong>PAN</strong></td><td style='font-family:monospace'>{d.EmployeePan}</td><td></td><td></td></tr>
  <tr><td><strong>Date of Birth</strong></td><td>{d.EmployeeDob}</td><td></td><td></td></tr>
  <tr><td><strong>Assessment Year</strong></td><td>{NextFy(d.FinancialYear)}</td><td><strong>Financial Year</strong></td><td>{d.FinancialYear}</td></tr>
</table>

<table>
  <thead><tr><th style='width:55%'>Particulars</th><th style='width:15%;text-align:right'></th><th style='width:15%;text-align:right'></th><th style='width:15%;text-align:right'>Amount</th></tr></thead>
  <tbody>
    <tr><td colspan='4' class='section-band'>AS PER {(d.Regime == "Old" ? "OLD" : "NEW")} METHOD</td></tr>

    <tr><td colspan='4' style='font-weight:600'>Income from Salary</td></tr>
    <tr><td colspan='4' style='padding-left:12px'>FROM {d.EmployerName}</td></tr>
    {grossRows}
    <tr style='font-weight:700'>
      <td>Gross Salary Paid</td><td></td>
      <td style='text-align:right;font-family:monospace;border-top:2px solid #475569'>{d.GrossSalary:N0}</td>
      <td></td>
    </tr>

    {(d.ExemptedTotal > 0 ? "<tr><td colspan='4' style='font-weight:600'>Less - Allowances Exempted</td></tr>" : "")}
    {(d.HraExempt > 0 ? $"<tr><td style='padding-left:24px'>- HRA Exemption</td><td></td><td style='text-align:right;font-family:monospace'>{d.HraExempt:N0}</td><td></td></tr>" : "")}
    {(d.LtaExempt > 0 ? $"<tr><td style='padding-left:24px'>- LTA Exemption [Sec 10(5)]</td><td></td><td style='text-align:right;font-family:monospace'>{d.LtaExempt:N0}</td><td></td></tr>" : "")}
    {string.Join("", d.Lines.Where(l => l.Exempted > 0).Select(l => $"<tr><td style='padding-left:24px'>- {l.Name} (bills)</td><td></td><td style='text-align:right;font-family:monospace'>{l.Exempted:N0}</td><td></td></tr>"))}
    <tr style='font-weight:700'>
      <td>Net Taxable Salary</td><td></td><td></td>
      <td style='text-align:right;font-family:monospace;border-top:2px solid #475569'>{d.NetTaxableSalary:N0}</td>
    </tr>

    <tr><td>Less: Standard Deduction</td><td></td><td></td><td style='text-align:right;font-family:monospace'>{d.StdDeduction:N0}</td></tr>
    <tr style='font-weight:700'>
      <td>Income from Salary</td><td></td><td></td>
      <td style='text-align:right;font-family:monospace;border-top:2px solid #475569'>{d.IncomeFromSalary:N0}</td>
    </tr>

    {(d.Chapter6A > 0 ? $@"
    <tr><td colspan='4' style='font-weight:600'>Less: Chapter VI-A Deductions</td></tr>
    {(d.Sec80C  > 0 ? $"<tr><td style='padding-left:24px'>- Section 80C (incl. PF ₹{d.AnnualPf:N0})</td><td></td><td style='text-align:right;font-family:monospace'>{d.Sec80C:N0}</td><td></td></tr>" : "")}
    {(d.Sec80D  > 0 ? $"<tr><td style='padding-left:24px'>- Section 80D (Health insurance)</td><td></td><td style='text-align:right;font-family:monospace'>{d.Sec80D:N0}</td><td></td></tr>" : "")}
    {(d.Sec80G  > 0 ? $"<tr><td style='padding-left:24px'>- Section 80G (Donations)</td><td></td><td style='text-align:right;font-family:monospace'>{d.Sec80G:N0}</td><td></td></tr>" : "")}
    {(d.Sec80CCD1B > 0 ? $"<tr><td style='padding-left:24px'>- Section 80CCD(1B) (NPS additional)</td><td></td><td style='text-align:right;font-family:monospace'>{d.Sec80CCD1B:N0}</td><td></td></tr>" : "")}
    {(d.Sec80CCD2  > 0 ? $"<tr><td style='padding-left:24px'>- Section 80CCD(2) (Employer NPS)</td><td></td><td style='text-align:right;font-family:monospace'>{d.Sec80CCD2:N0}</td><td></td></tr>" : "")}
    {(d.OtherDeductions > 0 ? $"<tr><td style='padding-left:24px'>- Other Deductions</td><td></td><td style='text-align:right;font-family:monospace'>{d.OtherDeductions:N0}</td><td></td></tr>" : "")}
    <tr style='font-weight:700'><td>Total Chapter VI-A</td><td></td><td></td><td style='text-align:right;font-family:monospace'>{d.Chapter6A:N0}</td></tr>" : "")}

    <tr style='font-weight:700;background:#fef3c7'>
      <td>Taxable Income</td><td></td><td></td>
      <td style='text-align:right;font-family:monospace'>{d.TaxableIncome:N0}</td>
    </tr>

    <tr><td>Tax on Income (Slab)</td><td></td><td></td><td style='text-align:right;font-family:monospace'>{d.TaxOnIncome:N0}</td></tr>
    {(d.Rebate87A > 0 ? $"<tr><td>Less: Rebate u/s 87A</td><td></td><td></td><td style='text-align:right;font-family:monospace'>({d.Rebate87A:N0})</td></tr>" : "")}
    <tr><td>Tax After Rebate</td><td></td><td></td><td style='text-align:right;font-family:monospace'>{d.TaxAfterRebate:N0}</td></tr>
    {(d.Surcharge > 0 ? $"<tr><td>Add: Surcharge</td><td></td><td></td><td style='text-align:right;font-family:monospace'>{d.Surcharge:N0}</td></tr>" : "")}
    <tr><td>Add: Health &amp; Education Cess @ 4%</td><td></td><td></td><td style='text-align:right;font-family:monospace'>{d.Cess:N0}</td></tr>
    <tr style='font-weight:700;background:#dcfce7'>
      <td>Total Tax Liability</td><td></td><td></td>
      <td style='text-align:right;font-family:monospace'>{d.TotalTax:N0}</td>
    </tr>
  </tbody>
</table>

<p style='font-size:10px;color:#64748b;text-align:center;margin-top:20px'>
Computed under {(d.Regime == "Old" ? "Old" : "New")} Tax Regime · {TaxRules.YearLabel(d.FinancialYear)} · Generated by CDeTDS v{CDeTDS.Common.AppConstants.AppVersion}
</p>
</body></html>";
        }

        private static double CalcHraExemption(double basic, double hra, double rent, string cityType)
        {
            if (rent <= 0 || hra <= 0) return 0;
            double pct = cityType == "Metro" ? 0.50 : 0.40;
            return Math.Round(Math.Min(hra, Math.Min(basic * pct, Math.Max(0, rent - basic * 0.10))));
        }

        private static string NextFy(string fy)
        {
            if (fy.Length >= 7 && int.TryParse(fy.Substring(0, 4), out var y))
                return $"{y + 1}-{(y + 2) % 100:D2}";
            return fy;
        }

        public static string SaveHtml(IncomeComputationData d, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var safeName = string.Concat((d.EmployeeName ?? "employee").Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(outputDir, $"ComputationOfIncome_{safeName}_{d.FinancialYear}.html");
            File.WriteAllText(path, RenderHtml(d), System.Text.Encoding.UTF8);
            return path;
        }

        /// <summary>Generate a paginated A4 PDF — proper CA-style format with section bands.</summary>
        public static string SavePdf(IncomeComputationData d, string outputDir)
        {
            string Money(double v) => v == 0 ? "—" : v.ToString("N0");

            byte[] pdf = PdfReports.BuildA4(
                title:    "Computation of Income",
                subtitle: d.EmployerName,
                body:     c => c.Column(col =>
                {
                    // 'COMPUTED · NOT AUDITED' disclaimer strip
                    col.Item().PaddingBottom(8).Background("#fffbeb").Border(1).BorderColor("#fde68a").Padding(5).AlignCenter()
                        .Text("⚠ COMPUTED · NOT AUDITED — for taxpayer reference only. Not a substitute for Form 16.").FontSize(8).Italic().FontColor("#92400e");

                    // Identity block
                    col.Item().PaddingBottom(8).Table(t =>
                    {
                        t.ColumnsDefinition(td => { td.RelativeColumn(); td.RelativeColumn(); td.RelativeColumn(); td.RelativeColumn(); });
                        t.Cell().Element(PdfReports.LabelCell).Text("Name").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(d.EmployeeName).Bold();
                        t.Cell().Element(PdfReports.LabelCell).Text("PAN").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(d.EmployeePan).Bold();
                        t.Cell().Element(PdfReports.LabelCell).Text("Date of Birth").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(d.EmployeeDob);
                        t.Cell().Element(PdfReports.LabelCell).Text("Regime").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(d.Regime + " Regime").Bold();
                        t.Cell().Element(PdfReports.LabelCell).Text("Financial Year").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(d.FinancialYear).Bold();
                        t.Cell().Element(PdfReports.LabelCell).Text("Assessment Year").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(NextFy(d.FinancialYear)).Bold();
                    });

                    // Section band
                    col.Item().Background("#cbd5e1").AlignCenter().Padding(4).Text($"AS PER {(d.Regime == "Old" ? "OLD" : "NEW")} METHOD").Bold();

                    // Main table — Particulars / Amount
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(td => { td.RelativeColumn(4); td.RelativeColumn(1); td.RelativeColumn(1); });

                        t.Header(h =>
                        {
                            h.Cell().Element(PdfReports.HeaderCell).Text("Particulars").Bold();
                            h.Cell().Element(PdfReports.HeaderCell).AlignRight().Text("").Bold();
                            h.Cell().Element(PdfReports.HeaderCell).AlignRight().Text("Amount (₹)").Bold();
                        });

                        t.Cell().ColumnSpan(3).Element(PdfReports.LabelCell).Text("Income from Salary").Bold();
                        t.Cell().ColumnSpan(3).Element(c2 => c2.PaddingLeft(20).PaddingVertical(2)).Text($"FROM {d.EmployerName}").FontColor(PdfReports.MutedColor).FontSize(9);

                        foreach (var l in d.Lines)
                        {
                            t.Cell().Element(c2 => c2.PaddingLeft(32).PaddingVertical(2)).Text(string.IsNullOrEmpty(l.RuleRef) ? $"- {l.Name}" : $"- {l.Name} [{l.RuleRef}]");
                            t.Cell().Element(PdfReports.AmountCell).Text(Money(l.Annual));
                            t.Cell().Element(PdfReports.AmountCell).Text("");
                        }

                        t.Cell().Element(PdfReports.SubtotalCell).Text("Gross Salary Paid").Bold();
                        t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text("").Bold();
                        t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text(Money(d.GrossSalary)).Bold();

                        if (d.ExemptedTotal > 0)
                        {
                            t.Cell().ColumnSpan(3).Element(PdfReports.LabelCell).Text("Less — Allowances Exempted").Bold();
                            if (d.HraExempt > 0)
                            {
                                t.Cell().Element(c2 => c2.PaddingLeft(32).PaddingVertical(2)).Text("- HRA Exemption");
                                t.Cell().Element(PdfReports.AmountCell).Text(Money(d.HraExempt));
                                t.Cell().Element(PdfReports.AmountCell).Text("");
                            }
                            if (d.LtaExempt > 0)
                            {
                                t.Cell().Element(c2 => c2.PaddingLeft(32).PaddingVertical(2)).Text("- LTA Exemption [Sec 10(5)]");
                                t.Cell().Element(PdfReports.AmountCell).Text(Money(d.LtaExempt));
                                t.Cell().Element(PdfReports.AmountCell).Text("");
                            }
                            foreach (var l in d.Lines.Where(x => x.Exempted > 0))
                            {
                                t.Cell().Element(c2 => c2.PaddingLeft(32).PaddingVertical(2)).Text($"- {l.Name} (bills)");
                                t.Cell().Element(PdfReports.AmountCell).Text(Money(l.Exempted));
                                t.Cell().Element(PdfReports.AmountCell).Text("");
                            }
                        }

                        t.Cell().Element(PdfReports.SubtotalCell).Text("Net Taxable Salary").Bold();
                        t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text("").Bold();
                        t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text(Money(d.NetTaxableSalary)).Bold();

                        t.Cell().Element(PdfReports.LabelCell).Text("Less: Standard Deduction");
                        t.Cell().Element(PdfReports.AmountCell).Text("");
                        t.Cell().Element(PdfReports.AmountCell).Text(Money(d.StdDeduction));

                        t.Cell().Element(PdfReports.SubtotalCell).Text("Income from Salary").Bold();
                        t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text("").Bold();
                        t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text(Money(d.IncomeFromSalary)).Bold();

                        if (d.Chapter6A > 0)
                        {
                            t.Cell().ColumnSpan(3).Element(PdfReports.LabelCell).Text("Less: Chapter VI-A Deductions").Bold();
                            void Row(string name, double v) { if (v <= 0) return;
                                t.Cell().Element(c2 => c2.PaddingLeft(32).PaddingVertical(2)).Text(name);
                                t.Cell().Element(PdfReports.AmountCell).Text(Money(v));
                                t.Cell().Element(PdfReports.AmountCell).Text(""); }
                            Row($"- Sec 80C (incl. PF ₹{d.AnnualPf:N0})", d.Sec80C);
                            Row("- Sec 80D (Health insurance)",          d.Sec80D);
                            Row("- Sec 80G (Donations)",                 d.Sec80G);
                            Row("- Sec 80CCD(1B) (NPS additional)",      d.Sec80CCD1B);
                            Row("- Sec 80CCD(2) (Employer NPS)",         d.Sec80CCD2);
                            Row("- Other Deductions",                    d.OtherDeductions);
                            t.Cell().Element(PdfReports.SubtotalCell).Text("Total Chapter VI-A").Bold();
                            t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text("").Bold();
                            t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text(Money(d.Chapter6A)).Bold();
                        }

                        t.Cell().Element(c2 => c2.Background("#fef3c7").Padding(4)).Text("Taxable Income").Bold();
                        t.Cell().Element(c2 => c2.Background("#fef3c7").Padding(4)).AlignRight().Text("");
                        t.Cell().Element(c2 => c2.Background("#fef3c7").Padding(4)).AlignRight().Text(Money(d.TaxableIncome)).Bold();

                        t.Cell().Element(PdfReports.LabelCell).Text("Tax on Income (Slab)");
                        t.Cell().Element(PdfReports.AmountCell).Text("");
                        t.Cell().Element(PdfReports.AmountCell).Text(Money(d.TaxOnIncome));

                        if (d.Rebate87A > 0)
                        {
                            t.Cell().Element(PdfReports.LabelCell).Text("Less: Rebate u/s 87A");
                            t.Cell().Element(PdfReports.AmountCell).Text("");
                            t.Cell().Element(PdfReports.AmountCell).Text("(" + Money(d.Rebate87A) + ")").FontColor(PdfReports.AccentColor);
                        }
                        t.Cell().Element(PdfReports.LabelCell).Text("Tax After Rebate");
                        t.Cell().Element(PdfReports.AmountCell).Text("");
                        t.Cell().Element(PdfReports.AmountCell).Text(Money(d.TaxAfterRebate));

                        if (d.Surcharge > 0)
                        {
                            t.Cell().Element(PdfReports.LabelCell).Text("Add: Surcharge");
                            t.Cell().Element(PdfReports.AmountCell).Text("");
                            t.Cell().Element(PdfReports.AmountCell).Text(Money(d.Surcharge));
                        }
                        t.Cell().Element(PdfReports.LabelCell).Text("Add: Health & Education Cess @ 4%");
                        t.Cell().Element(PdfReports.AmountCell).Text("");
                        t.Cell().Element(PdfReports.AmountCell).Text(Money(d.Cess));

                        t.Cell().Element(c2 => c2.Background("#dcfce7").Padding(6)).Text("Total Tax Liability").Bold().FontColor(PdfReports.AccentColor);
                        t.Cell().Element(c2 => c2.Background("#dcfce7").Padding(6)).AlignRight().Text("");
                        t.Cell().Element(c2 => c2.Background("#dcfce7").Padding(6)).AlignRight().Text(Money(d.TotalTax)).Bold().FontColor(PdfReports.AccentColor).FontSize(12);
                    });

                    col.Item().PaddingTop(20).AlignCenter().Text($"Computed under {(d.Regime == "Old" ? "Old" : "New")} Tax Regime · {TaxRules.YearLabel(d.FinancialYear)}").FontSize(9).Italic().FontColor(PdfReports.MutedColor);
                }));

            Directory.CreateDirectory(outputDir);
            var safeName = string.Concat((d.EmployeeName ?? "employee").Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(outputDir, $"ComputationOfIncome_{safeName}_{d.FinancialYear}.pdf");
            File.WriteAllBytes(path, pdf);
            return path;
        }
    }

    public class IncomeComputationData
    {
        public string EmployerName    { get; set; } = "";
        public string EmployeeName    { get; set; } = "";
        public string EmployeePan     { get; set; } = "";
        public string EmployeeDob     { get; set; } = "";
        public string FinancialYear   { get; set; } = "";
        public string Regime          { get; set; } = "New";
        public List<IncomeLine> Lines { get; set; } = new();
        public double GrossSalary     { get; set; }
        public double HraExempt       { get; set; }
        public double LtaExempt       { get; set; }
        public double ExemptedTotal   { get; set; }
        public double NetTaxableSalary{ get; set; }
        public double StdDeduction    { get; set; }
        public double IncomeFromSalary{ get; set; }
        public double AnnualPf        { get; set; }
        public double Sec80C          { get; set; }
        public double Sec80D          { get; set; }
        public double Sec80G          { get; set; }
        public double Sec80CCD1B      { get; set; }
        public double Sec80CCD2       { get; set; }
        public double OtherDeductions { get; set; }
        public double Chapter6A       { get; set; }
        public double TaxableIncome   { get; set; }
        public double TaxOnIncome     { get; set; }
        public double Rebate87A       { get; set; }
        public double TaxAfterRebate  { get; set; }
        public double Surcharge       { get; set; }
        public double Cess            { get; set; }
        public double TotalTax        { get; set; }
    }

    public class IncomeLine
    {
        public string Name     { get; set; } = "";
        public double Annual   { get; set; }
        public double Exempted { get; set; }
        public string RuleRef  { get; set; } = "";
    }
}
