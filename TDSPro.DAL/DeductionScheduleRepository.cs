using CDeTDS.DAL.Models;

namespace CDeTDS.DAL
{
    public class DeductionScheduleRepository
    {
        public List<DeductionSchedule> GetActive(int employeeId)
        {
            var list = new List<DeductionSchedule>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT id,employee_id,deductor_id,type,description,
                                       total_amount,installment_amt,total_installments,
                                       recovered_amt,start_fy,start_month,is_active,
                                       created_at,notes
                                FROM deduction_schedules
                                WHERE employee_id=@eid AND is_active=1
                                ORDER BY id";
            cmd.Parameters.AddWithValue("@eid", employeeId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Read(r));
            return list;
        }

        public List<DeductionSchedule> GetAll(int deductorId)
        {
            var list = new List<DeductionSchedule>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT id,employee_id,deductor_id,type,description,
                                       total_amount,installment_amt,total_installments,
                                       recovered_amt,start_fy,start_month,is_active,
                                       created_at,notes
                                FROM deduction_schedules
                                WHERE deductor_id=@did
                                ORDER BY is_active DESC, id DESC";
            cmd.Parameters.AddWithValue("@did", deductorId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Read(r));
            return list;
        }

        public void Save(DeductionSchedule s)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            if (s.Id > 0)
            {
                cmd.CommandText = @"UPDATE deduction_schedules SET
                    type=@tp, description=@desc, total_amount=@tot,
                    installment_amt=@inst, total_installments=@tc,
                    recovered_amt=@rec, start_fy=@sfy, start_month=@sm,
                    is_active=@ia, notes=@nt
                    WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", s.Id);
            }
            else
            {
                cmd.CommandText = @"INSERT INTO deduction_schedules
                    (employee_id,deductor_id,type,description,total_amount,
                     installment_amt,total_installments,recovered_amt,
                     start_fy,start_month,is_active,created_at,notes)
                    VALUES(@eid,@did,@tp,@desc,@tot,@inst,@tc,@rec,@sfy,@sm,@ia,@ca,@nt)";
                cmd.Parameters.AddWithValue("@eid", s.EmployeeId);
                cmd.Parameters.AddWithValue("@did", s.DeductorId);
                cmd.Parameters.AddWithValue("@ca", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            }
            cmd.Parameters.AddWithValue("@tp",   s.Type);
            cmd.Parameters.AddWithValue("@desc", s.Description);
            cmd.Parameters.AddWithValue("@tot",  s.TotalAmount);
            cmd.Parameters.AddWithValue("@inst", s.InstallmentAmt);
            cmd.Parameters.AddWithValue("@tc",   s.TotalInstallments);
            cmd.Parameters.AddWithValue("@rec",  s.RecoveredAmt);
            cmd.Parameters.AddWithValue("@sfy",  s.StartFy);
            cmd.Parameters.AddWithValue("@sm",   s.StartMonth);
            cmd.Parameters.AddWithValue("@ia",   s.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@nt",   s.Notes);
            cmd.ExecuteNonQuery();

            if (s.Id <= 0)
            {
                using var idCmd = conn.CreateCommand();
                idCmd.CommandText = "SELECT last_insert_rowid()";
                s.Id = Convert.ToInt32(idCmd.ExecuteScalar());
            }
        }

        // Add recovered amount; auto-close when fully recovered
        public void UpdateRecovered(int id, double amount)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            // Use a subquery so is_active sees the already-updated recovered_amt value
            cmd.CommandText = @"UPDATE deduction_schedules
                SET recovered_amt = MIN(total_amount, recovered_amt + @amt),
                    is_active = CASE WHEN MIN(total_amount, recovered_amt + @amt) >= total_amount THEN 0 ELSE is_active END
                WHERE id=@id";
            cmd.Parameters.AddWithValue("@amt", amount);
            cmd.Parameters.AddWithValue("@id",  id);
            cmd.ExecuteNonQuery();
        }

        public void Close(int id)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "UPDATE deduction_schedules SET is_active=0 WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        static DeductionSchedule Read(System.Data.IDataReader r) => new()
        {
            Id                = r.GetInt32(0),
            EmployeeId        = r.GetInt32(1),
            DeductorId        = r.GetInt32(2),
            Type              = r.GetString(3),
            Description       = r.GetString(4),
            TotalAmount       = r.GetDouble(5),
            InstallmentAmt    = r.GetDouble(6),
            TotalInstallments = r.GetInt32(7),
            RecoveredAmt      = r.GetDouble(8),
            StartFy           = r.GetString(9),
            StartMonth        = r.GetInt32(10),
            IsActive          = r.GetInt32(11) == 1,
            CreatedAt         = r.GetString(12),
            Notes             = r.GetString(13),
        };
    }
}
