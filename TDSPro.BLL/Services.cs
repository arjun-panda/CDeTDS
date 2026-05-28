using TDSPro.Common;
using TDSPro.DAL;
using TDSPro.DAL.Models;
using TDSPro.DAL.Repositories;

namespace TDSPro.BLL
{
    // ── Deductor Service ──────────────────────────────────────────────────────
    public class DeductorService
    {
        private readonly DeductorRepository _repo = new();

        public List<Deductor> GetAll() => _repo.GetAll();
        public Deductor? GetById(int id) => _repo.GetById(id);

        public (bool Ok, string Msg) Save(Deductor d)
        {
            var tanErr = Validators.GetTanError(d.Tan);
            if (!string.IsNullOrEmpty(tanErr)) return (false, tanErr);

            if (string.IsNullOrWhiteSpace(d.Pan))
                return (false, "PAN is required.");
            var panErr = Validators.GetPanError(d.Pan);
            if (!string.IsNullOrEmpty(panErr)) return (false, panErr);

            if (string.IsNullOrWhiteSpace(d.CompanyName))
                return (false, "Company name is required.");

            var result = _repo.Save(d);
            if (result.Ok)
                Database.LogAction("system", d.Id == 0 ? "CREATE" : "UPDATE",
                    "Deductor", $"{d.CompanyName} / {d.Tan}");
            return result;
        }

        public (bool Ok, string Msg) Delete(int id)
        {
            var d = _repo.GetById(id);
            var result = _repo.Delete(id);
            if (result.Ok && d != null)
                Database.LogAction("system", "DELETE", "Deductor", d.CompanyName);
            return result;
        }

        // Counts dependent records — used by the delete confirmation to show impact
        public (int Entries, int Challans) GetImpactCounts(int deductorId)
        {
            using var conn = Database.GetConnection();
            using var c1 = conn.CreateCommand();
            c1.CommandText = "SELECT COUNT(*) FROM tds_entries WHERE deductor_id=@id";
            c1.Parameters.AddWithValue("@id", deductorId);
            var entries = Convert.ToInt32(c1.ExecuteScalar() ?? 0);

            using var c2 = conn.CreateCommand();
            c2.CommandText = "SELECT COUNT(*) FROM challans WHERE deductor_id=@id";
            c2.Parameters.AddWithValue("@id", deductorId);
            var challans = Convert.ToInt32(c2.ExecuteScalar() ?? 0);

            return (entries, challans);
        }

        // Lists soft-deleted deductors (is_active=0) for restore UI
        public List<Deductor> GetDeleted()
        {
            var list = new List<Deductor>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM deductors WHERE is_active=0 ORDER BY company_name";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Deductor
                {
                    Id = r.GetInt32(r.GetOrdinal("id")),
                    CompanyName = r.GetString(r.GetOrdinal("company_name")),
                    Tan = r.GetString(r.GetOrdinal("tan")),
                    Pan = r.GetString(r.GetOrdinal("pan")),
                });
            }
            return list;
        }

        public (bool Ok, string Msg) Restore(int id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE deductors SET is_active=1 WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                var rows = cmd.ExecuteNonQuery();
                if (rows == 0) return (false, "Company not found.");
                Database.LogAction("system", "RESTORE", "Deductor", id.ToString());
                return (true, "Company restored.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }
    }

    // ── Deductee Service ──────────────────────────────────────────────────────
    public class DeducteeService
    {
        private readonly DeducteeRepository _repo = new();

        public List<Deductee> GetAll(bool includeEmployees = false) => _repo.GetAll(includeEmployees);
        public Deductee? GetById(int id) => _repo.GetById(id);

        public (bool Ok, string Msg) Save(Deductee d)
        {
            if (string.IsNullOrWhiteSpace(d.Name))
                return (false, "Deductee name is required.");

            var panErr = Validators.GetPanError(d.Pan);
            if (!string.IsNullOrEmpty(panErr)) return (false, panErr);

            if (string.IsNullOrWhiteSpace(d.Section))
                return (false, "TDS Section is required.");

            var result = _repo.Save(d);
            if (result.Ok)
                Database.LogAction("system", d.Id == 0 ? "CREATE" : "UPDATE",
                    "Deductee", $"{d.Name} / {d.Pan}");
            return result;
        }

        public (bool Ok, string Msg) Delete(int id)
        {
            var d = _repo.GetById(id);
            var result = _repo.Delete(id);
            if (result.Ok && d != null)
                Database.LogAction("system", "DELETE", "Deductee", d.Name);
            return result;
        }

        public double GetDefaultRate(string section, string deducteeType)
        {
            // Use dynamic rules engine (no hardcoded rates)
            try
            {
                var engine = new TdsRulesEngine();
                var rule   = engine.GetApplicableRule(section, deducteeType, true, DateTime.Today);
                return rule?.TdsRate ?? 0;
            }
            catch { return 0; }
        }
    }

    // ── TDS Entry Service ─────────────────────────────────────────────────────
    public class TdsEntryService
    {
        private readonly TdsEntryRepository _repo = new();

        public List<TdsEntry> GetAll(int? deductorId = null, string? fy = null)
            => _repo.GetAll(deductorId, fy);

        public (bool Ok, string Msg) Save(TdsEntry e)
        {
            if (e.DeductorId <= 0) return (false, "Please select a deductor.");
            if (e.DeducteeId <= 0) return (false, "Please select a deductee.");
            if (string.IsNullOrWhiteSpace(e.Section)) return (false, "Section is required.");
            if (e.Amount <= 0) return (false, "Amount must be greater than 0.");

            // Only fill rate from rules engine if caller did not supply one
            if (e.Rate <= 0)
            {
                var rulesSvc  = new TdsRulesService();
                var calcResult = rulesSvc.Calculate(e.Section, e.Amount,
                    deducteeType: "Individual", panAvailable: true, itrFiled: true, txDate: e.EntryDate);
                if (calcResult.ApplicableRate > 0)
                    e.Rate = calcResult.ApplicableRate;
            }

            // Recalculate TDS only if caller left it at 0
            if (e.TdsAmount <= 0 && e.Amount > 0 && e.Rate > 0)
                e.TdsAmount = Validators.CalculateTds(e.Amount, e.Rate);

            // Sec 192 salary TDS: cess is already baked into TdsAmount by the payroll engine.
            // Never store cess as a separate field for salary — zero it out.
            if (e.Section is "192" or "192A" or "192B")
            { e.Surcharge = 0; e.Cess = 0; }
            else if (e.Section is not "195")
                e.Cess = 0;

            e.TotalTds = e.TdsAmount + e.Surcharge + e.Cess + e.Interest + e.LateFee;

            var result = _repo.Save(e);
            if (result.Ok)
                Database.LogAction("system", e.Id == 0 ? "CREATE" : "UPDATE",
                    "TdsEntry", $"{e.EntryNo} / Rs {e.TotalTds:N2}");
            return result;
        }

        public (bool Ok, string Msg) Delete(int id)
        {
            var result = _repo.Delete(id);
            if (result.Ok)
                Database.LogAction("system", "DELETE", "TdsEntry", id.ToString());
            return result;
        }

        public (bool Ok, string Msg) MarkAdjusted(int id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "UPDATE tds_entries SET status='Adjusted' WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                int rows = cmd.ExecuteNonQuery();
                if (rows == 0) return (false, "Entry not found.");
                Database.LogAction("system", "UPDATE", "TdsEntry", $"id={id} marked Adjusted");
                return (true, "");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public List<TdsEntry> GetUnlinkedByQuarter(int deductorId, string fy, string quarter)
            => _repo.GetAll(deductorId, fy)
                    .Where(e => e.Quarter == quarter && string.IsNullOrEmpty(e.ChallanNo))
                    .ToList();

        public List<TdsEntry> GetByQuarter(int deductorId, string fy, string quarter)
            => _repo.GetAll(deductorId, fy)
                    .Where(e => e.Quarter == quarter)
                    .ToList();

        public void LinkEntriesToChallan(string challanNo, List<int> entryIds, List<int> unlinkIds)
        {
            using var conn = Database.GetConnection();
            using var tx   = conn.BeginTransaction();
            try
            {
                if (entryIds.Count > 0)
                {
                    var ids = string.Join(",", entryIds);
                    using var linkCmd = conn.CreateCommand();
                    linkCmd.Transaction = tx;
                    linkCmd.CommandText = $"UPDATE tds_entries SET challan_no=@cn, status='Paid' WHERE id IN ({ids})";
                    linkCmd.Parameters.AddWithValue("@cn", challanNo);
                    linkCmd.ExecuteNonQuery();
                }
                if (unlinkIds.Count > 0)
                {
                    var ids = string.Join(",", unlinkIds);
                    using var unlinkCmd = conn.CreateCommand();
                    unlinkCmd.Transaction = tx;
                    unlinkCmd.CommandText = $"UPDATE tds_entries SET challan_no='', status='Pending' WHERE id IN ({ids})";
                    unlinkCmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch { tx.Rollback(); throw; }
            Database.LogAction("system", "LINK", "Challan", $"{challanNo} linked {entryIds.Count} entries");
        }
    }

    // ── Challan Service ───────────────────────────────────────────────────────
    public class ChallanService
    {
        private readonly ChallanRepository _repo = new();

        public List<Challan> GetAll(int? deductorId = null, string? fy = null)
            => _repo.GetAll(deductorId, fy);

        public (bool Ok, string Msg) Save(Challan c)
        {
            if (string.IsNullOrWhiteSpace(c.ChallanNo))
                return (false, "Challan number is required.");
            if (string.IsNullOrWhiteSpace(c.BsrCode))
                return (false, "BSR code is required.");

            c.TotalAmount = c.TdsAmount + c.Surcharge + c.Cess + c.Interest + c.LateFee;

            var result = _repo.Save(c);
            if (result.Ok)
                Database.LogAction("system", c.Id == 0 ? "CREATE" : "UPDATE",
                    "Challan", $"{c.ChallanNo} / {c.BsrCode}");
            return result;
        }

        public (bool Ok, string Msg) Delete(int id)
        {
            // Before deleting, unlink all TDS entries that reference this challan
            try
            {
                using var conn = Database.GetConnection();
                // Get challan_no for this id
                using var getCmd = conn.CreateCommand();
                getCmd.CommandText = "SELECT challan_no FROM challans WHERE id=@id";
                getCmd.Parameters.AddWithValue("@id", id);
                var challanNo = getCmd.ExecuteScalar() as string ?? "";
                if (!string.IsNullOrEmpty(challanNo))
                {
                    using var unlinkCmd = conn.CreateCommand();
                    unlinkCmd.CommandText = "UPDATE tds_entries SET challan_no='', status='Pending' WHERE challan_no=@cn";
                    unlinkCmd.Parameters.AddWithValue("@cn", challanNo);
                    unlinkCmd.ExecuteNonQuery();
                }
            }
            catch { }

            var result = _repo.Delete(id);
            if (result.Ok)
                Database.LogAction("system", "DELETE", "Challan", id.ToString());
            return result;
        }
    }

    // ── Dashboard Service ─────────────────────────────────────────────────────
    public class DashboardService
    {
        private readonly DashboardRepository _repo = new();
        public DashboardStats GetStats(string fy, int? deductorId = null) => _repo.GetStats(fy, deductorId);
    }
}
