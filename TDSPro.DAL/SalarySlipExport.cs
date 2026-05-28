using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// Salary Slip generator — produces professional payslip in HTML (print-to-PDF)
    /// and Excel (.xlsx) format. No third-party PDF library required.
    /// Layout: A4 landscape — Earnings | Deductions side-by-side + tax summary section.
    /// </summary>
    public static class SalarySlipExport
    {
        private static string R(double v) => v == 0 ? "—" : "₹" + v.ToString("N0");
        private static string MonthYear(int month, int year)
            => new DateTime(year, month, 1).ToString("MMMM yyyy", CultureInfo.InvariantCulture);

        // ════════════════════════════════════════════════════════════════════
        // HTML / PRINT-TO-PDF
        // ════════════════════════════════════════════════════════════════════
        public static string GenerateHtml(
            MonthlySalaryEntry entry,
            AnnualComputation  annual,
            Employee           emp,
            Deductor           deductor,
            string             outputFolder)
        {
            var sb = new System.Text.StringBuilder();
            string monthLabel  = MonthYear(entry.Month, entry.Year);
            var chosen = annual.ChosenRegime == "New" ? annual.NewRegime : annual.OldRegime;

            // Always recompute from fields — don't trust stored GrossPayment
            entry.RecalcGross();

            // ── Earnings rows — only non-zero (except Basic which always shows) ──
            double htmlVarEarnSum   = entry.LineItems.Where(l => l.Category == "varEarn").Sum(l => l.Taxable);
            double htmlOtherLineSum = entry.LineItems.Where(l => l.Category == "other").Sum(l => l.Taxable + l.Exempt);
            double htmlOtherBalance = Math.Max(0, entry.OtherAllowances - htmlVarEarnSum - htmlOtherLineSum);

            var allEarnings = new List<(string Label, double Amount)>
            {
                ("Basic Salary",                  entry.Basic),
                ("House Rent Allowance",           entry.HRA),
                ("Dearness Allowance",             entry.DaAmount),
                ("Special Allowance",              entry.SpecialAllowance),
                ("Medical Allowance",              entry.MedicalAllowance),
                ("Leave Travel Allowance (LTA)",   entry.Lta),
                ("Bonus",                          entry.Bonus),
                ("Commission",                     entry.Commission),
                ("Advance Salary",                 entry.AdvanceSalary),
                ("Arrears",                        entry.Arrears),
                ("Other Allowances",               htmlOtherBalance),
                ("NPS (Employer)",                 entry.NpsEmployer),
                ("Perquisites [taxable]",          entry.PerqTaxable),
                ("Leave Encashment [taxable]",     entry.LeaveEncTaxable),
            };
            // Insert named varEarn rows (e.g. Incentive, Joining Bonus) after Arrears
            foreach (var li in entry.LineItems.Where(l => l.Category == "varEarn" && l.Taxable != 0))
                allEarnings.Insert(allEarnings.FindIndex(x => x.Label == "Other Allowances"), (li.Name, li.Taxable));
            // Insert named other-allowance rows before NPS
            foreach (var ol in entry.LineItems.Where(l => l.Category == "other" && (l.Taxable + l.Exempt) != 0))
                allEarnings.Insert(allEarnings.FindIndex(x => x.Label == "NPS (Employer)"), (ol.Name, ol.Taxable + ol.Exempt));
            var earnings = allEarnings.Where(x => x.Amount != 0 || x.Label == "Basic Salary").ToList();

            // ── Deduction rows — only non-zero ───────────────────────────────
            int    htmlLopDays   = entry.LopDays;
            double htmlLopAmount = htmlLopDays > 0 ? Math.Round(entry.Basic * htmlLopDays / 30.0, 0) : 0;
            int    htmlDim       = DateTime.DaysInMonth(entry.Year, entry.Month);
            int    htmlDaysPaid  = entry.DaysWorked > 0 ? entry.DaysWorked : htmlDim;

            var allDeductions = new List<(string Label, double Amount)>
            {
                ("Loss of Pay",               htmlLopAmount),
                ("Provident Fund (Employee)", entry.PfEmployee),
                ("VPF / Extra PF",            entry.VPF),
                ("Professional Tax",          entry.ProfessionalTax),
                ("ESI (Employee)",            entry.EsiEmployee),
            };
            foreach (var li in entry.LineItems.Where(l => l.Category == "varDed"))
                allDeductions.Add((li.Name, li.Taxable));
            allDeductions.Add(($"Income Tax ({TDSPro.Common.TaxRules.SalaryTdsSection(entry.FinancialYear)})", entry.TdsDeducted));
            var deductions = allDeductions.Where(x => x.Amount != 0).ToList();

            double grossEarnings   = earnings.Sum(x => x.Amount);
            double totalDeductions = htmlLopAmount + entry.PfEmployee + entry.VPF + entry.ProfessionalTax
                                   + entry.EsiEmployee + entry.VarDedTotal + entry.TdsDeducted;
            double netSalary       = grossEarnings - totalDeductions;

            // Pad both lists to equal length for side-by-side layout
            int maxRows = Math.Max(earnings.Count, deductions.Count);
            while (earnings.Count  < maxRows) earnings.Add(("", 0));
            while (deductions.Count< maxRows) deductions.Add(("", 0));

            sb.Append($@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'>
<title>Salary Slip — {Esc(emp.Name)} — {monthLabel}</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#eee;print-color-adjust:exact;-webkit-print-color-adjust:exact}}
.page{{width:297mm;min-height:210mm;margin:8mm auto;background:#fff;padding:12mm;box-shadow:0 0 8px rgba(0,0,0,.2)}}
/* ── Header ── */
.slip-header{{text-align:center;border-bottom:3px solid #1e3a8a;padding-bottom:10px;margin-bottom:12px}}
.co-name{{font-size:18px;font-weight:700;color:#1e3a8a;letter-spacing:.3px;margin-bottom:3px}}
.co-addr{{font-size:9.5px;color:#555;margin-bottom:2px}}
.co-ids{{font-size:9.5px;color:#374151;font-weight:600;margin-bottom:8px}}
.slip-title-box{{display:inline-block;background:#1e3a8a;color:#fff;padding:4px 28px;border-radius:4px;font-size:13px;font-weight:700;letter-spacing:1.5px;margin-bottom:4px}}
.slip-period{{font-size:10px;color:#6b7280}}
/* ── Employee info ── */
.emp-grid{{display:grid;grid-template-columns:1fr 1fr;gap:6px 20px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;padding:10px 12px;margin-bottom:12px;font-size:10px}}
.emp-row{{display:flex;gap:4px}}
.emp-lbl{{color:#6b7280;width:130px;flex-shrink:0}}
.emp-val{{font-weight:600;color:#111}}
/* ── Days row ── */
.days-bar{{display:grid;grid-template-columns:repeat(2,1fr);background:#1e3a8a;color:#fff;border-radius:5px;padding:7px 14px;margin-bottom:12px;font-size:10px}}
.days-bar span{{text-align:center}}
.days-bar strong{{display:block;font-size:15px;font-weight:700}}
/* ── Earnings / Deductions table ── */
.ed-wrap{{display:grid;grid-template-columns:1fr 1fr;gap:0;border:1px solid #e2e8f0;border-radius:6px;overflow:hidden;margin-bottom:12px;font-size:10px}}
.ed-half{{}}
.ed-hdr{{background:#1e3a8a;color:#fff;padding:5px 10px;font-weight:600;font-size:10px;display:flex;justify-content:space-between}}
.ed-row{{display:flex;justify-content:space-between;padding:4px 10px;border-bottom:1px solid #f0f0f0}}
.ed-row:nth-child(even){{background:#f8fafc}}
.ed-row.blank{{visibility:hidden}}
.ed-row .lbl{{color:#374151}}
.ed-row .amt{{font-weight:500;color:#111;font-variant-numeric:tabular-nums;font-feature-settings:'tnum';min-width:80px;text-align:right}}
.ed-total{{background:#1e3a8a;color:#fff;padding:5px 10px;display:flex;justify-content:space-between;font-weight:700;font-size:11px}}
.ed-divider{{border-left:2px solid #e2e8f0}}
/* ── Net salary bar ── */
.net-bar{{background:linear-gradient(135deg,#0f4c81,#1e3a8a);color:#fff;border-radius:6px;padding:10px 18px;display:flex;justify-content:space-between;align-items:center;margin-bottom:12px}}
.net-bar .label{{font-size:12px;font-weight:600}}
.net-bar .amount{{font-size:24px;font-weight:700;letter-spacing:-0.5px}}
/* ── Tax summary ── */
.tax-wrap{{border:1px solid #e2e8f0;border-radius:6px;overflow:hidden;font-size:10px;margin-bottom:12px}}
.tax-hdr{{background:#0f4c81;color:#fff;padding:5px 12px;font-weight:600;display:flex;justify-content:space-between}}
.tax-table{{width:100%;border-collapse:collapse}}
.tax-table th{{background:#dbeafe;padding:4px 10px;text-align:left;color:#1e3a8a;font-size:9px}}
.tax-table th.right{{text-align:right}}
.tax-table td{{padding:4px 10px;border-bottom:1px solid #f0f0f0;color:#374151}}
.tax-table td.num{{text-align:right;font-variant-numeric:tabular-nums;font-feature-settings:'tnum'}}
.tax-table tr:nth-child(even) td{{background:#f8fafc}}
.tax-table tr.total-row td{{background:#dbeafe;font-weight:700;color:#1e3a8a}}
.regime-tag{{display:inline-block;padding:2px 8px;border-radius:10px;font-size:9px;font-weight:600}}
.regime-old{{background:#fef3c7;color:#92400e}}
.regime-new{{background:#d1fae5;color:#065f46}}
/* ── TDS position ── */
.tds-bar{{display:grid;grid-template-columns:repeat(5,1fr);gap:0;border:1px solid #d1fae5;border-radius:6px;overflow:hidden;margin-bottom:12px;font-size:10px}}
.tds-cell{{text-align:center;padding:7px 6px;background:#f0fdf4}}
.tds-cell:nth-child(even){{background:#dcfce7}}
.tds-cell .tlbl{{color:#166534;font-size:9px;margin-bottom:2px}}
.tds-cell .tval{{font-weight:700;color:#14532d;font-variant-numeric:tabular-nums;font-feature-settings:'tnum'}}
/* ── Footer ── */
.slip-footer{{display:flex;justify-content:space-between;align-items:flex-end;border-top:1px solid #e2e8f0;padding-top:10px;font-size:9px;color:#9ca3af}}
.sig-block{{text-align:center}}
.sig-line{{width:120px;border-top:1px solid #6b7280;margin:24px auto 4px}}
@media print{{body{{background:#fff}}.page{{box-shadow:none;margin:0;padding:10mm;width:100%}}}}
</style></head>
<body><div class='page'>

<!-- HEADER -->
<div class='slip-header'>
  <div class='slip-title-box'>SALARY SLIP</div>
  <div class='co-name'>{Esc(deductor.CompanyName)}</div>
  {(string.IsNullOrWhiteSpace(deductor.Address) ? "" : $"<div class='co-addr'>{Esc(deductor.Address)}{(string.IsNullOrWhiteSpace(deductor.City) ? "" : ", " + Esc(deductor.City))}{(string.IsNullOrWhiteSpace(deductor.State) ? "" : ", " + Esc(deductor.State))}{(string.IsNullOrWhiteSpace(deductor.Pincode) ? "" : " — " + Esc(deductor.Pincode))}</div>")}
  <div class='co-ids'>{(string.IsNullOrWhiteSpace(deductor.Pan) ? "" : $"PAN: {Esc(deductor.Pan)}")} {((!string.IsNullOrWhiteSpace(deductor.Pan) && !string.IsNullOrWhiteSpace(deductor.Tan)) ? "&nbsp;|&nbsp;" : "")} {(string.IsNullOrWhiteSpace(deductor.Tan) ? "" : $"TAN: {Esc(deductor.Tan)}")}</div>
  <div class='slip-period'>Pay Period: {monthLabel}</div>
</div>

<!-- EMPLOYEE INFO -->
<div class='emp-grid'>
  <div class='emp-row'><span class='emp-lbl'>Employee Code</span><span class='emp-val'>{Esc(emp.EmployeeCode)}</span></div>
  <div class='emp-row'><span class='emp-lbl'>PAN</span><span class='emp-val'>{Esc(emp.Pan)}</span></div>
  <div class='emp-row'><span class='emp-lbl'>Employee Name</span><span class='emp-val'>{Esc(emp.Name)}</span></div>
  <div class='emp-row'><span class='emp-lbl'>Date of Joining</span><span class='emp-val'>{Esc(emp.JoinDate)}</span></div>
  <div class='emp-row'><span class='emp-lbl'>Designation</span><span class='emp-val'>{Esc(emp.Designation)}</span></div>
  <div class='emp-row'><span class='emp-lbl'>Tax Regime</span><span class='emp-val'>{Esc(annual.ChosenRegime)} Regime</span></div>
  <div class='emp-row'><span class='emp-lbl'>Department</span><span class='emp-val'>{Esc(emp.Department)}</span></div>
  <div class='emp-row'><span class='emp-lbl'>Bank A/c</span><span class='emp-val'>{Esc(emp.BankAccount)} — {Esc(emp.BankIfsc)}</span></div>
</div>

<!-- DAYS -->
<div class='days-bar'>
  <span><strong>{htmlLopDays}</strong>Loss of Pay Days</span>
  <span><strong>{monthLabel}</strong>Pay Period</span>
</div>

<!-- EARNINGS / DEDUCTIONS -->
<div class='ed-wrap'>
  <div class='ed-half'>
    <div class='ed-hdr'><span>EARNINGS</span><span>Amount</span></div>");

            for (int i = 0; i < maxRows; i++)
            {
                var (el, ea) = earnings[i];
                if (string.IsNullOrEmpty(el)) { sb.Append("<div class='ed-row blank'><span>&nbsp;</span><span>&nbsp;</span></div>"); continue; }
                if (ea == 0 && string.IsNullOrEmpty(el)) continue;
                sb.Append($"<div class='ed-row'><span class='lbl'>{Esc(el)}</span><span class='amt'>{R(ea)}</span></div>");
            }
            sb.Append($"<div class='ed-total'><span>GROSS EARNINGS</span><span>{R(grossEarnings)}</span></div>");

            sb.Append("</div><div class='ed-half ed-divider'><div class='ed-hdr'><span>DEDUCTIONS</span><span>Amount</span></div>");

            for (int i = 0; i < maxRows; i++)
            {
                var (dl, da) = deductions[i];
                if (string.IsNullOrEmpty(dl)) { sb.Append("<div class='ed-row blank'><span>&nbsp;</span><span>&nbsp;</span></div>"); continue; }
                sb.Append($"<div class='ed-row'><span class='lbl'>{Esc(dl)}</span><span class='amt'>{R(da)}</span></div>");
            }
            sb.Append($"<div class='ed-total'><span>TOTAL DEDUCTIONS</span><span>{R(totalDeductions)}</span></div>");
            sb.Append("</div></div>"); // close ed-wrap

            // NET SALARY
            sb.Append($@"<div class='net-bar'>
  <div>
    <div class='label'>NET SALARY PAYABLE</div>
    <div style='font-size:9px;opacity:.8'>(Gross Earnings – Total Deductions)</div>
  </div>
  <div class='amount'>{R(netSalary)}</div>
</div>");

            // TDS POSITION
            sb.Append($@"<div class='tds-bar'>
  <div class='tds-cell'><div class='tlbl'>Annual Tax (chosen)</div><div class='tval'>{R(chosen.TotalTax)}</div></div>
  <div class='tds-cell'><div class='tlbl'>YTD TDS Deducted</div><div class='tval'>{R(annual.YtdTdsDeducted)}</div></div>
  <div class='tds-cell'><div class='tlbl'>Balance Tax</div><div class='tval'>{(annual.BalanceTax < 0 ? "—" : R(annual.BalanceTax))}</div></div>
  <div class='tds-cell'><div class='tlbl'>Months Remaining</div><div class='tval'>{annual.MonthsRemaining}</div></div>
  <div class='tds-cell'><div class='tlbl'>TDS This Month</div><div class='tval'>{R(entry.TdsDeducted)}</div></div>
</div>");

            // FOOTER
            sb.Append($@"<div class='slip-footer'>
  <div>
    <div>This is a computer-generated salary slip and does not require a signature.</div>
    <div>Generated by TDS Pro v3.0 &nbsp;|&nbsp; {TDSPro.Common.TaxRules.ActName(entry.FinancialYear)} &nbsp;|&nbsp; {DateTime.Now:dd-MMM-yyyy HH:mm}</div>
  </div>
  <div class='sig-block'>
    <div class='sig-line'></div>
    <div>Authorised Signatory</div>
  </div>
</div>
</div></body></html>");

            Directory.CreateDirectory(outputFolder);
            string fileName = $"SalarySlip_{emp.EmployeeCode}_{entry.Month:D2}_{entry.Year}.html";
            string path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            return path;
        }

        // Single-value row for salary slip tax section
        private static string SRow(string label, double val)
            => val == 0 ? "" :
               $"<tr><td>{Esc(label)}</td><td class='num'>{R(val)}</td></tr>";

        // FY label from entry month/year
        private static string _fyLabel(TDSPro.DAL.Models.MonthlySalaryEntry e)
        {
            int fyStart = e.Month >= 4 ? e.Year : e.Year - 1;
            return $"{fyStart}-{(fyStart+1).ToString()[^2..]}";
        }

        private static string TaxRow(string label, double oldVal, double newVal)
            => $"<tr><td>{Esc(label)}</td><td class='num'>{R(oldVal)}</td><td class='num'>{R(newVal)}</td></tr>";

        // ════════════════════════════════════════════════════════════════════
        // PDF — proper paginated A4 portrait via QuestPDF
        // ════════════════════════════════════════════════════════════════════
        public static string GeneratePdf(
            MonthlySalaryEntry entry,
            AnnualComputation  annual,
            Employee           emp,
            Deductor           deductor,
            string             outputFolder)
        {
            entry.RecalcGross();
            string monthLabel = MonthYear(entry.Month, entry.Year);

            double pdfVarEarnSum   = entry.LineItems.Where(l => l.Category == "varEarn").Sum(l => l.Taxable);
            double pdfOtherLineSum = entry.LineItems.Where(l => l.Category == "other").Sum(l => l.Taxable + l.Exempt);
            double pdfOtherBalance = Math.Max(0, entry.OtherAllowances - pdfVarEarnSum - pdfOtherLineSum);

            var earnings = new List<(string Label, double Amount)>
            {
                ("Basic Salary",                  entry.Basic),
                ("House Rent Allowance",           entry.HRA),
                ("Dearness Allowance",             entry.DaAmount),
                ("Special Allowance",              entry.SpecialAllowance),
                ("Medical Allowance",              entry.MedicalAllowance),
                ("Leave Travel Allowance (LTA)",   entry.Lta),
                ("Bonus",                          entry.Bonus),
                ("Commission",                     entry.Commission),
                ("Advance Salary",                 entry.AdvanceSalary),
                ("Arrears",                        entry.Arrears),
                ("Other Allowances",               pdfOtherBalance),
                ("NPS (Employer)",                 entry.NpsEmployer),
                ("Perquisites [taxable]",          entry.PerqTaxable),
                ("Leave Encashment [taxable]",     entry.LeaveEncTaxable),
            };
            foreach (var li in entry.LineItems.Where(l => l.Category == "varEarn" && l.Taxable != 0))
                earnings.Insert(earnings.FindIndex(x => x.Label == "Other Allowances"), (li.Name, li.Taxable));
            foreach (var ol in entry.LineItems.Where(l => l.Category == "other" && (l.Taxable + l.Exempt) != 0))
                earnings.Insert(earnings.FindIndex(x => x.Label == "NPS (Employer)"), (ol.Name, ol.Taxable + ol.Exempt));
            earnings = earnings.Where(x => x.Amount != 0 || x.Label == "Basic Salary").ToList();

            int lopDays       = entry.LopDays;
            double lopAmount  = lopDays > 0 ? Math.Round(entry.Basic * lopDays / 30.0, 0) : 0;

            var deductionsList = new List<(string Label, double Amount)>
            {
                ("Loss of Pay",               lopAmount),
                ("Provident Fund (Employee)", entry.PfEmployee),
                ("VPF / Extra PF",            entry.VPF),
                ("Professional Tax",          entry.ProfessionalTax),
                ("ESI (Employee)",            entry.EsiEmployee),
            };
            foreach (var li in entry.LineItems.Where(l => l.Category == "varDed"))
                deductionsList.Add((li.Name, li.Taxable));
            deductionsList.Add(("Income Tax (TDS)", entry.TdsDeducted));
            var deductions = deductionsList.Where(x => x.Amount != 0).ToList();

            double grossTotal = earnings.Sum(e => e.Amount);
            double dedTotal   = lopAmount + entry.PfEmployee + entry.VPF + entry.ProfessionalTax
                              + entry.EsiEmployee + entry.VarDedTotal + entry.TdsDeducted;
            double netPay     = grossTotal - dedTotal;

            // Compose deductor address line + pay period
            var dedAddrParts = new[] { deductor.Address, deductor.City, deductor.State, deductor.Pincode }
                .Where(s => !string.IsNullOrWhiteSpace(s)).ToArray();
            var dedAddress   = string.Join(", ", dedAddrParts);
            int dim         = DateTime.DaysInMonth(entry.Year, entry.Month);
            int daysPaid    = entry.DaysWorked > 0 ? entry.DaysWorked : dim;
            var periodStart  = new DateTime(entry.Year, entry.Month, 1).ToString("dd-MMM-yyyy");
            var periodEnd    = new DateTime(entry.Year, entry.Month, dim).ToString("dd-MMM-yyyy");
            string netInWords = TDSPro.Common.AmountInWords.Rupees(netPay);
            bool draft       = !entry.IsLocked;

            // Build centered header lines
            var idParts = new List<string>();
            if (!string.IsNullOrWhiteSpace(deductor.Pan)) idParts.Add($"PAN: {deductor.Pan}");
            if (!string.IsNullOrWhiteSpace(deductor.Tan)) idParts.Add($"TAN: {deductor.Tan}");
            string idLine = string.Join("  |  ", idParts);
            string coLine = deductor.CompanyName
                + (string.IsNullOrEmpty(dedAddress) ? "" : "\n" + dedAddress)
                + (string.IsNullOrEmpty(idLine)     ? "" : "\n" + idLine);

            byte[] pdf = PdfReports.BuildA4(
                title:        "SALARY SLIP",
                subtitle:     coLine,
                centerHeader: true,
                body:         c => c.Column(col =>
                {
                    // Pay period strip
                    col.Item().PaddingBottom(6).AlignCenter()
                        .Text($"Pay Period: {monthLabel}  ({periodStart} to {periodEnd})")
                        .FontSize(9).FontColor(PdfReports.MutedColor);

                    // Employee header block
                    col.Item().PaddingBottom(10).Table(t =>
                    {
                        t.ColumnsDefinition(d => { d.RelativeColumn(); d.RelativeColumn(); d.RelativeColumn(); d.RelativeColumn(); });
                        t.Cell().Element(PdfReports.LabelCell).Text("Employee Code").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(emp.EmployeeCode).Bold();
                        t.Cell().Element(PdfReports.LabelCell).Text("PAN").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(emp.Pan).Bold();

                        t.Cell().Element(PdfReports.LabelCell).Text("Name").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().ColumnSpan(3).Element(PdfReports.LabelCell).Text(emp.Name).Bold();

                        t.Cell().Element(PdfReports.LabelCell).Text("Designation").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(string.IsNullOrEmpty(emp.Designation) ? "—" : emp.Designation);
                        t.Cell().Element(PdfReports.LabelCell).Text("Bank A/c").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(string.IsNullOrEmpty(emp.BankAccount) ? "—" : emp.BankAccount);

                        // LOP row — only when there is loss of pay
                        if (lopDays > 0)
                        {
                            t.Cell().Element(PdfReports.LabelCell).Text("Loss of Pay Days").FontColor(PdfReports.MutedColor).FontSize(9);
                            t.Cell().ColumnSpan(3).Element(PdfReports.LabelCell).Text(lopDays.ToString());
                        }
                    });

                    // Earnings / Deductions side-by-side
                    col.Item().Row(row =>
                    {
                        row.RelativeItem().Column(cc =>
                        {
                            cc.Item().Element(PdfReports.HeaderCell).Text("EARNINGS").Bold().FontColor(PdfReports.PrimaryColor);
                            cc.Item().Table(t =>
                            {
                                t.ColumnsDefinition(d => { d.RelativeColumn(2); d.RelativeColumn(1); });
                                foreach (var (label, amt) in earnings)
                                {
                                    t.Cell().Element(PdfReports.LabelCell).Text(label);
                                    t.Cell().Element(PdfReports.AmountCell).Text(R(amt));
                                }
                                t.Cell().Element(PdfReports.SubtotalCell).Text("Gross Earnings").Bold();
                                t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text(R(grossTotal)).Bold().FontColor(PdfReports.AccentColor);
                            });
                        });
                        row.ConstantItem(20);
                        row.RelativeItem().Column(cc =>
                        {
                            cc.Item().Element(PdfReports.HeaderCell).Text("DEDUCTIONS").Bold().FontColor(PdfReports.ErrorColor);
                            cc.Item().Table(t =>
                            {
                                t.ColumnsDefinition(d => { d.RelativeColumn(2); d.RelativeColumn(1); });
                                if (deductions.Count == 0)
                                {
                                    t.Cell().ColumnSpan(2).Element(PdfReports.LabelCell).Text("(none)").FontColor(PdfReports.MutedColor).Italic();
                                }
                                foreach (var (label, amt) in deductions)
                                {
                                    t.Cell().Element(PdfReports.LabelCell).Text(label);
                                    t.Cell().Element(PdfReports.AmountCell).Text(R(amt));
                                }
                                t.Cell().Element(PdfReports.SubtotalCell).Text("Total Deductions").Bold();
                                t.Cell().Element(PdfReports.SubtotalCell).AlignRight().Text(R(dedTotal)).Bold().FontColor(PdfReports.ErrorColor);
                            });
                        });
                    });

                    // Net Pay block (highlighted)
                    col.Item().PaddingTop(12).Background("#dcfce7").Padding(10).Column(cc =>
                    {
                        cc.Item().Row(r =>
                        {
                            r.RelativeItem().Text("NET PAY").Bold().FontColor(PdfReports.AccentColor).FontSize(14);
                            r.ConstantItem(150).AlignRight().Text(R(netPay)).Bold().FontSize(16).FontColor(PdfReports.AccentColor);
                        });
                        cc.Item().PaddingTop(2).Text(netInWords).FontSize(9).Italic().FontColor("#166534");
                    });

                    // Draft watermark (only when entry not approved/locked)
                    if (draft)
                    {
                        col.Item().PaddingTop(10).Background("#fef2f2").Border(1).BorderColor("#fca5a5").Padding(6).AlignCenter()
                            .Text("⚠ DRAFT — NOT YET APPROVED. Approve the month in Salary Data → Approve & Lock before issuing.").FontSize(9).Bold().FontColor("#991b1b");
                    }

                    // Signature block
                    col.Item().PaddingTop(40).Row(r =>
                    {
                        r.RelativeItem().Column(cc =>
                        {
                            cc.Item().Text("Received the above amount.").FontSize(9).FontColor(PdfReports.MutedColor);
                            cc.Item().PaddingTop(20).BorderTop(1).BorderColor(PdfReports.BorderColor).Width(180).PaddingTop(2).Text("Employee Signature").FontSize(9).FontColor(PdfReports.MutedColor);
                        });
                        r.RelativeItem().Column(cc =>
                        {
                            cc.Item().AlignRight().Text("For " + deductor.CompanyName).FontSize(9).FontColor(PdfReports.MutedColor);
                            cc.Item().PaddingTop(20).AlignRight().BorderTop(1).BorderColor(PdfReports.BorderColor).Width(180).PaddingTop(2).AlignRight().Text("Authorized Signatory").FontSize(9).FontColor(PdfReports.MutedColor);
                        });
                    });

                    col.Item().PaddingTop(10).AlignCenter().Text("This is a computer-generated salary slip and does not require a physical signature.").FontSize(8).Italic().FontColor(PdfReports.MutedColor);
                }));

            Directory.CreateDirectory(outputFolder);
            var safeName = string.Concat((emp.Name ?? "employee").Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(outputFolder, $"SalarySlip_{safeName}_{monthLabel.Replace(' ', '_')}.pdf");
            File.WriteAllBytes(path, pdf);
            return path;
        }

        // ════════════════════════════════════════════════════════════════════
        // EXCEL (.xlsx) — professional styled payslip
        // ════════════════════════════════════════════════════════════════════
        public static string GenerateExcel(
            MonthlySalaryEntry entry,
            AnnualComputation  annual,
            Employee           emp,
            Deductor           deductor,
            string             outputFolder)
        {
            string monthLabel = MonthYear(entry.Month, entry.Year);
            var ch = annual.ChosenRegime == "New" ? annual.NewRegime : annual.OldRegime;
            string regimeName = annual.ChosenRegime + " Regime";

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Salary Slip");

            // ── Page setup ───────────────────────────────────────────────────
            ws.PageSetup.PaperSize       = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 1);
            ws.PageSetup.Margins.Left    = 0.4;
            ws.PageSetup.Margins.Right   = 0.4;
            ws.PageSetup.Margins.Top     = 0.4;
            ws.PageSetup.Margins.Bottom  = 0.4;

            // ── Column widths (exact from reference) ─────────────────────────
            ws.Column(1).Width = 28.66;
            ws.Column(2).Width = 20.66;
            ws.Column(3).Width = 28.66;
            ws.Column(4).Width = 20.66;

            // ── Colour constants ─────────────────────────────────────────────
            var cNavy    = XLColor.FromHtml("#1E3A8A");
            var cDkNavy  = XLColor.FromHtml("#0F4C81");
            var cGreen   = XLColor.FromHtml("#166534");
            var cLtGreen = XLColor.FromHtml("#DCFCE7");
            var cLtBlue  = XLColor.FromHtml("#F8FAFC");
            var cWhite   = XLColor.White;
            var cGray    = XLColor.FromHtml("#94A3B8");
            var cBlack   = XLColor.Black;

            // ── Helpers ───────────────────────────────────────────────────────
            string Rv(double v) => v == 0 ? "—" : "₹" + v.ToString("N0");

            // Style a single cell
            void Sty(IXLCell cell, XLColor bg, XLColor fg, bool bold, int sz=9,
                     XLAlignmentHorizontalValues ha = XLAlignmentHorizontalValues.Left)
            {
                cell.Style.Fill.BackgroundColor = bg;
                cell.Style.Font.FontColor       = fg;
                cell.Style.Font.Bold            = bold;
                cell.Style.Font.FontSize        = sz;
                cell.Style.Alignment.Horizontal = ha;
                cell.Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                cell.Style.Alignment.WrapText   = false;
            }

            // Style a range
            void StyR(IXLRange rng, XLColor bg, XLColor fg, bool bold, int sz=9,
                      XLAlignmentHorizontalValues ha = XLAlignmentHorizontalValues.Left)
            {
                foreach (var c in rng.Cells()) Sty(c, bg, fg, bold, sz, ha);
            }

            // Full-width section header (A:D merged)
            void SecHdr4(int row, string txt, XLColor bg, int sz=9)
            {
                ws.Range(row,1,row,4).Merge();
                ws.Cell(row,1).Value = txt;
                StyR(ws.Range(row,1,row,4), bg, cWhite, true, sz, XLAlignmentHorizontalValues.Left);
                ws.Cell(row,1).Style.Alignment.Indent = 1;
                ws.Row(row).Height = 18;
            }

            // Split header (A:B | C:D)
            void SecHdr2(int row, string t1, string t2, XLColor bg, double ht=18)
            {
                ws.Range(row,1,row,2).Merge();
                ws.Range(row,3,row,4).Merge();
                ws.Cell(row,1).Value = t1; ws.Cell(row,3).Value = t2;
                StyR(ws.Range(row,1,row,2), bg, cWhite, true, 9, XLAlignmentHorizontalValues.Left);
                StyR(ws.Range(row,3,row,4), bg, cWhite, true, 9, XLAlignmentHorizontalValues.Left);
                ws.Cell(row,1).Style.Alignment.Indent = 1;
                ws.Cell(row,3).Style.Alignment.Indent = 1;
                ws.Row(row).Height = ht;
            }

            // 4-column data row: labelA | valB | labelC | valD
            void DataRow4(int row, string la, string va, string lc, string vd, bool shade)
            {
                var bg = shade ? cLtBlue : cWhite;
                ws.Cell(row,1).Value = la; Sty(ws.Cell(row,1), bg, cBlack, false);
                ws.Cell(row,2).Value = va; Sty(ws.Cell(row,2), bg, cBlack, true, 9, XLAlignmentHorizontalValues.Right);
                ws.Cell(row,3).Value = lc; Sty(ws.Cell(row,3), bg, cBlack, false);
                ws.Cell(row,4).Value = vd; Sty(ws.Cell(row,4), bg, cBlack, true, 9, XLAlignmentHorizontalValues.Right);
                ws.Row(row).Height = 16.05;
            }

            // Side-by-side earnings/deductions row
            void EdRow(int row, string el, string ev, string dl, string dv, bool shade)
            {
                var bg = shade ? cLtGreen : cWhite;
                ws.Cell(row,1).Value = el; Sty(ws.Cell(row,1), bg, cBlack, false);
                ws.Cell(row,2).Value = ev; Sty(ws.Cell(row,2), bg, cBlack, false, 9, XLAlignmentHorizontalValues.Right);
                ws.Cell(row,3).Value = dl; Sty(ws.Cell(row,3), bg, cBlack, false);
                ws.Cell(row,4).Value = dv; Sty(ws.Cell(row,4), bg, cBlack, false, 9, XLAlignmentHorizontalValues.Right);
                ws.Row(row).Height = 16.05;
            }

            int r = 1;

            // ── Row 1-2: Company header (merged A1:D2) ────────────────────────
            ws.Range(r,1,r+1,4).Merge();
            ws.Cell(r,1).Value = deductor.CompanyName + "\n" + "TAN: " + deductor.Tan
                + "  |  " + deductor.Address;
            StyR(ws.Range(r,1,r+1,4), cNavy, cWhite, true, 11, XLAlignmentHorizontalValues.Center);
            ws.Cell(r,1).Style.Alignment.WrapText = true;
            ws.Cell(r,1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            ws.Row(r).Height = 19.2; ws.Row(r+1).Height = 19.2;
            r += 2;

            // ── Row 3: Slip title ─────────────────────────────────────────────
            ws.Range(r,1,r,4).Merge();
            ws.Cell(r,1).Value = $"SALARY SLIP — {monthLabel.ToUpper()}";
            StyR(ws.Range(r,1,r,4), cDkNavy, cWhite, true, 10, XLAlignmentHorizontalValues.Center);
            ws.Row(r).Height = 16.05; r++;

            // ── Row 4: spacer ─────────────────────────────────────────────────
            ws.Row(r).Height = 6; r++;

            // ── Row 5: Employee Information header ────────────────────────────
            SecHdr4(r, "EMPLOYEE INFORMATION", cDkNavy); r++;

            // ── Rows 6-9: Employee details ────────────────────────────────────
            DataRow4(r, "Employee Code", emp.EmployeeCode, "Employee Name", emp.Name, false); r++;
            DataRow4(r, "PAN", emp.Pan, "Date of Joining", emp.JoinDate, true); r++;
            DataRow4(r, "Designation", emp.Designation, "Department", emp.Department, false); r++;
            DataRow4(r, "Tax Regime", regimeName, "Bank A/c",
                string.IsNullOrEmpty(emp.BankAccount) ? "—" : emp.BankAccount, true); r++;

            // ── Row 10: spacer ────────────────────────────────────────────────
            ws.Row(r).Height = 6; r++;

            // ── Row 11: Earnings | Deductions headers ─────────────────────────
            SecHdr2(r, "EARNINGS  (Monthly ₹)", "DEDUCTIONS  (Monthly ₹)", cNavy); r++;

            // ── Rows 12+: Side-by-side earnings / deductions ─────────────────
            entry.RecalcGross();
            int    xlLopDays   = entry.LopDays;
            double xlLopAmount = xlLopDays > 0 ? Math.Round(entry.Basic * xlLopDays / 30.0, 0) : 0;
            double ded   = xlLopAmount + entry.PfEmployee + entry.VPF + entry.ProfessionalTax
                         + entry.EsiEmployee + entry.VarDedTotal + entry.TdsDeducted;

            double xlVarEarnSum   = entry.LineItems.Where(l => l.Category == "varEarn").Sum(l => l.Taxable);
            double xlOtherLineSum = entry.LineItems.Where(l => l.Category == "other").Sum(l => l.Taxable + l.Exempt);
            double xlOtherBalance = Math.Max(0, entry.OtherAllowances - xlVarEarnSum - xlOtherLineSum);

            var earns = new List<(string, double)> {
                ("Basic Salary",               entry.Basic),
                ("House Rent Allowance",        entry.HRA),
                ("Dearness Allowance",          entry.DaAmount),
                ("Special Allowance",           entry.SpecialAllowance),
                ("Medical Allowance",           entry.MedicalAllowance),
                ("Leave Travel Allowance (LTA)",entry.Lta),
                ("Bonus",                       entry.Bonus),
                ("Commission",                  entry.Commission),
                ("Advance Salary",              entry.AdvanceSalary),
                ("Arrears",                     entry.Arrears),
                ("Other Allowances",            xlOtherBalance),
                ("NPS (Employer)",              entry.NpsEmployer),
                ("Perquisites (Taxable)",       entry.PerqTaxable),
                ("Leave Enc. (Taxable)",        entry.LeaveEncTaxable),
            };
            foreach (var li in entry.LineItems.Where(l => l.Category == "varEarn" && l.Taxable != 0))
                earns.Insert(earns.FindIndex(x => x.Item1 == "Other Allowances"), (li.Name, li.Taxable));
            foreach (var ol in entry.LineItems.Where(l => l.Category == "other" && (l.Taxable + l.Exempt) != 0))
                earns.Insert(earns.FindIndex(x => x.Item1 == "NPS (Employer)"), (ol.Name, ol.Taxable + ol.Exempt));
            earns = earns.Where(x => x.Item2 != 0 || x.Item1 == "Basic Salary").ToList();

            var dedsList = new List<(string, double)> {
                ("Loss of Pay",          xlLopAmount),
                ("Provident Fund",       entry.PfEmployee),
                ("VPF / Extra PF",       entry.VPF),
                ("Professional Tax",     entry.ProfessionalTax),
                ("ESI (Employee)",       entry.EsiEmployee),
            };
            foreach (var li in entry.LineItems.Where(l => l.Category == "varDed"))
                dedsList.Add((li.Name, li.Taxable));
            dedsList.Add(($"Income Tax ({TDSPro.Common.TaxRules.SalaryTdsSection(entry.FinancialYear)})", entry.TdsDeducted));
            var deds = dedsList.Where(x => x.Item2 != 0).ToList();
            double gross = earns.Sum(x => x.Item2);
            double net   = gross - ded;

            int maxRows = Math.Max(earns.Count, deds.Count);
            for (int i=0; i<maxRows; i++)
            {
                string el = i<earns.Count ? earns[i].Item1 : "";
                string ev = i<earns.Count ? Rv(earns[i].Item2) : "";
                string dl = i<deds.Count  ? deds[i].Item1  : "";
                string dv = i<deds.Count  ? Rv(deds[i].Item2)  : "";
                EdRow(r, el, ev, dl, dv, i%2==0); r++;
            }

            // ── Row 20: Gross | Total deductions ─────────────────────────────
            SecHdr2(r, $"GROSS EARNINGS   ₹{gross:N0}", $"TOTAL DEDUCTIONS   ₹{ded:N0}", cNavy, 19.95); r++;

            // ── Row 21: spacer ────────────────────────────────────────────────
            ws.Row(r).Height = 6; r++;

            // ── Row 22: Net Salary ────────────────────────────────────────────
            ws.Range(r,1,r,4).Merge();
            ws.Cell(r,1).Value = $"NET SALARY PAYABLE   ₹{net:N0}";
            StyR(ws.Range(r,1,r,4), cNavy, cWhite, true, 11, XLAlignmentHorizontalValues.Center);
            ws.Row(r).Height = 22.05; r++;

            // ── Row 23: spacer ────────────────────────────────────────────────
            ws.Row(r).Height = 6; r++;

            // ── Row 24: TDS Position header ───────────────────────────────────
            SecHdr4(r, "TDS POSITION", cGreen); r++;

            // ── Row 25: TDS sub-headers ───────────────────────────────────────
            ws.Range(r,1,r,2).Merge(); ws.Range(r,3,r,4).Merge();
            ws.Cell(r,1).Value = "Annual Tax  /  YTD TDS";
            ws.Cell(r,3).Value = "Balance  /  This Month";
            StyR(ws.Range(r,1,r,2), cWhite, cGreen, true, 9, XLAlignmentHorizontalValues.Center);
            StyR(ws.Range(r,3,r,4), cWhite, cGreen, true, 9, XLAlignmentHorizontalValues.Center);
            ws.Row(r).Height = 16.05; r++;

            // ── Row 26: TDS values ────────────────────────────────────────────
            ws.Range(r,1,r,2).Merge(); ws.Range(r,3,r,4).Merge();
            ws.Cell(r,1).Value = $"₹{ch.TotalTax:N0}  /  ₹{annual.YtdTdsDeducted:N0}";
            ws.Cell(r,3).Value = $"{(annual.BalanceTax < 0 ? "—" : "₹"+annual.BalanceTax.ToString("N0"))}  /  ₹{entry.TdsDeducted:N0}";
            StyR(ws.Range(r,1,r,2), cLtGreen, cGreen, true, 9, XLAlignmentHorizontalValues.Center);
            StyR(ws.Range(r,3,r,4), cLtGreen, cGreen, true, 9, XLAlignmentHorizontalValues.Center);
            ws.Row(r).Height = 18; r++;

            // ── Row 27: spacer ────────────────────────────────────────────────
            ws.Row(r).Height = 6; r++;

            // ── Footer ───────────────────────────────────────────────────────
            ws.Range(r,1,r,4).Merge();
            ws.Cell(r,1).Value = $"Computer generated — TDS Pro v3.0  |  {TDSPro.Common.TaxRules.ActName(entry.FinancialYear)}  |  {DateTime.Now:dd-MMM-yyyy HH:mm}";
            Sty(ws.Cell(r,1), cWhite, cGray, false, 8, XLAlignmentHorizontalValues.Center);
            ws.Cell(r,1).Style.Font.Italic = true;
            ws.Row(r).Height = 13.95;

            // ── Global font ───────────────────────────────────────────────────
            ws.RangeUsed().Style.Font.FontName = "Segoe UI";

            Directory.CreateDirectory(outputFolder);
            string fileName = $"SalarySlip_{emp.EmployeeCode}_{entry.Month:D2}_{entry.Year}.xlsx";
            string path = Path.Combine(outputFolder, fileName);
            wb.SaveAs(path);
            return path;
        }


        // ════════════════════════════════════════════════════════════════════
        // ANNUAL TAX COMPUTATION — standalone download
        // ════════════════════════════════════════════════════════════════════

        public static string GenerateAnnualHtml(
            AnnualComputation annual,
            Employee emp,
            string fy,
            string outputFolder,
            Deductor? deductor = null,
            EmployeeYearSummary? yearSummary = null)
        {
            var o = annual.OldRegime;
            var n = annual.NewRegime;
            bool chosenOld = annual.ChosenRegime == "Old";
            var chosen = chosenOld ? o : n;
            var ss = emp.Salary ?? new SalaryStructure();

            // ── helper: two-column amount cells ──────────────────────────────
            string TwoCol(double ov, double nv, bool bold = false, bool chosen2 = false) {
                string b = bold ? ";font-weight:700" : "";
                string co = chosen2 ? " chosen" : "";
                return $"<td class='num{(chosenOld?co:"")}{(bold?" b":"")}'style='color:#374151{b}'>{R(ov)}</td>" +
                       $"<td class='num{(!chosenOld?co:"")}{(bold?" b":"")}'style='color:#374151{b}'>{R(nv)}</td>";
            }
            string BandRow(string label, string icon = "") =>
                $"<tr class='band'><td colspan='3'>{icon} {Esc(label)}</td></tr>";
            string DataRow(string label, double ov, double nv, bool indent = false, bool total = false, bool subtotal = false, string note = "") {
                string cls = total ? " class='tot'" : subtotal ? " class='sub'" : "";
                string pad = indent ? "style='padding-left:26px;color:#6b7280'" : "";
                string nb = note.Length > 0 ? $" <small style='color:#94a3b8;font-size:8.5px'>[{Esc(note)}]</small>" : "";
                return $"<tr{cls}><td {pad}>{Esc(label)}{nb}</td>{TwoCol(ov, nv, total || subtotal, total)}</tr>";
            }
            string SepRow() => "<tr class='sep'><td colspan='3'></td></tr>";
            // render a value with sign (negative shown in red with parens)
            string Signed(double v) => v == 0 ? "—" : v < 0 ? $"<span style='color:#dc2626'>(₹{Math.Abs(v):N0})</span>" : $"₹{v:N0}";

            var sb = new System.Text.StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'>
<title>Annual Tax Computation — {Esc(emp.Name)} — {Esc(fy)}</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#eee;print-color-adjust:exact;-webkit-print-color-adjust:exact;font-size:10.5px}}
.page{{width:190mm;margin:10mm auto;background:#fff;padding:12mm;box-shadow:0 0 8px rgba(0,0,0,.2)}}
.hdr{{border-bottom:3px solid #1e3a8a;padding-bottom:8px;margin-bottom:10px;display:flex;justify-content:space-between;align-items:flex-end}}
.co{{font-size:13px;font-weight:700;color:#1e3a8a}}
.emp-box{{background:#f8fafc;border:1px solid #e2e8f0;border-radius:5px;padding:8px 12px;margin-bottom:10px;display:grid;grid-template-columns:1fr 1fr;gap:3px 20px;font-size:9.5px}}
.emp-row{{display:flex;gap:4px}}.lbl{{color:#6b7280;width:115px;flex-shrink:0}}.val{{font-weight:600;color:#111}}
table{{width:100%;border-collapse:collapse}}
.col-hdr{{background:#1e3a8a;color:#fff;padding:6px 10px;font-weight:600;font-size:9px;text-align:center}}
.col-hdr.title{{text-align:left;font-size:11px}}
td{{padding:4px 10px;border-bottom:1px solid #f0f2f5;color:#374151;vertical-align:middle}}
td.num{{text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}}
td.b{{font-weight:700}}
tr:nth-child(even) td{{background:#f9fafb}}
tr.sep td{{padding:1px 0;border:none;background:#e2e8f0;height:1px}}
tr.band td{{background:#1e3a8a;color:#fff;font-weight:700;font-size:9.5px;padding:5px 10px;letter-spacing:.2px}}
tr.sub td{{background:#dbeafe;font-weight:700;color:#1e3a8a;border-top:1px solid #bfdbfe}}
tr.tot td{{background:#1e3a8a!important;font-weight:700;color:#fff;font-size:11px;border-top:2px solid #1e4080}}
tr.tot td.num{{color:#fff}}
tr.chosen td.num{{font-weight:700}}
.badge{{display:inline-block;padding:2px 7px;border-radius:10px;font-size:8.5px;font-weight:600;margin-left:5px}}
.old-badge{{background:#fef3c7;color:#92400e}}.new-badge{{background:#d1fae5;color:#065f46}}
.tds-grid{{display:grid;grid-template-columns:repeat(3,1fr);gap:0;border:1px solid #bfdbfe;border-radius:5px;overflow:hidden;margin-top:10px;font-size:10px}}
.tc{{text-align:center;padding:7px 4px;background:#eff6ff}}
.tc:nth-child(odd){{background:#dbeafe}}
.tc .tl{{color:#1e40af;font-size:8px;margin-bottom:2px}}.tc .tv{{font-weight:700;color:#1e3a8a;font-variant-numeric:tabular-nums}}
.footer{{font-size:8px;color:#9ca3af;text-align:center;margin-top:12px;border-top:1px solid #e2e8f0;padding-top:6px}}
@media print{{body{{background:#fff}}.page{{box-shadow:none;margin:0;padding:8mm;width:100%}}}}
</style></head><body><div class='page'>

<div style='text-align:center;background:#1e3a8a;color:#fff;padding:9px 12px;border-radius:4px 4px 0 0;margin-bottom:0'>
  <div style='font-size:14px;font-weight:700;letter-spacing:.3px'>{Esc(deductor?.CompanyName ?? "")}</div>
  {(string.IsNullOrEmpty(deductor?.Tan) ? "" : $"<div style='font-size:9px;opacity:.8;margin-top:2px'>TAN: {Esc(deductor.Tan)}</div>")}
</div>
<div class='hdr' style='margin-top:0;padding-top:8px'>
  <div>
    <div class='co'>Annual Tax Computation — FY {Esc(fy)}</div>
    <div style='font-size:8.5px;color:#6b7280;margin-top:2px'>TDS Pro v3.0 &nbsp;|&nbsp; {TDSPro.Common.TaxRules.ActName(fy)}</div>
  </div>
  <div style='text-align:right;font-size:8.5px;color:#6b7280'>{DateTime.Now:dd-MMM-yyyy HH:mm}</div>
</div>

<div class='emp-box'>
  <div class='emp-row'><span class='lbl'>Employee</span><span class='val'>{Esc(emp.Name)}</span></div>
  <div class='emp-row'><span class='lbl'>PAN</span><span class='val'>{Esc(emp.Pan)}</span></div>
  <div class='emp-row'><span class='lbl'>Employee Code</span><span class='val'>{Esc(emp.EmployeeCode)}</span></div>
  <div class='emp-row'><span class='lbl'>Designation</span><span class='val'>{Esc(emp.Designation)}</span></div>
  <div class='emp-row'><span class='lbl'>Chosen Regime</span>
    <span class='val'>{Esc(annual.ChosenRegime)} Regime
      <span class='badge {(annual.ChosenRegime=="New"?"new":"old")}-badge'>Chosen ✓</span>
    </span>
  </div>
  <div class='emp-row'><span class='lbl'>Annual Tax (chosen)</span><span class='val'>₹{chosen.TotalTax:N0}</span></div>
</div>

<table>
<thead><tr>
  <td class='col-hdr title' style='width:54%'>Particulars</td>
  <td class='col-hdr' style='width:23%'>Old Regime<span class='badge old-badge{(chosenOld?" chosen":"")}'>{(chosenOld?"✓ Chosen":"")}</span></td>
  <td class='col-hdr' style='width:23%'>New Regime<span class='badge new-badge{(!chosenOld?" chosen":"")}'>{(!chosenOld?"✓ Chosen":"")}</span></td>
</tr></thead>
<tbody>");

            // ── A. GROSS SALARY ───────────────────────────────────────────────
            sb.Append(BandRow("A. Gross Salary"));
            // Fixed components
            if (ss.Basic > 0)            sb.Append(DataRow("Basic Salary",          ss.Basic*12,           ss.Basic*12,           indent:true));
            if (ss.Hra > 0)              sb.Append(DataRow("House Rent Allowance",   ss.Hra*12,             ss.Hra*12,             indent:true));
            if (ss.Da > 0)               sb.Append(DataRow("Dearness Allowance",     ss.Da*12,              ss.Da*12,              indent:true));
            if (ss.SpecialAllowance > 0) sb.Append(DataRow("Special Allowance",      ss.SpecialAllowance*12,ss.SpecialAllowance*12,indent:true));
            if (ss.MedicalAllowance > 0) sb.Append(DataRow("Medical Allowance",      ss.MedicalAllowance*12,ss.MedicalAllowance*12,indent:true));
            if (ss.Lta > 0)              sb.Append(DataRow("Leave Travel Allowance", ss.Lta*12,             ss.Lta*12,             indent:true));
            // Named components
            foreach (var c in ss.Components.Where(c => c.Received > 0))
                sb.Append(DataRow(c.Name, c.Received*12, c.Received*12, indent:true, note:c.RuleRef));
            // Variable pay
            if (ss.AnnualBonus > 0)     sb.Append(DataRow("Performance Bonus",   ss.AnnualBonus,   ss.AnnualBonus,   indent:true));
            if (ss.AnnualIncentive > 0) sb.Append(DataRow("Sales / Incentive",   ss.AnnualIncentive,ss.AnnualIncentive,indent:true));
            sb.Append(DataRow("Gross Salary (A)", o.GrossSalary, n.GrossSalary, subtotal:true));

            // ── B. EXEMPTIONS (Sec 10) ────────────────────────────────────────
            bool hasExemptions = o.HraExemption > 0 || n.HraExemption > 0
                || o.Sec10Items.Any(x => x.OldRegime > 0 || x.NewRegime > 0);
            if (hasExemptions)
            {
                sb.Append(SepRow());
                sb.Append(BandRow("B. Less: Exemptions u/s 10"));
                if (o.HraExemption > 0 || n.HraExemption > 0)
                    sb.Append(DataRow("HRA Exemption", o.HraExemption, n.HraExemption, indent:true, note:"Sec 10(13A)"));
                foreach (var item in o.Sec10Items.Where(x => x.Name != "HRA"))
                    sb.Append(DataRow(item.Name, item.OldRegime, item.NewRegime, indent:true, note:item.RuleRef));
                double totalExOld = o.HraExemption + o.Sec10Items.Where(x=>x.Name!="HRA").Sum(x=>x.OldRegime);
                double totalExNew = n.HraExemption + o.Sec10Items.Where(x=>x.Name!="HRA").Sum(x=>x.NewRegime);
                sb.Append(DataRow("Total Exemptions (B)", totalExOld, totalExNew, subtotal:true));
            }

            // ── C. NET TAXABLE SALARY ─────────────────────────────────────────
            sb.Append(SepRow());
            sb.Append(BandRow("C. Net Taxable Salary (A − B)"));
            double netOld = o.GrossSalary - (o.HraExemption + o.Sec10Items.Where(x=>x.Name!="HRA").Sum(x=>x.OldRegime));
            double netNew = n.GrossSalary - (n.HraExemption + o.Sec10Items.Where(x=>x.Name!="HRA").Sum(x=>x.NewRegime));
            sb.Append(DataRow("Net Taxable Salary (C)", netOld, netNew, subtotal:true));

            // ── D. DEDUCTIONS ─────────────────────────────────────────────────
            sb.Append(SepRow());
            sb.Append(BandRow("D. Less: Deductions"));
            sb.Append(DataRow("Standard Deduction", o.StandardDeduction, n.StandardDeduction, indent:true, note:"u/s 16(ia)"));
            if (o.ProfTaxDeduction > 0 || n.ProfTaxDeduction > 0)
                sb.Append(DataRow("Professional Tax", o.ProfTaxDeduction, n.ProfTaxDeduction, indent:true, note:"u/s 16(iii)"));
            // Chapter VI-A (old regime only — new shows zero)
            bool hasChap6A = o.Chapter6A > 0;
            if (hasChap6A)
            {
                sb.Append(DataRow("Chapter VI-A Deductions", o.Chapter6A, 0, indent:true, note:"80C/80D/80G etc."));
            }
            if (o.NpsEmployer80CCD2 > 0 || n.NpsEmployer80CCD2 > 0)
                sb.Append(DataRow("NPS Employer 80CCD(2)", o.NpsEmployer80CCD2, n.NpsEmployer80CCD2, indent:true));
            double totalDedOld = o.StandardDeduction + o.ProfTaxDeduction + o.Chapter6A + o.NpsEmployer80CCD2;
            double totalDedNew = n.StandardDeduction + n.ProfTaxDeduction + n.Chapter6A + n.NpsEmployer80CCD2;
            sb.Append(DataRow("Total Deductions (D)", totalDedOld, totalDedNew, subtotal:true));

            // ── E. OTHER SOURCES ──────────────────────────────────────────────
            if (o.IncomeOtherSources > 0 || n.IncomeOtherSources > 0)
            {
                sb.Append(SepRow());
                sb.Append(BandRow("E. Add: Income from Other Sources"));
                sb.Append(DataRow("Interest / Other Income", o.IncomeOtherSources, n.IncomeOtherSources, indent:true));
            }

            // ── F. NET TAXABLE INCOME ─────────────────────────────────────────
            sb.Append(SepRow());
            sb.Append($"<tr class='sub'><td><strong>Net Taxable Income (C − D + E)</strong></td>{TwoCol(o.TotalIncome, n.TotalIncome, bold:true, chosen2:true)}</tr>");

            // ── G. TAX COMPUTATION ────────────────────────────────────────────
            sb.Append(SepRow());
            sb.Append(BandRow("F. Tax Computation"));
            sb.Append(DataRow("Tax on Income (Slab)", o.TaxOnIncome, n.TaxOnIncome, indent:true));
            if (o.Rebate87A > 0 || n.Rebate87A > 0)
                sb.Append(DataRow("Less: Rebate u/s 87A", o.Rebate87A, n.Rebate87A, indent:true));
            sb.Append(DataRow("Tax After Rebate", o.TaxAfterRebate, n.TaxAfterRebate, indent:true));
            if (o.Surcharge > 0 || n.Surcharge > 0)
                sb.Append(DataRow("Add: Surcharge", o.Surcharge, n.Surcharge, indent:true));
            if (o.Cess > 0 || n.Cess > 0)
                sb.Append(DataRow("Add: Health & Education Cess (4%)", o.Cess, n.Cess, indent:true));
            sb.Append($"<tr class='tot'><td>TOTAL TAX PAYABLE</td>{TwoCol(o.TotalTax, n.TotalTax, bold:true, chosen2:true)}</tr>");

            sb.Append("</tbody></table>");

            // ── MONTHLY BREAKDOWN ─────────────────────────────────────────────
            var fyMonths = new[]{ 4,5,6,7,8,9,10,11,12,1,2,3 };
            var monthNames = new[]{ "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };
            var runs = yearSummary?.MonthlyRuns ?? new Dictionary<int, PayrollRun>();
            if (runs.Count > 0)
            {
                sb.Append(@"
<div style='margin-top:14px'>
<div style='background:#1e3a8a;color:#fff;font-weight:700;font-size:10px;padding:6px 10px;border-radius:4px 4px 0 0;letter-spacing:.3px'>Monthly Salary Statement — April to March</div>
<table style='width:100%;border-collapse:collapse;font-size:9.5px'>
<thead><tr style='background:#dbeafe;color:#1e3a8a;font-weight:600;font-size:9px'>
  <th style='padding:5px 8px;text-align:left'>Month</th>
  <th style='padding:5px 8px;text-align:right'>Gross</th>
  <th style='padding:5px 8px;text-align:right'>HRA Ex.</th>
  <th style='padding:5px 8px;text-align:right'>Std. Ded.</th>
  <th style='padding:5px 8px;text-align:right'>Taxable</th>
  <th style='padding:5px 8px;text-align:right'>TDS</th>
  <th style='padding:5px 8px;text-align:right'>PF</th>
  <th style='padding:5px 8px;text-align:right'>PT</th>
  <th style='padding:5px 8px;text-align:right'>Net Pay</th>
</tr></thead><tbody>");
                double totGross=0,totHra=0,totStd=0,totTax=0,totTds=0,totPf=0,totPt=0,totNet=0;
                for (int mi=0; mi<12; mi++)
                {
                    int m = fyMonths[mi];
                    string bg = mi%2==0 ? "#fff" : "#f8fafc";
                    if (runs.TryGetValue(m, out var pr))
                    {
                        double stdM = pr.StandardDeduction > 0 ? pr.StandardDeduction : (chosenOld ? o.StandardDeduction : n.StandardDeduction) / 12.0;
                        totGross+=pr.GrossSalary; totHra+=pr.HraExemption; totStd+=stdM;
                        totTax+=pr.TaxableIncome; totTds+=pr.TdsDeducted;
                        totPf+=pr.PfEmployee; totPt+=pr.ProfessionalTax; totNet+=pr.NetPay;
                        string M(double v) => v==0?"—":"₹"+v.ToString("N0");
                        sb.Append($"<tr style='background:{bg};border-bottom:1px solid #f0f2f5'>" +
                            $"<td style='padding:4px 8px;font-weight:600;color:#374151'>{monthNames[mi]}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums'>{M(pr.GrossSalary)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#6b7280'>{M(pr.HraExemption)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#6b7280'>{M(stdM)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums'>{M(pr.TaxableIncome)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#dc2626;font-weight:600'>{M(pr.TdsDeducted)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#6b7280'>{M(pr.PfEmployee)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#6b7280'>{M(pr.ProfessionalTax)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#15803d;font-weight:600'>{M(pr.NetPay)}</td>" +
                            "</tr>");
                    }
                    else
                    {
                        sb.Append($"<tr style='background:{bg};border-bottom:1px solid #f0f2f5'>" +
                            $"<td style='padding:4px 8px;color:#9ca3af'>{monthNames[mi]}</td>" +
                            $"<td colspan='8' style='padding:4px 8px;color:#d1d5db;font-size:9px'>— not entered —</td></tr>");
                    }
                }
                // Total row
                string T(double v) => v==0?"₹0":"₹"+v.ToString("N0");
                sb.Append($"<tr style='background:#1e3a8a;color:#fff;font-weight:700;font-size:10px'>" +
                    $"<td style='padding:5px 8px'>Total</td>" +
                    $"<td style='padding:5px 8px;text-align:right;font-variant-numeric:tabular-nums'>{T(totGross)}</td>" +
                    $"<td style='padding:5px 8px;text-align:right;font-variant-numeric:tabular-nums'>{T(totHra)}</td>" +
                    $"<td style='padding:5px 8px;text-align:right;font-variant-numeric:tabular-nums'>{T(totStd)}</td>" +
                    $"<td style='padding:5px 8px;text-align:right;font-variant-numeric:tabular-nums'>{T(totTax)}</td>" +
                    $"<td style='padding:5px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#fca5a5'>{T(totTds)}</td>" +
                    $"<td style='padding:5px 8px;text-align:right;font-variant-numeric:tabular-nums'>{T(totPf)}</td>" +
                    $"<td style='padding:5px 8px;text-align:right;font-variant-numeric:tabular-nums'>{T(totPt)}</td>" +
                    $"<td style='padding:5px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#86efac'>{T(totNet)}</td>" +
                    "</tr>");
                sb.Append("</tbody></table></div>");
            }

            // ── TDS SUMMARY ────────────────────────────────────────────────
            // Use actual total TDS from monthly runs if available (includes all 12 months)
            double totalTdsPaid = runs.Count > 0
                ? runs.Values.Sum(r => r.TdsDeducted)
                : annual.YtdTdsDeducted;
            double balance = chosen.TotalTax - totalTdsPaid;
            sb.Append($@"
<div class='tds-grid' style='margin-top:10px'>
  <div class='tc'><div class='tl'>Tax Payable (Chosen)</div><div class='tv'>₹{chosen.TotalTax:N0}</div></div>
  <div class='tc'><div class='tl'>TDS Paid (YTD)</div><div class='tv'>₹{totalTdsPaid:N0}</div></div>
  <div class='tc'><div class='tl'>Balance Tax</div><div class='tv' style='color:{(balance<0?"#166534":balance>0?"#dc2626":"#1e3a8a")}'>{Signed(balance)}</div></div>
</div>

<div class='footer'>Computer-generated &nbsp;|&nbsp; TDS Pro v3.0 &nbsp;|&nbsp; Not a legal document &nbsp;|&nbsp; {TDSPro.Common.TaxRules.ActName(fy)}</div>
</div></body></html>");

            Directory.CreateDirectory(outputFolder);
            string safeName = string.Concat(emp.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileName = $"Computation_{safeName}({emp.Pan})_{fy.Replace("/","-")}.html";
            string path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            return path;
        }

        // ════════════════════════════════════════════════════════════════════
        // MONTHLY SALARY STATEMENT — separate standalone report
        // ════════════════════════════════════════════════════════════════════

        public static string GenerateMonthlySalaryHtml(
            Employee emp,
            string fy,
            string outputFolder,
            Deductor? deductor = null,
            EmployeeYearSummary? yearSummary = null)
        {
            var runs = yearSummary?.MonthlyRuns ?? new Dictionary<int, PayrollRun>();
            int[] fyM = { 4,5,6,7,8,9,10,11,12,1,2,3 };
            string[] mn = { "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };
            string co = deductor?.CompanyName ?? "";
            string addr = string.Join(", ", new[]{ deductor?.Address, deductor?.City, deductor?.State }.Where(s => !string.IsNullOrWhiteSpace(s)));

            // Determine which pay components have any value across the year
            bool hasBasic   = runs.Values.Any(r => r.Basic   > 0);
            bool hasHra     = runs.Values.Any(r => r.Hra     > 0);
            bool hasDa      = runs.Values.Any(r => r.Da      > 0);
            bool hasSpecial = runs.Values.Any(r => r.Special  > 0);
            bool hasMedical = runs.Values.Any(r => r.Medical  > 0);
            bool hasLta     = runs.Values.Any(r => r.Lta     > 0);
            bool hasOther   = runs.Values.Any(r => r.Other   > 0);
            bool hasPf      = runs.Values.Any(r => r.PfEmployee    > 0);
            bool hasEsi     = runs.Values.Any(r => r.EsiEmployee   > 0);
            bool hasPt      = runs.Values.Any(r => r.ProfessionalTax > 0);
            bool hasTds     = runs.Values.Any(r => r.TdsDeducted   > 0);
            bool hasOtherD  = runs.Values.Any(r => r.OtherDeductions > 0);

            // Build column list: Month, [pay cols...], Gross, [ded cols...], Total Ded, Net Pay
            var payCols  = new List<(string label, Func<PayrollRun,double> fn)>();
            var dedCols  = new List<(string label, Func<PayrollRun,double> fn)>();
            if (hasBasic)   payCols.Add(("Basic",        r => r.Basic));
            if (hasHra)     payCols.Add(("HRA",          r => r.Hra));
            if (hasDa)      payCols.Add(("DA",           r => r.Da));
            if (hasSpecial) payCols.Add(("Special Allw", r => r.Special));
            if (hasMedical) payCols.Add(("Medical",      r => r.Medical));
            if (hasLta)     payCols.Add(("LTA",          r => r.Lta));
            if (hasOther)   payCols.Add(("Other",        r => r.Other));
            if (hasPf)      dedCols.Add(("PF",           r => r.PfEmployee));
            if (hasEsi)     dedCols.Add(("ESI",          r => r.EsiEmployee));
            if (hasPt)      dedCols.Add(("PT",           r => r.ProfessionalTax));
            if (hasTds)     dedCols.Add(("TDS",          r => r.TdsDeducted));
            if (hasOtherD)  dedCols.Add(("Other Ded",    r => r.OtherDeductions));

            int totalCols = 1 + payCols.Count + 1 + dedCols.Count + 1 + 1; // Month + pays + Gross + deds + TotalDed + Net

            string C(double v) => v == 0 ? "—" : "₹" + v.ToString("N0");

            var sb2 = new System.Text.StringBuilder();
            sb2.Append($@"<!DOCTYPE html><html><head><meta charset='utf-8'>
<title>Monthly Salary Statement — {Esc(emp.Name)} — FY {fy}</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:Arial,sans-serif;font-size:10px;color:#1a1a2e;background:#fff;padding:20px}}
.hdr{{text-align:center;margin-bottom:14px}}
.hdr .co{{font-size:14px;font-weight:700;color:#1e3a8a;letter-spacing:.3px}}
.hdr .addr{{font-size:9px;color:#64748b;margin-top:2px}}
.hdr .title{{margin-top:10px;font-size:12px;font-weight:700;color:#fff;background:#1e3a8a;display:inline-block;padding:4px 24px;border-radius:3px;letter-spacing:.5px}}
.hdr .emp{{margin-top:8px;font-size:9.5px;color:#374151}}
.hdr .emp b{{color:#1e3a8a}}
table{{width:100%;border-collapse:collapse;margin-top:10px;font-size:9px}}
thead th{{background:#1e3a8a;color:#fff;font-weight:600;padding:5px 6px;text-align:right;border:1px solid #1e3a8a;white-space:nowrap}}
thead th:first-child{{text-align:left}}
thead th.grp{{background:#1565c0;font-size:8px;letter-spacing:.3px;text-align:center}}
tbody tr:nth-child(even){{background:#f8fafc}}
tbody tr:hover{{background:#eff6ff}}
tbody td{{padding:4px 6px;text-align:right;border:1px solid #e2e8f0;font-variant-numeric:tabular-nums}}
tbody td:first-child{{text-align:left;font-weight:600;color:#374151}}
tbody td.zero{{color:#d1d5db}}
tbody td.gross{{font-weight:700;color:#1e3a8a;background:#eff6ff}}
tbody td.ded{{color:#dc2626}}
tbody td.tded{{font-weight:700;color:#dc2626;background:#fff5f5}}
tbody td.net{{font-weight:700;color:#15803d;background:#f0fdf4}}
tbody td.tds-cell{{color:#b45309;font-weight:600}}
tfoot tr{{background:#1e3a8a;color:#fff;font-weight:700}}
tfoot td{{padding:5px 6px;text-align:right;border:1px solid #1565c0;font-variant-numeric:tabular-nums}}
tfoot td:first-child{{text-align:left}}
tfoot td.net{{color:#86efac}}
tfoot td.ded{{color:#fca5a5}}
.footer{{margin-top:12px;text-align:center;font-size:8px;color:#94a3b8}}
</style></head><body>
<div class='hdr'>
  <div class='co'>{Esc(co)}</div>
  <div class='addr'>{Esc(addr)}</div>
  <div class='title'>Monthly Salary Statement</div>
  <div class='emp'>Employee: <b>{Esc(emp.Name)}</b> &nbsp;|&nbsp; PAN: <b>{Esc(emp.Pan)}</b> &nbsp;|&nbsp; FY: <b>{fy}</b></div>
</div>
<table>
<thead>
<tr>
  <th rowspan='2' style='text-align:left;vertical-align:bottom'>Month</th>
  <th colspan='{payCols.Count + 1}' class='grp'>EARNINGS</th>
  <th colspan='{dedCols.Count + 1}' class='grp'>DEDUCTIONS</th>
  <th rowspan='2' style='background:#14532d'>Net Pay</th>
</tr>
<tr>");
            foreach (var (lbl, _) in payCols)
                sb2.Append($"<th>{Esc(lbl)}</th>");
            sb2.Append("<th>Gross</th>");
            foreach (var (lbl, _) in dedCols)
                sb2.Append($"<th>{Esc(lbl)}</th>");
            sb2.Append("<th>Total Ded.</th>");
            sb2.Append("</tr></thead><tbody>");

            // Totals
            var totPay  = new double[payCols.Count];
            double totGross2=0, totTotalDed=0, totNet2=0;
            var totDed  = new double[dedCols.Count];

            for (int mi = 0; mi < 12; mi++)
            {
                int m = fyM[mi];
                if (!runs.TryGetValue(m, out var pr))
                {
                    sb2.Append($"<tr><td>{mn[mi]}</td><td colspan='{totalCols-1}' style='color:#d1d5db;text-align:center'>— not entered —</td></tr>");
                    continue;
                }
                sb2.Append($"<tr><td>{mn[mi]}</td>");
                for (int pi = 0; pi < payCols.Count; pi++)
                {
                    double v = payCols[pi].fn(pr);
                    totPay[pi] += v;
                    sb2.Append($"<td>{C(v)}</td>");
                }
                totGross2 += pr.GrossSalary;
                sb2.Append($"<td class='gross'>{C(pr.GrossSalary)}</td>");
                for (int di = 0; di < dedCols.Count; di++)
                {
                    double v = dedCols[di].fn(pr);
                    totDed[di] += v;
                    bool isTds = dedCols[di].label == "TDS";
                    sb2.Append($"<td class='{(isTds?"tds-cell":"ded")}'>{C(v)}</td>");
                }
                double totalDed = pr.TotalDeductions;
                totTotalDed += totalDed;
                totNet2 += pr.NetPay;
                sb2.Append($"<td class='tded'>{C(totalDed)}</td>");
                sb2.Append($"<td class='net'>{C(pr.NetPay)}</td></tr>");
            }

            // Total row
            sb2.Append("<tr style='display:none'></tr>"); // spacer trick for tfoot styling
            sb2.Append("</tbody><tfoot><tr><td>Total</td>");
            for (int pi = 0; pi < payCols.Count; pi++)
                sb2.Append($"<td>{C(totPay[pi])}</td>");
            sb2.Append($"<td>{C(totGross2)}</td>");
            for (int di = 0; di < dedCols.Count; di++)
            {
                bool isTds = dedCols[di].label == "TDS";
                sb2.Append($"<td class='{(isTds?"":"ded")}'>{C(totDed[di])}</td>");
            }
            sb2.Append($"<td class='ded'>{C(totTotalDed)}</td>");
            sb2.Append($"<td class='net'>{C(totNet2)}</td></tr></tfoot></table>");
            sb2.Append($"<div class='footer'>Computer-generated &nbsp;|&nbsp; TDS Pro &nbsp;|&nbsp; {DateTime.Now:dd-MMM-yyyy HH:mm} &nbsp;|&nbsp; Not a legal document</div>");
            sb2.Append("</body></html>");

            Directory.CreateDirectory(outputFolder);
            string safeName2 = string.Concat(emp.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileName2 = $"MonthlySalary_{safeName2}({emp.Pan})_{fy.Replace("/","-")}.html";
            string path2 = Path.Combine(outputFolder, fileName2);
            File.WriteAllText(path2, sb2.ToString(), System.Text.Encoding.UTF8);
            return path2;
        }

        public static string GenerateMonthlySalaryExcel(
            Employee emp,
            string fy,
            string outputFolder,
            Deductor? deductor = null,
            EmployeeYearSummary? yearSummary = null)
        {
            var runs = yearSummary?.MonthlyRuns ?? new Dictionary<int, PayrollRun>();
            int[] fyM = { 4,5,6,7,8,9,10,11,12,1,2,3 };
            string[] mn = { "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };

            bool hasBasic   = runs.Values.Any(r => r.Basic   > 0);
            bool hasHra     = runs.Values.Any(r => r.Hra     > 0);
            bool hasDa      = runs.Values.Any(r => r.Da      > 0);
            bool hasSpecial = runs.Values.Any(r => r.Special  > 0);
            bool hasMedical = runs.Values.Any(r => r.Medical  > 0);
            bool hasLta     = runs.Values.Any(r => r.Lta     > 0);
            bool hasOther   = runs.Values.Any(r => r.Other   > 0);
            bool hasPf      = runs.Values.Any(r => r.PfEmployee    > 0);
            bool hasEsi     = runs.Values.Any(r => r.EsiEmployee   > 0);
            bool hasPt      = runs.Values.Any(r => r.ProfessionalTax > 0);
            bool hasTds     = runs.Values.Any(r => r.TdsDeducted   > 0);
            bool hasOtherD  = runs.Values.Any(r => r.OtherDeductions > 0);

            var payCols = new List<(string label, Func<PayrollRun,double> fn)>();
            var dedCols = new List<(string label, Func<PayrollRun,double> fn)>();
            if (hasBasic)   payCols.Add(("Basic",        r => r.Basic));
            if (hasHra)     payCols.Add(("HRA",          r => r.Hra));
            if (hasDa)      payCols.Add(("DA",           r => r.Da));
            if (hasSpecial) payCols.Add(("Special Allw", r => r.Special));
            if (hasMedical) payCols.Add(("Medical",      r => r.Medical));
            if (hasLta)     payCols.Add(("LTA",          r => r.Lta));
            if (hasOther)   payCols.Add(("Other",        r => r.Other));
            if (hasPf)      dedCols.Add(("PF",           r => r.PfEmployee));
            if (hasEsi)     dedCols.Add(("ESI",          r => r.EsiEmployee));
            if (hasPt)      dedCols.Add(("PT",           r => r.ProfessionalTax));
            if (hasTds)     dedCols.Add(("TDS",          r => r.TdsDeducted));
            if (hasOtherD)  dedCols.Add(("Other Ded",    r => r.OtherDeductions));

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Monthly Salary");

            // Title
            int lastCol = 1 + payCols.Count + 1 + dedCols.Count + 1 + 1;
            ws.Range(1,1,1,lastCol).Merge();
            ws.Cell(1,1).Value = $"{deductor?.CompanyName ?? ""} — Monthly Salary Statement — FY {fy}";
            ws.Cell(1,1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
            ws.Cell(1,1).Style.Font.FontColor = XLColor.White;
            ws.Cell(1,1).Style.Font.Bold = true;
            ws.Cell(1,1).Style.Font.FontSize = 12;
            ws.Cell(1,1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(1).Height = 20;

            ws.Range(2,1,2,lastCol).Merge();
            ws.Cell(2,1).Value = $"Employee: {emp.Name}   |   PAN: {emp.Pan}   |   FY: {fy}";
            ws.Cell(2,1).Style.Font.Bold = true;
            ws.Cell(2,1).Style.Font.FontSize = 10;
            ws.Cell(2,1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(2).Height = 16;

            // Group header row 3
            int earningsStart = 2; int earningsEnd = 1 + payCols.Count + 1;
            int dedStart = earningsEnd + 1; int dedEnd = earningsEnd + dedCols.Count + 1;
            ws.Range(3, earningsStart, 3, earningsEnd).Merge();
            ws.Cell(3, earningsStart).Value = "EARNINGS";
            ws.Cell(3, earningsStart).Style.Fill.BackgroundColor = XLColor.FromHtml("#1565c0");
            ws.Cell(3, earningsStart).Style.Font.FontColor = XLColor.White;
            ws.Cell(3, earningsStart).Style.Font.Bold = true;
            ws.Cell(3, earningsStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Range(3, dedStart, 3, dedEnd).Merge();
            ws.Cell(3, dedStart).Value = "DEDUCTIONS";
            ws.Cell(3, dedStart).Style.Fill.BackgroundColor = XLColor.FromHtml("#dc2626");
            ws.Cell(3, dedStart).Style.Font.FontColor = XLColor.White;
            ws.Cell(3, dedStart).Style.Font.Bold = true;
            ws.Cell(3, dedStart).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Column headers row 4
            int c4 = 1;
            void Hdr(string lbl, string? bg = null)
            {
                ws.Cell(4, c4).Value = lbl;
                ws.Cell(4, c4).Style.Fill.BackgroundColor = bg != null ? XLColor.FromHtml(bg) : XLColor.FromHtml("#1e3a8a");
                ws.Cell(4, c4).Style.Font.FontColor = XLColor.White;
                ws.Cell(4, c4).Style.Font.Bold = true;
                ws.Cell(4, c4).Style.Alignment.Horizontal = c4 == 1 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;
                ws.Cell(4, c4).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                c4++;
            }
            Hdr("Month");
            foreach (var (lbl2, _) in payCols) Hdr(lbl2);
            Hdr("Gross", "#15803d");
            foreach (var (lbl2, _) in dedCols) Hdr(lbl2, "#b91c1c");
            Hdr("Total Ded.", "#b91c1c");
            Hdr("Net Pay", "#14532d");
            ws.Row(4).Height = 16;

            // Data rows
            var totPay  = new double[payCols.Count];
            double totGross3=0, totTDed=0, totNet3=0;
            var totDed3 = new double[dedCols.Count];
            int r2 = 5;
            for (int mi = 0; mi < 12; mi++)
            {
                int m = fyM[mi];
                string rowBg = mi % 2 == 0 ? "#ffffff" : "#f8fafc";
                if (!runs.TryGetValue(m, out var pr))
                {
                    ws.Cell(r2, 1).Value = mn[mi];
                    ws.Range(r2, 2, r2, lastCol).Merge();
                    ws.Cell(r2, 2).Value = "— not entered —";
                    ws.Cell(r2, 2).Style.Font.FontColor = XLColor.FromHtml("#d1d5db");
                    ws.Cell(r2, 2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Row(r2).Style.Fill.BackgroundColor = XLColor.FromHtml(rowBg);
                    r2++; continue;
                }
                int ci = 1;
                void V(double v, bool bold = false, string? fg = null)
                {
                    var cell = ws.Cell(r2, ci);
                    if (v == 0) cell.Value = "—";
                    else { cell.Value = v; cell.Style.NumberFormat.Format = "#,##0"; }
                    if (bold) cell.Style.Font.Bold = true;
                    if (fg != null) cell.Style.Font.FontColor = XLColor.FromHtml(fg);
                    cell.Style.Alignment.Horizontal = ci == 1 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#e2e8f0");
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(rowBg);
                    ci++;
                }
                ws.Cell(r2, ci).Value = mn[mi]; ws.Cell(r2, ci).Style.Font.Bold = true;
                ws.Cell(r2, ci).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(r2, ci).Style.Border.OutsideBorderColor = XLColor.FromHtml("#e2e8f0");
                ws.Cell(r2, ci).Style.Fill.BackgroundColor = XLColor.FromHtml(rowBg); ci++;
                for (int pi = 0; pi < payCols.Count; pi++) { double v = payCols[pi].fn(pr); totPay[pi]+=v; V(v); }
                totGross3 += pr.GrossSalary; V(pr.GrossSalary, bold:true, fg:"#1e3a8a");
                for (int di = 0; di < dedCols.Count; di++) { double v = dedCols[di].fn(pr); totDed3[di]+=v; V(v, fg:"#dc2626"); }
                totTDed += pr.TotalDeductions; V(pr.TotalDeductions, bold:true, fg:"#dc2626");
                totNet3 += pr.NetPay; V(pr.NetPay, bold:true, fg:"#15803d");
                r2++;
            }

            // Total row
            int ct = 1;
            void Tot(double v, string? fg = null)
            {
                var cell = ws.Cell(r2, ct);
                if (v == 0) cell.Value = "—";
                else { cell.Value = v; cell.Style.NumberFormat.Format = "#,##0"; }
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                cell.Style.Font.FontColor = fg != null ? XLColor.FromHtml(fg) : XLColor.White;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = ct == 1 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1565c0");
                ct++;
            }
            ws.Cell(r2, ct).Value = "Total"; ws.Cell(r2, ct).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
            ws.Cell(r2, ct).Style.Font.FontColor = XLColor.White; ws.Cell(r2, ct).Style.Font.Bold = true;
            ws.Cell(r2, ct).Style.Border.OutsideBorder = XLBorderStyleValues.Thin; ct++;
            for (int pi = 0; pi < payCols.Count; pi++) Tot(totPay[pi]);
            Tot(totGross3, "#86efac");
            for (int di = 0; di < dedCols.Count; di++) Tot(totDed3[di], "#fca5a5");
            Tot(totTDed, "#fca5a5");
            Tot(totNet3, "#86efac");
            ws.Row(r2).Height = 16;

            // Column widths
            ws.Column(1).Width = 10;
            for (int ci2 = 2; ci2 <= lastCol; ci2++) ws.Column(ci2).Width = 14;

            Directory.CreateDirectory(outputFolder);
            string safeName3 = string.Concat(emp.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileName3 = $"MonthlySalary_{safeName3}({emp.Pan})_{fy.Replace("/","-")}.xlsx";
            string path3 = Path.Combine(outputFolder, fileName3);
            wb.SaveAs(path3);
            return path3;
        }

        public static string GenerateAnnualExcel(
            AnnualComputation annual,
            Employee emp,
            string fy,
            string outputFolder,
            Deductor? deductor = null,
            EmployeeYearSummary? yearSummary = null)
        {
            var o = annual.OldRegime;
            var n = annual.NewRegime;
            var chosen = annual.ChosenRegime == "New" ? n : o;

            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Annual Tax Computation");
            ws.PageSetup.PaperSize       = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 1);
            ws.Column(1).Width = 36;
            ws.Column(2).Width = 18;
            ws.Column(3).Width = 18;

            int r = 1;

            // Company name header
            if (!string.IsNullOrEmpty(deductor?.CompanyName))
            {
                ws.Range(r,1,r,3).Merge();
                ws.Cell(r,1).Value = deductor.CompanyName
                    + (string.IsNullOrEmpty(deductor.Tan) ? "" : $"  |  TAN: {deductor.Tan}");
                ws.Cell(r,1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                ws.Cell(r,1).Style.Font.FontColor = XLColor.White;
                ws.Cell(r,1).Style.Font.Bold = true;
                ws.Cell(r,1).Style.Font.FontSize = 13;
                ws.Cell(r,1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                ws.Cell(r,1).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
                ws.Row(r).Height = 26; r++;
            }

            // Report title
            ws.Range(r,1,r,3).Merge();
            ws.Cell(r,1).Value = $"Annual Tax Computation — FY {fy}";
            ws.Cell(r,1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
            ws.Cell(r,1).Style.Font.FontColor = XLColor.White;
            ws.Cell(r,1).Style.Font.Bold = true; ws.Cell(r,1).Style.Font.FontSize = 11;
            ws.Cell(r,1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Cell(r,1).Style.Alignment.Vertical   = XLAlignmentVerticalValues.Center;
            ws.Row(r).Height = 22; r++;

            // Employee info
            void InfoRow(int row, string l, string v)
            {
                ws.Cell(row,1).Value=l; ws.Cell(row,1).Style.Font.FontColor=XLColor.Gray;
                ws.Range(row,2,row,3).Merge();
                ws.Cell(row,2).Value=v; ws.Cell(row,2).Style.Font.Bold=true;
            }
            InfoRow(r,"Employee", emp.Name); r++;
            InfoRow(r,"Employee Code", emp.EmployeeCode); r++;
            InfoRow(r,"PAN", emp.Pan); r++;
            InfoRow(r,"Designation", emp.Designation); r++;
            InfoRow(r,"Chosen Regime", annual.ChosenRegime + " Regime"); r++; r++;

            // Column headers
            string[] hdrs = {"Component","Old Regime","New Regime"};
            for (int i=0;i<3;i++)
            {
                ws.Cell(r,i+1).Value = hdrs[i];
                ws.Cell(r,i+1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                ws.Cell(r,i+1).Style.Font.FontColor = XLColor.White;
                ws.Cell(r,i+1).Style.Font.Bold = true;
                ws.Cell(r,i+1).Style.Alignment.Horizontal =
                    i==0 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;
            }
            r++;

            var dataRows = new (string, double, double, bool sep, bool tot)[]
            {
                ("Gross Salary (incl Perqs)",  o.GrossSalary,       n.GrossSalary,       false, false),
                ("Standard Deduction",         o.StandardDeduction, n.StandardDeduction, false, false),
                ("HRA Exemption",              o.HraExemption,      n.HraExemption,      false, false),
                ("Professional Tax",           o.ProfTaxDeduction,  n.ProfTaxDeduction,  false, false),
                ("Chapter VI-A Deductions",    o.Chapter6A,         n.Chapter6A,         false, false),
                ("NPS Employer 80CCD(2)",      o.NpsEmployer80CCD2, n.NpsEmployer80CCD2, false, false),
                ("Income from Other Sources",  o.IncomeOtherSources,n.IncomeOtherSources,false, false),
                ("", 0, 0, true, false),
                ("Taxable Income",             o.TotalIncome,       n.TotalIncome,       false, false),
                ("Tax on Income",              o.TaxOnIncome,       n.TaxOnIncome,       false, false),
                ("87A Rebate",                 o.Rebate87A,         n.Rebate87A,         false, false),
                ("Tax After Rebate",           o.TaxAfterRebate,    n.TaxAfterRebate,    false, false),
                ("Surcharge",                  o.Surcharge,         n.Surcharge,         false, false),
                ("Cess (4%)",                  o.Cess,              n.Cess,              false, false),
                ("TOTAL TAX",                  o.TotalTax,          n.TotalTax,          false, true),
            };

            bool chosenIsNew = annual.ChosenRegime == "New";
            foreach (var (label, ov, nv, isSep, isTot) in dataRows)
            {
                if (isSep)
                {
                    ws.Range(r,1,r,3).Style.Fill.BackgroundColor = XLColor.FromHtml("#e2e8f0");
                    ws.Row(r).Height = 4; r++; continue;
                }
                var bg = isTot ? XLColor.FromHtml("#dbeafe") : (r%2==0 ? XLColor.FromHtml("#f8fafc") : XLColor.White);
                ws.Cell(r,1).Value = label;
                ws.Cell(r,2).Value = ov == 0 ? "—" : "₹"+ov.ToString("N0");
                ws.Cell(r,3).Value = nv == 0 ? "—" : "₹"+nv.ToString("N0");
                ws.Range(r,1,r,3).Style.Fill.BackgroundColor = bg;
                ws.Cell(r,2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(r,3).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                if (isTot)
                {
                    ws.Range(r,1,r,3).Style.Font.Bold = true;
                    ws.Range(r,1,r,3).Style.Font.FontColor = XLColor.FromHtml("#1e3a8a");
                    ws.Range(r,1,r,3).Style.Border.TopBorder = XLBorderStyleValues.Medium;
                    ws.Range(r,1,r,3).Style.Border.TopBorderColor = XLColor.FromHtml("#1e3a8a");
                }
                // Highlight chosen regime total
                if (isTot)
                {
                    var chosenCol = chosenIsNew ? ws.Cell(r,3) : ws.Cell(r,2);
                    chosenCol.Style.Fill.BackgroundColor = XLColor.FromHtml("#0f4c81");
                    chosenCol.Style.Font.FontColor = XLColor.White;
                }
                r++;
            }
            r++;

            // TDS position — use actual total from monthly runs if available
            var xlRuns = yearSummary?.MonthlyRuns ?? new Dictionary<int, PayrollRun>();
            double xlTdsPaid = xlRuns.Count > 0 ? xlRuns.Values.Sum(r2 => r2.TdsDeducted) : annual.YtdTdsDeducted;
            double xlBalance = chosen.TotalTax - xlTdsPaid;
            ws.Range(r,1,r,3).Merge();
            ws.Cell(r,1).Value = $"Annual Tax: ₹{chosen.TotalTax:N0}   |   TDS Paid: ₹{xlTdsPaid:N0}   |   Balance: ₹{xlBalance:N0}";
            ws.Cell(r,1).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0fdf4");
            ws.Cell(r,1).Style.Font.FontColor = XLColor.FromHtml("#14532d");
            ws.Cell(r,1).Style.Font.Bold = true;
            ws.Cell(r,1).Style.Alignment.Indent = 1; r++;

            // Footer
            ws.Range(r,1,r,3).Merge();
            ws.Cell(r,1).Value = $"Generated by TDS Pro v3.0 | Income-tax Act 2025 | {DateTime.Now:dd-MMM-yyyy}";
            ws.Cell(r,1).Style.Font.Italic = true;
            ws.Cell(r,1).Style.Font.FontColor = XLColor.Gray;
            ws.Cell(r,1).Style.Alignment.Indent = 1;

            // ── Monthly Salary Sheet ──────────────────────────────────────────
            var runs = yearSummary?.MonthlyRuns ?? new Dictionary<int, PayrollRun>();
            if (runs.Count > 0)
            {
                var wm = wb.Worksheets.Add("Monthly Salary");
                wm.PageSetup.PaperSize = XLPaperSize.A4Paper;
                wm.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                wm.PageSetup.FitToPages(1, 1);

                // Header
                wm.Range(1,1,1,9).Merge();
                wm.Cell(1,1).Value = $"{deductor?.CompanyName ?? ""} — Monthly Salary Statement — FY {fy}";
                wm.Cell(1,1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                wm.Cell(1,1).Style.Font.FontColor = XLColor.White;
                wm.Cell(1,1).Style.Font.Bold = true; wm.Cell(1,1).Style.Font.FontSize = 11;
                wm.Cell(1,1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                wm.Row(1).Height = 22;

                wm.Range(2,1,2,9).Merge();
                wm.Cell(2,1).Value = $"Employee: {emp.Name}  |  Code: {emp.EmployeeCode}  |  PAN: {emp.Pan}  |  Regime: {annual.ChosenRegime}";
                wm.Cell(2,1).Style.Fill.BackgroundColor = XLColor.FromHtml("#dbeafe");
                wm.Cell(2,1).Style.Font.Bold = true;
                wm.Row(2).Height = 18;

                string[] mHdrs = { "Month","Gross Salary","HRA Exemption","Std. Deduction","Taxable Income","TDS Deducted","PF Employee","Prof. Tax","Net Pay" };
                for (int i=0;i<9;i++)
                {
                    wm.Cell(3,i+1).Value = mHdrs[i];
                    wm.Cell(3,i+1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                    wm.Cell(3,i+1).Style.Font.FontColor = XLColor.White;
                    wm.Cell(3,i+1).Style.Font.Bold = true;
                    wm.Cell(3,i+1).Style.Alignment.Horizontal =
                        i==0 ? XLAlignmentHorizontalValues.Left : XLAlignmentHorizontalValues.Right;
                }
                wm.Row(3).Height = 16;

                int[] fyMonths = { 4,5,6,7,8,9,10,11,12,1,2,3 };
                string[] mNames = { "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };
                bool chosenOldXl = annual.ChosenRegime == "Old";
                double totG=0,totH=0,totS=0,totT=0,totD=0,totP=0,totPt2=0,totN=0;
                for (int mi=0;mi<12;mi++)
                {
                    int row = 4+mi, m = fyMonths[mi];
                    var bg2 = mi%2==0 ? XLColor.White : XLColor.FromHtml("#f8fafc");
                    wm.Row(row).Style.Fill.BackgroundColor = bg2;
                    wm.Cell(row,1).Value = mNames[mi];
                    wm.Cell(row,1).Style.Font.Bold = true;
                    if (runs.TryGetValue(m, out var pr))
                    {
                        double stdM = pr.StandardDeduction > 0 ? pr.StandardDeduction
                            : (chosenOldXl ? o.StandardDeduction : n.StandardDeduction) / 12.0;
                        double[] vals = { pr.GrossSalary, pr.HraExemption, stdM, pr.TaxableIncome, pr.TdsDeducted, pr.PfEmployee, pr.ProfessionalTax, pr.NetPay };
                        totG+=pr.GrossSalary; totH+=pr.HraExemption; totS+=stdM;
                        totT+=pr.TaxableIncome; totD+=pr.TdsDeducted;
                        totP+=pr.PfEmployee; totPt2+=pr.ProfessionalTax; totN+=pr.NetPay;
                        for (int ci=0;ci<8;ci++)
                        {
                            if (vals[ci] == 0) wm.Cell(row,ci+2).Value = "—";
                            else wm.Cell(row,ci+2).Value = vals[ci];
                            wm.Cell(row,ci+2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            if (vals[ci]>0 && ci>=0)
                                wm.Cell(row,ci+2).Style.NumberFormat.Format = "₹#,##0";
                        }
                    }
                    else
                    {
                        wm.Range(row,2,row,9).Merge();
                        wm.Cell(row,2).Value = "— not entered —";
                        wm.Cell(row,2).Style.Font.Italic = true;
                        wm.Cell(row,2).Style.Font.FontColor = XLColor.LightGray;
                    }
                }
                // Total row
                int tRow = 16;
                wm.Row(tRow).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                wm.Row(tRow).Style.Font.FontColor = XLColor.White;
                wm.Row(tRow).Style.Font.Bold = true;
                wm.Cell(tRow,1).Value = "Total";
                double[] tots = { totG,totH,totS,totT,totD,totP,totPt2,totN };
                for (int ci=0;ci<8;ci++)
                {
                    wm.Cell(tRow,ci+2).Value = tots[ci];
                    wm.Cell(tRow,ci+2).Style.NumberFormat.Format = "₹#,##0";
                    wm.Cell(tRow,ci+2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                wm.Row(tRow).Height = 18;
                for (int ci=1;ci<=9;ci++) wm.Column(ci).AdjustToContents();
                wm.Column(1).Width = 10;
            }

            Directory.CreateDirectory(outputFolder);
            string safeNameXl = string.Concat(emp.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileName = $"Computation_{safeNameXl}({emp.Pan})_{fy.Replace("/","-")}.xlsx";
            string path = Path.Combine(outputFolder, fileName);
            wb.SaveAs(path);
            return path;
        }

        private static string Esc(string? s)
            => (s ?? "").Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
    }
}
