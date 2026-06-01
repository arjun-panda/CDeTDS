using CDeTDS.Common;
using CDeTDS.DAL.Models;

namespace CDeTDS.BLL
{
    /// <summary>
    /// Pure validation logic. No DB access. Returns a ValidationResult so the UI
    /// can show all errors together and the save path can refuse bad data.
    /// </summary>
    public static class PayrollValidators
    {
        // ── Employee ─────────────────────────────────────────────────────────
        public static ValidationResult ValidateEmployee(Employee e)
        {
            var v = new ValidationResult();
            if (e == null) { v.AddError("Employee is null."); return v; }

            if (string.IsNullOrWhiteSpace(e.EmployeeCode)) v.AddError("Employee Code is required.");
            if (string.IsNullOrWhiteSpace(e.Name))         v.AddError("Name is required.");
            if (e.Name?.Length > 100)                      v.AddError("Name is too long (max 100 chars).");

            // PAN mandatory + valid format
            if (string.IsNullOrWhiteSpace(e.Pan))
                v.AddError("PAN is required.");
            else if (!Validators.IsValidPan(e.Pan))
                v.AddError($"PAN '{e.Pan}' is invalid. Expected 5 letters + 4 digits + 1 letter (e.g. ABCDE1234F).");

            // DOB required + parseable + age sanity (15-120)
            if (string.IsNullOrWhiteSpace(e.DateOfBirth))
                v.AddError("Date of Birth is required.");
            else if (!Validators.TryParseAppDate(e.DateOfBirth, out var dob))
                v.AddError($"Date of Birth '{e.DateOfBirth}' is unrecognised.");
            else
            {
                int age = (int)((DateTime.Today - dob).TotalDays / 365.25);
                if (age < 15)  v.AddError($"Date of Birth makes age {age} — must be at least 15.");
                if (age > 120) v.AddError($"Date of Birth makes age {age} — likely a typo.");
            }

            // Join date required + parseable + must be on/before today
            if (string.IsNullOrWhiteSpace(e.JoinDate))
                v.AddError("Date of Joining is required.");
            else if (!Validators.TryParseAppDate(e.JoinDate, out var jd))
                v.AddError($"Date of Joining '{e.JoinDate}' is unrecognised.");
            else if (jd > DateTime.Today.AddDays(1))   // small tolerance for timezone
                v.AddError($"Date of Joining {jd:dd-MMM-yyyy} cannot be in the future.");

            // Leaving date (optional) must be ≥ JoinDate when present
            if (!string.IsNullOrWhiteSpace(e.LeavingDate))
            {
                if (!Validators.TryParseAppDate(e.LeavingDate, out var ld))
                    v.AddError($"Date of Leaving '{e.LeavingDate}' is unrecognised.");
                else if (Validators.TryParseAppDate(e.JoinDate, out var jd2) && ld < jd2)
                    v.AddError("Date of Leaving cannot be before Date of Joining.");
            }

            // Contact (warnings — not blocking)
            if (!string.IsNullOrWhiteSpace(e.Phone) && !Validators.IsValidMobile(e.Phone))
                v.AddWarning($"Phone '{e.Phone}' isn't a valid 10-digit Indian mobile.");
            if (!string.IsNullOrWhiteSpace(e.Email) && !Validators.IsValidEmail(e.Email))
                v.AddWarning($"Email '{e.Email}' looks malformed.");
            if (!string.IsNullOrWhiteSpace(e.BankIfsc) && !Validators.IsValidIfsc(e.BankIfsc))
                v.AddWarning($"IFSC '{e.BankIfsc}' isn't a valid IFSC code.");
            if (!string.IsNullOrWhiteSpace(e.AadhaarNumber) && !Validators.IsValidAadhaar(e.AadhaarNumber))
                v.AddWarning($"Aadhaar '{e.AadhaarNumber}' isn't 12 digits starting 2-9.");
            if (!string.IsNullOrWhiteSpace(e.PinCode) && !Validators.IsValidPin(e.PinCode))
                v.AddWarning($"PIN '{e.PinCode}' isn't a valid 6-digit Indian pincode.");

            // Tax regime sanity
            if (e.TaxRegime != "Old" && e.TaxRegime != "New")
                v.AddError($"Tax Regime '{e.TaxRegime}' must be Old or New.");

            // HRA city type
            if (!string.IsNullOrWhiteSpace(e.HraCityType)
                && e.HraCityType != "Metro" && e.HraCityType != "Non-Metro")
                v.AddError($"HRA City Type '{e.HraCityType}' must be Metro or Non-Metro.");

            // Salary structure (delegate)
            if (e.Salary != null)
                v.Merge(ValidateSalaryStructure(e.Salary));

            return v;
        }

        // ── SalaryStructure ──────────────────────────────────────────────────
        public static ValidationResult ValidateSalaryStructure(SalaryStructure s)
        {
            var v = new ValidationResult();
            if (s == null) { v.AddError("Salary structure is null."); return v; }

            // Non-negative checks
            if (s.Basic < 0)            v.AddError("Basic Salary cannot be negative.");
            if (s.Hra < 0)              v.AddError("HRA cannot be negative.");
            if (s.Da < 0)               v.AddError("DA cannot be negative.");
            if (s.SpecialAllowance < 0) v.AddError("Special Allowance cannot be negative.");
            if (s.MedicalAllowance < 0) v.AddError("Medical Allowance cannot be negative.");
            if (s.Lta < 0)              v.AddError("LTA cannot be negative.");
            if (s.OtherAllowance < 0)   v.AddError("Other Allowance cannot be negative.");
            if (s.PfFixedAmount < 0)    v.AddError("PF Fixed Amount cannot be negative.");
            if (s.TargetCtc < 0)        v.AddError("Target CTC cannot be negative.");

            // Sanity caps — guard against typos like ₹1e9
            if (s.Basic > 100_000_000)  v.AddError("Basic Salary exceeds ₹10 crore — check entry.");
            if (s.TargetCtc > 100_000_000) v.AddError("Target CTC exceeds ₹10 crore — check entry.");

            // HRA cannot exceed Basic (legally HRA exemption capped at Basic anyway; flag oddity)
            if (s.Hra > s.Basic * 2 && s.Basic > 0)
                v.AddWarning("HRA is more than 2× Basic — unusual.");

            // CTC reconciliation (when anchored): gross + employer contributions ≤ TargetCtc ± ₹100
            if (s.TargetCtc > 0)
            {
                double empPf = s.PfApplicable
                    ? (s.PfFixedAmount > 0 ? s.PfFixedAmount : (s.Basic > 15000 ? 1800 : Math.Round(s.Basic * 0.12)))
                    : 0;
                double grat  = s.IncludeGratuity ? Math.Round(s.Basic * 0.0481) : 0;
                double total = s.Basic + s.Hra + s.Da + s.SpecialAllowance + s.Lta + s.ComponentsReceived() + empPf + grat;
                double diff  = Math.Abs(total - s.TargetCtc);
                if (diff > 100)
                    v.AddWarning($"CTC reconciliation is off by ₹{diff:N0} (computed ₹{total:N0} vs target ₹{s.TargetCtc:N0}).");
            }

            // Component validation
            foreach (var c in s.Components)
            {
                if (string.IsNullOrWhiteSpace(c.Name))
                    v.AddError("Component has empty name.");
                if (c.Received < 0) v.AddError($"Component '{c.Name}' Received cannot be negative.");
                if (c.Paid < 0)     v.AddError($"Component '{c.Name}' Paid cannot be negative.");
                if (c.Taxable < 0)  v.AddError($"Component '{c.Name}' Taxable cannot be negative.");
                if (c.Paid > c.Received)
                    v.AddWarning($"Component '{c.Name}' Paid (₹{c.Paid:N0}) > Received (₹{c.Received:N0}) — exempt portion exceeds amount received.");
            }

            return v;
        }

        // ── MonthlySalaryEntry ───────────────────────────────────────────────
        public static ValidationResult ValidateMonthlyEntry(MonthlySalaryEntry e)
        {
            var v = new ValidationResult();
            if (e == null) { v.AddError("Salary entry is null."); return v; }

            if (e.Month < 1 || e.Month > 12)   v.AddError($"Month {e.Month} out of range 1-12.");
            if (e.Year < 2000 || e.Year > 2100) v.AddError($"Year {e.Year} out of range.");
            if (string.IsNullOrWhiteSpace(e.FinancialYear)) v.AddError("Financial Year is required.");

            // Non-negative core fields
            if (e.Basic < 0)          v.AddError("Basic cannot be negative.");
            if (e.HRA < 0)            v.AddError("HRA cannot be negative.");
            if (e.DaAmount < 0)       v.AddError("DA cannot be negative.");
            if (e.SpecialAllowance < 0) v.AddError("Special Allowance cannot be negative.");
            if (e.Lta < 0)            v.AddError("LTA cannot be negative.");
            if (e.OtherAllowances < 0) v.AddError("Other Allowances cannot be negative.");
            if (e.Bonus < 0)          v.AddError("Bonus cannot be negative.");
            if (e.PfEmployee < 0)     v.AddError("PF cannot be negative.");
            if (e.TdsDeducted < 0)    v.AddError("TDS Deducted cannot be negative.");
            if (e.NetSalary < 0)
                v.AddWarning("Net salary is negative — deductions exceed gross.");

            // Days worked sanity
            if (e.WorkingDays > 0)
            {
                if (e.DaysWorked < 0 || e.DaysWorked > e.WorkingDays)
                    v.AddError($"Days worked {e.DaysWorked} out of range 0-{e.WorkingDays}.");
                if (e.LopDays < 0 || e.LopDays > e.WorkingDays)
                    v.AddError($"LOP days {e.LopDays} out of range 0-{e.WorkingDays}.");
            }

            return v;
        }
    }
}
