using CDeTDS.DAL.Models;
using System.Text;

namespace CDeTDS.DAL
{
    /// <summary>
    /// Generates a print-ready HTML paper return for Form 26Q / 24Q.
    /// Open in browser → Ctrl+P → Save as PDF.
    /// </summary>
    public static class PaperReturnGenerator
    {
        public static string Generate(ReturnData d, string outputPath)
        {
            var h   = d.Header;
            var sb  = new StringBuilder();
            var now = DateTime.Now.ToString("dd-MM-yyyy HH:mm");

            // ── FY-aware form identity (Income-tax Act 2025 transition) ─────────
            // The Act, form number, sections and year label depend on the FY:
            //   FY ≤ 2025-26 → Act 1961, forms 24Q/26Q/27EQ, "FY"
            //   FY ≥ 2026-27 → Act 2025, forms 138/140/143,  "TY"
            bool   newAct  = CDeTDS.Common.TaxRules.IsNewAct(h.FinancialYear);
            string formNo  = CDeTDS.Common.TaxRules.FormTypeForFy(h.FormType, h.FinancialYear);
            string oldEquiv = CDeTDS.Common.TaxRules.FormTypeForFy(formNo, "2025-26");
            bool   isSalaryForm = formNo is "24Q" or "138";
            bool   isTcsForm    = formNo is "27EQ" or "143";
            // Official Gazette statutory layout (PART A 16-row, PART B A–K, DECLARATION,
            // official Notes) applies to all new-Act forms: 138 (salary), 140 (non-salary),
            // 143 (TCS). TCS uses Collector/collection wording in place of Deductor/deduction.
            bool   officialForm = newAct;
            string partyLabel  = isTcsForm ? "Collector" : "Deductor";   // PART A party noun
            string actionLabel = isTcsForm ? "collection" : "deduction"; // noun: "...of tax"
            string actionVerb  = isTcsForm ? "collecting" : "deducting"; // verb: "...tax at source"
            string actName = CDeTDS.Common.TaxRules.ActName(h.FinancialYear);
            string yearLbl = CDeTDS.Common.TaxRules.YearLabel(h.FinancialYear);

            sb.Append($@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""UTF-8"">
<title>Paper Return — {formNo} {h.FinancialYear} {h.Quarter}</title>
<style>
  * {{ box-sizing: border-box; margin: 0; padding: 0; }}
  body {{ font-family: Arial, sans-serif; font-size: 9pt; color: #000; background: #fff; }}
  @page {{ size: A4; margin: 12mm 10mm; }}
  @media print {{ .no-print {{ display: none !important; }} }}

  .no-print {{
    position: fixed; top: 12px; right: 16px; z-index: 999;
    display: flex; gap: 8px;
  }}
  .btn {{
    padding: 8px 18px; border: none; border-radius: 4px; cursor: pointer;
    font-size: 12px; font-weight: 600;
  }}
  .btn-print {{ background: #1565C0; color: #fff; }}
  .btn-dl    {{ background: #2e7d32; color: #fff; }}

  .page-header {{
    text-align: center; border-bottom: 2px solid #000; padding-bottom: 6px; margin-bottom: 8px;
  }}
  .page-header h2 {{ font-size: 13pt; font-weight: bold; }}
  .page-header h3 {{ font-size: 10pt; font-weight: normal; margin-top: 2px; }}
  .watermark {{
    color: #888; font-size: 8pt; margin-top: 2px;
  }}

  .info-grid {{
    display: grid; grid-template-columns: 1fr 1fr; gap: 0;
    border: 1px solid #000; margin-bottom: 8px;
  }}
  .info-cell {{
    padding: 3px 6px; border-right: 1px solid #ccc; border-bottom: 1px solid #ccc;
    font-size: 8pt;
  }}
  .info-cell:nth-child(even) {{ border-right: none; }}
  .info-label {{ font-size: 7pt; color: #555; display: block; }}
  .info-value {{ font-weight: 600; }}

  .section-title {{
    background: #1565C0; color: #fff; padding: 3px 8px;
    font-size: 9pt; font-weight: bold; margin: 8px 0 0 0;
  }}

  /* Official statutory-form tables (Form 138 PART A etc.) — plain black borders */
  table.partA {{ border: 1px solid #000; margin: 8px 0; }}
  table.partA td {{ border: 1px solid #000; padding: 3px 6px; font-size: 8pt; vertical-align: top; }}
  table.partA .partA-head {{ text-align: center; font-weight: bold; background: #f0f0f0; }}
  table.partA .partA-no {{ text-align: center; width: 56px; font-weight: bold; }}

  table {{
    width: 100%; border-collapse: collapse; font-size: 7.5pt; margin-bottom: 8px;
  }}
  th {{
    background: #1565C0; color: #fff; border: 1px solid #0d47a1; padding: 4px 4px;
    text-align: center; font-weight: bold; font-size: 7pt; white-space: nowrap;
    position: sticky; top: 0; z-index: 2;
  }}
  @media print {{
    th {{
      position: static !important;
      background: #1565C0 !important;
      color: #fff !important;
      -webkit-print-color-adjust: exact;
      print-color-adjust: exact;
    }}
  }}
  td {{
    border: 1px solid #ccc; padding: 2px 4px; text-align: left; white-space: nowrap;
  }}
  td.num {{ text-align: right; }}
  tr:nth-child(even) td {{ background: #f8fafc; }}
  .total-row td {{ background: #e8f5e9; font-weight: bold; border-top: 2px solid #000; }}

  /* Annexure II scrollable wrapper */
  .ann2-wrap {{
    overflow-x: auto; width: 100%; margin-bottom: 8px;
    border: 1px solid #0d47a1; border-radius: 4px;
  }}
  .ann2-wrap table {{ margin-bottom: 0; min-width: max-content; }}
  .ann2-wrap th {{ background: #1565C0; color: #fff; font-size: 6.5pt; line-height: 1.3; white-space: normal; min-width: 60px; }}
  .ann2-wrap td {{ font-size: 7pt; }}
  .ann2-label {{
    background: #e3f2fd; color: #1565C0; font-size: 8pt; font-weight: 700;
    padding: 4px 8px; border-left: 4px solid #1565C0; margin: 8px 0 2px 0;
  }}
  @media print {{
    .ann2-wrap {{ overflow: visible; }}
    .ann2-wrap table {{ font-size: 5.5pt; }}
    .ann2-wrap th {{ font-size: 5pt; padding: 2px 2px; }}
    .ann2-wrap td {{ font-size: 5.5pt; padding: 1px 2px; }}
    .page-break {{ page-break-before: always; }}
  }}

  .summary-box {{
    border: 1px solid #000; padding: 6px 10px; margin-bottom: 8px;
    display: grid; grid-template-columns: repeat(4, 1fr); gap: 4px;
  }}
  .kpi {{ text-align: center; }}
  .kpi-label {{ font-size: 7pt; color: #555; }}
  .kpi-value {{ font-size: 11pt; font-weight: bold; color: #1565C0; }}

  .sign-block {{
    margin-top: 20px; display: grid; grid-template-columns: 1fr 1fr;
    gap: 20px; font-size: 8pt;
  }}
  .sign-line {{
    border-top: 1px solid #000; margin-top: 30px; padding-top: 4px; text-align: center;
  }}
  .footer {{
    text-align: center; font-size: 7pt; color: #888;
    border-top: 1px solid #ccc; margin-top: 12px; padding-top: 4px;
  }}
</style>
</head>
<body>

<div class=""no-print"">
  <button class=""btn btn-print"" onclick=""window.print()"">🖨 Print / Save PDF</button>
  <button class=""btn btn-dl"" onclick=""downloadHtml()"">⬇ Download HTML</button>
</div>

<div class=""page-header"">
  <h2>FORM NO. {formNo}{(newAct ? $@" <span style=""font-size:9pt;font-weight:normal;color:#555"">(erstwhile Form {oldEquiv})</span>" : "")}</h2>
  <h3>{(newAct
        ? isSalaryForm
            // Official Form 138 subtitle (Gazette of India).
            ? "[See rule 219(1) [Table: Sl. No. 1]] — Income-tax Act 2025, section 392"
            : isTcsForm
                // Official Form 143 subtitle (Gazette of India).
                ? "[See rule 219(1) [Table: Sl. No. 4]] — Income-tax Act 2025, section 394"
                // Official Form 140 subtitle (Gazette of India).
                : "[See rule 219(1) [Table: Sl. No. 3]] — Income-tax Act 2025, section 393"
        : formNo=="24Q"
            ? "(See section 192 and rule 31A)"
            : formNo=="26Q"
                ? "(See sections 193, 194, 194A, 194B, 194BB, 194C, 194D, 194EE, 194F, 194G, 194H, 194-I, 194J, 194LA and rule 31A)"
                : formNo=="27EQ"
                    ? "(See section 206C and rule 31AA)"
                    : "(See rule 31A)")}</h3>
  <h3 style=""font-weight:normal;font-size:10pt"">
    {(isTcsForm
        ? (newAct
            ? "Quarterly statement of collection of tax under section&nbsp;397(3)(b) of the Income&#8209;tax Act,&nbsp;2025 in respect of tax collected at source under section&nbsp;394"
            : "Quarterly statement of collection of tax under sub&#8209;section&nbsp;(3) of section&nbsp;206C of the Income&#8209;tax Act,&nbsp;1961")
        : isSalaryForm
            ? (newAct
                // Exact statutory wording from the official Form 138 (FN 138-24Q.pdf).
                ? "Quarterly statement of deduction of tax under section&nbsp;397(3)(b) of the Income&#8209;tax Act,&nbsp;2025 in respect of salary paid to employee under section&nbsp;392, or income of specified senior citizen under section&nbsp;393(1) [Table: Sl.&nbsp;No.&nbsp;8(iii)]"
                : "Quarterly statement of deduction of tax under sub&#8209;section&nbsp;(3) of section&nbsp;200 of the Income&#8209;tax Act,&nbsp;1961 in respect of salary")
            : (newAct
                ? "Quarterly statement of deduction of tax under section&nbsp;397(3)(b) of the Income&#8209;tax Act,&nbsp;2025 in respect of payments other than salary under section&nbsp;393"
                : "Quarterly statement of deduction of tax under sub&#8209;section&nbsp;(3) of section&nbsp;200 of the Income&#8209;tax Act,&nbsp;1961 in respect of payments other than salary"))}
  </h3>
  <h3 style=""font-weight:normal;font-size:10pt"">
    for the quarter ended {(h.Quarter=="Q1"?"30th June":h.Quarter=="Q2"?"30th September":h.Quarter=="Q3"?"31st December":"31st March")} &nbsp;
    ({yearLbl})
    &nbsp;·&nbsp; {(h.IsCorrection ? $"CORRECTION — {h.CorrectionType}" : "ORIGINAL")}
  </h3>
  <div class=""watermark"">Generated by CDeTDS v{CDeTDS.Common.AppConstants.AppVersion} · {now} · FOR REFERENCE ONLY — Submit .fvu file to TRACES portal</div>
</div>

{(officialForm
    // ── Official Form 138/140 PART A: 16-row numbered table (Gazette layout) ──
    ? $@"<table class=""partA"">
  <tr><td colspan=""3"" class=""partA-head"">PART A</td></tr>
  <tr><td class=""partA-no"">Row No.</td><td colspan=""2"" class=""partA-head"" style=""text-align:left"">Particulars of the {partyLabel}</td></tr>
  <tr><td class=""partA-no"">1.</td><td>Type of {partyLabel}</td><td>{(IsGovtDeductor(h.DeductorType) ? "☑ Government&nbsp;&nbsp; ☐ Non-Government" : "☐ Government&nbsp;&nbsp; ☑ Non-Government")}</td></tr>
  <tr><td class=""partA-no"">2.</td><td>Name</td><td>{Esc(h.DeductorName)}</td></tr>
  <tr><td class=""partA-no"">3.</td><td>Address</td><td>{Esc(h.DeductorAddress)}, {Esc(h.DeductorCity)}, {Esc(h.DeductorState)} — {Esc(h.DeductorPin)}</td></tr>
  <tr><td class=""partA-no"">4.</td><td>Permanent Account Number</td><td style=""font-family:monospace"">{Esc(h.PanOfDeductor)}</td></tr>
  <tr><td class=""partA-no"">5.</td><td>Tax Deduction and Collection Account Number</td><td style=""font-family:monospace"">{Esc(h.TanOfDeductor)}</td></tr>
  <tr><td class=""partA-no"">6.</td><td>Email id</td><td>{Esc(h.Email)}</td></tr>
  <tr><td class=""partA-no"">7.</td><td>Contact number</td><td>{Esc(h.Phone)}</td></tr>
  <tr><td class=""partA-no"">8.</td><td>Tax year</td><td>{Esc(h.FinancialYear)}</td></tr>
  <tr><td class=""partA-no"">9.</td><td>Has the statement been filed earlier for this quarter</td><td>{(h.IsCorrection ? "Yes" : "No")}</td></tr>
  <tr><td class=""partA-no"">10.</td><td>If answer to (9) is yes, then Return Receipt Number of original statement</td><td style=""font-family:monospace"">{Esc(h.IsCorrection ? (string.IsNullOrEmpty(h.OriginalPrn) ? h.PreviousPrn : h.OriginalPrn) : "")}</td></tr>
  <tr><td class=""partA-no"">11.</td><td>If Government {partyLabel}, please mention AIN of PAO/DTO/CDDO</td><td></td></tr>
  <tr><td colspan=""3"" class=""partA-head"" style=""text-align:left;font-weight:bold"">Particulars of the person responsible for {actionLabel} of tax</td></tr>
  <tr><td class=""partA-no"">12.</td><td>Name</td><td>{Esc(h.ResponsibleName)}</td></tr>
  <tr><td class=""partA-no"">13.</td><td>Permanent Account Number</td><td style=""font-family:monospace"">{Esc(h.ResponsiblePan)}</td></tr>
  <tr><td class=""partA-no"">14.</td><td>Address</td><td>{Esc(h.DeductorAddress)}, {Esc(h.DeductorCity)} — {Esc(h.DeductorPin)}</td></tr>
  <tr><td class=""partA-no"">15.</td><td>Email id</td><td>{Esc(h.Email)}</td></tr>
  <tr><td class=""partA-no"">16.</td><td>Contact number</td><td>{Esc(h.Phone)}</td></tr>
</table>"
    : $@"<div class=""section-title"">PART A — PARTICULARS OF THE {(isTcsForm ? "COLLECTOR" : "DEDUCTOR")}</div>
<div class=""info-grid"">
  <div class=""info-cell""><span class=""info-label"">{(isTcsForm ? "Collector" : "Deductor")} Name</span><span class=""info-value"">{Esc(h.DeductorName)}</span></div>
  <div class=""info-cell""><span class=""info-label"">TAN</span><span class=""info-value"">{Esc(h.TanOfDeductor)}</span></div>
  <div class=""info-cell""><span class=""info-label"">PAN</span><span class=""info-value"">{Esc(h.PanOfDeductor)}</span></div>
  <div class=""info-cell""><span class=""info-label"">Deductor Type</span><span class=""info-value"">{Esc(h.DeductorType)} {(!string.IsNullOrEmpty(h.DeductorType) ? $"(Code: {CDeTDS.DAL.FvuGenerator.GetDeductorCategoryPublic(h.DeductorType)})" : "")}</span></div>
  <div class=""info-cell""><span class=""info-label"">Address</span><span class=""info-value"">{Esc(h.DeductorAddress)}, {Esc(h.DeductorCity)} — {Esc(h.DeductorPin)}</span></div>
  <div class=""info-cell""><span class=""info-label"">State</span><span class=""info-value"">{Esc(h.DeductorState)}</span></div>
  <div class=""info-cell""><span class=""info-label"">Responsible Person</span><span class=""info-value"">{Esc(h.ResponsibleName)}</span></div>
  <div class=""info-cell""><span class=""info-label"">Designation / PAN</span><span class=""info-value"">{Esc(h.Designation)} / {Esc(h.ResponsiblePan)}</span></div>
  <div class=""info-cell""><span class=""info-label"">Filing Date</span><span class=""info-value"">{h.FilingDate:dd-MM-yyyy}</span></div>
  <div class=""info-cell""><span class=""info-label"">Return Type</span><span class=""info-value"" style=""color:{(h.IsCorrection?"#b45309":"#15803d")};font-weight:700"">{(h.IsCorrection ? $"CORRECTION ({h.CorrectionType})" : "ORIGINAL")}</span></div>
  {(h.IsCorrection ? $@"<div class=""info-cell""><span class=""info-label"">Previous PRN</span><span class=""info-value"" style=""font-family:monospace;font-size:11px"">{Esc(h.PreviousPrn)}</span></div>
  <div class=""info-cell""><span class=""info-label"">Original PRN</span><span class=""info-value"" style=""font-family:monospace;font-size:11px"">{Esc(string.IsNullOrEmpty(h.OriginalPrn) ? h.PreviousPrn : h.OriginalPrn)}</span></div>" : "")}
</div>")}

{(isSalaryForm
    // Salary forms: deductee rows live in Deductees for every quarter (Q1-Q4);
    // SalaryDetails is only populated for the Q4 salary annexure. Count whichever
    // is populated so the summary is never 0 on a salary return.
    ? $@"<div class=""summary-box"">
  <div class=""kpi""><div class=""kpi-label"">Total Challans</div><div class=""kpi-value"">{d.Challans.Count}</div></div>
  <div class=""kpi""><div class=""kpi-label"">Employees (distinct PAN)</div><div class=""kpi-value"">{(d.Deductees.Count > 0 ? d.Deductees.Select(e => (e.Pan ?? "").Trim().ToUpper()).Where(p => p.Length > 0).Distinct().Count() : d.SalaryDetails.Select(s => (s.Pan ?? "").Trim().ToUpper()).Where(p => p.Length > 0).Distinct().Count())}</div></div>
  <div class=""kpi""><div class=""kpi-label"">Deductee Records</div><div class=""kpi-value"">{(d.Deductees.Count > 0 ? d.Deductees.Count : d.SalaryDetails.Count)}</div></div>
  <div class=""kpi""><div class=""kpi-label"">Total TDS Deducted</div><div class=""kpi-value"">₹{(d.Deductees.Count > 0 ? d.TotalTdsDeducted : d.SalaryDetails.Sum(s => s.TdsDeducted)):N0}</div></div>
</div>"
    : $@"<div class=""summary-box"">
  <div class=""kpi""><div class=""kpi-label"">Total Challans</div><div class=""kpi-value"">{d.Challans.Count}</div></div>
  <div class=""kpi""><div class=""kpi-label"">Total {(isTcsForm ? "Collectees" : "Deductees")}</div><div class=""kpi-value"">{d.Deductees.Count}</div></div>
  <div class=""kpi""><div class=""kpi-label"">{(isTcsForm ? "Gross Amount Received" : "Gross Amount Paid")}</div><div class=""kpi-value"">₹{d.TotalAmountPaid:N0}</div></div>
  <div class=""kpi""><div class=""kpi-label"">Total {(isTcsForm ? "TCS Collected" : "TDS Deducted")}</div><div class=""kpi-value"">₹{d.TotalTdsDeducted:N0}</div></div>
</div>")}
");

            // ── PART B — Challan details ────────────────────────────────────────
            bool officialPartB = officialForm;   // Form 138/140 official A–K layout
            if (officialPartB)
            {
                // Official Form 138/140 PART B columns A–K (Gazette layout).
                sb.Append($@"<div style=""font-weight:bold;font-size:9pt;margin:10px 0 2px"">PART B: Details of tax Deducted at source and paid to the credit of the Central Government</div>
<div style=""font-size:8.5pt;margin:2px 0"">1. Details of tax deducted and paid to the credit of the Central Government:</div>
<table class=""partA"">
<thead><tr>
  <th>Sl.<br/>No.<br/>(A)</th><th>Total tax<br/>(B)</th><th>Total interest<br/>(C)</th>
  <th>Total fee<br/>(D)</th><th>Total penalty/<br/>others (E)</th>
  <th>Total amount<br/>deposited (B+C+D+E)<br/>(F)</th>
  <th>Mode of<br/>payment (G)</th><th>BSR code/<br/>Receipt No. of<br/>Form 137 (H)</th>
  <th>Date deposited<br/>(dd/mm/yyyy) (I)</th>
  <th>Challan Serial No./<br/>DDO Serial No. of<br/>Form 137 (J)</th>
  <th>Minor Head<br/>of Challan (K)</th>
</tr></thead><tbody>");
                double pbTax=0, pbInt=0, pbFee=0, pbTot=0; int pbSl=0;
                foreach (var c in d.Challans)
                {
                    pbSl++;
                    double tax = c.TdsDeposited + c.Surcharge + c.Cess;
                    double total = tax + c.Interest + c.LateFee;
                    pbTax += tax; pbInt += c.Interest; pbFee += c.LateFee; pbTot += total;
                    sb.Append($@"<tr>
  <td style=""text-align:center"">{pbSl}</td>
  <td class=""num"">{tax:N0}</td><td class=""num"">{(c.Interest>0?c.Interest.ToString("N0"):"0")}</td>
  <td class=""num"">{(c.LateFee>0?c.LateFee.ToString("N0"):"0")}</td><td class=""num"">0</td>
  <td class=""num"">{total:N0}</td>
  <td style=""text-align:center"">{(IsGovtDeductor(h.DeductorType) ? "B" : "C")}</td>
  <td style=""font-family:monospace"">{Esc(c.BsrCode)}</td>
  <td style=""text-align:center"">{c.ChallanDate:dd/MM/yyyy}</td>
  <td style=""font-family:monospace;text-align:center"">{Esc(c.ChallanNo)}</td>
  <td style=""text-align:center"">200</td>
</tr>");
                }
                sb.Append($@"<tr style=""font-weight:bold;background:#f0f0f0"">
  <td style=""text-align:center"">Total</td><td class=""num"">{pbTax:N0}</td><td class=""num"">{pbInt:N0}</td>
  <td class=""num"">{pbFee:N0}</td><td class=""num"">0</td><td class=""num"">{pbTot:N0}</td>
  <td colspan=""5""></td></tr>
</tbody></table>");
                // PART B item 2 — the statutory note enclosing the annexures.
                if (isSalaryForm)
                {
                    sb.Append(@"<div style=""font-size:8.5pt;margin:4px 0 8px;line-height:1.5"">
  2. Details of salary paid and tax deducted thereon—<br/>
  &nbsp;&nbsp;(i) enclose <b>Annexure-I</b> along with each statement having details of relevant quarter in the case of employee and specified senior citizen.<br/>
  &nbsp;&nbsp;(ii) enclose <b>Annexure-II</b> along with the last statement i.e. for the quarter ending 31st March having details for the whole tax year in the case of employee.<br/>
  &nbsp;&nbsp;(iii) enclose <b>Annexure-III</b> along with the last statement i.e. for the quarter ending 31st March having details for the whole tax year in the case of specified senior citizen.
</div>");
                }
                else if (isTcsForm)
                {
                    sb.Append(@"<div style=""font-size:8.5pt;margin:4px 0 8px;line-height:1.5"">
  2. Details of amount received and tax collected thereon from the collectees (see Annexure).
</div>");
                }
                else
                {
                    sb.Append(@"<div style=""font-size:8.5pt;margin:4px 0 8px;line-height:1.5"">
  2. Details of amount paid and tax deducted thereon from the deductees and amount paid without deduction (see Annexure).
</div>");
                }
            }
            else
            {
            sb.Append($@"<div class=""section-title"">PART B — CHALLAN DETAILS{(newAct ? " (Form No. 137)" : "")}</div>
<table>
<thead><tr>
  <th>Sl.</th><th>BSR Code</th><th>Challan Date</th><th>Challan No.</th>
  <th>Section</th><th style=""text-align:right"">TDS Deposited (₹)</th>
  <th style=""text-align:right"">Surcharge (₹)</th><th style=""text-align:right"">Cess (₹)</th>
  <th style=""text-align:right"">Interest (₹)</th><th style=""text-align:right"">Total (₹)</th>
  <th>Deductees</th>
</tr></thead><tbody>");

            double cTds = 0, cSur = 0, cCess = 0, cInt = 0, cTot = 0;
            foreach (var c in d.Challans)
            {
                cTds += c.TdsDeposited; cSur += c.Surcharge; cCess += c.Cess;
                cInt += c.Interest;     cTot += c.TotalDeposited;
                sb.Append($@"<tr>
  <td class=""num"">{c.SlNo}</td><td>{Esc(c.BsrCode)}</td>
  <td>{c.ChallanDate:dd-MM-yyyy}</td><td>{Esc(c.ChallanNo)}</td>
  <td>{Esc(c.Section)}</td>
  <td class=""num"">{c.TdsDeposited:N2}</td>
  <td class=""num"">{c.Surcharge:N2}</td>
  <td class=""num"">{c.Cess:N2}</td>
  <td class=""num"">{c.Interest:N2}</td>
  <td class=""num"">{c.TotalDeposited:N2}</td>
  <td class=""num"">{c.NoOfDeductees}</td>
</tr>");
            }
            sb.Append($@"<tr class=""total-row"">
  <td colspan=""5"">TOTAL</td>
  <td class=""num"">{cTds:N2}</td>
  <td class=""num"">{cSur:N2}</td>
  <td class=""num"">{cCess:N2}</td>
  <td class=""num"">{cInt:N2}</td>
  <td class=""num"">{cTot:N2}</td>
  <td class=""num"">{d.Challans.Sum(c => c.NoOfDeductees)}</td>
</tr></tbody></table>");
            }

            // ── Annexure I / Part B — per-challan deductee sections ──────────
            bool is24Q = isSalaryForm;   // 24Q or 138 (salary family)

            // Grand totals across all challans
            double gAmt=0, gDed=0, gSur=0, gCess=0, gTotal=0, gDep=0;

            if (is24Q)
            {
                sb.Append($@"<div class=""section-title"">ANNEXURE I — Deductee-wise Break-up of TDS (u/s {(newAct ? "392" : "192")})</div>");

                // Group deductees by challan — one section per challan matching official NSDL format
                int challSeq = 0;
                foreach (var ch in d.Challans)
                {
                    challSeq++;
                    var chDeductees = d.Deductees
                        .Where(e => e.ChallanNo == ch.ChallanNo && e.BsrCode == ch.BsrCode)
                        .ToList();
                    // Unlinked entries go under first challan
                    if (challSeq == 1)
                        chDeductees = chDeductees
                            .Concat(d.Deductees.Where(e => string.IsNullOrEmpty(e.ChallanNo)))
                            .ToList();

                    double chTdsSum = chDeductees.Sum(e => e.TdsDeducted);

                    // Challan header box — matches NSDL official layout
                    sb.Append($@"<div style=""border:1px solid #1565C0;border-radius:6px;padding:8px 14px;margin:10px 0 4px 0;
                         background:#f0f7ff;display:grid;grid-template-columns:1fr 1fr 1fr;gap:4px;font-size:8pt"">
  <div><span style=""color:#555"">BSR Code:</span> <b style=""font-family:monospace"">{Esc(ch.BsrCode)}</b></div>
  <div><span style=""color:#555"">Challan Date:</span> <b>{ch.ChallanDate:dd/MM/yyyy}</b></div>
  <div><span style=""color:#555"">Challan Serial No.:</span> <b style=""font-family:monospace"">{Esc(ch.ChallanNo)}</b></div>
  <div><span style=""color:#555"">Amount as per Challan:</span> <b>₹{ch.TdsDeposited:N0}</b></div>
  <div><span style=""color:#555"">Total TDS col. 326:</span> <b>₹{chTdsSum:N0}</b></div>
  <div><span style=""color:#555"">Interest:</span> <b>{(ch.Interest > 0 ? "₹" + ch.Interest.ToString("N0") : "—")}</b></div>
</div>
<table style=""margin-bottom:2px"">
<thead><tr>
  <th>Sl.No<br/>(A)</th><th>Challan Ref<br/>(Sl.No A, Part B)<br/>(B)</th><th>PAN<br/>(C)</th>
  <th style=""min-width:130px"">Name of {(newAct ? "employee/<br/>specified senior citizen" : "Employee")}<br/>(D)</th>
  <th>Emp ref/<br/>PPO No.<br/>(E)</th>
  <th>Section<br/>Code<br/>(F)</th>
  <th>Date of payment/<br/>credit (G)</th>
  <th>Date of<br/>deduction (H)</th>
  <th style=""text-align:right"">Amount Paid/<br/>Credited (I)</th>
  <th style=""text-align:right"">Total Tax<br/>Deducted (J)</th>
  <th>Deposited?<br/>(K)</th>
  <th style=""text-align:right"">Total Tax<br/>Deposited (L)</th>
  <th>Date of<br/>deposit (M)</th>
  <th>Reason for non/<br/>lower/higher<br/>deduction (N)</th>
  <th>Cert No.<br/>u/s 395(1)<br/>(O)</th>
</tr></thead><tbody>");

                    int cSlNo = 1;
                    double chAmt=0, chDed=0, chSur=0, chCess=0, chTot=0;
                    foreach (var e in chDeductees)
                    {
                        double tot = e.TdsDeducted + e.Surcharge + e.Cess;
                        chAmt += e.AmountPaid; chDed += e.TdsDeducted;
                        chSur += e.Surcharge;  chCess += e.Cess; chTot += tot;
                        gAmt += e.AmountPaid; gDed += e.TdsDeducted;
                        gSur += e.Surcharge;  gCess += e.Cess; gTotal += tot;
                        // Annexure I "Section Code" (col F). Old-Act salary = 92B; new-Act
                        // Form 138 = the 4-digit payment code (1002 default for non-Govt salary).
                        string secCode = "92B";
                        if (newAct)
                        {
                            var pc = BuiltInTdsRules.PaymentCodeFor(string.IsNullOrEmpty(e.Section) ? "192" : e.Section, "", "");
                            secCode = string.IsNullOrEmpty(pc) ? "1002" : pc;
                        }
                        // Reason for non/lower/higher deduction (col N): A = lower/no
                        // deduction on s.395(1) cert; C = higher rate u/s 397(2) (no PAN).
                        string reason = !string.IsNullOrEmpty(e.LowerDeductionCertNo) ? "A"
                                      : (!CDeTDS.Common.Validators.IsValidPan(e.Pan) ? "C" : "");
                        sb.Append($@"<tr>
  <td class=""num"">{cSlNo++}</td>
  <td class=""num"" style=""text-align:center"">{challSeq}</td>
  <td style=""font-family:monospace;font-size:7pt"">{Esc(e.Pan)}</td>
  <td>{Esc(e.Name)}</td>
  <td class=""num""></td>
  <td style=""text-align:center"">{secCode}</td>
  <td>{e.PaymentDate:dd/MM/yyyy}</td>
  <td>{e.PaymentDate:dd/MM/yyyy}</td>
  <td class=""num"">{e.AmountPaid:N0}</td>
  <td class=""num"">{(tot > 0 ? tot.ToString("N0") : "")}</td>
  <td style=""text-align:center"">{(tot > 0 ? "Yes" : "No")}</td>
  <td class=""num"">{(tot > 0 ? tot.ToString("N0") : "")}</td>
  <td>{ch.ChallanDate:dd/MM/yyyy}</td>
  <td style=""text-align:center"">{reason}</td>
  <td style=""font-family:monospace;font-size:7pt"">{Esc(e.LowerDeductionCertNo)}</td>
</tr>");
                    }
                    sb.Append($@"<tr class=""total-row"">
  <td colspan=""8"" style=""text-align:right;font-weight:bold"">Total</td>
  <td class=""num"">{chAmt:N0}</td>
  <td class=""num"">{chTot:N0}</td>
  <td></td>
  <td class=""num"">{chTot:N0}</td>
  <td colspan=""3""></td>
</tr></tbody></table>");
                }

                // Grand total across all challans
                sb.Append($@"<div style=""background:#e8f5e9;border:2px solid #2e7d32;border-radius:4px;padding:6px 14px;
                     font-size:8pt;font-weight:700;display:flex;gap:30px;margin-bottom:8px"">
  <span>GRAND TOTAL</span>
  <span>Amount Paid: ₹{gAmt:N0}</span>
  <span>TDS Deducted: ₹{gDed:N0}</span>
  <span>Total TDS: ₹{gTotal:N0}</span>
</div>");
            }
            else
            {
                // 26Q / 140 / 27EQ / 143 — per-challan sections
                sb.Append($@"<div class=""section-title"">{(officialForm
                    ? (isTcsForm ? "ANNEXURE — Collectee-wise Break-up of TCS" : "ANNEXURE — Deductee-wise Break-up of TDS")
                    : " I — DEDUCTEE DETAILS (per Challan)")}</div>");

                int challSeq = 0;
                foreach (var ch in d.Challans)
                {
                    challSeq++;
                    var chDeductees = d.Deductees
                        .Where(e => e.ChallanNo == ch.ChallanNo)
                        .ToList();
                    if (challSeq == 1)
                        chDeductees = chDeductees
                            .Concat(d.Deductees.Where(e => string.IsNullOrEmpty(e.ChallanNo)))
                            .ToList();

                    double chTdsSum = chDeductees.Sum(e => e.TdsDeducted);
                    sb.Append($@"<div style=""border:1px solid #1565C0;border-radius:6px;padding:8px 14px;
                         margin:10px 0 4px 0;background:#f0f7ff;font-size:8pt;
                         display:grid;grid-template-columns:1fr 1fr 1fr;gap:4px"">
  <div><span style=""color:#555"">BSR Code:</span> <b style=""font-family:monospace"">{Esc(ch.BsrCode)}</b></div>
  <div><span style=""color:#555"">Challan Date:</span> <b>{ch.ChallanDate:dd/MM/yyyy}</b></div>
  <div><span style=""color:#555"">Challan Serial:</span> <b style=""font-family:monospace"">{Esc(ch.ChallanNo)}</b></div>
  <div><span style=""color:#555"">Amount as per Challan:</span> <b>₹{ch.TdsDeposited:N0}</b></div>
  <div><span style=""color:#555"">Total TDS (col. 326):</span> <b>₹{chTdsSum:N0}</b></div>
</div>
<table style=""margin-bottom:2px"">
<thead><tr>
  <th>Sl.No<br/>(A)</th><th>Challan Ref<br/>(Sl.No A, Part B)<br/>(B)</th><th>PAN<br/>(C)</th>
  <th style=""min-width:120px"">Name of the<br/>{(isTcsForm ? "collectee" : "deductee")} (D)</th>
  <th>{(isTcsForm ? "Collection" : "Section")}<br/>code (E)</th>
  <th>Date of {(isTcsForm ? "receipt/<br/>debit" : "payment/<br/>credit")} (F)</th>
  <th style=""text-align:right"">Amount {(isTcsForm ? "received/<br/>debited" : "paid/<br/>credited")} (G)</th>
  <th style=""text-align:right"">Total Tax<br/>{(isTcsForm ? "Collected" : "Deducted")} (J)</th>
  <th>Deposited?<br/>(K)</th>
  <th style=""text-align:right"">Total Tax<br/>Deposited (L)</th>
  <th>Rate of<br/>{(isTcsForm ? "collection" : "deduction")} (M)</th>
  <th>Date of<br/>{(isTcsForm ? "collection" : "deduction")} (N)</th>
  <th>Reason non/<br/>lower/higher (O)</th>
  <th>Cert No.<br/>u/s 395({(isTcsForm ? "3" : "1")}) (P)</th>
  <th>{(isTcsForm ? "Collectee<br/>code (Q)" : "UIN of<br/>Form 121 (Q)")}</th>
</tr></thead><tbody>");

                    int q6SlNo = 1;
                    double q6Amt=0, q6Ded=0, q6Dep=0;
                    foreach (var e in chDeductees)
                    {
                        double tot = e.TdsDeducted + e.Surcharge + e.Cess;
                        q6Amt += e.AmountPaid; q6Ded += e.TdsDeducted; q6Dep += e.TdsDeposited;
                        gAmt += e.AmountPaid; gDed += e.TdsDeducted; gDep += e.TdsDeposited;
                        // Section code (col E): new-Act = 4-digit payment code; old = legacy.
                        string secCodeNs = e.Section;
                        if (newAct && !string.IsNullOrEmpty(e.Section))
                        {
                            var pc = BuiltInTdsRules.PaymentCodeFor(e.Section, "", e.DeducteeType ?? "");
                            if (!string.IsNullOrEmpty(pc)) secCodeNs = pc;
                        }
                        // Reason (col O): A = lower/no deduction via s.395 cert; C = higher (no PAN).
                        string reasonNs = !string.IsNullOrEmpty(e.LowerDeductionCertNo) ? "A"
                                        : (!CDeTDS.Common.Validators.IsValidPan(e.Pan) ? "C" : "");
                        sb.Append($@"<tr>
  <td class=""num"">{q6SlNo++}</td>
  <td class=""num"" style=""text-align:center"">{challSeq}</td>
  <td style=""font-family:monospace;font-size:7pt"">{Esc(e.Pan)}</td>
  <td>{Esc(e.Name)}</td>
  <td style=""text-align:center"">{Esc(secCodeNs)}</td>
  <td>{e.PaymentDate:dd/MM/yyyy}</td>
  <td class=""num"">{e.AmountPaid:N0}</td>
  <td class=""num"">{(tot>0?tot.ToString("N0"):"")}</td>
  <td style=""text-align:center"">{(e.TdsDeposited>0?"Yes":"No")}</td>
  <td class=""num"">{(e.TdsDeposited>0?e.TdsDeposited.ToString("N0"):"")}</td>
  <td class=""num"">{e.Rate:N2}</td>
  <td>{e.PaymentDate:dd/MM/yyyy}</td>
  <td style=""text-align:center"">{reasonNs}</td>
  <td style=""font-family:monospace;font-size:7pt"">{Esc(e.LowerDeductionCertNo)}</td>
  <td></td>
</tr>");
                    }
                    sb.Append($@"<tr class=""total-row"">
  <td colspan=""6"" style=""text-align:right;font-weight:bold"">Total</td>
  <td class=""num"">{q6Amt:N0}</td>
  <td class=""num"">{q6Ded:N0}</td>
  <td></td>
  <td class=""num"">{q6Dep:N0}</td>
  <td colspan=""5""></td>
</tr></tbody></table>");
                }
                // Grand total for 26Q
                sb.Append($@"<div style=""background:#e8f5e9;border:2px solid #2e7d32;border-radius:4px;padding:6px 14px;
                     font-size:8pt;font-weight:700;display:flex;gap:30px;margin-bottom:8px"">
  <span>GRAND TOTAL</span>
  <span>{(isTcsForm ? "Amount Received" : "Amount Paid")}: ₹{gAmt:N2}</span>
  <span>{(isTcsForm ? "TCS Collected" : "TDS Deducted")}: ₹{gDed:N2}</span>
  <span>{(isTcsForm ? "TCS Deposited" : "TDS Deposited")}: ₹{gDep:N2}</span>
</div>");
            }

            // ── Annexure II — Salary Details (24Q Q4 only) ────────────────────
            // Two-table layout exactly matching official NSDL Form 24Q Annexure II
            // Table 1: cols 330-342  |  Table 2: cols 343-356
            if (isSalaryForm && h.Quarter == "Q4" && d.SalaryDetails.Any())
            {
                sb.Append($@"<div class=""section-title"" style=""page-break-before:always"">ANNEXURE II — Detail of salary paid / credited during {yearLbl} and net tax payable</div>
<div style=""font-size:7.5pt;color:#374151;margin:4px 0 6px 0"">
  (Please see separate Annexure I for each quarter. Annexure II is filed with Q4 only.)
</div>");

                // Pre-compute per-employee derived values
                int sSlNo = 1;
                // Table 1 totals
                double t335=0, t337=0, t338=0, t340=0, t342=0;
                // Table 2 totals
                double t343=0, t345=0, t346=0, t347=0, t348=0, t349=0, t350=0, t351=0, t352=0, t353=0, t354=0, t355=0;

                // ── TABLE 1 ── cols 330-342
                sb.Append(@"<div class=""ann2-label"">Part A — Salary &amp; Deductions (Columns 330–342)</div>
<div class=""ann2-wrap""><table>
<thead><tr>
  <th>Sl<br/>(330)</th>
  <th>PAN<br/>(331)</th>
  <th style=""min-width:130px"">Name of Employee<br/>(332)</th>
  <th>Age<br/>(333)</th>
  <th>Date From<br/>(334)</th>
  <th>Date To<br/>(334)</th>
  <th>Taxable Salary<br/>Cur. Emp (₹)<br/>(335)</th>
  <th>Taxable Salary<br/>Prev. Emp (₹)<br/>(336)</th>
  <th>Total Salary<br/>335+336 (₹)<br/>(337)</th>
  <th>Dedn u/s 16(ia)<br/>+16(ii) (₹)<br/>(338)</th>
  <th>Dedn u/s<br/>16(iii) (₹)<br/>(339)</th>
  <th>Income u/h<br/>Salaries (₹)<br/>(340)</th>
  <th>Income Other<br/>Sources (₹)<br/>(341)</th>
  <th>Gross Total<br/>Income (₹)<br/>(342)</th>
</tr></thead><tbody>");

                var rows = new List<(ReturnSalaryDetail s, string ageCode, double col335, double col337, double col338, double col340, double col342)>();
                foreach (var s in d.SalaryDetails)
                {
                    // Age code: S=60-79, O=>80, W=female, G=others
                    // We use Gender from SD: W=female, others=G (age data not in ReturnSalaryDetail)
                    string ageCode = s.Gender == "W" ? "W" : "G";

                    // NSDL col 335 = Gross − Sec 10 exemptions (HRA + bills reimbursements)
                    // Professional Tax u/s 16(iii) is also deducted BEFORE col 335 per NSDL spec.
                    // Standard Deduction u/s 16(ia) goes in col 338 SEPARATELY.
                    // col 340 = col 337 − col 338 (Income u/h Salaries)
                    // col 342 = col 340 (GTI, no other sources assumed)
                    // col 346 = col 342 − col 345 (Ch VIA) = TaxableIncome from engine
                    //
                    // We use engine's TaxableIncome (s.TaxableIncome) as the anchor and
                    // derive col 335 upward so all columns reconcile exactly.
                    double col338 = s.StandardDeduction;               // 16(ia) std ded
                    // col342 = GTI = TaxableIncome + Ch6A (reverse from engine output)
                    double col342 = Math.Max(0, s.TaxableIncome + s.Chapter6ATotal);
                    double col340 = col342;                             // no other income sources
                    // col337 = Income u/h Salaries + std ded = col340 + col338
                    double col337 = col340 + col338;
                    // col335 = col337 − prev emp salary
                    double col335 = Math.Max(0, col337 - s.PrevEmpSalary);

                    t335 += col335; t337 += col337; t338 += col338; t340 += col340; t342 += col342;
                    rows.Add((s, ageCode, col335, col337, col338, col340, col342));

                    sb.Append($@"<tr>
  <td class=""num"">{sSlNo++}</td>
  <td style=""font-family:monospace;font-size:7pt"">{Esc(s.Pan)}</td>
  <td>{Esc(s.Name)}</td>
  <td style=""text-align:center"">{ageCode}</td>
  <td style=""white-space:nowrap"">{s.EmploymentFrom:dd/MM/yyyy}</td>
  <td style=""white-space:nowrap"">{s.EmploymentTo:dd/MM/yyyy}</td>
  <td class=""num"">{col335:N0}</td>
  <td class=""num"">{(s.PrevEmpSalary>0?s.PrevEmpSalary.ToString("N0"):"")}</td>
  <td class=""num"">{col337:N0}</td>
  <td class=""num"">{col338:N0}</td>
  <td class=""num""></td>
  <td class=""num"">{col340:N0}</td>
  <td class=""num""></td>
  <td class=""num"">{col342:N0}</td>
</tr>");
                }
                sb.Append($@"<tr class=""total-row"">
  <td colspan=""6"">TOTAL</td>
  <td class=""num"">{t335:N0}</td><td></td>
  <td class=""num"">{t337:N0}</td>
  <td class=""num"">{t338:N0}</td><td></td>
  <td class=""num"">{t340:N0}</td><td></td>
  <td class=""num"">{t342:N0}</td>
</tr></tbody></table></div>");

                // ── TABLE 2 ── cols 343-356
                sb.Append(@"<div class=""ann2-label"">Part B — Tax Computation &amp; TDS (Columns 343–356)</div>
<div class=""ann2-wrap""><table>
<thead><tr>
  <th>Sl<br/>(330)</th>
  <th>Dedn 80C<br/>80CCC 80CCD (₹)<br/>(343)</th>
  <th>Other Ch.VIA<br/>provisions (₹)<br/>(344)</th>
  <th>Total Ch.VIA<br/>343+344 (₹)<br/>(345)</th>
  <th>Taxable Income<br/>342−345 (₹)<br/>(346)</th>
  <th>Income Tax<br/>on 346 (₹)<br/>(347)</th>
  <th>Surcharge<br/>(₹)<br/>(348)</th>
  <th>Education<br/>Cess (₹)<br/>(349)</th>
  <th>Relief<br/>u/s 89 (₹)<br/>(350)</th>
  <th>Net Tax<br/>Payable (₹)<br/>(351)</th>
  <th>TDS Whole Year<br/>Cur. Emp. (₹)<br/>(352)</th>
  <th>TDS Prev.<br/>Emp. (₹)<br/>(353)</th>
  <th>Total TDS<br/>352+353 (₹)<br/>(354)</th>
  <th>Shortfall(+)<br/>Excess(−) (₹)<br/>(355)</th>
  <th>Higher Rate<br/>PAN (Y/N)<br/>(356)</th>
</tr></thead><tbody>");

                sSlNo = 1;
                foreach (var (s, ageCode, col335, col337, col338, col340, col342) in rows)
                {
                    double col343 = s.Chapter6ATotal;    // 80C+80CCC+80CCD portion
                    double col344 = 0;                   // other Ch VIA (NPS employer etc. — not split in model)
                    double col345 = col343 + col344;
                    double col346 = Math.Max(0, col342 - col345);   // taxable income
                    double col347 = s.TaxPayable + s.Rebate87A; // pre-rebate tax (matches SD[23])
                    double col348 = s.Surcharge;
                    double col349 = s.Cess;
                    double col350 = s.Rebate87A;         // rebate u/s 87A shown in col 350
                    double col351 = Math.Max(0, s.TotalTaxPayable); // net tax = col347+348+349-350
                    double col352 = s.TdsDeducted;
                    double col353 = s.PrevEmpTds;
                    double col354 = col352 + col353;
                    double col355 = col351 - col354;     // positive=shortfall, negative=excess

                    t343+=col343; t345+=col345; t346+=col346; t347+=col347;
                    t348+=col348; t349+=col349; t350+=col350; t351+=col351;
                    t352+=col352; t353+=col353; t354+=col354; t355+=col355;

                    string shortfallStr = col355 == 0 ? "" : col355 > 0
                        ? col355.ToString("N0") : $"({Math.Abs(col355):N0})";

                    sb.Append($@"<tr>
  <td class=""num"">{sSlNo++}</td>
  <td class=""num"">{(col343>0?col343.ToString("N0"):"")}</td>
  <td class=""num"">{(col344>0?col344.ToString("N0"):"")}</td>
  <td class=""num"">{(col345>0?col345.ToString("N0"):"")}</td>
  <td class=""num"">{col346:N0}</td>
  <td class=""num"">{(col347>0?col347.ToString("N0"):"")}</td>
  <td class=""num"">{(col348>0?col348.ToString("N0"):"")}</td>
  <td class=""num"">{(col349>0?col349.ToString("N0"):"")}</td>
  <td class=""num"">{(col350>0?col350.ToString("N0"):"")}</td>
  <td class=""num"">{(col351>0?col351.ToString("N0"):"")}</td>
  <td class=""num"">{(col352>0?col352.ToString("N0"):"")}</td>
  <td class=""num"">{(col353>0?col353.ToString("N0"):"")}</td>
  <td class=""num"">{(col354>0?col354.ToString("N0"):"")}</td>
  <td class=""num"" style=""color:{(col355>0?"#dc2626":col355<0?"#16a34a":"inherit")}"">{shortfallStr}</td>
  <td style=""text-align:center"">N</td>
</tr>");
                }
                sb.Append($@"<tr class=""total-row"">
  <td>TOTAL</td>
  <td class=""num"">{t343:N0}</td><td></td>
  <td class=""num"">{t345:N0}</td>
  <td class=""num"">{t346:N0}</td>
  <td class=""num"">{t347:N0}</td>
  <td class=""num"">{t348:N0}</td>
  <td class=""num"">{t349:N0}</td>
  <td class=""num"">{(t350>0?t350.ToString("N0"):"")}</td>
  <td class=""num"">{t351:N0}</td>
  <td class=""num"">{t352:N0}</td>
  <td class=""num"">{t353:N0}</td>
  <td class=""num"">{t354:N0}</td>
  <td class=""num"" style=""color:{(t355>0?"#dc2626":t355<0?"#16a34a":"inherit")}"">{(t355>0?t355.ToString("N0"):t355<0?$"({Math.Abs(t355):N0})":"")}</td>
  <td></td>
</tr></tbody></table></div>");
            }

            // ── Verification / signature block ─────────────────────────────────
            string annexureSigLabel = isSalaryForm
                ? "Name and signature of employer / person responsible for paying salary"
                : $"Signature of person responsible for {actionVerb} tax at source";
            sb.Append($@"
<div class=""sign-block"">
  <div style=""flex:2;padding-right:24px"">
    <div style=""font-weight:700;font-size:10pt;margin-bottom:6px"">{((officialForm) ? "DECLARATION" : "VERIFICATION")}</div>
    <div style=""font-size:9.5pt;line-height:1.7"">
      {((officialForm)
        ? $@"I, <span style=""border-bottom:1px solid #000;min-width:160px;display:inline-block"">{Esc(h.ResponsibleName)}</span> (name of the person responsible for {actionVerb} tax at source), having Permanent Account Number <span style=""border-bottom:1px solid #000;min-width:110px;display:inline-block;font-family:monospace"">{Esc(h.ResponsiblePan)}</span>, am the person responsible for {actionVerb} tax at source in the case of <span style=""border-bottom:1px solid #000;min-width:160px;display:inline-block"">{Esc(h.DeductorName)}</span> (name of the {partyLabel.ToLower()}). I certify that all the particulars furnished above are correct and complete."
        : $@"I, <span style=""border-bottom:1px solid #000;min-width:220px;display:inline-block;padding-bottom:1px"">{Esc(h.ResponsibleName)}</span>, hereby certify that all the particulars furnished above are correct and complete.")}
    </div>
    <div style=""margin-top:28px;display:flex;gap:40px"">
      <div>
        <div style=""border-top:1px solid #000;width:180px;padding-top:3px;font-size:8.5pt"">
          Place :&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;.
        </div>
      </div>
      <div>
        <div style=""border-top:1px solid #000;width:180px;padding-top:3px;font-size:8.5pt"">
          Date :&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;&nbsp;.
        </div>
      </div>
    </div>
    <div style=""margin-top:32px"">
      <div style=""border-top:1px solid #000;width:320px;padding-top:3px;font-size:8.5pt"">
        {Esc(annexureSigLabel)}
      </div>
    </div>
    <div style=""margin-top:20px"">
      <div style=""border-top:1px solid #000;width:320px;padding-top:3px;font-size:8.5pt"">
        Name and designation of person responsible for {actionVerb} tax at source
      </div>
      <div style=""font-size:9pt;margin-top:3px"">{Esc(h.ResponsibleName)} &nbsp;·&nbsp; {Esc(h.Designation)}</div>
    </div>
  </div>
  <div style=""flex:1;border-left:1px solid #ccc;padding-left:16px;font-size:8.5pt;color:#555"">
    <div style=""font-weight:700;color:#000;margin-bottom:4px"">Return Details</div>
    Form No. {formNo} &nbsp;·&nbsp; {actName}<br/>
    Quarter: {h.Quarter} &nbsp;·&nbsp; {yearLbl}<br/>
    TAN: {Esc(h.TanOfDeductor)}<br/>
    PAN: {Esc(h.PanOfDeductor)}<br/>
    {(h.IsCorrection ? $"Type: Correction ({h.CorrectionType})<br/>PRN: {Esc(h.PreviousPrn)}" : "Type: Original")}
  </div>
</div>

<div class=""footer"">
  {((officialForm && isTcsForm)
    // Official Form 143 (TCS) Notes (Gazette of India).
    ? @"<b>Notes:</b>&nbsp;
  (1)(a) In case of individual, the first, middle and last name shall be provided in full without abbreviations. In any other case, name shall be provided in full. (b) In case of Central Government, mention name of Ministry/Department; State Government, name of the State.&nbsp;
  (2) The address shall contain: Country/Region, Flat/Door/Block No., Road/Street/Block/Sector, PIN/ZIP Code, Post Office, Area/locality, District, State.&nbsp;
  (3) It is mandatory for non-Government collectors to quote PAN. Government collectors mention 'PANNOTREQD'.&nbsp;
  (4) In column (B), total tax shall be sum of amount of tax collected, Surcharge and Health &amp; Education Cess.&nbsp;
  (5) Fee paid under section 427 for late filing of TCS statement in 'Total Fee' (column D).&nbsp;
  (6) In column (F), Government DDOs to mention amount remitted by PAO/CDDO/DTO; other collectors write exact amount deposited.&nbsp;
  (7) In column (G), Government collectors write 'B' (book adjustment); other collectors write 'C'.&nbsp;
  (8) CIN/BIN particulars (H, I, J) must match TIN 2.0/TRACES portal.&nbsp;
  (9) In column (K), mention minor head as marked on the challan. (10) Amounts in &#8377; unless otherwise provided.&nbsp;
  Annexure reason codes: A = lower collection on a certificate u/s 395(3); B = non-collection on a declaration u/s 394(2); C = higher rate u/s 397(2) (no PAN); F = no collection u/s 394(5)/402(6); K = no collection u/s 394(4); Y = receipt at/below threshold u/s 394(1); Z = no/lower collection per notification u/s 400(1)."
    : (officialForm)
    // Official Form 138/140 Notes (Gazette of India).
    ? @"<b>Notes:</b>&nbsp;
  (1)(a) In case of individual, the first, middle and last name shall be provided in full without abbreviations. In any other case, name shall be provided in full.
  (b) In case of Central Government, mention name of Ministry/Department. In case of State Government, mention name of the State.&nbsp;
  (2) The address shall contain: Country/Region, Flat/Door/Block No., Road/Street/Block/Sector, PIN/ZIP Code, Post Office, Area/locality, District, State.&nbsp;
  (3) It is mandatory for non-Government deductors/payers to quote PAN. In case of Government deductors/payers, PAN should be mentioned as 'PANNOTREQD'.&nbsp;
  (4) In column (B), total tax shall be sum of amount of tax deducted, Surcharge and Health &amp; Education Cess.&nbsp;
  (5) Fee paid under section 427 for late filing of TDS statement to be mentioned in separate column of 'Total Fee' (column D).&nbsp;
  (6) In column (F), Government DDOs to mention the amount remitted by the PAO/CDDO/DTO. Other deductors/payers to write the exact amount deposited through challan.&nbsp;
  (7) In column (G), Government deductors/payers to write 'B' where TDS is remitted to the credit of Central Government through book adjustment. Other deductors/payers to write 'C'.&nbsp;
  (8) Challan/Transfer Voucher (CIN/BIN) particulars, i.e. H, I, J should be exactly the same as available at TIN 2.0/TRACES portal.&nbsp;
  (9) In column (K), mention minor head as marked on the challan.&nbsp;
  (10) Amounts to be filled in &#8377; unless otherwise provided.&nbsp;
  Annexure I, Note: Write 'A' if 'lower deduction' or 'no deduction' is on account of a certificate issued under section 395(1). Write 'C' if deduction is at a higher rate under section 397(2) on account of non-furnishing of PAN."
    : $@"<b>Notes:</b>&nbsp;
  (1) Indicate the type of deductor — 'Government' / 'Others'.&nbsp;
  (2) Government deductors to give particulars of transfer vouchers; other deductors to give particulars of challan No. regarding deposit into bank.&nbsp;
  (3) Column is relevant only for Government deductors.&nbsp;
  {(isSalaryForm ? "(4) Salary includes wages, annuity, pension, gratuity, fees, commission, bonus, perquisites, profits in lieu of or in addition to any salary or wages. (5) Please record on every page the totals of each of the columns." : $"(4) Note: Write 'A' if lower deduction or 'B' if no deduction is on account of a certificate under section {(newAct ? "395" : "197")}.")}")}<br/>
  <span style=""color:#aaa"">Computer-generated paper copy &nbsp;·&nbsp; CDeTDS v{CDeTDS.Common.AppConstants.AppVersion} &nbsp;·&nbsp; {now} &nbsp;·&nbsp; Submit the validated .fvu file to TRACES portal — this printout is for record only.</span>
</div>

<script>
function downloadHtml() {{
  var a = document.createElement('a');
  a.href = 'data:text/html;charset=utf-8,' + encodeURIComponent(document.documentElement.outerHTML);
  a.download = '{formNo}_{h.TanOfDeductor}_{h.FinancialYear.Replace("-","")}_Paper_{h.Quarter}.html';
  a.click();
}}
</script>
</body></html>");

            var html = sb.ToString();
            File.WriteAllText(outputPath, html, Encoding.UTF8);
            return outputPath;
        }

        static string Esc(string? s) => System.Net.WebUtility.HtmlEncode(s ?? "");

        // Government deductor categories (Central/State Govt, PSU, Local Authority).
        static bool IsGovtDeductor(string? deductorType)
        {
            var t = (deductorType ?? "").ToUpperInvariant();
            return t.Contains("GOV") || t.Contains("CENTRAL") || t.Contains("STATE")
                || t.Contains("PSU") || t.Contains("LOCAL") || t == "A" || t == "S";
        }
    }
}
