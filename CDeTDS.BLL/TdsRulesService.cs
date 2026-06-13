using CDeTDS.DAL;
using CDeTDS.DAL.Models;

namespace CDeTDS.BLL
{
    /// <summary>
    /// Business logic layer for the dynamic TDS Rules Engine.
    /// </summary>
    public class TdsRulesService
    {
        private readonly TdsRulesEngine _engine = new();

        public List<TdsRule> GetAllRules(bool activeOnly = true)
            => _engine.GetAllRules(activeOnly);

        public TdsRule? GetApplicableRule(string section, string deducteeType,
            bool isResident, DateTime date)
            => _engine.GetApplicableRule(section, deducteeType, isResident, date);

        /// <summary>
        /// Full TDS calculation respecting 2026 rules.
        /// Handles 206AA (no PAN) and 206AB (ITR not filed) automatically.
        /// </summary>
        public TdsCalculationResult Calculate(
            string   section,
            double   amount,
            string   deducteeType  = "Individual",
            bool     isResident    = true,
            bool     panAvailable  = true,
            bool     itrFiled      = true,
            DateTime? txDate       = null,
            double   ytdAmount     = 0)
        {
            var date = txDate ?? DateTime.Today;
            return _engine.Calculate(section, amount, deducteeType,
                isResident, panAvailable, itrFiled, date, ytdAmount: ytdAmount);
        }

        /// <summary>
        /// Validate a user-entered rate against DB rules.
        /// Used during Excel import and manual entry.
        /// </summary>
        public (bool Ok, string Message) ValidateRate(
            string section, double enteredRate,
            string deducteeType, bool isResident, DateTime txDate)
            => _engine.ValidateRate(section, enteredRate, deducteeType, isResident, txDate);

        /// <summary>
        /// Get sections for UI dropdowns — from DB, not hardcoded.
        /// </summary>
        public List<(string Section, string Nature)> GetSectionsList()
            => _engine.GetSectionsList();

        public (bool Ok, string Msg) SaveRule(TdsRule rule)
            => _engine.SaveRule(rule);
    }
}
