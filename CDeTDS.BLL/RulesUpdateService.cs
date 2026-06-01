using System;
using System.Linq;
using CDeTDS.DAL;
using CDeTDS.DAL.Models;

namespace CDeTDS.BLL
{
    public class RulesUpdateService
    {
        private readonly TdsRulesEngine _engine = new();
        private const string VersionKey = "RULES_VERSION";

        public string AutoUpdateIfNeeded()
        {
            try
            {
                var dbVersion = Database.GetSetting(VersionKey, "");
                if (dbVersion == BuiltInTdsRules.CurrentVersion)
                    return $"Rules up to date — v{BuiltInTdsRules.CurrentVersion}";

                int updated = 0, added = 0;
                var existing = _engine.GetAllRules(false);

                foreach (var rule in BuiltInTdsRules.Rules)
                {
                    var match = existing.FirstOrDefault(r =>
                        string.Equals(r.SectionCode,     rule.SectionCode,     StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.NatureOfPayment, rule.NatureOfPayment, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.DeducteeType,    rule.DeducteeType,    StringComparison.OrdinalIgnoreCase));

                    DateTime.TryParse(rule.EffectiveFrom, out var effFrom);

                    var dbRule = new TdsRule
                    {
                        Id              = match?.Id ?? 0,
                        SectionCode     = rule.SectionCode,
                        NatureOfPayment = rule.NatureOfPayment,
                        DeducteeType    = rule.DeducteeType,
                        IsResident      = rule.Resident,
                        ThresholdLimit  = rule.ThresholdRs,
                        TdsRate         = rule.RatePercent,
                        SurchargeRate   = rule.SurchargePercent,
                        CessRate        = rule.CessPercent,
                        EffectiveFrom   = effFrom == default ? new DateTime(2026,4,1) : effFrom,
                        EffectiveTo     = null,
                        ReferenceAct    = rule.ReferenceAct,
                        Notes           = match?.Notes ?? "",
                        IsActive        = true,
                    };

                    _engine.SaveRule(dbRule);
                    if (match == null) added++; else updated++;
                }

                Database.SetSetting(VersionKey, BuiltInTdsRules.CurrentVersion);
                Database.LogAction("System", "RULES_AUTO_UPDATE", "TdsRules",
                    $"Auto-updated {updated} + added {added} rules to v{BuiltInTdsRules.CurrentVersion}");

                return $"✓ Rules auto-updated to v{BuiltInTdsRules.CurrentVersion} ({added} added, {updated} updated)";
            }
            catch (Exception ex)
            {
                return $"Rules update failed: {ex.Message}";
            }
        }

        public string DbVersion    => Database.GetSetting(VersionKey, "Not set");
        public static string AppVersion => BuiltInTdsRules.CurrentVersion;
        public bool UpdateAvailable => DbVersion != BuiltInTdsRules.CurrentVersion;
    }
}
