using System;
using System.Collections.Generic;

namespace CDeTDS.DAL
{
    // ══════════════════════════════════════════════════════════════════════════
    // AUTHORITATIVE TDS RULES — SEALED, COMPILED INTO APP
    //
    // Source: Income Tax Act 2025 (Section 392, 393), IT Act 1961 (Section 192-206),
    //         Finance Act 2025, CBDT Circular 2026.
    //
    // These rules are AUTO-SEEDED into the DB on startup.
    // Standard sections CANNOT be manually edited — only custom sections can.
    // Updates come with app releases — not from user edits.
    //
    // Version format: FY-YYYYMMDD  e.g. "2026-27-20260401"
    // ══════════════════════════════════════════════════════════════════════════

    public record BuiltInRule(
        string SectionCode,
        string NatureOfPayment,
        string DeducteeType,       // "All" | "Individual" | "Company" | "HUF"
        bool   Resident,
        double ThresholdRs,        // 0 = no threshold
        double RatePercent,        // 0 = slab (salary)
        double SurchargePercent,
        double CessPercent,
        string EffectiveFrom,
        string EffectiveTo,        // "" = active
        string RulesVersion,       // version that introduced this rule
        // IT Act 2025 4-digit payment code — fill from the OFFICIAL Protean data
        // structure document when the final new-act FVU/RPU ships; "" until then.
        string PaymentCode = ""
    )
    {
        // New Act section reference auto-derived — never user-editable
        public string ReferenceAct => SectionCode switch {
            "192"    => "Section 392(1) — IT Act 2025",
            "192A"   => "Section 393(1) Sl.4(i) — IT Act 2025",
            "193"    => "Section 393(1) Sl.1 — IT Act 2025",
            "194"    => "Section 393(1) Sl.2 — IT Act 2025",
            "194A"   => "Section 393(1) Sl.3 — IT Act 2025",
            "194B"   => "Section 393(1) Sl.5(i) — IT Act 2025",
            "194BA"  => "Section 393(1) Sl.5(iv) — IT Act 2025",
            "194BB"  => "Section 393(1) Sl.5(ii) — IT Act 2025",
            "194C"   => "Section 393(1) Sl.6 — IT Act 2025",
            "194D"   => "Section 393(1) Sl.7 — IT Act 2025",
            "194DA"  => "Section 393(1) Sl.8(i) — IT Act 2025",
            "194G"   => "Section 393(1) Sl.9 — IT Act 2025",
            "194H"   => "Section 393(1) Sl.10(i) — IT Act 2025",
            "194I"   => "Section 393(1) Sl.11 — IT Act 2025",
            "194IA"  => "Section 393(1) Sl.12(i) — IT Act 2025",
            "194IB"  => "Section 393(1) Sl.12(ii) — IT Act 2025",
            "194IC"  => "Section 393(1) Sl.12(iii) — IT Act 2025",
            "194J"   => "Section 393(1) Sl.13 — IT Act 2025",
            "194K"   => "Section 393(1) Sl.8(ii) — IT Act 2025",
            "194LA"  => "Section 393(1) Sl.14 — IT Act 2025",
            "194M"   => "Section 393(1) Sl.6(ii) — IT Act 2025",
            "194N"   => "Section 393(1) Sl.15 — IT Act 2025",
            "194O"   => "Section 393(1) Sl.16 — IT Act 2025",
            "194Q"   => "Section 393(1) Sl.17 — IT Act 2025",   // 194Q continues; FA 2025 only removed the 206C(1H) cross-reference
            "194R"   => "Section 393(1) Sl.18 — IT Act 2025",
            "194S"   => "Section 393(1) Sl.8(vi) — IT Act 2025",
            "195"    => "Section 393(2) — IT Act 2025",
            "206AB"  => "Section 397(3) — IT Act 2025 (Removed w.e.f. 1-Apr-2025)",
            // No official Section 393 Sl. mapping known for this code — cite the
            // legacy section honestly rather than inventing a table entry.
            _        => $"IT Act 2025 (legacy s.{SectionCode})"
        };
        public bool IsStandard => BuiltInTdsRules.StandardSections.Contains(SectionCode);
    }

    public static class BuiltInTdsRules
    {
        // ── Version: bump this when rates change ─────────────────────────────
        // 20260612: corrected ReferenceAct strings (194Q "removed" mislabel; honest
        //           fallback for unmapped codes) — bump forces re-apply on startup
        // 20260613: IT Act 2025 payment codes staged from Protean RPU v1.0 (BETA,
        //           TY 2026-27 onwards) — see FVU_NEWACT_1.0\PaymentCodes_RPU1.0.md.
        //           Codes are inert until FVU_USE_PAYMENT_CODES is enabled, which must
        //           wait for the FINAL RPU/FVU release + data structure document.
        public const string CurrentVersion = "2026-27-20260613";

        /// <summary>
        /// IT Act 2025 payment code for a rule row, extracted from Protean RPU v1.0
        /// (BETA — re-verify against the final release before enabling). Returns ""
        /// where the mapping is ambiguous; those stay blank until confirmed against
        /// the official data structure document (see PaymentCodes_RPU1.0.md).
        /// </summary>
        public static string PaymentCodeFor(string section, string nature, string deducteeType)
        {
            var s  = (section ?? "").ToUpperInvariant();
            var n  = (nature ?? "");
            var dt = (deducteeType ?? "");
            return s switch
            {
                "192"   => "1002",   // non-Government employees (1001/1003 for Govt categories)
                "192A"  => "1004",
                "193"   => "1019",
                "194"   => "1029",
                "194A"  => n.Contains("Sr", StringComparison.OrdinalIgnoreCase) ||
                           n.Contains("senior", StringComparison.OrdinalIgnoreCase) ? "1020" : "1021",
                "194B"  => "1058",
                "194BA" => "1060",
                "194BB" => "1062",
                "194C"  => dt is "Individual" or "HUF" ? "1023" : "1024",
                "194D"  => "1005",
                "194DA" => "1030",
                "194EE" => "1066",
                "194G"  => "1063",
                "194H"  => "1006",
                "194I"  => n.Contains("Machinery", StringComparison.OrdinalIgnoreCase) ||
                           n.Contains("Plant", StringComparison.OrdinalIgnoreCase) ? "1008" : "1009",
                "194IC" => "1011",
                "194LA" => "1012",
                "194N"  => "1065",   // person other than co-operative society (1064 = co-op)
                "194O"  => "1035",
                "194P"  => "1032",
                "194Q"  => "1031",
                "194R"  => "1033",
                "194T"  => "1067",
                // Ambiguous pending official data-structure doc: 194J (1025/1026/1027),
                // 194K ("94K" oddity), 194S (1037/1038), 195/196x (Form 144), TCS (Form 143)
                _       => "",
            };
        }

        public static readonly HashSet<string> StandardSections = new(StringComparer.OrdinalIgnoreCase)
        {
            "192","192A","193","194","194A","194B","194BA","194BB","194C","194D",
            "194DA","194G","194H","194I","194IA","194IB","194IC","194J","194K",
            "194LA","194M","194N","194O","194P","194Q","194R","194S","195","206AA","206AB","206CCA"
            // Note: 194Q and 206AB are kept in StandardSections so their historical records are protected from user edit
        };

        // ── Authoritative rules — FY 2026-27 (IT Act 2025) ───────────────────
        // Source: Finance Act 2025, CBDT, IT Act 2025 Section 393(1) Table
        public static readonly BuiltInRule[] Rules = new[]
        {
            // ── CESS APPLICABILITY RULE (Source: Finance Act 2009, CBDT Circular 3/2025) ──────────
            // Cess = 4  → Section 192 (salary) only: employer computes full tax liability incl. cess
            // Cess = 4  → Non-resident sections (195, 196A-D, 194E, 194LB-LD): TDS is final tax
            // Cess = 0  → All resident non-salary sections (192A, 193, 194 through 194S, 206AB):
            //             Deductee pays cess when filing their own ITR; deductor uses flat rate only.
            // ─────────────────────────────────────────────────────────────────────────────────────

            // SALARY — Slab rates, rate=0 means compute via TaxRules engine; CESS=4 (full liability)
            new BuiltInRule("192",   "Salary",                          "Individual", true,       0,   0.00, 0, 4, "2026-04-01", "", CurrentVersion),

            // PF WITHDRAWAL — resident; cess=0
            new BuiltInRule("192A",  "PF Withdrawal",                   "Individual", true,   50000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // INTEREST ON SECURITIES — resident; cess=0
            new BuiltInRule("193",   "Interest on Securities",          "Company",    true,    5000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("193",   "Interest on Securities",          "Individual", true,   10000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // DIVIDENDS — resident; cess=0; Finance Act 2025 increased threshold from ₹5,000 → ₹10,000 w.e.f. 1-Apr-2025
            new BuiltInRule("194",   "Dividends",                       "All",        true,   10000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // INTEREST OTHER THAN SECURITIES — resident; cess=0
            // Threshold: ₹40,000 general; ₹50,000 for banks/co-op/post office (Sec 194A proviso)
            // Finance Act 2025: Senior citizen threshold raised from ₹50,000 → ₹1,00,000 w.e.f. 1-Apr-2025
            new BuiltInRule("194A",  "Interest other than securities",  "Company",    true,   40000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194A",  "Interest other than securities",  "Individual", true,   40000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194A",  "Interest — Bank/Co-op/Post (Sr.Citizen)", "Individual", true, 100000, 10.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // LOTTERY / CROSSWORD — resident; cess=0
            new BuiltInRule("194B",  "Lottery Winnings",                "All",        true,   10000,  30.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194BA", "Online Games",                    "All",        true,       0,  30.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194BB", "Winnings from Horse Race",        "All",        true,   10000,  30.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // CONTRACTOR — resident; cess=0
            new BuiltInRule("194C",  "Payment to Contractor",           "Individual", true,   30000,   1.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194C",  "Payment to Contractor",           "HUF",        true,   30000,   1.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194C",  "Payment to Contractor",           "Company",    true,   30000,   2.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // INSURANCE COMMISSION — resident; cess=0
            new BuiltInRule("194D",  "Insurance Commission",            "Individual", true,   15000,   5.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194D",  "Insurance Commission",            "Company",    true,   15000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194DA", "Life Insurance Maturity",         "All",        true,  100000,   5.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // LOTTERY AGENT / COMMISSION — resident; cess=0
            new BuiltInRule("194G",  "Commission on Lottery",           "All",        true,   15000,   5.00, 0, 0, "2026-04-01", "", CurrentVersion),
            // 194H: Finance Act 2025 reduced rate 5% → 2% AND raised threshold ₹15,000 → ₹20,000 w.e.f. 1-Apr-2025
            // Ref: Finance Act 2025, Section 49(a); IT Act 2025 Section 393(1) Sl.10(i)
            new BuiltInRule("194H",  "Commission / Brokerage",          "All",        true,   20000,   2.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // RENT — resident; cess=0
            // Finance Act 2025: threshold changed to ₹50,000 PER MONTH (or part thereof) w.e.f. 1-Apr-2025
            // Stored as 50000 (monthly). TDS triggered when single month credit/payment exceeds ₹50,000.
            // Ref: Finance Act 2025, Section 50; IT Act 2025 Section 393(1) Sl.11
            new BuiltInRule("194I",  "Rent - Plant & Machinery",        "All",        true,   50000,   2.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194I",  "Rent - Land/Building/Furniture",  "All",        true,   50000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194IA", "Transfer of Immovable Property",  "All",        true, 5000000,   1.00, 0, 0, "2026-04-01", "", CurrentVersion),
            // 194IB: Finance Act 2023 reduced rate from 5% → 2% w.e.f. FY 2023-24; cess=0
            new BuiltInRule("194IB", "Rent by Individual/HUF",          "Individual", true,   50000,   2.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194IC", "Joint Dev Agreement",             "All",        true,       0,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // PROFESSIONAL / TECHNICAL FEES — resident; cess=0
            // Finance Act 2025 increased 194J threshold from ₹30,000 → ₹50,000 w.e.f. 1-Apr-2025
            new BuiltInRule("194J",  "Professional Fees",               "All",        true,   50000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194J",  "Technical Services / Royalty",    "All",        true,   50000,   2.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // OTHER — resident; cess=0
            new BuiltInRule("194K",  "Income from Mutual Fund Units",   "All",        true,    5000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194LA", "Compensation (Compulsory Acq.)",  "All",        true,  250000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194M",  "Contractor (Individual/HUF)",     "Individual", true, 5000000,   2.00, 0, 0, "2026-04-01", "", CurrentVersion),
            // 194N: Two-tier rates — ₹1Cr @ 2% for return-filers; for non-filers: ₹20L @ 2%, ₹1Cr @ 5%; cess=0
            new BuiltInRule("194N",  "Cash Withdrawal (Return-filer)",  "All",        true, 10000000,  2.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194N",  "Cash Withdrawal (Non-filer ≤1Cr)","All",        true,   200000,  2.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194N",  "Cash Withdrawal (Non-filer >1Cr)","All",        true, 10000000,  5.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194O",  "E-commerce Payments",             "All",        true,       0,   1.00, 0, 0, "2026-04-01", "", CurrentVersion),
            // 194Q: REMOVED by Finance Act 2025 w.e.f. 1-Apr-2025 — no TDS on purchase of goods from FY 2025-26 onwards
            new BuiltInRule("194Q",  "Purchase of Goods (Removed — Finance Act 2025)", "All", true, 5000000, 0.10, 0, 0, "2021-07-01", "2025-03-31", "2025-26-20250401"),
            new BuiltInRule("194R",  "Benefit/Perquisite to Business",  "All",        true,   20000,  10.00, 0, 0, "2026-04-01", "", CurrentVersion),
            new BuiltInRule("194S",  "Virtual Digital Assets (VDA)",    "All",        true,   10000,   1.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // 194P — Tax deduction for specified senior citizens (75+) by specified bank
            // Introduced Finance Act 2021; bank computes full tax liability (salary + interest); cess=4
            // No separate TDS return by deductee — bank files Form 26QAA; rate = slab (rate=0 → engine)
            // Ref: Section 393(1) Sl.19 — IT Act 2025
            new BuiltInRule("194P",  "Senior Citizen (75+) — Specified Bank", "Individual", true, 0, 0.00, 0, 4, "2021-04-01", "", CurrentVersion),

            // NON-RESIDENTS — TDS is often final tax; cess=4 applies at source
            new BuiltInRule("195",   "Payments to Non-Residents",       "All",        false,      0,  20.00, 0, 4, "2026-04-01", "", CurrentVersion),

            // 206AA — Higher TDS when PAN not available; rate = Max(applicable rate, 20%)
            // Not a deductible section by itself — used as override flag; stored for reference/display
            // Ref: Section 397(1) — IT Act 2025
            new BuiltInRule("206AA", "Higher TDS — PAN Not Available",  "All",        true,       0,  20.00, 0, 0, "2026-04-01", "", CurrentVersion),

            // 206AB: REMOVED by Finance Act 2025 w.e.f. 1-Apr-2025 — section abolished
            new BuiltInRule("206AB", "Higher Rate — ITR not filed (Removed — Finance Act 2025)", "All", true, 0, 20.00, 0, 0, "2021-07-01", "2025-03-31", "2025-26-20250401"),
        };
    }
}
