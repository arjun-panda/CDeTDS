using Microsoft.Data.Sqlite;

namespace TDSPro.DAL
{
    /// <summary>
    /// Due date reminder system.
    /// Stores due dates per FY/Quarter, checks upcoming/overdue,
    /// can trigger popup alerts.
    /// </summary>
    public class DueDateService
    {
        // ── Due dates table ────────────────────────────────────────────────────
        public static void EnsureTable()
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS due_dates (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    fy           TEXT NOT NULL,
                    quarter      TEXT NOT NULL,
                    form_type    TEXT NOT NULL DEFAULT '26Q',
                    due_date     TEXT NOT NULL,
                    filed_date   TEXT DEFAULT '',
                    status       TEXT DEFAULT 'Pending',
                    notes        TEXT DEFAULT '',
                    created_at   TEXT DEFAULT (datetime('now','localtime')),
                    UNIQUE(fy, quarter, form_type)
                );
                CREATE INDEX IF NOT EXISTS idx_duedates_fy ON due_dates(fy);";
            cmd.ExecuteNonQuery();
            SeedDueDates();
        }

        private static void SeedDueDates()
        {
            using var conn = Database.GetConnection();
            using var chk  = conn.CreateCommand();
            chk.CommandText = "SELECT COUNT(*) FROM due_dates";
            if ((long)(chk.ExecuteScalar() ?? 0L) > 0) return;

            var entries = new[]
            {
                // FY 2026-27 (current)
                ("2026-27","Q1","24Q","2026-07-31"),
                ("2026-27","Q1","26Q","2026-07-31"),
                ("2026-27","Q2","24Q","2026-10-31"),
                ("2026-27","Q2","26Q","2026-10-31"),
                ("2026-27","Q3","24Q","2027-01-31"),
                ("2026-27","Q3","26Q","2027-01-31"),
                ("2026-27","Q4","24Q","2027-05-31"),
                ("2026-27","Q4","26Q","2027-05-31"),
                // FY 2025-26 (previous)
                ("2025-26","Q1","24Q","2025-07-31"),
                ("2025-26","Q1","26Q","2025-07-31"),
                ("2025-26","Q2","24Q","2025-10-31"),
                ("2025-26","Q2","26Q","2025-10-31"),
                ("2025-26","Q3","24Q","2026-01-31"),
                ("2025-26","Q3","26Q","2026-01-31"),
                ("2025-26","Q4","24Q","2026-05-31"),
                ("2025-26","Q4","26Q","2026-05-31"),
            };

            using var tx = conn.BeginTransaction();
            foreach (var (fy, q, ft, dd) in entries)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = @"INSERT OR IGNORE INTO due_dates (fy,quarter,form_type,due_date)
                                    VALUES(@fy,@q,@ft,@dd)";
                ins.Parameters.AddWithValue("@fy", fy);
                ins.Parameters.AddWithValue("@q",  q);
                ins.Parameters.AddWithValue("@ft", ft);
                ins.Parameters.AddWithValue("@dd", dd);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // ── Get all due dates ─────────────────────────────────────────────────
        public List<DueDate> GetAll(string? fy = null)
        {
            EnsureTable();
            var list = new List<DueDate>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = fy != null
                ? "SELECT * FROM due_dates WHERE fy=@fy ORDER BY due_date"
                : "SELECT * FROM due_dates ORDER BY fy, due_date";
            if (fy != null) cmd.Parameters.AddWithValue("@fy", fy);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(MapRow(r));
            return list;
        }

        // ── Get upcoming/overdue filings ──────────────────────────────────────
        public List<DueDate> GetAlerts(int lookAheadDays = 30)
        {
            EnsureTable();
            var list = new List<DueDate>();
            var today   = DateTime.Today;
            var horizon = today.AddDays(lookAheadDays);

            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT * FROM due_dates
                WHERE status='Pending'
                  AND date(due_date) <= @horizon
                ORDER BY due_date";
            cmd.Parameters.AddWithValue("@horizon", horizon.ToString("yyyy-MM-dd"));
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(MapRow(r));
            return list;
        }

        // ── Mark as filed ─────────────────────────────────────────────────────
        public void MarkFiled(string fy, string quarter, string formType, DateTime? filedDate = null)
        {
            EnsureTable();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"UPDATE due_dates SET status='Filed', filed_date=@fd
                                WHERE fy=@fy AND quarter=@q AND form_type=@ft";
            cmd.Parameters.AddWithValue("@fd", (filedDate ?? DateTime.Today).ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@fy", fy);
            cmd.Parameters.AddWithValue("@q",  quarter);
            cmd.Parameters.AddWithValue("@ft", formType);
            cmd.ExecuteNonQuery();
        }

        public void RevertToPending(string fy, string quarter, string formType)
        {
            EnsureTable();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"UPDATE due_dates SET status='Pending', filed_date=NULL
                                WHERE fy=@fy AND quarter=@q AND form_type=@ft";
            cmd.Parameters.AddWithValue("@fy", fy);
            cmd.Parameters.AddWithValue("@q",  quarter);
            cmd.Parameters.AddWithValue("@ft", formType);
            cmd.ExecuteNonQuery();
            Database.LogAction("system", "REVERT_FILED", "DueDate",
                $"{formType} {quarter} {fy} reverted to Pending");
        }

        // ── Due date summary for dashboard ────────────────────────────────────
        public (int Overdue, int DueSoon, int Filed) GetSummary(string fy)
        {
            EnsureTable();
            var today   = DateTime.Today;
            var horizon = today.AddDays(30);
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT
                    SUM(CASE WHEN status='Pending' AND date(due_date) < @today   THEN 1 ELSE 0 END) AS overdue,
                    SUM(CASE WHEN status='Pending' AND date(due_date) BETWEEN @today AND @horizon THEN 1 ELSE 0 END) AS due_soon,
                    SUM(CASE WHEN status='Filed'                                  THEN 1 ELSE 0 END) AS filed
                FROM due_dates WHERE fy=@fy";
            cmd.Parameters.AddWithValue("@today",   today.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@horizon", horizon.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@fy", fy);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return (0, 0, 0);
            return (
                r.IsDBNull(0) ? 0 : Convert.ToInt32(r[0]),
                r.IsDBNull(1) ? 0 : Convert.ToInt32(r[1]),
                r.IsDBNull(2) ? 0 : Convert.ToInt32(r[2])
            );
        }

        // ── Save / update a due date ──────────────────────────────────────────
        public void Save(DueDate dd)
        {
            EnsureTable();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            if (dd.Id == 0)
            {
                cmd.CommandText = @"INSERT OR REPLACE INTO due_dates
                    (fy,quarter,form_type,due_date,filed_date,status,notes)
                    VALUES(@fy,@q,@ft,@dd,@fd,@s,@n)";
            }
            else
            {
                cmd.CommandText = @"UPDATE due_dates SET
                    due_date=@dd,filed_date=@fd,status=@s,notes=@n
                    WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", dd.Id);
            }
            cmd.Parameters.AddWithValue("@fy", dd.Fy);
            cmd.Parameters.AddWithValue("@q",  dd.Quarter);
            cmd.Parameters.AddWithValue("@ft", dd.FormType);
            cmd.Parameters.AddWithValue("@dd", dd.DueDateValue.ToString("yyyy-MM-dd"));
            cmd.Parameters.AddWithValue("@fd", dd.FiledDate?.ToString("yyyy-MM-dd") ?? "");
            cmd.Parameters.AddWithValue("@s",  dd.Status);
            cmd.Parameters.AddWithValue("@n",  dd.Notes);
            cmd.ExecuteNonQuery();
        }

        private static DueDate MapRow(SqliteDataReader r) => new()
        {
            Id           = r.GetInt32(r.GetOrdinal("id")),
            Fy           = r.GetString(r.GetOrdinal("fy")),
            Quarter      = r.GetString(r.GetOrdinal("quarter")),
            FormType     = r.GetString(r.GetOrdinal("form_type")),
            DueDateValue = DateTime.Parse(r.GetString(r.GetOrdinal("due_date"))),
            FiledDate    = r.IsDBNull(r.GetOrdinal("filed_date")) || string.IsNullOrEmpty(r.GetString(r.GetOrdinal("filed_date")))
                           ? (DateTime?)null
                           : DateTime.Parse(r.GetString(r.GetOrdinal("filed_date"))),
            Status       = r.GetString(r.GetOrdinal("status")),
            Notes        = r.IsDBNull(r.GetOrdinal("notes")) ? "" : r.GetString(r.GetOrdinal("notes")),
        };
    }

    public class DueDate
    {
        public int      Id           { get; set; }
        public string   Fy           { get; set; } = "";
        public string   Quarter      { get; set; } = "";
        public string   FormType     { get; set; } = "26Q";
        public DateTime DueDateValue { get; set; }
        public DateTime? FiledDate   { get; set; }
        public string   Status       { get; set; } = "Pending";
        public string   Notes        { get; set; } = "";

        public bool IsOverdue  => Status == "Pending" && DueDateValue < DateTime.Today;
        public bool IsDueSoon  => Status == "Pending" && DueDateValue >= DateTime.Today
                               && DueDateValue <= DateTime.Today.AddDays(30);
        public int  DaysLeft   => (DueDateValue - DateTime.Today).Days;
        public string DaysLabel => IsOverdue ? $"Overdue by {Math.Abs(DaysLeft)} days"
                                 : DaysLeft == 0 ? "Due TODAY!"
                                 : $"{DaysLeft} days left";
    }
}
