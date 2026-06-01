using CDeTDS.DAL.Models;

namespace CDeTDS.DAL
{
    /// <summary>
    /// NIL Return — NSDL FVU-compatible .txt for quarters with no TDS transactions.
    /// Per CBDT Circular: mandatory even when no tax is deducted. Penalty u/s 272A(2).
    /// </summary>
    public static class NilReturnHelper
    {
        public static string GenerateNilReturn(
            Deductor deductor, string fy, string quarter, string formType = "26Q")
        {
            string nsdlForm = formType switch { "138"=>"24Q","140"=>"26Q",_=>formType };
            string fvuVer   = "9.4";   // current target; IT Act 2025 reuses same FVU until NSDL publishes successor
            string ay       = AssessmentYear(fy);
            string today    = System.DateTime.Today.ToString("dd/MM/yyyy");
            string tan      = (deductor.Tan ?? "").ToUpper();
            string pan      = (deductor.Pan ?? "").ToUpper();
            string dedCat   = "C";   // Deductor.CompanyType not in base model — default Company
            string name     = Safe(deductor.CompanyName, 75);
            string addr     = Safe(deductor.Address, 255);
            string pin      = ((deductor.Pincode ?? "") + "      ")[..6];
            string contact  = Safe(deductor.ContactPerson, 75);
            string qCode    = quarter.TrimStart('Q');

            var lines = new System.Collections.Generic.List<string>
            {
                $"FH|T|{nsdlForm}|{fvuVer}|1|1",
                $"BH|{tan}|{ay}|{nsdlForm}|{qCode}|{pan}|{dedCat}|{today}|R|0|0|0|0",
                $"DE|{tan}|{pan}|{name}|{addr}|{pin}|||{contact}|{pan}|{contact}|DIRECTOR|{today}",
                "BC|0|0|0|0|0|0|0|0|0",
                "FC|1|0|0|0|0",
            };
            return string.Join("\r\n", lines) + "\r\n";
        }

        public static (bool Ok, string Path, string Error) SaveNilReturn(
            Deductor deductor, string fy, string quarter, string formType = "26Q")
        {
            try
            {
                var content  = GenerateNilReturn(deductor, fy, quarter, formType);
                var outDir   = System.IO.Path.Combine(
                    System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyDocuments),
                    "CDeTDS", "Returns", fy, quarter, "NIL");
                System.IO.Directory.CreateDirectory(outDir);
                var fyShort  = fy.Replace("-", "");
                var path     = System.IO.Path.Combine(outDir,
                    $"{formType}_NIL_{deductor.Tan}_{fyShort}_{quarter}.txt");
                System.IO.File.WriteAllText(path, content, System.Text.Encoding.ASCII);
                Database.LogAction("system", "NIL_RETURN", "Return",
                    $"{formType} NIL {quarter} {fy} — {deductor.Tan}");
                return (true, path, "");
            }
            catch (Exception ex) { return (false, "", ex.Message); }
        }

        private static string AssessmentYear(string fy)
        {
            var p = fy.Split('-');
            return p.Length == 2 && int.TryParse(p[0], out int y)
                ? $"{y+1}{(y+2).ToString()[^2..]}" : fy.Replace("-","");
        }

        private static string Safe(string? s, int max)
        {
            s = (s ?? "").Trim().ToUpper().Replace("|","");
            return s.Length > max ? s[..max] : s;
        }
    }
}
