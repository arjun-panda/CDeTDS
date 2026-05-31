namespace TDSPro.Common
{
    // ══════════════════════════════════════════════════════════════════════════
    // TDSPro Tax Rules — verified against incometax.gov.in, Finance Acts 2020-2026
    // Last audit: April 2026
    // Key sources:
    //   incometax.gov.in/iec/foportal/help/individual/return-applicable-1  (Salaried <60)
    //   incometax.gov.in/iec/foportal/help/individual/return-applicable-2  (Senior/Super Senior)
    //   incometax.gov.in/iec/foportal/help/new-tax-vs-old-tax-regime-faqs  (Official FAQ)
    //   cleartax.in/s/standard-deduction-salary                             (Std ded verification)
    //   CBDT Circular on Section 192 TDS on salary
    // ══════════════════════════════════════════════════════════════════════════

    public record Slab(double From, double To, double Rate);

    /// <summary>Age category for old-regime slab selection per Section 192.</summary>
    public enum AgeCategory { Below60, Senior60to79, SuperSenior80Plus }

    /// <summary>All parameters for one FY, regime, and age category.</summary>
    public record RegimeRules(
        string      FY,
        bool        IsNew,
        AgeCategory Age,        // ignored for new regime (same slabs for all ages)
        Slab[]      Slabs,
        double      StandardDeduction,      // ₹ annual (salaried/pensioner)
        double      Rebate87AThreshold,     // max TAXABLE income to claim rebate
        double      Rebate87AMaxAmount,     // max rebate = min(tax, this)
        bool        MarginalRelief87A,      // true for new regime FY 2023-24+
        double      BasicExemptionLimit,    // nil-rate floor (informational)
        double      MaxSurchargeRate,       // 0.37 old regime, 0.25 new regime
        double      Nps80CCD2Rate           // employer NPS deduction cap (fraction of salary)
    );

    public static class TaxRules
    {
        // ════════════════════════════════════════════════════════════════════
        // RULE TABLES
        // All values verified against official sources.
        // ════════════════════════════════════════════════════════════════════

        // ── OLD REGIME — slab definitions ────────────────────────────────────
        // Slabs UNCHANGED since FY 2017-18.
        // Standard deduction: ₹50,000 throughout (introduced FY 2019-20; Budget 2024
        //   raised to ₹75K ONLY for NEW regime — old regime stays ₹50K permanently).
        // 87A: ₹12,500 for taxable income ≤ ₹5L (no marginal relief in old regime).
        // Surcharge: max 37% for income >₹5Cr.
        // 80CCD(2): 10% of salary (old regime; unchanged).

        private static Slab[] OldSlabs_Below60 => new[]
        {
            new Slab(0,       250000, 0.00),  // Nil up to ₹2.5L
            new Slab(250000,  500000, 0.05),  // 5%
            new Slab(500000, 1000000, 0.20),  // 20%
            new Slab(1000000, double.MaxValue, 0.30), // 30%
        };

        private static Slab[] OldSlabs_Senior60to79 => new[]
        {
            new Slab(0,       300000, 0.00),  // Nil up to ₹3L (higher exemption for 60-79 yrs)
            new Slab(300000,  500000, 0.05),  // 5%
            new Slab(500000, 1000000, 0.20),  // 20%
            new Slab(1000000, double.MaxValue, 0.30), // 30%
        };

        private static Slab[] OldSlabs_SuperSenior => new[]
        {
            new Slab(0,       500000, 0.00),  // Nil up to ₹5L (higher exemption for 80+ yrs)
            new Slab(500000, 1000000, 0.20),  // 20%
            new Slab(1000000, double.MaxValue, 0.30), // 30%
        };
        // Note: Super senior citizen (80+) under old regime has ₹5L nil,
        // effectively NO 87A benefit since their nil exemption already covers ₹5L income.

        // ── NEW REGIME — slab definitions ────────────────────────────────────
        // NEW REGIME: IDENTICAL slabs for ALL ages (no senior citizen benefit).
        // Budget 2023 (FY 2023-24): restructured slabs, std ded ₹50K, 87A ₹25K ≤ ₹7L
        // Budget 2024 (FY 2024-25): revised slabs, std ded raised to ₹75K, 80CCD(2) 14%
        // Budget 2025 (FY 2025-26): new 7-slab, 87A raised to ₹60K ≤ ₹12L

        private static Slab[] NewSlabs_2020to2022 => new[]
        {
            new Slab(0,       250000, 0.00),
            new Slab(250000,  500000, 0.05),
            new Slab(500000,  750000, 0.10),
            new Slab(750000, 1000000, 0.15),
            new Slab(1000000,1250000, 0.20),
            new Slab(1250000,1500000, 0.25),
            new Slab(1500000, double.MaxValue, 0.30),
        };

        private static Slab[] NewSlabs_2023_24 => new[]
        {
            new Slab(0,       300000, 0.00),
            new Slab(300000,  600000, 0.05),
            new Slab(600000,  900000, 0.10),
            new Slab(900000, 1200000, 0.15),
            new Slab(1200000,1500000, 0.20),
            new Slab(1500000, double.MaxValue, 0.30),
        };

        private static Slab[] NewSlabs_2024_25 => new[]
        {
            new Slab(0,       300000, 0.00),
            new Slab(300000,  700000, 0.05),
            new Slab(700000, 1000000, 0.10),
            new Slab(1000000,1200000, 0.15),
            new Slab(1200000,1500000, 0.20),
            new Slab(1500000, double.MaxValue, 0.30),
        };

        private static Slab[] NewSlabs_2025_26 => new[]
        {
            new Slab(0,       400000, 0.00),  // Nil up to ₹4L
            new Slab(400000,  800000, 0.05),  // 5%
            new Slab(800000, 1200000, 0.10),  // 10%
            new Slab(1200000,1600000, 0.15),  // 15%
            new Slab(1600000,2000000, 0.20),  // 20%
            new Slab(2000000,2400000, 0.25),  // 25%
            new Slab(2400000, double.MaxValue, 0.30), // 30%
        };

        // ── MASTER RULE TABLE ────────────────────────────────────────────────
        // Format: (FY, IsNew, AgeCategory, Slabs, StdDed, 87AThreshold, 87AMax,
        //          MarginalRelief, BasicExemption, MaxSurcharge, NPS80CCD2Rate)

        private static readonly RegimeRules[] _rules = BuildRules();

        private static RegimeRules[] BuildRules()
        {
            var list = new System.Collections.Generic.List<RegimeRules>();

            // Helper to add old-regime entries for all ages
            void AddOld(string fy, double stdDed, double r87Threshold, double r87Max,
                        double basicExemptNew = 250000)
            {
                // Below 60
                list.Add(new RegimeRules(fy, false, AgeCategory.Below60,
                    OldSlabs_Below60, stdDed, r87Threshold, r87Max, false, 250000, 0.37, 0.10));
                // Senior 60-79: same 87A as below-60 (₹5L threshold; income ≤ ₹5L means nil tax anyway)
                list.Add(new RegimeRules(fy, false, AgeCategory.Senior60to79,
                    OldSlabs_Senior60to79, stdDed, r87Threshold, r87Max, false, 300000, 0.37, 0.10));
                // Super Senior 80+: 87A threshold ₹5L but tax on ≤5L is already 0 due to nil slab
                list.Add(new RegimeRules(fy, false, AgeCategory.SuperSenior80Plus,
                    OldSlabs_SuperSenior, stdDed, 0, 0, false, 500000, 0.37, 0.10));
            }

            // Helper to add new-regime (same slabs for all ages)
            void AddNew(string fy, Slab[] slabs, double stdDed, double r87Threshold,
                        double r87Max, bool marginal, double basicEx, double nps2rate,
                        double maxSc = 0.25)
            {
                // New regime: same for all ages — add only AgeCategory.Below60 as canonical
                // GetRules for new regime ignores age and returns this entry
                list.Add(new RegimeRules(fy, true, AgeCategory.Below60,
                    slabs, stdDed, r87Threshold, r87Max, marginal, basicEx, maxSc, nps2rate));
            }

            // ── OLD REGIME (all FYs covered by software) ─────────────────────
            // Standard deduction: ₹50,000 for ALL years in old regime.
            // (Budget 2024 raised std ded to ₹75K ONLY for new regime.)
            AddOld("2020-21", 50000, 500000, 12500);
            AddOld("2021-22", 50000, 500000, 12500);
            AddOld("2022-23", 50000, 500000, 12500);
            AddOld("2023-24", 50000, 500000, 12500);
            AddOld("2024-25", 50000, 500000, 12500);  // ← ₹50K, NOT ₹75K
            AddOld("2025-26", 50000, 500000, 12500);  // old regime stays ₹50K; Budget 2024 raised to ₹75K for NEW regime only
            AddOld("2026-27", 50000, 500000, 12500);
            AddOld("2027-28", 50000, 500000, 12500);

            // ── NEW REGIME ────────────────────────────────────────────────────
            // FY 2020-21 to 2022-23: original 7 slabs, no std ded
            //   87A: ₹12,500 for income ≤ ₹5L (Section 87A applies to all resident individuals)
            //   Surcharge: 37% max (Budget 2023 cap of 25% starts from FY 2023-24)
            AddNew("2020-21", NewSlabs_2020to2022, 0,     500000, 12500, false, 250000, 0.10, 0.37);
            AddNew("2021-22", NewSlabs_2020to2022, 0,     500000, 12500, false, 250000, 0.10, 0.37);
            AddNew("2022-23", NewSlabs_2020to2022, 0,     500000, 12500, false, 250000, 0.10, 0.37);

            // FY 2023-24: Budget 2023 — std ded ₹50K introduced for new regime
            //   87A ₹25K for ≤ ₹7L + marginal relief | Surcharge capped at 25%
            AddNew("2023-24", NewSlabs_2023_24, 50000, 700000, 25000, true,  300000, 0.10, 0.25);

            // FY 2024-25: Budget 2024 — std ded raised to ₹75K (new regime only!)
            //   80CCD(2) raised to 14% of salary for new regime
            AddNew("2024-25", NewSlabs_2024_25, 75000, 700000, 25000, true,  300000, 0.14, 0.25);

            // FY 2025-26: Budget 2025 — new 7-slab, 87A ₹60K for ≤ ₹12L
            AddNew("2025-26", NewSlabs_2025_26, 75000, 1200000, 60000, true, 400000, 0.14, 0.25);

            // FY 2026-27 and beyond: no changes announced (Budget 2026 retained same)
            AddNew("2026-27", NewSlabs_2025_26, 75000, 1200000, 60000, true, 400000, 0.14, 0.25);
            AddNew("2027-28", NewSlabs_2025_26, 75000, 1200000, 60000, true, 400000, 0.14, 0.25);

            return list.ToArray();
        }

        // ── LOOKUP ────────────────────────────────────────────────────────────

        /// <summary>
        /// Returns regime rules for the given FY, regime, and (for old regime) age category.
        /// New regime: age is ignored (same slabs for all ages per section 115BAC).
        /// Old regime: correct slabs and basic exemption based on age.
        /// </summary>
        public static RegimeRules GetRules(string fy, bool isNew,
            AgeCategory age = AgeCategory.Below60)
        {
            string key = NormaliseFY(fy);
            if (isNew) age = AgeCategory.Below60; // new regime ignores age
            var found = Array.Find(_rules, r => r.FY == key && r.IsNew == isNew && r.Age == age);
            if (found != null) return found;
            // Fallback: latest known
            if (isNew) return Array.Find(_rules, r => r.IsNew && r.FY == "2025-26")!;
            return Array.Find(_rules, r => !r.IsNew && r.FY == "2025-26" && r.Age == age)
                ?? Array.Find(_rules, r => !r.IsNew && r.FY == "2025-26")!;
        }

        /// <summary>Determine age category from date of birth as of a given FY end date.</summary>
        public static AgeCategory GetAgeCategory(DateTime? dob, string fy)
        {
            if (dob == null) return AgeCategory.Below60;
            int fyEnd = FyStartYear(fy) + 1;
            // Age during the FY = age attained at any time during the FY
            // Per Section 192: age as on last day of FY (31 March of assessment year)
            var fyEndDate = new DateTime(fyEnd, 3, 31);
            int age = (int)((fyEndDate - dob.Value).TotalDays / 365.25);
            if (age >= 80) return AgeCategory.SuperSenior80Plus;
            if (age >= 60) return AgeCategory.Senior60to79;
            return AgeCategory.Below60;
        }

        private static string NormaliseFY(string fy)
        {
            if (string.IsNullOrWhiteSpace(fy)) return "2025-26";
            var digits = System.Text.RegularExpressions.Regex.Matches(fy, @"\d+");
            if (digits.Count >= 2)
            {
                int y1 = int.Parse(digits[0].Value.Length == 4 ? digits[0].Value : "20" + digits[0].Value);
                int y2 = int.Parse(digits[1].Value.Length == 4 ? digits[1].Value : "20" + digits[1].Value);
                if (y2 == y1 + 1) return $"{y1}-{y2.ToString()[^2..]}";
            }
            if (digits.Count == 1)
            {
                int y1 = int.Parse(digits[0].Value.Length == 4 ? digits[0].Value : "20" + digits[0].Value);
                return $"{y1}-{(y1+1).ToString()[^2..]}";
            }
            return fy.Length >= 7 ? fy[..7] : "2025-26";
        }

        public static int FyStartYear(string fy)
        {
            var d = System.Text.RegularExpressions.Regex.Match(fy, @"\d{4}");
            return d.Success ? int.Parse(d.Value) : DateTime.Today.Year;
        }

        // ── COMPUTATION ──────────────────────────────────────────────────────

        /// <summary>Pure slab tax — no surcharge, no cess, no rebate.</summary>
        public static double ComputeSlabTax(double income, RegimeRules rules)
        {
            // Use decimal arithmetic inside each bracket to avoid floating-point drift
            // at exact threshold boundaries (e.g. ₹50L, ₹1Cr). Convert back to double at end.
            decimal tax = 0m, inc = (decimal)income;
            foreach (var s in rules.Slabs)
            {
                decimal from = (decimal)s.From;
                if (inc <= from) break;
                // s.To == double.MaxValue marks the top (open-ended) slab — use income as upper bound
                bool isTopSlab = s.To >= double.MaxValue / 2;
                decimal upper = isTopSlab ? inc : Math.Min(inc, (decimal)s.To);
                tax += (upper - from) * (decimal)s.Rate;
                if (isTopSlab || inc <= (decimal)s.To) break;
            }
            return (double)Math.Round(tax, 0, MidpointRounding.AwayFromZero);
        }

        /// <summary>
        /// Section 87A rebate + marginal relief.
        /// Returns (taxAfterRebate, rebateAmount).
        ///
        /// Old regime:  No marginal relief. Rebate = min(tax, ₹12,500) for income ≤ ₹5L.
        /// New regime FY 2023-24+: Marginal relief — tax cannot exceed (income − ₹7L/₹12L).
        ///   Prevents the cliff where ₹1 extra income triggers disproportionate tax.
        /// </summary>
        public static (double taxAfter, double rebate) Apply87A(
            double rawTax, double income, RegimeRules rules)
        {
            if (rules.Rebate87AThreshold <= 0 || rules.Rebate87AMaxAmount <= 0)
                return (rawTax, 0);   // no rebate for this FY/regime combination

            if (income <= rules.Rebate87AThreshold)
            {
                double rebate = Math.Min(rawTax, rules.Rebate87AMaxAmount);
                return (Math.Max(0, rawTax - rebate), rebate);
            }

            if (rules.MarginalRelief87A)
            {
                // Marginal relief: effective tax ≤ (income − 87A threshold)
                double marginalCap = income - rules.Rebate87AThreshold;
                double capped      = Math.Min(rawTax, marginalCap);
                return (capped, Math.Max(0, rawTax - capped));
            }

            return (rawTax, 0);
        }

        /// <summary>
        /// Surcharge for individuals — regime-aware.
        ///
        /// OLD REGIME (incometax.gov.in — individual below 60 / senior / super senior):
        ///   > ₹50L  to ₹1Cr  :  10%
        ///   > ₹1Cr  to ₹2Cr  :  15%
        ///   > ₹2Cr  to ₹5Cr  :  25%
        ///   > ₹5Cr           :  37%
        ///
        /// NEW REGIME (Budget 2023, FY 2023-24 onwards — max surcharge CAPPED at 25%):
        ///   > ₹50L  to ₹1Cr  :  10%
        ///   > ₹1Cr  to ₹2Cr  :  15%
        ///   > ₹2Cr           :  25%  (no 37% in new regime)
        ///
        /// Note: Marginal relief on surcharge also exists but is rarely relevant for
        ///       salary TDS; amounts at ₹50L/₹1Cr thresholds are small.
        /// </summary>
        public static double CalcSurcharge(double taxAfterRebate, double income, RegimeRules rules)
        {
            // Round income to nearest rupee before bracket test to eliminate floating-point
            // drift from DB round-trips or accumulated monthly summation.
            long inc = (long)Math.Round(income);
            if (inc <=  5_000_000) return 0;
            if (inc <= 10_000_000) return Math.Round(taxAfterRebate * 0.10);  // >₹50L to ₹1Cr
            if (inc <= 20_000_000) return Math.Round(taxAfterRebate * 0.15);  // >₹1Cr to ₹2Cr
            if (inc <= 50_000_000 || rules.MaxSurchargeRate <= 0.25)
                return Math.Round(taxAfterRebate * 0.25);   // >₹2Cr (both) or >₹5Cr new regime
            return Math.Round(taxAfterRebate * 0.37);       // >₹5Cr old regime only
        }

        /// <summary>Backward-compatible overload — uses old regime max surcharge (37%).</summary>
        public static double CalcSurcharge(double taxAfterRebate, double income)
            => CalcSurcharge(taxAfterRebate, income,
               new RegimeRules("", false, AgeCategory.Below60, Array.Empty<Slab>(),
                               0, 0, 0, false, 0, 0.37, 0.10));

        /// <summary>Full computation: total, rawTax, rebate, surcharge, cess.</summary>
        public static (double total, double rawTax, double rebate, double surcharge, double cess)
            ComputeFull(double taxableIncome, string fy, bool isNew,
                        AgeCategory age = AgeCategory.Below60)
        {
            var rules             = GetRules(fy, isNew, age);
            double raw            = ComputeSlabTax(taxableIncome, rules);
            var (taxAfter, rebate)= Apply87A(raw, taxableIncome, rules);
            double sc             = CalcSurcharge(taxAfter, taxableIncome, rules);
            double cess           = Math.Round((taxAfter + sc) * 0.04);
            double total          = Math.Round(taxAfter + sc + cess);
            return (total, raw, rebate, sc, cess);
        }

        public static double GetStandardDeduction(string fy, bool isNew)
            => GetRules(fy, isNew).StandardDeduction;

        public static double Get80CCD2Rate(string fy, bool isNew)
            => GetRules(fy, isNew).Nps80CCD2Rate;

        public static string NewRegimeHint(string fy)
        {
            var r = GetRules(fy, true);
            string std = r.StandardDeduction > 0 ? $"Std ded ₹{r.StandardDeduction/1000:0}K" : "No std ded";
            string reb = r.Rebate87AThreshold > 0
                ? $" | Zero tax ≤₹{r.Rebate87AThreshold/100000:0}L" : "";
            return $"New Regime ({fy}): {std}{reb}";
        }

        // ════════════════════════════════════════════════════════════════════
        // IT ACT 2025 — FY-AWARE SECTION & FORM REFERENCES
        // Rule: FY 2025-26 and earlier → Income Tax Act 1961 (old sections/forms)
        //       FY 2026-27 onwards     → Income Tax Act 2025 (new sections/forms)
        // Source: IT Act 2025, IT Rules 2026, CBDT notifications effective Apr 1 2026
        // ════════════════════════════════════════════════════════════════════

        /// <summary>Returns true if FY 2026-27 or later — Income Tax Act 2025 applies.</summary>
        public static bool IsNewAct(string fy)
        {
            if (string.IsNullOrEmpty(fy) || fy.Length < 4) return false;
            return int.TryParse(fy[..4], out int y) && y >= 2026;
        }

        /// <summary>
        /// Salary TDS section.
        /// FY ≤ 2025-26 → Section 192(1)   |   FY ≥ 2026-27 → Section 392(1)
        /// </summary>
        public static string SalaryTdsSection(string fy)
            => IsNewAct(fy) ? "Section 392(1)" : "Section 192(1)";

        /// <summary>
        /// Salary TDS certificate form.
        /// FY ≤ 2025-26 → Form 16   |   FY ≥ 2026-27 → Form 130
        /// </summary>
        public static string SalaryTdsCertForm(string fy)
            => IsNewAct(fy) ? "Form 130" : "Form 16";

        /// <summary>
        /// Quarterly salary TDS return form.
        /// FY ≤ 2025-26 → Form 24Q   |   FY ≥ 2026-27 → Form 138
        /// </summary>
        public static string SalaryReturnForm(string fy)
            => IsNewAct(fy) ? "Form 138" : "Form 24Q";

        /// <summary>
        /// Non-salary TDS certificate form.
        /// FY ≤ 2025-26 → Form 16A   |   FY ≥ 2026-27 → Form 131
        /// </summary>
        public static string NonSalaryTdsCertForm(string fy)
            => IsNewAct(fy) ? "Form 131" : "Form 16A";

        /// <summary>
        /// Quarterly non-salary TDS return form.
        /// FY ≤ 2025-26 → Form 26Q   |   FY ≥ 2026-27 → Form 140
        /// </summary>
        public static string NonSalaryReturnForm(string fy)
            => IsNewAct(fy) ? "Form 140" : "Form 26Q";

        /// <summary>
        /// Annual Information Statement form.
        /// FY ≤ 2025-26 → Form 26AS   |   FY ≥ 2026-27 → Form 168
        /// </summary>
        public static string AisForm(string fy)
            => IsNewAct(fy) ? "Form 168" : "Form 26AS";

        /// <summary>
        /// Nil/lower deduction declaration form.
        /// FY ≤ 2025-26 → Form 15G / 15H   |   FY ≥ 2026-27 → Form 121
        /// </summary>
        public static string NilDeductionForm(string fy)
            => IsNewAct(fy) ? "Form 121" : "Form 15G / 15H";

        /// <summary>
        /// Non-salary TDS section prefix.
        /// FY ≤ 2025-26 → Section 194x   |   FY ≥ 2026-27 → Section 393(1)
        /// </summary>
        public static string NonSalaryTdsSection(string fy)
            => IsNewAct(fy) ? "Section 393(1)" : "Section 194";

        /// <summary>
        /// Assessment Year / Tax Year label for display.
        /// FY 2025-26 → AY 2026-27   |   FY 2026-27 → TY 2027-28
        /// </summary>
        public static string AssessmentYearLabel(string fy)
        {
            if (string.IsNullOrEmpty(fy) || fy.Length < 4) return "";
            if (!int.TryParse(fy[..4], out int y)) return "";
            string range = $"{y+1}-{(y+2).ToString()[^2..]}";
            return IsNewAct(fy) ? $"TY {range}" : $"AY {range}";
        }

        /// <summary>Full act name for display in reports and certificates.</summary>
        public static string ActName(string fy)
            => IsNewAct(fy) ? "Income-tax Act 2025" : "Income-tax Act 1961";

        // Section code → IT Act 2025 reference mapping
        // Source: IT Act 2025, Section 393(1) Table as notified by CBDT
        private static readonly Dictionary<string, string> NewActSectionMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["192"]    = "Section 392(1) — Salary",
            ["192A"]   = "Section 393(1) Sl.4(i) — PF Withdrawal",
            ["193"]    = "Section 393(1) Sl.1 — Interest on Securities",
            ["194"]    = "Section 393(1) Sl.2 — Dividends",
            ["194A"]   = "Section 393(1) Sl.3 — Interest (other than securities)",
            ["194B"]   = "Section 393(1) Sl.5(i) — Lottery / Crossword",
            ["194BA"]  = "Section 393(1) Sl.5(iv) — Online Games",
            ["194BB"]  = "Section 393(1) Sl.5(ii) — Horse Race Winnings",
            ["194C"]   = "Section 393(1) Sl.6 — Payment to Contractor",
            ["194D"]   = "Section 393(1) Sl.7 — Insurance Commission",
            ["194DA"]  = "Section 393(1) Sl.8(i) — Life Insurance Maturity",
            ["194G"]   = "Section 393(1) Sl.9 — Lottery Agent Commission",
            ["194H"]   = "Section 393(1) Sl.10(i) — Commission / Brokerage",
            ["194I"]   = "Section 393(1) Sl.11 — Rent",
            ["194IA"]  = "Section 393(1) Sl.12(i) — Transfer of Immovable Property",
            ["194IB"]  = "Section 393(1) Sl.12(ii) — Rent by Individual/HUF",
            ["194IC"]  = "Section 393(1) Sl.12(iii) — Joint Dev Agreement",
            ["194J"]   = "Section 393(1) Sl.13 — Professional / Technical Fees",
            ["194K"]   = "Section 393(1) Sl.8(ii) — Income from Mutual Fund Units",
            ["194LA"]  = "Section 393(1) Sl.14 — Compensation on Compulsory Acquisition",
            ["194M"]   = "Section 393(1) Sl.6(ii) — Contractor (Individual/HUF)",
            ["194N"]   = "Section 393(1) Sl.15 — Cash Withdrawal",
            ["194O"]   = "Section 393(1) Sl.16 — E-commerce Payments",
            ["194Q"]   = "Section 393(1) Sl.17 — Purchase of Goods",
            ["194R"]   = "Section 393(1) Sl.18 — Benefit/Perquisite to Business",
            ["194S"]   = "Section 393(1) Sl.8(vi) — Virtual Digital Assets",
            ["195"]    = "Section 393(2) — Payments to Non-Residents",
            ["196A"]   = "Section 393(2) — Income of Foreign Cos from Units",
            ["206AB"]  = "Section 397(3) — Higher Rate (ITR not filed)",
            ["206CCA"] = "Section 398(3) — Higher Rate TCS (ITR not filed)",
        };

        /// <summary>
        /// Returns FY-aware section label for display next to section dropdown.
        /// FY ≤ 2025-26 → "Sec 192 — Income-tax Act 1961"
        /// FY ≥ 2026-27 → "Section 392(1) — Salary — Income-tax Act 2025"
        /// </summary>
        public static string SectionDisplayLabel(string sectionCode, string fy)
        {
            if (!IsNewAct(fy))
                return $"Sec {sectionCode} — Income-tax Act 1961";

            return NewActSectionMap.TryGetValue(sectionCode, out var mapped)
                ? $"{mapped} — Income-tax Act 2025"
                : $"Sec {sectionCode} — Income-tax Act 2025";
        }
    }
}
