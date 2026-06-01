using CDeTDS.DAL;
using CDeTDS.DAL.Models;

namespace CDeTDS.BLL
{
    public class DeductionScheduleService
    {
        private readonly DeductionScheduleRepository _repo = new();

        public List<DeductionSchedule> GetActive(int employeeId)
            => _repo.GetActive(employeeId);

        public List<DeductionSchedule> GetAll(int deductorId)
            => _repo.GetAll(deductorId);

        public (bool ok, string msg) Save(DeductionSchedule s)
        {
            if (s.TotalAmount <= 0)
                return (false, "Total amount must be greater than zero.");
            if (s.InstallmentAmt <= 0 && s.TotalInstallments <= 0)
                return (false, "Enter either a fixed installment amount or number of installments.");
            if (string.IsNullOrWhiteSpace(s.StartFy))
                return (false, "Start FY is required.");
            _repo.Save(s);
            return (true, "Saved.");
        }

        public void Close(int id) => _repo.Close(id);

        /// <summary>
        /// Records actual recovered amount for each active schedule this month.
        /// Uses the actual varDed line item amount saved in salary (supports manual overrides /
        /// extra repayments). Falls back to ThisInstallment if no line item found.
        /// </summary>
        public List<(DeductionSchedule Schedule, double Posted)> PostInstallments(
            int employeeId, int deductorId, string fy, int month)
        {
            var posted = new List<(DeductionSchedule, double)>();
            var schedules = _repo.GetActive(employeeId);
            if (!schedules.Any()) return posted;

            // Load actual line items saved for this employee+month to get real recovered amounts
            var salRepo  = new SalaryRepository();
            var entry    = salRepo.Get(employeeId, fy, month);
            var lineItems = entry != null ? salRepo.GetLineItems(entry.Id) : new List<SalaryLineItem>();

            foreach (var s in schedules)
            {
                if (!s.IsDue(fy, month)) continue;

                // Use actual amount from saved varDed line item (RuleRef = schedule ID)
                var li = lineItems.FirstOrDefault(l =>
                    l.Category == "varDed" && l.RuleRef == s.Id.ToString());
                double amount = li != null ? li.Taxable : s.ThisInstallment;

                if (amount <= 0) continue;
                _repo.UpdateRecovered(s.Id, amount, fy, month);
                posted.Add((s, amount));
            }
            return posted;
        }

        /// <summary>
        /// Returns varDed line items that should be auto-seeded for a given month.
        /// Used by the salary data UI to pre-populate variable deductions.
        /// </summary>
        public List<SalaryLineItem> GetDueLineItems(int employeeId, string fy, int month)
        {
            var items = new List<SalaryLineItem>();
            var schedules = _repo.GetActive(employeeId);
            foreach (var s in schedules)
            {
                if (!s.IsDue(fy, month)) continue;
                double amount = s.ThisInstallment;
                if (amount <= 0) continue;
                items.Add(new SalaryLineItem
                {
                    Category = "varDed",
                    Name     = string.IsNullOrWhiteSpace(s.Description) ? s.Type : $"{s.Type}: {s.Description}",
                    Taxable  = amount,
                    RuleRef  = s.Id.ToString(), // store schedule ID for tracking
                });
            }
            return items;
        }
    }
}
