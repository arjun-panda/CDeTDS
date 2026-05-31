using Microsoft.Data.Sqlite;
using TDSPro.Common;

namespace TDSPro.DAL
{
    public static class Database
    {
        private static string _dbPath = "";

        /// <summary>Absolute path to the live SQLite file. Empty before Initialize is called.</summary>
        public static string DbPath => _dbPath;

        // Current schema version — bump this when adding new migrations
        private const int SchemaVersion = 19;

        /// <summary>
        /// Fast path: creates tables + seeds. Returns immediately so the window can show.
        /// Call RunMigrationsAndBackup() in background after this.
        /// </summary>
        public static void Initialize(string appDataPath)
        {
            _dbPath = Path.Combine(appDataPath, AppConstants.DbFileName);
            CreateTables();
            // Fast-path column additions — runs every startup, no-op if column already exists.
            // Must cover every column added after initial release so existing DBs don't crash on page load.
            EnsureColumns();
            SeedTdsRules2026();
            SeedFvuConfig();
            SeedDefaultAdmin();
            SeedSampleData();
        }

        /// <summary>
        /// Idempotent — adds any column that may be missing on existing DBs.
        /// Called at startup before any page loads, so pages never crash with "no such column".
        /// </summary>
        private static void EnsureColumns()
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();

                void Add(string table, string col, string def)
                {
                    using var chk = conn.CreateCommand();
                    chk.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name='{col}'";
                    if (Convert.ToInt32(chk.ExecuteScalar()) == 0)
                    {
                        using var alt = conn.CreateCommand();
                        alt.CommandText = $"ALTER TABLE {table} ADD COLUMN {col} {def}";
                        alt.ExecuteNonQuery();
                    }
                }

                // monthly_salary_entries
                Add("monthly_salary_entries", "special_allowance", "REAL DEFAULT 0");
                Add("monthly_salary_entries", "medical_allowance",  "REAL DEFAULT 0");
                Add("monthly_salary_entries", "lta",                "REAL DEFAULT 0");
                Add("monthly_salary_entries", "days_worked",        "INTEGER DEFAULT 0");
                Add("monthly_salary_entries", "lop_days",           "INTEGER DEFAULT 0");
                Add("monthly_salary_entries", "working_days",       "INTEGER DEFAULT 30");
                Add("monthly_salary_entries", "status",             "TEXT DEFAULT 'Draft'");
                Add("monthly_salary_entries", "is_locked",          "INTEGER DEFAULT 0");
                Add("monthly_salary_entries", "approved_at",        "TEXT DEFAULT ''");
                Add("monthly_salary_entries", "approved_by",        "TEXT DEFAULT ''");
                Add("monthly_salary_entries", "surcharge_amt",      "REAL DEFAULT 0");
                Add("monthly_salary_entries", "cess_amt",           "REAL DEFAULT 0");
                Add("monthly_salary_entries", "tax_computed",       "REAL DEFAULT 0");
                Add("monthly_salary_entries", "saved_at",           "TEXT DEFAULT ''");

                // landlord_records
                Add("landlord_records", "city_type", "TEXT DEFAULT 'Non-Metro'");

                // tax_declarations
                Add("tax_declarations", "income_other_sources",  "REAL DEFAULT 0");
                Add("tax_declarations", "sec80e",                "REAL DEFAULT 0");
                Add("tax_declarations", "sec80eea",              "REAL DEFAULT 0");
                Add("tax_declarations", "sec80tta",              "REAL DEFAULT 0");
                Add("tax_declarations", "sec80ttb",              "REAL DEFAULT 0");
                Add("tax_declarations", "sec80dd",               "REAL DEFAULT 0");
                Add("tax_declarations", "sec80u",                "REAL DEFAULT 0");
                Add("tax_declarations", "lta_exemption",         "REAL DEFAULT 0");
                Add("tax_declarations", "landlord_pan",          "TEXT DEFAULT ''");
                Add("tax_declarations", "is_parent_senior",      "INTEGER DEFAULT 0");
                Add("tax_declarations", "hra_city_type",         "TEXT DEFAULT 'Non-Metro'");
                Add("tax_declarations", "other_deductions",      "REAL DEFAULT 0");
                Add("tax_declarations", "sec80ccd_employee",     "REAL DEFAULT 0");
                Add("tax_declarations", "sec80ccd_employer",     "REAL DEFAULT 0");
                Add("tax_declarations", "sec80g",                "REAL DEFAULT 0");
                Add("tax_declarations", "sec80d_self",           "REAL DEFAULT 0");
                Add("tax_declarations", "sec80d_parents",        "REAL DEFAULT 0");
                Add("tax_declarations", "sec80c",                "REAL DEFAULT 0");
                Add("tax_declarations", "rent_paid",             "REAL DEFAULT 0");

                // employees
                Add("employees", "hra_city_type",       "TEXT DEFAULT 'Non-Metro'");
                Add("employees", "hra_monthly_basis",   "INTEGER DEFAULT 0");
                Add("employees", "da_for_retirement",   "INTEGER DEFAULT 0");
                Add("employees", "is_differently_abled","INTEGER DEFAULT 0");
                Add("employees", "tax_regime",          "TEXT DEFAULT 'New'");

                // deduction_schedules (new in v16)
                Add("deduction_schedules", "type",               "TEXT NOT NULL DEFAULT 'Other'");
                Add("deduction_schedules", "description",        "TEXT NOT NULL DEFAULT ''");
                Add("deduction_schedules", "total_amount",       "REAL NOT NULL DEFAULT 0");
                Add("deduction_schedules", "installment_amt",    "REAL NOT NULL DEFAULT 0");
                Add("deduction_schedules", "total_installments", "INTEGER NOT NULL DEFAULT 0");
                Add("deduction_schedules", "recovered_amt",      "REAL NOT NULL DEFAULT 0");
                Add("deduction_schedules", "start_fy",           "TEXT NOT NULL DEFAULT ''");
                Add("deduction_schedules", "start_month",        "INTEGER NOT NULL DEFAULT 4");
                Add("deduction_schedules", "is_active",          "INTEGER NOT NULL DEFAULT 1");
                Add("deduction_schedules", "created_at",         "TEXT NOT NULL DEFAULT ''");
                Add("deduction_schedules", "notes",              "TEXT NOT NULL DEFAULT ''");
            }
            catch { }
        }

        /// <summary>
        /// Slow path — run in Task.Run() after window is shown.
        /// Skips migrations entirely if DB is already on SchemaVersion.
        /// </summary>
        public static void RunMigrationsAndBackup(string appDataPath)
        {
            var saved = int.TryParse(GetSetting("SCHEMA_VERSION", "0"), out var v) ? v : 0;
            if (saved < SchemaVersion)
            {
                RunMigrations();
                SetSetting("SCHEMA_VERSION", SchemaVersion.ToString());
            }
            FolderManager.RunDailyAutoBackup(_dbPath);
        }

        public static SqliteConnection GetConnection()
        {
            var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA foreign_keys  = ON;
                PRAGMA journal_mode  = WAL;
                PRAGMA synchronous   = NORMAL;
                PRAGMA cache_size    = 10000;
                PRAGMA temp_store    = MEMORY;";
            cmd.ExecuteNonQuery();
            return conn;
        }

        /// <summary>Force WAL checkpoint so all data is flushed to the main DB file before a restore copy.</summary>
        public static void CheckpointAndClose()
        {
            try
            {
                using var conn = new SqliteConnection($"Data Source={_dbPath}");
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
                cmd.ExecuteNonQuery();
            }
            catch { /* best-effort */ }
        }

        private static void CreateTables()
        {
            using var conn = new SqliteConnection($"Data Source={_dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                -- ── Deductors ─────────────────────────────────────────────────
                CREATE TABLE IF NOT EXISTS deductors (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    company_name   TEXT    NOT NULL,
                    tan            TEXT    NOT NULL UNIQUE,
                    pan            TEXT    NOT NULL,
                    address        TEXT    DEFAULT '',
                    city           TEXT    DEFAULT '',
                    state          TEXT    DEFAULT '',
                    pincode        TEXT    DEFAULT '',
                    contact_person TEXT    DEFAULT '',
                    phone          TEXT    DEFAULT '',
                    email          TEXT    DEFAULT '',
                    financial_year TEXT    DEFAULT '2025-26',
                    deductor_type  TEXT    DEFAULT 'Company',
                    is_active      INTEGER DEFAULT 1,
                    created_at     TEXT    DEFAULT (datetime('now','localtime'))
                );
                CREATE INDEX IF NOT EXISTS idx_deductors_tan ON deductors(tan);

                -- ── Deductees ─────────────────────────────────────────────────
                CREATE TABLE IF NOT EXISTS pan_verification_cache (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    pan          TEXT    NOT NULL UNIQUE,
                    status       TEXT    NOT NULL DEFAULT 'Unknown',
                    verified_name TEXT   NOT NULL DEFAULT '',
                    message      TEXT    NOT NULL DEFAULT '',
                    verified_at  TEXT    NOT NULL
                );

                CREATE TABLE IF NOT EXISTS deductees (
                    id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    deductee_code    TEXT    NOT NULL UNIQUE,
                    name             TEXT    NOT NULL,
                    pan              TEXT    NOT NULL UNIQUE,
                    pan_verified     INTEGER DEFAULT 0,
                    address          TEXT    DEFAULT '',
                    city             TEXT    DEFAULT '',
                    state            TEXT    DEFAULT '',
                    pincode          TEXT    DEFAULT '',
                    email            TEXT    DEFAULT '',
                    phone            TEXT    DEFAULT '',
                    section          TEXT    NOT NULL DEFAULT '194C',
                    rate             REAL    NOT NULL DEFAULT 0,
                    deductee_type    TEXT    DEFAULT 'Individual',
                    is_resident      INTEGER DEFAULT 1,
                    itr_filed        INTEGER DEFAULT 1,
                    lower_cert_no    TEXT    DEFAULT '',
                    lower_cert_rate  REAL    DEFAULT 0,
                    lower_cert_till  TEXT    DEFAULT '',
                    remarks          TEXT    DEFAULT '',
                    created_at       TEXT    DEFAULT (datetime('now','localtime'))
                );
                CREATE INDEX IF NOT EXISTS idx_deductees_pan  ON deductees(pan);
                CREATE INDEX IF NOT EXISTS idx_deductees_name ON deductees(name);

                -- ── TDS Rules Engine (dynamic — no hardcoded rates) ───────────
                CREATE TABLE IF NOT EXISTS tds_rules (
                    id               INTEGER PRIMARY KEY AUTOINCREMENT,
                    section_code     TEXT    NOT NULL,
                    nature_of_payment TEXT   NOT NULL,
                    deductee_type    TEXT    DEFAULT 'All',
                    is_resident      INTEGER DEFAULT 1,
                    threshold_limit  REAL    DEFAULT 0,
                    tds_rate         REAL    NOT NULL,
                    surcharge_rate   REAL    DEFAULT 0,
                    cess_rate        REAL    DEFAULT 0,
                    effective_from   TEXT    NOT NULL,
                    effective_to     TEXT,
                    reference_act    TEXT    DEFAULT '',
                    notes            TEXT    DEFAULT '',
                    is_active        INTEGER DEFAULT 1,
                    created_at       TEXT    DEFAULT (datetime('now','localtime'))
                );
                CREATE INDEX IF NOT EXISTS idx_rules_section  ON tds_rules(section_code);
                CREATE INDEX IF NOT EXISTS idx_rules_active   ON tds_rules(is_active, effective_from);

                -- ── TDS Entries ───────────────────────────────────────────────
                CREATE TABLE IF NOT EXISTS tds_entries (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    entry_no       TEXT    NOT NULL UNIQUE,
                    entry_date     TEXT    NOT NULL,
                    deductor_id    INTEGER NOT NULL,
                    deductee_id    INTEGER NOT NULL,
                    section        TEXT    NOT NULL,
                    nature_of_payment TEXT DEFAULT '',
                    amount         REAL    NOT NULL DEFAULT 0,
                    rate           REAL    NOT NULL DEFAULT 0,
                    tds_amount     REAL    NOT NULL DEFAULT 0,
                    surcharge      REAL    DEFAULT 0,
                    cess           REAL    DEFAULT 0,
                    total_tds      REAL    NOT NULL DEFAULT 0,
                    due_date       TEXT    DEFAULT '',
                    payment_date   TEXT    DEFAULT '',
                    interest       REAL    DEFAULT 0,
                    late_fee       REAL    DEFAULT 0,
                    challan_no     TEXT    DEFAULT '',
                    remarks        TEXT    DEFAULT '',
                    status         TEXT    DEFAULT 'Pending',
                    financial_year TEXT    DEFAULT '2025-26',
                    quarter        TEXT    DEFAULT 'Q1',
                    pan_available  INTEGER DEFAULT 1,
                    itr_filed      INTEGER DEFAULT 1,
                    higher_rate_applied INTEGER DEFAULT 0,
                    higher_rate_reason  TEXT DEFAULT '',
                    tds_rule_id    INTEGER,
                    created_at     TEXT    DEFAULT (datetime('now','localtime')),
                    FOREIGN KEY (deductor_id) REFERENCES deductors(id),
                    FOREIGN KEY (deductee_id) REFERENCES deductees(id),
                    FOREIGN KEY (tds_rule_id) REFERENCES tds_rules(id)
                );
                CREATE INDEX IF NOT EXISTS idx_entries_date    ON tds_entries(entry_date);
                CREATE INDEX IF NOT EXISTS idx_entries_fy      ON tds_entries(financial_year);
                CREATE INDEX IF NOT EXISTS idx_entries_section ON tds_entries(section);
                CREATE INDEX IF NOT EXISTS idx_entries_status  ON tds_entries(status);

                -- ── Challans ──────────────────────────────────────────────────
                CREATE TABLE IF NOT EXISTS challans (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    challan_no     TEXT    NOT NULL,
                    challan_date   TEXT    NOT NULL,
                    deductor_id    INTEGER,
                    bsr_code       TEXT    NOT NULL,
                    section        TEXT    DEFAULT '',
                    amount         REAL    DEFAULT 0,
                    tds_amount     REAL    NOT NULL DEFAULT 0,
                    surcharge      REAL    DEFAULT 0,
                    cess           REAL    DEFAULT 0,
                    interest       REAL    DEFAULT 0,
                    late_fee       REAL    DEFAULT 0,
                    total_amount   REAL    NOT NULL DEFAULT 0,
                    bank_name      TEXT    DEFAULT '',
                    ack_no         TEXT    DEFAULT '',
                    quarter        TEXT    DEFAULT 'Q1',
                    financial_year TEXT    DEFAULT '2025-26',
                    status         TEXT    DEFAULT 'Paid',
                    remarks        TEXT    DEFAULT '',
                    created_at     TEXT    DEFAULT (datetime('now','localtime'))
                );
                CREATE INDEX IF NOT EXISTS idx_challans_fy ON challans(financial_year);

                -- ── FVU / TRACES format config (no hardcoded layout) ──────────
                CREATE TABLE IF NOT EXISTS fvu_format_config (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    config_key     TEXT    NOT NULL UNIQUE,
                    form_type      TEXT    NOT NULL,
                    version        TEXT    NOT NULL DEFAULT '9.0',
                    field_layout   TEXT    NOT NULL DEFAULT '{}',
                    delimiter      TEXT    NOT NULL DEFAULT '^',
                    effective_from TEXT    NOT NULL,
                    notes          TEXT    DEFAULT '',
                    created_at     TEXT    DEFAULT (datetime('now','localtime'))
                );

                -- ── Users ─────────────────────────────────────────────────────
                CREATE TABLE IF NOT EXISTS users (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    username    TEXT    NOT NULL UNIQUE,
                    password    TEXT    NOT NULL,
                    full_name   TEXT    DEFAULT '',
                    role        TEXT    NOT NULL DEFAULT 'Operator',
                    email       TEXT    DEFAULT '',
                    status      TEXT    NOT NULL DEFAULT 'Active',
                    last_login  TEXT    DEFAULT '',
                    created_at  TEXT    DEFAULT (datetime('now','localtime'))
                );

                -- ── Audit log ─────────────────────────────────────────────────
                CREATE TABLE IF NOT EXISTS audit_log (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    username    TEXT    DEFAULT '',
                    action      TEXT    DEFAULT '',
                    module      TEXT    DEFAULT '',
                    details     TEXT    DEFAULT '',
                    created_at  TEXT    DEFAULT (datetime('now','localtime'))
                );
                CREATE INDEX IF NOT EXISTS idx_audit_date ON audit_log(created_at);

                -- ── TDS Filing History ────────────────────────────────────────
                CREATE TABLE IF NOT EXISTS tds_filing_history (
                    id              INTEGER PRIMARY KEY AUTOINCREMENT,
                    deductor_id     INTEGER NOT NULL,
                    form_type       TEXT    NOT NULL,
                    financial_year  TEXT    NOT NULL,
                    quarter         TEXT    NOT NULL,
                    is_correction   INTEGER DEFAULT 0,
                    correction_type TEXT    DEFAULT '',
                    prn             TEXT    NOT NULL DEFAULT '',
                    filed_at        TEXT    DEFAULT (datetime('now','localtime')),
                    txt_file_path   TEXT    DEFAULT '',
                    fvu_file_path   TEXT    DEFAULT '',
                    remarks         TEXT    DEFAULT ''
                );
                CREATE INDEX IF NOT EXISTS idx_filing_hist_ded ON tds_filing_history(deductor_id, financial_year, quarter);

                -- ── Landlord Records (HRA) ─────────────────────────────────────
                CREATE TABLE IF NOT EXISTS landlord_records (
                    id             INTEGER PRIMARY KEY AUTOINCREMENT,
                    employee_id    INTEGER NOT NULL,
                    financial_year TEXT    NOT NULL,
                    name           TEXT    DEFAULT '',
                    pan            TEXT    DEFAULT '',
                    annual_rent    REAL    DEFAULT 0,
                    from_date      TEXT    DEFAULT '',
                    to_date        TEXT    DEFAULT ''
                );
            ";
            cmd.ExecuteNonQuery();
        }

        // ── Schema migrations — safe for existing databases ───────────────────
        private static void RunMigrations()
        {
            using var conn = GetConnection();

            // Helper: add column only if it doesn't exist
            void AddColumnIfMissing(string table, string column, string definition)
            {
                try
                {
                    using var chk = conn.CreateCommand();
                    chk.CommandText = $"SELECT {column} FROM {table} LIMIT 1";
                    chk.ExecuteScalar(); // throws if column missing
                }
                catch
                {
                    try
                    {
                        using var alter = conn.CreateCommand();
                        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
                        alter.ExecuteNonQuery();
                    }
                    catch { /* column may have been added by concurrent call */ }
                }
            }

            // tds_entries — columns added in various versions
            AddColumnIfMissing("tds_entries", "financial_year",       "TEXT DEFAULT '2025-26'");
            AddColumnIfMissing("tds_entries", "quarter",              "TEXT DEFAULT 'Q1'");
            AddColumnIfMissing("tds_entries", "pan_available",        "INTEGER DEFAULT 1");
            AddColumnIfMissing("tds_entries", "itr_filed",            "INTEGER DEFAULT 1");
            AddColumnIfMissing("tds_entries", "higher_rate_applied",  "INTEGER DEFAULT 0");
            AddColumnIfMissing("tds_entries", "higher_rate_reason",   "TEXT DEFAULT ''");
            AddColumnIfMissing("tds_entries", "tds_rule_id",          "INTEGER");
            AddColumnIfMissing("tds_entries", "challan_id",           "INTEGER");
            AddColumnIfMissing("tds_entries", "nature_of_payment",    "TEXT DEFAULT ''");

            // deductors — bank defaults
            AddColumnIfMissing("deductors", "default_bsr_code",  "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "default_bank_name", "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "cpc_password",      "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "it_password",       "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "responsible_name",  "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "responsible_pan",   "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "designation",       "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "gstin",             "TEXT DEFAULT ''");

            // challans — columns added in various versions
            AddColumnIfMissing("challans", "financial_year", "TEXT DEFAULT '2025-26'");
            AddColumnIfMissing("challans", "quarter",        "TEXT DEFAULT 'Q1'");
            AddColumnIfMissing("challans", "section",        "TEXT DEFAULT ''");
            AddColumnIfMissing("challans", "amount",         "REAL DEFAULT 0");
            AddColumnIfMissing("challans", "surcharge",      "REAL DEFAULT 0");
            AddColumnIfMissing("challans", "interest",       "REAL DEFAULT 0");
            AddColumnIfMissing("challans", "late_fee",       "REAL DEFAULT 0");
            AddColumnIfMissing("challans", "ack_no",           "TEXT DEFAULT ''");
            AddColumnIfMissing("challans", "status",           "TEXT DEFAULT 'Pending'");
            AddColumnIfMissing("challans", "minor_head_code",  "TEXT DEFAULT '200'");

            // Always zero out cess on non-salary/NR sections — runs on every startup, idempotent
            {
                using var fix = conn.CreateCommand();
                fix.CommandText = @"
                    UPDATE tds_entries
                    SET cess      = 0,
                        total_tds = tds_amount + surcharge
                    WHERE section NOT IN ('192','195')
                      AND cess != 0";
                fix.ExecuteNonQuery();
            }

            // deductors
            AddColumnIfMissing("deductors", "is_active",      "INTEGER DEFAULT 1");
            AddColumnIfMissing("deductors", "deductor_type",  "TEXT DEFAULT 'Company'");
            AddColumnIfMissing("deductors", "contact_person", "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "phone",          "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "email",          "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "pincode",        "TEXT DEFAULT ''");
            AddColumnIfMissing("deductors", "financial_year", "TEXT DEFAULT '2026-27'");
            AddColumnIfMissing("deductors", "cpc_password",   "TEXT DEFAULT ''");  // TRACES password (AES-encrypted)
            AddColumnIfMissing("deductors", "it_password",    "TEXT DEFAULT ''");  // IT portal password (AES-encrypted)

            // tds_entries — per-entry section 197 cert (deductee-level cert may differ per entry)
            AddColumnIfMissing("tds_entries", "lower_cert_no",  "TEXT DEFAULT ''");

            // deductees
            AddColumnIfMissing("deductees", "lower_cert_no",   "TEXT DEFAULT ''");
            AddColumnIfMissing("deductees", "lower_cert_rate", "REAL DEFAULT 0");
            AddColumnIfMissing("deductees", "lower_cert_till", "TEXT DEFAULT ''");
            AddColumnIfMissing("deductees", "itr_filed",       "INTEGER DEFAULT 1");
            AddColumnIfMissing("deductees", "is_resident",     "INTEGER DEFAULT 1");
            AddColumnIfMissing("deductees", "deductor_id",     "INTEGER DEFAULT 0");
            AddColumnIfMissing("deductees", "tds_rate",        "REAL DEFAULT 0");
            AddColumnIfMissing("deductees", "pan_verified",             "INTEGER DEFAULT 0");
            AddColumnIfMissing("deductees", "pan_verification_status",  "TEXT DEFAULT ''");
            AddColumnIfMissing("deductees", "pan_verified_name",        "TEXT DEFAULT ''");
            AddColumnIfMissing("deductees", "pan_verified_at",          "TEXT DEFAULT ''");

            // ── Employees — new columns for complete employee record ────────────
            AddColumnIfMissing("employees", "fathers_name",              "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "date_of_birth",             "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "sex",                       "TEXT DEFAULT 'Male'");
            AddColumnIfMissing("employees", "pf_number",                 "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "ward_circle_range",         "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "std_code",                  "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "telephone_no",              "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "flat_door_block_no",        "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "premises_building_village", "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "road_street_post_office",   "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "area_locality",             "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "town_city_district",        "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "pin_code",                  "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "state",                     "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "hra_monthly_basis",         "INTEGER DEFAULT 1");
            AddColumnIfMissing("employees", "hra_city_type",             "TEXT DEFAULT 'Non-Metro'");
            AddColumnIfMissing("employees", "da_for_retirement",         "INTEGER DEFAULT 1");
            AddColumnIfMissing("employees", "is_differently_abled",      "INTEGER DEFAULT 0");

            // ── Employees — extended fields ───────────────────────────────────
            AddColumnIfMissing("employees", "aadhaar_number",            "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "residential_status",        "TEXT DEFAULT 'Resident'");
            AddColumnIfMissing("employees", "marital_status",            "TEXT DEFAULT 'Single'");
            AddColumnIfMissing("employees", "blood_group",               "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "employment_type",           "TEXT DEFAULT 'Permanent'");
            AddColumnIfMissing("employees", "work_email",                "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "emergency_contact",         "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "emergency_mobile",          "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "uan",                       "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "esi_ip_number",             "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "bank_name",                 "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "bank_branch",               "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "bank_account_type",         "TEXT DEFAULT 'Savings'");
            AddColumnIfMissing("employees", "prev_employer_name",        "TEXT DEFAULT ''");
            AddColumnIfMissing("employees", "prev_employer_income",      "REAL DEFAULT 0");
            AddColumnIfMissing("employees", "prev_employer_tds",         "REAL DEFAULT 0");

            // ── Salary structures — allowance columns ─────────────────────────
            AddColumnIfMissing("salary_structures", "medical_allowance",  "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "lta",                "REAL DEFAULT 0");

            // ── Payroll runs — pro-rata columns ───────────────────────────────
            AddColumnIfMissing("payroll_runs", "pro_rata_days",    "INTEGER DEFAULT 0");
            AddColumnIfMissing("payroll_runs", "pro_rata_total",   "INTEGER DEFAULT 0");
            AddColumnIfMissing("payroll_runs", "medical",          "REAL DEFAULT 0");
            AddColumnIfMissing("payroll_runs", "lta",              "REAL DEFAULT 0");
            AddColumnIfMissing("payroll_runs", "rebate87a",        "REAL DEFAULT 0");
            // ── Monthly salary entries — separate allowance columns ───────────
            AddColumnIfMissing("monthly_salary_entries", "special_allowance",  "REAL DEFAULT 0");
            AddColumnIfMissing("monthly_salary_entries", "medical_allowance",  "REAL DEFAULT 0");
            AddColumnIfMissing("monthly_salary_entries", "lta",                "REAL DEFAULT 0");
            // Monthly Close workflow — days worked, LOP, status/lock
            AddColumnIfMissing("monthly_salary_entries", "days_worked",     "INTEGER DEFAULT 0");
            AddColumnIfMissing("monthly_salary_entries", "lop_days",        "INTEGER DEFAULT 0");
            AddColumnIfMissing("monthly_salary_entries", "working_days",    "INTEGER DEFAULT 30");
            AddColumnIfMissing("monthly_salary_entries", "status",          "TEXT DEFAULT 'Draft'");   // Draft / Locked / Skip
            AddColumnIfMissing("monthly_salary_entries", "is_locked",       "INTEGER DEFAULT 0");
            AddColumnIfMissing("monthly_salary_entries", "approved_at",     "TEXT DEFAULT ''");
            AddColumnIfMissing("monthly_salary_entries", "approved_by",     "TEXT DEFAULT ''");
            AddColumnIfMissing("monthly_salary_entries", "surcharge_amt",   "REAL DEFAULT 0");
            AddColumnIfMissing("monthly_salary_entries", "cess_amt",        "REAL DEFAULT 0");
            AddColumnIfMissing("monthly_salary_entries", "tax_computed",    "REAL DEFAULT 0");
            // PF fixed amount — 0 = auto 12%, >0 = employee-chosen fixed amount
            AddColumnIfMissing("salary_structures", "pf_fixed_amount", "REAL DEFAULT 0");
            // Reimbursements, variable pay, employer contributions (Pass 1 — May 2026)
            AddColumnIfMissing("salary_structures", "reimb_telephone",   "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "reimb_fuel",        "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "reimb_books",       "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "reimb_meal",        "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "reimb_uniform",     "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "annual_bonus",      "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "annual_incentive",  "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "employer_insurance","REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "employer_nps",      "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "target_ctc",        "REAL DEFAULT 0");
            AddColumnIfMissing("salary_structures", "include_gratuity",  "INTEGER DEFAULT 1");
            // Monthly salary entries table (added in v3.1)
            AddColumnIfMissing("tax_declarations", "income_other_sources", "REAL DEFAULT 0");
            AddColumnIfMissing("tax_declarations", "sec80e",               "REAL DEFAULT 0");
            AddColumnIfMissing("tax_declarations", "sec80eea",             "REAL DEFAULT 0");
            AddColumnIfMissing("tax_declarations", "sec80tta",             "REAL DEFAULT 0");
            AddColumnIfMissing("tax_declarations", "sec80ttb",             "REAL DEFAULT 0");
            AddColumnIfMissing("tax_declarations", "sec80dd",              "REAL DEFAULT 0");
            AddColumnIfMissing("tax_declarations", "sec80u",               "REAL DEFAULT 0");
            AddColumnIfMissing("tax_declarations", "lta_exemption",        "REAL DEFAULT 0");
            AddColumnIfMissing("tax_declarations", "landlord_pan",         "TEXT DEFAULT ''");
            AddColumnIfMissing("tax_declarations", "is_parent_senior",     "INTEGER DEFAULT 0");

            // Unique index on salary_structures(employee_id) — one row per employee.
            // ON CONFLICT(employee_id) in imports requires this index to work.
            using (var cmdSsIdx = conn.CreateCommand()) {
                cmdSsIdx.CommandText = @"
                    CREATE UNIQUE INDEX IF NOT EXISTS ux_salary_structures_employee
                    ON salary_structures(employee_id)";
                cmdSsIdx.ExecuteNonQuery();
            }

            // One-time cleanup: strip time portion and normalize month-name dates.
            // Pass 1 (SQL): strip " HH:mm:ss" suffix from any date containing a space.
            using (var cmdDateFix = conn.CreateCommand()) {
                cmdDateFix.CommandText = @"
                    UPDATE employees
                    SET join_date = TRIM(SUBSTR(join_date, 1, INSTR(join_date, ' ') - 1))
                    WHERE INSTR(join_date, ' ') > 0;
                    UPDATE employees
                    SET date_of_birth = TRIM(SUBSTR(date_of_birth, 1, INSTR(date_of_birth, ' ') - 1))
                    WHERE INSTR(date_of_birth, ' ') > 0";
                cmdDateFix.ExecuteNonQuery();
            }
            // Pass 2 (C#): normalize "dd-Mon-yyyy" → "dd-MM-yyyy" (SQLite can't do month-name maps).
            NormalizeDateFields(conn);
            // Pass 3: auto-assign employee codes for any rows that are blank.
            BackfillEmployeeCodes(conn);

            // Landlord records table
            using (var cmdLl = conn.CreateCommand()) {
                cmdLl.CommandText = @"
                    CREATE TABLE IF NOT EXISTS landlord_records (
                        id             INTEGER PRIMARY KEY AUTOINCREMENT,
                        employee_id    INTEGER NOT NULL,
                        financial_year TEXT NOT NULL,
                        name           TEXT DEFAULT '',
                        pan            TEXT DEFAULT '',
                        annual_rent    REAL DEFAULT 0,
                        city_type      TEXT DEFAULT 'Non-Metro',
                        from_date      TEXT DEFAULT '',
                        to_date        TEXT DEFAULT ''
                    )";
                cmdLl.ExecuteNonQuery();
            }
            AddColumnIfMissing("landlord_records", "city_type", "TEXT DEFAULT 'Non-Metro'");

            // Create monthly_salary_entries if not exists
            using (var cmd2 = conn.CreateCommand()) {
                cmd2.CommandText = @"
                    CREATE TABLE IF NOT EXISTS monthly_salary_entries (
                        id              INTEGER PRIMARY KEY AUTOINCREMENT,
                        employee_id     INTEGER NOT NULL,
                        deductor_id     INTEGER NOT NULL DEFAULT 0,
                        financial_year  TEXT NOT NULL,
                        month           INTEGER NOT NULL,
                        year            INTEGER NOT NULL,
                        basic           REAL DEFAULT 0,
                        grade_pay       REAL DEFAULT 0,
                        hra             REAL DEFAULT 0,
                        da_percent      REAL DEFAULT 0,
                        da_amount       REAL DEFAULT 0,
                        bonus           REAL DEFAULT 0,
                        commission      REAL DEFAULT 0,
                        advance_salary  REAL DEFAULT 0,
                        arrears         REAL DEFAULT 0,
                        other_allowances REAL DEFAULT 0,
                        nps_employer    REAL DEFAULT 0,
                        perq_total      REAL DEFAULT 0,
                        perq_exempted   REAL DEFAULT 0,
                        leave_enc_total    REAL DEFAULT 0,
                        leave_enc_exempted REAL DEFAULT 0,
                        pf_employee     REAL DEFAULT 0,
                        vpf             REAL DEFAULT 0,
                        professional_tax REAL DEFAULT 0,
                        esi_employee    REAL DEFAULT 0,
                        tds_deducted    REAL DEFAULT 0,
                        gross_payment   REAL DEFAULT 0,
                        gross_taxable   REAL DEFAULT 0,
                        net_salary      REAL DEFAULT 0,
                        saved_at        TEXT DEFAULT '',
                        FOREIGN KEY(employee_id) REFERENCES employees(id),
                        UNIQUE(employee_id, financial_year, month)
                    )";
                cmd2.ExecuteNonQuery();
            }

            // Named line items for monthly salary entries (perquisites + other allowances)
            using (var cmd3 = conn.CreateCommand()) {
                cmd3.CommandText = @"
                    CREATE TABLE IF NOT EXISTS salary_line_items (
                        id                       INTEGER PRIMARY KEY AUTOINCREMENT,
                        monthly_salary_entry_id  INTEGER NOT NULL,
                        category                 TEXT NOT NULL,  -- 'perq' or 'other'
                        ordinal                  INTEGER NOT NULL DEFAULT 0,
                        name                     TEXT NOT NULL DEFAULT '',
                        taxable                  REAL NOT NULL DEFAULT 0,
                        exempt                   REAL NOT NULL DEFAULT 0,
                        rule_ref                 TEXT NOT NULL DEFAULT '',
                        FOREIGN KEY(monthly_salary_entry_id) REFERENCES monthly_salary_entries(id) ON DELETE CASCADE
                    )";
                cmd3.ExecuteNonQuery();
            }

            // Deduction schedules (loan/advance recovery tracking)
            using (var cmdDs = conn.CreateCommand()) {
                cmdDs.CommandText = @"
                    CREATE TABLE IF NOT EXISTS deduction_schedules (
                        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        employee_id         INTEGER NOT NULL,
                        deductor_id         INTEGER NOT NULL DEFAULT 0,
                        type                TEXT NOT NULL DEFAULT 'Other',
                        description         TEXT NOT NULL DEFAULT '',
                        total_amount        REAL NOT NULL DEFAULT 0,
                        installment_amt     REAL NOT NULL DEFAULT 0,
                        total_installments  INTEGER NOT NULL DEFAULT 0,
                        recovered_amt       REAL NOT NULL DEFAULT 0,
                        start_fy            TEXT NOT NULL DEFAULT '',
                        start_month         INTEGER NOT NULL DEFAULT 4,
                        is_active           INTEGER NOT NULL DEFAULT 1,
                        created_at          TEXT NOT NULL DEFAULT '',
                        notes               TEXT NOT NULL DEFAULT '',
                        FOREIGN KEY(employee_id) REFERENCES employees(id)
                    )";
                cmdDs.ExecuteNonQuery();
            }

            // Named salary components (replaces rigid Reimb*/AnnualBonus single-value columns)
            using (var cmd5 = conn.CreateCommand()) {
                cmd5.CommandText = @"
                    CREATE TABLE IF NOT EXISTS salary_components (
                        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        salary_structure_id INTEGER NOT NULL,
                        category            TEXT NOT NULL,           -- allowance / reimbursement / perquisite / variable
                        ordinal             INTEGER NOT NULL DEFAULT 0,
                        name                TEXT NOT NULL DEFAULT '',
                        received            REAL NOT NULL DEFAULT 0,
                        paid                REAL NOT NULL DEFAULT 0,
                        taxable             REAL NOT NULL DEFAULT 0,
                        rule_ref            TEXT NOT NULL DEFAULT '',
                        FOREIGN KEY(salary_structure_id) REFERENCES salary_structures(id) ON DELETE CASCADE
                    )";
                cmd5.ExecuteNonQuery();
            }

            // tds_filing_history (added in v14)
            using (var fh = conn.CreateCommand()) {
                fh.CommandText = @"
                    CREATE TABLE IF NOT EXISTS tds_filing_history (
                        id              INTEGER PRIMARY KEY AUTOINCREMENT,
                        deductor_id     INTEGER NOT NULL,
                        form_type       TEXT    NOT NULL,
                        financial_year  TEXT    NOT NULL,
                        quarter         TEXT    NOT NULL,
                        is_correction   INTEGER DEFAULT 0,
                        correction_type TEXT    DEFAULT '',
                        prn             TEXT    NOT NULL DEFAULT '',
                        filed_at        TEXT    DEFAULT (datetime('now','localtime')),
                        txt_file_path   TEXT    DEFAULT '',
                        fvu_file_path   TEXT    DEFAULT '',
                        remarks         TEXT    DEFAULT ''
                    );
                    CREATE INDEX IF NOT EXISTS idx_filing_hist_ded ON tds_filing_history(deductor_id, financial_year, quarter)";
                fh.ExecuteNonQuery();
            }

            // Unique constraint on filing history — must deduplicate first or CREATE UNIQUE INDEX fails
            using (var uc = conn.CreateCommand()) {
                // Remove duplicates keeping latest row per unique key
                uc.CommandText = @"
                    DELETE FROM tds_filing_history
                    WHERE id NOT IN (
                        SELECT MAX(id) FROM tds_filing_history
                        GROUP BY deductor_id, form_type, financial_year, quarter, is_correction
                    )";
                uc.ExecuteNonQuery();
            }
            using (var uc2 = conn.CreateCommand()) {
                uc2.CommandText = @"
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_filing_hist_unique
                    ON tds_filing_history(deductor_id, form_type, financial_year, quarter, is_correction)";
                uc2.ExecuteNonQuery();
            }

            // Reimbursement claims (bill tracking per month)
            using (var cmd4 = conn.CreateCommand()) {
                cmd4.CommandText = @"
                    CREATE TABLE IF NOT EXISTS reimbursement_claims (
                        id              INTEGER PRIMARY KEY AUTOINCREMENT,
                        employee_id     INTEGER NOT NULL,
                        financial_year  TEXT NOT NULL,
                        month           INTEGER NOT NULL,
                        year            INTEGER NOT NULL,
                        category        TEXT NOT NULL,   -- 'telephone' / 'fuel' / 'books' / 'meal' / 'uniform' / 'lta'
                        eligible        REAL DEFAULT 0,  -- monthly cap from salary_structures
                        claimed         REAL DEFAULT 0,  -- amount claimed
                        bill_count      INTEGER DEFAULT 0,
                        status          TEXT DEFAULT 'pending', -- pending / submitted / approved / rejected / expired
                        notes           TEXT DEFAULT '',
                        saved_at        TEXT DEFAULT '',
                        FOREIGN KEY(employee_id) REFERENCES employees(id),
                        UNIQUE(employee_id, financial_year, month, category)
                    )";
                cmd4.ExecuteNonQuery();
            }

            // ── Payroll tables ────────────────────────────────────────────────
            try
            {
                using var pc = GetConnection();
                using var pm = pc.CreateCommand();
                pm.CommandText = @"
                    CREATE TABLE IF NOT EXISTS employees (
                        id                       INTEGER PRIMARY KEY AUTOINCREMENT,
                        employee_code            TEXT    NOT NULL DEFAULT '',
                        name                     TEXT    NOT NULL,
                        pan                      TEXT    NOT NULL DEFAULT '',
                        deductor_id              INTEGER NOT NULL DEFAULT 0,
                        designation              TEXT    DEFAULT '',
                        department               TEXT    DEFAULT '',
                        join_date                TEXT    DEFAULT '',
                        leaving_date             TEXT    DEFAULT '',
                        tax_regime               TEXT    DEFAULT 'New',
                        is_active                INTEGER DEFAULT 1,
                        email                    TEXT    DEFAULT '',
                        phone                    TEXT    DEFAULT '',
                        bank_account             TEXT    DEFAULT '',
                        bank_ifsc                TEXT    DEFAULT '',
                        fathers_name             TEXT    DEFAULT '',
                        date_of_birth            TEXT    DEFAULT '',
                        sex                      TEXT    DEFAULT 'Male',
                        pf_number                TEXT    DEFAULT '',
                        ward_circle_range        TEXT    DEFAULT '',
                        std_code                 TEXT    DEFAULT '',
                        telephone_no             TEXT    DEFAULT '',
                        flat_door_block_no       TEXT    DEFAULT '',
                        premises_building_village TEXT   DEFAULT '',
                        road_street_post_office  TEXT    DEFAULT '',
                        area_locality            TEXT    DEFAULT '',
                        town_city_district       TEXT    DEFAULT '',
                        pin_code                 TEXT    DEFAULT '',
                        state                    TEXT    DEFAULT '',
                        hra_monthly_basis        INTEGER DEFAULT 1,
                        da_for_retirement        INTEGER DEFAULT 1,
                        is_differently_abled     INTEGER DEFAULT 0
                    );
                    CREATE TABLE IF NOT EXISTS salary_structures (
                        id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                        employee_id        INTEGER NOT NULL,
                        basic              REAL    DEFAULT 0,
                        hra                REAL    DEFAULT 0,
                        da                 REAL    DEFAULT 0,
                        special_allowance  REAL    DEFAULT 0,
                        other_allowance    REAL    DEFAULT 0,
                        pf_applicable      INTEGER DEFAULT 1,
                        pf_fixed_amount    REAL    DEFAULT 0,
                        esi_applicable     INTEGER DEFAULT 0,
                        pt_state           TEXT    DEFAULT '',
                        effective_from     TEXT    DEFAULT '',
                        FOREIGN KEY(employee_id) REFERENCES employees(id)
                    );
                    CREATE TABLE IF NOT EXISTS tax_declarations (
                        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        employee_id         INTEGER NOT NULL,
                        financial_year      TEXT    NOT NULL,
                        rent_paid           REAL    DEFAULT 0,
                        hra_city_type       TEXT    DEFAULT 'Non-Metro',
                        sec_80c             REAL    DEFAULT 0,
                        sec_80d_self        REAL    DEFAULT 0,
                        sec_80d_parents     REAL    DEFAULT 0,
                        sec_80g             REAL    DEFAULT 0,
                        sec_80ccd_employee  REAL    DEFAULT 0,
                        sec_80ccd_employer  REAL    DEFAULT 0,
                        other_deductions    REAL    DEFAULT 0,
                        UNIQUE(employee_id, financial_year),
                        FOREIGN KEY(employee_id) REFERENCES employees(id)
                    );
                    CREATE TABLE IF NOT EXISTS payroll_runs (
                        id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                        employee_id         INTEGER NOT NULL,
                        deductor_id         INTEGER NOT NULL DEFAULT 0,
                        month               INTEGER NOT NULL,
                        year                INTEGER NOT NULL,
                        financial_year      TEXT    DEFAULT '',
                        basic               REAL    DEFAULT 0,
                        hra                 REAL    DEFAULT 0,
                        da                  REAL    DEFAULT 0,
                        special             REAL    DEFAULT 0,
                        medical             REAL    DEFAULT 0,
                        lta                 REAL    DEFAULT 0,
                        other               REAL    DEFAULT 0,
                        gross_salary        REAL    DEFAULT 0,
                        pf_employee         REAL    DEFAULT 0,
                        esi_employee        REAL    DEFAULT 0,
                        professional_tax    REAL    DEFAULT 0,
                        tds_deducted        REAL    DEFAULT 0,
                        other_deductions    REAL    DEFAULT 0,
                        tax_regime_used     TEXT    DEFAULT 'New',
                        hra_exemption       REAL    DEFAULT 0,
                        standard_deduction  REAL    DEFAULT 75000,
                        chapter6a_deduction REAL    DEFAULT 0,
                        taxable_income      REAL    DEFAULT 0,
                        annual_tax          REAL    DEFAULT 0,
                        surcharge           REAL    DEFAULT 0,
                        cess                REAL    DEFAULT 0,
                        total_annual_tax    REAL    DEFAULT 0,
                        ytd_tds             REAL    DEFAULT 0,
                        status              TEXT    DEFAULT 'Draft',
                        tds_entry_id        INTEGER,
                        pro_rata_days       INTEGER DEFAULT 0,
                        pro_rata_total      INTEGER DEFAULT 0,
                        UNIQUE(employee_id, deductor_id, month, year, financial_year),
                        FOREIGN KEY(employee_id) REFERENCES employees(id)
                    );";
                pm.ExecuteNonQuery();
            }
            catch { }
            // PAN verification cache table (idempotent)
            try {
                using var cacheConn = GetConnection();
                using var cacheCmd  = cacheConn.CreateCommand();
                cacheCmd.CommandText = @"CREATE TABLE IF NOT EXISTS pan_verification_cache (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    pan TEXT NOT NULL UNIQUE,
                    status TEXT NOT NULL DEFAULT 'Unknown',
                    verified_name TEXT NOT NULL DEFAULT '',
                    message TEXT NOT NULL DEFAULT '',
                    verified_at TEXT NOT NULL)";
                cacheCmd.ExecuteNonQuery();
            } catch { }

            // ── Unique indexes for duplicate-safe imports ─────────────────────
            // challans: natural key = bsr_code + challan_no + challan_date + deductor_id
            try
            {
                using var conn2 = GetConnection();
                using var cmd2  = conn2.CreateCommand();
                cmd2.CommandText = @"CREATE UNIQUE INDEX IF NOT EXISTS
                    idx_challans_natural ON challans(bsr_code, challan_no, challan_date, deductor_id)";
                cmd2.ExecuteNonQuery();
            } catch { }

            // tds_entries: natural key = deductor_id + deductee_id + entry_date + section + amount + quarter
            try
            {
                using var conn3 = GetConnection();
                using var cmd3  = conn3.CreateCommand();
                // idx_entries_natural removed — entry_no (IMP-row-hash) is now the upsert key,
                // allowing two genuine rows with same deductee+date+amount to both be stored.
                cmd3.CommandText = "SELECT 1"; // no-op
                cmd3.ExecuteNonQuery();
            } catch { }

            // ── Fix pf_applicable=0 where pf_fixed_amount>0 (data integrity) ──
            // Employees with a fixed PF amount must have pf_applicable=1 or PF is silently zero.
            try
            {
                using var connPf = GetConnection();
                using var cmdPf  = connPf.CreateCommand();
                cmdPf.CommandText = "UPDATE salary_structures SET pf_applicable=1 WHERE pf_fixed_amount > 0 AND pf_applicable=0";
                cmdPf.ExecuteNonQuery();
            } catch { }

            // ── Fix pf_employee=0 in monthly entries where structure has pf_fixed_amount>0 ──
            try
            {
                using var connMp = GetConnection();
                using var cmdMp  = connMp.CreateCommand();
                cmdMp.CommandText = @"
                    UPDATE monthly_salary_entries
                    SET pf_employee = (SELECT s.pf_fixed_amount FROM salary_structures s WHERE s.employee_id = monthly_salary_entries.employee_id),
                        net_salary  = gross_payment
                                      - (SELECT s.pf_fixed_amount FROM salary_structures s WHERE s.employee_id = monthly_salary_entries.employee_id)
                                      - esi_employee - tds_deducted
                    WHERE pf_employee = 0
                      AND employee_id IN (SELECT employee_id FROM salary_structures WHERE pf_fixed_amount > 0)";
                cmdMp.ExecuteNonQuery();
            } catch { }

            // ── Fix rules effective_from: 2026-04-01 → 2025-04-01 ────────────
            try
            {
                using var conn4 = GetConnection();
                using var cmd4  = conn4.CreateCommand();
                cmd4.CommandText = @"UPDATE tds_rules SET effective_from='2025-04-01'
                                     WHERE effective_from='2026-04-01'";
                cmd4.ExecuteNonQuery();
            } catch { }

            // ── Remove due_dates older than FY 2025-26 ───────────────────────
            try
            {
                using var connD = GetConnection();
                using var cmdD  = connD.CreateCommand();
                cmdD.CommandText = "DELETE FROM due_dates WHERE fy < '2025-26'";
                cmdD.ExecuteNonQuery();
            } catch { }

            // ── Auto-mark past quarters of previous FY as Filed ──────────────
            // New install — Q1/Q2/Q3 of 2025-26 are already past, mark Filed
            try
            {
                using var connP = GetConnection();
                using var cmdP  = connP.CreateCommand();
                cmdP.CommandText = @"
                    UPDATE due_dates SET status='Filed'
                    WHERE fy='2025-26'
                      AND quarter IN ('Q1','Q2','Q3')
                      AND status='Pending'";
                cmdP.ExecuteNonQuery();
            } catch { }

            // ── Insert FY 2026-27 due dates if missing ────────────────────────
            try
            {
                using var connF = GetConnection();
                var fy2627 = new[]
                {
                    ("2026-27","Q1","24Q","2026-07-31"), ("2026-27","Q1","26Q","2026-07-31"),
                    ("2026-27","Q2","24Q","2026-10-31"), ("2026-27","Q2","26Q","2026-10-31"),
                    ("2026-27","Q3","24Q","2027-01-31"), ("2026-27","Q3","26Q","2027-01-31"),
                    ("2026-27","Q4","24Q","2027-05-31"), ("2026-27","Q4","26Q","2027-05-31"),
                };
                using var tx = connF.BeginTransaction();
                foreach (var (fy, q, ft, dd) in fy2627)
                {
                    using var ins = connF.CreateCommand();
                    ins.CommandText = "INSERT OR IGNORE INTO due_dates (fy,quarter,form_type,due_date) VALUES(@fy,@q,@ft,@dd)";
                    ins.Parameters.AddWithValue("@fy", fy);
                    ins.Parameters.AddWithValue("@q",  q);
                    ins.Parameters.AddWithValue("@ft", ft);
                    ins.Parameters.AddWithValue("@dd", dd);
                    ins.ExecuteNonQuery();
                }
                tx.Commit();
            } catch { }

            // ── Fix due_date seeds: wrong years in existing DBs ───────────────
            // FY 2025-26 Q4 was seeded as 2027-05-31 (should be 2026-05-31)
            // FY 2024-25 dates were seeded with +1 year offsets
            try
            {
                using var conn5 = GetConnection();
                using var cmd5  = conn5.CreateCommand();
                cmd5.CommandText = @"
                    UPDATE due_dates SET due_date='2025-07-31' WHERE fy='2025-26' AND quarter='Q1' AND due_date='2026-07-31';
                    UPDATE due_dates SET due_date='2025-10-31' WHERE fy='2025-26' AND quarter='Q2' AND due_date='2026-10-31';
                    UPDATE due_dates SET due_date='2026-01-31' WHERE fy='2025-26' AND quarter='Q3' AND due_date='2027-01-31';
                    UPDATE due_dates SET due_date='2026-05-31' WHERE fy='2025-26' AND quarter='Q4' AND due_date='2027-05-31';
                    UPDATE due_dates SET due_date='2024-07-31' WHERE fy='2024-25' AND quarter='Q1' AND due_date='2025-07-31';
                    UPDATE due_dates SET due_date='2024-10-31' WHERE fy='2024-25' AND quarter='Q2' AND due_date='2025-10-31';
                    UPDATE due_dates SET due_date='2025-01-31' WHERE fy='2024-25' AND quarter='Q3' AND due_date='2026-01-31';
                    UPDATE due_dates SET due_date='2025-05-31' WHERE fy='2024-25' AND quarter='Q4' AND due_date='2026-05-31';";
                cmd5.ExecuteNonQuery();
            } catch { }

            // ── v15: dedupe filing history, keep most recent row per filing ─────
            try
            {
                using var conn6 = GetConnection();
                using var del6  = conn6.CreateCommand();
                del6.CommandText = @"
                    DELETE FROM tds_filing_history
                    WHERE id NOT IN (
                        SELECT MAX(id)
                        FROM tds_filing_history
                        GROUP BY deductor_id, form_type, financial_year, quarter, is_correction
                    )";
                del6.ExecuteNonQuery();
                using var idx6 = conn6.CreateCommand();
                idx6.CommandText = @"
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_filing_hist_unique
                    ON tds_filing_history(deductor_id, form_type, financial_year, quarter, is_correction)";
                idx6.ExecuteNonQuery();
            } catch { }

            // ── v18: fix TDS rules — deduplicate + apply Finance Act 2025 changes ──
            // Finance Act 2025 (Budget 2025-26) effective 1-Apr-2025:
            //   194   threshold: Rs.5,000 → Rs.10,000 (raised)
            //   194H  rate: 5% → 2% (reduced)
            //   194I  threshold: Rs.2,40,000 → Rs.6,00,000/year (raised to Rs.50,000/month)
            //   194IB rate: 5% → 2% (Finance Act 2023 had reduced this; seed had wrong 5%)
            //   194J  threshold: Rs.30,000 → Rs.50,000 (raised)
            //   193   Company threshold: Rs.5,000 → Rs.10,000 (unified per FA 2023)
            //   194Q  REMOVED w.e.f. 1-Apr-2025
            //   206AB REMOVED w.e.f. 1-Apr-2025 (mark inactive)
            try
            {
                using var c18 = GetConnection();
                var fixes = new[]
                {
                    // 194: FA 2025 raised threshold to Rs.10,000 — update 'Individual' row and remove old 'All' duplicate
                    "UPDATE tds_rules SET threshold_limit=10000, deductee_type='All', notes='FA 2025: threshold raised Rs.5,000->Rs.10,000 w.e.f. 1-Apr-2025' WHERE section_code='194' AND deductee_type='Individual' AND threshold_limit=5000",
                    "DELETE FROM tds_rules WHERE section_code='194' AND deductee_type='All' AND threshold_limit=10000 AND id NOT IN (SELECT MIN(id) FROM tds_rules WHERE section_code='194')",
                    // 194H: FA 2025 reduced rate 5%→2%
                    "UPDATE tds_rules SET tds_rate=2, notes='FA 2025: rate reduced 5%->2% w.e.f. 1-Apr-2025; excludes insurance commission and securities brokerage' WHERE section_code='194H'",
                    // 194I: FA 2025 raised threshold to Rs.6,00,000 (Rs.50,000/month)
                    "UPDATE tds_rules SET threshold_limit=600000, notes='FA 2025: threshold raised to Rs.6L/year (Rs.50K/month) w.e.f. 1-Apr-2025' WHERE section_code='194I'",
                    // 194IB: FA 2023 reduced rate 5%→2% (this was already in DB correctly; seed was wrong)
                    "UPDATE tds_rules SET tds_rate=2, notes='FA 2023: rate reduced 5%->2% w.e.f. 1-Oct-2023; Rs.50,000/month threshold' WHERE section_code='194IB'",
                    // 194J: FA 2025 raised threshold to Rs.50,000
                    "UPDATE tds_rules SET threshold_limit=50000, notes='FA 2025: threshold raised Rs.30,000->Rs.50,000; directors no threshold; technical services 2%' WHERE section_code='194J'",
                    // 193: FA 2023 unified threshold to Rs.10,000 for all
                    "UPDATE tds_rules SET threshold_limit=10000, deductee_type='All', notes='FA 2023: uniform Rs.10,000 threshold for all deductees' WHERE section_code='193' AND deductee_type='Company' AND threshold_limit=5000",
                    // 193: merge to single 'All' entry — delete old 'Individual' if 'All' now exists
                    "DELETE FROM tds_rules WHERE section_code='193' AND deductee_type='Individual' AND EXISTS (SELECT 1 FROM tds_rules WHERE section_code='193' AND deductee_type='All')",
                    // 194Q: REMOVED by FA 2025
                    "DELETE FROM tds_rules WHERE section_code='194Q'",
                    // 206AB: REMOVED by FA 2025 — mark inactive
                    "UPDATE tds_rules SET is_active=0, notes='REMOVED by Finance Act 2025 w.e.f. 1-Apr-2025 — no longer applicable' WHERE section_code='206AB'",
                };
                foreach (var sql in fixes)
                {
                    using var cmd18 = c18.CreateCommand();
                    cmd18.CommandText = sql;
                    cmd18.ExecuteNonQuery();
                }

                // Remove true duplicates (same section+deductee+threshold+rate, keep lowest id)
                using var del18 = c18.CreateCommand();
                del18.CommandText = @"
                    DELETE FROM tds_rules
                    WHERE id NOT IN (
                        SELECT MIN(id) FROM tds_rules
                        GROUP BY section_code, deductee_type, threshold_limit, tds_rate
                    )";
                del18.ExecuteNonQuery();
            } catch { }

            // ── v19: finalise Finance Act 2025 TDS rule corrections ──────────────
            // Corrects v18 errors and adds two fixes:
            //   194H  threshold: Rs.15,000 → Rs.20,000 (FA 2025 also raised threshold)
            //   194I  stored as monthly Rs.50,000 (not annual Rs.6,00,000 — law specifies per month)
            //   194Q  RESTORE — FA 2025 did NOT remove 194Q; only 206C(1H) cross-reference removed
            try
            {
                using var c19 = GetConnection();
                var v19 = new[]
                {
                    // 194H: threshold raised Rs.15,000→Rs.20,000 by FA 2025; rate 2% (already set by v18)
                    "UPDATE tds_rules SET threshold_limit=20000, notes='FA 2025: threshold raised Rs.15,000->Rs.20,000; rate 2% (reduced from 5% by FA 2025) w.e.f. 1-Apr-2025' WHERE section_code='194H'",
                    // 194I: change to monthly threshold Rs.50,000 (law specifies per month or part thereof)
                    "UPDATE tds_rules SET threshold_limit=50000, notes='FA 2025: monthly threshold Rs.50,000 per month or part thereof w.e.f. 1-Apr-2025; 2% P&M, 10% Land/Building' WHERE section_code='194I' AND tds_rate=2",
                    "UPDATE tds_rules SET threshold_limit=50000, notes='FA 2025: monthly threshold Rs.50,000 per month or part thereof w.e.f. 1-Apr-2025; 10% Land/Building/Furniture' WHERE section_code='194I' AND tds_rate=10",
                    // 194Q: RESTORE — FA 2025 did NOT remove 194Q; only 206C(1H) cross-reference removed
                    // Re-insert if deleted by v18, using INSERT OR IGNORE
                    @"INSERT OR IGNORE INTO tds_rules
                      (section_code,nature_of_payment,deductee_type,is_resident,
                       threshold_limit,tds_rate,surcharge_rate,cess_rate,
                       effective_from,reference_act,notes,is_active)
                      VALUES('194Q','Purchase of Goods','All',1,5000000,0.1,0,0,
                             '2025-04-01','IT Act 2025 s.194Q',
                             'Buyer turnover >Rs.10Cr; 206C(1H) cross-ref removed by FA 2025 but 194Q itself continues',
                             1)",
                    // Ensure 194Q is active (v18 may have deleted it)
                    "UPDATE tds_rules SET is_active=1, notes='Buyer turnover >Rs.10Cr; 206C(1H) cross-ref removed by FA 2025 but 194Q itself continues' WHERE section_code='194Q'",
                    // 206AB: confirm omitted/inactive
                    "UPDATE tds_rules SET is_active=0, notes='Omitted by Finance Act 2025 w.e.f. 1-Apr-2025 — higher TDS for ITR non-filers no longer applicable' WHERE section_code='206AB'",
                };
                foreach (var sql in v19)
                {
                    using var cmd19 = c19.CreateCommand();
                    cmd19.CommandText = sql;
                    cmd19.ExecuteNonQuery();
                }
            } catch { }

            // ── v17: fix filing history UNIQUE index to include correction_type ──
            // Old index prevented C1/C2/C3 corrections being stored as separate history rows.
            try
            {
                using var conn7 = GetConnection();
                using var cmd7  = conn7.CreateCommand();
                cmd7.CommandText = "DROP INDEX IF EXISTS idx_filing_hist_unique";
                cmd7.ExecuteNonQuery();
                cmd7.CommandText = @"
                    DELETE FROM tds_filing_history
                    WHERE id NOT IN (
                        SELECT MAX(id) FROM tds_filing_history
                        GROUP BY deductor_id, form_type, financial_year, quarter, is_correction, COALESCE(correction_type,'')
                    )";
                cmd7.ExecuteNonQuery();
                cmd7.CommandText = @"
                    CREATE UNIQUE INDEX IF NOT EXISTS idx_filing_hist_unique
                    ON tds_filing_history(deductor_id, form_type, financial_year, quarter, is_correction, COALESCE(correction_type,''))";
                cmd7.ExecuteNonQuery();
            } catch { }
        }

        // ── Seed TDS Rules 2026 ───────────────────────────────────────────────
        private static void SeedTdsRules2026()
        {
            using var conn = GetConnection();
            using var chk  = conn.CreateCommand();
            chk.CommandText = "SELECT COUNT(*) FROM tds_rules";
            var count = (long)(chk.ExecuteScalar() ?? 0L);
            if (count > 0) return;

            // Income-tax Act 2025 / Rules 2026 — effective 1 April 2026
            // Format: (section, nature, deductee_type, is_resident, threshold, rate, surcharge, cess, eff_from, ref_act, notes)
            var rules = new (string sec, string nature, string dtype, int resident,
                             double threshold, double rate, double sc, double cess,
                             string effFrom, string refAct, string notes)[]
            {
                // Cess column: 4 = add 4% H&E Cess at source; 0 = deductee pays cess in ITR
                // Rule: Cess=4 only for Section 192 (salary) and non-resident sections (195).
                //       All resident non-salary sections: Cess=0 (Finance Act 2009 + CBDT Cir 3/2025)

                // ── Salary — full liability computed incl. cess ───────────────
                ("192",   "Salary",                            "Individual", 1, 300000,  0,    0, 4, "2025-04-01", "IT Act 2025 s.192",   "Slab rates; employer computes full tax incl. cess"),
                // ── Resident non-salary sections — cess=0 ─────────────────────
                ("192A",  "PF Withdrawal",                     "Individual", 1,  50000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.192A",  "PAN mandatory; deductee pays cess in ITR"),

                // ── Interest / Securities ──────────────────────────────────────
                ("193",   "Interest on Securities",            "All",        1,  10000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.193",   "FA 2023: unified Rs.10,000 threshold for all deductees"),
                ("194",   "Dividends",                         "All",        1,  10000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194",   "FA 2025: threshold raised Rs.5,000->Rs.10,000 w.e.f. 1-Apr-2025"),
                ("194A",  "Interest other than securities",    "Individual", 1,  40000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194A",  "₹50K for bank/co-op/post; ₹50K senior citizen"),
                ("194A",  "Interest other than securities",    "Company",    1,  40000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194A",  ""),

                // ── Lottery / Games ────────────────────────────────────────────
                ("194B",  "Lottery Winnings",                  "All",        1,  10000, 30,    0, 0, "2025-04-01", "IT Act 2025 s.194B",  ""),
                ("194BA", "Online Games",                      "All",        1,      0, 30,    0, 0, "2025-04-01", "IT Act 2025 s.194BA", "No threshold; net winnings basis"),
                ("194BB", "Winnings from Horse Race",          "All",        1,  10000, 30,    0, 0, "2025-04-01", "IT Act 2025 s.194BB", ""),

                // ── Contractors ────────────────────────────────────────────────
                ("194C",  "Payment to Contractor",             "Individual", 1,  30000,  1,    0, 0, "2025-04-01", "IT Act 2025 s.194C",  "Single ₹30K; aggregate ₹1L p.a."),
                ("194C",  "Payment to Contractor",             "HUF",        1,  30000,  1,    0, 0, "2025-04-01", "IT Act 2025 s.194C",  ""),
                ("194C",  "Payment to Contractor",             "Company",    1,  30000,  2,    0, 0, "2025-04-01", "IT Act 2025 s.194C",  ""),

                // ── Insurance / Commission / Rent ──────────────────────────────
                ("194D",  "Insurance Commission",              "Individual", 1,  15000,  5,    0, 0, "2025-04-01", "IT Act 2025 s.194D",  ""),
                ("194D",  "Insurance Commission",              "Company",    1,  15000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194D",  ""),
                ("194DA", "Life Insurance Maturity",           "All",        1, 100000,  5,    0, 0, "2025-04-01", "IT Act 2025 s.194DA", "On income portion only"),
                ("194G",  "Commission on Lottery",             "All",        1,  15000,  5,    0, 0, "2025-04-01", "IT Act 2025 s.194G",  ""),
                ("194H",  "Commission / Brokerage",            "All",        1,  20000,  2,    0, 0, "2025-04-01", "IT Act 2025 s.194H",  "FA 2025: rate 2% (reduced from 5%); threshold Rs.20,000 (raised from Rs.15,000) w.e.f. 1-Apr-2025"),
                ("194I",  "Rent - Plant & Machinery",          "All",        1,  50000,  2,    0, 0, "2025-04-01", "IT Act 2025 s.194I",  "FA 2025: monthly threshold Rs.50,000 per month or part thereof w.e.f. 1-Apr-2025"),
                ("194I",  "Rent - Land/Building/Furniture",    "All",        1,  50000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194I",  "FA 2025: monthly threshold Rs.50,000 per month or part thereof w.e.f. 1-Apr-2025"),
                ("194IA", "Transfer of Immovable Property",   "All",        1,5000000,  1,    0, 0, "2025-04-01", "IT Act 2025 s.194IA", "Buyer deducts; agri land exempt"),
                ("194IB", "Rent by Individual/HUF",            "Individual", 1,  50000,  2,    0, 0, "2025-04-01", "IT Act 2025 s.194IB", "FA 2023: rate 2% (reduced from 5% w.e.f. 1-Oct-2023); Rs.50,000/month threshold; Ind/HUF not subject to tax audit"),
                ("194IC", "Joint Dev Agreement",               "All",        1,      0, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194IC", "No threshold"),

                // ── Professional Fees ─────────────────────────────────────────
                ("194J",  "Professional Fees",                 "All",        1,  50000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194J",  "FA 2025: threshold raised Rs.30,000->Rs.50,000 w.e.f. 1-Apr-2025; directors no threshold"),
                ("194J",  "Technical Services / Royalty",      "All",        1,  50000,  2,    0, 0, "2025-04-01", "IT Act 2025 s.194J",  "FA 2025: threshold Rs.50,000; technical services/call centre/royalty: 2%"),

                // ── Mutual Fund / Securities ──────────────────────────────────
                ("194K",  "Income from MF Units",              "All",        1,   5000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194K",  "Dividend; cap gains exempt from TDS"),
                ("194LA", "Compensation on Immovable Property","All",        1, 250000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194LA", "Rural agri land exempt"),

                // ── Large Contracts / Cash / E-comm ──────────────────────────
                ("194M",  "Contract Commission (>50L indiv)",  "Individual", 1,5000000,  2,    0, 0, "2025-04-01", "IT Act 2025 s.194M",  "Rate reduced 5%→2% by FA 2024 w.e.f. 01-Oct-2024"),
                ("194N",  "Cash Withdrawal (return-filer)",    "All",        1,10000000, 2,    0, 0, "2025-04-01", "IT Act 2025 s.194N",  "≥₹1Cr: 2% for ITR filers"),
                ("194N",  "Cash Withdrawal (non-filer ≤1Cr)",  "All",        1,  200000, 2,    0, 0, "2025-04-01", "IT Act 2025 s.194N",  "₹20L–1Cr: 2% for non-filers"),
                ("194N",  "Cash Withdrawal (non-filer >1Cr)",  "All",        1,10000000, 5,    0, 0, "2025-04-01", "IT Act 2025 s.194N",  ">₹1Cr: 5% for non-filers"),
                ("194O",  "E-commerce Operator",               "All",        1,      0,  1,    0, 0, "2025-04-01", "IT Act 2025 s.194O",  "w.e.f. Oct 2020"),
                ("194P",  "Senior Citizen (75+) Bank",         "Individual", 1,      0,  0,    0, 4, "2025-04-01", "IT Act 2025 s.194P",  "Bank computes full tax incl. cess; like s.192"),
                ("194Q",  "Purchase of Goods",                 "All",        1,5000000,0.1,    0, 0, "2025-04-01", "IT Act 2025 s.194Q",  "Buyer turnover >Rs.10Cr; FA 2025 removed 206C(1H) cross-ref but 194Q itself continues"),
                ("194R",  "Benefit/Perquisite in Business",    "All",        1,  20000, 10,    0, 0, "2025-04-01", "IT Act 2025 s.194R",  "Cash/non-cash; FMV for non-cash"),
                ("194S",  "VDA / Crypto Transfer",             "All",        1,  10000,  1,    0, 0, "2025-04-01", "IT Act 2025 s.194S",  "₹10K specified persons; ₹50K others"),

                // ── Non-Residents — cess=4 (TDS is often final tax) ──────────
                ("195",   "Payments to Non-Residents",         "NRI - Individual",0,0,  0,    0, 4, "2025-04-01", "IT Act 2025 s.195",   "Rate per DTAA or Act; cess+surcharge apply under Act rates"),
                ("195",   "Payments to Non-Residents",         "NRI - Company",   0,0,  0,    0, 4, "2025-04-01", "IT Act 2025 s.195",   "Foreign co: surcharge 2% (>1Cr), 5% (>10Cr)"),

                // ── Higher rates — resident; cess=0 ──────────────────────────
                ("206AA", "Higher TDS - PAN not available",    "All",        1,      0, 20,    0, 0, "2025-04-01", "IT Act 2025 s.206AA", "Higher of 20% or twice applicable rate"),
                ("206AB", "Higher TDS - ITR not filed",        "All",        1,      0, 20,    0, 0, "2025-04-01", "IT Act 2025 s.206AB", "Higher of 5% or twice normal rate; check TRACES"),
            };

            using var tx = conn.BeginTransaction();
            foreach (var r in rules)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = @"
                    INSERT INTO tds_rules
                    (section_code,nature_of_payment,deductee_type,is_resident,
                     threshold_limit,tds_rate,surcharge_rate,cess_rate,
                     effective_from,reference_act,notes)
                    VALUES (@sc,@np,@dt,@ir,@th,@tr,@sr,@cr,@ef,@ra,@nt)";
                ins.Parameters.AddWithValue("@sc", r.sec);
                ins.Parameters.AddWithValue("@np", r.nature);
                ins.Parameters.AddWithValue("@dt", r.dtype);
                ins.Parameters.AddWithValue("@ir", r.resident);
                ins.Parameters.AddWithValue("@th", r.threshold);
                ins.Parameters.AddWithValue("@tr", r.rate);
                ins.Parameters.AddWithValue("@sr", r.sc);
                ins.Parameters.AddWithValue("@cr", r.cess);
                ins.Parameters.AddWithValue("@ef", r.effFrom);
                ins.Parameters.AddWithValue("@ra", r.refAct);
                ins.Parameters.AddWithValue("@nt", r.notes);
                ins.ExecuteNonQuery();
            }
            tx.Commit();
        }

        // ── Seed FVU config (configurable, not hardcoded) ────────────────────
        private static void SeedFvuConfig()
        {
            using var conn = GetConnection();
            using var chk  = conn.CreateCommand();
            chk.CommandText = "SELECT COUNT(*) FROM fvu_format_config";
            if ((long)(chk.ExecuteScalar() ?? 0L) > 0) return;

            var configs = new (string key, string form, string ver, string eff, string notes)[]
            {
                ("FVU_FORMAT_26Q", "26Q", "9.0", "2026-04-01",
                 "Non-salary TDS return format v9.0 effective FY 2025-26"),
                ("FVU_FORMAT_24Q", "24Q", "9.0", "2026-04-01",
                 "Salary TDS return format v9.0 effective FY 2025-26"),
            };

            foreach (var c in configs)
            {
                using var ins = conn.CreateCommand();
                ins.CommandText = @"INSERT INTO fvu_format_config
                    (config_key,form_type,version,delimiter,effective_from,notes)
                    VALUES(@ck,@ft,@v,'^',@ef,@nt)";
                ins.Parameters.AddWithValue("@ck", c.key);
                ins.Parameters.AddWithValue("@ft", c.form);
                ins.Parameters.AddWithValue("@v",  c.ver);
                ins.Parameters.AddWithValue("@ef", c.eff);
                ins.Parameters.AddWithValue("@nt", c.notes);
                ins.ExecuteNonQuery();
            }
        }

        // ── Seed default admin user ───────────────────────────────────────────
        private static void SeedDefaultAdmin()
        {
            using var conn = GetConnection();
            using var chk  = conn.CreateCommand();
            chk.CommandText = "SELECT COUNT(*) FROM users";
            if ((long)(chk.ExecuteScalar() ?? 0L) > 0) return;

            using var ins = conn.CreateCommand();
            ins.CommandText = @"INSERT INTO users (username,password,full_name,role,email,status)
                VALUES('admin',@pw,'Administrator','Super Admin','admin@capitaldesk.co.in','Active')";
            ins.Parameters.AddWithValue("@pw", HashPassword("admin@123"));
            ins.ExecuteNonQuery();
        }

        public static string HashPassword(string pw) =>
            Convert.ToHexString(
                System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(pw)));

        public static void LogAction(string username, string action, string module, string details = "")
        {
            try
            {
                using var conn = GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO audit_log (username,action,module,details)
                                    VALUES(@u,@a,@m,@d)";
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@a", action);
                cmd.Parameters.AddWithValue("@m", module);
                cmd.Parameters.AddWithValue("@d", details);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
        /// <summary>
        /// Seed rich sample data so every screen shows real data on first run.
        /// Only seeds if tables are empty.
        /// </summary>
        private static void SeedSampleData()
        {
            using var conn = GetConnection();
            using var chk  = conn.CreateCommand();
            chk.CommandText = "SELECT COUNT(*) FROM deductors";
            if ((long)(chk.ExecuteScalar() ?? 0L) > 0) return;

            using var tx = conn.BeginTransaction();

            // ── 2 Deductors ───────────────────────────────────────────────────
            void AddDeductor(string name, string tan, string pan, string city, string state, string type)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO deductors
                    (company_name,tan,pan,address,city,state,pincode,contact_person,phone,email,financial_year,deductor_type,is_active)
                    VALUES(@nm,@tn,@pn,'Commercial Street',@ct,@st,'560001','Finance Team','9876543210','finance@company.in','2026-27',@tp,1)";
                cmd.Parameters.AddWithValue("@nm", name);
                cmd.Parameters.AddWithValue("@tn", tan);
                cmd.Parameters.AddWithValue("@pn", pan);
                cmd.Parameters.AddWithValue("@ct", city);
                cmd.Parameters.AddWithValue("@st", state);
                cmd.Parameters.AddWithValue("@tp", type);
                cmd.ExecuteNonQuery();
            }
            AddDeductor("Innovatech Solutions Pvt Ltd", "BLRA12345B", "AACCI1234C", "Bengaluru", "Karnataka", "Company");
            AddDeductor("Mehta & Associates LLP",       "MUMA98765Z", "AAFM9876D",  "Mumbai",    "Maharashtra", "Firm/LLP");

            // Get deductor IDs
            long ded1Id, ded2Id;
            using (var idCmd = conn.CreateCommand())
            {
                idCmd.CommandText = "SELECT id FROM deductors ORDER BY id ASC LIMIT 1";
                ded1Id = (long)(idCmd.ExecuteScalar() ?? 1L);
            }
            using (var idCmd = conn.CreateCommand())
            {
                idCmd.CommandText = "SELECT id FROM deductors ORDER BY id DESC LIMIT 1";
                ded2Id = (long)(idCmd.ExecuteScalar() ?? 2L);
            }

            // ── 8 Deductees ───────────────────────────────────────────────────
            var deductees = new[]
            {
                ("RAJKU1234A", "Raj Kumar & Sons",         "194C", "Individual", 1.00),
                ("PRIYA5678B", "Priya IT Consultants",     "194J", "Firm",      10.00),
                ("MEHTA9012C", "Mehta Trading Company",    "194A", "Company",   10.00),
                ("SHARM3456D", "Sharma Enterprises",       "194H", "Firm",       5.00),
                ("SINGH7890E", "Singh & Associates",       "194C", "Individual", 1.00),
                ("KAPOO2345F", "Kapoor Realty Ltd",        "194I", "Company",   10.00),
                ("VERMA6789G", "Verma Tech Services",      "194J", "Individual",10.00),
                ("GUPTA1122H", "Gupta Logistics Pvt Ltd",  "194C", "Company",   2.00),
            };
            int code = 1;
            var deeIds = new List<long>();
            foreach (var (pan, name, sec, dtype, rate) in deductees)
            {
                using var dd = conn.CreateCommand();
                dd.CommandText = @"INSERT INTO deductees
                    (deductee_code,name,pan,section,rate,deductee_type,is_resident,itr_filed)
                    VALUES(@c,@n,@p,@s,@r,@dt,1,1)";
                dd.Parameters.AddWithValue("@c", $"DED{code:D5}");
                dd.Parameters.AddWithValue("@n", name);
                dd.Parameters.AddWithValue("@p", pan);
                dd.Parameters.AddWithValue("@s", sec);
                dd.Parameters.AddWithValue("@r", rate);
                dd.Parameters.AddWithValue("@dt", dtype);
                dd.ExecuteNonQuery();
                using var lastId = conn.CreateCommand();
                lastId.CommandText = "SELECT last_insert_rowid()";
                deeIds.Add((long)(lastId.ExecuteScalar() ?? 0L));
                code++;
            }

            // ── TDS Entries: 4 quarters, 2 deductors, realistic amounts ──────
            var entries = new[]
            {
                // (deductorId, deducteeIdx, quarter, date, amount, section, status)
                (ded1Id, 0, "Q1", "2026-04-15", 250000.0, "194C", "Paid"),
                (ded1Id, 1, "Q1", "2026-05-10", 180000.0, "194J", "Paid"),
                (ded1Id, 2, "Q1", "2026-06-20", 320000.0, "194A", "Paid"),
                (ded1Id, 3, "Q2", "2026-07-12", 150000.0, "194H", "Paid"),
                (ded1Id, 0, "Q2", "2026-08-05", 275000.0, "194C", "Paid"),
                (ded1Id, 4, "Q2", "2026-09-18", 90000.0,  "194C", "Paid"),
                (ded1Id, 1, "Q3", "2026-10-08", 220000.0, "194J", "Paid"),
                (ded1Id, 5, "Q3", "2026-11-14", 480000.0, "194I", "Paid"),
                (ded1Id, 6, "Q3", "2026-12-22", 165000.0, "194J", "Pending"),
                (ded1Id, 7, "Q4", "2027-01-10", 310000.0, "194C", "Pending"),
                (ded1Id, 2, "Q4", "2027-02-18", 290000.0, "194A", "Overdue"),
                (ded1Id, 3, "Q4", "2027-03-05", 125000.0, "194H", "Overdue"),
                (ded2Id, 0, "Q1", "2026-04-22", 180000.0, "194C", "Paid"),
                (ded2Id, 6, "Q1", "2026-06-15", 240000.0, "194J", "Paid"),
                (ded2Id, 7, "Q2", "2026-08-30", 520000.0, "194C", "Paid"),
                (ded2Id, 1, "Q3", "2026-10-20", 195000.0, "194J", "Pending"),
                (ded2Id, 4, "Q4", "2027-01-25", 140000.0, "194C", "Overdue"),
            };

            int entryNo = 1;
            foreach (var (did, deeIdx, qtr, date, amt, sec, status) in entries)
            {
                var deeId   = deeIds[deeIdx];
                double rate = sec switch { "194J" => 10.0, "194I" => 10.0, "194H" => 5.0, "194A" => 10.0, _ => did == ded1Id ? 1.0 : 2.0 };
                double tds  = amt * rate / 100.0;
                double cess  = 0; // cess=0 for all resident non-salary sections
                double total = tds;

                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO tds_entries
                    (entry_no,deductor_id,deductee_id,entry_date,financial_year,quarter,
                     section,amount,rate,tds_amount,surcharge,cess,interest,late_fee,
                     total_tds,status,remarks)
                    VALUES(@en,@did,@deid,@dt,@fy,@qtr,@sec,@amt,@rate,@tds,0,@cess,0,0,@tot,@st,'Sample data')";
                cmd.Parameters.AddWithValue("@en",   $"TDS{entryNo:D5}");
                cmd.Parameters.AddWithValue("@did",  did);
                cmd.Parameters.AddWithValue("@deid", deeId);
                cmd.Parameters.AddWithValue("@dt",   date);
                cmd.Parameters.AddWithValue("@fy",   "2026-27");
                cmd.Parameters.AddWithValue("@qtr",  qtr);
                cmd.Parameters.AddWithValue("@sec",  sec);
                cmd.Parameters.AddWithValue("@amt",  amt);
                cmd.Parameters.AddWithValue("@rate", rate);
                cmd.Parameters.AddWithValue("@tds",  tds);
                cmd.Parameters.AddWithValue("@cess", cess);
                cmd.Parameters.AddWithValue("@tot",  total);
                cmd.Parameters.AddWithValue("@st",   status);
                cmd.ExecuteNonQuery();
                entryNo++;
            }

            // ── Challans: one per quarter ─────────────────────────────────────
            var challans = new[]
            {
                (ded1Id, "Q1", "2026-07-10", "CHL001", "SBIN0001234", 8400.0,  336.0, "Paid"),
                (ded1Id, "Q2", "2026-10-08", "CHL002", "HDFC0004567", 7105.0,  284.2, "Paid"),
                (ded1Id, "Q3", "2027-01-12", "CHL003", "ICIC0007890", 9350.0,  374.0, "Paid"),
                (ded1Id, "Q4", "2027-04-15", "CHL004", "SBIN0001234", 7657.5,  306.3, "Pending"),
                (ded2Id, "Q1", "2026-07-18", "CHL005", "HDFC0004567", 6930.0,  277.2, "Paid"),
                (ded2Id, "Q2", "2026-10-20", "CHL006", "ICIC0007890", 10764.0, 430.6, "Paid"),
                (ded2Id, "Q3", "2027-01-22", "CHL007", "SBIN0001234", 4056.0,  162.2, "Pending"),
            };

            foreach (var (did, qtr, date, no, bsr, tds, cess, status) in challans)
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO challans
                    (deductor_id,challan_no,bsr_code,challan_date,financial_year,quarter,
                     section,tds_amount,surcharge,cess,interest,late_fee,total_amount,status,bank_name)
                    VALUES(@did,@no,@bsr,@dt,'2026-27',@qtr,'194C',@tds,0,@cess,0,0,@tot,@st,'SBI')";
                cmd.Parameters.AddWithValue("@did",  did);
                cmd.Parameters.AddWithValue("@no",   no);
                cmd.Parameters.AddWithValue("@bsr",  bsr);
                cmd.Parameters.AddWithValue("@dt",   date);
                cmd.Parameters.AddWithValue("@qtr",  qtr);
                cmd.Parameters.AddWithValue("@tds",  tds);
                cmd.Parameters.AddWithValue("@cess", cess);
                cmd.Parameters.AddWithValue("@tot",  tds + cess);
                cmd.Parameters.AddWithValue("@st",   status);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        // ── Auto-delete all sample data when first real deductor is saved ─────
        public static void DeleteSampleData()
        {
            try
            {
                using var conn = GetConnection();
                using var tx   = conn.BeginTransaction();

                // Identify sample deductor IDs by their seeded TANs
                var sampleIds = new List<long>();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT id FROM deductors WHERE tan IN ('BLRA12345B','MUMA98765Z')";
                    using var r = cmd.ExecuteReader();
                    while (r.Read()) sampleIds.Add(r.GetInt64(0));
                }
                if (sampleIds.Count == 0) return; // already cleaned or never seeded

                foreach (var did in sampleIds)
                {
                    // Delete challans for this deductor
                    using var dc = conn.CreateCommand();
                    dc.CommandText = "DELETE FROM challans WHERE deductor_id=@id";
                    dc.Parameters.AddWithValue("@id", did);
                    dc.ExecuteNonQuery();

                    // Delete TDS entries for this deductor
                    using var de = conn.CreateCommand();
                    de.CommandText = "DELETE FROM tds_entries WHERE deductor_id=@id";
                    de.Parameters.AddWithValue("@id", did);
                    de.ExecuteNonQuery();

                    // Delete the deductor
                    using var dd = conn.CreateCommand();
                    dd.CommandText = "DELETE FROM deductors WHERE id=@id";
                    dd.Parameters.AddWithValue("@id", did);
                    dd.ExecuteNonQuery();
                }

                // Delete sample deductees (marked with remarks 'Sample data' or seeded PANs)
                using var dde = conn.CreateCommand();
                dde.CommandText = @"DELETE FROM deductees WHERE pan IN
                    ('RAJKU1234A','PRIYA5678B','MEHTA9012C','SHARM3456D',
                     'SINGH7890E','KAPOO2345F','VERMA6789G','GUPTA1122H')";
                dde.ExecuteNonQuery();

                tx.Commit();
            }
            catch { }
        }

        /// <summary>
        /// Run SQLite VACUUM + integrity_check in background.
        /// Called once per session after login.
        /// </summary>
        public static void VacuumAndCheck(string appDataPath)
        {
            try
            {
                using var conn = GetConnection();
                // Integrity check
                using var chk = conn.CreateCommand();
                chk.CommandText = "PRAGMA integrity_check";
                var result = chk.ExecuteScalar()?.ToString() ?? "";
                if (result != "ok")
                    LogAction("system", "DB_INTEGRITY_FAIL", "Database", result);

                // Analyse for query planner (fast, non-locking)
                using var ana = conn.CreateCommand();
                ana.CommandText = "PRAGMA analysis_limit=1000; ANALYZE";
                ana.ExecuteNonQuery();

                // VACUUM only if DB > 10MB (expensive)
                var dbPath = Path.Combine(appDataPath, AppConstants.DbFileName);
                if (File.Exists(dbPath) && new FileInfo(dbPath).Length > 10_000_000)
                {
                    using var vac = conn.CreateCommand();
                    vac.CommandText = "VACUUM";
                    vac.ExecuteNonQuery();
                    LogAction("system", "DB_VACUUM", "Database", "Completed");
                }
            }
            catch { }
        }


        /// <summary>Get a persistent app setting by key.</summary>
        public static string GetSetting(string key, string defaultValue = "")
        {
            try
            {
                using var conn = GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS app_settings (
                        key   TEXT PRIMARY KEY,
                        value TEXT NOT NULL DEFAULT ''
                    );
                    SELECT value FROM app_settings WHERE key=@k LIMIT 1";
                cmd.Parameters.AddWithValue("@k", key);
                var result = cmd.ExecuteScalar();
                return result == null || result == DBNull.Value
                    ? defaultValue
                    : result.ToString() ?? defaultValue;
            }
            catch { return defaultValue; }
        }

        /// <summary>Persist an app setting.</summary>
        public static void SetSetting(string key, string value)
        {
            try
            {
                using var conn = GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS app_settings (
                        key   TEXT PRIMARY KEY,
                        value TEXT NOT NULL DEFAULT ''
                    );
                    INSERT INTO app_settings(key,value) VALUES(@k,@v)
                    ON CONFLICT(key) DO UPDATE SET value=excluded.value";
                cmd.Parameters.AddWithValue("@k", key);
                cmd.Parameters.AddWithValue("@v", value);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        private static void BackfillEmployeeCodes(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            // Find highest existing EMP#####
            using var maxCmd = conn.CreateCommand();
            maxCmd.CommandText = @"
                SELECT COALESCE(MAX(CAST(REPLACE(employee_code,'EMP','') AS INTEGER)),0)
                FROM employees
                WHERE employee_code LIKE 'EMP%'
                  AND REPLACE(employee_code,'EMP','') GLOB '[0-9]*'";
            int next = Convert.ToInt32(maxCmd.ExecuteScalar() ?? 0) + 1;

            // Fetch all employees with blank code
            using var sel = conn.CreateCommand();
            sel.CommandText = "SELECT id FROM employees WHERE TRIM(COALESCE(employee_code,''))='' ORDER BY id";
            var ids = new System.Collections.Generic.List<long>();
            using (var rdr = sel.ExecuteReader())
                while (rdr.Read()) ids.Add(rdr.GetInt64(0));

            foreach (var id in ids)
            {
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE employees SET employee_code=@c WHERE id=@id";
                upd.Parameters.AddWithValue("@c", $"EMP{next:D5}");
                upd.Parameters.AddWithValue("@id", id);
                upd.ExecuteNonQuery();
                next++;
            }
        }

        // Normalize "dd-Mon-yyyy" dates in employees table → "dd-MM-yyyy"
        private static void NormalizeDateFields(Microsoft.Data.Sqlite.SqliteConnection conn)
        {
            var monthMap = new System.Collections.Generic.Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["Jan"]="01",["Feb"]="02",["Mar"]="03",["Apr"]="04",["May"]="05",["Jun"]="06",
                ["Jul"]="07",["Aug"]="08",["Sep"]="09",["Sept"]="09",["Oct"]="10",["Nov"]="11",["Dec"]="12"
            };

            static string? FixDate(string? val, System.Collections.Generic.Dictionary<string,string> map)
            {
                if (string.IsNullOrEmpty(val)) return val;
                var m = System.Text.RegularExpressions.Regex.Match(val, @"^(\d{2})-([A-Za-z]+)-(\d{4})$");
                if (!m.Success) return null; // not this format, no change
                return map.TryGetValue(m.Groups[2].Value, out var mm)
                    ? $"{m.Groups[1].Value}-{mm}-{m.Groups[3].Value}"
                    : null;
            }

            using var sel = conn.CreateCommand();
            sel.CommandText = "SELECT id, join_date, date_of_birth FROM employees";
            var toUpdate = new System.Collections.Generic.List<(long id, string jd, string dob)>();
            using (var r = sel.ExecuteReader())
            {
                while (r.Read())
                {
                    long id  = r.GetInt64(0);
                    var  jd  = r.IsDBNull(1) ? "" : r.GetString(1);
                    var  dob = r.IsDBNull(2) ? "" : r.GetString(2);
                    var  jd2 = FixDate(jd,  monthMap) ?? jd;
                    var  dob2= FixDate(dob, monthMap) ?? dob;
                    if (jd2 != jd || dob2 != dob)
                        toUpdate.Add((id, jd2, dob2));
                }
            }
            foreach (var (id, jd, dob) in toUpdate)
            {
                using var upd = conn.CreateCommand();
                upd.CommandText = "UPDATE employees SET join_date=@j, date_of_birth=@d WHERE id=@id";
                upd.Parameters.AddWithValue("@j",  jd);
                upd.Parameters.AddWithValue("@d",  dob);
                upd.Parameters.AddWithValue("@id", id);
                upd.ExecuteNonQuery();
            }
        }

        // ── Filing History ────────────────────────────────────────────────────
        public static void SaveFilingHistory(
            int deductorId, string formType, string fy, string quarter,
            bool isCorrection, string correctionType, string prn,
            string txtPath = "", string fvuPath = "", string remarks = "")
        {
            try
            {
                using var conn = GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"INSERT INTO tds_filing_history
                    (deductor_id,form_type,financial_year,quarter,is_correction,
                     correction_type,prn,txt_file_path,fvu_file_path,remarks)
                    VALUES(@did,@ft,@fy,@qt,@ic,@ct,@prn,@txt,@fvu,@rm)
                    ON CONFLICT(deductor_id,form_type,financial_year,quarter,is_correction)
                    DO UPDATE SET
                        correction_type = excluded.correction_type,
                        prn             = CASE WHEN excluded.prn != '' THEN excluded.prn ELSE prn END,
                        txt_file_path   = CASE WHEN excluded.txt_file_path != '' THEN excluded.txt_file_path ELSE txt_file_path END,
                        fvu_file_path   = CASE WHEN excluded.fvu_file_path != '' THEN excluded.fvu_file_path ELSE fvu_file_path END,
                        remarks         = CASE WHEN excluded.remarks != '' THEN excluded.remarks ELSE remarks END,
                        filed_at        = datetime('now','localtime')";
                cmd.Parameters.AddWithValue("@did", deductorId);
                cmd.Parameters.AddWithValue("@ft",  formType);
                cmd.Parameters.AddWithValue("@fy",  fy);
                cmd.Parameters.AddWithValue("@qt",  quarter);
                cmd.Parameters.AddWithValue("@ic",  isCorrection ? 1 : 0);
                cmd.Parameters.AddWithValue("@ct",  correctionType);
                cmd.Parameters.AddWithValue("@prn", prn);
                cmd.Parameters.AddWithValue("@txt", txtPath);
                cmd.Parameters.AddWithValue("@fvu", fvuPath);
                cmd.Parameters.AddWithValue("@rm",  remarks);
                cmd.ExecuteNonQuery();
            }
            catch { }
        }

        public static List<FilingHistoryRecord> GetFilingHistory(
            int deductorId, string fy, string formType, string quarter)
        {
            var list = new List<FilingHistoryRecord>();
            try
            {
                using var conn = GetConnection();
                using var cmd  = conn.CreateCommand();
                cmd.CommandText = @"SELECT * FROM tds_filing_history
                    WHERE deductor_id=@did AND financial_year=@fy
                      AND form_type=@ft AND quarter=@qt
                    ORDER BY filed_at DESC";
                cmd.Parameters.AddWithValue("@did", deductorId);
                cmd.Parameters.AddWithValue("@fy",  fy);
                cmd.Parameters.AddWithValue("@ft",  formType);
                cmd.Parameters.AddWithValue("@qt",  quarter);
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    list.Add(new FilingHistoryRecord
                    {
                        Id             = Convert.ToInt32(r["id"]),
                        DeductorId     = Convert.ToInt32(r["deductor_id"]),
                        FormType       = r["form_type"]?.ToString() ?? "",
                        FinancialYear  = r["financial_year"]?.ToString() ?? "",
                        Quarter        = r["quarter"]?.ToString() ?? "",
                        IsCorrection   = Convert.ToInt32(r["is_correction"]) == 1,
                        CorrectionType = r["correction_type"]?.ToString() ?? "",
                        Prn            = r["prn"]?.ToString() ?? "",
                        FiledAt        = r["filed_at"]?.ToString() ?? "",
                        TxtFilePath    = r["txt_file_path"]?.ToString() ?? "",
                        FvuFilePath    = r["fvu_file_path"]?.ToString() ?? "",
                        Remarks        = r["remarks"]?.ToString() ?? "",
                    });
                }
            }
            catch { }
            return list;
        }

    }

    public class FilingHistoryRecord
    {
        public int    Id             { get; set; }
        public int    DeductorId     { get; set; }
        public string FormType       { get; set; } = "";
        public string FinancialYear  { get; set; } = "";
        public string Quarter        { get; set; } = "";
        public bool   IsCorrection   { get; set; }
        public string CorrectionType { get; set; } = "";
        public string Prn            { get; set; } = "";
        public string FiledAt        { get; set; } = "";
        public string TxtFilePath    { get; set; } = "";
        public string FvuFilePath    { get; set; } = "";
        public string Remarks        { get; set; } = "";

        public string Label => IsCorrection
            ? $"{CorrectionType} filed {FiledAt[..10]} — PRN: {Prn}"
            : $"Original filed {FiledAt[..10]} — PRN: {Prn}";
    }
}
