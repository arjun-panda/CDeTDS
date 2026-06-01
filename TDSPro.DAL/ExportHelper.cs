using System.Text;
using CDeTDS.DAL.Models;

namespace CDeTDS.DAL
{
    public static class ExportHelper
    {
        // ── CSV export for TDS entries ─────────────────────────────────────────
        public static string EntresToCsv(List<TdsEntry> entries)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Entry No,Date,Deductor,Deductee,PAN,Section,Amount,Rate%," +
                          "TDS Amount,Surcharge,Cess,Total TDS,Interest,Late Fee," +
                          "Due Date,Payment Date,Challan No,Status,FY,Quarter,Remarks");
            foreach (var e in entries)
            {
                sb.AppendLine(string.Join(",",
                    Q(e.EntryNo),
                    Q(e.EntryDate.ToString("dd-MM-yyyy")),
                    Q(e.DeductorName),
                    Q(e.DeducteeName),
                    Q(e.DeducteePan),
                    Q(e.Section),
                    e.Amount.ToString("F2"),
                    e.Rate.ToString("F2"),
                    e.TdsAmount.ToString("F2"),
                    e.Surcharge.ToString("F2"),
                    e.Cess.ToString("F2"),
                    e.TotalTds.ToString("F2"),
                    e.Interest.ToString("F2"),
                    e.LateFee.ToString("F2"),
                    Q(e.DueDate?.ToString("dd-MM-yyyy") ?? ""),
                    Q(e.PaymentDate?.ToString("dd-MM-yyyy") ?? ""),
                    Q(e.ChallanNo),
                    Q(e.Status),
                    Q(e.FinancialYear),
                    Q(e.Quarter),
                    Q(e.Remarks)
                ));
            }
            return sb.ToString();
        }

        // ── CSV export for challans ────────────────────────────────────────────
        public static string ChallansToCsv(List<Challan> challans)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Challan No,Date,Deductor,BSR Code,Section,Quarter," +
                          "TDS Amount,Surcharge,Cess,Interest,Late Fee,Total Amount," +
                          "Bank Name,Ack No,Status,FY,Remarks");
            foreach (var c in challans)
            {
                sb.AppendLine(string.Join(",",
                    Q(c.ChallanNo),
                    Q(c.ChallanDate.ToString("dd-MM-yyyy")),
                    Q(c.DeductorName),
                    Q(c.BsrCode),
                    Q(c.Section),
                    Q(c.Quarter),
                    c.TdsAmount.ToString("F2"),
                    c.Surcharge.ToString("F2"),
                    c.Cess.ToString("F2"),
                    c.Interest.ToString("F2"),
                    c.LateFee.ToString("F2"),
                    c.TotalAmount.ToString("F2"),
                    Q(c.BankName),
                    Q(c.AckNo),
                    Q(c.Status),
                    Q(c.FinancialYear),
                    Q(c.Remarks)
                ));
            }
            return sb.ToString();
        }

        // ── Print-ready text report ────────────────────────────────────────────
        public static string BuildTextReport(ReturnData data)
        {
            var sb  = new StringBuilder();
            var h   = data.Header;
            var sep = new string('=', 100);
            var lin = new string('-', 100);

            sb.AppendLine(sep);
            sb.AppendLine($"  CDeTDS — {h.FormType} Return Summary");
            sb.AppendLine($"  Deductor : {h.DeductorName}  |  TAN: {h.TanOfDeductor}  |  PAN: {h.PanOfDeductor}");
            sb.AppendLine($"  FY       : {h.FinancialYear}  |  Quarter: {h.Quarter}");
            sb.AppendLine($"  Generated: {DateTime.Now:dd-MMM-yyyy HH:mm}");
            sb.AppendLine(sep);
            sb.AppendLine();

            // Challan summary
            sb.AppendLine("CHALLAN DETAILS");
            sb.AppendLine(lin);
            sb.AppendLine($"{"Sl",-4}{"BSR Code",-10}{"Date",-14}{"Challan No",-14}{"TDS",-14}{"Interest",-12}{"Total",-14}");
            sb.AppendLine(lin);
            foreach (var ch in data.Challans)
            {
                sb.AppendLine($"{ch.SlNo,-4}{ch.BsrCode,-10}{ch.ChallanDate:dd-MM-yyyy,-14}" +
                              $"{ch.ChallanNo,-14}{ch.TdsDeposited,13:N2} {ch.Interest,11:N2} {ch.TotalDeposited,13:N2}");
            }
            sb.AppendLine(lin);
            sb.AppendLine($"{"TOTAL",-38}{data.Challans.Sum(c => c.TdsDeposited),13:N2} " +
                          $"{data.Challans.Sum(c => c.Interest),11:N2} " +
                          $"{data.Challans.Sum(c => c.TotalDeposited),13:N2}");
            sb.AppendLine();

            // Deductee details
            sb.AppendLine("DEDUCTEE / TRANSACTION DETAILS");
            sb.AppendLine(lin);
            sb.AppendLine($"{"Sl",-4}{"PAN",-12}{"Name",-28}{"Section",-9}{"Amount",-14}{"TDS",-14}{"Status"}");
            sb.AppendLine(lin);
            foreach (var d in data.Deductees)
            {
                var name = d.Name.Length > 26 ? d.Name[..26] : d.Name;
                sb.AppendLine($"{d.SlNo,-4}{d.Pan,-12}{name,-28}{d.Section,-9}" +
                              $"{d.AmountPaid,13:N2} {d.TdsDeducted,13:N2}");
            }
            sb.AppendLine(lin);
            sb.AppendLine($"{"TOTAL",-53}{data.TotalAmountPaid,13:N2} {data.TotalTdsDeducted,13:N2}");
            sb.AppendLine();
            sb.AppendLine(sep);
            sb.AppendLine($"  Total Deductees : {data.TotalDeductees}");
            sb.AppendLine($"  Total Amount    : Rs {data.TotalAmountPaid:N2}");
            sb.AppendLine($"  Total TDS       : Rs {data.TotalTdsDeducted:N2}");
            sb.AppendLine(sep);

            return sb.ToString();
        }

        private static string Q(string s) => $"\"{s.Replace("\"", "\"\"")}\"";
    }
}
