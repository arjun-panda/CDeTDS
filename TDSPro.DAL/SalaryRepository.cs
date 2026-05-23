using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    public class SalaryRepository
    {
        // ── CRUD ─────────────────────────────────────────────────────────────

        public void Save(MonthlySalaryEntry e)
        {
            e.RecalcGross();
            using var conn = Database.GetConnection();
            // Transactional: entry upsert + line item delete + re-inserts succeed or fail together.
            // Without this, a crash mid-loop leaves orphan rows or missing line items.
            using var tx   = conn.BeginTransaction();
            try
            {
            using var cmd  = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO monthly_salary_entries
                    (employee_id,deductor_id,financial_year,month,year,
                     basic,grade_pay,hra,da_percent,da_amount,
                     special_allowance,medical_allowance,lta,
                     bonus,commission,advance_salary,arrears,other_allowances,nps_employer,
                     perq_total,perq_exempted,leave_enc_total,leave_enc_exempted,
                     pf_employee,vpf,professional_tax,esi_employee,
                     tds_deducted,gross_payment,gross_taxable,net_salary,saved_at,
                     days_worked,lop_days,working_days,status,is_locked,approved_at,approved_by)
                VALUES(@eid,@did,@fy,@m,@y,
                       @bas,@gp,@hra,@dap,@daa,
                       @spa,@mda,@lta,
                       @bon,@com,@adv,@arr,@oth,@nps,
                       @ptot,@pex,@ltot,@lex,
                       @pf,@vpf,@pt,@esi,
                       @tds,@gp2,@gt,@net,@sa,
                       @dw,@lop,@wd,@st,@il,@aa,@ab)
                ON CONFLICT(employee_id,financial_year,month) DO UPDATE SET
                    basic=@bas,grade_pay=@gp,hra=@hra,da_percent=@dap,da_amount=@daa,
                    special_allowance=@spa,medical_allowance=@mda,lta=@lta,
                    bonus=@bon,commission=@com,advance_salary=@adv,arrears=@arr,
                    other_allowances=@oth,nps_employer=@nps,
                    perq_total=@ptot,perq_exempted=@pex,
                    leave_enc_total=@ltot,leave_enc_exempted=@lex,
                    pf_employee=@pf,vpf=@vpf,professional_tax=@pt,esi_employee=@esi,
                    tds_deducted=@tds,gross_payment=@gp2,gross_taxable=@gt,net_salary=@net,
                    saved_at=@sa,deductor_id=@did,
                    days_worked=@dw,lop_days=@lop,working_days=@wd,
                    status=@st,is_locked=@il,approved_at=@aa,approved_by=@ab";

            void P(string n, object v) => cmd.Parameters.AddWithValue(n, v);
            P("@eid",e.EmployeeId); P("@did",e.DeductorId); P("@fy",e.FinancialYear);
            P("@m",e.Month);       P("@y",e.Year);
            P("@bas",e.Basic);     P("@gp",e.GradePay);    P("@hra",e.HRA);
            P("@dap",e.DaPercent); P("@daa",e.DaAmount);
            P("@spa",e.SpecialAllowance); P("@mda",e.MedicalAllowance); P("@lta",e.Lta);
            P("@bon",e.Bonus);     P("@com",e.Commission);  P("@adv",e.AdvanceSalary);
            P("@arr",e.Arrears);   P("@oth",e.OtherAllowances); P("@nps",e.NpsEmployer);
            P("@ptot",e.PerqTotal); P("@pex",e.PerqExempted);
            P("@ltot",e.LeaveEncTotal); P("@lex",e.LeaveEncExempted);
            P("@pf",e.PfEmployee); P("@vpf",e.VPF); P("@pt",e.ProfessionalTax);
            P("@esi",e.EsiEmployee);
            P("@tds", Math.Round(e.TdsDeducted, MidpointRounding.AwayFromZero));
            P("@gp2",e.GrossPayment); P("@gt",e.GrossTaxableSalary); P("@net",e.NetSalary);
            P("@sa",DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            P("@dw", e.DaysWorked); P("@lop", e.LopDays); P("@wd", e.WorkingDays);
            P("@st", e.Status ?? "Draft"); P("@il", e.IsLocked ? 1 : 0);
            P("@aa", e.ApprovedAt ?? ""); P("@ab", e.ApprovedBy ?? "");
            cmd.ExecuteNonQuery();

            // Resolve entry id (upsert may have inserted or updated)
            using var idCmd = conn.CreateCommand();
            idCmd.Transaction = tx;
            idCmd.CommandText = @"SELECT id FROM monthly_salary_entries
                                  WHERE employee_id=@e AND financial_year=@f AND month=@m";
            idCmd.Parameters.AddWithValue("@e", e.EmployeeId);
            idCmd.Parameters.AddWithValue("@f", e.FinancialYear);
            idCmd.Parameters.AddWithValue("@m", e.Month);
            var idObj = idCmd.ExecuteScalar();
            int entryId = idObj == null ? 0 : Convert.ToInt32(idObj);
            e.Id = entryId;

            // Replace line items for this entry
            if (entryId > 0)
            {
                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM salary_line_items WHERE monthly_salary_entry_id=@eid";
                del.Parameters.AddWithValue("@eid", entryId);
                del.ExecuteNonQuery();

                int ord = 0;
                foreach (var li in e.LineItems)
                {
                    using var ins = conn.CreateCommand();
                    ins.Transaction = tx;
                    ins.CommandText = @"INSERT INTO salary_line_items
                        (monthly_salary_entry_id,category,ordinal,name,taxable,exempt,rule_ref)
                        VALUES(@eid,@cat,@ord,@nm,@tx,@ex,@ru)";
                    ins.Parameters.AddWithValue("@eid", entryId);
                    ins.Parameters.AddWithValue("@cat", li.Category);
                    ins.Parameters.AddWithValue("@ord", ord++);
                    ins.Parameters.AddWithValue("@nm",  li.Name ?? "");
                    ins.Parameters.AddWithValue("@tx",  li.Taxable);
                    ins.Parameters.AddWithValue("@ex",  li.Exempt);
                    ins.Parameters.AddWithValue("@ru",  li.RuleRef ?? "");
                    ins.ExecuteNonQuery();
                }
            }
            tx.Commit();
            }
            catch
            {
                try { tx.Rollback(); } catch { }
                throw;
            }
        }

        public List<SalaryLineItem> GetLineItems(int entryId)
        {
            var list = new List<SalaryLineItem>();
            if (entryId <= 0) return list;
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT id,monthly_salary_entry_id,category,ordinal,name,taxable,exempt,rule_ref
                                FROM salary_line_items
                                WHERE monthly_salary_entry_id=@eid
                                ORDER BY category, ordinal";
            cmd.Parameters.AddWithValue("@eid", entryId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new SalaryLineItem
                {
                    Id       = r.GetInt32(0),
                    EntryId  = r.GetInt32(1),
                    Category = r.GetString(2),
                    Ordinal  = r.GetInt32(3),
                    Name     = r.IsDBNull(4) ? "" : r.GetString(4),
                    Taxable  = r.IsDBNull(5) ? 0  : Convert.ToDouble(r[5]),
                    Exempt   = r.IsDBNull(6) ? 0  : Convert.ToDouble(r[6]),
                    RuleRef  = r.IsDBNull(7) ? "" : r.GetString(7),
                });
            }
            return list;
        }

        public MonthlySalaryEntry? Get(int employeeId, string fy, int month)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT * FROM monthly_salary_entries
                                WHERE employee_id=@eid AND financial_year=@fy AND month=@m LIMIT 1";
            cmd.Parameters.AddWithValue("@eid", employeeId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            cmd.Parameters.AddWithValue("@m",   month);
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return null;
            var entry = Read(r);
            r.Close();
            entry.LineItems = GetLineItems(entry.Id);
            return entry;
        }

        public List<MonthlySalaryEntry> GetAllForFY(int employeeId, string fy)
        {
            var list = new List<MonthlySalaryEntry>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT * FROM monthly_salary_entries
                                WHERE employee_id=@eid AND financial_year=@fy
                                ORDER BY year, month";
            cmd.Parameters.AddWithValue("@eid", employeeId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Read(r));
            r.Close();
            foreach (var e in list) e.LineItems = GetLineItems(e.Id);
            return list;
        }

        public double GetYtdTds(int employeeId, string fy, int currentMonth, int currentYear)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT COALESCE(SUM(tds_deducted),0) FROM monthly_salary_entries
                                WHERE employee_id=@eid AND financial_year=@fy
                                  AND (year*12+month) < (@cy*12+@cm)";
            cmd.Parameters.AddWithValue("@eid", employeeId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            cmd.Parameters.AddWithValue("@cy",  currentYear);
            cmd.Parameters.AddWithValue("@cm",  currentMonth);
            return Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
        }

        // ── READ helper ──────────────────────────────────────────────────────
        private static MonthlySalaryEntry Read(Microsoft.Data.Sqlite.SqliteDataReader r)
        {
            double D(string c) { int o=r.GetOrdinal(c); return r.IsDBNull(o)?0:Convert.ToDouble(r[o]); }
            int    I(string c) { int o=r.GetOrdinal(c); return r.IsDBNull(o)?0:r.GetInt32(o); }
            string S(string c) { int o=r.GetOrdinal(c); return r.IsDBNull(o)?"":r.GetString(o); }
            var ent = new MonthlySalaryEntry
            {
                Id            = I("id"),        EmployeeId    = I("employee_id"),
                DeductorId    = I("deductor_id"),FinancialYear = S("financial_year"),
                Month         = I("month"),      Year          = I("year"),
                Basic         = D("basic"),      GradePay      = D("grade_pay"),
                HRA           = D("hra"),         DaPercent     = D("da_percent"),
                DaAmount      = D("da_amount"),
                SpecialAllowance = D("special_allowance"), MedicalAllowance = D("medical_allowance"), Lta = D("lta"),
                Bonus         = D("bonus"),       Commission    = D("commission"),
                AdvanceSalary = D("advance_salary"), Arrears    = D("arrears"),
                OtherAllowances = D("other_allowances"), NpsEmployer = D("nps_employer"),
                PerqTotal     = D("perq_total"),  PerqExempted  = D("perq_exempted"),
                LeaveEncTotal = D("leave_enc_total"), LeaveEncExempted = D("leave_enc_exempted"),
                PfEmployee    = D("pf_employee"), VPF           = D("vpf"),
                ProfessionalTax = D("professional_tax"), EsiEmployee = D("esi_employee"),
                TdsDeducted   = D("tds_deducted"),
                GrossPayment  = D("gross_payment"), GrossTaxableSalary = D("gross_taxable"),
                NetSalary     = D("net_salary"),
                DaysWorked    = TryI(r, "days_worked"),
                LopDays       = TryI(r, "lop_days"),
                WorkingDays   = TryI(r, "working_days") is int wd && wd > 0 ? wd : 30,
                Status        = TryS(r, "status").Length > 0 ? TryS(r, "status") : "Draft",
                IsLocked      = TryI(r, "is_locked") == 1,
                ApprovedAt    = TryS(r, "approved_at"),
                ApprovedBy    = TryS(r, "approved_by"),
            };
            return ent;
        }

        private static int    TryI(Microsoft.Data.Sqlite.SqliteDataReader r, string col)
        { try { int o = r.GetOrdinal(col); return r.IsDBNull(o) ? 0 : r.GetInt32(o); } catch { return 0; } }
        private static string TryS(Microsoft.Data.Sqlite.SqliteDataReader r, string col)
        { try { int o = r.GetOrdinal(col); return r.IsDBNull(o) ? "" : r.GetString(o); } catch { return ""; } }
    }
}
