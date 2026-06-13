using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using CDeTDS.Common;
using CDeTDS.DAL.Models;

namespace CDeTDS.DAL
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
            bool htmlProRated = entry.DaysWorked > 0 && entry.DaysWorked < AppConstants.StandardPayrollDays;
            double htmlLopAmount = (!htmlProRated && htmlLopDays > 0) ? Math.Round(entry.GrossPayment * htmlLopDays / AppConstants.StandardPayrollDays, 0) : 0;
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
            allDeductions.Add(("TDS Deducted", entry.TdsDeducted));
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

            // FOOTER
            sb.Append($@"<div class='slip-footer'>
  <div>
    <div>This is a computer-generated salary slip and does not require a signature.</div>
    <div>Generated by CDeTDS v{CDeTDS.Common.AppConstants.AppVersion} &nbsp;|&nbsp; {CDeTDS.Common.TaxRules.ActName(entry.FinancialYear)} &nbsp;|&nbsp; {DateTime.Now:dd-MMM-yyyy HH:mm}</div>
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
        private static string _fyLabel(CDeTDS.DAL.Models.MonthlySalaryEntry e)
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
            bool pdfProRated = entry.DaysWorked > 0 && entry.DaysWorked < AppConstants.StandardPayrollDays;
            double lopAmount  = (!pdfProRated && lopDays > 0) ? Math.Round(entry.GrossPayment * lopDays / AppConstants.StandardPayrollDays, 0) : 0;

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
            deductionsList.Add(("TDS Deducted", entry.TdsDeducted));
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
            string netInWords = CDeTDS.Common.AmountInWords.Rupees(netPay);

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

                    // Employee header block — 4-column grid matching HTML slip
                    // Employee info — same 4-column order as the Excel slip
                    col.Item().PaddingBottom(8).Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(8).Table(t =>
                    {
                        t.ColumnsDefinition(d => { d.RelativeColumn(1.4f); d.RelativeColumn(1.6f); d.RelativeColumn(1.4f); d.RelativeColumn(1.6f); });

                        void Lbl(string text) => t.Cell().PaddingBottom(4)
                            .Text(text).FontColor(PdfReports.MutedColor).FontSize(8.5f);
                        void Val(string text) => t.Cell().PaddingBottom(4)
                            .Text(string.IsNullOrWhiteSpace(text) ? "—" : text).Bold().FontSize(9);

                        // Row 1: Employee Code | value | Employee Name | value
                        Lbl("Employee Code"); Val(emp.EmployeeCode);
                        Lbl("Employee Name"); Val(emp.Name);

                        // Row 2: PAN | value | Date of Joining | value
                        Lbl("PAN");           Val(emp.Pan);
                        Lbl("Date of Joining"); Val(emp.JoinDate);

                        // Row 3: Designation | value | Department | value
                        Lbl("Designation");   Val(emp.Designation);
                        Lbl("Department");    Val(emp.Department);

                        // Row 4: Tax Regime | value | Bank A/c | value
                        string bankVal = !string.IsNullOrWhiteSpace(emp.BankAccount)
                            ? emp.BankAccount + (!string.IsNullOrWhiteSpace(emp.BankIfsc) ? " / " + emp.BankIfsc : "")
                            : "—";
                        Lbl("Tax Regime"); Val(annual.ChosenRegime + " Regime");
                        Lbl("Bank A/c");   Val(bankVal);

                        if (lopDays > 0)
                        {
                            Lbl("Loss of Pay Days"); t.Cell().ColumnSpan(3).PaddingBottom(4)
                                .Text($"{lopDays} day(s)").Bold().FontSize(9).FontColor(PdfReports.ErrorColor);
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
            var cLtBlue  = XLColor.FromHtml("#F8FAFC");
            var cLtRow   = XLColor.FromHtml("#EFF6FF");  // alternating row tint
            var cWhite   = XLColor.White;
            var cGray    = XLColor.FromHtml("#94A3B8");
            var cBlack   = XLColor.Black;
            var cRed     = XLColor.FromHtml("#DC2626");

            const string numFmt = "#,##0";   // integer with thousands separator, no ₹ prefix

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

            // Set a numeric amount cell: actual number value + format so Excel right-aligns natively
            void SetAmt(IXLCell cell, double v, XLColor bg, XLColor fg, bool bold = false)
            {
                if (v == 0) { cell.Value = "—"; Sty(cell, bg, cGray, false, 9, XLAlignmentHorizontalValues.Right); }
                else
                {
                    cell.Value = v;
                    cell.Style.NumberFormat.Format = numFmt;
                    Sty(cell, bg, fg, bold, 9, XLAlignmentHorizontalValues.Right);
                }
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

            // Split header (A:B label only | C:D label only) — no amounts in header
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

            // 4-column data row: labelA | valB | labelC | valD  (text values)
            void DataRow4(int row, string la, string va, string lc, string vd, bool shade)
            {
                var bg = shade ? cLtBlue : cWhite;
                ws.Cell(row,1).Value = la; Sty(ws.Cell(row,1), bg, cBlack, false);
                ws.Cell(row,2).Value = va; Sty(ws.Cell(row,2), bg, cBlack, true, 9, XLAlignmentHorizontalValues.Left);
                ws.Cell(row,3).Value = lc; Sty(ws.Cell(row,3), bg, cBlack, false);
                ws.Cell(row,4).Value = vd; Sty(ws.Cell(row,4), bg, cBlack, true, 9, XLAlignmentHorizontalValues.Left);
                ws.Row(row).Height = 16.05;
            }

            // Side-by-side earnings/deductions row — amounts as real numbers
            void EdRow(int row, string el, double ev, string dl, double dv, bool shade)
            {
                var bg = shade ? cLtRow : cWhite;
                ws.Cell(row,1).Value = el; Sty(ws.Cell(row,1), bg, cBlack, false);
                SetAmt(ws.Cell(row,2), ev, bg, cBlack);
                ws.Cell(row,3).Value = dl; Sty(ws.Cell(row,3), bg, cBlack, false);
                SetAmt(ws.Cell(row,4), dv, bg, cBlack);
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
            bool xlProRated = entry.DaysWorked > 0 && entry.DaysWorked < AppConstants.StandardPayrollDays;
            double xlLopAmount = (!xlProRated && xlLopDays > 0) ? Math.Round(entry.GrossPayment * xlLopDays / AppConstants.StandardPayrollDays, 0) : 0;
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
            dedsList.Add(("TDS Deducted", entry.TdsDeducted));
            var deds = dedsList.Where(x => x.Item2 != 0).ToList();
            double gross = earns.Sum(x => x.Item2);
            double net   = gross - ded;

            int maxRows = Math.Max(earns.Count, deds.Count);
            for (int i=0; i<maxRows; i++)
            {
                string el = i<earns.Count ? earns[i].Item1 : "";
                double ev = i<earns.Count ? earns[i].Item2  : 0;
                string dl = i<deds.Count  ? deds[i].Item1   : "";
                double dv = i<deds.Count  ? deds[i].Item2   : 0;
                EdRow(r, el, ev, dl, dv, i%2==0); r++;
            }

            // ── Gross | Total deductions subtotal row ─────────────────────────
            // Write all four cells first, then style — never merge then unmerge (values get lost)
            ws.Cell(r,1).Value = "GROSS EARNINGS";
            ws.Cell(r,2).Value = gross;
            ws.Cell(r,3).Value = "TOTAL DEDUCTIONS";
            ws.Cell(r,4).Value = ded;
            Sty(ws.Cell(r,1), cNavy, cWhite, true, 9, XLAlignmentHorizontalValues.Left);
            ws.Cell(r,1).Style.Alignment.Indent = 1;
            Sty(ws.Cell(r,2), cNavy, cWhite, true, 9, XLAlignmentHorizontalValues.Right);
            ws.Cell(r,2).Style.NumberFormat.Format = numFmt;
            Sty(ws.Cell(r,3), cNavy, cWhite, true, 9, XLAlignmentHorizontalValues.Left);
            ws.Cell(r,3).Style.Alignment.Indent = 1;
            Sty(ws.Cell(r,4), cNavy, cWhite, true, 9, XLAlignmentHorizontalValues.Right);
            ws.Cell(r,4).Style.NumberFormat.Format = numFmt;
            ws.Row(r).Height = 19.95; r++;

            // ── spacer ────────────────────────────────────────────────────────
            ws.Row(r).Height = 6; r++;

            // ── Net Salary row ────────────────────────────────────────────────
            // Label in cols 1-3 (merged after setting value), amount in col 4
            ws.Cell(r,1).Value = "NET SALARY PAYABLE";
            ws.Cell(r,4).Value = net;
            ws.Range(r,1,r,3).Merge();
            Sty(ws.Cell(r,1), cNavy, cWhite, true, 11, XLAlignmentHorizontalValues.Left);
            ws.Cell(r,1).Style.Alignment.Indent = 1;
            Sty(ws.Cell(r,4), cNavy, cWhite, true, 13, XLAlignmentHorizontalValues.Right);
            ws.Cell(r,4).Style.NumberFormat.Format = numFmt;
            ws.Row(r).Height = 24; r++;

            // ── spacer ────────────────────────────────────────────────────────
            ws.Row(r).Height = 6; r++;

            // ── Footer ───────────────────────────────────────────────────────
            ws.Range(r,1,r,4).Merge();
            ws.Cell(r,1).Value = $"Computer generated — CDeTDS v{CDeTDS.Common.AppConstants.AppVersion}  |  {CDeTDS.Common.TaxRules.ActName(entry.FinancialYear)}  |  {DateTime.Now:dd-MMM-yyyy HH:mm}";
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
                string bClass = bold ? " b" : "";
                string co = chosen2 ? " chosen" : "";
                // For total/subtotal rows (chosen2==true) avoid inline color so
                // the row-level CSS (e.g. tr.tot td) controls contrast in print/PDF.
                string style = chosen2 ? "" : " style='color:#374151" + (bold ? ";font-weight:700" : "") + "'";
                string leftCls  = $"num{(chosenOld ? co : "")}{bClass}";
                string rightCls = $"num{(!chosenOld ? co : "")}{bClass}";
                return $"<td class='{leftCls}'{style}>{R(ov)}</td>" +
                       $"<td class='{rightCls}'{style}>{R(nv)}</td>";
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
@media print{{
    body{{background:#fff}}
    .page{{box-shadow:none;margin:0;padding:8mm;width:100%}}
    /* When printing browsers often omit background colors; ensure totals remain readable */
    tr.tot td{{background:none!important;color:#1e3a8a!important;border-top:2px solid #1e4080}}
    tr.tot td.num{{color:#1e3a8a!important}}
}}
</style></head><body><div class='page'>

<div style='text-align:center;background:#1e3a8a;color:#fff;padding:9px 12px;border-radius:4px 4px 0 0;margin-bottom:0'>
  <div style='font-size:14px;font-weight:700;letter-spacing:.3px'>{Esc(deductor?.CompanyName ?? "")}</div>
  {(string.IsNullOrEmpty(deductor?.Tan) ? "" : $"<div style='font-size:9px;opacity:.8;margin-top:2px'>TAN: {Esc(deductor.Tan)}</div>")}
</div>
<div class='hdr' style='margin-top:0;padding-top:8px'>
  <div>
    <div class='co'>Annual Tax Computation — FY {Esc(fy)}</div>
    <div style='font-size:8.5px;color:#6b7280;margin-top:2px'>CDeTDS v{CDeTDS.Common.AppConstants.AppVersion} &nbsp;|&nbsp; {CDeTDS.Common.TaxRules.ActName(fy)}</div>
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
                // Per-month tax figures = chosen-regime annual computation prorated /12.
                // (PayrollRun rows adapted from monthly_salary_entries carry no tax fields,
                //  and the legacy fields held ANNUAL values — never show those per month.)
                double hraExM = Math.Round(chosen.HraExemption / 12.0);
                double stdM   = Math.Round(chosen.StandardDeduction / 12.0);
                double taxM   = Math.Round(chosen.TotalIncome / 12.0);
                for (int mi=0; mi<12; mi++)
                {
                    int m = fyMonths[mi];
                    string bg = mi%2==0 ? "#fff" : "#f8fafc";
                    if (runs.TryGetValue(m, out var pr))
                    {
                        totGross+=pr.GrossSalary; totHra+=hraExM; totStd+=stdM;
                        totTax+=taxM; totTds+=pr.TdsDeducted;
                        totPf+=pr.PfEmployee; totPt+=pr.ProfessionalTax; totNet+=pr.NetPay;
                        string M(double v) => v==0?"—":"₹"+v.ToString("N0");
                        sb.Append($"<tr style='background:{bg};border-bottom:1px solid #f0f2f5'>" +
                            $"<td style='padding:4px 8px;font-weight:600;color:#374151'>{monthNames[mi]}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums'>{M(pr.GrossSalary)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#6b7280'>{M(hraExM)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums;color:#6b7280'>{M(stdM)}</td>" +
                            $"<td style='padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums'>{M(taxM)}</td>" +
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

<div class='footer'>Computer-generated &nbsp;|&nbsp; CDeTDS v{CDeTDS.Common.AppConstants.AppVersion} &nbsp;|&nbsp; Not a legal document &nbsp;|&nbsp; {CDeTDS.Common.TaxRules.ActName(fy)}</div>
</div></body></html>");

            Directory.CreateDirectory(outputFolder);
            string safeName = string.Concat(emp.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileName = $"Computation_{safeName}({emp.Pan})_{fy.Replace("/","-")}.html";
            string path = Path.Combine(outputFolder, fileName);
            File.WriteAllText(path, sb.ToString(), System.Text.Encoding.UTF8);
            return path;
        }

        // ════════════════════════════════════════════════════════════════════
        // ANNUAL TAX COMPUTATION — true PDF (QuestPDF)
        // ════════════════════════════════════════════════════════════════════
        public static string GenerateAnnualPdf(
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
            var runs = yearSummary?.MonthlyRuns ?? new Dictionary<int, PayrollRun>();

            var subParts = new List<string> { deductor?.CompanyName ?? "" };
            if (!string.IsNullOrWhiteSpace(deductor?.Tan)) subParts.Add($"TAN: {deductor.Tan}");
            string subtitle = string.Join("  |  ", subParts.Where(s => !string.IsNullOrWhiteSpace(s)));

            byte[] pdf = PdfReports.BuildA4(
                title:        $"Annual Tax Computation — FY {fy}",
                subtitle:     subtitle,
                centerHeader: true,
                body:         c => c.Column(col =>
                {
                    // ── Employee info box ─────────────────────────────────────
                    col.Item().PaddingBottom(8).Background("#f8fafc").Border(1).BorderColor("#e2e8f0").Padding(8).Table(t =>
                    {
                        t.ColumnsDefinition(d => { d.RelativeColumn(1.2f); d.RelativeColumn(1.8f); d.RelativeColumn(1.2f); d.RelativeColumn(1.8f); });
                        void Lbl(string s) => t.Cell().PaddingBottom(3).Text(s).FontColor(PdfReports.MutedColor).FontSize(8.5f);
                        void Val(string s) => t.Cell().PaddingBottom(3).Text(string.IsNullOrWhiteSpace(s) ? "—" : s).Bold().FontSize(9);
                        Lbl("Employee");      Val(emp.Name);
                        Lbl("PAN");           Val(emp.Pan);
                        Lbl("Employee Code"); Val(emp.EmployeeCode);
                        Lbl("Designation");   Val(emp.Designation);
                        Lbl("Chosen Regime"); Val(annual.ChosenRegime + " Regime ✓");
                        Lbl("Annual Tax (chosen)"); Val($"₹{chosen.TotalTax:N0}");
                    });

                    // ── Computation table ─────────────────────────────────────
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(d => { d.RelativeColumn(2.4f); d.RelativeColumn(1); d.RelativeColumn(1); });

                        void Band(string label) => t.Cell().ColumnSpan(3)
                            .Background("#1e3a8a").PaddingVertical(3).PaddingHorizontal(6)
                            .Text(label).Bold().FontColor("#ffffff").FontSize(8.5f);

                        void Row(string label, double ov, double nv, bool indent = false,
                                 bool subtotal = false, bool total = false, string note = "")
                        {
                            string bg = total ? "#1e3a8a" : subtotal ? "#dbeafe" : "#ffffff";
                            string fg = total ? "#ffffff" : subtotal ? "#1e3a8a" : "#374151";
                            bool   b  = total || subtotal;
                            var lc = t.Cell().Background(bg).PaddingVertical(2.5f).PaddingHorizontal(indent ? 16 : 6);
                            lc.Text(txt =>
                            {
                                var sp = txt.Span(label).FontColor(fg).FontSize(b ? 9 : 8.5f);
                                if (b) sp.Bold();
                                if (note.Length > 0) txt.Span($"  [{note}]").FontColor("#94a3b8").FontSize(7);
                            });
                            void Amt(double v, bool chosenSide)
                            {
                                var cc = t.Cell().Background(bg).PaddingVertical(2.5f).PaddingHorizontal(6).AlignRight();
                                var sp = cc.Text(v == 0 ? "—" : $"₹{v:N0}").FontColor(fg).FontSize(b ? 9 : 8.5f);
                                if (b || chosenSide) sp.Bold();
                            }
                            Amt(ov, chosenOld); Amt(nv, !chosenOld);
                        }

                        // Header
                        t.Cell().Background("#1e3a8a").PaddingVertical(4).PaddingHorizontal(6)
                            .Text("Particulars").Bold().FontColor("#ffffff").FontSize(9);
                        t.Cell().Background("#1e3a8a").PaddingVertical(4).PaddingHorizontal(6).AlignRight()
                            .Text("Old Regime" + (chosenOld ? "  ✓" : "")).Bold().FontColor("#ffffff").FontSize(9);
                        t.Cell().Background("#1e3a8a").PaddingVertical(4).PaddingHorizontal(6).AlignRight()
                            .Text("New Regime" + (!chosenOld ? "  ✓" : "")).Bold().FontColor("#ffffff").FontSize(9);

                        // A. Gross Salary
                        Band("A. Gross Salary");
                        if (ss.Basic > 0)            Row("Basic Salary",          ss.Basic*12,            ss.Basic*12,            indent:true);
                        if (ss.Hra > 0)              Row("House Rent Allowance",   ss.Hra*12,              ss.Hra*12,              indent:true);
                        if (ss.Da > 0)               Row("Dearness Allowance",     ss.Da*12,               ss.Da*12,               indent:true);
                        if (ss.SpecialAllowance > 0) Row("Special Allowance",      ss.SpecialAllowance*12, ss.SpecialAllowance*12, indent:true);
                        if (ss.MedicalAllowance > 0) Row("Medical Allowance",      ss.MedicalAllowance*12, ss.MedicalAllowance*12, indent:true);
                        if (ss.Lta > 0)              Row("Leave Travel Allowance", ss.Lta*12,              ss.Lta*12,              indent:true);
                        foreach (var comp in ss.Components.Where(x => x.Received > 0))
                            Row(comp.Name, comp.Received*12, comp.Received*12, indent:true, note:comp.RuleRef);
                        if (ss.AnnualBonus > 0)     Row("Performance Bonus", ss.AnnualBonus,     ss.AnnualBonus,     indent:true);
                        if (ss.AnnualIncentive > 0) Row("Sales / Incentive", ss.AnnualIncentive, ss.AnnualIncentive, indent:true);
                        Row("Gross Salary (A)", o.GrossSalary, n.GrossSalary, subtotal:true);

                        // B. Exemptions u/s 10
                        bool hasEx = o.HraExemption > 0 || n.HraExemption > 0
                            || o.Sec10Items.Any(x => x.OldRegime > 0 || x.NewRegime > 0);
                        if (hasEx)
                        {
                            Band("B. Less: Exemptions u/s 10");
                            if (o.HraExemption > 0 || n.HraExemption > 0)
                                Row("HRA Exemption", o.HraExemption, n.HraExemption, indent:true, note:"Sec 10(13A)");
                            foreach (var item in o.Sec10Items.Where(x => x.Name != "HRA"))
                                Row(item.Name, item.OldRegime, item.NewRegime, indent:true, note:item.RuleRef);
                            double exOld = o.HraExemption + o.Sec10Items.Where(x=>x.Name!="HRA").Sum(x=>x.OldRegime);
                            double exNew = n.HraExemption + o.Sec10Items.Where(x=>x.Name!="HRA").Sum(x=>x.NewRegime);
                            Row("Total Exemptions (B)", exOld, exNew, subtotal:true);
                        }

                        // C. Net Taxable Salary
                        double netOld = o.GrossSalary - (o.HraExemption + o.Sec10Items.Where(x=>x.Name!="HRA").Sum(x=>x.OldRegime));
                        double netNew = n.GrossSalary - (n.HraExemption + o.Sec10Items.Where(x=>x.Name!="HRA").Sum(x=>x.NewRegime));
                        Band("C. Net Taxable Salary (A − B)");
                        Row("Net Taxable Salary (C)", netOld, netNew, subtotal:true);

                        // D. Deductions
                        Band("D. Less: Deductions");
                        Row("Standard Deduction", o.StandardDeduction, n.StandardDeduction, indent:true, note:"u/s 16(ia)");
                        if (o.ProfTaxDeduction > 0 || n.ProfTaxDeduction > 0)
                            Row("Professional Tax", o.ProfTaxDeduction, n.ProfTaxDeduction, indent:true, note:"u/s 16(iii)");
                        if (o.Chapter6A > 0)
                            Row("Chapter VI-A Deductions", o.Chapter6A, 0, indent:true, note:"80C/80D/80G etc.");
                        if (o.NpsEmployer80CCD2 > 0 || n.NpsEmployer80CCD2 > 0)
                            Row("NPS Employer 80CCD(2)", o.NpsEmployer80CCD2, n.NpsEmployer80CCD2, indent:true);
                        Row("Total Deductions (D)",
                            o.StandardDeduction + o.ProfTaxDeduction + o.Chapter6A + o.NpsEmployer80CCD2,
                            n.StandardDeduction + n.ProfTaxDeduction + n.Chapter6A + n.NpsEmployer80CCD2,
                            subtotal:true);

                        // E. Other sources
                        if (o.IncomeOtherSources > 0 || n.IncomeOtherSources > 0)
                        {
                            Band("E. Add: Income from Other Sources");
                            Row("Interest / Other Income", o.IncomeOtherSources, n.IncomeOtherSources, indent:true);
                        }

                        // Net taxable income
                        Row("Net Taxable Income (C − D + E)", o.TotalIncome, n.TotalIncome, subtotal:true);

                        // F. Tax computation
                        Band("F. Tax Computation");
                        Row("Tax on Income (Slab)", o.TaxOnIncome, n.TaxOnIncome, indent:true);
                        if (o.Rebate87A > 0 || n.Rebate87A > 0)
                            Row("Less: Rebate u/s 87A", o.Rebate87A, n.Rebate87A, indent:true);
                        Row("Tax After Rebate", o.TaxAfterRebate, n.TaxAfterRebate, indent:true);
                        if (o.Surcharge > 0 || n.Surcharge > 0)
                            Row("Add: Surcharge", o.Surcharge, n.Surcharge, indent:true);
                        if (o.Cess > 0 || n.Cess > 0)
                            Row("Add: Health & Education Cess (4%)", o.Cess, n.Cess, indent:true);
                        Row("TOTAL TAX PAYABLE", o.TotalTax, n.TotalTax, total:true);
                    });

                    // ── Monthly statement ─────────────────────────────────────
                    if (runs.Count > 0)
                    {
                        col.Item().PaddingTop(12).Background("#1e3a8a").PaddingVertical(4).PaddingHorizontal(6)
                            .Text("Monthly Salary Statement — April to March").Bold().FontColor("#ffffff").FontSize(9);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(d =>
                            {
                                d.ConstantColumn(34);
                                for (int i = 0; i < 8; i++) d.RelativeColumn(1);
                            });
                            string[] hdrs = { "Month","Gross","HRA Ex.","Std. Ded.","Taxable","TDS","PF","PT","Net Pay" };
                            foreach (var h in hdrs)
                                t.Cell().Background("#dbeafe").PaddingVertical(3).PaddingHorizontal(4)
                                    .Element(x => h == "Month" ? x : x.AlignRight())
                                    .Text(h).Bold().FontColor("#1e3a8a").FontSize(7.5f);

                            // Per-month tax figures = chosen-regime annual computation prorated /12
                            double hraExM = Math.Round(chosen.HraExemption / 12.0);
                            double stdM   = Math.Round(chosen.StandardDeduction / 12.0);
                            double taxM   = Math.Round(chosen.TotalIncome / 12.0);
                            int[] fyMonths = { 4,5,6,7,8,9,10,11,12,1,2,3 };
                            string[] mNames = { "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };
                            double tg=0,th=0,ts2=0,tt=0,td2=0,tp=0,tpt=0,tn=0;

                            for (int mi = 0; mi < 12; mi++)
                            {
                                string bg = mi % 2 == 0 ? "#ffffff" : "#f8fafc";
                                void MC(string s, bool right = true, string fg = "#374151", bool bold = false)
                                {
                                    var cc = t.Cell().Background(bg).PaddingVertical(2).PaddingHorizontal(4);
                                    var el = right ? cc.AlignRight() : cc;
                                    var sp = el.Text(s).FontColor(fg).FontSize(7.5f);
                                    if (bold) sp.Bold();
                                }
                                if (runs.TryGetValue(fyMonths[mi], out var pr))
                                {
                                    tg+=pr.GrossSalary; th+=hraExM; ts2+=stdM; tt+=taxM;
                                    td2+=pr.TdsDeducted; tp+=pr.PfEmployee; tpt+=pr.ProfessionalTax; tn+=pr.NetPay;
                                    string F(double v) => v == 0 ? "—" : $"₹{v:N0}";
                                    MC(mNames[mi], right:false, bold:true);
                                    MC(F(pr.GrossSalary)); MC(F(hraExM), fg:"#6b7280"); MC(F(stdM), fg:"#6b7280");
                                    MC(F(taxM)); MC(F(pr.TdsDeducted), fg:"#dc2626", bold:true);
                                    MC(F(pr.PfEmployee), fg:"#6b7280"); MC(F(pr.ProfessionalTax), fg:"#6b7280");
                                    MC(F(pr.NetPay), fg:"#15803d", bold:true);
                                }
                                else
                                {
                                    MC(mNames[mi], right:false, fg:"#9ca3af");
                                    t.Cell().ColumnSpan(8).Background(bg).PaddingVertical(2).PaddingHorizontal(4)
                                        .Text("— not entered —").Italic().FontColor("#d1d5db").FontSize(7.5f);
                                }
                            }
                            // Total row
                            void TC(string s, bool right = true, string fg = "#ffffff")
                            {
                                var cc = t.Cell().Background("#1e3a8a").PaddingVertical(3).PaddingHorizontal(4);
                                var el = right ? cc.AlignRight() : cc;
                                el.Text(s).Bold().FontColor(fg).FontSize(8);
                            }
                            string FT(double v) => $"₹{v:N0}";
                            TC("Total", right:false);
                            TC(FT(tg)); TC(FT(th)); TC(FT(ts2)); TC(FT(tt));
                            TC(FT(td2), fg:"#fca5a5"); TC(FT(tp)); TC(FT(tpt)); TC(FT(tn), fg:"#86efac");
                        });
                    }

                    // ── TDS position strip ────────────────────────────────────
                    double tdsPaid = runs.Count > 0 ? runs.Values.Sum(x => x.TdsDeducted) : annual.YtdTdsDeducted;
                    double balance = chosen.TotalTax - tdsPaid;
                    col.Item().PaddingTop(10).Border(1).BorderColor("#bfdbfe").Row(row =>
                    {
                        void Box(IContainer cc, string label, string value, string vColor)
                        {
                            cc.Background("#eff6ff").PaddingVertical(6).PaddingHorizontal(8).Column(b =>
                            {
                                b.Item().AlignCenter().Text(label).FontColor("#1e40af").FontSize(7.5f);
                                b.Item().AlignCenter().Text(value).Bold().FontColor(vColor).FontSize(10);
                            });
                        }
                        row.RelativeItem().Element(x => Box(x, "Tax Payable (Chosen)", $"₹{chosen.TotalTax:N0}", "#1e3a8a"));
                        row.RelativeItem().Element(x => Box(x, "TDS Paid (YTD)", $"₹{tdsPaid:N0}", "#1e3a8a"));
                        row.RelativeItem().Element(x => Box(x, "Balance Tax",
                            balance < 0 ? $"(₹{Math.Abs(balance):N0})" : $"₹{balance:N0}",
                            balance < 0 ? "#166534" : balance > 0 ? "#dc2626" : "#1e3a8a"));
                    });
                }));

            Directory.CreateDirectory(outputFolder);
            string safeName = string.Concat(emp.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileName = $"Computation_{safeName}({emp.Pan})_{fy.Replace("/","-")}.pdf";
            string path = Path.Combine(outputFolder, fileName);
            File.WriteAllBytes(path, pdf);
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
            EmployeeYearSummary? yearSummary = null,
            List<MonthlySalaryStatRow>? rows = null)
        {
            rows ??= new List<MonthlySalaryStatRow>();
            string co   = deductor?.CompanyName ?? "";
            string addr = string.Join(", ", new[]{ deductor?.Address, deductor?.City, deductor?.State }.Where(s => !string.IsNullOrWhiteSpace(s)));
            string C(double v) => v == 0 ? "—" : "₹" + v.ToString("N0");

            // Fallback column presence — if entries missing, show zeros
            bool hasBasic       = rows.Any(r => r.Basic          > 0);
            bool hasHra         = rows.Any(r => r.Hra            > 0);
            bool hasDa          = rows.Any(r => r.Da             > 0);
            bool hasSpecial     = rows.Any(r => r.Special        > 0);
            bool hasMedical     = rows.Any(r => r.Medical        > 0);
            bool hasLta         = rows.Any(r => r.Lta            > 0);
            bool hasBonus       = rows.Any(r => r.Bonus          > 0);
            bool hasCommission  = rows.Any(r => r.Commission     > 0);
            bool hasAdvance     = rows.Any(r => r.AdvanceSalary  > 0);
            bool hasArrears     = rows.Any(r => r.Arrears        > 0);
            bool hasNpsEmpl     = rows.Any(r => r.NpsEmployer    > 0);
            bool hasPerq        = rows.Any(r => r.PerqTaxable    > 0);
            bool hasLeaveEnc    = rows.Any(r => r.LeaveEncTaxable> 0);
            bool hasOther       = rows.Any(r => r.Other          > 0);
            bool hasHraEx       = rows.Any(r => r.HraEx          > 0);
            bool hasOtherEx     = rows.Any(r => r.OtherEx        > 0);
            bool hasChap6A      = rows.Any(r => r.Chap6A         > 0);
            bool hasNps         = rows.Any(r => r.Nps80CCD2      > 0);
            bool hasSurch       = rows.Any(r => r.Surcharge      > 0);
            bool hasRebate      = rows.Any(r => r.Rebate87A      > 0);
            bool hasOtherSrc    = rows.Any(r => r.OtherSources   > 0);
            bool hasPf          = rows.Any(r => r.Pf             > 0);
            bool hasEsi         = rows.Any(r => r.Esi            > 0);

            var sb2 = new System.Text.StringBuilder();
            sb2.Append($@"<!DOCTYPE html><html><head><meta charset='utf-8'>
<title>Monthly Salary Statement — {Esc(emp.Name)} — FY {fy}</title>
<style>
*{{box-sizing:border-box;margin:0;padding:0}}
body{{font-family:Arial,sans-serif;font-size:9.5px;color:#1a1a2e;background:#fff;padding:18px}}
.hdr{{text-align:center;margin-bottom:12px}}
.hdr .co{{font-size:13px;font-weight:700;color:#1e3a8a}}
.hdr .addr{{font-size:8.5px;color:#64748b;margin-top:2px}}
.hdr .title{{margin-top:8px;font-size:11px;font-weight:700;color:#fff;background:#1e3a8a;display:inline-block;padding:4px 22px;border-radius:3px;letter-spacing:.4px}}
.hdr .emp{{margin-top:7px;font-size:9px;color:#374151}}
.hdr .emp b{{color:#1e3a8a}}
table{{width:100%;border-collapse:collapse;margin-top:8px;font-size:8.5px}}
th,td{{border:1px solid #dde1e7;padding:3px 5px;text-align:right;font-variant-numeric:tabular-nums;white-space:nowrap}}
th{{font-weight:700}}
td:first-child,th:first-child{{text-align:left}}
/* section header rows */
tr.sec-a th{{background:#1e3a8a;color:#fff;text-align:center}}
tr.sec-b th{{background:#0369a1;color:#fff;text-align:center}}
tr.sec-c th{{background:#065f46;color:#fff;text-align:center}}
tr.sec-d th{{background:#7c3aed;color:#fff;text-align:center}}
tr.sec-e th{{background:#b45309;color:#fff;text-align:center}}
tr.sec-nti th{{background:#166534;color:#fff;text-align:center}}
tr.sec-f th{{background:#9f1239;color:#fff;text-align:center}}
tr.sec-sal th{{background:#374151;color:#fff;text-align:center}}
/* subtotal rows */
tr.sub td{{font-weight:700;background:#f0f9ff;color:#1e3a8a}}
tr.sub-b td{{font-weight:700;background:#e0f2fe;color:#0369a1}}
tr.sub-c td{{font-weight:700;background:#d1fae5;color:#065f46;font-size:9px}}
tr.sub-d td{{font-weight:700;background:#ede9fe;color:#7c3aed}}
tr.sub-nti td{{font-weight:700;background:#dcfce7;color:#166534;font-size:9px}}
tr.sub-f td{{font-weight:700;background:#fff1f2;color:#9f1239}}
/* total row */
tr.tot td{{background:#1e3a8a;color:#fff;font-weight:700;font-size:9px}}
tr.tot td.g{{color:#86efac}}
tr.tot td.r{{color:#fca5a5}}
/* value cells */
td.red{{color:#dc2626}} td.grn{{color:#15803d;font-weight:700}}
td.ded{{color:#7c3aed}} td.tax{{color:#9f1239;font-weight:600}} td.dim{{color:#9ca3af}}
.footer{{margin-top:10px;text-align:center;font-size:7.5px;color:#94a3b8}}
</style></head><body>
<div class='hdr'>
  <div class='co'>{Esc(co)}</div>
  <div class='addr'>{Esc(addr)}</div>
  <div class='title'>Monthly Salary Statement — April to March</div>
  <div class='emp'>Employee: <b>{Esc(emp.Name)}</b> &nbsp;|&nbsp; Code: <b>{Esc(emp.EmployeeCode)}</b> &nbsp;|&nbsp; PAN: <b>{Esc(emp.Pan)}</b> &nbsp;|&nbsp; FY: <b>{fy}</b></div>
</div>
<table><thead>");

            // ── Header row ──
            sb2.Append("<tr class='sec-a'>");
            sb2.Append("<th>Month</th>");
            // A. Gross
            if (hasBasic)      sb2.Append("<th>Basic</th>");
            if (hasHra)        sb2.Append("<th>HRA</th>");
            if (hasDa)         sb2.Append("<th>DA</th>");
            if (hasSpecial)    sb2.Append("<th>Special</th>");
            if (hasMedical)    sb2.Append("<th>Medical</th>");
            if (hasLta)        sb2.Append("<th>LTA</th>");
            if (hasBonus)      sb2.Append("<th>Bonus</th>");
            if (hasCommission) sb2.Append("<th>Commission</th>");
            if (hasAdvance)    sb2.Append("<th>Advance</th>");
            if (hasArrears)    sb2.Append("<th>Arrears</th>");
            if (hasNpsEmpl)    sb2.Append("<th>NPS (Empr)</th>");
            if (hasPerq)       sb2.Append("<th>Perq.</th>");
            if (hasLeaveEnc)   sb2.Append("<th>Leave Enc.</th>");
            if (hasOther)      sb2.Append("<th>Other</th>");
            sb2.Append("<th style='background:#15803d'>A. Gross</th>");
            // B. Exemptions
            if (hasHraEx)   sb2.Append("<th style='background:#0369a1'>HRA Ex.</th>");
            if (hasOtherEx) sb2.Append("<th style='background:#0369a1'>Other Ex.</th>");
            sb2.Append("<th style='background:#0369a1'>B. Total Ex.</th>");
            // C
            sb2.Append("<th style='background:#065f46'>C. Net Taxable</th>");
            // D. Deductions
            sb2.Append("<th style='background:#6d28d9'>Std Ded.</th>");
            if (rows.Any(r=>r.ProfTax>0)) sb2.Append("<th style='background:#6d28d9'>Prof.Tax</th>");
            if (hasChap6A)  sb2.Append("<th style='background:#6d28d9'>Ch.VI-A</th>");
            if (hasNps)     sb2.Append("<th style='background:#6d28d9'>NPS 80CCD(2)</th>");
            sb2.Append("<th style='background:#7c3aed'>D. Total Ded.</th>");
            // E
            if (hasOtherSrc) sb2.Append("<th style='background:#b45309'>E. Other Src.</th>");
            // Net Taxable Income
            sb2.Append("<th style='background:#166534'>Net Taxable Inc.</th>");
            // F. Tax
            sb2.Append("<th style='background:#be123c'>Tax on Inc.</th>");
            if (hasRebate)  sb2.Append("<th style='background:#be123c'>Rebate 87A</th>");
            sb2.Append("<th style='background:#be123c'>Tax/Rebate</th>");
            if (hasSurch)   sb2.Append("<th style='background:#be123c'>Surcharge</th>");
            sb2.Append("<th style='background:#be123c'>Cess</th>");
            sb2.Append("<th style='background:#9f1239'>F. Total Tax</th>");
            sb2.Append("<th style='background:#9f1239'>TDS Deducted</th>");
            // Salary deductions / net
            if (hasPf)  sb2.Append("<th style='background:#374151'>PF</th>");
            if (hasEsi) sb2.Append("<th style='background:#374151'>ESI</th>");
            sb2.Append("<th style='background:#14532d'>Net Pay</th>");
            sb2.Append("</tr></thead><tbody>");

            // ── Data rows ──
            // Totals accumulators
            double tBasic=0,tHra=0,tDa=0,tSpec=0,tMed=0,tLta=0;
            double tBonus=0,tComm=0,tAdv=0,tArr=0,tNpsE=0,tPerq=0,tLeaveEnc=0;
            double tOth=0,tGross=0;
            double tHraEx=0,tOthEx=0,tTotEx=0,tNetTaxSal=0;
            double tStd=0,tPt=0,tC6a=0,tNps=0,tTotDed=0;
            double tOtSrc=0,tNti=0;
            double tTaxInc=0,tReb=0,tTaxReb=0,tSc=0,tCess=0,tTotTax=0,tTds=0;
            double tPf=0,tEsi=0,tNet=0;

            int[] fyM2 = { 4,5,6,7,8,9,10,11,12,1,2,3 };
            string[] mn2 = { "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };
            var rowByMonth = rows.ToDictionary(r => r.MonthLabel);

            for (int mi = 0; mi < 12; mi++)
            {
                string lbl = mn2[mi];
                string bg  = mi % 2 == 0 ? "#fff" : "#f8fafc";
                if (!rowByMonth.TryGetValue(lbl, out var r))
                {
                    // count total columns for the colspan
                    int spanCount = (hasBasic?1:0)+(hasHra?1:0)+(hasDa?1:0)+(hasSpecial?1:0)+(hasMedical?1:0)+(hasLta?1:0)
                        +(hasBonus?1:0)+(hasCommission?1:0)+(hasAdvance?1:0)+(hasArrears?1:0)
                        +(hasNpsEmpl?1:0)+(hasPerq?1:0)+(hasLeaveEnc?1:0)+(hasOther?1:0)+1
                        +(hasHraEx?1:0)+(hasOtherEx?1:0)+1+1
                        +(rows.Any(x=>x.ProfTax>0)?1:0)+(hasChap6A?1:0)+(hasNps?1:0)+1+1
                        +(hasOtherSrc?1:0)+1
                        +1+(hasRebate?1:0)+1+(hasSurch?1:0)+1+1+1
                        +(hasPf?1:0)+(hasEsi?1:0)+1;
                    sb2.Append($"<tr style='background:{bg}'><td style='color:#374151;font-weight:600'>{lbl}</td><td colspan='{spanCount}' style='color:#d1d5db;text-align:center;font-style:italic'>— not entered —</td></tr>");
                    continue;
                }
                tBasic+=r.Basic; tHra+=r.Hra; tDa+=r.Da; tSpec+=r.Special; tMed+=r.Medical; tLta+=r.Lta;
                tBonus+=r.Bonus; tComm+=r.Commission; tAdv+=r.AdvanceSalary; tArr+=r.Arrears;
                tNpsE+=r.NpsEmployer; tPerq+=r.PerqTaxable; tLeaveEnc+=r.LeaveEncTaxable;
                tOth+=r.Other; tGross+=r.GrossTotal;
                tHraEx+=r.HraEx; tOthEx+=r.OtherEx; tTotEx+=r.TotalEx; tNetTaxSal+=r.NetTaxableSalary;
                tStd+=r.StdDed; tPt+=r.ProfTax; tC6a+=r.Chap6A; tNps+=r.Nps80CCD2; tTotDed+=r.TotalDed;
                tOtSrc+=r.OtherSources; tNti+=r.NetTaxableIncome;
                tTaxInc+=r.TaxOnIncome; tReb+=r.Rebate87A; tTaxReb+=r.TaxAfterRebate; tSc+=r.Surcharge; tCess+=r.Cess; tTotTax+=r.TotalTax; tTds+=r.TdsDeducted;
                tPf+=r.Pf; tEsi+=r.Esi; tNet+=r.NetPay;

                sb2.Append($"<tr style='background:{bg}'>");
                sb2.Append($"<td style='font-weight:600;color:#374151'>{lbl}</td>");
                if (hasBasic)      sb2.Append($"<td>{C(r.Basic)}</td>");
                if (hasHra)        sb2.Append($"<td>{C(r.Hra)}</td>");
                if (hasDa)         sb2.Append($"<td>{C(r.Da)}</td>");
                if (hasSpecial)    sb2.Append($"<td>{C(r.Special)}</td>");
                if (hasMedical)    sb2.Append($"<td>{C(r.Medical)}</td>");
                if (hasLta)        sb2.Append($"<td>{C(r.Lta)}</td>");
                if (hasBonus)      sb2.Append($"<td>{C(r.Bonus)}</td>");
                if (hasCommission) sb2.Append($"<td>{C(r.Commission)}</td>");
                if (hasAdvance)    sb2.Append($"<td>{C(r.AdvanceSalary)}</td>");
                if (hasArrears)    sb2.Append($"<td>{C(r.Arrears)}</td>");
                if (hasNpsEmpl)    sb2.Append($"<td>{C(r.NpsEmployer)}</td>");
                if (hasPerq)       sb2.Append($"<td>{C(r.PerqTaxable)}</td>");
                if (hasLeaveEnc)   sb2.Append($"<td>{C(r.LeaveEncTaxable)}</td>");
                if (hasOther)      sb2.Append($"<td>{C(r.Other)}</td>");
                sb2.Append($"<td class='sub' style='color:#1e3a8a;font-weight:700'>{C(r.GrossTotal)}</td>");
                if (hasHraEx)   sb2.Append($"<td class='dim'>{C(r.HraEx)}</td>");
                if (hasOtherEx) sb2.Append($"<td class='dim'>{C(r.OtherEx)}</td>");
                sb2.Append($"<td style='color:#0369a1;font-weight:700'>{C(r.TotalEx)}</td>");
                sb2.Append($"<td style='color:#065f46;font-weight:700'>{C(r.NetTaxableSalary)}</td>");
                sb2.Append($"<td class='ded'>{C(r.StdDed)}</td>");
                if (rows.Any(x=>x.ProfTax>0)) sb2.Append($"<td class='ded'>{C(r.ProfTax)}</td>");
                if (hasChap6A)  sb2.Append($"<td class='ded'>{C(r.Chap6A)}</td>");
                if (hasNps)     sb2.Append($"<td class='ded'>{C(r.Nps80CCD2)}</td>");
                sb2.Append($"<td style='color:#7c3aed;font-weight:700'>{C(r.TotalDed)}</td>");
                if (hasOtherSrc) sb2.Append($"<td style='color:#b45309'>{C(r.OtherSources)}</td>");
                sb2.Append($"<td style='color:#166534;font-weight:700'>{C(r.NetTaxableIncome)}</td>");
                sb2.Append($"<td class='tax'>{C(r.TaxOnIncome)}</td>");
                if (hasRebate)  sb2.Append($"<td class='dim'>{C(r.Rebate87A)}</td>");
                sb2.Append($"<td class='tax'>{C(r.TaxAfterRebate)}</td>");
                if (hasSurch)   sb2.Append($"<td class='tax'>{C(r.Surcharge)}</td>");
                sb2.Append($"<td class='tax'>{C(r.Cess)}</td>");
                sb2.Append($"<td style='color:#9f1239;font-weight:700'>{C(r.TotalTax)}</td>");
                sb2.Append($"<td style='color:#dc2626;font-weight:700'>{C(r.TdsDeducted)}</td>");
                if (hasPf)  sb2.Append($"<td class='dim'>{C(r.Pf)}</td>");
                if (hasEsi) sb2.Append($"<td class='dim'>{C(r.Esi)}</td>");
                sb2.Append($"<td class='grn'>{C(r.NetPay)}</td>");
                sb2.Append("</tr>");
            }

            // ── Total row ──
            sb2.Append("<tr class='tot'>");
            sb2.Append("<td>Total</td>");
            if (hasBasic)      sb2.Append($"<td>{C(tBasic)}</td>");
            if (hasHra)        sb2.Append($"<td>{C(tHra)}</td>");
            if (hasDa)         sb2.Append($"<td>{C(tDa)}</td>");
            if (hasSpecial)    sb2.Append($"<td>{C(tSpec)}</td>");
            if (hasMedical)    sb2.Append($"<td>{C(tMed)}</td>");
            if (hasLta)        sb2.Append($"<td>{C(tLta)}</td>");
            if (hasBonus)      sb2.Append($"<td>{C(tBonus)}</td>");
            if (hasCommission) sb2.Append($"<td>{C(tComm)}</td>");
            if (hasAdvance)    sb2.Append($"<td>{C(tAdv)}</td>");
            if (hasArrears)    sb2.Append($"<td>{C(tArr)}</td>");
            if (hasNpsEmpl)    sb2.Append($"<td>{C(tNpsE)}</td>");
            if (hasPerq)       sb2.Append($"<td>{C(tPerq)}</td>");
            if (hasLeaveEnc)   sb2.Append($"<td>{C(tLeaveEnc)}</td>");
            if (hasOther)      sb2.Append($"<td>{C(tOth)}</td>");
            sb2.Append($"<td class='g'>{C(tGross)}</td>");
            if (hasHraEx)   sb2.Append($"<td>{C(tHraEx)}</td>");
            if (hasOtherEx) sb2.Append($"<td>{C(tOthEx)}</td>");
            sb2.Append($"<td>{C(tTotEx)}</td>");
            sb2.Append($"<td class='g'>{C(tNetTaxSal)}</td>");
            sb2.Append($"<td class='r'>{C(tStd)}</td>");
            if (rows.Any(x=>x.ProfTax>0)) sb2.Append($"<td class='r'>{C(tPt)}</td>");
            if (hasChap6A)  sb2.Append($"<td class='r'>{C(tC6a)}</td>");
            if (hasNps)     sb2.Append($"<td class='r'>{C(tNps)}</td>");
            sb2.Append($"<td class='r'>{C(tTotDed)}</td>");
            if (hasOtherSrc) sb2.Append($"<td>{C(tOtSrc)}</td>");
            sb2.Append($"<td class='g'>{C(tNti)}</td>");
            sb2.Append($"<td class='r'>{C(tTaxInc)}</td>");
            if (hasRebate)  sb2.Append($"<td>{C(tReb)}</td>");
            sb2.Append($"<td class='r'>{C(tTaxReb)}</td>");
            if (hasSurch)   sb2.Append($"<td class='r'>{C(tSc)}</td>");
            sb2.Append($"<td class='r'>{C(tCess)}</td>");
            sb2.Append($"<td class='r'>{C(tTotTax)}</td>");
            sb2.Append($"<td class='r'>{C(tTds)}</td>");
            if (hasPf)  sb2.Append($"<td>{C(tPf)}</td>");
            if (hasEsi) sb2.Append($"<td>{C(tEsi)}</td>");
            sb2.Append($"<td class='g'>{C(tNet)}</td>");
            sb2.Append("</tr></tbody></table>");
            sb2.Append($"<div class='footer'>Computer-generated &nbsp;|&nbsp; CDeTDS v{AppConstants.AppVersion} &nbsp;|&nbsp; {DateTime.Now:dd-MMM-yyyy HH:mm} &nbsp;|&nbsp; Not a legal document</div>");
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
            EmployeeYearSummary? yearSummary = null,
            List<MonthlySalaryStatRow>? rows = null)
        {
            rows ??= new List<MonthlySalaryStatRow>();

            bool hasBasic       = rows.Any(r => r.Basic          > 0);
            bool hasHra         = rows.Any(r => r.Hra            > 0);
            bool hasDa          = rows.Any(r => r.Da             > 0);
            bool hasSpecial     = rows.Any(r => r.Special        > 0);
            bool hasMedical     = rows.Any(r => r.Medical        > 0);
            bool hasLta         = rows.Any(r => r.Lta            > 0);
            bool hasBonus       = rows.Any(r => r.Bonus          > 0);
            bool hasCommission  = rows.Any(r => r.Commission     > 0);
            bool hasAdvance     = rows.Any(r => r.AdvanceSalary  > 0);
            bool hasArrears     = rows.Any(r => r.Arrears        > 0);
            bool hasNpsEmpl     = rows.Any(r => r.NpsEmployer    > 0);
            bool hasPerq        = rows.Any(r => r.PerqTaxable    > 0);
            bool hasLeaveEnc    = rows.Any(r => r.LeaveEncTaxable> 0);
            bool hasOther       = rows.Any(r => r.Other          > 0);
            bool hasHraEx       = rows.Any(r => r.HraEx          > 0);
            bool hasOtherEx     = rows.Any(r => r.OtherEx        > 0);
            bool hasChap6A      = rows.Any(r => r.Chap6A         > 0);
            bool hasNps         = rows.Any(r => r.Nps80CCD2      > 0);
            bool hasSurch       = rows.Any(r => r.Surcharge      > 0);
            bool hasRebate      = rows.Any(r => r.Rebate87A      > 0);
            bool hasOtherSrc    = rows.Any(r => r.OtherSources   > 0);
            bool hasPt          = rows.Any(r => r.ProfTax        > 0);
            bool hasPf          = rows.Any(r => r.Pf             > 0);
            bool hasEsi         = rows.Any(r => r.Esi            > 0);

            using var wb = new XLWorkbook();
            var ws = wb.AddWorksheet("Monthly Salary");

            // Build ordered column definitions
            var cols = new List<(string Label, string Group, string BgHex, Func<MonthlySalaryStatRow,double> Fn)>();
            // Month is col 1 (handled separately)
            // A. Gross
            if (hasBasic)      cols.Add(("Basic",       "A. Gross Salary", "#1e3a8a", r => r.Basic));
            if (hasHra)        cols.Add(("HRA",          "A. Gross Salary", "#1e3a8a", r => r.Hra));
            if (hasDa)         cols.Add(("DA",           "A. Gross Salary", "#1e3a8a", r => r.Da));
            if (hasSpecial)    cols.Add(("Special",      "A. Gross Salary", "#1e3a8a", r => r.Special));
            if (hasMedical)    cols.Add(("Medical",      "A. Gross Salary", "#1e3a8a", r => r.Medical));
            if (hasLta)        cols.Add(("LTA",          "A. Gross Salary", "#1e3a8a", r => r.Lta));
            if (hasBonus)      cols.Add(("Bonus",        "A. Gross Salary", "#1e3a8a", r => r.Bonus));
            if (hasCommission) cols.Add(("Commission",   "A. Gross Salary", "#1e3a8a", r => r.Commission));
            if (hasAdvance)    cols.Add(("Advance Sal.", "A. Gross Salary", "#1e3a8a", r => r.AdvanceSalary));
            if (hasArrears)    cols.Add(("Arrears",      "A. Gross Salary", "#1e3a8a", r => r.Arrears));
            if (hasNpsEmpl)    cols.Add(("NPS (Empr)",   "A. Gross Salary", "#1e3a8a", r => r.NpsEmployer));
            if (hasPerq)       cols.Add(("Perquisites",  "A. Gross Salary", "#1e3a8a", r => r.PerqTaxable));
            if (hasLeaveEnc)   cols.Add(("Leave Enc.",   "A. Gross Salary", "#1e3a8a", r => r.LeaveEncTaxable));
            if (hasOther)      cols.Add(("Other",        "A. Gross Salary", "#1e3a8a", r => r.Other));
            cols.Add(("A. Gross",           "A. Gross Salary", "#15803d", r => r.GrossTotal));
            // B. Exemptions
            if (hasHraEx)   cols.Add(("HRA Exemption",  "B. Exemptions", "#0369a1", r => r.HraEx));
            if (hasOtherEx) cols.Add(("Other Exemptions","B. Exemptions", "#0369a1", r => r.OtherEx));
            cols.Add(("B. Total Exemptions","B. Exemptions", "#0369a1", r => r.TotalEx));
            // C
            cols.Add(("C. Net Taxable Salary","C. Net Taxable", "#065f46", r => r.NetTaxableSalary));
            // D
            cols.Add(("Standard Ded. u/s 16","D. Deductions", "#6d28d9", r => r.StdDed));
            if (hasPt)      cols.Add(("Professional Tax", "D. Deductions", "#6d28d9", r => r.ProfTax));
            if (hasChap6A)  cols.Add(("Chapter VI-A",     "D. Deductions", "#6d28d9", r => r.Chap6A));
            if (hasNps)     cols.Add(("NPS 80CCD(2)",     "D. Deductions", "#6d28d9", r => r.Nps80CCD2));
            cols.Add(("D. Total Deductions", "D. Deductions", "#7c3aed", r => r.TotalDed));
            // E
            if (hasOtherSrc) cols.Add(("E. Other Sources","E. Other Sources","#b45309", r => r.OtherSources));
            // Net Taxable Income
            cols.Add(("Net Taxable Income",  "Net Taxable Inc.", "#166534", r => r.NetTaxableIncome));
            // F. Tax
            cols.Add(("Tax on Income",       "F. Tax Computation","#be123c", r => r.TaxOnIncome));
            if (hasRebate) cols.Add(("Rebate u/s 87A",   "F. Tax Computation","#be123c", r => r.Rebate87A));
            cols.Add(("Tax After Rebate",    "F. Tax Computation","#be123c", r => r.TaxAfterRebate));
            if (hasSurch)  cols.Add(("Surcharge",        "F. Tax Computation","#be123c", r => r.Surcharge));
            cols.Add(("Cess (4%)",           "F. Tax Computation","#be123c", r => r.Cess));
            cols.Add(("F. Total Tax",        "F. Tax Computation","#9f1239", r => r.TotalTax));
            cols.Add(("TDS Deducted",        "F. Tax Computation","#9f1239", r => r.TdsDeducted));
            // Salary deductions
            if (hasPf)  cols.Add(("PF",     "Salary Deductions","#374151", r => r.Pf));
            if (hasEsi) cols.Add(("ESI",    "Salary Deductions","#374151", r => r.Esi));
            cols.Add(("Net Pay",            "Net Pay",          "#14532d", r => r.NetPay));

            int totalCols = 1 + cols.Count;

            // ── Title rows ──
            int curRow = 1;
            ws.Range(curRow,1,curRow,totalCols).Merge();
            ws.Cell(curRow,1).Value = $"{deductor?.CompanyName ?? ""} — Monthly Salary Statement — FY {fy}";
            ApplyHdr(ws.Cell(curRow,1), "#1e3a8a", 12, center:true);
            ws.Row(curRow).Height = 22; curRow++;

            ws.Range(curRow,1,curRow,totalCols).Merge();
            ws.Cell(curRow,1).Value = $"Employee: {emp.Name}   |   Code: {emp.EmployeeCode}   |   PAN: {emp.Pan}   |   FY: {fy}";
            ws.Cell(curRow,1).Style.Font.Bold = true; ws.Cell(curRow,1).Style.Font.FontSize = 10;
            ws.Cell(curRow,1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            ws.Row(curRow).Height = 16; curRow++;

            // ── Group header row ──
            ws.Cell(curRow,1).Value = "";
            ws.Cell(curRow,1).Style.Fill.BackgroundColor = XLColor.FromHtml("#374151");
            // Merge groups
            var groups = cols.Select(c => c.Group).ToList();
            int gc = 2;
            while (gc <= totalCols)
            {
                string grp = groups[gc-2];
                int start = gc;
                while (gc <= totalCols && groups[gc-2] == grp) gc++;
                int end = gc - 1;
                if (end > start) ws.Range(curRow, start, curRow, end).Merge();
                string grpBg = cols[start-2].BgHex;
                for (int i = start; i <= end; i++)
                {
                    ws.Cell(curRow,i).Value = (i == start) ? grp : "";
                    ApplyHdr(ws.Cell(curRow,i), grpBg, 8, center:true);
                }
            }
            ws.Row(curRow).Height = 14; curRow++;

            // ── Column header row ──
            ws.Cell(curRow,1).Value = "Month";
            ApplyHdr(ws.Cell(curRow,1), "#374151");
            for (int ci = 0; ci < cols.Count; ci++)
            {
                ws.Cell(curRow, ci+2).Value = cols[ci].Label;
                ApplyHdr(ws.Cell(curRow, ci+2), cols[ci].BgHex);
            }
            ws.Row(curRow).Height = 28; curRow++;

            // ── Data rows ──
            string[] mn3 = { "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };
            var rowByMonth = rows.ToDictionary(r => r.MonthLabel);
            var totals = new double[cols.Count];

            for (int mi = 0; mi < 12; mi++)
            {
                string lbl = mn3[mi];
                string bg  = mi % 2 == 0 ? "#ffffff" : "#f8fafc";

                ws.Cell(curRow,1).Value = lbl;
                ws.Cell(curRow,1).Style.Font.Bold = true;
                ws.Cell(curRow,1).Style.Fill.BackgroundColor = XLColor.FromHtml(bg);
                ws.Cell(curRow,1).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                ws.Cell(curRow,1).Style.Border.OutsideBorderColor = XLColor.FromHtml("#dde1e7");

                if (!rowByMonth.TryGetValue(lbl, out var r))
                {
                    ws.Range(curRow,2,curRow,totalCols).Merge();
                    ws.Cell(curRow,2).Value = "— not entered —";
                    ws.Cell(curRow,2).Style.Font.FontColor = XLColor.FromHtml("#d1d5db");
                    ws.Cell(curRow,2).Style.Font.Italic = true;
                    ws.Cell(curRow,2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                    ws.Range(curRow,1,curRow,totalCols).Style.Fill.BackgroundColor = XLColor.FromHtml(bg);
                    curRow++; continue;
                }

                for (int ci = 0; ci < cols.Count; ci++)
                {
                    double v = cols[ci].Fn(r);
                    totals[ci] += v;
                    var cell = ws.Cell(curRow, ci+2);
                    if (v == 0) { cell.Value = "—"; cell.Style.Font.FontColor = XLColor.FromHtml("#d1d5db"); }
                    else { cell.Value = v; cell.Style.NumberFormat.Format = "#,##0"; }
                    // Bold and colour for subtotal/total columns
                    bool isSub = cols[ci].Label.StartsWith("A.") || cols[ci].Label.StartsWith("B.") ||
                                 cols[ci].Label.StartsWith("C.") || cols[ci].Label.StartsWith("D.") ||
                                 cols[ci].Label.StartsWith("F.") || cols[ci].Label == "Net Pay" ||
                                 cols[ci].Label == "Net Taxable Income" || cols[ci].Label == "TDS Deducted";
                    if (isSub) { cell.Style.Font.Bold = true; cell.Style.Font.FontColor = XLColor.FromHtml(cols[ci].BgHex); }
                    else cell.Style.Font.FontColor = XLColor.FromHtml("#374151");
                    cell.Style.Fill.BackgroundColor = XLColor.FromHtml(bg);
                    cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                    cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                    cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#dde1e7");
                }
                curRow++;
            }

            // ── Total row ──
            ws.Cell(curRow,1).Value = "Total";
            ApplyHdr(ws.Cell(curRow,1), "#1e3a8a");
            for (int ci = 0; ci < cols.Count; ci++)
            {
                var cell = ws.Cell(curRow, ci+2);
                double v = totals[ci];
                if (v == 0) cell.Value = "—";
                else { cell.Value = v; cell.Style.NumberFormat.Format = "#,##0"; }
                cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                cell.Style.Font.FontColor = cols[ci].Label == "Net Pay" || cols[ci].Label.StartsWith("A.")
                    ? XLColor.FromHtml("#86efac")
                    : cols[ci].Label.StartsWith("D.") || cols[ci].Label == "TDS Deducted" || cols[ci].Label.StartsWith("F.")
                        ? XLColor.FromHtml("#fca5a5")
                        : XLColor.White;
                cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1565c0");
            }
            ws.Row(curRow).Height = 16;

            // ── Column widths ──
            ws.Column(1).Width = 9;
            for (int ci = 0; ci < cols.Count; ci++)
            {
                double w = cols[ci].Label.Length > 14 ? 18 : 14;
                ws.Column(ci+2).Width = w;
            }

            Directory.CreateDirectory(outputFolder);
            string safeName3 = string.Concat(emp.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileName3 = $"MonthlySalary_{safeName3}({emp.Pan})_{fy.Replace("/","-")}.xlsx";
            string path3 = Path.Combine(outputFolder, fileName3);
            wb.SaveAs(path3);
            return path3;
        }

        public static string GenerateMonthlySalaryPdf(
            Employee emp,
            string fy,
            string outputFolder,
            Deductor? deductor = null,
            EmployeeYearSummary? yearSummary = null,
            List<MonthlySalaryStatRow>? rows = null)
        {
            rows ??= new List<MonthlySalaryStatRow>();

            // Same dynamic column set as the Excel statement
            var cols = new List<(string Label, string BgHex, Func<MonthlySalaryStatRow,double> Fn)>();
            void Add(bool present, string label, string bg, Func<MonthlySalaryStatRow,double> fn)
            { if (present) cols.Add((label, bg, fn)); }

            Add(rows.Any(r => r.Basic > 0),           "Basic",          "#1e3a8a", r => r.Basic);
            Add(rows.Any(r => r.Hra > 0),             "HRA",            "#1e3a8a", r => r.Hra);
            Add(rows.Any(r => r.Da > 0),              "DA",             "#1e3a8a", r => r.Da);
            Add(rows.Any(r => r.Special > 0),         "Special",        "#1e3a8a", r => r.Special);
            Add(rows.Any(r => r.Medical > 0),         "Medical",        "#1e3a8a", r => r.Medical);
            Add(rows.Any(r => r.Lta > 0),             "LTA",            "#1e3a8a", r => r.Lta);
            Add(rows.Any(r => r.Bonus > 0),           "Bonus",          "#1e3a8a", r => r.Bonus);
            Add(rows.Any(r => r.Commission > 0),      "Commission",     "#1e3a8a", r => r.Commission);
            Add(rows.Any(r => r.AdvanceSalary > 0),   "Advance",        "#1e3a8a", r => r.AdvanceSalary);
            Add(rows.Any(r => r.Arrears > 0),         "Arrears",        "#1e3a8a", r => r.Arrears);
            Add(rows.Any(r => r.NpsEmployer > 0),     "NPS (Empr)",     "#1e3a8a", r => r.NpsEmployer);
            Add(rows.Any(r => r.PerqTaxable > 0),     "Perq.",          "#1e3a8a", r => r.PerqTaxable);
            Add(rows.Any(r => r.LeaveEncTaxable > 0), "Leave Enc.",     "#1e3a8a", r => r.LeaveEncTaxable);
            Add(rows.Any(r => r.Other > 0),           "Other",          "#1e3a8a", r => r.Other);
            cols.Add(("A. Gross",                     "#15803d", r => r.GrossTotal));
            Add(rows.Any(r => r.HraEx > 0),           "HRA Ex.",        "#0369a1", r => r.HraEx);
            Add(rows.Any(r => r.OtherEx > 0),         "Other Ex.",      "#0369a1", r => r.OtherEx);
            cols.Add(("B. Total Ex.",                 "#0369a1", r => r.TotalEx));
            cols.Add(("C. Net Taxable",               "#065f46", r => r.NetTaxableSalary));
            cols.Add(("Std Ded.",                     "#6d28d9", r => r.StdDed));
            Add(rows.Any(r => r.ProfTax > 0),         "Prof.Tax",       "#6d28d9", r => r.ProfTax);
            Add(rows.Any(r => r.Chap6A > 0),          "Ch.VI-A",        "#6d28d9", r => r.Chap6A);
            Add(rows.Any(r => r.Nps80CCD2 > 0),       "NPS 80CCD(2)",   "#6d28d9", r => r.Nps80CCD2);
            cols.Add(("D. Total Ded.",                "#7c3aed", r => r.TotalDed));
            Add(rows.Any(r => r.OtherSources > 0),    "E. Other Src.",  "#b45309", r => r.OtherSources);
            cols.Add(("Net Taxable Inc.",             "#166534", r => r.NetTaxableIncome));
            cols.Add(("Tax on Inc.",                  "#be123c", r => r.TaxOnIncome));
            Add(rows.Any(r => r.Rebate87A > 0),       "Rebate 87A",     "#be123c", r => r.Rebate87A);
            cols.Add(("Tax/Rebate",                   "#be123c", r => r.TaxAfterRebate));
            Add(rows.Any(r => r.Surcharge > 0),       "Surcharge",      "#be123c", r => r.Surcharge);
            cols.Add(("Cess",                         "#be123c", r => r.Cess));
            cols.Add(("F. Total Tax",                 "#9f1239", r => r.TotalTax));
            cols.Add(("TDS Deducted",                 "#9f1239", r => r.TdsDeducted));
            Add(rows.Any(r => r.Pf > 0),              "PF",             "#374151", r => r.Pf);
            Add(rows.Any(r => r.Esi > 0),             "ESI",            "#374151", r => r.Esi);
            cols.Add(("Net Pay",                      "#14532d", r => r.NetPay));

            string subtitle = string.Join("  |  ", new[]
            {
                deductor?.CompanyName ?? "",
                $"Employee: {emp.Name}", $"Code: {emp.EmployeeCode}", $"PAN: {emp.Pan}"
            }.Where(s => !string.IsNullOrWhiteSpace(s)));

            // Many columns → shrink font as the table widens
            float fs = cols.Count <= 16 ? 7f : cols.Count <= 22 ? 6.2f : 5.5f;

            byte[] pdf = PdfReports.BuildA4(
                title:        $"Monthly Salary Statement — FY {fy}",
                subtitle:     subtitle,
                centerHeader: true,
                landscape:    true,
                body:         c => c.Table(t =>
                {
                    t.ColumnsDefinition(d =>
                    {
                        d.ConstantColumn(26);
                        foreach (var _ in cols) d.RelativeColumn(1);
                    });

                    // Header row
                    t.Cell().Background("#374151").Padding(2).Text("Month").Bold().FontColor("#ffffff").FontSize(fs);
                    foreach (var (label, bg, _) in cols)
                        t.Cell().Background(bg).Padding(2).AlignRight().Text(label).Bold().FontColor("#ffffff").FontSize(fs);

                    string[] mn = { "Apr","May","Jun","Jul","Aug","Sep","Oct","Nov","Dec","Jan","Feb","Mar" };
                    var rowByMonth = rows.ToDictionary(r => r.MonthLabel);
                    var totals = new double[cols.Count];

                    for (int mi = 0; mi < 12; mi++)
                    {
                        string bg = mi % 2 == 0 ? "#ffffff" : "#f8fafc";
                        t.Cell().Background(bg).Padding(2).Text(mn[mi]).Bold().FontColor("#374151").FontSize(fs);
                        if (!rowByMonth.TryGetValue(mn[mi], out var r))
                        {
                            t.Cell().ColumnSpan((uint)cols.Count).Background(bg).Padding(2)
                                .Text("— not entered —").Italic().FontColor("#d1d5db").FontSize(fs);
                            continue;
                        }
                        for (int ci = 0; ci < cols.Count; ci++)
                        {
                            double v = cols[ci].Fn(r);
                            totals[ci] += v;
                            bool isKey = cols[ci].Label.StartsWith("A.") || cols[ci].Label.StartsWith("B.") ||
                                         cols[ci].Label.StartsWith("C.") || cols[ci].Label.StartsWith("D.") ||
                                         cols[ci].Label.StartsWith("F.") || cols[ci].Label == "Net Pay" ||
                                         cols[ci].Label == "Net Taxable Inc." || cols[ci].Label == "TDS Deducted";
                            var sp = t.Cell().Background(bg).Padding(2).AlignRight()
                                .Text(v == 0 ? "—" : $"₹{v:N0}")
                                .FontColor(v == 0 ? "#d1d5db" : isKey ? cols[ci].BgHex : "#374151").FontSize(fs);
                            if (isKey && v != 0) sp.Bold();
                        }
                    }

                    // Total row
                    t.Cell().Background("#1e3a8a").Padding(2).Text("Total").Bold().FontColor("#ffffff").FontSize(fs);
                    for (int ci = 0; ci < cols.Count; ci++)
                        t.Cell().Background("#1e3a8a").Padding(2).AlignRight()
                            .Text(totals[ci] == 0 ? "—" : $"₹{totals[ci]:N0}").Bold().FontColor("#ffffff").FontSize(fs);
                }));

            Directory.CreateDirectory(outputFolder);
            string safeNamePdf = string.Concat(emp.Name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string fileNamePdf = $"MonthlySalary_{safeNamePdf}({emp.Pan})_{fy.Replace("/","-")}.pdf";
            string pathPdf = Path.Combine(outputFolder, fileNamePdf);
            File.WriteAllBytes(pathPdf, pdf);
            return pathPdf;
        }

        private static void ApplyHdr(IXLCell cell, string bg, int fontSize = 9, bool center = false)
        {
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml(bg);
            cell.Style.Font.FontColor = XLColor.White;
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontSize = fontSize;
            cell.Style.Alignment.Horizontal = center ? XLAlignmentHorizontalValues.Center : XLAlignmentHorizontalValues.Right;
            cell.Style.Alignment.WrapText = true;
            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.OutsideBorderColor = XLColor.FromHtml("#1565c0");
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

            var runs = yearSummary?.MonthlyRuns ?? new Dictionary<int, PayrollRun>();

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

            var dataRows = new List<(string Label, double Ov, double Nv, bool sep, bool tot)>
            {
                ("Gross Salary (incl Perqs)",  o.GrossSalary,       n.GrossSalary,       false, false),
                ("HRA Exemption",              o.HraExemption,      n.HraExemption,      false, false),
            };
            // Itemised Sec 10 exemptions (other than HRA) — must appear or the
            // Gross → Taxable arithmetic doesn't reconcile on the sheet
            foreach (var s10 in o.Sec10Items.Where(x => x.Name != "HRA" && (x.OldRegime > 0 || x.NewRegime > 0)))
                dataRows.Add((s10.Name + (string.IsNullOrEmpty(s10.RuleRef) ? "" : $" [{s10.RuleRef}]"),
                              s10.OldRegime, s10.NewRegime, false, false));
            dataRows.AddRange(new (string, double, double, bool, bool)[]
            {
                ("Standard Deduction",         o.StandardDeduction, n.StandardDeduction, false, false),
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
            });

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
            double xlTdsPaid = runs.Count > 0 ? runs.Values.Sum(r2 => r2.TdsDeducted) : annual.YtdTdsDeducted;
            double xlBalance = chosen.TotalTax - xlTdsPaid;
            ws.Range(r,1,r,3).Merge();
            ws.Cell(r,1).Value = $"Annual Tax: ₹{chosen.TotalTax:N0}   |   TDS Paid: ₹{xlTdsPaid:N0}   |   Balance: ₹{xlBalance:N0}";
            ws.Cell(r,1).Style.Fill.BackgroundColor = XLColor.FromHtml("#f0fdf4");
            ws.Cell(r,1).Style.Font.FontColor = XLColor.FromHtml("#14532d");
            ws.Cell(r,1).Style.Font.Bold = true;
            ws.Cell(r,1).Style.Alignment.Indent = 1; r++;

            // Footer
            ws.Range(r,1,r,3).Merge();
            ws.Cell(r,1).Value = $"Generated by CDeTDS v{CDeTDS.Common.AppConstants.AppVersion} | {CDeTDS.Common.TaxRules.ActName(fy)} | {DateTime.Now:dd-MMM-yyyy}";
            ws.Cell(r,1).Style.Font.Italic = true;
            ws.Cell(r,1).Style.Font.FontColor = XLColor.Gray;
            ws.Cell(r,1).Style.Alignment.Indent = 1;

            // ── Monthly Salary Sheet ──────────────────────────────────────────
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
                // Per-month tax figures = chosen-regime annual computation prorated /12
                // (adapted PayrollRun rows carry no tax fields; legacy fields were annual)
                double hraExMXl = Math.Round(chosen.HraExemption / 12.0);
                double stdMXl   = Math.Round(chosen.StandardDeduction / 12.0);
                double taxMXl   = Math.Round(chosen.TotalIncome / 12.0);
                double totG=0,totH=0,totS=0,totT=0,totD=0,totP=0,totPt2=0,totN=0;
                for (int mi=0;mi<12;mi++)
                {
                    int row = 4+mi, m = fyMonths[mi];
                    var bg2 = mi%2==0 ? XLColor.White : XLColor.FromHtml("#f8fafc");
                    // Style only the table's 9 columns — styling the whole row paints to the sheet edge
                    wm.Range(row,1,row,9).Style.Fill.BackgroundColor = bg2;
                    wm.Cell(row,1).Value = mNames[mi];
                    wm.Cell(row,1).Style.Font.Bold = true;
                    if (runs.TryGetValue(m, out var pr))
                    {
                        double[] vals = { pr.GrossSalary, hraExMXl, stdMXl, taxMXl, pr.TdsDeducted, pr.PfEmployee, pr.ProfessionalTax, pr.NetPay };
                        totG+=pr.GrossSalary; totH+=hraExMXl; totS+=stdMXl;
                        totT+=taxMXl; totD+=pr.TdsDeducted;
                        totP+=pr.PfEmployee; totPt2+=pr.ProfessionalTax; totN+=pr.NetPay;
                        for (int ci=0;ci<8;ci++)
                        {
                            if (vals[ci] == 0) wm.Cell(row,ci+2).Value = "—";
                            else wm.Cell(row,ci+2).Value = vals[ci];
                            wm.Cell(row,ci+2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                            if (vals[ci]>0)
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
                // Total row — style only the table's 9 columns, not the whole sheet row
                int tRow = 16;
                wm.Range(tRow,1,tRow,9).Style.Fill.BackgroundColor = XLColor.FromHtml("#1e3a8a");
                wm.Range(tRow,1,tRow,9).Style.Font.FontColor = XLColor.White;
                wm.Range(tRow,1,tRow,9).Style.Font.Bold = true;
                wm.Cell(tRow,1).Value = "Total";
                double[] tots = { totG,totH,totS,totT,totD,totP,totPt2,totN };
                for (int ci=0;ci<8;ci++)
                {
                    wm.Cell(tRow,ci+2).Value = tots[ci];
                    wm.Cell(tRow,ci+2).Style.NumberFormat.Format = "₹#,##0";
                    wm.Cell(tRow,ci+2).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                }
                wm.Row(tRow).Height = 18;
                // Explicit widths — AdjustToContents under-measures ₹-formatted numbers (shows #######)
                wm.Column(1).Width = 10;
                for (int ci=2;ci<=9;ci++) wm.Column(ci).Width = 16;
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
