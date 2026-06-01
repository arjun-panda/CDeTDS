using Microsoft.Data.Sqlite;
using CDeTDS.DAL.Models;

namespace CDeTDS.DAL.Repositories
{
    // ── Deductor Repository ───────────────────────────────────────────────────
    public class DeductorRepository
    {
        public List<Deductor> GetAll()
        {
            var list = new List<Deductor>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM deductors WHERE is_active=1 ORDER BY company_name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Map(r));
            return list;
        }

        public Deductor? GetById(int id)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM deductors WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? Map(r) : null;
        }

        public (bool Ok, string Msg) Save(Deductor d)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = conn.CreateCommand();
                if (d.Id == 0)
                {
                    // Auto-wipe sample data on first real deductor save
                    Database.DeleteSampleData();
                    cmd.CommandText = @"INSERT INTO deductors
                        (company_name,tan,pan,address,city,state,pincode,
                         contact_person,phone,email,financial_year,cpc_password,it_password,
                         default_bsr_code,default_bank_name,responsible_name,responsible_pan,designation,gstin,deductor_type)
                        VALUES(@cn,@t,@p,@ad,@ci,@st,@pi,@cp,@ph,@em,@fy,@cpwd,@ipwd,@bsr,@bank,@rn,@rp,@des,@gstin,@dt)";
                }
                else
                {
                    cmd.CommandText = @"UPDATE deductors SET
                        company_name=@cn,tan=@t,pan=@p,address=@ad,city=@ci,
                        state=@st,pincode=@pi,contact_person=@cp,phone=@ph,
                        email=@em,financial_year=@fy,
                        cpc_password=@cpwd,it_password=@ipwd,
                        default_bsr_code=@bsr,default_bank_name=@bank,
                        responsible_name=@rn,responsible_pan=@rp,designation=@des,gstin=@gstin,
                        deductor_type=@dt WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", d.Id);
                }
                cmd.Parameters.AddWithValue("@cn", d.CompanyName);
                cmd.Parameters.AddWithValue("@t",  d.Tan.ToUpper());
                cmd.Parameters.AddWithValue("@p",  d.Pan.ToUpper());
                cmd.Parameters.AddWithValue("@ad", d.Address);
                cmd.Parameters.AddWithValue("@ci", d.City);
                cmd.Parameters.AddWithValue("@st", d.State);
                cmd.Parameters.AddWithValue("@pi", d.Pincode);
                cmd.Parameters.AddWithValue("@cp", d.ContactPerson);
                cmd.Parameters.AddWithValue("@ph", d.Phone);
                cmd.Parameters.AddWithValue("@em", d.Email);
                cmd.Parameters.AddWithValue("@fy",   d.FinancialYear);
                cmd.Parameters.AddWithValue("@cpwd", string.IsNullOrEmpty(d.CpcPassword) ? "" : AesEncryption.Encrypt(d.CpcPassword));
                cmd.Parameters.AddWithValue("@ipwd", string.IsNullOrEmpty(d.ItPassword)  ? "" : AesEncryption.Encrypt(d.ItPassword));
                cmd.Parameters.AddWithValue("@bsr",  d.DefaultBsrCode);
                cmd.Parameters.AddWithValue("@bank", d.DefaultBankName);
                cmd.Parameters.AddWithValue("@rn",   d.ResponsibleName);
                cmd.Parameters.AddWithValue("@rp",   d.ResponsiblePan.ToUpper());
                cmd.Parameters.AddWithValue("@des",  d.Designation);
                cmd.Parameters.AddWithValue("@gstin",d.Gstin.Trim().ToUpper());
                cmd.Parameters.AddWithValue("@dt",   string.IsNullOrEmpty(d.DeductorType) ? "Company" : d.DeductorType);
                bool isInsert = d.Id == 0;
                cmd.ExecuteNonQuery();
                if (isInsert)
                {
                    using var idCmd = conn.CreateCommand();
                    idCmd.CommandText = "SELECT last_insert_rowid()";
                    d.Id = Convert.ToInt32(idCmd.ExecuteScalar());
                }
                return (true, isInsert ? "Deductor saved." : "Deductor updated.");
            }
            catch (SqliteException ex) when (ex.Message.Contains("UNIQUE"))
            {
                return (false, "TAN already exists.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public (bool Ok, string Msg) Delete(int id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "UPDATE deductors SET is_active=0 WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return (true, "Deductor deleted.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        private static Deductor Map(SqliteDataReader r) => new()
        {
            Id            = r.GetInt32(r.GetOrdinal("id")),
            CompanyName   = r.GetString(r.GetOrdinal("company_name")),
            Tan           = r.GetString(r.GetOrdinal("tan")),
            Pan           = r.GetString(r.GetOrdinal("pan")),
            Address       = r.IsDBNull(r.GetOrdinal("address"))       ? "" : r.GetString(r.GetOrdinal("address")),
            City          = r.IsDBNull(r.GetOrdinal("city"))          ? "" : r.GetString(r.GetOrdinal("city")),
            State         = r.IsDBNull(r.GetOrdinal("state"))         ? "" : r.GetString(r.GetOrdinal("state")),
            Pincode       = r.IsDBNull(r.GetOrdinal("pincode"))       ? "" : r.GetString(r.GetOrdinal("pincode")),
            ContactPerson = r.IsDBNull(r.GetOrdinal("contact_person"))? "" : r.GetString(r.GetOrdinal("contact_person")),
            Phone         = r.IsDBNull(r.GetOrdinal("phone"))         ? "" : r.GetString(r.GetOrdinal("phone")),
            Email         = r.IsDBNull(r.GetOrdinal("email"))         ? "" : r.GetString(r.GetOrdinal("email")),
            FinancialYear = r.IsDBNull(r.GetOrdinal("financial_year"))? "2024-25" : r.GetString(r.GetOrdinal("financial_year")),
            CpcPassword      = SafeDecrypt(r.IsDBNull(r.GetOrdinal("cpc_password")) ? "" : r.GetString(r.GetOrdinal("cpc_password"))),
            ItPassword       = SafeDecrypt(r.IsDBNull(r.GetOrdinal("it_password"))  ? "" : r.GetString(r.GetOrdinal("it_password"))),
            DefaultBsrCode   = r.IsDBNull(r.GetOrdinal("default_bsr_code"))  ? "" : r.GetString(r.GetOrdinal("default_bsr_code")),
            DefaultBankName  = r.IsDBNull(r.GetOrdinal("default_bank_name")) ? "" : r.GetString(r.GetOrdinal("default_bank_name")),
            ResponsibleName  = r.IsDBNull(r.GetOrdinal("responsible_name"))  ? "" : r.GetString(r.GetOrdinal("responsible_name")),
            ResponsiblePan   = r.IsDBNull(r.GetOrdinal("responsible_pan"))   ? "" : r.GetString(r.GetOrdinal("responsible_pan")),
            Designation      = r.IsDBNull(r.GetOrdinal("designation"))       ? "" : r.GetString(r.GetOrdinal("designation")),
            Gstin            = r.IsDBNull(r.GetOrdinal("gstin"))             ? "" : r.GetString(r.GetOrdinal("gstin")),
            DeductorType     = r.IsDBNull(r.GetOrdinal("deductor_type"))    ? "Company" : r.GetString(r.GetOrdinal("deductor_type")),
        };

        private static string SafeDecrypt(string enc)
        {
            if (string.IsNullOrEmpty(enc)) return "";
            try { return AesEncryption.Decrypt(enc); } catch { return ""; }
        }
    }

    // ── Deductee Repository ───────────────────────────────────────────────────
    public class DeducteeRepository
    {
        public List<Deductee> GetAll(bool includeEmployees = false)
        {
            var list = new List<Deductee>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = includeEmployees
                ? "SELECT * FROM deductees ORDER BY name"
                : "SELECT * FROM deductees WHERE (deductee_code IS NULL OR deductee_code NOT LIKE 'EMP-%') ORDER BY name";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Map(r));
            return list;
        }

        public Deductee? GetById(int id)
        {
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM deductees WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            using var r = cmd.ExecuteReader();
            return r.Read() ? Map(r) : null;
        }

        public (bool Ok, string Msg) Save(Deductee d)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = conn.CreateCommand();
                if (d.Id == 0)
                {
                    var nextId = GetNextId(conn);
                    d.DeducteeCode = $"DED{nextId:D5}";
                    cmd.CommandText = @"INSERT INTO deductees
                        (deductee_code,name,pan,section,rate,deductee_type,
                         is_resident,itr_filed,lower_cert_no,lower_cert_rate,lower_cert_till,remarks,
                         pan_verified,pan_verification_status,pan_verified_name,pan_verified_at)
                        VALUES(@dc,@n,@p,@s,@r,@dt,@ir,@itrf,@lc,@lr,@lt,@rm,
                               @pv,@pvs,@pvn,@pvt)";
                    cmd.Parameters.AddWithValue("@dc", d.DeducteeCode);
                }
                else
                {
                    cmd.CommandText = @"UPDATE deductees SET
                        name=@n,pan=@p,section=@s,rate=@r,deductee_type=@dt,
                        is_resident=@ir,itr_filed=@itrf,lower_cert_no=@lc,lower_cert_rate=@lr,
                        lower_cert_till=@lt,remarks=@rm,
                        pan_verified=@pv,pan_verification_status=@pvs,
                        pan_verified_name=@pvn,pan_verified_at=@pvt
                        WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", d.Id);
                }
                cmd.Parameters.AddWithValue("@n",  d.Name);
                cmd.Parameters.AddWithValue("@p",  d.Pan.ToUpper());
                cmd.Parameters.AddWithValue("@s",  d.Section);
                cmd.Parameters.AddWithValue("@r",  d.Rate);
                cmd.Parameters.AddWithValue("@dt", d.DeducteeType);
                cmd.Parameters.AddWithValue("@ir",   d.IsResident ? 1 : 0);
                cmd.Parameters.AddWithValue("@itrf", d.ItrFiled   ? 1 : 0);
                cmd.Parameters.AddWithValue("@lc", d.LowerCertNo);
                cmd.Parameters.AddWithValue("@lr", d.LowerCertRate);
                cmd.Parameters.AddWithValue("@lt", d.LowerCertTill);
                cmd.Parameters.AddWithValue("@rm", d.Remarks);
                cmd.Parameters.AddWithValue("@pv",  d.PanVerified ? 1 : 0);
                cmd.Parameters.AddWithValue("@pvs", d.PanVerificationStatus);
                cmd.Parameters.AddWithValue("@pvn", d.PanVerifiedName);
                cmd.Parameters.AddWithValue("@pvt", d.PanVerifiedAt);
                cmd.ExecuteNonQuery();
                return (true, "Deductee saved.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public (bool Ok, string Msg) Delete(int id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM deductees WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return (true, "Deductee deleted.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        private static long GetNextId(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(MAX(id),0)+1 FROM deductees";
            return (long)(cmd.ExecuteScalar() ?? 1L);
        }

        private static Deductee Map(SqliteDataReader r) => new()
        {
            Id            = r.GetInt32(r.GetOrdinal("id")),
            DeducteeCode  = r.GetString(r.GetOrdinal("deductee_code")),
            Name          = r.GetString(r.GetOrdinal("name")),
            Pan           = r.GetString(r.GetOrdinal("pan")),
            Section       = r.GetString(r.GetOrdinal("section")),
            Rate          = r.GetDouble(r.GetOrdinal("rate")),
            DeducteeType  = r.IsDBNull(r.GetOrdinal("deductee_type")) ? "Individual" : r.GetString(r.GetOrdinal("deductee_type")),
            IsResident    = r.GetInt32(r.GetOrdinal("is_resident")) == 1,
            ItrFiled      = (r.GetOrdinal("itr_filed") is var ifo && !r.IsDBNull(ifo) ? r.GetInt32(ifo) : 1) == 1,
            LowerCertNo   = r.IsDBNull(r.GetOrdinal("lower_cert_no"))   ? "" : r.GetString(r.GetOrdinal("lower_cert_no")),
            LowerCertRate = r.IsDBNull(r.GetOrdinal("lower_cert_rate")) ? 0  : r.GetDouble(r.GetOrdinal("lower_cert_rate")),
            LowerCertTill = r.IsDBNull(r.GetOrdinal("lower_cert_till")) ? "" : r.GetString(r.GetOrdinal("lower_cert_till")),
            PanVerified           = (r.GetOrdinal("pan_verified") is var pvo && !r.IsDBNull(pvo) ? r.GetInt32(pvo) : 0) == 1,
            PanVerificationStatus = r.GetOrdinal("pan_verification_status") is var pvso && !r.IsDBNull(pvso) ? r.GetString(pvso) : "",
            PanVerifiedName       = r.GetOrdinal("pan_verified_name")       is var pvno && !r.IsDBNull(pvno) ? r.GetString(pvno) : "",
            PanVerifiedAt         = r.GetOrdinal("pan_verified_at")         is var pvto && !r.IsDBNull(pvto) ? r.GetString(pvto) : "",
            Remarks       = r.IsDBNull(r.GetOrdinal("remarks"))         ? "" : r.GetString(r.GetOrdinal("remarks")),
        };
    }

    // ── TDS Entry Repository ──────────────────────────────────────────────────
    public class TdsEntryRepository
    {
        public List<TdsEntry> GetAll(int? deductorId = null, string? fy = null)
        {
            var list = new List<TdsEntry>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            var where = "WHERE 1=1";
            if (deductorId.HasValue) { where += " AND e.deductor_id=@did"; cmd.Parameters.AddWithValue("@did", deductorId); }
            if (!string.IsNullOrEmpty(fy)) { where += " AND e.financial_year=@fy"; cmd.Parameters.AddWithValue("@fy", fy); }
            cmd.CommandText = $@"
                SELECT e.*, d.company_name as deductor_name,
                       dd.name as deductee_name, dd.pan as deductee_pan
                FROM tds_entries e
                LEFT JOIN deductors d  ON e.deductor_id = d.id
                LEFT JOIN deductees dd ON e.deductee_id = dd.id
                {where} ORDER BY e.entry_date DESC LIMIT 500";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Map(r));
            return list;
        }

        public (bool Ok, string Msg) Save(TdsEntry e)
        {
            try
            {
                using var conn = Database.GetConnection();

                // Validate Sec 197 LDC expiry — entry date must not exceed cert_till
                if (e.Remarks?.Trim().Equals("A", StringComparison.OrdinalIgnoreCase) == true && e.DeducteeId > 0)
                {
                    using var ldcCmd = conn.CreateCommand();
                    ldcCmd.CommandText = "SELECT lower_cert_till FROM deductees WHERE id=@id";
                    ldcCmd.Parameters.AddWithValue("@id", e.DeducteeId);
                    var tillRaw = ldcCmd.ExecuteScalar() as string;
                    if (!string.IsNullOrEmpty(tillRaw) &&
                        DateTime.TryParse(tillRaw, out var certTill) &&
                        e.EntryDate.Date > certTill.Date)
                        return (false, $"Sec 197 certificate expired on {certTill:dd-MMM-yyyy}. Entry date {e.EntryDate:dd-MMM-yyyy} is after the validity period.");
                }

                // Validate challan_no exists for this deductor (skip if blank or Adjusted)
                if (!string.IsNullOrEmpty(e.ChallanNo) && e.Status != "Adjusted")
                {
                    using var chkCmd = conn.CreateCommand();
                    chkCmd.CommandText = "SELECT COUNT(1) FROM challans WHERE challan_no=@cn AND deductor_id=@did";
                    chkCmd.Parameters.AddWithValue("@cn",  e.ChallanNo);
                    chkCmd.Parameters.AddWithValue("@did", e.DeductorId);
                    var exists = (long)(chkCmd.ExecuteScalar() ?? 0L);
                    if (exists == 0)
                        return (false, $"Challan '{e.ChallanNo}' not found. Add it in Challans first.");
                }

                using var cmd = conn.CreateCommand();
                if (e.Id == 0)
                {
                    using var c2 = conn.CreateCommand();
                    c2.CommandText = "SELECT COALESCE(MAX(CAST(SUBSTR(entry_no,4) AS INTEGER)),0) FROM tds_entries WHERE entry_no LIKE 'TDS%'";
                    var cnt = (long)(c2.ExecuteScalar() ?? 0L);
                    e.EntryNo = $"TDS{cnt + 1:D6}";
                    cmd.CommandText = @"INSERT INTO tds_entries
                        (entry_no,entry_date,deductor_id,deductee_id,section,nature_of_payment,
                         amount,rate,tds_amount,surcharge,cess,total_tds,due_date,payment_date,
                         interest,late_fee,challan_no,remarks,status,financial_year,quarter)
                        VALUES(@en,@ed,@di,@dei,@s,@pn,@am,@ra,@ta,@su,@ce,@tt,
                               @dd,@pd,@in,@lf,@cn,@rm,@st,@fy,@qt)";
                }
                else
                {
                    cmd.CommandText = @"UPDATE tds_entries SET
                        entry_date=@ed,deductor_id=@di,deductee_id=@dei,section=@s,
                        nature_of_payment=@pn,amount=@am,rate=@ra,tds_amount=@ta,
                        surcharge=@su,cess=@ce,total_tds=@tt,due_date=@dd,
                        payment_date=@pd,interest=@in,late_fee=@lf,challan_no=@cn,
                        remarks=@rm,status=@st,financial_year=@fy,quarter=@qt
                        WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", e.Id);
                }
                cmd.Parameters.AddWithValue("@en", e.EntryNo);
                cmd.Parameters.AddWithValue("@ed", e.EntryDate.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@di", e.DeductorId);
                cmd.Parameters.AddWithValue("@dei",e.DeducteeId);
                cmd.Parameters.AddWithValue("@s",  e.Section);
                cmd.Parameters.AddWithValue("@pn", e.PaymentNature);
                cmd.Parameters.AddWithValue("@am", e.Amount);
                cmd.Parameters.AddWithValue("@ra", e.Rate);
                cmd.Parameters.AddWithValue("@ta", Math.Round(e.TdsAmount,  MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@su", Math.Round(e.Surcharge,  MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@ce", Math.Round(e.Cess,       MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@tt", Math.Round(e.TotalTds,   MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@dd", e.DueDate?.ToString("yyyy-MM-dd") ?? "");
                cmd.Parameters.AddWithValue("@pd", e.PaymentDate?.ToString("yyyy-MM-dd") ?? "");
                cmd.Parameters.AddWithValue("@in", e.Interest);
                cmd.Parameters.AddWithValue("@lf", e.LateFee);
                cmd.Parameters.AddWithValue("@cn", e.ChallanNo);
                cmd.Parameters.AddWithValue("@rm", e.Remarks);
                cmd.Parameters.AddWithValue("@st", e.Status);
                cmd.Parameters.AddWithValue("@fy", e.FinancialYear);
                cmd.Parameters.AddWithValue("@qt", e.Quarter);
                cmd.ExecuteNonQuery();
                TdsLog.Write($"[DAL.Save] Wrote to DB — Id={e.Id} EntryNo={e.EntryNo} Section={e.Section} Amount={e.Amount} Rate={e.Rate} TdsAmount={e.TdsAmount} Surcharge={e.Surcharge} Cess={e.Cess} TotalTds={e.TotalTds} Interest={e.Interest} LateFee={e.LateFee}");
                return (true, "Entry saved.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public TdsEntry? GetByRemarksTag(int deductorId, string tag)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT e.*, d.company_name as deductor_name,
                                       dd.name as deductee_name, dd.pan as deductee_pan
                                FROM tds_entries e
                                LEFT JOIN deductors d  ON e.deductor_id = d.id
                                LEFT JOIN deductees dd ON e.deductee_id = dd.id
                                WHERE e.deductor_id=@did AND e.remarks LIKE @tag LIMIT 1";
            cmd.Parameters.AddWithValue("@did", deductorId);
            cmd.Parameters.AddWithValue("@tag", $"%{tag}%");
            using var r = cmd.ExecuteReader();
            return r.Read() ? Map(r) : null;
        }

        public (bool Ok, string Msg) Delete(int id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM tds_entries WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return (true, "Entry deleted.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // Safe column reader — returns default if column doesn't exist in result set
        private static string SafeGetString(SqliteDataReader r, string col, string def = "")
        {
            try
            {
                int ord = r.GetOrdinal(col);
                return r.IsDBNull(ord) ? def : r.GetString(ord);
            }
            catch (ArgumentOutOfRangeException) { return def; }
        }
        private static double SafeGetDouble(SqliteDataReader r, string col, double def = 0)
        {
            try
            {
                int ord = r.GetOrdinal(col);
                return r.IsDBNull(ord) ? def : r.GetDouble(ord);
            }
            catch (ArgumentOutOfRangeException) { return def; }
        }
        private static int SafeGetInt(SqliteDataReader r, string col, int def = 0)
        {
            try
            {
                int ord = r.GetOrdinal(col);
                return r.IsDBNull(ord) ? def : r.GetInt32(ord);
            }
            catch (ArgumentOutOfRangeException) { return def; }
        }

        private static TdsEntry Map(SqliteDataReader r)
        {
            var e = new TdsEntry
            {
                Id            = r.GetInt32(r.GetOrdinal("id")),
                EntryNo       = r.GetString(r.GetOrdinal("entry_no")),
                EntryDate     = DateTime.Parse(r.GetString(r.GetOrdinal("entry_date"))),
                DeductorId    = r.GetInt32(r.GetOrdinal("deductor_id")),
                DeducteeId    = r.GetInt32(r.GetOrdinal("deductee_id")),
                Section       = r.GetString(r.GetOrdinal("section")),
                PaymentNature = SafeGetString(r, "nature_of_payment"),
                Amount        = r.GetDouble(r.GetOrdinal("amount")),
                Rate          = r.GetDouble(r.GetOrdinal("rate")),
                TdsAmount     = r.GetDouble(r.GetOrdinal("tds_amount")),
                Surcharge     = r.GetDouble(r.GetOrdinal("surcharge")),
                Cess          = r.GetDouble(r.GetOrdinal("cess")),
                TotalTds      = r.GetDouble(r.GetOrdinal("total_tds")),
                Interest      = r.GetDouble(r.GetOrdinal("interest")),
                LateFee       = r.GetDouble(r.GetOrdinal("late_fee")),
                ChallanNo     = r.IsDBNull(r.GetOrdinal("challan_no")) ? "" : r.GetString(r.GetOrdinal("challan_no")),
                Remarks       = r.IsDBNull(r.GetOrdinal("remarks"))    ? "" : r.GetString(r.GetOrdinal("remarks")),
                Status        = r.IsDBNull(r.GetOrdinal("status"))     ? "Pending" : r.GetString(r.GetOrdinal("status")),
                FinancialYear = r.IsDBNull(r.GetOrdinal("financial_year")) ? "2024-25" : r.GetString(r.GetOrdinal("financial_year")),
                Quarter       = r.IsDBNull(r.GetOrdinal("quarter"))    ? "Q1" : r.GetString(r.GetOrdinal("quarter")),
                DeductorName  = r.IsDBNull(r.GetOrdinal("deductor_name"))  ? "" : r.GetString(r.GetOrdinal("deductor_name")),
                DeducteeName  = r.IsDBNull(r.GetOrdinal("deductee_name"))  ? "" : r.GetString(r.GetOrdinal("deductee_name")),
                DeducteePan   = r.IsDBNull(r.GetOrdinal("deductee_pan"))   ? "" : r.GetString(r.GetOrdinal("deductee_pan")),
            };
            TdsLog.Write($"[DAL.Map] Read from DB — EntryNo={e.EntryNo} Section={e.Section} Amount={e.Amount} Rate={e.Rate} TdsAmount={e.TdsAmount} Surcharge={e.Surcharge} Cess={e.Cess} TotalTds={e.TotalTds} Interest={e.Interest} LateFee={e.LateFee}");
            return e;
        }
    }

    // ── Challan Repository ────────────────────────────────────────────────────
    public class ChallanRepository
    {
        public List<Challan> GetAll(int? deductorId = null, string? fy = null)
        {
            var list = new List<Challan>();
            using var conn = Database.GetConnection();
            using var cmd = conn.CreateCommand();
            var where = "WHERE 1=1";
            if (deductorId.HasValue) { where += " AND c.deductor_id=@did"; cmd.Parameters.AddWithValue("@did", deductorId); }
            if (!string.IsNullOrEmpty(fy)) { where += " AND c.financial_year=@fy"; cmd.Parameters.AddWithValue("@fy", fy); }
            cmd.CommandText = $@"
                SELECT c.*, d.company_name as deductor_name
                FROM challans c
                LEFT JOIN deductors d ON c.deductor_id=d.id
                {where} ORDER BY c.challan_date DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Map(r));
            return list;
        }

        public (bool Ok, string Msg) Save(Challan c)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = conn.CreateCommand();
                if (c.Id == 0)
                {
                    cmd.CommandText = @"INSERT INTO challans
                        (challan_no,challan_date,deductor_id,bsr_code,section,amount,
                         tds_amount,surcharge,cess,interest,late_fee,total_amount,
                         bank_name,ack_no,quarter,financial_year,status,remarks,minor_head_code)
                        VALUES(@cn,@cd,@di,@bs,@s,@am,@ta,@su,@ce,@in,@lf,@to,
                               @bn,@an,@qt,@fy,@st,@rm,@mh)
                        ON CONFLICT(bsr_code,challan_no,challan_date,deductor_id) DO UPDATE SET
                            section=excluded.section, amount=excluded.amount,
                            tds_amount=excluded.tds_amount, surcharge=excluded.surcharge,
                            cess=excluded.cess, interest=excluded.interest,
                            late_fee=excluded.late_fee, total_amount=excluded.total_amount,
                            bank_name=excluded.bank_name, ack_no=excluded.ack_no,
                            quarter=excluded.quarter, financial_year=excluded.financial_year,
                            status=excluded.status, remarks=excluded.remarks,
                            minor_head_code=excluded.minor_head_code";
                }
                else
                {
                    cmd.CommandText = @"UPDATE challans SET
                        challan_no=@cn,challan_date=@cd,deductor_id=@di,bsr_code=@bs,
                        section=@s,amount=@am,tds_amount=@ta,surcharge=@su,cess=@ce,
                        interest=@in,late_fee=@lf,total_amount=@to,bank_name=@bn,
                        ack_no=@an,quarter=@qt,financial_year=@fy,status=@st,remarks=@rm,
                        minor_head_code=@mh
                        WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", c.Id);
                }
                cmd.Parameters.AddWithValue("@cn", c.ChallanNo);
                cmd.Parameters.AddWithValue("@cd", c.ChallanDate.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("@di", (object?)c.DeductorId ?? DBNull.Value);
                cmd.Parameters.AddWithValue("@bs", c.BsrCode);
                cmd.Parameters.AddWithValue("@s",  c.Section);
                cmd.Parameters.AddWithValue("@am", c.Amount);
                cmd.Parameters.AddWithValue("@ta", Math.Round(c.TdsAmount,  MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@su", Math.Round(c.Surcharge,  MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@ce", Math.Round(c.Cess,       MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@in", Math.Round(c.Interest,   MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@lf", Math.Round(c.LateFee,    MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@to", Math.Round(c.TotalAmount, MidpointRounding.AwayFromZero));
                cmd.Parameters.AddWithValue("@bn", c.BankName);
                cmd.Parameters.AddWithValue("@an", c.AckNo);
                cmd.Parameters.AddWithValue("@qt", c.Quarter);
                cmd.Parameters.AddWithValue("@fy", c.FinancialYear);
                cmd.Parameters.AddWithValue("@st", c.Status);
                cmd.Parameters.AddWithValue("@rm", c.Remarks);
                cmd.Parameters.AddWithValue("@mh", c.MinorHeadCode);
                cmd.ExecuteNonQuery();
                return (true, "Challan saved.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public (bool Ok, string Msg) Delete(int id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM challans WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return (true, "Challan deleted.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        private static Challan Map(SqliteDataReader r) => new()
        {
            Id            = r.GetInt32(r.GetOrdinal("id")),
            ChallanNo     = r.GetString(r.GetOrdinal("challan_no")),
            ChallanDate   = DateTime.Parse(r.GetString(r.GetOrdinal("challan_date"))),
            DeductorId    = r.IsDBNull(r.GetOrdinal("deductor_id")) ? null : r.GetInt32(r.GetOrdinal("deductor_id")),
            BsrCode       = r.GetString(r.GetOrdinal("bsr_code")),
            Section       = r.IsDBNull(r.GetOrdinal("section"))       ? "" : r.GetString(r.GetOrdinal("section")),
            Amount        = r.GetDouble(r.GetOrdinal("amount")),
            TdsAmount     = r.GetDouble(r.GetOrdinal("tds_amount")),
            Surcharge     = r.GetDouble(r.GetOrdinal("surcharge")),
            Cess          = r.GetDouble(r.GetOrdinal("cess")),
            Interest      = r.GetDouble(r.GetOrdinal("interest")),
            LateFee       = r.GetDouble(r.GetOrdinal("late_fee")),
            TotalAmount   = r.GetDouble(r.GetOrdinal("total_amount")),
            BankName      = r.IsDBNull(r.GetOrdinal("bank_name"))     ? "" : r.GetString(r.GetOrdinal("bank_name")),
            AckNo         = r.IsDBNull(r.GetOrdinal("ack_no"))        ? "" : r.GetString(r.GetOrdinal("ack_no")),
            Quarter       = r.IsDBNull(r.GetOrdinal("quarter"))       ? "Q1" : r.GetString(r.GetOrdinal("quarter")),
            FinancialYear = r.IsDBNull(r.GetOrdinal("financial_year"))? "2024-25" : r.GetString(r.GetOrdinal("financial_year")),
            Status        = r.IsDBNull(r.GetOrdinal("status"))        ? "Paid" : r.GetString(r.GetOrdinal("status")),
            Remarks         = r.IsDBNull(r.GetOrdinal("remarks"))          ? "" : r.GetString(r.GetOrdinal("remarks")),
            MinorHeadCode   = r.IsDBNull(r.GetOrdinal("minor_head_code"))  ? "200" : r.GetString(r.GetOrdinal("minor_head_code")),
            DeductorName    = r.IsDBNull(r.GetOrdinal("deductor_name"))    ? "" : r.GetString(r.GetOrdinal("deductor_name")),
        };
    }

    // ── Dashboard Repository ──────────────────────────────────────────────────
    public class DashboardRepository
    {
        public DashboardStats GetStats(string fy, int? deductorId = null)
        {
            using var conn = Database.GetConnection();
            var stats = new DashboardStats();
            var didFilter = deductorId.HasValue && deductorId.Value > 0 ? " AND deductor_id=@did" : "";

            long Q(string sql) {
                using var c = conn.CreateCommand();
                c.CommandText = sql;
                c.Parameters.AddWithValue("@fy", fy);
                if (deductorId.HasValue && deductorId.Value > 0) c.Parameters.AddWithValue("@did", deductorId.Value);
                return (long)(c.ExecuteScalar() ?? 0L);
            }
            double QD(string sql) {
                using var c = conn.CreateCommand();
                c.CommandText = sql;
                c.Parameters.AddWithValue("@fy", fy);
                if (deductorId.HasValue && deductorId.Value > 0) c.Parameters.AddWithValue("@did", deductorId.Value);
                var v = c.ExecuteScalar();
                return v == null || v == DBNull.Value ? 0 : Convert.ToDouble(v);
            }

            stats.TotalEntries   = (int)Q($"SELECT COUNT(*) FROM tds_entries WHERE financial_year=@fy{didFilter}");
            stats.TotalTds       = QD($"SELECT COALESCE(SUM(total_tds),0) FROM tds_entries WHERE financial_year=@fy{didFilter}");
            stats.TotalAmount    = QD($"SELECT COALESCE(SUM(amount),0) FROM tds_entries WHERE financial_year=@fy{didFilter}");
            stats.TotalChallans  = (int)Q($"SELECT COUNT(*) FROM challans WHERE financial_year=@fy{didFilter}");
            stats.PendingEntries = (int)Q($"SELECT COUNT(*) FROM tds_entries WHERE status='Pending' AND financial_year=@fy{didFilter}");

            // Deductees scoped to this deductor (via tds_entries join); fallback to global count if no deductor
            if (deductorId.HasValue && deductorId.Value > 0)
            {
                using var cd = conn.CreateCommand();
                cd.CommandText = "SELECT COUNT(DISTINCT deductee_id) FROM tds_entries WHERE deductor_id=@did AND financial_year=@fy";
                cd.Parameters.AddWithValue("@did", deductorId.Value);
                cd.Parameters.AddWithValue("@fy",  fy);
                stats.TotalDeductees = Convert.ToInt32(cd.ExecuteScalar() ?? 0);
            }
            else
            {
                using var c2 = conn.CreateCommand();
                c2.CommandText = "SELECT COUNT(*) FROM deductees";
                stats.TotalDeductees = Convert.ToInt32(c2.ExecuteScalar() ?? 0);
            }

            using var c3 = conn.CreateCommand();
            c3.CommandText = "SELECT COUNT(*) FROM deductors WHERE is_active=1";
            stats.TotalDeductors = Convert.ToInt32(c3.ExecuteScalar() ?? 0);

            return stats;
        }
    }
}
