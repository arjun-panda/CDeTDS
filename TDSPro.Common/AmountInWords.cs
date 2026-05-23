namespace TDSPro.Common
{
    /// <summary>
    /// Convert a rupee amount to Indian-English words (Lakh/Crore grouping).
    /// Used on salary slips and challans where amount-in-words is conventional.
    /// </summary>
    public static class AmountInWords
    {
        private static readonly string[] Units =
        {
            "Zero","One","Two","Three","Four","Five","Six","Seven","Eight","Nine",
            "Ten","Eleven","Twelve","Thirteen","Fourteen","Fifteen","Sixteen",
            "Seventeen","Eighteen","Nineteen"
        };
        private static readonly string[] Tens =
        {
            "","","Twenty","Thirty","Forty","Fifty","Sixty","Seventy","Eighty","Ninety"
        };

        /// <summary>"Rupees Two Lakh Thirty-Four Thousand Five Hundred Sixty-Seven Only"</summary>
        public static string Rupees(double amount)
        {
            if (amount == 0) return "Rupees Zero Only";
            long whole = (long)Math.Floor(Math.Abs(amount));
            int  paise = (int)Math.Round((Math.Abs(amount) - whole) * 100);
            string sign = amount < 0 ? "Minus " : "";

            string rupeePart = "Rupees " + IndianGroup(whole);
            string paisePart = paise > 0 ? " and " + Below100(paise) + " Paise" : "";
            return sign + rupeePart + paisePart + " Only";
        }

        // Indian numbering: crore / lakh / thousand / hundred
        private static string IndianGroup(long n)
        {
            if (n == 0) return "Zero";
            var sb = new System.Text.StringBuilder();
            long crore     = n / 10000000;     n %= 10000000;
            long lakh      = n / 100000;        n %= 100000;
            long thousand  = n / 1000;          n %= 1000;
            long hundred   = n / 100;           n %= 100;

            if (crore    > 0) sb.Append(IndianGroup(crore)).Append(" Crore ");
            if (lakh     > 0) sb.Append(IndianGroup(lakh)).Append(" Lakh ");
            if (thousand > 0) sb.Append(IndianGroup(thousand)).Append(" Thousand ");
            if (hundred  > 0) sb.Append(Below100(hundred)).Append(" Hundred ");
            if (n > 0)
            {
                if (sb.Length > 0) sb.Append("and ");
                sb.Append(Below100(n));
            }
            return sb.ToString().Trim();
        }

        private static string Below100(long n)
        {
            if (n < 20) return Units[n];
            int tens = (int)(n / 10), ones = (int)(n % 10);
            return ones == 0 ? Tens[tens] : Tens[tens] + "-" + Units[ones];
        }
    }
}
