# AUDIT_PLAN.md — CDeTDS Full Codebase Audit

Audit the entire CDeTDS codebase against this plan, in order. Read CLAUDE.md
first — it contains the domain rules every check below depends on.

For each finding, report: file + line, severity (CRITICAL / HIGH / MEDIUM /
LOW), what is wrong, why it matters under the Act, and the exact fix. Do not
silently fix things — list findings first, then fix only after confirmation.

---

## Part 1 — Transition logic (HIGHEST PRIORITY)

The app must handle the 1961 Act → 2025 Act transition perfectly.

- [ ] 1.1 Locate every place the code decides "old regime vs new regime".
      Verify it uses the EARLIER of credit date or payment date — not filing
      date, not quarter, not deposit date, not today's date.
- [ ] 1.2 Test case: credit 15-Mar-2026, payment 10-Apr-2026 → must resolve to
      OLD Act / section 194-series / Form 26Q stream.
- [ ] 1.3 Test case: advance payment 20-Mar-2026, credit 05-Apr-2026 → OLD Act.
- [ ] 1.4 Test case: credit and payment both 02-Apr-2026 → NEW Act / payment
      code / Form 140 stream.
- [ ] 1.5 Verify Q4 FY 2025-26 statement generation produces old forms
      (24Q/26Q/27Q/27EQ) with old section codes, regardless of generation date.
- [ ] 1.6 Verify Q1 TY 2026-27 onwards produces new forms (138/140/144/143)
      with numeric payment codes only.
- [ ] 1.7 Verify no code path can put old section codes and new payment codes
      into the same statement file.
- [ ] 1.8 Verify year labels: FY/AY used for old-regime outputs, Tax Year (TY)
      for new-regime outputs (UI, reports, file fields, certificates).
- [ ] 1.9 Late-deposit scenario: deduction Mar-2026 deposited May-2026 — confirm
      it stays in the old-regime stream and reconciliation accepts old codes
      with post-April dates.
- [ ] 1.10 Certificates: Form 16/16A for old regime, Form 130/131 for new.

## Part 2 — Data layer (SQLite schema)

- [ ] 2.1 Tables exist and are correctly related: deductor master (TAN, PAN,
      deductor type, responsible person), deductee master (PAN, name,
      category/status), challans, deduction transactions, filed statements.
- [ ] 2.2 Each deduction transaction stores: credit date, payment date, derived
      trigger date, regime flag, legacy section AND/OR payment code as
      applicable, amount, TDS, rate, deductee link, challan link.
- [ ] 2.3 Mapping table exists: legacy section ↔ Section 392/393/394 table
      entry ↔ numeric payment code ↔ rate ↔ threshold(s) ↔ description. Check
      completeness against the official list (flag any codes sourced from
      blogs rather than the Protean data structure document).
- [ ] 2.4 Filed-statements table stores token/receipt number, form type,
      period, regime, file path/copy, and a record-level snapshot — sufficient
      to generate correction statements later.
- [ ] 2.5 Foreign keys, indexes, and date columns stored in an unambiguous
      format (ISO yyyy-mm-dd in DB even if UI shows dd/mm/yyyy).
- [ ] 2.6 No regime-specific data hard-coded in UI code that belongs in the DB.

## Part 3 — Validation layer (pre-FVU)

- [ ] 3.1 PAN regex: ^[A-Z]{3}[PCHFATBLJG][A-Z][0-9]{4}[A-Z]$ (or equivalent);
      TAN regex: ^[A-Z]{4}[0-9]{5}[A-Z]$.
- [ ] 3.2 Challan totals = sum of linked deductee rows (TDS, surcharge, cess,
      interest, fee separately where applicable).
- [ ] 3.3 Threshold logic per payment type, including aggregate thresholds
      (e.g. contractor ₹30,000 single / ₹1,00,000 aggregate) — confirm
      aggregate tracking works across the full year, not per quarter.
- [ ] 3.4 Higher-rate deduction applied when PAN absent/invalid; correct
      "reason for higher deduction" flag written to the file.
- [ ] 3.5 Lower/nil deduction handling: certificate number capture (old 197 /
      new 395), reason flags.
- [ ] 3.6 Date validations: deduction date within quarter; deposit due-date
      checks (7th of following month; 30 April for March deductions,
      non-government); warn on late deposit with interest implications.
- [ ] 3.7 Friendly error messages — a novice end-user must understand them
      without knowing FVU error codes.

## Part 4 — File generation (.txt)

- [ ] 4.1 Record sequence and structure: FH → BH → CD → DD (→ SD for salary
      Q4). One BH per statement, CD lines each followed by their DD lines.
- [ ] 4.2 Delimiter is caret ^, output is clean ASCII, .txt extension, no BOM,
      correct line endings.
- [ ] 4.3 Field order, lengths, mandatory/optional status traced to the
      OFFICIAL Protean data structure document — flag every field whose layout
      was assumed rather than verified. List them for manual verification.
- [ ] 4.4 Form-type field carries 24Q/26Q/27Q/27EQ (old) vs 138/140/144/143
      (new) correctly per regime.
- [ ] 4.5 Section/payment-code field format per regime (e.g. old files often
      use truncated codes like "94C" — verify against spec; new files use
      4-digit payment codes).
- [ ] 4.6 Amount fields: correct decimal handling, no negative values where
      disallowed, rounding rules consistent with the spec.
- [ ] 4.7 Correction statement generation: add/update/delete record flags,
      original token number referenced, only changed records included as the
      spec requires.
- [ ] 4.8 Generated sample files for each form type exist and are stored for
      regression testing.

## Part 5 — FVU integration & software quality

- [ ] 5.1 FVU invoked via subprocess on the bundled official .jar; app never
      re-implements validation that FVU owns. Correct FVU version selected per
      regime (legacy FVU for FY ≤ 2025-26; new utility for TY 2026-27+).
      Confirm the new-regime FVU is the FINAL release, not the beta.
- [ ] 5.2 FVU error report parsed and shown to the user readably; .fvu output
      file saved and surfaced for portal upload.
- [ ] 5.3 Error handling: no bare except blocks swallowing failures; DB writes
      wrapped in transactions; no data loss on crash mid-generation.
- [ ] 5.4 License/login module: does not block access to the user's own data
      on expiry in a way that destroys data; password storage not plaintext.
- [ ] 5.5 Backup/restore of the SQLite DB; safe handling of concurrent runs.
- [ ] 5.6 Hard-coded paths, dates, or rates that should be configuration.
- [ ] 5.7 Anything that would embarrass a commercial product: debug prints,
      placeholder text, dead code, inconsistent naming (any leftover "TDS Pro"
      strings should read "CDeTDS").

---

## How to run this audit

1. Read CLAUDE.md.
2. Map the codebase: list every module and its responsibility.
3. Work Parts 1→5 in order. Part 1 findings gate everything else.
4. Produce a findings report grouped by severity before changing any code.
5. After fixes are approved and applied, write/execute test scripts for the
   Part 1 scenarios (1.2, 1.3, 1.4, 1.9) and the challan-total check (3.2),
   and run a sample generation per form type.

## Out of scope for code audit (manual steps for the developer)

- Download the FINAL data structure document + RPU/FVU for TY 2026-27 from
  protean-tinpan.com and verify Part 4 field layouts against it.
- Run real generated files through the actual FVU — that is the ground truth.
- Verify the complete payment-code master list against the official document.
