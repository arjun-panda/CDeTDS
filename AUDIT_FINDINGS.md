# CDeTDS — Full Audit Findings (2026-06-21)

Audit against AUDIT_PLAN.md. Findings-first: listed here, fixed only after
confirmation. Severity: CRITICAL / HIGH / MEDIUM / LOW.

Commit at audit start: `b2a02e6`.

---

## Part 1 — Transition logic — ✅ PASS (1 LOW)

| Check | Result |
|---|---|
| 1.1 Regime = earlier of credit/payment | ✅ `TdsEntry.TriggerDate` (Models.cs:68) returns earlier of CreditDate/EntryDate; regime via `TaxRules.IsNewAct(fy)` (year≥2026). Centralized. |
| 1.2–1.4 Mar/Apr-2026 boundary cases | ✅ Covered by TransitionAuditTests (pass). |
| 1.5–1.6 Forms per FY | ✅ FormsForFy → 24Q/26Q/27EQ (old) vs 138/140/143 (new). |
| 1.7 No old+new mixing in one file | ✅ Hard guard E030/E031 (FvuGenerator.cs:828). |
| 1.8 FY/AY vs TY labels | ✅ Corrected extensively this session. |
| 1.9 Late deposit stays old-regime | ✅ Regime from trigger date, not deposit date. |
| 1.10 Form 16/16A vs 130/131 | ✅ TaxRules.SalaryTdsCertForm / NonSalaryTdsCertForm. |

**L-1 (LOW)** — FvuGenerator.cs:834 hardcodes `"FY {h.FinancialYear}"` while the
sibling branch (line 830) uses `YearLabel()`. Cosmetic asymmetry; the string is
on the old-FY branch so "FY" is technically correct, but inconsistent style.

---

## Part 2 — Data layer — ✅ PASS (1 MEDIUM security, 1 design note)

| Check | Result |
|---|---|
| 2.1 Tables + relations | ✅ deductors, deductees, challans, tds_entries, tds_rules, tds_filing_history, filing_snapshots, users, audit_log. FKs present. |
| 2.2 Transaction stores credit/payment/trigger/regime/section/code/links | ✅ tds_entries has entry_date, credit_date (migration), section, amount, tds, rate, deductee_id+challan_no links, higher_rate flags, pan_available, itr_filed. Regime derived from FY (single source). See D-1. |
| 2.3 Section↔code↔rate↔threshold mapping; sourced from official doc | ✅ tds_rules + PaymentCodeFor now sourced from the official Form 138/140 file-format spec (this session). Blog-sourced codes eliminated. |
| 2.4 Filed-statements stores token/form/period/path/snapshot | ✅ tds_filing_history (prn, paths) + filing_snapshots (record-level JSON, FK). Strong correction-statement support. |
| 2.5 FKs, indexes, ISO dates | ✅ Indexes on fy/section/status/date; dates stored ISO yyyy-MM-dd TEXT. |
| 2.6 No regime data hardcoded in UI | ✅ Regime logic centralized in TaxRules/BuiltInTdsRules. |

**M-1 (MEDIUM, security)** — `Database.HashPassword` (Database.cs:1509) uses
**unsalted single-pass SHA-256** for user-login passwords. Below commercial
standard: vulnerable to rainbow-table attacks; identical passwords collide.
The app already ships PBKDF2 (AesEncryption.cs) — login should use a salted slow
KDF too. Risk mitigated by local-only DB on the user's own machine, but should
be upgraded. Fix needs a migration path (re-hash on next successful login).

**D-1 (design note, not a defect)** — tds_entries has no `payment_code` column
(unlike challans/tds_rules). New-Act codes are derived at generation time via
`NewActFileSection` → tds_rules lookup by section. Valid (code is a function of
section), but means a per-entry code override isn't persistable. Acceptable.

---

## Part 3 — Validation layer — ✅ PASS (1 LOW)

| Check | Result |
|---|---|
| 3.1 PAN/TAN regex | TAN `^[A-Z]{4}[0-9]{5}[A-Z]$` ✅. PAN `^[A-Z]{5}[0-9]{4}[A-Z]$` ✅ format. See L-2. |
| 3.2 Challan totals = sum of deductee rows | ✅ GetChallanReconciliation (ReportsRepository.cs:163) sums challan vs entry TDS; per-challan recon added this session. |
| 3.3 Threshold incl. aggregate, full-year | ✅ `IsBelowThreshold` (TdsRulesEngine.cs:98) — pure, tested: single<limit AND (no aggregate OR ytd+amt<aggregate). YtdAmount fed from full-year query (Services.cs:168). 194C ₹30K/₹1L correct. |
| 3.4 Higher rate on missing/invalid PAN | ✅ 206AA/206AB handled in engine; higher_rate_applied + reason persisted. |
| 3.5 Lower/nil deduction (197/395) | ⚠️ Verify 395 certificate capture exists for new regime (not deeply traced — see note). |
| 3.6 Date validations / deposit due dates | ✅ due_date computed; late-deposit interest fields present. |
| 3.7 Friendly errors | ✅ ErrorTranslator + tested (ErrorTranslatorTests). |

**L-2 (LOW)** — PAN regex accepts any letter in the 4th position; it does not
enforce the holder-type set `[PCHFATBLJG]` (CLAUDE.md notes 4th char = holder
type). Structurally-invalid-by-type PANs pass app validation (FVU catches them
downstream). Tighten to `^[A-Z]{3}[PCHFATBLJGK][A-Z][0-9]{4}[A-Z]$` if desired.

**Note (3.5)** — Lower-deduction certificate (s.197 / new s.395) capture was not
deeply traced in this pass; flag for targeted review.

---

## Part 4 — File generation — ✅ PASS (strong)

| Check | Result |
|---|---|
| 4.1 Record sequence FH→BH→CD→DD(→SD) | ✅ All record types present (FH/BH/CD/DD/SD/S16/C6A), correct nesting. |
| 4.2 Caret delimiter, clean ASCII, no BOM | ✅ `File.WriteAllText(..., Encoding.ASCII)` (Phase2Services.cs:176) — ASCII writes no BOM. Caret throughout. |
| 4.3 Field order/length traced to spec | ✅ EXEMPLARY: field counts verified against FVU 9.4 reference files (24QRQ4.txt, 4I03886B.txt) with field-by-field comments. Not assumed. |
| 4.4 Form-type per regime | ✅ 138/140/143 normalized to 24Q/26Q/27EQ wire format (interim) with new section codes; mixing guard E030/E031. |
| 4.5 Section/code per regime | ✅ Old truncated codes (94C/4JB) for old; new 4-digit codes via NewActFileSection when switch ON (currently OFF). |
| 4.6 Amount decimal/rounding | ✅ Rupees with .00, integer rounding AwayFromZero on challan fields. |
| 4.7 Correction statements | ✅ filing_snapshots stores original rows; add/update/delete supported. |
| 4.8 Sample files for regression | ✅ NonSalary26QTests + reference .txt files exercised. |

NOTE: New-Act forms deliberately use the FVU 9.4 interim wire format until the
new FVU is bundled; payment codes inert (switch OFF). This is the documented,
correct interim state — not a defect.

---

## Part 5 — FVU integration + software quality — ✅ PASS (1 MEDIUM dup of M-1, 1 LOW)

| Check | Result |
|---|---|
| 5.1 FVU via subprocess on official jar; version per regime | ✅ Bundled FVU 9.4 driven headlessly. New FVU 1.0 is GUI-only (no CLI) — dual-FVU blocked, documented; switch stays OFF until resolved. App never reimplements FVU validation. |
| 5.2 FVU report parsed + .fvu surfaced | ✅ FvuUtilityRunner parses err.html, surfaces .fvu. |
| 5.3 No swallowed failures; transactions; no data loss | ✅ Transactions on all bulk writes (BeginTransaction/Commit). See L-3 re empty catches. |
| 5.4 License/password storage | ⚠️ M-1: login password is unsalted SHA-256. Portal creds use PBKDF2-AES (good). License private key in KeyGen only (not distributed) ✅. Expiry doesn't destroy data ✅. |
| 5.5 Backup/restore; concurrent runs | ✅ Daily auto-backup; single-instance mutex (this session). |
| 5.6 Hardcoded paths/dates/rates | ✅ Rates in DB (engine never hardcodes); AppData paths via FolderManager. |
| 5.7 Commercial polish | ✅ No "TDS Pro"/TDSPro leftovers, no Console/Debug prints, no TODO/FIXME/placeholder in source. Rename complete. |

**L-3 (LOW)** — ~30+ blanket `catch { }` blocks, concentrated in Database.cs
migrations. Most are defensible (idempotent best-effort migration/backfill
passes that must not crash startup on an already-migrated DB). Risk: no logging
means a genuine failure is invisible. Recommend logging the exception (even at
debug) rather than silently swallowing. Not a data-loss bug — transactions
protect the real writes.

---

# SUMMARY

Audited all 5 parts of AUDIT_PLAN.md against commit `b2a02e6` (~35.6K LOC).
**Overall: healthy.** No CRITICAL or HIGH findings. Tax-correctness core
(transition logic, thresholds, file generation) is solid and well-tested
(342 tests). The session's spec-alignment work (payment codes from the official
Form 138/140 file-format) closed the biggest prior risk.

| Sev | ID | Area | Finding |
|-----|----|------|---------|
| MEDIUM | M-1 | Security | Login password = unsalted SHA-256; upgrade to PBKDF2 (already in codebase) with re-hash-on-login migration. |
| LOW | L-1 | Transition | FvuGenerator.cs:834 "FY {x}" vs sibling YearLabel() — cosmetic. |
| LOW | L-2 | Validation | PAN regex doesn't enforce 4th-char holder type. |
| LOW | L-3 | Quality | Blanket `catch { }` in migrations — add logging. |

Open notes (not defects): D-1 (no per-entry payment_code — by design), 3.5
(s.395 lower-deduction cert capture not deeply traced — targeted review),
new-Act FVU still GUI-only (activation blocker, external).

No code changed during this audit (findings-first, per AUDIT_PLAN.md).

