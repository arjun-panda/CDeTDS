using Microsoft.Data.Sqlite;
using TDSPro.Common;
using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// Dynamic TDS Rules Engine.
    /// All rates come from the tds_rules database table.
    /// NEVER hardcodes any rate — picks correct rule based on:
    ///   - Transaction date (effective_from / effective_to)
    ///   - Deductee type (Individual / Company / NRI)
    ///   - Resident status
    ///   - PAN availability  → 206AA higher rate
    ///   - ITR filed status  → 206AB higher rate
    /// </summary>
    public class TdsRulesEngine
    {
        // ── Get all rules (for UI display / admin) ────────────────────────────
        public List<TdsRule> GetAllRules(bool activeOnly = true)
        {
            var list = new List<TdsRule>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = activeOnly
                ? "SELECT * FROM tds_rules WHERE is_active=1 ORDER BY section_code, deductee_type"
                : "SELECT * FROM tds_rules ORDER BY section_code, deductee_type";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(MapRule(r));
            return list;
        }

        // ── Get applicable rule for a specific transaction ────────────────────
        public TdsRule? GetApplicableRule(
            string sectionCode,
            string deducteeType,
            bool   isResident,
            DateTime transactionDate)
        {
            using var conn = Database.GetConnection();
            var dt   = NormalizeDeducteeType(deducteeType);
            var sc   = sectionCode.ToUpper();
            var td   = transactionDate.ToString("yyyy-MM-dd");
            var ir   = isResident ? 1 : 0;

            // Pass 1: exact date-range match
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    SELECT * FROM tds_rules
                    WHERE section_code   = @sc
                      AND is_active      = 1
                      AND is_resident    = @ir
                      AND (deductee_type = @dt OR deductee_type = 'All')
                      AND effective_from <= @td
                      AND (effective_to  IS NULL OR effective_to >= @td)
                    ORDER BY
                        CASE WHEN deductee_type = @dt THEN 0 ELSE 1 END,
                        effective_from DESC
                    LIMIT 1";
                cmd.Parameters.AddWithValue("@sc", sc);
                cmd.Parameters.AddWithValue("@dt", dt);
                cmd.Parameters.AddWithValue("@ir", ir);
                cmd.Parameters.AddWithValue("@td", td);
                using var r = cmd.ExecuteReader();
                if (r.Read()) return MapRule(r);
            }

            // Pass 2: fallback — return nearest rule ignoring date bounds
            // (handles historical entries where rules predate the effective_from)
            using (var cmd2 = conn.CreateCommand())
            {
                cmd2.CommandText = @"
                    SELECT * FROM tds_rules
                    WHERE section_code   = @sc
                      AND is_active      = 1
                      AND is_resident    = @ir
                      AND (deductee_type = @dt OR deductee_type = 'All')
                    ORDER BY
                        CASE WHEN deductee_type = @dt THEN 0 ELSE 1 END,
                        effective_from ASC
                    LIMIT 1";
                cmd2.Parameters.AddWithValue("@sc", sc);
                cmd2.Parameters.AddWithValue("@dt", dt);
                cmd2.Parameters.AddWithValue("@ir", ir);
                using var r2 = cmd2.ExecuteReader();
                return r2.Read() ? MapRule(r2) : null;
            }
        }

        // ── Full TDS calculation with all 2026 rules applied ─────────────────
        public TdsCalculationResult Calculate(
            string   sectionCode,
            double   grossAmount,
            string   deducteeType,
            bool     isResident,
            bool     panAvailable,
            bool     itrFiled,
            DateTime transactionDate,
            string?  overrideNature = null)
        {
            var result = new TdsCalculationResult
            {
                SectionCode    = sectionCode.ToUpper(),
                GrossAmount    = grossAmount,
                PanAvailable   = panAvailable,
            };

            // ── Step 1: Get base rule from DB ─────────────────────────────────
            var rule = GetApplicableRule(sectionCode, deducteeType, isResident, transactionDate);

            if (rule == null)
            {
                result.Warnings.Add($"No active TDS rule found for Section {sectionCode} " +
                                    $"({deducteeType}, {transactionDate:dd-MMM-yyyy}). Rate set to 0.");
                result.RuleApplied = "No matching rule";
                return result;
            }

            result.NatureOfPayment = overrideNature ?? rule.NatureOfPayment;
            result.RuleApplied     = $"Rule #{rule.Id} — {rule.ReferenceAct}";

            // ── Step 2: Threshold check ───────────────────────────────────────
            if (rule.ThresholdLimit > 0 && grossAmount < rule.ThresholdLimit)
            {
                result.BelowThreshold = true;
                result.Warnings.Add($"Amount Rs {grossAmount:N0} is below threshold " +
                                    $"Rs {rule.ThresholdLimit:N0} for Section {sectionCode}. No TDS applicable.");
                return result;
            }

            // ── Step 3: Determine applicable rate ─────────────────────────────
            double applicableRate = rule.TdsRate;
            bool   higherRate     = false;
            string higherReason   = "";

            // 206AA — No PAN: higher of (normal rate, 20%, twice the normal rate)
            if (!panAvailable)
            {
                double rate206AA = Math.Max(
                    AppConstants.HigherTdsRateNoPan,
                    Math.Max(rule.TdsRate, rule.TdsRate * 2));
                applicableRate = rate206AA;
                higherRate     = true;
                higherReason   = $"PAN not available — Section 206AA applied. Rate: {applicableRate}%";
                result.Warnings.Add(higherReason);
            }
            // 206AB — REMOVED w.e.f. 1-Apr-2025 by Finance Act 2025; no higher rate for ITR non-filers from FY 2025-26 onwards
            else if (!itrFiled && isResident && !IsExemptSection(sectionCode)
                     && transactionDate < new DateTime(2025, 4, 1))
            {
                double rate206AB = Math.Max(5.0, Math.Max(rule.TdsRate, rule.TdsRate * 2));
                if (rate206AB > applicableRate)
                {
                    applicableRate = rate206AB;
                    higherRate     = true;
                    higherReason   = $"ITR not filed — Section 206AB applied. Rate: {applicableRate}%";
                    result.Warnings.Add(higherReason);
                }
            }

            // ── Step 4: Compute TDS, surcharge, cess ─────────────────────────
            // Section 288B: TDS, surcharge, cess all rounded to nearest rupee
            double tdsAmount = Math.Round(grossAmount * applicableRate / 100, MidpointRounding.AwayFromZero);
            double surcharge = Math.Round(tdsAmount * rule.SurchargeRate / 100, MidpointRounding.AwayFromZero);
            double cess      = Math.Round((tdsAmount + surcharge) * rule.CessRate / 100, MidpointRounding.AwayFromZero);
            double totalTds  = tdsAmount + surcharge + cess;

            result.ApplicableRate   = applicableRate;
            result.TdsAmount        = tdsAmount;
            result.SurchargeAmount  = surcharge;
            result.CessAmount       = cess;
            result.TotalTds         = totalTds;
            result.HigherRateApplied= higherRate;
            result.HigherRateReason = higherReason;

            return result;
        }

        // ── Validate a manually entered rate against DB rule ──────────────────
        public (bool Ok, string Message) ValidateRate(
            string sectionCode,
            double enteredRate,
            string deducteeType,
            bool   isResident,
            DateTime transactionDate)
        {
            var rule = GetApplicableRule(sectionCode, deducteeType, isResident, transactionDate);
            if (rule == null)
                return (false, $"Section {sectionCode} not found in TDS Rules table for {transactionDate:dd-MMM-yyyy}.");

            // Allow ±0.01% tolerance for floating point
            if (Math.Abs(enteredRate - rule.TdsRate) < 0.01)
                return (true, $"Rate {enteredRate}% matches rule for Section {sectionCode}.");

            return (false,
                $"Rate mismatch for Section {sectionCode}: entered {enteredRate}%, " +
                $"expected {rule.TdsRate}% as per {rule.ReferenceAct}.");
        }

        // ── Get sections list for UI dropdowns (from DB, not hardcoded) ───────
        public List<(string Section, string Nature)> GetSectionsList()
        {
            var list = new List<(string, string)>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT DISTINCT section_code, nature_of_payment
                FROM tds_rules
                WHERE is_active = 1
                ORDER BY section_code";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add((r.GetString(0), r.GetString(1)));
            return list;
        }

        // ── Admin: add/update a rule ──────────────────────────────────────────
        public (bool Ok, string Msg) SaveRule(TdsRule rule)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd  = conn.CreateCommand();
                if (rule.Id == 0)
                {
                    cmd.CommandText = @"INSERT INTO tds_rules
                        (section_code,nature_of_payment,deductee_type,is_resident,
                         threshold_limit,tds_rate,surcharge_rate,cess_rate,
                         effective_from,effective_to,reference_act,notes,is_active)
                        VALUES(@sc,@np,@dt,@ir,@th,@tr,@sr,@cr,@ef,@et,@ra,@nt,1)";
                }
                else
                {
                    cmd.CommandText = @"UPDATE tds_rules SET
                        section_code=@sc, nature_of_payment=@np, deductee_type=@dt,
                        is_resident=@ir, threshold_limit=@th, tds_rate=@tr,
                        surcharge_rate=@sr, cess_rate=@cr, effective_from=@ef,
                        effective_to=@et, reference_act=@ra, notes=@nt
                        WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", rule.Id);
                }
                cmd.Parameters.AddWithValue("@sc", rule.SectionCode.ToUpper());
                cmd.Parameters.AddWithValue("@np", rule.NatureOfPayment);
                cmd.Parameters.AddWithValue("@dt", rule.DeducteeType);
                cmd.Parameters.AddWithValue("@ir", rule.IsResident ? 1 : 0);
                cmd.Parameters.AddWithValue("@th", rule.ThresholdLimit);
                cmd.Parameters.AddWithValue("@tr", rule.TdsRate);
                cmd.Parameters.AddWithValue("@sr", rule.SurchargeRate);
                cmd.Parameters.AddWithValue("@cr", rule.CessRate);
                cmd.Parameters.AddWithValue("@ef", rule.EffectiveFrom.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@et",
                    rule.EffectiveTo.HasValue ? rule.EffectiveTo.Value.ToString("yyyy-MM-dd") : DBNull.Value);
                cmd.Parameters.AddWithValue("@ra", rule.ReferenceAct);
                cmd.Parameters.AddWithValue("@nt", rule.Notes);
                cmd.ExecuteNonQuery();

                Database.LogAction("system", rule.Id == 0 ? "ADD_RULE" : "UPDATE_RULE",
                    "TdsRules", $"{rule.SectionCode} rate={rule.TdsRate}%");
                return (true, "Rule saved.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private static string NormalizeDeducteeType(string t) => t switch
        {
            var s when s.StartsWith("NRI") => s,
            "Company" => "Company",
            "HUF"     => "HUF",
            _         => "Individual"
        };

        private static bool IsExemptSection(string s) =>
            s is "192" or "192A" or "194B" or "194BB" or "194BA" or "195";

        private static TdsRule MapRule(SqliteDataReader r) => new()
        {
            Id              = r.GetInt32(r.GetOrdinal("id")),
            SectionCode     = r.GetString(r.GetOrdinal("section_code")),
            NatureOfPayment = r.GetString(r.GetOrdinal("nature_of_payment")),
            DeducteeType    = r.IsDBNull(r.GetOrdinal("deductee_type"))   ? "All" : r.GetString(r.GetOrdinal("deductee_type")),
            IsResident      = r.GetInt32(r.GetOrdinal("is_resident")) == 1,
            ThresholdLimit  = r.IsDBNull(r.GetOrdinal("threshold_limit")) ? 0 : r.GetDouble(r.GetOrdinal("threshold_limit")),
            TdsRate         = r.GetDouble(r.GetOrdinal("tds_rate")),
            SurchargeRate   = r.IsDBNull(r.GetOrdinal("surcharge_rate"))  ? 0 : r.GetDouble(r.GetOrdinal("surcharge_rate")),
            CessRate        = r.IsDBNull(r.GetOrdinal("cess_rate"))       ? 0 : r.GetDouble(r.GetOrdinal("cess_rate")),
            EffectiveFrom   = DateTime.Parse(r.GetString(r.GetOrdinal("effective_from"))),
            EffectiveTo     = r.IsDBNull(r.GetOrdinal("effective_to"))    ? null : DateTime.Parse(r.GetString(r.GetOrdinal("effective_to"))),
            ReferenceAct    = r.IsDBNull(r.GetOrdinal("reference_act"))   ? "" : r.GetString(r.GetOrdinal("reference_act")),
            Notes           = r.IsDBNull(r.GetOrdinal("notes"))           ? "" : r.GetString(r.GetOrdinal("notes")),
            IsActive        = r.GetInt32(r.GetOrdinal("is_active")) == 1,
        };
    }
}
