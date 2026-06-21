using System.Text.RegularExpressions;

namespace CDeTDS.Common
{
    public static class Validators
    {
        // 4th char = holder type: P(individual) C(company) H(HUF) F(firm) A(AOP)
        // T(trust) B(BOI) L(local authority) J(artificial juridical person) G(govt).
        private static readonly Regex PanRegex = new(@"^[A-Z]{3}[ABCFGHJLPT][A-Z][0-9]{4}[A-Z]$");
        private static readonly Regex TanRegex = new(@"^[A-Z]{4}[0-9]{5}[A-Z]$");
        private static readonly Regex AadhaarRegex = new(@"^[2-9]\d{11}$");
        private static readonly Regex IfscRegex    = new(@"^[A-Z]{4}0[A-Z0-9]{6}$");
        private static readonly Regex PinRegex     = new(@"^[1-9]\d{5}$");
        private static readonly Regex MobileRegex  = new(@"^[6-9]\d{9}$");
        private static readonly Regex EmailRegex   = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        // ── Additional ID validators (added for B3 validation layer) ─────────
        public static bool IsValidAadhaar(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var digits = new string(s.Where(char.IsDigit).ToArray());
            return AadhaarRegex.IsMatch(digits);
        }
        public static bool IsValidIfsc(string? s) => !string.IsNullOrWhiteSpace(s) && IfscRegex.IsMatch(s.Trim().ToUpper());
        public static bool IsValidPin(string? s)  => !string.IsNullOrWhiteSpace(s) && PinRegex.IsMatch(s.Trim());
        public static bool IsValidMobile(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            var digits = new string(s.Where(char.IsDigit).ToArray());
            // Strip +91 / 91 / 0 prefixes so "+91 98765 43210", "0987654321" all normalise
            if (digits.Length == 12 && digits.StartsWith("91")) digits = digits.Substring(2);
            else if (digits.Length == 11 && digits.StartsWith("0")) digits = digits.Substring(1);
            return MobileRegex.IsMatch(digits);
        }
        public static bool IsValidEmail(string? s) => !string.IsNullOrWhiteSpace(s) && EmailRegex.IsMatch(s.Trim());

        /// <summary>Parses dates accepted by the app (dd-MMM-yyyy preferred but flexible).</summary>
        public static bool TryParseAppDate(string? s, out DateTime dt)
        {
            dt = default;
            if (string.IsNullOrWhiteSpace(s)) return false;
            return DateTime.TryParseExact(s.Trim(), "dd-MMM-yyyy",
                       System.Globalization.CultureInfo.InvariantCulture,
                       System.Globalization.DateTimeStyles.None, out dt)
                || DateTime.TryParse(s.Trim(), out dt);
        }

        // ── PAN ───────────────────────────────────────────────────────────────
        public static bool IsValidPan(string? pan)
        {
            if (string.IsNullOrWhiteSpace(pan)) return false;
            return PanRegex.IsMatch(pan.Trim().ToUpper());
        }

        public static string GetPanError(string pan)
        {
            if (string.IsNullOrWhiteSpace(pan)) return "PAN is required.";
            pan = pan.Trim().ToUpper();
            if (pan.Length != 10)    return $"PAN must be 10 characters (entered {pan.Length}).";
            // Specific message when only the 4th (holder-type) char is invalid.
            if (Regex.IsMatch(pan, @"^[A-Z]{3}[A-Z][A-Z][0-9]{4}[A-Z]$") && !PanRegex.IsMatch(pan))
                return $"Invalid PAN: 4th character '{pan[3]}' is not a valid holder type " +
                       "(must be one of P C H F A T B L J G). Example: ABCPE1234F";
            if (!PanRegex.IsMatch(pan)) return "Invalid PAN format. Example: ABCPE1234F";
            return string.Empty;
        }

        /// <summary>
        /// Decode entity type from 4th character of PAN (IT Act 2025).
        /// </summary>
        public static string GetPanEntityType(string pan)
        {
            if (!IsValidPan(pan)) return "";
            return pan.Trim().ToUpper()[3] switch
            {
                'P' => "Individual (Person)",
                'H' => "HUF",
                'C' => "Company",
                'F' => "Firm / LLP",
                'A' => "AOP",
                'T' => "Trust",
                'B' => "BOI",
                'L' => "Local Authority",
                'J' => "Artificial Juridical Person",
                'G' => "Government",
                _   => "Unknown Entity"
            };
        }

        /// <summary>
        /// Maps PAN 4th character to the DeducteeType string used in AppConstants.DeducteeTypes.
        /// Returns "" if PAN is incomplete (less than 4 chars) — no auto-set yet.
        /// </summary>
        public static string PanToDeducteeType(string pan)
        {
            if (string.IsNullOrWhiteSpace(pan) || pan.Length < 4) return "";
            return char.ToUpper(pan[3]) switch
            {
                'P' => "Individual",
                'H' => "HUF",
                'F' => "Firm",
                'C' => "Company",
                'A' => "AOP",
                'B' => "BOI",
                'T' => "Trust",
                'G' => "Government",
                'J' => "AOP",        // Artificial Juridical Person → closest is AOP
                'L' => "Government", // Local Authority → Government
                _   => "Other"
            };
        }

        // ── TAN ───────────────────────────────────────────────────────────────
        public static bool IsValidTan(string tan)
        {
            if (string.IsNullOrWhiteSpace(tan)) return false;
            return TanRegex.IsMatch(tan.Trim().ToUpper());
        }

        public static string GetTanError(string tan)
        {
            if (string.IsNullOrWhiteSpace(tan)) return "TAN is required.";
            tan = tan.Trim().ToUpper();
            if (tan.Length != 10)    return $"TAN must be 10 characters (entered {tan.Length}).";
            if (!TanRegex.IsMatch(tan)) return "Invalid TAN format. Example: DELA12345A";
            return string.Empty;
        }

        // ── Amount ────────────────────────────────────────────────────────────
        public static bool IsValidAmount(string amount, out double value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(amount)) return false;
            return double.TryParse(amount.Replace(",", "").Trim(), out value) && value >= 0;
        }

        // ── Date ──────────────────────────────────────────────────────────────
        public static bool IsValidDate(string date, out DateTime value)
        {
            value = DateTime.Today;
            if (string.IsNullOrWhiteSpace(date)) return false;
            return DateTime.TryParseExact(date.Trim(),
                new[] { "dd-MM-yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "d/M/yyyy", "ddMMyyyy" },
                null, System.Globalization.DateTimeStyles.None, out value);
        }

        /// <summary>
        /// Returns TDS quarter ("Q1"/"Q2"/"Q3"/"Q4") for a date within a given FY.
        /// Q1=Apr-Jun, Q2=Jul-Sep, Q3=Oct-Dec, Q4=Jan-Mar.
        /// </summary>
        public static string DateToQuarter(DateTime dt, string fy)
        {
            var parts = fy.Split('-');
            if (parts.Length < 1 || !int.TryParse(parts[0], out int y)) y = dt.Year;
            if (dt >= new DateTime(y,   4, 1) && dt <= new DateTime(y,   6, 30)) return "Q1";
            if (dt >= new DateTime(y,   7, 1) && dt <= new DateTime(y,   9, 30)) return "Q2";
            if (dt >= new DateTime(y,  10, 1) && dt <= new DateTime(y,  12, 31)) return "Q3";
            if (dt >= new DateTime(y+1, 1, 1) && dt <= new DateTime(y+1, 3, 31)) return "Q4";
            return "";
        }

        // ── TDS Calculations (called AFTER rules engine returns rate) ─────────
        public static double CalculateTds(double amount, double rate)
            => Math.Round(amount * rate / 100, 0);

        public static double CalculateCess(double tds, double cessRate = 4.0)
            => Math.Round(tds * cessRate / 100, 2);

        public static double CalculateSurcharge(double tds, double surchargeRate)
            => Math.Round(tds * surchargeRate / 100, 2);

        /// <summary>
        /// Simple interest for late deduction: 1% per month from deduction date.
        /// Late deposit: 1.5% per month from deduction date to deposit date.
        /// </summary>
        public static double CalculateInterest(
            double tdsAmount, DateTime deductionDate, DateTime paymentDate,
            bool lateDeduction = false)
        {
            if (paymentDate <= deductionDate) return 0;
            int months = ((paymentDate.Year - deductionDate.Year) * 12)
                       + paymentDate.Month - deductionDate.Month;
            if (months < 1) months = 1;
            double rate = lateDeduction ? 1.0 : 1.5; // 1% deduction, 1.5% deposit
            return Math.Round(tdsAmount * rate / 100 * months, 2);
        }

        /// <summary>
        /// Late filing fee u/s 234E: Rs 200 per day, max = TDS amount.
        /// </summary>
        public static double CalculateLateFee(double tdsAmount, DateTime dueDate, DateTime filingDate)
        {
            if (filingDate <= dueDate) return 0;
            int days = (filingDate - dueDate).Days;
            return Math.Min(Math.Round(days * 200.0, 2), tdsAmount);
        }
    }
}
