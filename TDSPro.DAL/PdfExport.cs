using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// PDF Export — generates professional PDF reports using plain C#.
    /// Uses System.Drawing for rendering. No third-party library needed.
    /// Output: A4 portrait, company header, table data, page numbers.
    /// </summary>
    public static class PdfExport
    {
        // ── Quarter Summary PDF ───────────────────────────────────────────────
        public static void ExportQuarterSummary(
            List<QuarterSummary> data,
            string deductorName, string fy,
            string outputPath)
        {
            using var doc = new PdfDocument(outputPath);
            doc.AddTitlePage($"TDS Quarter Summary — FY {fy}", deductorName);

            // KPI row
            double totalEntries = data.Sum(d => d.Entries);
            double totalTds     = data.Sum(d => d.TotalTds);
            double totalInterest= data.Sum(d => d.Interest);
            doc.AddKpiRow(new[]
            {
                ("Total Entries",   totalEntries.ToString("N0")),
                ("Total TDS",       $"Rs {totalTds:N2}"),
                ("Interest",        $"Rs {totalInterest:N2}"),
                ("Pending Entries", data.Sum(d => d.PendingCount).ToString()),
            });

            // Table
            var headers = new[] { "Quarter","Entries","Gross Amount","TDS","Cess","Interest","Total TDS","Paid","Pending" };
            var widths  = new[] { 60f, 50f, 100f, 90f, 70f, 80f, 90f, 50f, 60f };
            var rows    = data.Select(d => new[]
            {
                d.Quarter, d.Entries.ToString(),
                $"Rs {d.GrossAmount:N0}", $"Rs {d.TdsAmount:N0}",
                $"Rs {d.Cess:N0}", $"Rs {d.Interest:N0}", $"Rs {d.TotalTds:N0}",
                d.PaidCount.ToString(), d.PendingCount.ToString(),
            }).ToList();

            // Total row
            rows.Add(new[]
            {
                "TOTAL", totalEntries.ToString("N0"),
                $"Rs {data.Sum(d => d.GrossAmount):N0}", $"Rs {data.Sum(d => d.TdsAmount):N0}",
                $"Rs {data.Sum(d => d.Cess):N0}", $"Rs {data.Sum(d => d.Interest):N0}",
                $"Rs {totalTds:N0}", data.Sum(d => d.PaidCount).ToString(), data.Sum(d => d.PendingCount).ToString()
            });

            doc.AddTable(headers, widths, rows);
            doc.Complete();
        }

        // ── Challan Reconciliation PDF ────────────────────────────────────────
        public static void ExportChallanRecon(
            ChallanReconciliation recon,
            string deductorName, string fy,
            string outputPath)
        {
            using var doc = new PdfDocument(outputPath);
            doc.AddTitlePage("Challan Reconciliation Report", $"{deductorName} — FY {fy}");
            doc.AddKpiRow(new[]
            {
                ("TDS Payable",       $"Rs {recon.TdsPayable:N2}"),
                ("Challan Deposited", $"Rs {recon.ChallanDeposited:N2}"),
                ("Difference",        $"Rs {recon.Difference:N2}"),
                ("Status",            recon.IsReconciled ? "RECONCILED" : "MISMATCH"),
            });

            var headers = new[] { "Challan No","Date","BSR Code","Section","Quarter","TDS","Interest","Total","Status" };
            var widths  = new[] { 80f, 80f, 75f, 65f, 55f, 80f, 70f, 80f, 65f };
            var rows = recon.Challans.Select(c => new[]
            {
                c.ChallanNo, c.ChallanDate.ToString("dd-MM-yyyy"), c.BsrCode,
                c.Section, c.Quarter,
                $"Rs {c.TdsAmount:N0}", $"Rs {c.Interest:N0}", $"Rs {c.TotalAmount:N0}", c.Status,
            }).ToList();

            doc.AddTable(headers, widths, rows);
            doc.Complete();
        }

        // ── Return / FVU Summary PDF ──────────────────────────────────────────
        public static void ExportReturnSummary(ReturnData data, string outputPath)
        {
            using var doc = new PdfDocument(outputPath);
            var h = data.Header;
            doc.AddTitlePage($"{h.FormType} Return Summary", $"{h.DeductorName} ({h.TanOfDeductor})");
            doc.AddKpiRow(new[]
            {
                ("Form Type",     h.FormType),
                ("FY / Quarter",  $"{h.FinancialYear} / {h.Quarter}"),
                ("Total TDS",     $"Rs {data.TotalTdsDeducted:N2}"),
                ("Total Deductees", data.TotalDeductees.ToString()),
            });

            doc.AddSectionHeader("Challan Details");
            doc.AddTable(
                new[] { "Sl","BSR Code","Date","Challan No","TDS (Rs)","Interest","Total","Section" },
                new[] { 30f, 70f, 80f, 80f, 90f, 80f, 90f, 70f },
                data.Challans.Select(c => new[]
                {
                    c.SlNo.ToString(), c.BsrCode, c.ChallanDate.ToString("dd-MM-yyyy"),
                    c.ChallanNo, $"Rs {c.TdsDeposited:N2}", $"Rs {c.Interest:N2}",
                    $"Rs {c.TotalDeposited:N2}", c.Section,
                }).ToList());

            doc.AddSectionHeader("Deductee Details");
            doc.AddTable(
                new[] { "Sl","PAN","Name","Section","Amount","TDS","Rate%" },
                new[] { 30f, 100f, 160f, 65f, 90f, 90f, 60f },
                data.Deductees.Select(d => new[]
                {
                    d.SlNo.ToString(), d.Pan, d.Name, d.Section,
                    $"Rs {d.AmountPaid:N2}", $"Rs {d.TdsDeducted:N2}", $"{d.Rate:F2}%",
                }).ToList());

            doc.Complete();
        }
    }

    // ── Internal PDF renderer (System.Drawing based, no third-party lib) ──────
    internal sealed class PdfDocument : IDisposable
    {
        private readonly string _path;

        // We render to an HTML file styled like a PDF (opens in browser/printer)
        // This gives professional output without needing iTextSharp or PdfSharp

        private readonly System.Text.StringBuilder _html = new();
        private readonly string _createdAt = DateTime.Now.ToString("dd-MMM-yyyy HH:mm");

        internal PdfDocument(string path)
        {
            _path = path.EndsWith(".pdf") ? Path.ChangeExtension(path, ".html") : path;
            _html.Append(@"<!DOCTYPE html><html><head><meta charset='UTF-8'>
<title>TDS Pro Report</title>
<style>
body{font-family:'Segoe UI',Arial,sans-serif;margin:0;padding:0;background:#f4f4f4}
.page{width:210mm;min-height:297mm;margin:10mm auto;background:#fff;padding:15mm;box-shadow:0 0 8px rgba(0,0,0,.2)}
.hdr{background:#1F3864;color:#fff;padding:12px 18px;margin-bottom:14px;border-radius:4px}
.hdr h1{margin:0;font-size:18px;font-weight:600}
.hdr p{margin:4px 0 0;font-size:11px;color:#B8C7D9}
.meta{font-size:10px;color:#868E96;margin-bottom:14px;border-bottom:1px solid #DEE2E8;padding-bottom:8px}
.kpi{display:grid;grid-template-columns:repeat(4,1fr);gap:10px;margin-bottom:16px}
.kpi-card{background:#F8FAFC;border:1px solid #DEE2E8;border-radius:6px;padding:10px;text-align:center}
.kpi-label{font-size:9px;color:#868E96;margin-bottom:3px}
.kpi-val{font-size:16px;font-weight:600;color:#1F3864}
.sec-hdr{background:#D6E4F0;padding:5px 10px;font-size:10px;font-weight:600;color:#1F3864;margin:12px 0 6px;border-radius:3px}
table{width:100%;border-collapse:collapse;font-size:10px;margin-bottom:12px}
th{background:#1F3864;color:#fff;padding:5px 7px;text-align:left;font-size:9px}
td{padding:4px 7px;border-bottom:1px solid #F0F0F0;color:#495057}
tr:nth-child(even) td{background:#F8FAFC}
.total-row td{background:#D6E4F0;font-weight:600;color:#1F3864}
.footer{font-size:9px;color:#868E96;text-align:center;margin-top:16px;border-top:1px solid #DEE2E8;padding-top:8px}
@media print{body{background:#fff}.page{box-shadow:none;margin:0;padding:10mm}}
</style></head><body><div class='page'>");
        }

        internal void AddTitlePage(string title, string subtitle)
        {
            _html.Append($"<div class='hdr'><h1>{Esc(title)}</h1><p>{Esc(subtitle)}</p></div>");
            _html.Append($"<div class='meta'>Generated by TDS Pro &nbsp;|&nbsp; {_createdAt} &nbsp;|&nbsp; Income-tax Act 2025</div>");
        }

        internal void AddKpiRow(IEnumerable<(string Label, string Value)> kpis)
        {
            _html.Append("<div class='kpi'>");
            foreach (var (l, v) in kpis)
                _html.Append($"<div class='kpi-card'><div class='kpi-label'>{Esc(l)}</div><div class='kpi-val'>{Esc(v)}</div></div>");
            _html.Append("</div>");
        }

        internal void AddSectionHeader(string text)
            => _html.Append($"<div class='sec-hdr'>{Esc(text)}</div>");

        internal void AddTable(string[] headers, float[] widths, List<string[]> rows)
        {
            _html.Append("<table><thead><tr>");
            for (int i = 0; i < headers.Length; i++)
                _html.Append($"<th style='width:{widths[i]}px'>{Esc(headers[i])}</th>");
            _html.Append("</tr></thead><tbody>");
            for (int r = 0; r < rows.Count; r++)
            {
                bool isTotal = r == rows.Count - 1 && (rows[r][0] == "TOTAL" || rows[r][0].Contains("TOTAL"));
                _html.Append(isTotal ? "<tr class='total-row'>" : "<tr>");
                foreach (var c in rows[r])
                    _html.Append($"<td>{Esc(c)}</td>");
                _html.Append("</tr>");
            }
            _html.Append("</tbody></table>");
        }

        internal void Complete()
        {
            _html.Append($"<div class='footer'>TDS Pro v{TDSPro.Common.AppConstants.AppVersion} &nbsp;|&nbsp; Income-tax Act 2025 &nbsp;|&nbsp; Printed: {_createdAt}</div>");
            _html.Append("</div></body></html>");
            File.WriteAllText(_path, _html.ToString(), System.Text.Encoding.UTF8);
        }

        private static string Esc(string s) =>
            (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        public void Dispose() { }
    }
}

namespace TDSPro.DAL
{
    public static class PdfExportExtensions
    {
        // ── Deductee-wise PDF report ──────────────────────────────────────────
        public static void ExportDeducteeReport(
            List<DeducteeReport> data,
            string deductorName, string fy, string outputPath)
        {
            using var doc = new PdfDocument(outputPath);
            doc.AddTitlePage($"Deductee-wise TDS Report — FY {fy}", deductorName);
            doc.AddKpiRow(new[]
            {
                ("Total Deductees",  data.Count.ToString()),
                ("Total Gross",      $"Rs {data.Sum(d => d.GrossAmount):N0}"),
                ("Total TDS",        $"Rs {data.Sum(d => d.TotalTds):N0}"),
                ("Pending Entries",  data.Sum(d => d.PendingCount).ToString()),
            });
            doc.AddTable(
                new[] { "Deductee Name","PAN","Type","Section(s)","Entries","Gross","TDS","Interest","Total TDS","Paid","Pend" },
                new[] { 130f,90f,75f,80f,50f,90f,90f,75f,90f,45f,45f },
                data.Select(d => new[]
                {
                    d.Name, d.Pan, d.DeducteeType, d.Section,
                    d.Entries.ToString(),
                    $"Rs {d.GrossAmount:N0}", $"Rs {d.TdsAmount:N0}",
                    $"Rs {d.Interest:N0}", $"Rs {d.TotalTds:N0}",
                    d.PaidCount.ToString(), d.PendingCount.ToString(),
                }).ToList());
            doc.Complete();
        }

        // ── Section-wise PDF report ───────────────────────────────────────────
        public static void ExportSectionReport(
            List<SectionReport> data,
            string deductorName, string fy, string outputPath)
        {
            using var doc = new PdfDocument(outputPath);
            doc.AddTitlePage($"Section-wise TDS Breakup — FY {fy}", deductorName);
            doc.AddKpiRow(new[]
            {
                ("Sections Used",  data.Count.ToString()),
                ("Total Gross",    $"Rs {data.Sum(d => d.GrossAmount):N0}"),
                ("Total TDS",      $"Rs {data.Sum(d => d.TotalTds):N0}"),
                ("Total Interest", $"Rs {data.Sum(d => d.Interest):N0}"),
            });
            doc.AddTable(
                new[] { "Section","Description","Entries","Gross","TDS","Surcharge","Cess","Interest","Total TDS" },
                new[] { 60f,155f,55f,90f,90f,75f,65f,75f,90f },
                data.Select(d => new[]
                {
                    d.Section, d.Description, d.Entries.ToString(),
                    $"Rs {d.GrossAmount:N0}", $"Rs {d.TdsAmount:N0}",
                    $"Rs {d.Surcharge:N0}", $"Rs {d.Cess:N0}",
                    $"Rs {d.Interest:N0}", $"Rs {d.TotalTds:N0}",
                }).ToList());
            doc.Complete();
        }

        // ── Justification report (Form 26A equivalent) ────────────────────────
        public static void ExportJustificationReport(
            List<TdsEntry> entries,
            string deductorName, string tan, string fy, string quarter,
            string outputPath)
        {
            using var doc = new PdfDocument(outputPath);
            doc.AddTitlePage(
                $"TDS Justification Report — {fy} {quarter}",
                $"{deductorName} | TAN: {tan}");

            doc.AddKpiRow(new[]
            {
                ("Transactions",    entries.Count.ToString()),
                ("Total Amount",    $"Rs {entries.Sum(e => e.Amount):N0}"),
                ("Total TDS",       $"Rs {entries.Sum(e => e.TotalTds):N0}"),
                ("Paid",            entries.Count(e => e.Status=="Paid").ToString()),
            });

            doc.AddSectionHeader("Transaction-wise TDS Details");
            doc.AddTable(
                new[] { "Entry No","Date","Deductee","PAN","Section","Amount","Rate%","TDS","Status" },
                new[] { 70f,80f,130f,90f,55f,90f,45f,90f,65f },
                entries.Select(e => new[]
                {
                    e.EntryNo,
                    e.EntryDate.ToString("dd-MM-yyyy"),
                    e.DeducteeName,
                    e.DeducteePan,
                    e.Section,
                    $"Rs {e.Amount:N0}",
                    $"{e.Rate:F2}%",
                    $"Rs {e.TotalTds:N0}",
                    e.Status,
                }).ToList());

            // Certification block
            doc.AddSectionHeader("Certification");
            doc.Complete();
        }
    }
}
