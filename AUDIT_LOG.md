# TDSPro Accuracy Audit Log
Generated: 2026-05-23

## METHODOLOGY
Every file read in full. Every number cross-checked against:
- TaxRules.cs (master rule table)
- DB schema (Database.cs)
- UI labels (Payroll.razor)
- Tests (SalaryComputationTests.cs)
- FvuGenerator.cs (NSDL codes)

---

## CONFIRMED BUGS (to fix)

### BUG-1 [CRITICAL] HRA rent unit mismatch — PayrollService.cs:169
**File:** TDSPro.BLL/PayrollService.cs, line 169
**Bug:** `RentPaid` in `tax_declarations` is ANNUAL (UI label: "Annual Rent Paid ₹").
  `CalcHraExemption` takes MONTHLY rent. Passing annual directly → HRA exemption 12× inflated.
**Evidence:** Payroll.razor:1818 label = "Annual Rent Paid (₹)". Line 133 annualizes it as `decl.RentPaid * 12` (confirms it's already annual). IncomeComputationGenerator:92 correctly does `rent/12`.
**Fix:** `CalcHraExemption(ss.Basic, ss.Hra, decl.RentPaid / 12, hraCityType)` 

### BUG-2 [CRITICAL] HRA rent unit mismatch — SalaryService.cs:287
**File:** TDSPro.BLL/SalaryService.cs, line 287
**Same bug as BUG-1 in the ComputeAnnual path.**
**Fix:** divide `decl.RentPaid` by 12 before passing.

### BUG-3 [MEDIUM] Hardcoded stdDed fallback 75000 in ReportsRepository.cs:437,451
**File:** TDSPro.DAL/Repositories/ReportsRepository.cs, lines 437 and 451
**Bug:** `double stdDed = 75000` hardcoded as default when `payroll_runs` is empty.
  For old-regime employees this default is WRONG (should be 50,000).
  For new-regime FY 2023-24 it is also WRONG (should be 50,000 not 75,000).
**Fix:** Derive from TaxRules dynamically. Load `isNewRegime` from `regime`, call `TaxRules.GetRules(fy, isNew).StandardDeduction`.

### BUG-4 [MEDIUM] MSE path old-regime taxable subtracts HRA twice — ReportsRepository.cs:514
**File:** TDSPro.DAL/Repositories/ReportsRepository.cs, line 514
**Bug:** Old-regime taxable = `mseTaxable - stdDed - hra - annualPtFromRuns - ch6a`
  But `mseTaxable = SUM(gross_taxable)` from monthly_salary_entries. The field `gross_taxable` 
  is the taxable gross AFTER bills reimbursements are stripped — it does NOT include HRA in gross.
  HRA exemption is stored separately in `perq_exempted`. So subtracting `hra` here double-counts.
**Evidence:** Comment at line 497 says "mseTaxable = SUM(gross_taxable_salary) which already has bills-reimbursement exemptions stripped" but the schema has HRA in gross_taxable vs perq_exempted needs verification.
**ACTION:** Verify monthly_salary_entries schema before fixing (may be fine if gross_taxable includes HRA in gross).

### BUG-5 [LOW] Dead code — PayrollService.cs:442-467
**File:** TDSPro.BLL/PayrollService.cs, lines 442-467
**Bug:** `ComputeTaxOldRegime` and `ComputeTaxNewRegime` are private methods that are NEVER called.
  They contain hardcoded slabs for only one FY. If ever called they would give wrong results for
  any FY other than 2025-26. Dead code that will mislead future maintainers.
**Fix:** Delete both methods.

### BUG-6 [LOW] isNewRegime fallback condition includes "O" — ReportsRepository.cs:461
**File:** TDSPro.DAL/Repositories/ReportsRepository.cs, line 461
**Status:** NOT actually a bug. regime from DB is always "New"/"Old". The `|| regime.Equals("O", ...)` 
  is dead code that never executes (DB never stores "O"). Harmless but misleading.
**Fix:** Remove the dead `|| regime.Equals("O", ...)` clause for clarity.

---

## VERIFIED CORRECT (do NOT change)

- TaxRules.cs slabs, 87A, surcharge, cess — fully verified, correct for all FYs
- Form16Generator.cs chapter6a MAX (fixed prior session) — correct
- Form16Generator.cs TdsDeducted from tds_entries �� correct
- Form16Generator.cs quarter TDS from tds_entries — correct
- ReportsRepository.cs TotalTaxPayable (fixed prior session) — correct  
- ReportsRepository.cs chapter6a MAX (fixed prior session) — correct
- PayrollService.cs 80C includes PF, capped 150000 — correct (Income Tax Act s.80C)
- PayrollService.cs 80CCD(1B) cap 50000 — correct (s.80CCD(1B))
- PayrollService.cs 80CCD(2) FY-aware rate via TaxRules.Get80CCD2Rate — correct
- PayrollService.cs 80D limits 25000/50000 age-based — correct
- PayrollService.cs 80EEA 150000 cap — correct (s.80EEA)
- PayrollService.cs 80DD/80U 125000 cap — correct (s.80DD/80U)
- PayrollService.cs marginal relief 87A via TaxRules.Apply87A — correct
- PayrollService.cs surcharge via TaxRules.CalcSurcharge — correct
- PayrollService.cs cess 4% on (tax+surcharge) — correct (s.4A)
- PayrollService.cs pro-rata 30-day method — correct
- PayrollService.cs remainMonths min 1 guard (line 487) — correct
- IncomeComputationGenerator.cs rent/12 — correct (fixed prior session)
- Form16A SQL excludes 192/392 — correct
- DeducteeReport SQL excludes 192/392 — correct
- NSDL regime code "O"=New, "N"=Old in FvuGenerator — correct
- ReportsRepository nsdlRegime New→"O", Old→"N" — correct

---

## HARDCODED NUMBERS AUDIT (cross-referenced against TaxRules.cs)

| Number | Location | Correct? | Reason |
|--------|----------|----------|--------|
| 75000 | ReportsRepository.cs:437,451 | WRONG | Should come from TaxRules dynamically (regime+FY dependent) |
| 150000 | PayrollService.cs:189 | CORRECT | 80C cap — statutory, unchanged since 2014 |
| 50000 | PayrollService.cs:193 | CORRECT | 80CCD(1B) cap — statutory |
| 50000/25000 | PayrollService.cs:174-175 | CORRECT | 80D limits — age-based statutory |
| 150000 | PayrollService.cs:195 | CORRECT | 80EEA cap — statutory |
| 125000 | PayrollService.cs:197-198 | CORRECT | 80DD/80U max — statutory |
| 10000 | SalaryService.cs:311 | CORRECT | 80TTA cap — statutory |
| 50000 | SalaryService.cs:310 | CORRECT | 80TTB senior cap — statutory |
| 0.04 | everywhere | CORRECT | Cess rate — statutory (unchanged) |
| 0.10 | TaxRules.cs:138 (NPS80CCD2Rate) | CORRECT | Old regime NPS employer rate |
| 0.14 | TaxRules.cs:184,187 | CORRECT | New regime NPS employer rate from FY 2024-25 |

---

## STATUS
- BUG-1,2: Fix immediately — affects every payroll run with rent declared
- BUG-3: Fix immediately — affects 24Q return salary detail taxable figures
- BUG-4: Needs schema verification before fix
- BUG-5,6: Cleanup
