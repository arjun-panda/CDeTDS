using System.Text;

namespace CDeTDS.DAL
{
    /// <summary>
    /// Renders a .fvu / validated TDS text file as a colour-coded HTML table.
    /// Each record type (FH, BH, CD, DD, SD, S16, C6A) gets its own colour band
    /// and a field-level breakdown so the user can inspect every column.
    /// </summary>
    public static class FvuFileViewer
    {
        // Field definitions per record type (1-based, matching NSDL spec)
        private static readonly Dictionary<string, string[]> FieldNames = new()
        {
            ["FH"] = new[] { "Line#","FH","FileType","R/C","Date","Seq","D","TAN","BatchCount","RPU",
                             "Hash1","Hash2","Hash3","Hash4","Hash5","Hash6","Hash7","PRN" },
            ["BH"] = new[] { "Line#","BH","BatchNo","ChallanCount","Form","TxType","UpdInd","OrigPRN","PrevPRN","PRN",
                             "PRNDate","LastTAN","TAN","Receipt","PAN","AY","FY","Quarter","DeductorName","Branch",
                             "Addr1","Addr2","Addr3","Addr4","Addr5","State","PIN","Email","STD","Phone",
                             "AddrChg","DeductorType","RespName","RespDesig","RespAddr1","RespAddr2","RespAddr3","RespAddr4","RespAddr5","RespState",
                             "RespPIN","RespEmail","RespMobile","RespSTD","RespPhone","RespAddrChg","BatchTDS","UnmatchedChln","SDCount","GrossTotalIncome",
                             "AOApproval","PrevFiled","LastDeductorType","StateName","PAOCode","F56","F57","F58","RespPAN","F60",
                             "F61","F62","F63","F64","F65","F66","F67","F68","F69","GSTIN","F71","F72" },
            ["CD"] = new[] { "Line#","CD","BatchNo","ChallanSlNo","ChallanCount","TV","BSRCode","F7","F8","ChallanSerial",
                             "F10","F11","F12","BankCode","F14","DepositDate","F16","F17","F18","TDS",
                             "Surcharge","Cess","Interest","Others","Fee","TotalDeposit","F27","TotalDeposit2","TotalDeposit3","F30",
                             "F31","TDS2","F33","F34","F35","F36","CIN","AOApproval","Rate","Section" },
            ["DD"] = new[] { "Line#","DD","BatchNo","ChallanSlNo","DDSeq","Mode","EmployeeSlNo","F8","F9","PAN",
                             "F11","F12","Name","TDS","Surcharge","Cess","TotalTDS","F18","TDSDeposited","F20",
                             "F21","AmountPaid","PaymentDate","DeductionDate","ChallanDate","F26","F27","F28","F29","F30",
                             "F31","Section","F33","F34","F35","F36","F37","F38","F39","F40",
                             "F41","F42","F43","F44","F45","F46","F47","F48","F49","F50",
                             "F51","F52","F53","F54","F55" },
            ["SD"] = new[] { "Line#","SD","BatchNo","ChallanSlNo","SDSeq","PAN","Name","EmployeeNo","F9","F10",
                             "Salary17_1","Salary17_2","Salary17_3","PerquisiteValue","ProfitInLieu","GrossSalary","GrossTotalIncome","Chapter6ATotal","Chapter6ACount","Ded80C",
                             "TaxableIncome","TaxPayable","Rebate87A","Surcharge","HEC","TotalTaxPayable","TDSPrevEmployer","TDSDeducted","TDSDeposited","F30",
                             "F31","F32","F33","F34","F35","F36","F37","F38","F39","F40" },
        };

        private static readonly Dictionary<string, string> RecordColors = new()
        {
            ["FH"]  = "#1e3a8a",
            ["BH"]  = "#0f766e",
            ["CD"]  = "#7c3aed",
            ["DD"]  = "#1d4ed8",
            ["SD"]  = "#b45309",
            ["S16"] = "#065f46",
            ["C6A"] = "#9f1239",
        };

        public static string GenerateHtml(string fvuFilePath)
        {
            var lines = File.ReadAllLines(fvuFilePath);
            var fileName = Path.GetFileName(fvuFilePath);
            var sb = new StringBuilder();

            sb.Append(@"<!DOCTYPE html><html><head><meta charset='utf-8'>
<title>FVU File — ").Append(fileName).Append(@"</title>
<style>
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:'Segoe UI',Arial,sans-serif;background:#f1f5f9;padding:16px;font-size:11px}
h1{font-size:15px;font-weight:700;color:#1e3a8a;margin-bottom:4px}
p.sub{font-size:10px;color:#64748b;margin-bottom:14px}
.record{margin-bottom:10px;border-radius:6px;overflow:hidden;box-shadow:0 1px 4px rgba(0,0,0,.1)}
.rec-hdr{display:flex;align-items:center;gap:10px;padding:6px 12px;color:#fff;font-weight:700;font-size:11px}
.rec-badge{background:rgba(255,255,255,.25);border-radius:4px;padding:1px 7px;font-size:10px}
.rec-line{color:rgba(255,255,255,.7);font-size:9px}
table{width:100%;border-collapse:collapse;background:#fff}
th{background:#f8fafc;color:#475569;font-size:9.5px;font-weight:600;padding:4px 8px;
   text-align:left;border-bottom:2px solid #e2e8f0;white-space:nowrap}
td{padding:3px 8px;border-bottom:1px solid #f1f5f9;vertical-align:top;word-break:break-all}
td.idx{color:#94a3b8;font-size:9px;width:28px;text-align:right;padding-right:6px}
td.fname{color:#64748b;font-size:9px;white-space:nowrap;width:120px}
td.fval{color:#0f172a;font-weight:500}
td.fval.empty{color:#cbd5e1;font-style:italic}
.legend{display:flex;gap:8px;flex-wrap:wrap;margin-bottom:12px}
.leg{display:inline-flex;align-items:center;gap:4px;font-size:10px;color:#374151}
.leg-dot{width:10px;height:10px;border-radius:2px}
</style></head><body>
<h1>FVU File Viewer — ").Append(fileName).Append(@"</h1>
<p class='sub'>").Append(lines.Length).Append(@" records &nbsp;·&nbsp; ").Append(fvuFilePath).Append(@"</p>
<div class='legend'>");
            foreach (var (rt, col) in RecordColors)
                sb.Append($"<span class='leg'><span class='leg-dot' style='background:{col}'></span>{rt}</span>");
            sb.Append("</div>");

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd();
                if (string.IsNullOrEmpty(line)) continue;
                var fields = line.Split('^');
                if (fields.Length < 2) continue;

                var recType = fields[1].Trim().ToUpper();
                var color   = RecordColors.TryGetValue(recType, out var c) ? c : "#374151";
                var names   = FieldNames.TryGetValue(recType, out var n) ? n : Array.Empty<string>();

                sb.Append($"<div class='record'><div class='rec-hdr' style='background:{color}'>");
                sb.Append($"<span class='rec-badge'>{System.Net.WebUtility.HtmlEncode(recType)}</span>");
                sb.Append($"<span class='rec-line'>Line {fields[0]}</span>");
                // Show key summary fields in header
                if (recType == "DD" && fields.Length > 12)
                    sb.Append($"<span style='opacity:.9'>{System.Net.WebUtility.HtmlEncode(fields[12])} &nbsp; PAN: {System.Net.WebUtility.HtmlEncode(fields[9])}</span>");
                else if (recType == "CD" && fields.Length > 15)
                    sb.Append($"<span style='opacity:.9'>BSR: {System.Net.WebUtility.HtmlEncode(fields[6])} &nbsp; Date: {System.Net.WebUtility.HtmlEncode(fields[15])}</span>");
                else if (recType == "BH" && fields.Length > 18)
                    sb.Append($"<span style='opacity:.9'>{System.Net.WebUtility.HtmlEncode(fields[18])}</span>");
                sb.Append("</div><table><tr><th>#</th><th>Field</th><th>Value</th></tr>");

                for (int i = 0; i < fields.Length; i++)
                {
                    var fname = i < names.Length ? names[i] : $"F{i + 1}";
                    var fval  = fields[i];
                    var isEmpty = string.IsNullOrEmpty(fval);
                    sb.Append($"<tr><td class='idx'>{i + 1}</td>");
                    sb.Append($"<td class='fname'>{System.Net.WebUtility.HtmlEncode(fname)}</td>");
                    sb.Append($"<td class='fval{(isEmpty ? " empty" : "")}'>{(isEmpty ? "—" : System.Net.WebUtility.HtmlEncode(fval))}</td></tr>");
                }
                sb.Append("</table></div>");
            }

            sb.Append("</body></html>");
            return sb.ToString();
        }
    }
}
