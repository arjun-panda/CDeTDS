using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    public class PayrollRepository
    {
        // ══════════════════════════════════════════════════════════════════════
        // EMPLOYEES
        // ══════════════════════════════════════════════════════════════════════
        public List<Employee> GetAllEmployees(int? deductorId = null)
        {
            var list = new List<Employee>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT e.id, e.employee_code, e.name, e.pan, e.deductor_id,
                       e.designation, e.department, e.join_date, e.leaving_date,
                       e.tax_regime, e.is_active, e.email, e.phone,
                       e.bank_account, e.bank_ifsc,
                       e.fathers_name, e.date_of_birth,
                       e.sex, e.pf_number, e.ward_circle_range,
                       e.std_code, e.telephone_no,
                       e.flat_door_block_no, e.premises_building_village,
                       e.road_street_post_office, e.area_locality,
                       e.town_city_district, e.pin_code, e.state,
                       e.hra_monthly_basis, e.hra_city_type, e.da_for_retirement, e.is_differently_abled,
                       e.aadhaar_number, e.residential_status, e.marital_status,
                       e.blood_group, e.employment_type,
                       e.work_email, e.emergency_contact, e.emergency_mobile,
                       e.uan, e.esi_ip_number,
                       e.bank_name, e.bank_branch, e.bank_account_type,
                       e.prev_employer_name, e.prev_employer_income, e.prev_employer_tds,
                       s.id, s.basic, s.hra, s.da, s.special_allowance,
                       s.other_allowance, s.pf_applicable, s.pf_fixed_amount, s.esi_applicable,
                       s.pt_state, s.effective_from, s.medical_allowance, s.lta,
                       s.reimb_telephone, s.reimb_fuel, s.reimb_books, s.reimb_meal, s.reimb_uniform,
                       s.annual_bonus, s.annual_incentive, s.employer_insurance, s.employer_nps,
                       s.target_ctc, s.include_gratuity
                FROM employees e
                LEFT JOIN salary_structures s ON s.employee_id = e.id
                    AND s.id = (SELECT MAX(id) FROM salary_structures WHERE employee_id = e.id)
                WHERE e.is_active=1 AND (@did IS NULL OR e.deductor_id = @did)
                ORDER BY e.name";
            cmd.Parameters.AddWithValue("@did", (deductorId == null || deductorId == 0) ? (object)DBNull.Value : deductorId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var emp = new Employee
                {
                    Id           = r.GetInt32(0),
                    EmployeeCode = r.GetString(1),
                    Name         = r.GetString(2),
                    Pan          = r.GetString(3),
                    DeductorId   = r.GetInt32(4),
                    Designation  = r.GetString(5),
                    Department   = r.GetString(6),
                    JoinDate     = r.IsDBNull(7)  ? "" : r.GetString(7),
                    LeavingDate  = r.IsDBNull(8)  ? "" : r.GetString(8),
                    TaxRegime    = r.IsDBNull(9)  ? "New" : r.GetString(9),
                    IsActive     = r.GetInt32(10) == 1,
                    Email        = r.IsDBNull(11) ? "" : r.GetString(11),
                    Phone        = r.IsDBNull(12) ? "" : r.GetString(12),
                    BankAccount  = r.IsDBNull(13) ? "" : r.GetString(13),
                    BankIfsc     = r.IsDBNull(14) ? "" : r.GetString(14),
                    FathersName  = r.IsDBNull(15) ? "" : r.GetString(15),
                    DateOfBirth  = r.IsDBNull(16) ? "" : r.GetString(16),
                    Sex                    = r.IsDBNull(17) ? "Male" : r.GetString(17),
                    PfNumber               = r.IsDBNull(18) ? "" : r.GetString(18),
                    WardCircleRange        = r.IsDBNull(19) ? "" : r.GetString(19),
                    StdCode                = r.IsDBNull(20) ? "" : r.GetString(20),
                    TelephoneNo            = r.IsDBNull(21) ? "" : r.GetString(21),
                    FlatDoorBlockNo        = r.IsDBNull(22) ? "" : r.GetString(22),
                    PremisesBuildingVillage= r.IsDBNull(23) ? "" : r.GetString(23),
                    RoadStreetPostOffice   = r.IsDBNull(24) ? "" : r.GetString(24),
                    AreaLocality           = r.IsDBNull(25) ? "" : r.GetString(25),
                    TownCityDistrict       = r.IsDBNull(26) ? "" : r.GetString(26),
                    PinCode                = r.IsDBNull(27) ? "" : r.GetString(27),
                    State                  = r.IsDBNull(28) ? "" : r.GetString(28),
                    HraMonthlyBasis        = r.IsDBNull(29) ? true  : r.GetInt32(29) == 1,
                    HraCityType            = r.IsDBNull(30) ? "Non-Metro" : r.GetString(30),
                    DaForRetirement        = r.IsDBNull(31) ? true  : r.GetInt32(31) == 1,
                    IsDifferentlyAbled     = r.IsDBNull(32) ? false : r.GetInt32(32) == 1,
                    // extended fields (33-48)
                    AadhaarNumber      = r.IsDBNull(33) ? "" : r.GetString(33),
                    ResidentialStatus  = r.IsDBNull(34) ? "Resident"  : r.GetString(34),
                    MaritalStatus      = r.IsDBNull(35) ? "Single"    : r.GetString(35),
                    BloodGroup         = r.IsDBNull(36) ? "" : r.GetString(36),
                    EmploymentType     = r.IsDBNull(37) ? "Permanent" : r.GetString(37),
                    WorkEmail          = r.IsDBNull(38) ? "" : r.GetString(38),
                    EmergencyContact   = r.IsDBNull(39) ? "" : r.GetString(39),
                    EmergencyMobile    = r.IsDBNull(40) ? "" : r.GetString(40),
                    Uan                = r.IsDBNull(41) ? "" : r.GetString(41),
                    EsiIpNumber        = r.IsDBNull(42) ? "" : r.GetString(42),
                    BankName           = r.IsDBNull(43) ? "" : r.GetString(43),
                    BankBranch         = r.IsDBNull(44) ? "" : r.GetString(44),
                    BankAccountType    = r.IsDBNull(45) ? "Savings" : r.GetString(45),
                    PrevEmployerName   = r.IsDBNull(46) ? "" : r.GetString(46),
                    PrevEmployerIncome = r.IsDBNull(47) ? 0  : Convert.ToDouble(r[47]),
                    PrevEmployerTds    = r.IsDBNull(48) ? 0  : Convert.ToDouble(r[48]),
                };
                if (!r.IsDBNull(49))
                    emp.Salary = new SalaryStructure
                    {
                        Id               = r.GetInt32(49),
                        EmployeeId       = emp.Id,
                        Basic            = r.IsDBNull(50) ? 0 : Convert.ToDouble(r[50]),
                        Hra              = r.IsDBNull(51) ? 0 : Convert.ToDouble(r[51]),
                        Da               = r.IsDBNull(52) ? 0 : Convert.ToDouble(r[52]),
                        SpecialAllowance = r.IsDBNull(53) ? 0 : Convert.ToDouble(r[53]),
                        OtherAllowance   = r.IsDBNull(54) ? 0     : Convert.ToDouble(r[54]),
                        PfApplicable     = r.IsDBNull(55) ? true  : r.GetInt32(55) == 1,
                        PfFixedAmount    = r.IsDBNull(56) ? 0     : Convert.ToDouble(r[56]),
                        EsiApplicable    = r.IsDBNull(57) ? false : r.GetInt32(57) == 1,
                        PtState          = r.IsDBNull(58) ? ""    : r.GetString(58),
                        EffectiveFrom    = r.IsDBNull(59) ? ""    : r.GetString(59),
                        MedicalAllowance = r.IsDBNull(60) ? 0     : Convert.ToDouble(r[60]),
                        Lta              = r.IsDBNull(61) ? 0     : Convert.ToDouble(r[61]),
                        ReimbTelephone   = r.IsDBNull(62) ? 0     : Convert.ToDouble(r[62]),
                        ReimbFuel        = r.IsDBNull(63) ? 0     : Convert.ToDouble(r[63]),
                        ReimbBooks       = r.IsDBNull(64) ? 0     : Convert.ToDouble(r[64]),
                        ReimbMeal        = r.IsDBNull(65) ? 0     : Convert.ToDouble(r[65]),
                        ReimbUniform     = r.IsDBNull(66) ? 0     : Convert.ToDouble(r[66]),
                        AnnualBonus      = r.IsDBNull(67) ? 0     : Convert.ToDouble(r[67]),
                        AnnualIncentive  = r.IsDBNull(68) ? 0     : Convert.ToDouble(r[68]),
                        EmployerInsurance= r.IsDBNull(69) ? 0     : Convert.ToDouble(r[69]),
                        EmployerNps      = r.IsDBNull(70) ? 0     : Convert.ToDouble(r[70]),
                        TargetCtc        = r.IsDBNull(71) ? 0     : Convert.ToDouble(r[71]),
                        IncludeGratuity  = r.IsDBNull(72) ? true  : r.GetInt32(72) == 1,
                    };
                list.Add(emp);
            }
            r.Close();
            // Load components for each salary structure
            foreach (var e in list)
                if (e.Salary != null && e.Salary.Id > 0)
                    e.Salary.Components = GetComponents(e.Salary.Id);
            return list;
        }

        public (bool ok, string msg) SaveEmployee(Employee e)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd  = conn.CreateCommand();
                if (e.Id == 0)
                {
                    cmd.CommandText = @"
                        INSERT INTO employees
                            (employee_code,name,pan,deductor_id,designation,department,
                             join_date,leaving_date,tax_regime,is_active,email,phone,
                             bank_account,bank_ifsc,fathers_name,date_of_birth,
                             sex,pf_number,ward_circle_range,std_code,telephone_no,
                             flat_door_block_no,premises_building_village,road_street_post_office,
                             area_locality,town_city_district,pin_code,state,
                             hra_monthly_basis,hra_city_type,da_for_retirement,is_differently_abled,
                             aadhaar_number,residential_status,marital_status,blood_group,employment_type,
                             work_email,emergency_contact,emergency_mobile,uan,esi_ip_number,
                             bank_name,bank_branch,bank_account_type,
                             prev_employer_name,prev_employer_income,prev_employer_tds)
                        VALUES(@ec,@n,@p,@did,@des,@dep,@jd,@ld,@tr,@ia,@em,@ph,@ba,@bi,@fn,@dob,
                               @sx,@pfn,@wcr,@stdc,@teln,
                               @fdb,@pbv,@rspo,@al,@tcd,@pin,@st,
                               @hmb,@hct,@dfr,@ida,
                               @aan,@rs,@ms,@bg,@et,
                               @we,@ec2,@em2,@uan,@esi2,
                               @bn,@bb,@bat,
                               @pen,@pei,@pet)";
                }
                else
                {
                    cmd.CommandText = @"
                        UPDATE employees SET
                            employee_code=@ec,name=@n,pan=@p,deductor_id=@did,
                            designation=@des,department=@dep,join_date=@jd,leaving_date=@ld,
                            tax_regime=@tr,is_active=@ia,email=@em,phone=@ph,
                            bank_account=@ba,bank_ifsc=@bi,fathers_name=@fn,date_of_birth=@dob,
                            sex=@sx,pf_number=@pfn,ward_circle_range=@wcr,
                            std_code=@stdc,telephone_no=@teln,
                            flat_door_block_no=@fdb,premises_building_village=@pbv,
                            road_street_post_office=@rspo,area_locality=@al,
                            town_city_district=@tcd,pin_code=@pin,state=@st,
                            hra_monthly_basis=@hmb,hra_city_type=@hct,da_for_retirement=@dfr,is_differently_abled=@ida,
                            aadhaar_number=@aan,residential_status=@rs,marital_status=@ms,
                            blood_group=@bg,employment_type=@et,
                            work_email=@we,emergency_contact=@ec2,emergency_mobile=@em2,
                            uan=@uan,esi_ip_number=@esi2,
                            bank_name=@bn,bank_branch=@bb,bank_account_type=@bat,
                            prev_employer_name=@pen,prev_employer_income=@pei,prev_employer_tds=@pet
                        WHERE id=@id";
                    cmd.Parameters.AddWithValue("@id", e.Id);
                }
                cmd.Parameters.AddWithValue("@ec",  e.EmployeeCode);
                cmd.Parameters.AddWithValue("@n",   e.Name);
                cmd.Parameters.AddWithValue("@p",   e.Pan.ToUpper());
                cmd.Parameters.AddWithValue("@did", e.DeductorId);
                cmd.Parameters.AddWithValue("@des", e.Designation);
                cmd.Parameters.AddWithValue("@dep", e.Department);
                cmd.Parameters.AddWithValue("@jd",  e.JoinDate);
                cmd.Parameters.AddWithValue("@ld",  e.LeavingDate);
                cmd.Parameters.AddWithValue("@tr",  e.TaxRegime);
                cmd.Parameters.AddWithValue("@ia",  e.IsActive ? 1 : 0);
                cmd.Parameters.AddWithValue("@em",  e.Email);
                cmd.Parameters.AddWithValue("@ph",  e.Phone);
                cmd.Parameters.AddWithValue("@ba",  e.BankAccount);
                cmd.Parameters.AddWithValue("@bi",  e.BankIfsc);
                cmd.Parameters.AddWithValue("@fn",   e.FathersName);
                cmd.Parameters.AddWithValue("@dob",  e.DateOfBirth);
                cmd.Parameters.AddWithValue("@sx",   e.Sex);
                cmd.Parameters.AddWithValue("@pfn",  e.PfNumber);
                cmd.Parameters.AddWithValue("@wcr",  e.WardCircleRange);
                cmd.Parameters.AddWithValue("@stdc", e.StdCode);
                cmd.Parameters.AddWithValue("@teln", e.TelephoneNo);
                cmd.Parameters.AddWithValue("@fdb",  e.FlatDoorBlockNo);
                cmd.Parameters.AddWithValue("@pbv",  e.PremisesBuildingVillage);
                cmd.Parameters.AddWithValue("@rspo", e.RoadStreetPostOffice);
                cmd.Parameters.AddWithValue("@al",   e.AreaLocality);
                cmd.Parameters.AddWithValue("@tcd",  e.TownCityDistrict);
                cmd.Parameters.AddWithValue("@pin",  e.PinCode);
                cmd.Parameters.AddWithValue("@st",   e.State);
                cmd.Parameters.AddWithValue("@hmb",  e.HraMonthlyBasis ? 1 : 0);
                cmd.Parameters.AddWithValue("@hct",  string.IsNullOrEmpty(e.HraCityType) ? "Non-Metro" : e.HraCityType);
                cmd.Parameters.AddWithValue("@dfr",  e.DaForRetirement ? 1 : 0);
                cmd.Parameters.AddWithValue("@ida",  e.IsDifferentlyAbled ? 1 : 0);
                cmd.Parameters.AddWithValue("@aan",  e.AadhaarNumber);
                cmd.Parameters.AddWithValue("@rs",   e.ResidentialStatus);
                cmd.Parameters.AddWithValue("@ms",   e.MaritalStatus);
                cmd.Parameters.AddWithValue("@bg",   e.BloodGroup);
                cmd.Parameters.AddWithValue("@et",   e.EmploymentType);
                cmd.Parameters.AddWithValue("@we",   e.WorkEmail);
                cmd.Parameters.AddWithValue("@ec2",  e.EmergencyContact);
                cmd.Parameters.AddWithValue("@em2",  e.EmergencyMobile);
                cmd.Parameters.AddWithValue("@uan",  e.Uan);
                cmd.Parameters.AddWithValue("@esi2", e.EsiIpNumber);
                cmd.Parameters.AddWithValue("@bn",   e.BankName);
                cmd.Parameters.AddWithValue("@bb",   e.BankBranch);
                cmd.Parameters.AddWithValue("@bat",  e.BankAccountType);
                cmd.Parameters.AddWithValue("@pen",  e.PrevEmployerName);
                cmd.Parameters.AddWithValue("@pei",  e.PrevEmployerIncome);
                cmd.Parameters.AddWithValue("@pet",  e.PrevEmployerTds);
                cmd.ExecuteNonQuery();

                if (e.Id == 0)
                {
                    using var lid = conn.CreateCommand();
                    lid.CommandText = "SELECT last_insert_rowid()";
                    e.Id = Convert.ToInt32(lid.ExecuteScalar());
                }

                // Save salary structure
                if (e.Salary != null)
                    SaveSalaryStructure(e.Id, e.Salary);

                return (true, $"Employee '{e.Name}' saved.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        public void SaveSalaryStructure(int employeeId, SalaryStructure s)
        {
            using var conn = Database.GetConnection();
            // Transactional: structure UPDATE/INSERT + Components delete + re-inserts
            // must all succeed or all roll back. Prevents orphan Components on partial failure.
            using var tx = conn.BeginTransaction();
            try
            {

            // Find the current latest row for this employee
            using var findCmd = conn.CreateCommand();
            findCmd.Transaction = tx;
            findCmd.CommandText = @"SELECT id FROM salary_structures
                                    WHERE employee_id=@eid
                                    ORDER BY id DESC LIMIT 1";
            findCmd.Parameters.AddWithValue("@eid", employeeId);

            int existingId = 0;
            using (var r = findCmd.ExecuteReader())
                if (r.Read()) existingId = r.GetInt32(0);
            // Note: previously short-circuited when base fields were unchanged, which
            // caused Components/legacy-zero writes to be skipped. Always run the
            // UPDATE + Components replace path so child rows stay in sync.
            string effectiveFrom = string.IsNullOrEmpty(s.EffectiveFrom)
                ? DateTime.Today.ToString("yyyy-MM-dd")
                : s.EffectiveFrom;

            if (existingId > 0)
            {
                // Update the existing row in-place — preserves salary history row identity
                using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = @"
                    UPDATE salary_structures SET
                        basic=@b, hra=@h, da=@d,
                        special_allowance=@sa, other_allowance=@oa,
                        pf_applicable=@pf, pf_fixed_amount=@pfa,
                        esi_applicable=@esi, pt_state=@pt, effective_from=@ef,
                        medical_allowance=@ma, lta=@lta,
                        reimb_telephone=@rt, reimb_fuel=@rf, reimb_books=@rb,
                        reimb_meal=@rm, reimb_uniform=@ru,
                        annual_bonus=@ab, annual_incentive=@ai,
                        employer_insurance=@ei, employer_nps=@en,
                        target_ctc=@tc, include_gratuity=@ig
                    WHERE id=@id";
                upd.Parameters.AddWithValue("@id",  existingId);
                upd.Parameters.AddWithValue("@b",   s.Basic);
                upd.Parameters.AddWithValue("@h",   s.Hra);
                upd.Parameters.AddWithValue("@d",   s.Da);
                upd.Parameters.AddWithValue("@sa",  s.SpecialAllowance);
                upd.Parameters.AddWithValue("@oa",  s.OtherAllowance);
                upd.Parameters.AddWithValue("@pf",  s.PfApplicable ? 1 : 0);
                upd.Parameters.AddWithValue("@pfa", s.PfFixedAmount);
                upd.Parameters.AddWithValue("@esi", s.EsiApplicable ? 1 : 0);
                upd.Parameters.AddWithValue("@pt",  s.PtState);
                upd.Parameters.AddWithValue("@ef",  effectiveFrom);
                upd.Parameters.AddWithValue("@ma",  s.MedicalAllowance);
                upd.Parameters.AddWithValue("@lta", s.Lta);
                upd.Parameters.AddWithValue("@rt",  s.ReimbTelephone);
                upd.Parameters.AddWithValue("@rf",  s.ReimbFuel);
                upd.Parameters.AddWithValue("@rb",  s.ReimbBooks);
                upd.Parameters.AddWithValue("@rm",  s.ReimbMeal);
                upd.Parameters.AddWithValue("@ru",  s.ReimbUniform);
                upd.Parameters.AddWithValue("@ab",  s.AnnualBonus);
                upd.Parameters.AddWithValue("@ai",  s.AnnualIncentive);
                upd.Parameters.AddWithValue("@ei",  s.EmployerInsurance);
                upd.Parameters.AddWithValue("@en",  s.EmployerNps);
                upd.Parameters.AddWithValue("@tc",  s.TargetCtc);
                upd.Parameters.AddWithValue("@ig",  s.IncludeGratuity ? 1 : 0);
                upd.ExecuteNonQuery();
            }
            else
            {
                using var ins = conn.CreateCommand();
                ins.Transaction = tx;
                ins.CommandText = @"
                    INSERT INTO salary_structures
                        (employee_id,basic,hra,da,special_allowance,other_allowance,
                         pf_applicable,pf_fixed_amount,esi_applicable,pt_state,effective_from,
                         medical_allowance,lta,
                         reimb_telephone,reimb_fuel,reimb_books,reimb_meal,reimb_uniform,
                         annual_bonus,annual_incentive,employer_insurance,employer_nps,target_ctc,include_gratuity)
                    VALUES(@eid,@b,@h,@d,@sa,@oa,@pf,@pfa,@esi,@pt,@ef,@ma,@lta,
                           @rt,@rf,@rb,@rm,@ru,@ab,@ai,@ei,@en,@tc,@ig)";
                ins.Parameters.AddWithValue("@eid", employeeId);
                ins.Parameters.AddWithValue("@b",   s.Basic);
                ins.Parameters.AddWithValue("@h",   s.Hra);
                ins.Parameters.AddWithValue("@d",   s.Da);
                ins.Parameters.AddWithValue("@sa",  s.SpecialAllowance);
                ins.Parameters.AddWithValue("@oa",  s.OtherAllowance);
                ins.Parameters.AddWithValue("@pf",  s.PfApplicable ? 1 : 0);
                ins.Parameters.AddWithValue("@pfa", s.PfFixedAmount);
                ins.Parameters.AddWithValue("@esi", s.EsiApplicable ? 1 : 0);
                ins.Parameters.AddWithValue("@pt",  s.PtState);
                ins.Parameters.AddWithValue("@ef",  effectiveFrom);
                ins.Parameters.AddWithValue("@ma",  s.MedicalAllowance);
                ins.Parameters.AddWithValue("@lta", s.Lta);
                ins.Parameters.AddWithValue("@rt",  s.ReimbTelephone);
                ins.Parameters.AddWithValue("@rf",  s.ReimbFuel);
                ins.Parameters.AddWithValue("@rb",  s.ReimbBooks);
                ins.Parameters.AddWithValue("@rm",  s.ReimbMeal);
                ins.Parameters.AddWithValue("@ru",  s.ReimbUniform);
                ins.Parameters.AddWithValue("@ab",  s.AnnualBonus);
                ins.Parameters.AddWithValue("@ai",  s.AnnualIncentive);
                ins.Parameters.AddWithValue("@ei",  s.EmployerInsurance);
                ins.Parameters.AddWithValue("@en",  s.EmployerNps);
                ins.Parameters.AddWithValue("@tc",  s.TargetCtc);
                ins.Parameters.AddWithValue("@ig",  s.IncludeGratuity ? 1 : 0);
                ins.ExecuteNonQuery();
            }

            // Resolve current salary_structure id and replace components
            using var idCmd = conn.CreateCommand();
            idCmd.Transaction = tx;
            idCmd.CommandText = "SELECT id FROM salary_structures WHERE employee_id=@e ORDER BY id DESC LIMIT 1";
            idCmd.Parameters.AddWithValue("@e", employeeId);
            var idObj = idCmd.ExecuteScalar();
            int ssId = idObj == null ? 0 : Convert.ToInt32(idObj);
            s.Id = ssId;

            if (ssId > 0)
            {
                using var del = conn.CreateCommand();
                del.Transaction = tx;
                del.CommandText = "DELETE FROM salary_components WHERE salary_structure_id=@s";
                del.Parameters.AddWithValue("@s", ssId);
                del.ExecuteNonQuery();

                int ord = 0;
                foreach (var c in s.Components)
                {
                    using var insC = conn.CreateCommand();
                    insC.Transaction = tx;
                    insC.CommandText = @"INSERT INTO salary_components
                        (salary_structure_id,category,ordinal,name,received,paid,taxable,rule_ref)
                        VALUES(@s,@cat,@ord,@nm,@rc,@pd,@tx,@ru)";
                    insC.Parameters.AddWithValue("@s",   ssId);
                    insC.Parameters.AddWithValue("@cat", c.Category ?? "allowance");
                    insC.Parameters.AddWithValue("@ord", ord++);
                    insC.Parameters.AddWithValue("@nm",  c.Name ?? "");
                    insC.Parameters.AddWithValue("@rc",  c.Received);
                    insC.Parameters.AddWithValue("@pd",  c.Paid);
                    insC.Parameters.AddWithValue("@tx",  c.Taxable);
                    insC.Parameters.AddWithValue("@ru",  c.RuleRef ?? "");
                    insC.ExecuteNonQuery();
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

        public List<SalaryComponent> GetComponents(int salaryStructureId)
        {
            var list = new List<SalaryComponent>();
            if (salaryStructureId <= 0) return list;
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"SELECT id,salary_structure_id,category,ordinal,name,received,paid,taxable,rule_ref
                                FROM salary_components WHERE salary_structure_id=@s
                                ORDER BY category, ordinal";
            cmd.Parameters.AddWithValue("@s", salaryStructureId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new SalaryComponent {
                    Id = r.GetInt32(0), SalaryStructureId = r.GetInt32(1),
                    Category = r.GetString(2), Ordinal = r.GetInt32(3),
                    Name = r.IsDBNull(4) ? "" : r.GetString(4),
                    Received = r.IsDBNull(5) ? 0 : Convert.ToDouble(r[5]),
                    Paid     = r.IsDBNull(6) ? 0 : Convert.ToDouble(r[6]),
                    Taxable  = r.IsDBNull(7) ? 0 : Convert.ToDouble(r[7]),
                    RuleRef  = r.IsDBNull(8) ? "" : r.GetString(8),
                });
            return list;
        }

        /// <summary>
        /// Find an existing deductee row by PAN, or create a minimal one keyed to the employee.
        /// Used to link salary payroll to the TDS Entries module (Section 192).
        /// </summary>
        public int EnsureSalaryDeductee(Employee emp)
        {
            if (string.IsNullOrWhiteSpace(emp.Pan)) return 0;
            using var conn = Database.GetConnection();

            using var chk = conn.CreateCommand();
            chk.CommandText = "SELECT id FROM deductees WHERE pan=@p LIMIT 1";
            chk.Parameters.AddWithValue("@p", emp.Pan.ToUpper());
            var obj = chk.ExecuteScalar();
            if (obj != null && obj != DBNull.Value) return Convert.ToInt32(obj);

            using var cnt = conn.CreateCommand();
            cnt.CommandText = "SELECT COALESCE(MAX(id),0)+1 FROM deductees";
            int newId = Convert.ToInt32(cnt.ExecuteScalar() ?? 1);

            using var ins = conn.CreateCommand();
            ins.CommandText = @"INSERT INTO deductees
                (id,deductee_code,name,pan,section,rate,deductee_type,is_resident,remarks)
                VALUES(@id,@cd,@n,@p,@s,@r,@t,1,@rm)";
            ins.Parameters.AddWithValue("@id", newId);
            ins.Parameters.AddWithValue("@cd", $"EMP-{emp.EmployeeCode}");
            ins.Parameters.AddWithValue("@n",  emp.Name);
            ins.Parameters.AddWithValue("@p",  emp.Pan.ToUpper());
            ins.Parameters.AddWithValue("@s",  "192");
            ins.Parameters.AddWithValue("@r",  0);   // salary TDS rate computed per-employee
            ins.Parameters.AddWithValue("@t",  "Individual");
            ins.Parameters.AddWithValue("@rm", "Auto-created from Payroll module");
            ins.ExecuteNonQuery();
            return newId;
        }

        public (bool ok, string msg) DeleteEmployee(int id)
        {
            try
            {
                using var conn = Database.GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = "UPDATE employees SET is_active=0 WHERE id=@id";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.ExecuteNonQuery();
                return (true, "Employee deactivated.");
            }
            catch (Exception ex) { return (false, ex.Message); }
        }

        // ══════════════════════════════════════════════════════════════════════
        // DECLARATIONS
        // ══════════════════════════════════════════════════════════════════════
        public TaxDeclaration GetDeclaration(int employeeId, string fy)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id,rent_paid,hra_city_type,sec_80c,sec_80d_self,sec_80d_parents,
                       sec_80g,sec_80ccd_employee,sec_80ccd_employer,other_deductions,
                       income_other_sources,sec80e,sec80eea,sec80tta,sec80ttb,
                       sec80dd,sec80u,lta_exemption,landlord_pan,is_parent_senior
                FROM tax_declarations
                WHERE employee_id=@eid AND financial_year=@fy LIMIT 1";
            cmd.Parameters.AddWithValue("@eid", employeeId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            using var r = cmd.ExecuteReader();
            if (r.Read())
            {
                double Dbl(int i) => r.IsDBNull(i) ? 0 : Convert.ToDouble(r[i]);
                string Str(int i) => r.IsDBNull(i) ? "" : r.GetString(i);
                return new TaxDeclaration
                {
                    Id                   = r.GetInt32(0),
                    EmployeeId           = employeeId,
                    FinancialYear        = fy,
                    RentPaid             = Dbl(1),
                    HraCityType          = Str(2).Length > 0 ? Str(2) : "Non-Metro",
                    Sec80C               = Dbl(3),
                    Sec80D_Self          = Dbl(4),
                    Sec80D_Parents       = Dbl(5),
                    Sec80G               = Dbl(6),
                    Sec80CCD_Employee    = Dbl(7),
                    Sec80CCD_Employer    = Dbl(8),
                    OtherDeductions      = Dbl(9),
                    IncomeOtherSources   = Dbl(10),
                    Sec80E               = Dbl(11),
                    Sec80EEA             = Dbl(12),
                    Sec80TTA             = Dbl(13),
                    Sec80TTB             = Dbl(14),
                    Sec80DD              = Dbl(15),
                    Sec80U               = Dbl(16),
                    LtaExemption         = Dbl(17),
                    LandlordPan          = Str(18),
                    IsParentSeniorCitizen= !r.IsDBNull(19) && r.GetInt32(19) == 1,
                };
            }
            return new TaxDeclaration { EmployeeId = employeeId, FinancialYear = fy };
        }

        public void SaveDeclaration(TaxDeclaration d)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO tax_declarations
                    (employee_id,financial_year,rent_paid,hra_city_type,
                     sec_80c,sec_80d_self,sec_80d_parents,sec_80g,
                     sec_80ccd_employee,sec_80ccd_employer,other_deductions,income_other_sources,
                     sec80e,sec80eea,sec80tta,sec80ttb,sec80dd,sec80u,
                     lta_exemption,landlord_pan,is_parent_senior)
                VALUES(@eid,@fy,@rp,@hct,@s80c,@s80ds,@s80dp,@s80g,@s80ce,@s80cep,@od,@ois,
                       @s80e,@s80eea,@s80tta,@s80ttb,@s80dd,@s80u,@lta,@lpan,@ips)
                ON CONFLICT(employee_id,financial_year) DO UPDATE SET
                    rent_paid=excluded.rent_paid, hra_city_type=excluded.hra_city_type,
                    sec_80c=excluded.sec_80c, sec_80d_self=excluded.sec_80d_self,
                    sec_80d_parents=excluded.sec_80d_parents, sec_80g=excluded.sec_80g,
                    sec_80ccd_employee=excluded.sec_80ccd_employee,
                    sec_80ccd_employer=excluded.sec_80ccd_employer,
                    other_deductions=excluded.other_deductions,
                    income_other_sources=excluded.income_other_sources,
                    sec80e=excluded.sec80e, sec80eea=excluded.sec80eea,
                    sec80tta=excluded.sec80tta, sec80ttb=excluded.sec80ttb,
                    sec80dd=excluded.sec80dd, sec80u=excluded.sec80u,
                    lta_exemption=excluded.lta_exemption,
                    landlord_pan=excluded.landlord_pan,
                    is_parent_senior=excluded.is_parent_senior";
            cmd.Parameters.AddWithValue("@eid",   d.EmployeeId);
            cmd.Parameters.AddWithValue("@fy",    d.FinancialYear);
            cmd.Parameters.AddWithValue("@rp",    d.RentPaid);
            cmd.Parameters.AddWithValue("@hct",   d.HraCityType);
            cmd.Parameters.AddWithValue("@s80c",  d.Sec80C);
            cmd.Parameters.AddWithValue("@s80ds", d.Sec80D_Self);
            cmd.Parameters.AddWithValue("@s80dp", d.Sec80D_Parents);
            cmd.Parameters.AddWithValue("@s80g",  d.Sec80G);
            cmd.Parameters.AddWithValue("@s80ce", d.Sec80CCD_Employee);
            cmd.Parameters.AddWithValue("@s80cep",d.Sec80CCD_Employer);
            cmd.Parameters.AddWithValue("@od",    d.OtherDeductions);
            cmd.Parameters.AddWithValue("@ois",   d.IncomeOtherSources);
            cmd.Parameters.AddWithValue("@s80e",  d.Sec80E);
            cmd.Parameters.AddWithValue("@s80eea",d.Sec80EEA);
            cmd.Parameters.AddWithValue("@s80tta",d.Sec80TTA);
            cmd.Parameters.AddWithValue("@s80ttb",d.Sec80TTB);
            cmd.Parameters.AddWithValue("@s80dd", d.Sec80DD);
            cmd.Parameters.AddWithValue("@s80u",  d.Sec80U);
            cmd.Parameters.AddWithValue("@lta",   d.LtaExemption);
            cmd.Parameters.AddWithValue("@lpan",  d.LandlordPan);
            cmd.Parameters.AddWithValue("@ips",   d.IsParentSeniorCitizen ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        // ── Landlord Records ──────────────────────────────────────────────────
        public List<LandlordRecord> GetLandlords(int employeeId, string fy)
        {
            var list = new List<LandlordRecord>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "SELECT id,name,pan,annual_rent,from_date,to_date FROM landlord_records WHERE employee_id=@eid AND financial_year=@fy ORDER BY id";
            cmd.Parameters.AddWithValue("@eid", employeeId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new LandlordRecord {
                    Id           = r.GetInt32(0),
                    EmployeeId   = employeeId,
                    FinancialYear= fy,
                    Name         = r.IsDBNull(1) ? "" : r.GetString(1),
                    Pan          = r.IsDBNull(2) ? "" : r.GetString(2),
                    AnnualRent   = r.IsDBNull(3) ? 0  : r.GetDouble(3),
                    FromDate     = r.IsDBNull(4) ? "" : r.GetString(4),
                    ToDate       = r.IsDBNull(5) ? "" : r.GetString(5),
                });
            return list;
        }

        public void SaveLandlord(LandlordRecord lr)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            if (lr.Id == 0)
            {
                // Single round-trip: INSERT then fetch new rowid
                cmd.CommandText = @"
                    INSERT INTO landlord_records (employee_id,financial_year,name,pan,annual_rent,from_date,to_date)
                    VALUES(@eid,@fy,@nm,@pan,@ar,@fd,@td);
                    SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("@eid", lr.EmployeeId);
                cmd.Parameters.AddWithValue("@fy",  lr.FinancialYear);
                cmd.Parameters.AddWithValue("@nm",  lr.Name);
                cmd.Parameters.AddWithValue("@pan", lr.Pan);
                cmd.Parameters.AddWithValue("@ar",  lr.AnnualRent);
                cmd.Parameters.AddWithValue("@fd",  lr.FromDate);
                cmd.Parameters.AddWithValue("@td",  lr.ToDate);
                lr.Id = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else
            {
                cmd.CommandText = "UPDATE landlord_records SET name=@nm,pan=@pan,annual_rent=@ar,from_date=@fd,to_date=@td WHERE id=@id";
                cmd.Parameters.AddWithValue("@id",  lr.Id);
                cmd.Parameters.AddWithValue("@nm",  lr.Name);
                cmd.Parameters.AddWithValue("@pan", lr.Pan);
                cmd.Parameters.AddWithValue("@ar",  lr.AnnualRent);
                cmd.Parameters.AddWithValue("@fd",  lr.FromDate);
                cmd.Parameters.AddWithValue("@td",  lr.ToDate);
                cmd.ExecuteNonQuery();
            }
        }

        public void DeleteLandlord(int id)
        {
            if (id <= 0) return; // unsaved row — nothing in DB to delete
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM landlord_records WHERE id=@id";
            cmd.Parameters.AddWithValue("@id", id);
            cmd.ExecuteNonQuery();
        }

        // ══════════════════════════════════════════════════════════════════════
        // PAYROLL RUNS
        // ══════════════════════════════════════════════════════════════════════
        public List<PayrollRun> GetRuns(int month, int year, int? deductorId = null)
        {
            var list = new List<PayrollRun>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT pr.*, e.name, e.employee_code, e.pan
                FROM payroll_runs pr
                JOIN employees e ON pr.employee_id=e.id
                WHERE pr.month=@m AND pr.year=@y
                  AND (@did IS NULL OR pr.deductor_id=@did)
                ORDER BY e.name";
            cmd.Parameters.AddWithValue("@m",   month);
            cmd.Parameters.AddWithValue("@y",   year);
            cmd.Parameters.AddWithValue("@did", (deductorId == null || deductorId == 0) ? (object)DBNull.Value : deductorId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadRun(r));
            return list;
        }

        /// <summary>Returns all payroll runs for a full financial year, grouped by employee.</summary>
        public List<PayrollRun> GetRunsForFY(string financialYear, int? deductorId = null)
        {
            var list = new List<PayrollRun>();
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT pr.*, e.name, e.employee_code, e.pan
                FROM payroll_runs pr
                JOIN employees e ON pr.employee_id = e.id
                WHERE pr.financial_year = @fy
                  AND (@did IS NULL OR pr.deductor_id = @did)
                ORDER BY e.name, pr.month";
            cmd.Parameters.AddWithValue("@fy",  financialYear);
            cmd.Parameters.AddWithValue("@did", (deductorId == null || deductorId == 0) ? (object)DBNull.Value : deductorId);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(ReadRun(r));
            return list;
        }

        public double GetYtdTds(int employeeId, string fy, int currentMonth, int deductorId = 0)
        {
            int fyStartYear  = fy.Length >= 4 && int.TryParse(fy[..4], out int y) ? y : DateTime.Today.Year;
            int currentYear  = currentMonth >= 4 ? fyStartYear : fyStartYear + 1;

            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT COALESCE(SUM(tds_deducted),0)
                FROM payroll_runs
                WHERE employee_id    = @eid
                  AND financial_year = @fy
                  AND (@did = 0 OR deductor_id = @did)
                  AND (year * 12 + month) < (@cy * 12 + @cm)";
            cmd.Parameters.AddWithValue("@eid", employeeId);
            cmd.Parameters.AddWithValue("@fy",  fy);
            cmd.Parameters.AddWithValue("@did", deductorId);
            cmd.Parameters.AddWithValue("@cy",  currentYear);
            cmd.Parameters.AddWithValue("@cm",  currentMonth);
            return Convert.ToDouble(cmd.ExecuteScalar() ?? 0);
        }

        public void SaveRun(PayrollRun run)
        {
            using var conn = Database.GetConnection();
            using var cmd  = conn.CreateCommand();

            // UPSERT guard: find existing run for this employee+deductor+month+year+FY
            if (run.Id == 0)
            {
                using var chk = conn.CreateCommand();
                chk.CommandText = @"
                    SELECT id FROM payroll_runs
                    WHERE employee_id=@eid AND deductor_id=@did
                      AND month=@m AND year=@y AND financial_year=@fy
                    LIMIT 1";
                chk.Parameters.AddWithValue("@eid", run.EmployeeId);
                chk.Parameters.AddWithValue("@did", run.DeductorId);
                chk.Parameters.AddWithValue("@m",   run.Month);
                chk.Parameters.AddWithValue("@y",   run.Year);
                chk.Parameters.AddWithValue("@fy",  run.FinancialYear);
                var existing = chk.ExecuteScalar();
                if (existing != null)
                    run.Id = Convert.ToInt32(existing);
            }

            if (run.Id == 0)
            {
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO payroll_runs
                        (employee_id,deductor_id,month,year,financial_year,
                         basic,hra,da,special,medical,lta,other,gross_salary,
                         pf_employee,esi_employee,professional_tax,tds_deducted,other_deductions,
                         tax_regime_used,hra_exemption,standard_deduction,chapter6a_deduction,
                         taxable_income,annual_tax,surcharge,cess,total_annual_tax,ytd_tds,status,
                         pro_rata_days,pro_rata_total)
                    VALUES(@eid,@did,@m,@y,@fy,@b,@h,@da,@sp,@med,@lta,@ot,@gr,
                           @pf,@esi,@pt,@tds,@od,@tr,@hraex,@std,@c6a,
                           @ti,@at,@sc,@cess,@tat,@ytd,@st,
                           @prd,@prt)";
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE payroll_runs SET
                        basic=@b,hra=@h,da=@da,special=@sp,medical=@med,lta=@lta,other=@ot,gross_salary=@gr,
                        pf_employee=@pf,esi_employee=@esi,professional_tax=@pt,
                        tds_deducted=@tds,other_deductions=@od,
                        tax_regime_used=@tr,hra_exemption=@hraex,standard_deduction=@std,
                        chapter6a_deduction=@c6a,taxable_income=@ti,annual_tax=@at,
                        surcharge=@sc,cess=@cess,total_annual_tax=@tat,ytd_tds=@ytd,
                        status=@st,tds_entry_id=@tei,
                        pro_rata_days=@prd,pro_rata_total=@prt
                    WHERE id=@id";
                cmd.Parameters.AddWithValue("@id",  run.Id);
                cmd.Parameters.AddWithValue("@tei", (object?)run.TdsEntryId ?? DBNull.Value);
            }
            void P(string n, object v) => cmd.Parameters.AddWithValue(n, v);
            P("@eid", run.EmployeeId); P("@did", run.DeductorId);
            P("@m", run.Month); P("@y", run.Year); P("@fy", run.FinancialYear);
            P("@b", run.Basic); P("@h", run.Hra); P("@da", run.Da);
            P("@sp", run.Special); P("@med", run.Medical); P("@lta", run.Lta);
            P("@ot", run.Other); P("@gr", run.GrossSalary);
            P("@pf", run.PfEmployee); P("@esi", run.EsiEmployee);
            P("@pt", run.ProfessionalTax); P("@tds", Math.Round(run.TdsDeducted, MidpointRounding.AwayFromZero));
            P("@od", run.OtherDeductions); P("@tr", run.TaxRegimeUsed);
            P("@hraex", run.HraExemption); P("@std", run.StandardDeduction);
            P("@c6a", run.Chapter6ADeduction); P("@ti", run.TaxableIncome);
            P("@at", run.AnnualTax); P("@sc", run.Surcharge);
            P("@cess", run.Cess); P("@tat", run.TotalAnnualTax);
            P("@ytd", run.YtdTds); P("@st", run.Status);
            P("@prd", run.ProRataDays); P("@prt", run.ProRataTotal);
            cmd.ExecuteNonQuery();

            if (run.Id == 0)
            {
                using var lid = conn.CreateCommand();
                lid.CommandText = "SELECT last_insert_rowid()";
                run.Id = Convert.ToInt32(lid.ExecuteScalar());
            }
        }

        private static PayrollRun ReadRun(Microsoft.Data.Sqlite.SqliteDataReader r)
        {
            // Helper: safe column read by name — returns default if column doesn't exist in this result set
            int    I(string col) { int o = -1; try { o = r.GetOrdinal(col); } catch { } return o >= 0 && !r.IsDBNull(o) ? r.GetInt32(o)    : 0; }
            double D(string col) { int o = -1; try { o = r.GetOrdinal(col); } catch { } return o >= 0 && !r.IsDBNull(o) ? Convert.ToDouble(r[o]) : 0; }
            string S(string col, string def = "") { int o = -1; try { o = r.GetOrdinal(col); } catch { } return o >= 0 && !r.IsDBNull(o) ? r.GetString(o) : def; }
            int?  IN(string col) { int o = -1; try { o = r.GetOrdinal(col); } catch { } return o >= 0 && !r.IsDBNull(o) ? r.GetInt32(o) : (int?)null; }

            return new PayrollRun
            {
                Id                 = I("id"),
                EmployeeId         = I("employee_id"),
                DeductorId         = I("deductor_id"),
                Month              = I("month"),
                Year               = I("year"),
                FinancialYear      = S("financial_year"),
                Basic              = D("basic"),
                Hra                = D("hra"),
                Da                 = D("da"),
                Special            = D("special"),
                Medical            = D("medical"),
                Lta                = D("lta"),
                Other              = D("other"),
                GrossSalary        = D("gross_salary"),
                PfEmployee         = D("pf_employee"),
                EsiEmployee        = D("esi_employee"),
                ProfessionalTax    = D("professional_tax"),
                TdsDeducted        = D("tds_deducted"),
                OtherDeductions    = D("other_deductions"),
                TaxRegimeUsed      = S("tax_regime_used", "New"),
                HraExemption       = D("hra_exemption"),
                StandardDeduction  = D("standard_deduction"),
                Chapter6ADeduction = D("chapter6a_deduction"),
                TaxableIncome      = D("taxable_income"),
                AnnualTax          = D("annual_tax"),
                Surcharge          = D("surcharge"),
                Cess               = D("cess"),
                TotalAnnualTax     = D("total_annual_tax"),
                YtdTds             = D("ytd_tds"),
                Status             = S("status", "Draft"),
                TdsEntryId         = IN("tds_entry_id"),
                ProRataDays        = I("pro_rata_days"),   // 0 on old DBs — safe
                ProRataTotal       = I("pro_rata_total"),  // 0 on old DBs — safe
                // JOIN columns (present only in GetRuns / GetRunsForFY queries)
                EmployeeName       = S("name"),
                EmployeeCode       = S("employee_code"),
                Pan                = S("pan"),
            };
        }
    }
}
