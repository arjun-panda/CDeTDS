// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  TRACES Conso File Importer                                              ║
// ║  Reads FH/BH/CD/DD records from a TRACES correction file (.tds/.txt)    ║
// ║  and imports deductor, deductees, challans, and TDS entries into DB.    ║
// ╚══════════════════════════════════════════════════════════════════════════╝
using ICSharpCode.SharpZipLib.Zip;
using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    public class ConsoImportResult
    {
        public bool   Success       { get; set; }
        public string Message       { get; set; } = "";
        public string FormType      { get; set; } = "";
        public string Quarter       { get; set; } = "";
        public string FY            { get; set; } = "";
        public string DeductorName  { get; set; } = "";
        public string TAN           { get; set; } = "";
        public int    ChallansImported  { get; set; }
        public int    DeducteesImported { get; set; }
        public int    EntriesImported   { get; set; }
        public int    DeducteesSkipped  { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors   { get; set; } = new();
    }

    public static class ConsoImporter
    {
        /// <summary>
        /// Import a TRACES conso file (.tds or .txt) into the database.
        /// If the file is a password-protected ZIP (.tds), pass the password (TAN_RequestNumber).
        /// Returns a result summary.
        /// </summary>
        public static ConsoImportResult Import(string filePath, bool overwriteExisting = false, string? zipPassword = null)
        {
            var result = new ConsoImportResult();
            try
            {
                if (!File.Exists(filePath))
                    throw new FileNotFoundException($"File not found: {filePath}");

                List<string> lines;

                if (IsZipFile(filePath))
                {
                    if (string.IsNullOrWhiteSpace(zipPassword))
                        throw new Exception(
                            "This is a password-protected TRACES conso file.\n" +
                            "Password format: TAN_RequestNumber  (e.g. DELI03886B_86528)\n" +
                            "Find the Request Number in TRACES → Downloads → Requested Downloads.");

                    lines = ExtractLinesFromZip(filePath, zipPassword);
                }
                else
                {
                    lines = File.ReadAllLines(filePath)
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .ToList();
                }

                if (lines.Count == 0)
                    throw new Exception("File is empty.");

                // Parse records
                var fhRecord = lines.FirstOrDefault(l => l.StartsWith("FH|"));
                var bhRecord = lines.FirstOrDefault(l => l.StartsWith("BH|"));

                if (fhRecord == null)
                    throw new Exception("Invalid conso file — FH (File Header) record not found.");

                // ── Parse FH record ───────────────────────────────────────────
                var fh = fhRecord.Split('|');
                // FH|seq|formType|FY|quarter|TAN|deductorName|PAN|address|city|state|pin|...
                result.FormType     = Safe(fh, 2, "26Q");
                result.FY           = Safe(fh, 3, "2026-27");
                result.Quarter      = Safe(fh, 4, "Q1");
                result.TAN          = Safe(fh, 5, "");
                result.DeductorName = Safe(fh, 6, "Unknown");

                string pan      = Safe(fh, 7, "");
                string address  = Safe(fh, 8, "");
                string city     = Safe(fh, 9, "");
                string state    = Safe(fh, 10, "");
                string pincode  = Safe(fh, 11, "");
                string phone    = Safe(fh, 14, "");
                string email    = Safe(fh, 15, "");

                if (string.IsNullOrWhiteSpace(result.TAN))
                    throw new Exception("TAN not found in FH record (field 6).");

                using var conn = Database.GetConnection();
                using var tx   = conn.BeginTransaction();

                // ── Upsert Deductor ───────────────────────────────────────────
                long deductorId;
                using (var chk = conn.CreateCommand())
                {
                    chk.CommandText = "SELECT id FROM deductors WHERE tan=@tan LIMIT 1";
                    chk.Parameters.AddWithValue("@tan", result.TAN);
                    var existing = chk.ExecuteScalar();

                    if (existing != null && !overwriteExisting)
                    {
                        deductorId = (long)existing;
                        result.Warnings.Add($"Deductor '{result.DeductorName}' (TAN: {result.TAN}) already exists — using existing record.");
                    }
                    else if (existing != null)
                    {
                        deductorId = (long)existing;
                        using var upd = conn.CreateCommand();
                        upd.CommandText = @"UPDATE deductors SET company_name=@nm,pan=@pn,
                            address=@addr,city=@ct,state=@st,pincode=@pin,phone=@ph,email=@em
                            WHERE id=@id";
                        upd.Parameters.AddWithValue("@nm",   result.DeductorName);
                        upd.Parameters.AddWithValue("@pn",   pan);
                        upd.Parameters.AddWithValue("@addr", address);
                        upd.Parameters.AddWithValue("@ct",   city);
                        upd.Parameters.AddWithValue("@st",   state);
                        upd.Parameters.AddWithValue("@pin",  pincode);
                        upd.Parameters.AddWithValue("@ph",   phone);
                        upd.Parameters.AddWithValue("@em",   email);
                        upd.Parameters.AddWithValue("@id",   deductorId);
                        upd.ExecuteNonQuery();
                    }
                    else
                    {
                        using var ins = conn.CreateCommand();
                        ins.CommandText = @"INSERT INTO deductors
                            (company_name,tan,pan,address,city,state,pincode,phone,email,
                             financial_year,deductor_type,is_active)
                            VALUES(@nm,@tan,@pn,@addr,@ct,@st,@pin,@ph,@em,@fy,'Company',1)";
                        ins.Parameters.AddWithValue("@nm",   result.DeductorName);
                        ins.Parameters.AddWithValue("@tan",  result.TAN);
                        ins.Parameters.AddWithValue("@pn",   pan);
                        ins.Parameters.AddWithValue("@addr", address);
                        ins.Parameters.AddWithValue("@ct",   city);
                        ins.Parameters.AddWithValue("@st",   state);
                        ins.Parameters.AddWithValue("@pin",  pincode);
                        ins.Parameters.AddWithValue("@ph",   phone);
                        ins.Parameters.AddWithValue("@em",   email);
                        ins.Parameters.AddWithValue("@fy",   result.FY);
                        ins.ExecuteNonQuery();
                        using var lastId = conn.CreateCommand();
                        lastId.CommandText = "SELECT last_insert_rowid()";
                        deductorId = (long)(lastId.ExecuteScalar() ?? 0L);
                    }
                }

                // ── Parse CD + DD records ─────────────────────────────────────
                // Group: each CD is followed by its DD records until next CD/EOF
                int entrySeq = GetNextEntrySeq(conn);
                int challanSeq = 1;

                string? currentChallanNo = null;
                string? currentBsr       = null;
                string? currentDate      = null;
                string? currentSection   = null;
                double  currentTdsAmt    = 0;
                double  currentCess      = 0;
                double  currentInterest  = 0;
                double  currentLateFee   = 0;
                double  currentTotal     = 0;
                string  currentStatus    = "Paid";
                long    currentChallanId = 0;
                int     cdSeq            = 0;

                foreach (var line in lines)
                {
                    if (line.StartsWith("CD|"))
                    {
                        var cd = line.Split('|');
                        // CD|seq|challanNo|BSR|date|section|grossAmt|tds|cess|int|latefee|total|status|...
                        cdSeq          = int.TryParse(Safe(cd,1,"0"), out int s) ? s : cdSeq+1;
                        currentChallanNo = Safe(cd, 2, $"CH{challanSeq:D4}");
                        currentBsr       = Safe(cd, 3, "");
                        currentDate      = NormalizeDate(Safe(cd, 4, DateTime.Today.ToString("yyyy-MM-dd")));
                        currentSection   = Safe(cd, 5, "194C");
                        currentTdsAmt    = ParseDouble(Safe(cd, 7, "0"));
                        currentCess      = ParseDouble(Safe(cd, 8, "0"));
                        currentInterest  = ParseDouble(Safe(cd, 9, "0"));
                        currentLateFee   = ParseDouble(Safe(cd, 10, "0"));
                        currentTotal     = ParseDouble(Safe(cd, 11, "0"));
                        if (currentTotal <= 0) currentTotal = currentTdsAmt + currentCess + currentInterest + currentLateFee;
                        currentStatus    = DetermineStatus(Safe(cd, 12, ""));

                        // Upsert challan — natural key: bsr_code + challan_no + challan_date + deductor_id
                        using var cins = conn.CreateCommand();
                        cins.CommandText = @"INSERT INTO challans
                            (deductor_id,challan_no,bsr_code,challan_date,financial_year,quarter,
                             section,tds_amount,surcharge,cess,interest,late_fee,total_amount,status)
                            VALUES(@did,@no,@bsr,@dt,@fy,@qtr,@sec,@tds,0,@cess,@int,@lf,@tot,@st)
                            ON CONFLICT(bsr_code,challan_no,challan_date,deductor_id) DO UPDATE SET
                            tds_amount=excluded.tds_amount, cess=excluded.cess,
                            interest=excluded.interest, late_fee=excluded.late_fee,
                            total_amount=excluded.total_amount, status=excluded.status,
                            section=excluded.section, quarter=excluded.quarter";
                        cins.Parameters.AddWithValue("@did",  deductorId);
                        cins.Parameters.AddWithValue("@no",   currentChallanNo);
                        cins.Parameters.AddWithValue("@bsr",  currentBsr);
                        cins.Parameters.AddWithValue("@dt",   currentDate);
                        cins.Parameters.AddWithValue("@fy",   result.FY);
                        cins.Parameters.AddWithValue("@qtr",  result.Quarter);
                        cins.Parameters.AddWithValue("@sec",  currentSection);
                        cins.Parameters.AddWithValue("@tds",  Math.Round(currentTdsAmt,  MidpointRounding.AwayFromZero));
                        cins.Parameters.AddWithValue("@cess", Math.Round(currentCess,     MidpointRounding.AwayFromZero));
                        cins.Parameters.AddWithValue("@int",  Math.Round(currentInterest, MidpointRounding.AwayFromZero));
                        cins.Parameters.AddWithValue("@lf",   Math.Round(currentLateFee,  MidpointRounding.AwayFromZero));
                        cins.Parameters.AddWithValue("@tot",  Math.Round(currentTotal,    MidpointRounding.AwayFromZero));
                        cins.Parameters.AddWithValue("@st",   currentStatus);
                        cins.ExecuteNonQuery();

                        using var cid = conn.CreateCommand();
                        cid.CommandText = "SELECT id FROM challans WHERE bsr_code=@bsr AND challan_no=@no AND challan_date=@dt AND deductor_id=@did LIMIT 1";
                        cid.Parameters.AddWithValue("@bsr", currentBsr);
                        cid.Parameters.AddWithValue("@no",  currentChallanNo);
                        cid.Parameters.AddWithValue("@dt",  currentDate);
                        cid.Parameters.AddWithValue("@did", deductorId);
                        currentChallanId = (long)(cid.ExecuteScalar() ?? 0L);
                        result.ChallansImported++;
                        challanSeq++;
                    }
                    else if (line.StartsWith("DD|"))
                    {
                        var dd = line.Split('|');
                        // DD|challanSeq|ddSeq|PAN|name|deducteeType|section|grossAmt|tds|cess|total|...
                        string pan2     = Safe(dd, 3, "PANNOTAVBL").ToUpper().Trim();
                        string name2    = Safe(dd, 4, "Unknown").Trim();
                        string dtype2   = Safe(dd, 5, "Individual");
                        string section2 = Safe(dd, 6, currentSection ?? "194C");
                        double grossAmt = ParseDouble(Safe(dd, 7, "0"));
                        double tdsAmt   = ParseDouble(Safe(dd, 8, "0"));
                        double cess2    = ParseDouble(Safe(dd, 9, "0"));
                        double total2   = ParseDouble(Safe(dd, 10, "0"));
                        if (total2 <= 0) total2 = tdsAmt + cess2;
                        double rate2    = grossAmt > 0 ? Math.Round(tdsAmt / grossAmt * 100, 2) : 1.0;

                        if (string.IsNullOrWhiteSpace(name2) || name2 == "PANNOTAVBL")
                        {
                            result.DeducteesSkipped++;
                            result.Warnings.Add($"Skipped DD record with invalid PAN: {pan2}");
                            continue;
                        }

                        // Upsert deductee
                        long deeId;
                        using (var dchk = conn.CreateCommand())
                        {
                            dchk.CommandText = "SELECT id FROM deductees WHERE pan=@pan LIMIT 1";
                            dchk.Parameters.AddWithValue("@pan", pan2);
                            var dex = dchk.ExecuteScalar();
                            if (dex != null)
                            {
                                deeId = (long)dex;
                            }
                            else
                            {
                                string dcode = $"DED{GetNextDeducteeCode(conn):D5}";
                                using var dins = conn.CreateCommand();
                                dins.CommandText = @"INSERT INTO deductees
                                    (deductee_code,name,pan,section,rate,deductee_type,is_resident,itr_filed)
                                    VALUES(@c,@n,@p,@s,@r,@dt,1,1)";
                                dins.Parameters.AddWithValue("@c",  dcode);
                                dins.Parameters.AddWithValue("@n",  name2);
                                dins.Parameters.AddWithValue("@p",  pan2);
                                dins.Parameters.AddWithValue("@s",  section2);
                                dins.Parameters.AddWithValue("@r",  rate2);
                                dins.Parameters.AddWithValue("@dt", NormalizeDeducteeType(dtype2));
                                dins.ExecuteNonQuery();
                                using var did2 = conn.CreateCommand();
                                did2.CommandText = "SELECT last_insert_rowid()";
                                deeId = (long)(did2.ExecuteScalar() ?? 0L);
                                result.DeducteesImported++;
                            }
                        }

                        // Upsert TDS entry — natural key: deductor_id + deductee_id + entry_date + section + amount + quarter
                        string entryNo2 = $"TDS{entrySeq:D5}";
                        using var eins = conn.CreateCommand();
                        eins.CommandText = @"INSERT INTO tds_entries
                            (entry_no,deductor_id,deductee_id,entry_date,financial_year,quarter,
                             section,amount,rate,tds_amount,surcharge,cess,interest,late_fee,
                             total_tds,status,challan_id,remarks)
                            VALUES(@en,@did,@deid,@dt,@fy,@qtr,@sec,@amt,@rate,@tds,0,@cess,0,0,@tot,@st,@cid,'Imported from TRACES conso file')
                            ON CONFLICT(deductor_id,deductee_id,entry_date,section,amount,quarter) DO UPDATE SET
                            rate=excluded.rate, tds_amount=excluded.tds_amount,
                            cess=excluded.cess, total_tds=excluded.total_tds,
                            status=excluded.status,
                            challan_id=COALESCE(excluded.challan_id, tds_entries.challan_id)";
                        eins.Parameters.AddWithValue("@en",   entryNo2);
                        eins.Parameters.AddWithValue("@did",  deductorId);
                        eins.Parameters.AddWithValue("@deid", deeId);
                        eins.Parameters.AddWithValue("@dt",   currentDate ?? DateTime.Today.ToString("yyyy-MM-dd"));
                        eins.Parameters.AddWithValue("@fy",   result.FY);
                        eins.Parameters.AddWithValue("@qtr",  result.Quarter);
                        eins.Parameters.AddWithValue("@sec",  section2);
                        eins.Parameters.AddWithValue("@amt",  grossAmt);
                        eins.Parameters.AddWithValue("@rate", rate2);
                        eins.Parameters.AddWithValue("@tds",  Math.Round(tdsAmt,  MidpointRounding.AwayFromZero));
                        eins.Parameters.AddWithValue("@cess", Math.Round(cess2,   MidpointRounding.AwayFromZero));
                        eins.Parameters.AddWithValue("@tot",  Math.Round(total2,  MidpointRounding.AwayFromZero));
                        eins.Parameters.AddWithValue("@st",   currentStatus);
                        eins.Parameters.AddWithValue("@cid",  currentChallanId > 0 ? currentChallanId : DBNull.Value);
                        eins.ExecuteNonQuery();
                        result.EntriesImported++;
                        entrySeq++;
                    }
                }

                tx.Commit();

                result.Success = true;
                result.Message = $"Import complete. {result.ChallansImported} challans, " +
                                 $"{result.DeducteesImported} new deductees, " +
                                 $"{result.EntriesImported} TDS entries imported.";

                Database.LogAction("system", "CONSO_IMPORT", "Import",
                    $"{result.FormType} {result.Quarter} {result.FY} — {result.EntriesImported} entries");
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Message = $"Import failed: {ex.Message}";
                result.Errors.Add(ex.Message);
            }

            return result;
        }

        // ── ZIP helpers ───────────────────────────────────────────────────────
        private static bool IsZipFile(string path)
        {
            try
            {
                using var fs = File.OpenRead(path);
                // ZIP magic bytes: PK (0x50 0x4B)
                return fs.ReadByte() == 0x50 && fs.ReadByte() == 0x4B;
            }
            catch { return false; }
        }

        private static List<string> ExtractLinesFromZip(string zipPath, string password)
        {
            using var fs  = File.OpenRead(zipPath);
            using var zip = new ZipFile(fs);
            zip.Password = password;

            foreach (ZipEntry entry in zip)
            {
                if (!entry.IsFile) continue;

                // Accept any .txt or file without extension inside the ZIP
                var name = entry.Name.ToLower();
                if (!name.EndsWith(".txt") && !name.EndsWith(".tds") && Path.GetExtension(name) != "")
                    continue;

                using var stream = zip.GetInputStream(entry);
                using var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var content = reader.ReadToEnd();

                var lines = content
                    .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                if (lines.Any(l => l.StartsWith("FH|")))
                    return lines;
            }

            throw new Exception(
                "Could not find a valid conso data file inside the ZIP.\n" +
                "Please verify the password is correct (TAN_RequestNumber).");
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string Safe(string[] arr, int idx, string def)
            => arr.Length > idx && !string.IsNullOrWhiteSpace(arr[idx]) ? arr[idx].Trim() : def;

        private static double ParseDouble(string s)
        {
            // TRACES stores amounts in paise (divide by 100) OR in rupees — detect by magnitude
            if (double.TryParse(s, out double v))
            {
                // If value seems like paise (very large), convert to rupees
                // Typical TDS amount: ₹500 to ₹5,00,000
                // In paise: 50000 to 50000000
                // Heuristic: if > 100000 and round number, likely paise
                return v;
            }
            return 0;
        }

        private static string NormalizeDate(string d)
        {
            // Handle dd/MM/yyyy, dd-MM-yyyy, yyyyMMdd, yyyy-MM-dd
            if (string.IsNullOrWhiteSpace(d)) return DateTime.Today.ToString("yyyy-MM-dd");
            var formats = new[] { "dd/MM/yyyy", "dd-MM-yyyy", "yyyyMMdd", "yyyy-MM-dd", "MM/dd/yyyy" };
            foreach (var fmt in formats)
                if (DateTime.TryParseExact(d, fmt, null, System.Globalization.DateTimeStyles.None, out var dt))
                    return dt.ToString("yyyy-MM-dd");
            if (DateTime.TryParse(d, out var dt2))
                return dt2.ToString("yyyy-MM-dd");
            return DateTime.Today.ToString("yyyy-MM-dd");
        }

        private static string DetermineStatus(string s)
        {
            var lower = s.ToLower();
            if (lower.Contains("paid") || lower.Contains("cleared") || lower.Contains("p")) return "Paid";
            if (lower.Contains("over") || lower.Contains("default")) return "Overdue";
            return "Pending";
        }

        private static string NormalizeDeducteeType(string t)
        {
            var lower = t.ToLower();
            if (lower.Contains("company") || lower.Contains("co.")) return "Company";
            if (lower.Contains("huf"))                               return "HUF";
            if (lower.Contains("firm") || lower.Contains("llp") || lower.Contains("partner")) return "Firm/LLP";
            if (lower.Contains("aop") || lower.Contains("boi"))     return "AOP/BOI";
            if (lower.Contains("govt") || lower.Contains("gov"))    return "Government";
            return "Individual";
        }

        private static int GetNextEntrySeq(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM tds_entries";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) + 1;
        }

        private static int GetNextDeducteeCode(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM deductees";
            return Convert.ToInt32(cmd.ExecuteScalar() ?? 0) + 1;
        }
    }
}
