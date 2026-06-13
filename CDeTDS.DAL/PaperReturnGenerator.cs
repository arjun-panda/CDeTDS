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
            ? "(See section 392 of the Income-tax Act 2025)"
            : isTcsForm
                ? "(See section 394 of the Income-tax Act 2025)"
                : "(See section 393 of the Income-tax Act 2025)"
        : formNo=="24Q"
            ? "(See section 192 and rule 31A)"
            : formNo=="26Q"
                ? "(See sections 193, 194, 194A, 194B, 194BB, 194C, 194D, 194EE, 194F, 194G, 194H, 194-I, 194J, 194LA and rule 31A)"
                : formNo=="27EQ"
                    ? "(See section 206C and rule 31AA)"
                    : "(See rule 31A)")}</h3>
  <h3 style=""font-weight:normal;font-size:10pt"">
    {(isTcsForm
        ? $"Quarterly statement of collection of tax under {(newAct ? "section&nbsp;394 of the Income&#8209;tax Act,&nbsp;2025" : "sub&#8209;section&nbsp;(3) of section&nbsp;206C of the Income&#8209;tax Act,&nbsp;1961")}"
        : isSalaryForm
            ? $"Quarterly statement of deduction of tax under {(newAct ? "section&nbsp;392 of the Income&#8209;tax Act,&nbsp;2025" : "sub&#8209;section&nbsp;(3) of section&nbsp;200 of the Income&#8209;tax Act,&nbsp;1961")} in respect of salary"
            : $"Quarterly statement of deduction of tax under {(newAct ? "section&nbsp;393 of the Income&#8209;tax Act,&nbsp;2025" : "sub&#8209;section&nbsp;(3) of section&nbsp;200 of the Income&#8209;tax Act,&nbsp;1961")} in respect of payments other than salary")}
  </h3>
  <h3 style=""font-weight:normal;font-size:10pt"">
    for the quarter ended {(h.Quarter=="Q1"?"30th June":h.Quarter=="Q2"?"30th September":h.Quarter=="Q3"?"31st December":"31st March")} &nbsp;
    ({yearLbl})
    &nbsp;·&nbsp; {(h.IsCorrection ? $"CORRECTION — {h.CorrectionType}" : "ORIGINAL")}
  </h3>
  <div class=""watermark"">Generated by CDeTDS v{CDeTDS.Common.AppConstants.AppVersion} · {now} · FOR REFERENCE ONLY — Submit .fvu file to TRACES portal</div>
</div>

<div class=""info-grid"">
  <div class=""info-cell""><span class=""info-label"">Deductor Name</span><span class=""info-value"">{Esc(h.DeductorName)}</span></div>
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
</div>

<div class=""summary-box"">
  <div class=""kpi""><div class=""kpi-label"">Total Challans</div><div class=""kpi-value"">{d.Challans.Count}</div></div>
  <div class=""kpi""><div class=""kpi-label"">Total Deductees</div><div class=""kpi-value"">{d.Deductees.Count}</div></div>
  <div class=""kpi""><div class=""kpi-label"">Gross Amount Paid</div><div class=""kpi-value"">₹{d.TotalAmountPaid:N0}</div></div>
  <div class=""kpi""><div class=""kpi-label"">Total TDS Deducted</div><div class=""kpi-value"">₹{d.TotalTdsDeducted:N0}</div></div>
</div>
");

            // ── Challan table ──────────────────────────────────────────────────
            sb.Append(@"<div class=""section-title"">PART A — CHALLAN DETAILS</div>
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
  <th>Sr.<br/>314</th><th>Emp Ref<br/>315</th><th>PAN<br/>316</th>
  <th style=""min-width:140px"">Name of Employee<br/>317</th>
  <th>Sec<br/>318</th>
  <th>Date of Payment<br/>319</th>
  <th>Date of Deduction<br/>320</th>
  <th style=""text-align:right"">Amount Paid (₹)<br/>321</th>
  <th style=""text-align:right"">Tax (₹)<br/>322</th>
  <th style=""text-align:right"">Surcharge<br/>323</th>
  <th style=""text-align:right"">Cess<br/>324</th>
  <th style=""text-align:right"">Total TDS (₹)<br/>325</th>
  <th style=""text-align:right"">Total TDS Dep. (₹)<br/>326</th>
  <th>Date of Deposit<br/>327</th>
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
                        sb.Append($@"<tr>
  <td class=""num"">{cSlNo++}</td>
  <td class=""num""></td>
  <td style=""font-family:monospace;font-size:7pt"">{Esc(e.Pan)}</td>
  <td>{Esc(e.Name)}</td>
  <td style=""text-align:center"">92B</td>
  <td>{e.PaymentDate:dd/MM/yyyy}</td>
  <td>{e.PaymentDate:dd/MM/yyyy}</td>
  <td class=""num"">{e.AmountPaid:N0}</td>
  <td class=""num"">{(e.TdsDeducted > 0 ? e.TdsDeducted.ToString("N0") : "")}</td>
  <td class=""num"">{(e.Surcharge > 0 ? e.Surcharge.ToString("N0") : "")}</td>
  <td class=""num"">{(e.Cess > 0 ? e.Cess.ToString("N0") : "")}</td>
  <td class=""num"">{(tot > 0 ? tot.ToString("N0") : "")}</td>
  <td class=""num"">{(tot > 0 ? tot.ToString("N0") : "")}</td>
  <td>{ch.ChallanDate:dd/MM/yyyy}</td>
</tr>");
                    }
                    sb.Append($@"<tr class=""total-row"">
  <td colspan=""7""></td>
  <td class=""num"">{chAmt:N0}</td>
  <td class=""num"">{chDed:N0}</td>
  <td></td><td></td>
  <td class=""num"">{chTot:N0}</td>
  <td class=""num"">{chTot:N0}</td>
  <td></td>
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
                // 26Q / 27EQ — per-challan sections
                sb.Append(@"<div class=""section-title"">PART B — DEDUCTEE DETAILS (per Challan)</div>");

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
  <th>Sl.</th><th>PAN</th><th>Name</th><th>Section</th><th>Payment Date</th>
  <th style=""text-align:right"">Amount Paid (₹)</th>
  <th style=""text-align:right"">TDS (₹)</th>
  <th style=""text-align:right"">Surcharge (₹)</th>
  <th style=""text-align:right"">Cess (₹)</th>
  <th style=""text-align:right"">Total TDS (₹)</th>
  <th style=""text-align:right"">TDS Dep. (₹)</th>
  <th>Rate %</th>
</tr></thead><tbody>");

                    int q6SlNo = 1;
                    double q6Amt=0, q6Ded=0, q6Dep=0;
                    foreach (var e in chDeductees)
                    {
                        q6Amt += e.AmountPaid; q6Ded += e.TdsDeducted; q6Dep += e.TdsDeposited;
                        gAmt += e.AmountPaid; gDed += e.TdsDeducted; gDep += e.TdsDeposited;
                        sb.Append($@"<tr>
  <td class=""num"">{q6SlNo++}</td>
  <td style=""font-family:monospace;font-size:7pt"">{Esc(e.Pan)}</td>
  <td>{Esc(e.Name)}</td>
  <td>{Esc(e.Section)}</td>
  <td>{e.PaymentDate:dd/MM/yyyy}</td>
  <td class=""num"">{e.AmountPaid:N2}</td>
  <td class=""num"">{e.TdsDeducted:N2}</td>
  <td class=""num"">{e.Surcharge:N2}</td>
  <td class=""num"">{e.Cess:N2}</td>
  <td class=""num"">{(e.TdsDeducted+e.Surcharge+e.Cess):N2}</td>
  <td class=""num"">{e.TdsDeposited:N2}</td>
  <td class=""num"">{e.Rate:N2}</td>
</tr>");
                    }
                    sb.Append($@"<tr class=""total-row"">
  <td colspan=""5"">TOTAL</td>
  <td class=""num"">{q6Amt:N2}</td>
  <td class=""num"">{q6Ded:N2}</td>
  <td></td><td></td><td></td>
  <td class=""num"">{q6Dep:N2}</td>
  <td></td>
</tr></tbody></table>");
                }
                // Grand total for 26Q
                sb.Append($@"<div style=""background:#e8f5e9;border:2px solid #2e7d32;border-radius:4px;padding:6px 14px;
                     font-size:8pt;font-weight:700;display:flex;gap:30px;margin-bottom:8px"">
  <span>GRAND TOTAL</span>
  <span>Amount Paid: ₹{gAmt:N2}</span>
  <span>TDS Deducted: ₹{gDed:N2}</span>
  <span>TDS Deposited: ₹{gDep:N2}</span>
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
                : "Signature of person responsible for deducting tax at source";
            sb.Append($@"
<div class=""sign-block"">
  <div style=""flex:2;padding-right:24px"">
    <div style=""font-weight:700;font-size:10pt;margin-bottom:6px"">VERIFICATION</div>
    <div style=""font-size:9.5pt;line-height:1.7"">
      I, <span style=""border-bottom:1px solid #000;min-width:220px;display:inline-block;padding-bottom:1px"">{Esc(h.ResponsibleName)}</span>,
      hereby certify that all the particulars furnished above are correct and complete.
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
        Name and designation of person responsible for deducting tax at source
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
  <b>Notes:</b>&nbsp;
  (1) Indicate the type of deductor — 'Government' / 'Others'.&nbsp;
  (2) Government deductors to give particulars of transfer vouchers; other deductors to give particulars of challan No. regarding deposit into bank.&nbsp;
  (3) Column is relevant only for Government deductors.&nbsp;
  {(isSalaryForm ? "(4) Salary includes wages, annuity, pension, gratuity, fees, commission, bonus, perquisites, profits in lieu of or in addition to any salary or wages. (5) Please record on every page the totals of each of the columns." : $"(4) Note: Write 'A' if lower deduction or 'B' if no deduction is on account of a certificate under section {(newAct ? "395" : "197")}.")}<br/>
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
    }
}
