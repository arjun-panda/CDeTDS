using Microsoft.Data.Sqlite;

namespace CDeTDS.DAL
{
    /// <summary>
    /// PAN AutoComplete — searches the local deductee and deductor master
    /// for partial PAN / name matches. Returns rich suggestions instantly.
    /// Also maintains a PAN cache so previously used PANs auto-fill details.
    /// </summary>
    public static class PanAutoComplete
    {
        // ── Search deductees by PAN prefix or name ────────────────────────────
        public static List<PanSuggestion> SearchDeductees(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<PanSuggestion>();

            var q = query.Trim().ToUpper();
            var list = new List<PanSuggestion>();

            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();

            cmd.CommandText = @"
                SELECT pan, name, section, rate, deductee_type, is_resident,
                       lower_cert_no, lower_cert_rate, lower_cert_till, remarks
                FROM deductees
                WHERE UPPER(pan)  LIKE @q1
                   OR UPPER(name) LIKE @q2
                ORDER BY
                    CASE WHEN UPPER(pan) = @exact THEN 0
                         WHEN UPPER(pan) LIKE @q1  THEN 1
                         ELSE 2 END,
                    name
                LIMIT 15";
            cmd.Parameters.AddWithValue("@q1",   q + "%");
            cmd.Parameters.AddWithValue("@q2",   "%" + q + "%");
            cmd.Parameters.AddWithValue("@exact", q);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new PanSuggestion
                {
                    Pan           = r.GetString(r.GetOrdinal("pan")),
                    Name          = r.GetString(r.GetOrdinal("name")),
                    Section       = r.GetString(r.GetOrdinal("section")),
                    Rate          = r.GetDouble(r.GetOrdinal("rate")),
                    DeducteeType  = r.IsDBNull(r.GetOrdinal("deductee_type")) ? "Individual" : r.GetString(r.GetOrdinal("deductee_type")),
                    IsResident    = r.GetInt32(r.GetOrdinal("is_resident")) == 1,
                    LowerCertNo   = r.IsDBNull(r.GetOrdinal("lower_cert_no"))   ? "" : r.GetString(r.GetOrdinal("lower_cert_no")),
                    LowerCertRate = r.IsDBNull(r.GetOrdinal("lower_cert_rate")) ? 0  : r.GetDouble(r.GetOrdinal("lower_cert_rate")),
                    LowerCertTill = r.IsDBNull(r.GetOrdinal("lower_cert_till")) ? "" : r.GetString(r.GetOrdinal("lower_cert_till")),
                    Remarks       = r.IsDBNull(r.GetOrdinal("remarks"))         ? "" : r.GetString(r.GetOrdinal("remarks")),
                    Source        = "Deductee Master",
                });
            }
            return list;
        }

        // ── Search deductors by TAN or name ──────────────────────────────────
        public static List<PanSuggestion> SearchDeductors(string query)
        {
            if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
                return new List<PanSuggestion>();

            var q = query.Trim().ToUpper();
            var list = new List<PanSuggestion>();

            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT tan, pan, company_name, contact_person,
                       phone, email, address, city, state, pincode
                FROM deductors
                WHERE is_active = 1
                  AND (UPPER(tan)          LIKE @q1
                    OR UPPER(company_name) LIKE @q2
                    OR UPPER(pan)          LIKE @q1)
                ORDER BY
                    CASE WHEN UPPER(tan) = @exact THEN 0 ELSE 1 END, company_name
                LIMIT 10";
            cmd.Parameters.AddWithValue("@q1",   q + "%");
            cmd.Parameters.AddWithValue("@q2",   "%" + q + "%");
            cmd.Parameters.AddWithValue("@exact", q);

            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new PanSuggestion
                {
                    Pan           = r.IsDBNull(r.GetOrdinal("pan")) ? "" : r.GetString(r.GetOrdinal("pan")),
                    Tan           = r.GetString(r.GetOrdinal("tan")),
                    Name          = r.GetString(r.GetOrdinal("company_name")),
                    ContactPerson = r.IsDBNull(r.GetOrdinal("contact_person")) ? "" : r.GetString(r.GetOrdinal("contact_person")),
                    Phone         = r.IsDBNull(r.GetOrdinal("phone")) ? "" : r.GetString(r.GetOrdinal("phone")),
                    Email         = r.IsDBNull(r.GetOrdinal("email")) ? "" : r.GetString(r.GetOrdinal("email")),
                    Address       = r.IsDBNull(r.GetOrdinal("address")) ? "" : r.GetString(r.GetOrdinal("address")),
                    City          = r.IsDBNull(r.GetOrdinal("city")) ? "" : r.GetString(r.GetOrdinal("city")),
                    State         = r.IsDBNull(r.GetOrdinal("state")) ? "" : r.GetString(r.GetOrdinal("state")),
                    Pincode       = r.IsDBNull(r.GetOrdinal("pincode")) ? "" : r.GetString(r.GetOrdinal("pincode")),
                    Source        = "Deductor Master",
                });
            }
            return list;
        }

        // ── Exact PAN lookup ──────────────────────────────────────────────────
        public static PanSuggestion? GetByPan(string pan)
        {
            var results = SearchDeductees(pan);
            return results.FirstOrDefault(r => r.Pan.Equals(pan.Trim().ToUpper(),
                StringComparison.OrdinalIgnoreCase));
        }

        public static PanSuggestion? GetDeductorByTan(string tan)
        {
            var results = SearchDeductors(tan);
            return results.FirstOrDefault(r => r.Tan?.Equals(tan.Trim().ToUpper(),
                StringComparison.OrdinalIgnoreCase) == true);
        }

        // ── PAN history / recently used ───────────────────────────────────────
        public static List<string> GetRecentPans(int limit = 10)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT DISTINCT d.pan
                    FROM tds_entries e
                    JOIN deductees d ON e.deductee_id = d.id
                    ORDER BY e.id DESC
                    LIMIT @lim";
                cmd.Parameters.AddWithValue("@lim", limit);
                var list = new List<string>();
                using var r = cmd.ExecuteReader();
                while (r.Read()) list.Add(r.GetString(0));
                return list;
            }
            catch { return new List<string>(); }
        }

        // ── Validate PAN format ───────────────────────────────────────────────
        public static PanValidationResult ValidatePan(string pan)
        {
            if (string.IsNullOrWhiteSpace(pan))
                return new PanValidationResult { IsValid = false, Message = "PAN is required." };

            pan = pan.Trim().ToUpper();
            if (pan.Length != 10)
                return new PanValidationResult { IsValid = false, Message = $"PAN must be 10 characters (entered: {pan.Length})." };

            if (!System.Text.RegularExpressions.Regex.IsMatch(pan, @"^[A-Z]{5}[0-9]{4}[A-Z]$"))
                return new PanValidationResult { IsValid = false, Message = "Invalid PAN format. Expected: AAAAA0000A" };

            // Decode entity type from 4th character
            var entityType = pan[3] switch
            {
                'P' => "Individual (Person)",
                'H' => "HUF (Hindu Undivided Family)",
                'C' => "Company",
                'F' => "Firm / LLP",
                'A' => "Association of Persons (AOP)",
                'T' => "Trust",
                'B' => "Body of Individuals (BOI)",
                'L' => "Local Authority",
                'J' => "Artificial Juridical Person",
                'G' => "Government",
                _   => "Unknown Entity"
            };

            return new PanValidationResult
            {
                IsValid    = true,
                Pan        = pan,
                EntityType = entityType,
                Message    = $"Valid PAN — {entityType}",
            };
        }
    }

    // ── Models ─────────────────────────────────────────────────────────────────
    public class PanSuggestion
    {
        public string Pan           { get; set; } = "";
        public string? Tan          { get; set; }
        public string Name          { get; set; } = "";
        public string Section       { get; set; } = "";
        public double Rate          { get; set; }
        public string DeducteeType  { get; set; } = "Individual";
        public bool   IsResident    { get; set; } = true;
        public string LowerCertNo   { get; set; } = "";
        public double LowerCertRate { get; set; }
        public string LowerCertTill { get; set; } = "";
        public string Remarks       { get; set; } = "";
        public string ContactPerson { get; set; } = "";
        public string Phone         { get; set; } = "";
        public string Email         { get; set; } = "";
        public string Address       { get; set; } = "";
        public string City          { get; set; } = "";
        public string State         { get; set; } = "";
        public string Pincode       { get; set; } = "";
        public string Source        { get; set; } = "";

        public string DisplayText  => $"{Pan}  —  {Name}";
        public string SubText      => !string.IsNullOrEmpty(Section)
            ? $"Sec {Section} | {DeducteeType} | {Rate}%"
            : $"{City} | {State}";
    }

    public class PanValidationResult
    {
        public bool   IsValid    { get; set; }
        public string Pan        { get; set; } = "";
        public string EntityType { get; set; } = "";
        public string Message    { get; set; } = "";
    }
}
