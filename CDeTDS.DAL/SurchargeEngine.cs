namespace CDeTDS.DAL
{
    /// <summary>
    /// Surcharge and effective TDS rate calculator for Section 192 (Salary).
    /// Surcharge applies to individuals with income above specified thresholds.
    ///
    /// Income-tax Act 2025 — FY 2025-26 surcharge slabs:
    ///   Rs 50L–1Cr    →  10% surcharge on income tax
    ///   Rs 1Cr–2Cr    →  15% surcharge
    ///   Rs 2Cr–5Cr    →  25% surcharge
    ///   Above Rs 5Cr  →  37% surcharge  (subject to marginal relief)
    ///
    /// Health & Education Cess: 4% on (income tax + surcharge)
    /// </summary>
    public static class SurchargeEngine
    {
        // ── Surcharge slabs FY 2025-26 ────────────────────────────────────────
        private static readonly (double IncomeAbove, double SurchargeRate)[] SlabsFY2526 =
        {
            (50_00_000,  10.0),   // Rs 50 lakh
            (1_00_00_000,15.0),   // Rs 1 crore
            (2_00_00_000,25.0),   // Rs 2 crore
            (5_00_00_000,37.0),   // Rs 5 crore
        };

        public static SurchargeResult Calculate(
            double taxableIncome,
            double incomeTax,
            string fy = "2025-26",
            bool individualAssessee = true)
        {
            var result = new SurchargeResult
            {
                TaxableIncome    = taxableIncome,
                IncomeTax        = incomeTax,
                SurchargeRate    = 0,
                SurchargeAmount  = 0,
                CessRate         = 4.0,
            };

            if (!individualAssessee || taxableIncome <= 50_00_000)
            {
                // No surcharge below Rs 50 lakh
                result.CessAmount = Math.Round(incomeTax * 0.04, 2);
                result.TotalTax   = Math.Round(incomeTax + result.CessAmount, 2);
                return result;
            }

            // Determine applicable surcharge rate
            var slabs = SlabsFY2526;
            double surchargeRate = 0;
            foreach (var (threshold, rate) in slabs.Reverse())
            {
                if (taxableIncome > threshold)
                { surchargeRate = rate; break; }
            }

            double surcharge = Math.Round(incomeTax * surchargeRate / 100, 2);

            // Marginal relief: net tax + surcharge + cess at current slab cannot exceed
            // net tax + surcharge + cess at the lower threshold + income above that threshold.
            // Applied at every slab boundary per CBDT clarification.
            double marginalRelief = 0;
            (double threshold, double lowerRate)[] boundaries =
            {
                (50_00_000,   0.0),   // crossing into 10% slab
                (1_00_00_000, 10.0),  // crossing into 15% slab
                (2_00_00_000, 15.0),  // crossing into 25% slab
                (5_00_00_000, 25.0),  // crossing into 37% slab
            };
            foreach (var (boundary, lowerSurchRate) in boundaries)
            {
                if (taxableIncome <= boundary) break;

                // Tax liability at exact boundary (with lower surcharge rate)
                // We approximate income tax at boundary as: incomeTax × (boundary / taxableIncome)
                // This is an approximation valid for proportional slabs; precise only when tax is linear.
                // For a rigorous implementation the caller should pass tax-at-boundary separately.
                double taxAtBoundary      = incomeTax * boundary / taxableIncome;
                double surchargeAtBoundary = Math.Round(taxAtBoundary * lowerSurchRate / 100, 2);
                double cessAtBoundary     = Math.Round((taxAtBoundary + surchargeAtBoundary) * 0.04, 2);
                double totalAtBoundary    = taxAtBoundary + surchargeAtBoundary + cessAtBoundary;

                // Current total before marginal relief
                double currentTotal = incomeTax + surcharge + Math.Round((incomeTax + surcharge) * 0.04, 2);

                // Relief = amount by which current total exceeds (totalAtBoundary + excess income)
                double excessIncome = taxableIncome - boundary;
                double cap = totalAtBoundary + excessIncome;
                if (currentTotal > cap)
                {
                    double relief = currentTotal - cap;
                    marginalRelief = Math.Max(marginalRelief, Math.Round(relief, 2));
                }
            }
            surcharge = Math.Max(0, Math.Round(surcharge - marginalRelief, 2));

            double cess  = Math.Round((incomeTax + surcharge) * 0.04, 2);
            double total = incomeTax + surcharge + cess;

            result.SurchargeRate    = surchargeRate;
            result.SurchargeAmount  = surcharge;
            result.MarginalRelief   = marginalRelief;
            result.CessAmount       = cess;
            result.TotalTax         = Math.Round(total, 2);
            result.EffectiveTdsRate = taxableIncome > 0
                ? Math.Round(total / taxableIncome * 100, 4)
                : 0;

            return result;
        }

        /// <summary>
        /// Monthly TDS for a salaried employee (Section 192).
        /// Divides annual tax liability over remaining months of the FY.
        /// </summary>
        public static double MonthlyTds(
            double annualTaxableIncome,
            double annualIncomeTax,
            int remainingMonths = 12,
            string fy = "2025-26")
        {
            var result = Calculate(annualTaxableIncome, annualIncomeTax, fy);
            if (remainingMonths <= 0) return 0;
            return Math.Round(result.TotalTax / remainingMonths, 2);
        }
    }

    public class SurchargeResult
    {
        public double TaxableIncome   { get; set; }
        public double IncomeTax       { get; set; }
        public double SurchargeRate   { get; set; }
        public double SurchargeAmount { get; set; }
        public double MarginalRelief  { get; set; }
        public double CessRate        { get; set; } = 4.0;
        public double CessAmount      { get; set; }
        public double TotalTax        { get; set; }
        public double EffectiveTdsRate{ get; set; }

        public override string ToString() =>
            $"Income Tax: Rs {IncomeTax:N2}  |  " +
            $"Surcharge ({SurchargeRate}%): Rs {SurchargeAmount:N2}  |  " +
            $"Cess (4%): Rs {CessAmount:N2}  |  " +
            $"Total: Rs {TotalTax:N2}  |  Effective Rate: {EffectiveTdsRate:F4}%";
    }
}
