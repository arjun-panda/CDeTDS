using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    public class ReimbursementRepository
    {
        public List<ReimbursementClaim> GetForMonth(int employeeId, string fy, int month)
        {
            var list = new List<ReimbursementClaim>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT id,employee_id,financial_year,month,year,category,
                                       eligible,claimed,bill_count,status,notes,saved_at
                                FROM reimbursement_claims
                                WHERE employee_id=@e AND financial_year=@f AND month=@m
                                ORDER BY category";
            cmd.Parameters.AddWithValue("@e", employeeId);
            cmd.Parameters.AddWithValue("@f", fy);
            cmd.Parameters.AddWithValue("@m", month);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(Read(r));
            return list;
        }

        public List<ReimbursementClaim> GetForFy(int employeeId, string fy)
        {
            var list = new List<ReimbursementClaim>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT id,employee_id,financial_year,month,year,category,
                                       eligible,claimed,bill_count,status,notes,saved_at
                                FROM reimbursement_claims
                                WHERE employee_id=@e AND financial_year=@f
                                ORDER BY year,month,category";
            cmd.Parameters.AddWithValue("@e", employeeId);
            cmd.Parameters.AddWithValue("@f", fy);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Read(r));
            return list;
        }

        public void Save(ReimbursementClaim c)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"INSERT INTO reimbursement_claims
                (employee_id,financial_year,month,year,category,eligible,claimed,bill_count,status,notes,saved_at)
                VALUES(@e,@f,@m,@y,@c,@el,@cl,@bc,@st,@no,@sa)
                ON CONFLICT(employee_id,financial_year,month,category) DO UPDATE SET
                    eligible=@el,claimed=@cl,bill_count=@bc,status=@st,notes=@no,saved_at=@sa,year=@y";
            cmd.Parameters.AddWithValue("@e",  c.EmployeeId);
            cmd.Parameters.AddWithValue("@f",  c.FinancialYear);
            cmd.Parameters.AddWithValue("@m",  c.Month);
            cmd.Parameters.AddWithValue("@y",  c.Year);
            cmd.Parameters.AddWithValue("@c",  c.Category);
            cmd.Parameters.AddWithValue("@el", c.Eligible);
            cmd.Parameters.AddWithValue("@cl", c.Claimed);
            cmd.Parameters.AddWithValue("@bc", c.BillCount);
            cmd.Parameters.AddWithValue("@st", c.Status ?? "pending");
            cmd.Parameters.AddWithValue("@no", c.Notes ?? "");
            cmd.Parameters.AddWithValue("@sa", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        private static ReimbursementClaim Read(Microsoft.Data.Sqlite.SqliteDataReader r) => new ReimbursementClaim
        {
            Id            = r.GetInt32(0),
            EmployeeId    = r.GetInt32(1),
            FinancialYear = r.GetString(2),
            Month         = r.GetInt32(3),
            Year          = r.GetInt32(4),
            Category      = r.GetString(5),
            Eligible      = r.IsDBNull(6) ? 0 : Convert.ToDouble(r[6]),
            Claimed       = r.IsDBNull(7) ? 0 : Convert.ToDouble(r[7]),
            BillCount     = r.IsDBNull(8) ? 0 : r.GetInt32(8),
            Status        = r.IsDBNull(9) ? "pending" : r.GetString(9),
            Notes         = r.IsDBNull(10) ? "" : r.GetString(10),
            SavedAt       = r.IsDBNull(11) ? "" : r.GetString(11),
        };
    }
}
