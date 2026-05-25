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
                ("Other Allowances",               entry.OtherAllowances),
                ("NPS (Employer)",                 entry.NpsEmployer),
                ("Perquisites [taxable]",          entry.PerqTaxable),
                ("Leave Encashment [taxable]",     entry.LeaveEncTaxable),
            };
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
                ($"Income Tax ({TDSPro.Common.TaxRules.SalaryTdsSection(entry.FinancialYear)})", entry.TdsDeducted),
            };
            var deductions = allDeductions.Where(x => x.Amount != 0).ToList();

            double grossEarnings   = entry.GrossPayment;
            double totalDeductions = htmlLopAmount + entry.PfEmployee + entry.VPF + entry.ProfessionalTax
                                   + entry.EsiEmployee + entry.TdsDeducted;
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
.slip-header{{display:flex;justify-content:space-between;align-items:flex-start;border-bottom:3px solid #1e3a8a;padding-bottom:10px;margin-bottom:12px}}
.co-name{{font-size:17px;font-weight:700;color:#1e3a8a;margin-bottom:2px}}
.co-sub{{font-size:10px;color:#555}}
.slip-title{{text-align:right}}
.slip-title h2{{font-size:15px;font-weight:600;color:#1e3a8a;background:#dbeafe;padding:4px 12px;border-radius:4px}}
.slip-title p{{font-size:10px;color:#666;margin-top:4px}}
/* ── Employee info ── */
.emp-grid{{display:grid;grid-template-columns:1fr 1fr;gap:6px 20px;background:#f8fafc;border:1px solid #e2e8f0;border-radius:6px;padding:10px 12px;margin-bottom:12px;font-size:10px}}
.emp-row{{display:flex;gap:4px}}
.emp-lbl{{color:#6b7280;width:130px;flex-shrink:0}}
.emp-val{{font-weight:600;color:#111}}
/* ── Days row ── */
.days-bar{{display:grid;grid-template-columns:repeat(4,1fr);background:#1e3a8a;color:#fff;border-radius:5px;padding:7px 14px;margin-bottom:12px;font-size:10px}}
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
  <div>
    <div class='co-name'>{Esc(deductor.CompanyName)}</div>
    <div class='co-sub'>TAN: {Esc(deductor.Tan)} &nbsp;|&nbsp; {Esc(deductor.Address ?? "")}</div>
  </div>
  <div class='slip-title'>
    <h2>SALARY SLIP</h2>
    <p>{monthLabel}</p>
  </div>
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
  <span><strong>{htmlDim}</strong>Days in Month</span>
  <span><strong>{htmlDaysPaid}</strong>Days Present</span>
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
                ("Other Allowances",               entry.OtherAllowances),
                ("NPS (Employer)",                 entry.NpsEmployer),
                ("Perquisites [taxable]",          entry.PerqTaxable),
                ("Leave Encashment [taxable]",     entry.LeaveEncTaxable),
            }.Where(x => x.Amount != 0 || x.Label == "Basic Salary").ToList();

            int lopDays       = entry.LopDays;
            double lopAmount  = lopDays > 0 ? Math.Round(entry.Basic * lopDays / 30.0, 0) : 0;

            var deductions = new List<(string Label, double Amount)>
            {
                ("Loss of Pay",               lopAmount),
                ("Provident Fund (Employee)", entry.PfEmployee),
                ("VPF / Extra PF",            entry.VPF),
                ("Professional Tax",          entry.ProfessionalTax),
                ("ESI (Employee)",            entry.EsiEmployee),
                ("Income Tax (TDS)",          entry.TdsDeducted),
            }.Where(x => x.Amount != 0).ToList();

            double grossTotal = earnings.Sum(e => e.Amount);
            double dedTotal   = deductions.Sum(d => d.Amount);
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

            byte[] pdf = PdfReports.BuildA4(
                title:    $"Salary Slip — {monthLabel}  |  Pay Period: {periodStart} to {periodEnd}",
                subtitle: deductor.CompanyName + (string.IsNullOrEmpty(dedAddress) ? "" : "  ·  " + dedAddress),
                body:     c => c.Column(col =>
                {
                    // Employer identity strip (TAN / PAN)
                    if (!string.IsNullOrEmpty(deductor.Tan) || !string.IsNullOrEmpty(deductor.Pan))
                    {
                        col.Item().PaddingBottom(6).Row(r =>
                        {
                            if (!string.IsNullOrEmpty(deductor.Tan))
                                r.RelativeItem().Text(t => { t.Span("Employer TAN: ").FontColor(PdfReports.MutedColor).FontSize(9); t.Span(deductor.Tan).FontSize(9).Bold(); });
                            if (!string.IsNullOrEmpty(deductor.Pan))
                                r.RelativeItem().Text(t => { t.Span("Employer PAN: ").FontColor(PdfReports.MutedColor).FontSize(9); t.Span(deductor.Pan).FontSize(9).Bold(); });
                        });
                    }

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

                        // Days row
                        t.Cell().Element(PdfReports.LabelCell).Text("Days in Month").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(dim.ToString());
                        t.Cell().Element(PdfReports.LabelCell).Text("Paid Days").FontColor(PdfReports.MutedColor).FontSize(9);
                        t.Cell().Element(PdfReports.LabelCell).Text(daysPaid.ToString() + (lopDays > 0 ? $"  |  LOP: {lopDays}" : ""));
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
            double gross = entry.GrossPayment;
            double ded   = xlLopAmount + entry.PfEmployee + entry.VPF + entry.ProfessionalTax + entry.EsiEmployee + entry.TdsDeducted;
            double net   = gross - ded;

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
                ("Other Allowances",            entry.OtherAllowances),
                ("NPS (Employer)",              entry.NpsEmployer),
                ("Perquisites (Taxable)",       entry.PerqTaxable),
                ("Leave Enc. (Taxable)",        entry.LeaveEncTaxable),
            }.Where(x => x.Item2 != 0 || x.Item1 == "Basic Salary").ToList();

            var deds = new List<(string, double)> {
                ("Loss of Pay",          xlLopAmount),
                ("Provident Fund",       entry.PfEmployee),
                ("VPF / Extra PF",       entry.VPF),
                ("Professional Tax",     entry.ProfessionalTax),
                ("ESI (Employee)",       entry.EsiEmployee),
                ($"Income Tax ({TDSPro.Common.TaxRules.SalaryTdsSection(entry.FinancialYear)})", entry.TdsDeducted),
            }.Where(x => x.Item2 != 0).ToList();

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
            Deductor? deductor = null)
        {
            var o = annual.OldRegime;
            var n = annual.NewRegime;
            bool chosenOld = annual.ChosenRegime == "Old";
            var chosen       = chosenOld ? o : n;

            var rows = new (string Label, double OldVal, double NewVal, bool IsSep, bool IsTot)[]
            {
                ("Gross Salary (incl Perqs)",     o.GrossSalary,       n.GrossSalary,       false, false),
                ("Standard Deduction",            o.StandardDeduction, n.StandardDeduction, false, false),
                ("HRA Exemption",                 o.HraExemption,      n.HraExemption,      false, false),
                ("Professional Tax",              o.ProfTaxDeduction,  n.ProfTaxDeduction,  false, false),
                ("Chapter VI-A Deductions",       o.Chapter6A,         n.Chapter6A,         false, false),
                ("NPS Employer 80CCD(2)",         o.NpsEmployer80CCD2, n.NpsEmployer80CCD2, false, false),
                ("Income from Other Sources",     o.IncomeOtherSources,n.IncomeOtherSources,false, false),
                ("", 0, 0, true, false),
                ("Taxable Income",                o.TotalIncome,       n.TotalIncome,       false, false),
                ("Tax on Income",                 o.TaxOnIncome,       n.TaxOnIncome,       false, false),
                ("87A Rebate",                    o.Rebate87A,         n.Rebate87A,         false, false),
                ("Tax After Rebate",              o.TaxAfterRebate,    n.TaxAfterRebate,    false, false),
                ("Surcharge",                     o.Surcharge,         n.Surcharge,         false, false),
                ("Cess (4%)",                     o.Cess,              n.Cess,              false, false),
                ("TOTAL TAX",                     o.TotalTax,          n.TotalTax,          false, true),
            };

            var sb = new System.Text.StringBuilder();
            sb.Append($@"<!DOCTYPE html>
<html><head><meta charset='UTF-8'>
<title>Annual Tax Computation — {Esc(emp.Name)} — {Esc(fy)}</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:'Segoe UI',Arial,sans-serif;background:#eee;print-color-adjust:exact;-webkit-print-color-adjust:exact}}
.page{{width:180mm;margin:10mm auto;background:#fff;padding:12mm;box-shadow:0 0 8px rgba(0,0,0,.2)}}
.hdr{{border-bottom:3px solid #1e3a8a;padding-bottom:8px;margin-bottom:12px;display:flex;justify-content:space-between;align-items:flex-end}}
.co{{font-size:13px;font-weight:700;color:#1e3a8a}}
.emp-box{{background:#f8fafc;border:1px solid #e2e8f0;border-radius:5px;padding:9px 12px;margin-bottom:12px;display:grid;grid-template-columns:1fr 1fr;gap:4px 20px;font-size:10px}}
.emp-row{{display:flex;gap:4px}}.lbl{{color:#6b7280;width:120px;flex-shrink:0}}.val{{font-weight:600;color:#111}}
table{{width:100%;border-collapse:collapse;font-size:10.5px}}
.col-hdr{{background:#1e3a8a;color:#fff;padding:6px 12px;font-weight:600;font-size:9px;text-align:center}}
.col-hdr.title{{text-align:left;font-size:11px}}
th{{background:#dbeafe;color:#1e3a8a;padding:5px 12px;font-size:9.5px}}
th.right{{text-align:right}}
td{{padding:5px 12px;border-bottom:1px solid #f0f2f5;color:#374151;vertical-align:middle}}
td.num{{text-align:right;font-variant-numeric:tabular-nums}}
tr:nth-child(even) td{{background:#f8fafc}}
tr.sep td{{padding:2px 0;border:none;background:#e2e8f0;height:1px}}
tr.tot td{{background:#dbeafe!important;font-weight:700;color:#1e3a8a;font-size:11.5px;border-top:2px solid #1e3a8a}}
tr.chosen td.num{{font-weight:700}}
.badge{{display:inline-block;padding:2px 8px;border-radius:10px;font-size:9px;font-weight:600;margin-left:6px}}
.old-badge{{background:#fef3c7;color:#92400e}}.new-badge{{background:#d1fae5;color:#065f46}}
.tds-grid{{display:grid;grid-template-columns:repeat(4,1fr);gap:0;border:1px solid #d1fae5;border-radius:5px;overflow:hidden;margin-top:12px;font-size:10px}}
.tc{{text-align:center;padding:7px 4px;background:#f0fdf4}}
.tc:nth-child(even){{background:#dcfce7}}
.tc .tl{{color:#166534;font-size:8.5px;margin-bottom:2px}}.tc .tv{{font-weight:700;color:#14532d;font-variant-numeric:tabular-nums}}
.footer{{font-size:8.5px;color:#9ca3af;text-align:center;margin-top:14px;border-top:1px solid #e2e8f0;padding-top:8px}}
@media print{{body{{background:#fff}}.page{{box-shadow:none;margin:0;padding:10mm;width:100%}}}}
</style></head><body><div class='page'>

<div style='text-align:center;background:#1e3a8a;color:#fff;padding:10px 12px;border-radius:4px 4px 0 0;margin-bottom:0'>
  <div style='font-size:14px;font-weight:700;letter-spacing:.3px'>{Esc(deductor?.CompanyName ?? "")}</div>
  {(string.IsNullOrEmpty(deductor?.Tan) ? "" : $"<div style='font-size:9px;opacity:.85;margin-top:2px'>TAN: {Esc(deductor.Tan)}</div>")}
</div>
<div class='hdr' style='border-radius:0;margin-top:0;padding-top:8px'>
  <div>
    <div class='co'>Annual Tax Computation — FY {Esc(fy)}</div>
    <div style='font-size:9px;color:#6b7280;margin-top:2px'>Generated by TDS Pro v3.0 &nbsp;|&nbsp; {TDSPro.Common.TaxRules.ActName(fy)}</div>
  </div>
  <div style='text-align:right;font-size:9px;color:#6b7280'>{DateTime.Now:dd-MMM-yyyy HH:mm}</div>
</div>

<div class='emp-box'>
  <div class='emp-row'><span class='lbl'>Employee</span><span class='val'>{Esc(emp.Name)}</span></div>
  <div class='emp-row'><span class='lbl'>PAN</span><span class='val'>{Esc(emp.Pan)}</span></div>
  <div class='emp-row'><span class='lbl'>Code</span><span class='val'>{Esc(emp.EmployeeCode)}</span></div>
  <div class='emp-row'><span class='lbl'>Chosen Regime</span>
    <span class='val'>{Esc(annual.ChosenRegime)} Regime
      <span class='badge {(annual.ChosenRegime=="New"?"new":"old")}-badge'>Chosen ✓</span>
    </span>
  </div>
  <div class='emp-row'><span class='lbl'>Designation</span><span class='val'>{Esc(emp.Designation)}</span></div>
  <div class='emp-row'><span class='lbl'>Annual Tax</span><span class='val'>₹{chosen.TotalTax:N0}</span></div>
</div>

<table>
  <thead>
    <tr>
      <td class='col-hdr title' style='width:52%'>Component</td>
      <td class='col-hdr' style='width:24%'>Old Regime<span class='badge old-badge{(annual.ChosenRegime=="Old"?" chosen":"")}'>{(annual.ChosenRegime=="Old"?"✓ Chosen":"")}</span></td>
      <td class='col-hdr' style='width:24%'>New Regime<span class='badge new-badge{(annual.ChosenRegime=="New"?" chosen":"")}'>{(annual.ChosenRegime=="New"?"✓ Chosen":"")}</span></td>
    </tr>
  </thead>
  <tbody>");

            bool chosenOldBool = annual.ChosenRegime == "Old";
            var alwaysShow = new HashSet<string> {
                "Gross Salary (incl Perqs)", "Standard Deduction",
                "Taxable Income", "Tax on Income", "Tax After Rebate", "TOTAL TAX"
            };
            // Strip trailing separator rows before any always-show rows to avoid orphan dividers
            var filteredRows = rows
                .Where((r, i) => r.IsSep || r.IsTot || alwaysShow.Contains(r.Label) || r.OldVal != 0 || r.NewVal != 0)
                .ToArray();
            // Remove consecutive/leading/trailing separators
            var cleanRows = new List<(string Label, double OldVal, double NewVal, bool IsSep, bool IsTot)>();
            bool lastWasSep = true;
            foreach (var row in filteredRows)
            {
                if (row.IsSep) { if (!lastWasSep) cleanRows.Add(row); lastWasSep = true; }
                else { cleanRows.Add(row); lastWasSep = false; }
            }
            while (cleanRows.Count > 0 && cleanRows[^1].IsSep) cleanRows.RemoveAt(cleanRows.Count - 1);

            foreach (var row in cleanRows)
            {
                if (row.IsSep)
                {
                    sb.Append("<tr class='sep'><td colspan='3'></td></tr>");
                    continue;
                }
                string cls = row.IsTot ? " class='tot'" : "";
                sb.Append($"<tr{cls}>" +
                    $"<td>{Esc(row.Label)}</td>" +
                    $"<td class='num{(row.IsTot&&chosenOldBool?" chosen":"")}'>{R(row.OldVal)}</td>" +
                    $"<td class='num{(row.IsTot&&!chosenOldBool?" chosen":"")}'>{R(row.NewVal)}</td>" +
                    $"</tr>");
            }

            sb.Append($@"  </tbody>
</table>

<div class='tds-grid'>
  <div class='tc'><div class='tl'>Annual Tax (chosen)</div><div class='tv'>₹{chosen.TotalTax:N0}</div></div>
  <div class='tc'><div class='tl'>YTD TDS Deducted</div><div class='tv'>₹{annual.YtdTdsDeducted:N0}</div></div>
  <div class='tc'><div class='tl'>Balance Tax</div><div class='tv'>₹{Math.Abs(annual.BalanceTax):N0}{(annual.BalanceTax<0?" (excess)":"")}</div></div>
  <div class='tc'><div class='tl'>Monthly TDS</div><div class='tv'>₹{annual.ThisMonthTds:N0}</div></div>
</div>

<div class='footer'>Computer-generated &nbsp;|&nbsp; TDS Pro v3.0 &nbsp;|&nbsp; Not a legal document</div>
</div></body></html>");

            Directory.CreateDirectory(outputFolder);
            string fileName = $"AnnualTax_{emp.EmployeeCode}_{fy.Replace("/","-")}.html";
            string path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            return path;
        }

        public static string GenerateAnnualExcel(
            AnnualComputation annual,
            Employee emp,
            string fy,
            string outputFolder,
            Deductor? deductor = null)
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

            // TDS position
            ws.Range(r,1,r,3).Merge();
            ws.Cell(r,1).Value = $"Annual Tax: ₹{chosen.TotalTax:N0}   |   YTD TDS: ₹{annual.YtdTdsDeducted:N0}   |   Balance: ₹{annual.BalanceTax:N0}   |   Monthly TDS: ₹{annual.ThisMonthTds:N0}";
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

            Directory.CreateDirectory(outputFolder);
            string fileName = $"AnnualTax_{emp.EmployeeCode}_{fy.Replace("/","-")}.xlsx";
            string path = Path.Combine(outputFolder, fileName);
            wb.SaveAs(path);
            return path;
        }

        private static string Esc(string? s)
            => (s ?? "").Replace("&","&amp;").Replace("<","&lt;").Replace(">","&gt;");
    }
}
