using ClosedXML.Excel;
using TDSPro.Common;
using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// Excel Import / Export using ClosedXML.
    /// MIT License — 100% free, no Excel installation needed.
    /// No license key required.
    /// </summary>
    public static class ExcelEngine
    {
        // ── Theme colors ─────────────────────────────────────────────────────
        private static readonly XLColor HeaderBg    = XLColor.FromHtml("#1F3864");
        private static readonly XLColor HeaderFg    = XLColor.White;
        private static readonly XLColor AltRowBg    = XLColor.FromHtml("#F8FAFC");
        private static readonly XLColor TotalBg     = XLColor.FromHtml("#D6E4F0");
        private static readonly XLColor SampleBg    = XLColor.FromHtml("#FFFFCC");
        private static readonly XLColor GreenFg     = XLColor.FromHtml("#1E6B3C");
        private static readonly XLColor AmberFg     = XLColor.FromHtml("#7F4F24");
        private static readonly XLColor RedFg       = XLColor.FromHtml("#990000");
        private static readonly XLColor TallyHdrBg  = XLColor.FromHtml("#00467F");

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — TDS Entries
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportEntries(List<TdsEntry> entries, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("TDS Entries");

            var headers = new[]
            {
                "Entry No", "Entry Date", "Deductor", "Deductee", "PAN",
                "Section", "Payment Nature", "Amount (Rs)", "Rate %",
                "TDS Amount", "Surcharge", "Cess", "Total TDS",
                "Interest", "Late Fee", "Due Date", "Payment Date",
                "Challan No", "Status", "Financial Year", "Quarter", "Remarks"
            };

            // Header row
            for (int c = 0; c < headers.Length; c++)
            {
                var cell = ws.Cell(1, c + 1);
                cell.Value = headers[c];
                cell.Style.Font.Bold       = true;
                cell.Style.Font.FontColor  = XLColor.White;
                cell.Style.Fill.BackgroundColor = HeaderBg;
                cell.Style.Alignment.WrapText   = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                cell.Style.Border.OutsideBorder  = XLBorderStyleValues.Thin;
                cell.Style.Border.OutsideBorderColor = XLColor.White;
            }
            ws.Row(1).Height = 30;

            // Data rows
            int row = 2;
            foreach (var e in entries)
            {
                ws.Cell(row, 1).Value  = e.EntryNo;
                ws.Cell(row, 2).Value  = e.EntryDate.ToString("dd-MM-yyyy");
                ws.Cell(row, 3).Value  = e.DeductorName;
                ws.Cell(row, 4).Value  = e.DeducteeName;
                ws.Cell(row, 5).Value  = e.DeducteePan;
                ws.Cell(row, 6).Value  = e.Section;
                ws.Cell(row, 7).Value  = e.PaymentNature;
                ws.Cell(row, 8).Value  = e.Amount;
                ws.Cell(row, 9).Value  = e.Rate;
                ws.Cell(row, 10).Value = e.TdsAmount;
                ws.Cell(row, 11).Value = e.Surcharge;
                ws.Cell(row, 12).Value = e.Cess;
                ws.Cell(row, 13).Value = e.TotalTds;
                ws.Cell(row, 14).Value = e.Interest;
                ws.Cell(row, 15).Value = e.LateFee;
                ws.Cell(row, 16).Value = e.DueDate?.ToString("dd-MM-yyyy") ?? "";
                ws.Cell(row, 17).Value = e.PaymentDate?.ToString("dd-MM-yyyy") ?? "";
                ws.Cell(row, 18).Value = e.ChallanNo;
                ws.Cell(row, 19).Value = e.Status;
                ws.Cell(row, 20).Value = e.FinancialYear;
                ws.Cell(row, 21).Value = e.Quarter;
                ws.Cell(row, 22).Value = e.Remarks;

                // Alternate row shading
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = AltRowBg;

                // Status color
                var statusColor = e.Status switch
                {
                    "Paid"    => GreenFg,
                    "Pending" => AmberFg,
                    _         => RedFg
                };
                ws.Cell(row, 19).Style.Font.FontColor = statusColor;
                ws.Cell(row, 19).Style.Font.Bold      = true;

                // Number format for amount columns
                var amtFmt = "#,##0.00";
                foreach (int col in new[] { 8, 9, 10, 11, 12, 13, 14, 15 })
                    ws.Cell(row, col).Style.NumberFormat.Format = amtFmt;

                row++;
            }

            // Total row
            if (entries.Count > 0)
            {
                int totalRow = row;
                ws.Cell(totalRow, 1).Value = "TOTAL";
                ws.Cell(totalRow, 1).Style.Font.Bold = true;
                ws.Cell(totalRow, 8).FormulaA1  = $"SUM(H2:H{totalRow - 1})";
                ws.Cell(totalRow, 13).FormulaA1 = $"SUM(M2:M{totalRow - 1})";
                ws.Cell(totalRow, 14).FormulaA1 = $"SUM(N2:N{totalRow - 1})";

                var totalRange = ws.Range(totalRow, 1, totalRow, headers.Length);
                totalRange.Style.Fill.BackgroundColor = TotalBg;
                totalRange.Style.Font.Bold            = true;

                foreach (int col in new[] { 8, 13, 14 })
                    ws.Cell(totalRow, col).Style.NumberFormat.Format = "#,##0.00";
            }

            // Freeze header, auto-fit columns
            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 40);

            // Metadata
            wb.Properties.Author  = "TDS Pro";
            wb.Properties.Subject = $"TDS Entries Export — {DateTime.Today:dd-MMM-yyyy}";

            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Challans
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportChallans(List<Challan> challans, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Challans");

            var headers = new[]
            {
                "Challan No", "Date", "Deductor", "BSR Code", "Section",
                "Quarter", "TDS Amount", "Surcharge", "Cess", "Interest",
                "Late Fee", "Total Amount", "Bank Name", "Ack No",
                "Financial Year", "Status", "Remarks"
            };

            StyleHeaderRow(ws, 1, headers.Length);
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];

            int row = 2;
            foreach (var c in challans)
            {
                ws.Cell(row, 1).Value  = c.ChallanNo;
                ws.Cell(row, 2).Value  = c.ChallanDate.ToString("dd-MM-yyyy");
                ws.Cell(row, 3).Value  = c.DeductorName;
                ws.Cell(row, 4).Value  = c.BsrCode;
                ws.Cell(row, 5).Value  = c.Section;
                ws.Cell(row, 6).Value  = c.Quarter;
                ws.Cell(row, 7).Value  = c.TdsAmount;
                ws.Cell(row, 8).Value  = c.Surcharge;
                ws.Cell(row, 9).Value  = c.Cess;
                ws.Cell(row, 10).Value = c.Interest;
                ws.Cell(row, 11).Value = c.LateFee;
                ws.Cell(row, 12).Value = c.TotalAmount;
                ws.Cell(row, 13).Value = c.BankName;
                ws.Cell(row, 14).Value = c.AckNo;
                ws.Cell(row, 15).Value = c.FinancialYear;
                ws.Cell(row, 16).Value = c.Status;
                ws.Cell(row, 17).Value = c.Remarks;

                if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = AltRowBg;

                var statusColor = c.Status == "Paid" ? GreenFg : AmberFg;
                ws.Cell(row, 16).Style.Font.FontColor = statusColor;
                ws.Cell(row, 16).Style.Font.Bold = true;

                foreach (int col in new[] { 7, 8, 9, 10, 11, 12 })
                    ws.Cell(row, col).Style.NumberFormat.Format = "#,##0.00";

                row++;
            }

            // Totals
            if (challans.Count > 0)
            {
                var tr = row;
                ws.Cell(tr, 1).Value = "TOTAL";
                ws.Cell(tr, 7).FormulaA1  = $"SUM(G2:G{tr - 1})";
                ws.Cell(tr, 12).FormulaA1 = $"SUM(L2:L{tr - 1})";
                ws.Range(tr, 1, tr, headers.Length).Style.Fill.BackgroundColor = TotalBg;
                ws.Range(tr, 1, tr, headers.Length).Style.Font.Bold = true;
                foreach (int col in new[] { 7, 12 })
                    ws.Cell(tr, col).Style.NumberFormat.Format = "#,##0.00";
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 35);
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Deductee Master
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportDeductees(List<Deductee> deductees, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Deductees");

            var headers = new[]
            {
                "Deductee Code", "Name", "PAN", "Section", "Rate %",
                "Deductee Type", "Resident", "Lower Cert No",
                "Lower Rate %", "Lower Cert Till", "Remarks"
            };

            StyleHeaderRow(ws, 1, headers.Length);
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];

            int row = 2;
            foreach (var d in deductees)
            {
                ws.Cell(row, 1).Value  = d.DeducteeCode;
                ws.Cell(row, 2).Value  = d.Name;
                ws.Cell(row, 3).Value  = d.Pan;
                ws.Cell(row, 4).Value  = d.Section;
                ws.Cell(row, 5).Value  = d.Rate;
                ws.Cell(row, 6).Value  = d.DeducteeType;
                ws.Cell(row, 7).Value  = d.IsResident ? "Yes" : "No";
                ws.Cell(row, 8).Value  = d.LowerCertNo;
                ws.Cell(row, 9).Value  = d.LowerCertRate;
                ws.Cell(row, 10).Value = d.LowerCertTill;
                ws.Cell(row, 11).Value = d.Remarks;

                if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = AltRowBg;
                row++;
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 35);
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Tally-compatible journal voucher format
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportTallyFormat(List<TdsEntry> entries, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Tally Import");

            var headers = new[]
            {
                "Date", "Voucher Type", "Voucher No", "Ledger Name",
                "Dr/Cr", "Amount", "TDS Section", "TDS Rate", "TDS Amount",
                "PAN of Party", "Party Name", "Narration"
            };

            StyleHeaderRow(ws, 1, headers.Length, TallyHdrBg);
            for (int c = 0; c < headers.Length; c++)
                ws.Cell(1, c + 1).Value = headers[c];

            int row = 2;
            foreach (var e in entries)
            {
                // Dr line — expense / party
                ws.Cell(row, 1).Value  = e.EntryDate.ToString("dd-MM-yyyy");
                ws.Cell(row, 2).Value  = "Journal";
                ws.Cell(row, 3).Value  = e.EntryNo;
                ws.Cell(row, 4).Value  = e.DeducteeName;
                ws.Cell(row, 5).Value  = "Dr";
                ws.Cell(row, 6).Value  = e.Amount;
                ws.Cell(row, 7).Value  = e.Section;
                ws.Cell(row, 8).Value  = e.Rate;
                ws.Cell(row, 9).Value  = e.TdsAmount;
                ws.Cell(row, 10).Value = e.DeducteePan;
                ws.Cell(row, 11).Value = e.DeducteeName;
                ws.Cell(row, 12).Value = $"TDS u/s {e.Section} on payment to {e.DeducteeName}";
                ws.Cell(row, 5).Style.Font.FontColor = XLColor.FromHtml("#1F3864");
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                row++;

                // Cr line — TDS payable
                ws.Cell(row, 1).Value  = e.EntryDate.ToString("dd-MM-yyyy");
                ws.Cell(row, 2).Value  = "Journal";
                ws.Cell(row, 3).Value  = e.EntryNo;
                ws.Cell(row, 4).Value  = $"TDS Payable u/s {e.Section}";
                ws.Cell(row, 5).Value  = "Cr";
                ws.Cell(row, 6).Value  = e.TotalTds;
                ws.Cell(row, 7).Value  = e.Section;
                ws.Cell(row, 8).Value  = e.Rate;
                ws.Cell(row, 9).Value  = e.TdsAmount;
                ws.Cell(row, 10).Value = "";
                ws.Cell(row, 11).Value = "";
                ws.Cell(row, 12).Value = $"TDS payable u/s {e.Section}";
                ws.Cell(row, 5).Style.Font.FontColor = XLColor.FromHtml("#990000");
                ws.Cell(row, 6).Style.NumberFormat.Format = "#,##0.00";
                row++;

                // Blank separator row
                ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromHtml("#F4F6F9");
                row++;
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(8, 40);
            wb.Properties.Author = "TDS Pro";
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Import Template (blank with instructions)
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportImportTemplate(string path, string templateType)
        {
            using var wb = new XLWorkbook();

            if (templateType == "entries")
            {
                var ws   = wb.Worksheets.Add("TDS Entries");
                var info = wb.Worksheets.Add("Instructions");

                // Only 4 required columns — everything else is auto-calculated/derived
                var headers = new[]
                {
                    "Entry Date*\n(dd-MM-yyyy)",
                    "Deductee PAN*\n(e.g. ABCDE1234F)",
                    "Section*\n(e.g. 194C)",
                    "Payment Amount*\n(numbers only)"
                };

                StyleHeaderRow(ws, 1, headers.Length);
                for (int c = 0; c < headers.Length; c++)
                    ws.Cell(1, c + 1).Value = headers[c];
                ws.Row(1).Height = 40;

                // Mark auto-calculated columns header note
                var noteCell = ws.Cell(1, 5);
                noteCell.Value = "← Only these 4 columns needed. TDS Rate, Amount, Quarter, FY all auto-calculated.";
                noteCell.Style.Font.Italic    = true;
                noteCell.Style.Font.FontColor = XLColor.FromHtml("#888888");

                // Sample row (yellow) — shows real format
                var sample = new object[] { "15-04-2026", "ABCDE1234F", "194C", 150000 };
                for (int c = 0; c < sample.Length; c++)
                    ws.Cell(2, c + 1).Value = XLCellValue.FromObject(sample[c]);
                ws.Cell(2, 5).Value = "← Sample row — DELETE this row before importing";
                ws.Cell(2, 5).Style.Font.Italic    = true;
                ws.Cell(2, 5).Style.Font.FontColor = XLColor.FromHtml("#c62828");

                ws.Range(2, 1, 2, 5).Style.Fill.BackgroundColor = SampleBg;
                ws.Range(2, 1, 2, 5).Style.Font.Italic = true;

                ws.SheetView.FreezeRows(1);
                ws.Column(1).Width = 18;
                ws.Column(2).Width = 18;
                ws.Column(3).Width = 14;
                ws.Column(4).Width = 18;
                ws.Column(5).Width = 55;

                // Instructions sheet
                var instrData = new[]
                {
                    ("TDS Entry Import Template — Instructions", "", true),
                    ("", "", false),
                    ("ONLY 4 COLUMNS REQUIRED", "", true),
                    ("Col A  Entry Date",    "Format dd-MM-yyyy  e.g. 15-04-2026", false),
                    ("Col B  Deductee PAN",  "10-char PAN — format ABCDE1234F (must exist in Deductee Master)", false),
                    ("Col C  Section",       "TDS Section code  e.g. 194C  194J  194A  192", false),
                    ("Col D  Payment Amount","Gross payment amount — numbers only, no ₹ or commas", false),
                    ("", "", false),
                    ("AUTO-CALCULATED (do NOT add these columns)", "", true),
                    ("TDS Rate",      "Looked up from TDS Rules for the section + deductee type", false),
                    ("TDS Amount",    "Payment Amount × Rate ÷ 100", false),
                    ("Cess",          "4% of TDS Amount", false),
                    ("Total TDS",     "TDS + Cess", false),
                    ("Quarter",       "Auto-derived from Entry Date (Apr-Jun = Q1, etc.)", false),
                    ("Financial Year","Auto-set to current FY", false),
                    ("Deductee Type", "Auto-detected from 4th character of PAN", false),
                    ("", "", false),
                    ("IMPORTANT NOTES", "", true),
                    ("Row 2 (yellow)", "Sample row — DELETE before importing", false),
                    ("Deductee PAN",   "Deductee must exist in Deductee Master. If not found, auto-created from PAN.", false),
                    ("Date format",    "Only dd-MM-yyyy accepted  (day-month-year, dashes)", false),
                    ("Max rows",       "5,000 rows per file", false),
                };

                for (int i = 0; i < instrData.Length; i++)
                {
                    var (label, value, bold) = instrData[i];
                    info.Cell(i + 1, 1).Value = label;
                    info.Cell(i + 1, 2).Value = value;
                    if (bold)
                    {
                        info.Cell(i + 1, 1).Style.Font.Bold = true;
                        info.Cell(i + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#1F3864");
                    }
                }
                info.Columns().AdjustToContents(15, 60);
            }
            else if (templateType == "deductees")
            {
                var ws   = wb.Worksheets.Add("Deductees");
                var info = wb.Worksheets.Add("Instructions");

                // Required: Name + PAN + Section — Type and Rate auto-derived
                var req = new[] { "Name*", "PAN*\n(ABCDE1234F)", "Section*\n(e.g. 194C)" };
                var opt = new[]
                {
                    "Rate %\n(auto if blank)",
                    "Type\n(auto from PAN)",
                    "Resident\n(Yes / No)",
                    "Lower Cert No",
                    "Lower Rate %",
                    "Lower Cert Till\n(dd-MM-yyyy)",
                    "Remarks"
                };
                int reqCols = req.Length;
                int totCols = req.Length + opt.Length;

                // Required headers — green accent
                StyleHeaderRow(ws, 1, totCols);
                for (int c = 0; c < req.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = req[c];
                    ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E6B3C");
                }
                for (int c = 0; c < opt.Length; c++)
                {
                    ws.Cell(1, reqCols + c + 1).Value = opt[c];
                    ws.Cell(1, reqCols + c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                }
                ws.Row(1).Height = 40;

                // Sample row
                var sRow = new object[]
                {
                    "Raj Kumar Enterprises", "RAJKE1234C", "194C",
                    "", "", "Yes", "", "", "", "Sample row — DELETE before importing"
                };
                for (int c = 0; c < sRow.Length; c++)
                    ws.Cell(2, c + 1).Value = XLCellValue.FromObject(sRow[c]);
                ws.Range(2, 1, 2, totCols).Style.Fill.BackgroundColor = SampleBg;
                ws.Range(2, 1, 2, totCols).Style.Font.Italic = true;

                // Note row
                ws.Cell(3, 1).Value = "↑ Green columns = required.  Grey columns = optional (auto-filled if blank).  Delete Row 2 before importing.";
                ws.Cell(3, 1).Style.Font.Italic    = true;
                ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#555555");
                ws.Range(3, 1, 3, totCols).Merge();

                ws.SheetView.FreezeRows(1);
                ws.Column(1).Width = 28;
                ws.Column(2).Width = 18;
                ws.Column(3).Width = 14;
                for (int c = 4; c <= totCols; c++) ws.Column(c).Width = 16;

                // Instructions
                var instrData = new[]
                {
                    ("Deductee Master Import — Instructions", "", true),
                    ("", "", false),
                    ("REQUIRED COLUMNS (green header)", "", true),
                    ("Col A  Name",    "Full legal name of vendor / contractor / deductee", false),
                    ("Col B  PAN",     "10-char PAN — format ABCDE1234F", false),
                    ("Col C  Section", "TDS section applicable  e.g. 194C  194J  194I  194A  192", false),
                    ("", "", false),
                    ("AUTO-DERIVED IF LEFT BLANK (grey header)", "", true),
                    ("Rate %",         "Looked up from TDS Rules for Section + Deductee Type", false),
                    ("Type",           "Detected from 4th char of PAN: P=Individual, C=Company, H=HUF, F=Firm, etc.", false),
                    ("Resident",       "Defaults to Yes (resident)", false),
                    ("", "", false),
                    ("LOWER DEDUCTION CERTIFICATE (optional)", "", true),
                    ("Lower Cert No",  "Certificate number issued by IT Dept", false),
                    ("Lower Rate %",   "Rate specified in the certificate", false),
                    ("Lower Cert Till","Validity date of certificate  dd-MM-yyyy", false),
                    ("", "", false),
                    ("NOTES", "", true),
                    ("Upsert key",     "PAN — existing record updated, new record inserted", false),
                    ("Row 2 (yellow)", "Sample row — DELETE before importing", false),
                    ("Section codes",  "194C=Contractor  194J=Professional  194I=Rent  194A=Interest  194H=Commission  192=Salary", false),
                };
                for (int i = 0; i < instrData.Length; i++)
                {
                    var (lbl, val, bold) = instrData[i];
                    info.Cell(i + 1, 1).Value = lbl;
                    info.Cell(i + 1, 2).Value = val;
                    if (bold) { info.Cell(i + 1, 1).Style.Font.Bold = true; info.Cell(i + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#1F3864"); }
                }
                info.Columns().AdjustToContents(15, 60);
            }
            else if (templateType == "challans")
            {
                var ws   = wb.Worksheets.Add("Challans");
                var info = wb.Worksheets.Add("Instructions");

                // Required: BSR + Date + Challan No + TDS Amount — Quarter auto-derived from date
                var req = new[]
                {
                    "BSR Code*\n(7 digits)",
                    "Challan Date*\n(dd-MM-yyyy)",
                    "Challan Serial No*\n(5 digits)",
                    "TDS Amount*\n(numbers only)"
                };
                var opt = new[]
                {
                    "Section\n(e.g. 194C)",
                    "Surcharge\n(0 if none)",
                    "Cess\n(0 if none)",
                    "Interest\n(0 if none)",
                    "Late Fee\n(0 if none)",
                    "Bank Name",
                    "Ack No\n(ITNS 281 ref)",
                    "Remarks"
                };
                int reqCols = req.Length;
                int totCols = req.Length + opt.Length;

                StyleHeaderRow(ws, 1, totCols);
                for (int c = 0; c < req.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = req[c];
                    ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E6B3C");
                }
                for (int c = 0; c < opt.Length; c++)
                {
                    ws.Cell(1, reqCols + c + 1).Value = opt[c];
                    ws.Cell(1, reqCols + c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                }
                ws.Row(1).Height = 40;

                var sRow = new object[] { "1234567", "05-01-2026", "00234", 10000.00, "194C", 0, 0, 0, 0, "HDFC Bank", "ITNS123", "Sample row — DELETE before importing" };
                for (int c = 0; c < sRow.Length; c++) ws.Cell(2, c + 1).Value = XLCellValue.FromObject(sRow[c]);
                ws.Range(2, 1, 2, totCols).Style.Fill.BackgroundColor = SampleBg;
                ws.Range(2, 1, 2, totCols).Style.Font.Italic = true;

                ws.Cell(3, 1).Value = "↑ Green = required.  Grey = optional (Quarter & FY auto-derived from date).  Delete Row 2 before importing.";
                ws.Cell(3, 1).Style.Font.Italic = true;
                ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#555555");
                ws.Range(3, 1, 3, totCols).Merge();

                ws.SheetView.FreezeRows(1);
                ws.Column(1).Width = 16;
                ws.Column(2).Width = 18;
                ws.Column(3).Width = 18;
                ws.Column(4).Width = 18;
                for (int c = 5; c <= totCols; c++) ws.Column(c).Width = 15;

                var instrData = new[]
                {
                    ("Challan Import — Instructions", "", true),
                    ("", "", false),
                    ("REQUIRED COLUMNS (green header)", "", true),
                    ("Col A  BSR Code",       "7-digit Basic Statistical Return code of the bank branch", false),
                    ("Col B  Challan Date",   "Date of tax payment  dd-MM-yyyy", false),
                    ("Col C  Challan Serial No","5-digit serial number printed on the challan receipt", false),
                    ("Col D  TDS Amount",     "TDS deposited — numbers only, no ₹ or commas", false),
                    ("", "", false),
                    ("AUTO-DERIVED IF LEFT BLANK", "", true),
                    ("Quarter",  "Auto-derived from Challan Date (Apr-Jun=Q1, Jul-Sep=Q2, Oct-Dec=Q3, Jan-Mar=Q4)", false),
                    ("FY",       "Auto-derived from Challan Date", false),
                    ("Status",   "Always set to Paid on import", false),
                    ("", "", false),
                    ("OPTIONAL COLUMNS (grey header)", "", true),
                    ("Section",    "TDS section the payment relates to  e.g. 194C", false),
                    ("Surcharge",  "Enter 0 if not applicable", false),
                    ("Cess",       "Enter 0 if not applicable", false),
                    ("Interest",   "Interest u/s 201(1A) for late deposit", false),
                    ("Late Fee",   "Late filing fee u/s 234E", false),
                    ("Bank Name",  "Name of bank where challan was deposited", false),
                    ("Ack No",     "ITNS 281 acknowledgement reference", false),
                    ("", "", false),
                    ("NOTES", "", true),
                    ("Upsert key",     "BSR + Challan Serial No + Date — duplicate row updates existing record", false),
                    ("Row 2 (yellow)", "Sample row — DELETE before importing", false),
                };
                for (int i = 0; i < instrData.Length; i++)
                {
                    var (lbl, val, bold) = instrData[i];
                    info.Cell(i + 1, 1).Value = lbl;
                    info.Cell(i + 1, 2).Value = val;
                    if (bold) { info.Cell(i + 1, 1).Style.Font.Bold = true; info.Cell(i + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#1F3864"); }
                }
                info.Columns().AdjustToContents(15, 70);
            }
            else if (templateType == "employees")
            {
                var ws   = wb.Worksheets.Add("Employees");
                var info = wb.Worksheets.Add("Instructions");

                // Required: Name + PAN. Everything else optional.
                var req = new[] { "Name*", "PAN*\n(ABCDE1234F)" };
                var opt = new[]
                {
                    "Employee Code",
                    "Designation",
                    "Department",
                    "Date of Joining\n(dd-MM-yyyy)",
                    "Date of Birth\n(dd-MM-yyyy)",
                    "Gender\n(Male/Female/Other)",
                    "Email",
                    "Mobile",
                    "Tax Regime\n(New / Old)\ndefault: New",
                    "Basic Salary\n(monthly)",
                    "HRA\n(monthly)",
                    "DA\n(monthly)",
                    "Special Allowance\n(monthly)",
                    "Other Allowance\n(monthly)",
                    "PF Applicable\n(Yes / No)",
                    "ESI Applicable\n(Yes / No)"
                };
                int reqCols = req.Length;
                int totCols = req.Length + opt.Length;

                StyleHeaderRow(ws, 1, totCols);
                for (int c = 0; c < req.Length; c++)
                {
                    ws.Cell(1, c + 1).Value = req[c];
                    ws.Cell(1, c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#1E6B3C");
                }
                for (int c = 0; c < opt.Length; c++)
                {
                    ws.Cell(1, reqCols + c + 1).Value = opt[c];
                    ws.Cell(1, reqCols + c + 1).Style.Fill.BackgroundColor = XLColor.FromHtml("#37474F");
                }
                ws.Row(1).Height = 40;

                var sRow = new object[]
                {
                    "Raj Kumar", "RAJKU1234A",
                    "EMP001", "Manager", "Accounts",
                    "01-04-2024", "15-06-1990", "Male",
                    "raj@example.com", "9999999999", "New",
                    30000, 10000, 0, 5000, 2000, "Yes", "No"
                };
                for (int c = 0; c < sRow.Length; c++) ws.Cell(2, c + 1).Value = XLCellValue.FromObject(sRow[c]);
                ws.Range(2, 1, 2, totCols).Style.Fill.BackgroundColor = SampleBg;
                ws.Range(2, 1, 2, totCols).Style.Font.Italic = true;

                ws.Cell(3, 1).Value = "↑ Green = required.  Grey = optional.  Employee Code auto-generated if blank.  Tax Regime defaults to New.  Delete Row 2 before importing.";
                ws.Cell(3, 1).Style.Font.Italic = true;
                ws.Cell(3, 1).Style.Font.FontColor = XLColor.FromHtml("#555555");
                ws.Range(3, 1, 3, totCols).Merge();

                ws.SheetView.FreezeRows(1);
                ws.Column(1).Width = 28; ws.Column(2).Width = 18;
                for (int c = 3; c <= totCols; c++) ws.Column(c).Width = 18;

                var instrData = new[]
                {
                    ("Employee Master Import — Instructions", "", true),
                    ("", "", false),
                    ("REQUIRED COLUMNS (green header)", "", true),
                    ("Col A  Name",  "Full name of employee as on PAN card", false),
                    ("Col B  PAN",   "10-char PAN — format ABCDE1234F", false),
                    ("", "", false),
                    ("OPTIONAL COLUMNS (grey header — leave blank to auto-fill)", "", true),
                    ("Employee Code",     "Auto-generated as EMP00001, EMP00002 etc. if blank", false),
                    ("Designation",       "Job title e.g. Manager, Executive", false),
                    ("Department",        "Department name e.g. Accounts, HR", false),
                    ("Date of Joining",   "Format dd-MM-yyyy", false),
                    ("Date of Birth",     "Format dd-MM-yyyy", false),
                    ("Gender",            "Male / Female / Other  (defaults to Male)", false),
                    ("Email",             "Employee email address", false),
                    ("Mobile",            "10-digit mobile number", false),
                    ("Tax Regime",        "New or Old  (defaults to New — beneficial for most employees)", false),
                    ("", "", false),
                    ("SALARY STRUCTURE (optional — for TDS on salary calculation)", "", true),
                    ("Basic Salary",      "Monthly basic salary", false),
                    ("HRA",               "Monthly HRA component", false),
                    ("DA",                "Monthly Dearness Allowance", false),
                    ("Special Allowance", "Monthly special allowance", false),
                    ("Other Allowance",   "Monthly any other allowance", false),
                    ("PF Applicable",     "Yes / No  — whether PF is deducted (12% of Basic)", false),
                    ("ESI Applicable",    "Yes / No  — whether ESI is deducted (0.75% of gross)", false),
                    ("", "", false),
                    ("NOTES", "", true),
                    ("Upsert key",     "PAN (per company) — existing employee updated, new employee inserted", false),
                    ("Row 2 (yellow)", "Sample row — DELETE before importing", false),
                    ("Salary TDS",     "After import, run Payroll → Monthly Run to compute TDS on salary", false),
                };
                for (int i = 0; i < instrData.Length; i++)
                {
                    var (lbl, val, bold) = instrData[i];
                    info.Cell(i + 1, 1).Value = lbl;
                    info.Cell(i + 1, 2).Value = val;
                    if (bold) { info.Cell(i + 1, 1).Style.Font.Bold = true; info.Cell(i + 1, 1).Style.Font.FontColor = XLColor.FromHtml("#1F3864"); }
                }
                info.Columns().AdjustToContents(15, 65);
            }

            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // IMPORT — TDS Entries from Excel
        // ══════════════════════════════════════════════════════════════════════
        public static ImportResult ImportEntries(string path)
        {
            var result  = new ImportResult();
            var engine  = new TdsRulesEngine();

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(w =>
                w.Name.Contains("TDS", StringComparison.OrdinalIgnoreCase) ||
                w.Name.Contains("Entries", StringComparison.OrdinalIgnoreCase) ||
                w.Name.Equals("Sheet1", StringComparison.OrdinalIgnoreCase)) ??
                wb.Worksheets.First();

            if (ws.LastRowUsed() == null) { result.Errors.Add("Worksheet is empty."); return result; }

            var hdrErr = ValidateHeaders(ws, new[] { "date", "pan", "section", "amount" });
            if (hdrErr != null) { result.Errors.Add(hdrErr); return result; }

            BackupBeforeImport();

            int lastRow  = ws.LastRowUsed()!.RowNumber();
            int startRow = FindHeaderRow(ws) + 1;
            if (startRow < 2) startRow = 2;

            using var conn = Database.GetConnection();

            // Cache active deductor — import always targets the single active deductor
            using var dedCmd = conn.CreateCommand();
            dedCmd.CommandText = "SELECT id FROM deductors WHERE is_active=1 ORDER BY id LIMIT 1";
            var deductorId = dedCmd.ExecuteScalar();
            if (deductorId == null)
            {
                result.Errors.Add("No active deductor found. Please set up and select a deductor first.");
                return result;
            }

            for (int row = startRow; row <= lastRow; row++)
            {
                // Skip blank rows and yellow sample rows
                var firstCell = ws.Cell(row, 1).GetString().Trim();
                if (string.IsNullOrEmpty(firstCell)) continue;
                try
                {
                    if (ws.Cell(row, 1).Style.Fill.BackgroundColor.Color.ToArgb() == System.Drawing.Color.FromArgb(255, 255, 204).ToArgb())
                        continue;
                }
                catch { }
                // Skip rows where any text cell contains "sample"
                if (ws.Cell(row, 2).GetString().Contains("Sample", StringComparison.OrdinalIgnoreCase) ||
                    ws.Cell(row, 4).GetString().Contains("Sample", StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    // ── 4 required columns only ──────────────────────────────
                    var dateStr = ws.Cell(row, 1).GetString().Trim();
                    var pan     = ws.Cell(row, 2).GetString().Trim().ToUpper();
                    var section = ws.Cell(row, 3).GetString().Trim().ToUpper();
                    var amtText = ws.Cell(row, 4).GetString().Trim().Replace(",", "").Replace("₹", "");

                    // Validate
                    var rowErrs = new List<string>();
                    if (!DateTime.TryParseExact(dateStr,
                        new[] { "dd-MM-yyyy", "dd/MM/yyyy", "d-M-yyyy", "d/M/yyyy" },
                        null, System.Globalization.DateTimeStyles.None, out var entryDate))
                        rowErrs.Add($"Row {row}: Invalid date '{dateStr}' — use dd-MM-yyyy");

                    if (!Validators.IsValidPan(pan))
                        rowErrs.Add($"Row {row}: Invalid PAN '{pan}' — format ABCDE1234F");

                    if (string.IsNullOrEmpty(section))
                        rowErrs.Add($"Row {row}: Section is required");

                    if (!double.TryParse(amtText, System.Globalization.NumberStyles.Any,
                            System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
                        rowErrs.Add($"Row {row}: Invalid amount '{amtText}'");

                    if (rowErrs.Count > 0)
                    {
                        result.Errors.AddRange(rowErrs);
                        result.FailCount++;
                        continue;
                    }

                    // ── Deductee lookup / auto-create ────────────────────────
                    using var c2 = conn.CreateCommand();
                    c2.CommandText = "SELECT id, deductee_type FROM deductees WHERE pan=@p LIMIT 1";
                    c2.Parameters.AddWithValue("@p", pan);
                    object? deducteeId = null;
                    string  deducteeType = "Individual";
                    using (var rdr = c2.ExecuteReader())
                    {
                        if (rdr.Read()) { deducteeId = rdr.GetValue(0); deducteeType = rdr.GetString(1); }
                    }

                    if (deducteeId == null)
                    {
                        deducteeType = Validators.PanToDeducteeType(pan);
                        if (string.IsNullOrEmpty(deducteeType)) deducteeType = "Individual";
                        double autoRate = 0;
                        try { autoRate = engine.GetApplicableRule(section, deducteeType, true, entryDate)?.TdsRate ?? 0; } catch { }

                        using var cntD = conn.CreateCommand();
                        cntD.CommandText = "SELECT COALESCE(MAX(id),0)+1 FROM deductees";
                        var nextDId = (long)(cntD.ExecuteScalar() ?? 1L);

                        using var insD = conn.CreateCommand();
                        insD.CommandText = @"INSERT INTO deductees
                            (deductee_code,name,pan,section,rate,deductee_type,is_resident)
                            VALUES (@dc,@n,@p,@s,@r,@dt,1)";
                        insD.Parameters.AddWithValue("@dc", $"DED{nextDId:D5}");
                        insD.Parameters.AddWithValue("@n",  pan);
                        insD.Parameters.AddWithValue("@p",  pan);
                        insD.Parameters.AddWithValue("@s",  section);
                        insD.Parameters.AddWithValue("@r",  autoRate);
                        insD.Parameters.AddWithValue("@dt", deducteeType);
                        insD.ExecuteNonQuery();

                        using var c2b = conn.CreateCommand();
                        c2b.CommandText = "SELECT id FROM deductees WHERE pan=@p";
                        c2b.Parameters.AddWithValue("@p", pan);
                        deducteeId = c2b.ExecuteScalar();
                        result.Errors.Add($"Row {row}: Deductee '{pan}' auto-created ({deducteeType}) — update name in Deductee Master.");
                    }

                    // ── Auto-derive rate, nature, TDS, cess, quarter, FY ─────
                    double rate     = 0;
                    double cessRate = 0;
                    string nature   = "";
                    try
                    {
                        var rule = engine.GetApplicableRule(section, deducteeType, true, entryDate);
                        if (rule != null) { rate = rule.TdsRate; nature = rule.NatureOfPayment; cessRate = rule.CessRate; }
                    } catch { }

                    var tdsAmt = Math.Round(amount * rate / 100, 0);
                    var cess   = Math.Round(tdsAmt * cessRate / 100, 2);
                    var total  = tdsAmt + cess;

                    // Quarter from date
                    var quarter = entryDate.Month switch
                    {
                        >= 4 and <= 6  => "Q1",
                        >= 7 and <= 9  => "Q2",
                        >= 10 and <= 12 => "Q3",
                        _               => "Q4"
                    };

                    // FY from date
                    var fyYear = entryDate.Month >= 4 ? entryDate.Year : entryDate.Year - 1;
                    var fy     = $"{fyYear}-{(fyYear + 1) % 100:D2}";

                    // Deterministic entry_no: hash of all key fields so re-importing same row
                    // always produces the same ID (upsert = no duplicate) while two genuinely
                    // different rows (even same deductee+date+amount) get different IDs (both saved).
                    var hashSrc = $"{deductorId}|{deducteeId}|{entryDate:yyyy-MM-dd}|{section}|{amount:F2}|{quarter}|{row}";
                    var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(hashSrc));
                    var entryNo = $"IMP-{row}-{Convert.ToHexString(hashBytes)[..6]}";

                    using var ins = conn.CreateCommand();
                    ins.CommandText = @"
                        INSERT INTO tds_entries
                        (entry_no,entry_date,deductor_id,deductee_id,section,nature_of_payment,
                         amount,rate,tds_amount,surcharge,cess,total_tds,
                         payment_date,interest,late_fee,challan_no,
                         status,financial_year,quarter,remarks)
                        VALUES
                        (@en,@ed,@di,@dei,@s,@np,@am,@ra,@ta,0,@ce,@tt,
                         '',0,0,'','Pending',@fy,@qt,'')
                        ON CONFLICT(entry_no) DO UPDATE SET
                        rate=excluded.rate, nature_of_payment=excluded.nature_of_payment,
                        tds_amount=excluded.tds_amount,
                        cess=excluded.cess, total_tds=excluded.total_tds";
                    ins.Parameters.AddWithValue("@en",  entryNo);
                    ins.Parameters.AddWithValue("@ed",  entryDate.ToString("yyyy-MM-dd"));
                    ins.Parameters.AddWithValue("@di",  deductorId);
                    ins.Parameters.AddWithValue("@dei", deducteeId);
                    ins.Parameters.AddWithValue("@s",   section);
                    ins.Parameters.AddWithValue("@np",  nature);
                    ins.Parameters.AddWithValue("@am",  amount);
                    ins.Parameters.AddWithValue("@ra",  rate);
                    ins.Parameters.AddWithValue("@ta",  tdsAmt);
                    ins.Parameters.AddWithValue("@ce",  cess);
                    ins.Parameters.AddWithValue("@tt",  total);
                    ins.Parameters.AddWithValue("@fy",  fy);
                    ins.Parameters.AddWithValue("@qt",  quarter);
                    ins.ExecuteNonQuery();

                    result.SuccessCount++;
                    result.ImportedEntries.Add(entryNo);
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {row}: {ex.Message}");
                    result.FailCount++;
                }
            }

            Database.LogAction("system", "IMPORT_EXCEL", "TdsEntry",
                $"{result.SuccessCount} imported, {result.FailCount} failed");
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // IMPORT — Deductees from Excel
        // ══════════════════════════════════════════════════════════════════════
        public static ImportResult ImportDeductees(string path)
        {
            var result = new ImportResult();

            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(w =>
                w.Name.Contains("Deductee", StringComparison.OrdinalIgnoreCase)) ??
                wb.Worksheets.First();

            if (ws.LastRowUsed() == null) { result.Errors.Add("Worksheet is empty."); return result; }

            var hdrErr = ValidateHeaders(ws, new[] { "name", "pan" });
            if (hdrErr != null) { result.Errors.Add(hdrErr); return result; }

            BackupBeforeImport();

            int lastRow  = ws.LastRowUsed()!.RowNumber();
            int startRow = FindHeaderRow(ws) + 1;
            if (startRow < 2) startRow = 2;

            using var conn = Database.GetConnection();

            for (int row = startRow; row <= lastRow; row++)
            {
                try
                {
                    var name    = ws.Cell(row, 1).GetString().Trim();
                    var pan     = ws.Cell(row, 2).GetString().Trim().ToUpper();
                    var section = ws.Cell(row, 3).GetString().Trim().ToUpper();

                    if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(pan)) continue;
                    if (ws.Cell(row, 10).GetString().Contains("Sample", StringComparison.OrdinalIgnoreCase)) continue;

                    if (!Validators.IsValidPan(pan))
                    {
                        result.Errors.Add($"Row {row}: Invalid PAN '{pan}' for '{name}'");
                        result.FailCount++;
                        continue;
                    }

                    double.TryParse(ws.Cell(row, 4).GetString(), out var rate);
                    var type     = ws.Cell(row, 5).GetString().Trim();
                    // Auto-detect type from PAN 4th character if not supplied in Excel
                    if (string.IsNullOrEmpty(type))
                        type = Validators.PanToDeducteeType(pan);
                    if (string.IsNullOrEmpty(type)) type = "Individual";
                    // Auto-lookup rate from rules engine if not supplied in Excel
                    if (rate == 0 && !string.IsNullOrEmpty(section))
                    {
                        try
                        {
                            var rule = new TdsRulesEngine().GetApplicableRule(section, type, true, DateTime.Today);
                            if (rule != null) rate = rule.TdsRate;
                        }
                        catch { }
                    }
                    var resident = !ws.Cell(row, 6).GetString().Trim().Equals("No", StringComparison.OrdinalIgnoreCase);
                    var certNo   = ws.Cell(row, 7).GetString().Trim();
                    double.TryParse(ws.Cell(row, 8).GetString(), out var certRate);
                    var certTill = ws.Cell(row, 9).GetString().Trim();
                    var remarks  = ws.Cell(row, 10).GetString().Trim();

                    // Check if PAN exists
                    using var chk = conn.CreateCommand();
                    chk.CommandText = "SELECT id FROM deductees WHERE pan=@p";
                    chk.Parameters.AddWithValue("@p", pan);
                    var existing = chk.ExecuteScalar();

                    if (existing != null)
                    {
                        // Update
                        using var upd = conn.CreateCommand();
                        upd.CommandText = @"UPDATE deductees SET
                            name=@n, section=@s, rate=@r, deductee_type=@dt,
                            is_resident=@ir, lower_cert_no=@lc,
                            lower_cert_rate=@lr, lower_cert_till=@lt, remarks=@rm
                            WHERE pan=@p";
                        upd.Parameters.AddWithValue("@n",  name);
                        upd.Parameters.AddWithValue("@s",  section);
                        upd.Parameters.AddWithValue("@r",  rate);
                        upd.Parameters.AddWithValue("@dt", type);
                        upd.Parameters.AddWithValue("@ir", resident ? 1 : 0);
                        upd.Parameters.AddWithValue("@lc", certNo);
                        upd.Parameters.AddWithValue("@lr", certRate);
                        upd.Parameters.AddWithValue("@lt", certTill);
                        upd.Parameters.AddWithValue("@rm", remarks);
                        upd.Parameters.AddWithValue("@p",  pan);
                        upd.ExecuteNonQuery();
                        result.UpdatedCount++;
                    }
                    else
                    {
                        // Insert new
                        using var cnt = conn.CreateCommand();
                        cnt.CommandText = "SELECT COALESCE(MAX(id),0)+1 FROM deductees";
                        var nextId = (long)(cnt.ExecuteScalar() ?? 1L);
                        var code   = $"DED{nextId:D5}";

                        using var ins = conn.CreateCommand();
                        ins.CommandText = @"INSERT INTO deductees
                            (deductee_code, name, pan, section, rate, deductee_type,
                             is_resident, lower_cert_no, lower_cert_rate, lower_cert_till, remarks)
                            VALUES
                            (@dc,@n,@p,@s,@r,@dt,@ir,@lc,@lr,@lt,@rm)";
                        ins.Parameters.AddWithValue("@dc", code);
                        ins.Parameters.AddWithValue("@n",  name);
                        ins.Parameters.AddWithValue("@p",  pan);
                        ins.Parameters.AddWithValue("@s",  section);
                        ins.Parameters.AddWithValue("@r",  rate);
                        ins.Parameters.AddWithValue("@dt", type);
                        ins.Parameters.AddWithValue("@ir", resident ? 1 : 0);
                        ins.Parameters.AddWithValue("@lc", certNo);
                        ins.Parameters.AddWithValue("@lr", certRate);
                        ins.Parameters.AddWithValue("@lt", certTill);
                        ins.Parameters.AddWithValue("@rm", remarks);
                        ins.ExecuteNonQuery();
                        result.SuccessCount++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {row}: {ex.Message}");
                    result.FailCount++;
                }
            }

            Database.LogAction("system", "IMPORT_EXCEL", "Deductee",
                $"{result.SuccessCount} new, {result.UpdatedCount} updated, {result.FailCount} failed");
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // IMPORT — Challans from Excel
        // ══════════════════════════════════════════════════════════════════════
        public static ImportResult ImportChallans(string path, int deductorId, string fy)
        {
            var result = new ImportResult();
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(w =>
                w.Name.Contains("Challan", StringComparison.OrdinalIgnoreCase)) ??
                wb.Worksheets.First();

            if (ws.LastRowUsed() == null) { result.Errors.Add("Worksheet is empty."); return result; }

            var hdrErr = ValidateHeaders(ws, new[] { "bsr", "date", "challan" });
            if (hdrErr != null) { result.Errors.Add(hdrErr); return result; }

            BackupBeforeImport();

            int lastRow  = ws.LastRowUsed()!.RowNumber();
            int startRow = FindHeaderRow(ws) + 1;
            if (startRow < 2) startRow = 2;

            using var conn = Database.GetConnection();

            for (int row = startRow; row <= lastRow; row++)
            {
                if (string.IsNullOrEmpty(ws.Cell(row, 1).GetString().Trim())) continue;
                if (ws.Cell(row, 12).GetString().Contains("Sample", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    // Required columns: BSR=1, Date=2, ChallanNo=3, TDS=4
                    var bsr      = ws.Cell(row, 1).GetString().Trim();
                    var dateStr  = ws.Cell(row, 2).GetString().Trim();
                    var challanNo= ws.Cell(row, 3).GetString().Trim();

                    if (string.IsNullOrEmpty(bsr)) { result.Errors.Add($"Row {row}: BSR code required"); result.FailCount++; continue; }
                    if (!DateTime.TryParseExact(dateStr, new[] { "dd-MM-yyyy","dd/MM/yyyy","yyyy-MM-dd" },
                        null, System.Globalization.DateTimeStyles.None, out var challanDate))
                    { result.Errors.Add($"Row {row}: Invalid date '{dateStr}'"); result.FailCount++; continue; }

                    double.TryParse(ws.Cell(row, 4).GetString().Replace(",", "").Replace("₹", ""), out var tdsAmt);

                    // Optional columns: Section=5, Surcharge=6, Cess=7, Interest=8, LateFee=9, Bank=10, AckNo=11, Remarks=12
                    var section  = ws.Cell(row, 5).GetString().Trim().ToUpper();
                    double.TryParse(ws.Cell(row, 6).GetString().Replace(",", ""), out var surcharge);
                    double.TryParse(ws.Cell(row, 7).GetString().Replace(",", ""), out var cess);
                    double.TryParse(ws.Cell(row, 8).GetString().Replace(",", ""), out var interest);
                    double.TryParse(ws.Cell(row, 9).GetString().Replace(",", ""), out var lateFee);
                    var total    = tdsAmt + surcharge + cess + interest + lateFee;
                    var bankName = ws.Cell(row, 10).GetString().Trim();
                    var ackNo    = ws.Cell(row, 11).GetString().Trim();
                    var remarks  = ws.Cell(row, 12).GetString().Trim();

                    // Quarter and FY auto-derived from challan date
                    var quarter = challanDate.Month switch
                    {
                        >= 4 and <= 6  => "Q1",
                        >= 7 and <= 9  => "Q2",
                        >= 10 and <= 12 => "Q3",
                        _               => "Q4"
                    };
                    var fyYear = challanDate.Month >= 4 ? challanDate.Year : challanDate.Year - 1;
                    fy = $"{fyYear}-{(fyYear + 1) % 100:D2}";

                    // Upsert — natural key: bsr_code + challan_no + challan_date + deductor_id
                    using var ups = conn.CreateCommand();
                    ups.CommandText = @"INSERT INTO challans
                        (challan_no,challan_date,deductor_id,bsr_code,section,tds_amount,
                         surcharge,cess,interest,late_fee,total_amount,bank_name,ack_no,
                         quarter,financial_year,status,remarks)
                        VALUES (@cn,@cd,@di,@b,@s,@ta,@su,@ce,@in,@lf,@tt,@bn,@an,@q,@fy,'Paid',@rm)
                        ON CONFLICT(bsr_code,challan_no,challan_date,deductor_id) DO UPDATE SET
                        tds_amount=excluded.tds_amount, surcharge=excluded.surcharge,
                        cess=excluded.cess, interest=excluded.interest, late_fee=excluded.late_fee,
                        total_amount=excluded.total_amount, bank_name=excluded.bank_name,
                        ack_no=excluded.ack_no, section=excluded.section,
                        quarter=excluded.quarter, remarks=excluded.remarks";
                    ups.Parameters.AddWithValue("@cn", challanNo); ups.Parameters.AddWithValue("@cd", challanDate.ToString("yyyy-MM-dd"));
                    ups.Parameters.AddWithValue("@di", deductorId);ups.Parameters.AddWithValue("@b",  bsr);
                    ups.Parameters.AddWithValue("@s",  section);   ups.Parameters.AddWithValue("@ta", tdsAmt);
                    ups.Parameters.AddWithValue("@su", surcharge);  ups.Parameters.AddWithValue("@ce", cess);
                    ups.Parameters.AddWithValue("@in", interest);   ups.Parameters.AddWithValue("@lf", lateFee);
                    ups.Parameters.AddWithValue("@tt", total);      ups.Parameters.AddWithValue("@bn", bankName);
                    ups.Parameters.AddWithValue("@an", ackNo);      ups.Parameters.AddWithValue("@q",  quarter);
                    ups.Parameters.AddWithValue("@fy", fy);         ups.Parameters.AddWithValue("@rm", remarks);
                    ups.ExecuteNonQuery();
                    result.SuccessCount++;
                }
                catch (Exception ex) { result.Errors.Add($"Row {row}: {ex.Message}"); result.FailCount++; }
            }

            Database.LogAction("system", "IMPORT_EXCEL", "Challan",
                $"{result.SuccessCount} new, {result.UpdatedCount} updated, {result.FailCount} failed");
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Challan Import Template
        // IMPORT — Employee Master from Excel
        // ══════════════════════════════════════════════════════════════════════
        public static ImportResult ImportEmployees(string path, int deductorId)
        {
            var result = new ImportResult();
            if (deductorId <= 0)
            {
                result.Errors.Add("No company selected. Please select a company before importing employees.");
                return result;
            }
            using var wb = new XLWorkbook(path);
            var ws = wb.Worksheets.FirstOrDefault(w =>
                w.Name.Contains("Employee", StringComparison.OrdinalIgnoreCase)) ??
                wb.Worksheets.First();

            if (ws.LastRowUsed() == null) { result.Errors.Add("Worksheet is empty."); return result; }

            var hdrErr = ValidateHeaders(ws, new[] { "name", "pan" });
            if (hdrErr != null) { result.Errors.Add(hdrErr); return result; }

            BackupBeforeImport();

            int lastRow  = ws.LastRowUsed()!.RowNumber();
            int startRow = FindHeaderRow(ws) + 1;
            if (startRow < 2) startRow = 2;

            using var conn = Database.GetConnection();

            int skippedEmpty = 0;
            for (int row = startRow; row <= lastRow; row++)
            {
                var name = ws.Cell(row, 1).GetString().Trim();
                var pan  = ws.Cell(row, 2).GetString().Trim().ToUpper();
                if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(pan)) { skippedEmpty++; continue; }
                // Skip any obvious instruction/legend row (merged note row in template)
                if (name.StartsWith("↑") || name.Contains("required", StringComparison.OrdinalIgnoreCase)) continue;

                try
                {
                    if (!Validators.IsValidPan(pan))
                    { result.Errors.Add($"Row {row}: Invalid PAN '{pan}' for '{name}'"); result.FailCount++; continue; }

                    var code        = ws.Cell(row, 3).GetString().Trim();
                    var designation = ws.Cell(row, 4).GetString().Trim();
                    var department  = ws.Cell(row, 5).GetString().Trim();
                    var joinDate    = ws.Cell(row, 6).GetString().Trim();
                    var dob         = ws.Cell(row, 7).GetString().Trim();
                    var sex         = ws.Cell(row, 8).GetString().Trim();
                    if (sex != "Male" && sex != "Female" && sex != "Other") sex = "Male";
                    var email       = ws.Cell(row, 9).GetString().Trim();
                    var phone       = ws.Cell(row, 10).GetString().Trim();
                    var regime      = ws.Cell(row, 11).GetString().Trim();
                    if (regime != "Old") regime = "New";
                    double.TryParse(ws.Cell(row, 12).GetString(), out var basic);
                    double.TryParse(ws.Cell(row, 13).GetString(), out var hra);
                    double.TryParse(ws.Cell(row, 14).GetString(), out var da);
                    double.TryParse(ws.Cell(row, 15).GetString(), out var special);
                    double.TryParse(ws.Cell(row, 16).GetString(), out var other);
                    var pfAppl = !ws.Cell(row, 17).GetString().Trim().Equals("No", StringComparison.OrdinalIgnoreCase);
                    var esiAppl= ws.Cell(row, 18).GetString().Trim().Equals("Yes", StringComparison.OrdinalIgnoreCase);

                    // Check existing by PAN + deductor
                    using var chk = conn.CreateCommand();
                    chk.CommandText = "SELECT id FROM employees WHERE pan=@p AND deductor_id=@di";
                    chk.Parameters.AddWithValue("@p",  pan);
                    chk.Parameters.AddWithValue("@di", deductorId);
                    var existingId = chk.ExecuteScalar();

                    if (existingId != null)
                    {
                        using var upd = conn.CreateCommand();
                        upd.CommandText = @"UPDATE employees SET name=@n,employee_code=@ec,designation=@dg,
                            department=@dp,join_date=@jd,date_of_birth=@dob,sex=@sx,
                            email=@em,phone=@ph,tax_regime=@tr,is_active=1 WHERE id=@id";
                        upd.Parameters.AddWithValue("@n",  name);   upd.Parameters.AddWithValue("@ec", code);
                        upd.Parameters.AddWithValue("@dg", designation); upd.Parameters.AddWithValue("@dp", department);
                        upd.Parameters.AddWithValue("@jd", joinDate);    upd.Parameters.AddWithValue("@dob", dob);
                        upd.Parameters.AddWithValue("@sx", sex);    upd.Parameters.AddWithValue("@em", email);
                        upd.Parameters.AddWithValue("@ph", phone);  upd.Parameters.AddWithValue("@tr", regime);
                        upd.Parameters.AddWithValue("@id", existingId);
                        upd.ExecuteNonQuery();

                        // Update salary structure if amounts given
                        if (basic > 0)
                        {
                            using var su = conn.CreateCommand();
                            su.CommandText = @"INSERT INTO salary_structures (employee_id,basic,hra,da,special_allowance,other_allowance,pf_applicable,esi_applicable,effective_from)
                                VALUES (@ei,@b,@h,@d,@sp,@oa,@pf,@es,@ef)
                                ON CONFLICT(employee_id) DO UPDATE SET basic=excluded.basic,hra=excluded.hra,
                                da=excluded.da,special_allowance=excluded.special_allowance,
                                other_allowance=excluded.other_allowance,pf_applicable=excluded.pf_applicable,
                                esi_applicable=excluded.esi_applicable";
                            su.Parameters.AddWithValue("@ei", existingId); su.Parameters.AddWithValue("@b",  basic);
                            su.Parameters.AddWithValue("@h",  hra);        su.Parameters.AddWithValue("@d",  da);
                            su.Parameters.AddWithValue("@sp", special);    su.Parameters.AddWithValue("@oa", other);
                            su.Parameters.AddWithValue("@pf", pfAppl?1:0); su.Parameters.AddWithValue("@es", esiAppl?1:0);
                            su.Parameters.AddWithValue("@ef", DateTime.Today.ToString("yyyy-MM-dd"));
                            su.ExecuteNonQuery();
                        }
                        result.UpdatedCount++;
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(code))
                        {
                            using var cntCmd = conn.CreateCommand();
                            cntCmd.CommandText = "SELECT COALESCE(MAX(id),0)+1 FROM employees";
                            code = $"EMP{(long)(cntCmd.ExecuteScalar()??1L):D5}";
                        }
                        using var ins = conn.CreateCommand();
                        ins.CommandText = @"INSERT INTO employees
                            (deductor_id,name,pan,employee_code,designation,department,
                             join_date,date_of_birth,sex,email,phone,tax_regime,is_active)
                            VALUES (@di,@n,@p,@ec,@dg,@dp,@jd,@dob,@sx,@em,@ph,@tr,1)";
                        ins.Parameters.AddWithValue("@di", deductorId); ins.Parameters.AddWithValue("@n",  name);
                        ins.Parameters.AddWithValue("@p",  pan);        ins.Parameters.AddWithValue("@ec", code);
                        ins.Parameters.AddWithValue("@dg", designation);ins.Parameters.AddWithValue("@dp", department);
                        ins.Parameters.AddWithValue("@jd", joinDate);   ins.Parameters.AddWithValue("@dob",dob);
                        ins.Parameters.AddWithValue("@sx", sex);        ins.Parameters.AddWithValue("@em", email);
                        ins.Parameters.AddWithValue("@ph", phone);      ins.Parameters.AddWithValue("@tr", regime);
                        ins.ExecuteNonQuery();

                        if (basic > 0)
                        {
                            using var newId = conn.CreateCommand();
                            newId.CommandText = "SELECT id FROM employees WHERE pan=@p AND deductor_id=@di";
                            newId.Parameters.AddWithValue("@p", pan); newId.Parameters.AddWithValue("@di", deductorId);
                            var empId = newId.ExecuteScalar();
                            if (empId != null)
                            {
                                using var su = conn.CreateCommand();
                                su.CommandText = @"INSERT OR IGNORE INTO salary_structures
                                    (employee_id,basic,hra,da,special_allowance,other_allowance,pf_applicable,esi_applicable,effective_from)
                                    VALUES (@ei,@b,@h,@d,@sp,@oa,@pf,@es,@ef)";
                                su.Parameters.AddWithValue("@ei", empId); su.Parameters.AddWithValue("@b",  basic);
                                su.Parameters.AddWithValue("@h",  hra);   su.Parameters.AddWithValue("@d",  da);
                                su.Parameters.AddWithValue("@sp", special);su.Parameters.AddWithValue("@oa", other);
                                su.Parameters.AddWithValue("@pf", pfAppl?1:0); su.Parameters.AddWithValue("@es", esiAppl?1:0);
                                su.Parameters.AddWithValue("@ef", DateTime.Today.ToString("yyyy-MM-dd"));
                                su.ExecuteNonQuery();
                            }
                        }
                        result.SuccessCount++;
                    }
                }
                catch (Exception ex) { result.Errors.Add($"Row {row}: {ex.Message}"); result.FailCount++; }
            }

            Database.LogAction("system", "IMPORT_EXCEL", "Employee",
                $"{result.SuccessCount} new, {result.UpdatedCount} updated, {result.FailCount} failed");
            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Employee Master to Excel
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportEmployees(List<TDSPro.DAL.Models.Employee> employees, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Employees");

            var headers = new[]
            {
                "Name*", "PAN*", "Employee Code", "Designation", "Department",
                "Join Date\n(dd-MM-yyyy)", "Date of Birth\n(dd-MM-yyyy)", "Sex\n(Male/Female/Other)",
                "Email", "Phone", "Tax Regime\n(New/Old)",
                "Basic Salary", "HRA", "DA", "Special Allowance", "Other Allowance",
                "PF Applicable\n(Yes/No)", "ESI Applicable\n(Yes/No)"
            };
            StyleHeaderRow(ws, 1, headers.Length);
            for (int c = 0; c < headers.Length; c++) ws.Cell(1, c+1).Value = headers[c];
            ws.Row(1).Height = 36;

            int row = 2;
            foreach (var e in employees)
            {
                ws.Cell(row, 1).Value  = e.Name;
                ws.Cell(row, 2).Value  = e.Pan;
                ws.Cell(row, 3).Value  = e.EmployeeCode;
                ws.Cell(row, 4).Value  = e.Designation;
                ws.Cell(row, 5).Value  = e.Department;
                ws.Cell(row, 6).Value  = e.JoinDate;
                ws.Cell(row, 7).Value  = e.DateOfBirth;
                ws.Cell(row, 8).Value  = e.Sex;
                ws.Cell(row, 9).Value  = e.Email;
                ws.Cell(row, 10).Value = e.Phone;
                ws.Cell(row, 11).Value = e.TaxRegime;
                ws.Cell(row, 12).Value = e.Salary?.Basic ?? 0;
                ws.Cell(row, 13).Value = e.Salary?.Hra ?? 0;
                ws.Cell(row, 14).Value = e.Salary?.Da ?? 0;
                ws.Cell(row, 15).Value = e.Salary?.SpecialAllowance ?? 0;
                ws.Cell(row, 16).Value = e.Salary?.ComponentsReceived() ?? 0;   // = Other Allowance (sum of Components)
                ws.Cell(row, 17).Value = (e.Salary?.PfApplicable ?? true) ? "Yes" : "No";
                ws.Cell(row, 18).Value = (e.Salary?.EsiApplicable ?? false) ? "Yes" : "No";
                if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = AltRowBg;
                row++;
            }

            ws.SheetView.FreezeRows(1);
            ws.Columns().AdjustToContents(10, 30);
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // YEAR-END DATA TRANSFER
        // Copies deductee master + employees to new FY; resets entry/challan data.
        // ══════════════════════════════════════════════════════════════════════
        public static YearEndResult TransferYearEnd(int deductorId, string fromFY, string toFY)
        {
            var result = new YearEndResult { FromFY = fromFY, ToFY = toFY };
            using var conn = Database.GetConnection();
            using var tx   = conn.BeginTransaction();

            try
            {
                // 1. Deductee master — shared across FY, nothing to copy; already global.
                // Just count them for the report.
                using var dc = conn.CreateCommand();
                dc.CommandText = "SELECT COUNT(*) FROM deductees";
                result.DeducteesCarried = (int)(long)(dc.ExecuteScalar() ?? 0L);

                // 2. Employees — update tax_regime reset to New (user can override per employee)
                // Employees are already deductor-scoped, no FY column — they carry forward automatically.
                using var ec = conn.CreateCommand();
                ec.CommandText = "SELECT COUNT(*) FROM employees WHERE deductor_id=@di AND is_active=1";
                ec.Parameters.AddWithValue("@di", deductorId);
                result.EmployeesCarried = (int)(long)(ec.ExecuteScalar() ?? 0L);

                // 3. Clear tax declarations for the new FY (fresh declarations needed)
                using var td = conn.CreateCommand();
                td.CommandText = "DELETE FROM tax_declarations WHERE financial_year=@fy";
                td.Parameters.AddWithValue("@fy", toFY);
                td.ExecuteNonQuery();

                // 4. Update deductor's active financial year
                using var ufyd = conn.CreateCommand();
                ufyd.CommandText = "UPDATE deductors SET financial_year=@fy WHERE id=@di";
                ufyd.Parameters.AddWithValue("@fy", toFY);
                ufyd.Parameters.AddWithValue("@di", deductorId);
                ufyd.ExecuteNonQuery();

                // 5. Count prior year data for summary
                using var entC = conn.CreateCommand();
                entC.CommandText = "SELECT COUNT(*) FROM tds_entries WHERE deductor_id=@di AND financial_year=@fy";
                entC.Parameters.AddWithValue("@di", deductorId); entC.Parameters.AddWithValue("@fy", fromFY);
                result.EntriesArchived = (int)(long)(entC.ExecuteScalar() ?? 0L);

                using var chalC = conn.CreateCommand();
                chalC.CommandText = "SELECT COUNT(*) FROM challans WHERE deductor_id=@di AND financial_year=@fy";
                chalC.Parameters.AddWithValue("@di", deductorId); chalC.Parameters.AddWithValue("@fy", fromFY);
                result.ChallansArchived = (int)(long)(chalC.ExecuteScalar() ?? 0L);

                tx.Commit();
                result.Success = true;
                result.Message = $"Year-end transfer complete. Active FY set to {toFY}. " +
                    $"{result.DeducteesCarried} deductees and {result.EmployeesCarried} employees carried forward. " +
                    $"{result.EntriesArchived} entries and {result.ChallansArchived} challans from {fromFY} archived (not deleted — accessible via FY filter).";

                Database.LogAction("system", "YEAR_END", "Transfer",
                    $"From {fromFY} → {toFY}: {result.DeducteesCarried} deductees, {result.EmployeesCarried} employees");
            }
            catch (Exception ex)
            {
                tx.Rollback();
                result.Success  = false;
                result.Message  = $"Year-end transfer failed: {ex.Message}";
            }

            return result;
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Import templates (extended with Challans + Employees)
        // ══════════════════════════════════════════════════════════════════════
        // ══════════════════════════════════════════════════════════════════════
        // Helpers
        // ══════════════════════════════════════════════════════════════════════
        private static void StyleHeaderRow(IXLWorksheet ws, int row, int cols,
            XLColor? bg = null)
        {
            var bgColor = bg ?? HeaderBg;
            var range   = ws.Range(row, 1, row, cols);
            range.Style.Font.Bold            = true;
            range.Style.Font.FontColor       = XLColor.White;
            range.Style.Fill.BackgroundColor = bgColor;
            range.Style.Alignment.WrapText   = true;
            range.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
            range.Style.Border.OutsideBorderColor = XLColor.White;
        }

        private static int FindHeaderRow(IXLWorksheet ws)
        {
            for (int row = 1; row <= Math.Min(5, ws.LastRowUsed()?.RowNumber() ?? 1); row++)
            {
                var cell = ws.Cell(row, 1).GetString().ToLower();
                if (cell.Contains("date") || cell.Contains("entry") ||
                    cell.Contains("name") || cell.Contains("tan") || cell.Contains("pan"))
                    return row;
            }
            return 1;
        }

        // Returns an error string if required columns are missing, null if OK
        private static string? ValidateHeaders(IXLWorksheet ws, string[] requiredKeywords)
        {
            int hdrRow = FindHeaderRow(ws);
            int lastCol = ws.LastColumnUsed()?.ColumnNumber() ?? 0;
            var headers = new List<string>();
            for (int c = 1; c <= lastCol; c++)
                headers.Add(ws.Cell(hdrRow, c).GetString().ToLower());

            var missing = requiredKeywords
                .Where(kw => !headers.Any(h => h.Contains(kw)))
                .ToList();

            return missing.Count == 0 ? null
                : $"File is missing required columns: {string.Join(", ", missing)}. Please use the template from Import > Templates tab.";
        }

        // Pre-import backup (silent, never blocks import)
        private static void BackupBeforeImport()
        {
            try { FolderManager.BackupNow(Database.DbPath); } catch { }
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Quarter Summary Report
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportQuarterSummary(List<TDSPro.DAL.Models.QuarterSummary> data, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Quarter Summary");

            ws.Cell(1,1).Value = "Quarter-wise TDS Summary";
            ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;

            string[] hdrs = { "Quarter","Entries","Gross Amount","TDS","Surcharge","Cess","Interest","Total TDS","Paid","Pending" };
            for (int c = 0; c < hdrs.Length; c++)
            {
                var cell = ws.Cell(3, c+1);
                cell.Value = hdrs[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(23,52,140);
                cell.Style.Font.FontColor = XLColor.White; cell.Style.Font.Bold = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            int row = 4;
            foreach (var q in data)
            {
                ws.Cell(row,1).Value = q.Quarter;
                ws.Cell(row,2).Value = q.Entries;
                ws.Cell(row,3).Value = q.GrossAmount;
                ws.Cell(row,4).Value = q.TdsAmount;
                ws.Cell(row,5).Value = q.Surcharge;
                ws.Cell(row,6).Value = q.Cess;
                ws.Cell(row,7).Value = q.Interest;
                ws.Cell(row,8).Value = q.TotalTds;
                ws.Cell(row,9).Value = q.PaidCount;
                ws.Cell(row,10).Value = q.PendingCount;
                foreach (int c in new[]{3,4,5,6,7,8}) ws.Cell(row,c).Style.NumberFormat.Format = "#,##0";
                if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(249,251,253);
                row++;
            }
            // Totals
            ws.Cell(row,1).Value = "TOTAL"; ws.Cell(row,1).Style.Font.Bold = true;
            ws.Cell(row,2).Value = data.Sum(q => q.Entries);
            ws.Cell(row,3).Value = data.Sum(q => q.GrossAmount);
            ws.Cell(row,4).Value = data.Sum(q => q.TdsAmount);
            ws.Cell(row,5).Value = data.Sum(q => q.Surcharge);
            ws.Cell(row,6).Value = data.Sum(q => q.Cess);
            ws.Cell(row,7).Value = data.Sum(q => q.Interest);
            ws.Cell(row,8).Value = data.Sum(q => q.TotalTds);
            ws.Cell(row,9).Value = data.Sum(q => q.PaidCount);
            ws.Cell(row,10).Value = data.Sum(q => q.PendingCount);
            ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(214,228,240);
            ws.Row(row).Style.Font.Bold = true;
            foreach (int c in new[]{3,4,5,6,7,8}) ws.Cell(row,c).Style.NumberFormat.Format = "#,##0";

            ws.SheetView.FreezeRows(3); ws.Columns().AdjustToContents(8, 40);
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Deductee Report
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportDeducteeReport(List<TDSPro.DAL.Models.DeducteeReport> data, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Deductee Report");

            ws.Cell(1,1).Value = "Deductee-wise TDS Report";
            ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;

            string[] hdrs = { "Name","PAN","Type","Section(s)","Entries","Gross Amount","TDS","Interest","Total TDS","Paid","Pending" };
            for (int c = 0; c < hdrs.Length; c++)
            {
                var cell = ws.Cell(3, c+1);
                cell.Value = hdrs[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(23,52,140);
                cell.Style.Font.FontColor = XLColor.White; cell.Style.Font.Bold = true;
            }

            int row = 4;
            foreach (var r in data)
            {
                ws.Cell(row,1).Value = r.Name;
                ws.Cell(row,2).Value = r.Pan;
                ws.Cell(row,3).Value = r.DeducteeType;
                ws.Cell(row,4).Value = r.Section;
                ws.Cell(row,5).Value = r.Entries;
                ws.Cell(row,6).Value = r.GrossAmount;
                ws.Cell(row,7).Value = r.TdsAmount;
                ws.Cell(row,8).Value = r.Interest;
                ws.Cell(row,9).Value = r.TotalTds;
                ws.Cell(row,10).Value = r.PaidCount;
                ws.Cell(row,11).Value = r.PendingCount;
                foreach (int c in new[]{6,7,8,9}) ws.Cell(row,c).Style.NumberFormat.Format = "#,##0";
                if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(249,251,253);
                row++;
            }
            ws.Cell(row,1).Value = "TOTAL"; ws.Cell(row,1).Style.Font.Bold = true;
            ws.Cell(row,5).Value = data.Sum(r => r.Entries);
            ws.Cell(row,6).Value = data.Sum(r => r.GrossAmount);
            ws.Cell(row,7).Value = data.Sum(r => r.TdsAmount);
            ws.Cell(row,8).Value = data.Sum(r => r.Interest);
            ws.Cell(row,9).Value = data.Sum(r => r.TotalTds);
            ws.Cell(row,10).Value = data.Sum(r => r.PaidCount);
            ws.Cell(row,11).Value = data.Sum(r => r.PendingCount);
            ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(214,228,240);
            ws.Row(row).Style.Font.Bold = true;
            foreach (int c in new[]{6,7,8,9}) ws.Cell(row,c).Style.NumberFormat.Format = "#,##0";

            ws.SheetView.FreezeRows(3); ws.Columns().AdjustToContents(8, 40);
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Section Report
        // ══════════════════════════════════════════════════════════════════════
        public static void ExportSectionReport(List<TDSPro.DAL.Models.SectionReport> data, string path)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Section Report");

            ws.Cell(1,1).Value = "Section-wise TDS Report";
            ws.Cell(1,1).Style.Font.Bold = true; ws.Cell(1,1).Style.Font.FontSize = 13;

            string[] hdrs = { "Section","Nature of Payment","Entries","Gross Amount","TDS","Surcharge","Cess","Interest","Total TDS" };
            for (int c = 0; c < hdrs.Length; c++)
            {
                var cell = ws.Cell(3, c+1);
                cell.Value = hdrs[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(23,52,140);
                cell.Style.Font.FontColor = XLColor.White; cell.Style.Font.Bold = true;
            }

            int row = 4;
            foreach (var r in data)
            {
                ws.Cell(row,1).Value = r.Section;
                ws.Cell(row,2).Value = r.Description;
                ws.Cell(row,3).Value = r.Entries;
                ws.Cell(row,4).Value = r.GrossAmount;
                ws.Cell(row,5).Value = r.TdsAmount;
                ws.Cell(row,6).Value = r.Surcharge;
                ws.Cell(row,7).Value = r.Cess;
                ws.Cell(row,8).Value = r.Interest;
                ws.Cell(row,9).Value = r.TotalTds;
                foreach (int c in new[]{4,5,6,7,8,9}) ws.Cell(row,c).Style.NumberFormat.Format = "#,##0";
                if (row % 2 == 0) ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(249,251,253);
                row++;
            }
            ws.Cell(row,1).Value = "TOTAL"; ws.Cell(row,1).Style.Font.Bold = true;
            ws.Cell(row,3).Value = data.Sum(r => r.Entries);
            ws.Cell(row,4).Value = data.Sum(r => r.GrossAmount);
            ws.Cell(row,5).Value = data.Sum(r => r.TdsAmount);
            ws.Cell(row,6).Value = data.Sum(r => r.Surcharge);
            ws.Cell(row,7).Value = data.Sum(r => r.Cess);
            ws.Cell(row,8).Value = data.Sum(r => r.Interest);
            ws.Cell(row,9).Value = data.Sum(r => r.TotalTds);
            ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(214,228,240);
            ws.Row(row).Style.Font.Bold = true;
            foreach (int c in new[]{4,5,6,7,8,9}) ws.Cell(row,c).Style.NumberFormat.Format = "#,##0";

            ws.SheetView.FreezeRows(3); ws.Columns().AdjustToContents(8, 40);
            wb.SaveAs(path);
        }

        // ══════════════════════════════════════════════════════════════════════
        // EXPORT — Full Year Salary Summary
        // ══════════════════════════════════════════════════════════════════════
        public static string ExportYearSummary(
            List<TDSPro.DAL.Models.EmployeeYearSummary> summary,
            string fy,
            string[] monthNames,
            int[]    monthNums,
            string?  outputPath = null)
        {
            using var wb = new XLWorkbook();
            var ws = wb.Worksheets.Add("Year Summary");

            // ── Title row ─────────────────────────────────────────────────────
            ws.Cell(1,1).Value = $"Full Year Salary & TDS Summary — FY {fy}";
            ws.Cell(1,1).Style.Font.Bold = true;
            ws.Cell(1,1).Style.Font.FontSize = 13;
            ws.Cell(1,1).Style.Font.FontColor = XLColor.FromArgb(23,52,140);

            // ── Header row ────────────────────────────────────────────────────
            int hRow = 3;
            var headers = new List<string> { "Code", "Employee", "PAN" };
            headers.AddRange(monthNames);
            headers.AddRange(new[]{ "Months Run","Total Gross","Total TDS","Total PF","Total Net" });

            for (int c = 0; c < headers.Count; c++)
            {
                var cell = ws.Cell(hRow, c + 1);
                cell.Value = headers[c];
                cell.Style.Fill.BackgroundColor = XLColor.FromArgb(23,52,140);
                cell.Style.Font.FontColor       = XLColor.White;
                cell.Style.Font.Bold            = true;
                cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // ── Data rows ─────────────────────────────────────────────────────
            int row = hRow + 1;
            double grandGross=0, grandTds=0, grandPf=0, grandNet=0;

            foreach (var emp in summary)
            {
                ws.Cell(row, 1).Value = emp.EmployeeCode;
                ws.Cell(row, 2).Value = emp.EmployeeName;
                ws.Cell(row, 3).Value = emp.Pan;

                for (int mi = 0; mi < 12; mi++)
                {
                    int col = 4 + mi;
                    if (emp.MonthlyRuns.TryGetValue(monthNums[mi], out var run))
                    {
                        ws.Cell(row, col).Value = run.GrossSalary;
                        ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                        ws.Cell(row, col).Style.Font.FontColor = XLColor.FromArgb(22,101,52);
                        if (run.ProRataDays > 0)
                            ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromArgb(255,251,235);
                    }
                    else
                    {
                        ws.Cell(row, col).Value = "-";
                        ws.Cell(row, col).Style.Font.FontColor = XLColor.LightGray;
                    }
                }

                int sc = 16;
                ws.Cell(row, sc).Value   = $"{emp.MonthsRun}/12";
                ws.Cell(row, sc+1).Value = emp.TotalGross;
                ws.Cell(row, sc+2).Value = emp.TotalTds;
                ws.Cell(row, sc+3).Value = emp.TotalPf;
                ws.Cell(row, sc+4).Value = emp.TotalNet;

                foreach (int c in new[]{sc+1, sc+2, sc+3, sc+4})
                    ws.Cell(row, c).Style.NumberFormat.Format = "#,##0";

                ws.Cell(row, sc+1).Style.Font.Bold = true;
                ws.Cell(row, sc+2).Style.Font.FontColor = XLColor.FromArgb(29,78,216);
                ws.Cell(row, sc+4).Style.Font.FontColor = XLColor.FromArgb(5,150,105);

                // Alternate row shading
                if (row % 2 == 0)
                    ws.Row(row).Style.Fill.BackgroundColor = XLColor.FromArgb(249,251,253);

                grandGross += emp.TotalGross;
                grandTds   += emp.TotalTds;
                grandPf    += emp.TotalPf;
                grandNet   += emp.TotalNet;
                row++;
            }

            // ── Totals row ────────────────────────────────────────────────────
            ws.Cell(row, 2).Value = "TOTAL";
            ws.Cell(row, 2).Style.Font.Bold = true;
            int tsc = 16;
            foreach (var (col, val) in new[]{ (tsc+1, grandGross),(tsc+2, grandTds),(tsc+3, grandPf),(tsc+4, grandNet) })
            {
                ws.Cell(row, col).Value = val;
                ws.Cell(row, col).Style.Font.Bold = true;
                ws.Cell(row, col).Style.NumberFormat.Format = "#,##0";
                ws.Cell(row, col).Style.Fill.BackgroundColor = XLColor.FromArgb(219,234,254);
            }

            // ── Freeze + autofit ──────────────────────────────────────────────
            ws.SheetView.FreezeRows(hRow);
            ws.SheetView.FreezeColumns(3);
            ws.Columns().AdjustToContents(8, 40);
            ws.Column(2).Width = 25;

            wb.Properties.Author  = "TDS Pro";
            wb.Properties.Subject = $"Year Summary {fy}";

            string path;
            if (!string.IsNullOrEmpty(outputPath))
            {
                path = outputPath;
            }
            else
            {
                string dir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "TDSPro", fy.Replace("/","-"), "Reports");
                System.IO.Directory.CreateDirectory(dir);
                path = System.IO.Path.Combine(dir, $"YearSummary_{fy.Replace("/","-")}_{DateTime.Today:yyyyMMdd}.xlsx");
            }
            wb.SaveAs(path);
            return path;
        }
    }

    // ── Import result ─────────────────────────────────────────────────────────
    public class ImportResult
    {
        public int SuccessCount  { get; set; }
        public int UpdatedCount  { get; set; }
        public int FailCount     { get; set; }
        public List<string> Errors          { get; set; } = new();
        public List<string> ImportedEntries { get; set; } = new();
        public bool HasErrors => Errors.Count > 0;
        public string Summary =>
            $"{SuccessCount} imported, {UpdatedCount} updated, {FailCount} failed.";
    }

    // ── Year-end transfer result ───────────────────────────────────────────────
    public class YearEndResult
    {
        public bool   Success           { get; set; }
        public string Message           { get; set; } = "";
        public string FromFY            { get; set; } = "";
        public string ToFY              { get; set; } = "";
        public int    DeducteesCarried  { get; set; }
        public int    EmployeesCarried  { get; set; }
        public int    EntriesArchived   { get; set; }
        public int    ChallansArchived  { get; set; }
    }
}
