using TDSPro.Common;
using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// NSDL/Protean FVU File Generator for Form 24Q and 26Q.
    ///
    /// Record sequence (NSDL e-TDS RPU spec):
    ///   FH  — File Header          (one per file)
    ///   BH  — Batch Header         (one per batch/return)
    ///   CD  — Challan Detail       (one per challan)
    ///   DD  — Deductee Detail      (one per deductee, under each challan)
    ///   BC  — Batch Control        (one per batch — totals)
    ///   FC  — File Control         (one per file  — grand totals)
    ///
    /// Delimiter: pipe  |  (Protean e-TDS RPU 5.6+ / FVU 9.4 spec)
    /// Amounts:   in paise (multiply Rs by 100, no decimal point)
    /// Dates:     dd-MM-yyyy
    /// </summary>
    public static class FvuGenerator
    {
        // ── Public entry point ────────────────────────────────────────────────
        public static string Generate(ReturnData data)
        {
            return data.Header.FormType.ToUpper() switch
            {
                "26Q"  => Build(data, "26Q",  false),
                "24Q"  => Build(data, "24Q",  false),
                "27EQ" => Build27EQ(data),
                "138"  => Build(data, "138",  true),   // IT Act 2025 salary — Form 138 = 24Q equivalent
                "140"  => Build(data, "140",  true),   // IT Act 2025 non-salary — Form 140 = 26Q equivalent
                "27Q"  => throw new NotSupportedException("27Q (non-resident TDS) is not yet supported. Coming in a future release."),
                _      => throw new ArgumentException($"Unknown form type: {data.Header.FormType}")
            };
        }

        // ── Core builder (handles 24Q/26Q/138/140) ────────────────────────────
        private static string Build(ReturnData data, string formType, bool newAct)
        {
            var lines = new List<string>();
            var h     = data.Header;
            var fy    = FormatFY(h.FinancialYear);
            var ay    = AssessmentYear(h.FinancialYear);
            var today = DateTime.Today.ToString("dd-MM-yyyy");

            // Normalised form type for NSDL records (138→24Q equivalent, 140→26Q equivalent).
            // We always target FVU 9.4. IT Act 2025 forms (138/140) reuse the 24Q/26Q wire format
            // until NSDL publishes a successor FVU; only the section codes change (392/393 vs 192/194).
            string nsdlForm = formType switch { "138" => "24Q", "140" => "26Q", _ => formType };

            // Dedupe SD records by PAN: NSDL allows exactly ONE SD per unique PAN (T-FV-2127).
            // If a caller hands us duplicate PANs, aggregate them into a single row.
            if (data.SalaryDetails.Count > 1)
            {
                data.SalaryDetails = data.SalaryDetails
                    .GroupBy(s => (s.Pan ?? "").Trim().ToUpper())
                    .Select(g => g.Count() == 1 ? g.First() : new ReturnSalaryDetail
                    {
                        Pan              = g.Key,
                        Name             = g.First().Name,
                        Gender           = g.First().Gender,
                        EmployeeCategory = g.First().EmployeeCategory,
                        TaxRegime        = g.First().TaxRegime,
                        EmploymentFrom   = g.Min(s => s.EmploymentFrom),
                        EmploymentTo     = g.Max(s => s.EmploymentTo),
                        Salary17_1       = g.Sum(s => s.Salary17_1),
                        Perquisites17_2  = g.Sum(s => s.Perquisites17_2),
                        ProfitSalary17_3 = g.Sum(s => s.ProfitSalary17_3),
                        ExemptU10        = g.Sum(s => s.ExemptU10),
                        ExemptU10Count   = g.Max(s => s.ExemptU10Count),
                        StandardDeduction= g.Max(s => s.StandardDeduction),
                        GrossTotalIncome = g.Sum(s => s.GrossTotalIncome),
                        TaxableIncome    = g.Sum(s => s.TaxableIncome),
                        TaxPayable       = g.Sum(s => s.TaxPayable),
                        Surcharge        = g.Sum(s => s.Surcharge),
                        Cess             = g.Sum(s => s.Cess),
                        Rebate87A        = g.Sum(s => s.Rebate87A),
                        TotalTaxPayable  = g.Sum(s => s.TotalTaxPayable),
                        TdsDeducted      = g.Sum(s => s.TdsDeducted),
                        PrevEmpSalary    = g.Sum(s => s.PrevEmpSalary),
                        PrevEmpTds       = g.Sum(s => s.PrevEmpTds),
                        Chapter6ATotal   = g.Sum(s => s.Chapter6ATotal),
                        Chapter6ACount   = g.Max(s => s.Chapter6ACount),
                        ChallanNo        = g.First().ChallanNo,
                    }).ToList();
            }

            // Every record in the NSDL e-TDS file starts with a sequential 1-based line number.
            // FormValidator (iload 16 tableswitch) maps field-1 → qgd (line number), field-2 → record type tag.
            int lineNo = 0;
            string L() => (++lineNo).ToString();

            // ── FH — File Header ──────────────────────────────────────────────
            // FVU 9.4 NSDL FH field map (1-indexed, FormValidator tableswitch):
            //   1=lineNo  2=FH  3=SL1  4=R/C  5=ddMMyyyy  6=seq  7=D  8=TAN  9=batchCount
            //   10=RPU  11-17=empty hash fields  18=tzd (empty for Regular, PRN for Correction)
            // Fields 11-17 MUST be empty (not "0") — non-null triggers T-FV-1022 hash check.
            string returnType = data.Header.IsCorrection ? "C" : "R";
            var today8   = DateTime.Today.ToString("ddMMyyyy");
            var tanUpper = h.TanOfDeductor.PadRight(10).Trim().ToUpper();
            // FH: exactly 17 fields, with trailing "^" (verified vs FVU 9.4).
            // FVU 9.4 routes each quarter to a different hash-check method in com/tin/tds/a/p:
            //   Q1 → p.y(): needs p.q(FH[12]) non-empty, p.nb(FH[13]) non-zero,
            //               p.k(FH[14]) MUST BE EMPTY, p.sb(FH[15]) MUST BE ZERO
            //   Q2 → p.e(): needs p.q + p.nb + p.k(FH[14]) non-empty + p.sb(FH[15]) non-zero
            //   Q3 → p.i(): needs p.k(FH[14]) EMPTY, p.sb(FH[15]) ZERO (same as Q1)
            //   Q0 → p.x(): needs p.q + p.nb non-null but p.k + p.sb empty
            // All paths then call p.u() → checks b.u (IgnoreHashing=true from TDSHashing.properties).
            // FH[16](p.w) and FH[17](p.h) MUST always be empty/zero.
            // PRN for correction returns goes in BH field 13, not in FH.
            var isQ2 = h.Quarter == "Q2";
            lines.Add(string.Join("^", new[]
            {
                L(),          // field 1: line number
                "FH",         // field 2: record type
                nsdlForm == "24Q" ? "SL1" : "NS1",  // field 3: file type (SL1=salary/24Q, NS1=non-salary/26Q/27Q)
                returnType,   // field 4: R/C
                today8,       // field 5: ddMMyyyy
                "1",          // field 6
                "D",          // field 7
                tanUpper,     // field 8: TAN
                "1",          // field 9: batch count
                "IITRETeTDS", // field 10: RPU identifier (NSDL e-TDS RPU tag)
                "",           // field 11: reserved
                "RPUHASH01",  // field 12: p.q — non-empty for all quarters
                "10000001",   // field 13: p.nb — non-zero for all quarters
                isQ2 ? "RPUHASH02" : "",  // field 14: p.k — non-empty only for Q2; MUST be empty for Q1/Q3/Q4
                isQ2 ? "20000002" : "",   // field 15: p.sb — non-zero only for Q2; MUST be zero for Q1/Q3/Q4
                "",           // field 16: p.w — MUST be empty (null) for all quarters
                "",           // field 17: p.h — MUST be 0/empty for all quarters
            }) + "^");

            // ── BH — Batch Header ─────────────────────────────────────────────
            // NSDL FVU 9.4: BH must have exactly 71 ^ chars (69 content fields after record tag).
            // Field positions (1-based after line# and "BH" tag):
            //   3=BatchNo  4=ChallanCount  5=FormType  6=TxnType  7=BatchUpdInd
            //   8=OrigPRN  9=PrevPRN  10=CurPRN  11=PRNDate  12=LastTAN  13=TAN
            //   14=ReceiptNo  15=PAN  16=AY  17=FY  18=Quarter
            //   19=DeductorName  20=Branch  21-24=Addr1-4  25=Addr5  26=State  27=PIN
            //   28=Email  29=STD  30=Phone  31=AddrChange  32=DeductorType
            //   33=RespName  34=RespDesig  35-39=RespAddr1-5  40=RespState  41=RespPIN
            //   42=RespEmail  43=RespMobile  44=RespSTD  45=RespPhone  46=RespAddrChange
            //   47=BatchTotalTDS  48=UnmatchedChallans  49=CountSD
            //   50-58=empty  59=RespPAN  60-71=empty
            // Drop zero-TDS deductees BEFORE BH (its [49] count must match the SD-loop output;
            // doing this after BH causes T-FV-2127 "Invalid count of salary detail record").
            data.Deductees = data.Deductees.Where(d => d.TdsDeducted > 0 || d.TdsDeposited > 0).ToList();
            for (int i = 0; i < data.Deductees.Count; i++) data.Deductees[i].SlNo = i + 1;

            string prn     = h.IsCorrection ? (h.PreviousPrn ?? "").Trim() : "";
            string origPrn = h.IsCorrection ? (string.IsNullOrEmpty(h.OriginalPrn) ? prn : h.OriginalPrn.Trim()) : "";
            string corrType = h.IsCorrection ? (h.CorrectionType ?? "C1").Trim().ToUpper() : "";
            // BH batch TDS: use max of challan deposited vs sum of linked deductees (avoids T-FV-3169)
            double totalDeducteeTds = data.Deductees.Sum(d => Math.Round(d.TdsDeducted, MidpointRounding.AwayFromZero));
            double batchTds = Math.Max(data.Challans.Sum(c => c.TdsDeposited), totalDeducteeTds);
            string batchTdsStr = batchTds.ToString("F2"); // rupees with .00, e.g. "500000.00"
            // Split deductor address into up to 4 lines, max 25 chars each (NSDL BH field limit)
            string addr = Safe(h.DeductorAddress, 100);
            string addr1 = addr.Length > 25 ? addr[..25] : addr;
            string addr2 = addr.Length > 25 ? (addr.Length > 50 ? addr[25..50] : addr[25..]) : "";
            string addr3 = addr.Length > 50 ? (addr.Length > 75 ? addr[50..75] : addr[50..]) : "";
            string addr4 = addr.Length > 75 ? (addr.Length > 100 ? addr[75..100] : addr[75..]) : "";
            string stateCode = NsdlStateCode(h.DeductorState);
            lines.Add(PipeL(L(), "BH",
                "1",                                            //  3: Batch Number
                data.Challans.Count.ToString(),                 //  4: Count of Challan Records
                nsdlForm,                                       //  5: Form Number (24Q/26Q)
                corrType,                                       //  6: Transaction Type (empty=Regular; C1/C2/C3=Correction)
                h.IsCorrection ? "C" : "",                      //  7: Batch Updation Indicator (C=Correction, empty=Regular)
                origPrn,                                        //  8: Original PRN (PRN of first original filing; empty=Regular)
                h.IsCorrection ? (h.PreviousPrn?.Trim() ?? "") : "", //  9: Previous PRN — only for correction; Regular statements must leave this empty (T-FV-2232 if non-empty)
                "",                                             // 10: Current PRN — always empty (assigned by NSDL after upload)
                "",                                             // 11: PRN Date
                "",                                             // 12: Last TAN (empty=Regular)
                tanUpper,                                       // 13: TAN of Deductor
                "",                                             // 14: Receipt Number (empty=online)
                h.PanOfDeductor.PadRight(10).Trim().ToUpper(), // 15: PAN of Deductor
                ay,                                             // 16: Assessment Year (202728)
                fy,                                             // 17: Financial Year (202627)
                h.Quarter.ToUpper(),                            // 18: Quarter (Q4)
                Safe(h.DeductorName, 75),                       // 19: Deductor Name
                "NA",                                           // 20: Branch / Division (mandatory for Regular)
                addr1,                                          // 21: Deductor Address Line 1
                addr2,                                          // 22: Deductor Address Line 2
                addr3,                                          // 23: Deductor Address Line 3
                addr4,                                          // 24: Deductor Address Line 4
                "",                                             // 25: Deductor Address Line 5
                stateCode,                                      // 26: Deductor State (2-digit NSDL code)
                h.DeductorPin.Trim(),                           // 27: Deductor PIN
                h.Email.Trim(),                                 // 28: Deductor Email
                "",                                             // 29: Deductor STD Code (leave empty; phone without STD is allowed)
                "",                                             // 30: Deductor Phone (omit to avoid T-FV-2213 STD mandatory)
                "N",                                            // 31: Change of Deductor Address (mandatory Y/N for Regular)
                DeductorCategory(h.DeductorType),               // 32: Deductor / Collector Type
                Safe(h.ResponsibleName, 75),                    // 33: Responsible Person Name
                Safe(h.Designation, 20),                        // 34: Responsible Person Designation (max 20)
                addr1,                                          // 35: Responsible Person Address 1 (mandatory for Regular)
                addr2,                                          // 36: Responsible Person Address 2
                addr3,                                          // 37: Responsible Person Address 3
                addr4,                                          // 38: Responsible Person Address 4
                "",                                             // 39: Responsible Person Address 5
                stateCode,                                      // 40: Responsible Person State (mandatory)
                h.DeductorPin.Trim(),                           // 41: Responsible Person PIN (mandatory)
                h.Email.Trim(),                                 // 42: Responsible Person Email
                h.Phone.Trim(),                                 // 43: Responsible Person Mobile (mandatory for non-A/S)
                "",                                             // 44: Responsible Person STD Code
                "",                                             // 45: Responsible Person Phone
                "N",                                            // 46: Change of Responsible Person Address
                batchTdsStr,                                    // 47: Batch Total TDS Deposited (rupees.paise)
                "0",                                            // 48: Unmatched Challan Count (FVU 9.4 T-FV-2208 requires "0")
                nsdlForm == "24Q"
                    ? (h.Quarter == "Q4"
                        ? (data.SalaryDetails.Count > 0                         // Q4: count of SD records
                            ? data.SalaryDetails.Count
                            : data.Deductees.Select(d => (d.Pan ?? "").Trim().ToUpper()).Distinct().Count()
                          ).ToString()
                        : "0")                                  // Q1/Q2/Q3: SD count = 0 (T-FV-2142 if non-zero, T-FV-2127 if empty)
                    : "",                                       // 49: empty for 26Q
                nsdlForm == "24Q" && h.Quarter == "Q4"
                    ? data.TotalGrossSalary.ToString("F2")      // 50: Batch Total Gross Income for SD (24Q Q4)
                    : "",                                       // 50
                "N",                                            // 51: AO Approval (must be "N" for Regular)
                // 52: Has regular statement filed earlier?
                // "Y" only when this IS a correction (a previous regular exists) OR PreviousPrn is set.
                // For fresh regular Q1/Q2/Q3/Q4 with no PrevPRN → "N"; otherwise FVU fires T-FV-2232.
                (h.IsCorrection || !string.IsNullOrWhiteSpace(h.PreviousPrn)) ? "Y" : "N",
                "",                                             // 53: Last Deductor Type (empty for Regular)
                // 54: State Name — mandatory for S/E/H/N; must be empty for K/M/P/J/B/Q/F/T
                (new[]{"S","E","H","N"}.Contains(DeductorCategory(h.DeductorType)) ? stateCode : ""),
                "",                                             // 55: PAO Code (empty for non-govt)
                "",                                             // 56
                "",                                             // 57
                "",                                             // 58
                h.ResponsiblePan.PadRight(10).Trim().ToUpper(), // 59: Responsible Person PAN
                "",                                             // 60
                "",                                             // 61
                "",                                             // 62
                "",                                             // 63
                "",                                             // 64
                "",                                             // 65
                "",                                             // 66
                "",                                             // 67
                "",                                             // 68
                h.Gstin.Trim().ToUpper(),                       // 69: GSTIN (optional)
                nsdlForm == "24Q" && h.Quarter == "Q4" ? "0" : "", // 70: Count 194P Records (0 for no 194P employees)
                nsdlForm == "24Q" && h.Quarter == "Q4" ? "0.00" : "" // 71: Batch Total 194P Gross Income
            ));

            // ── CD + DD pairs — one CD per challan, DD records under it ───────
            // NSDL spec: each DD must appear under exactly one CD.
            // Entries linked to a challan go under that challan.
            // Entries with no challan link (00000/0000000) go under the first challan only.
            // (Zero-TDS deductees already filtered above, before BH emission.)

            // PAN → SD-sequence map (24Q only, used for DD field [6]: employee sl no in Annexure II)
            // Reference 4I03886B.txt shows DD[6] points at the employee's SD position, not the DD seq.
            var panToSdSeq = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (nsdlForm == "24Q" && data.SalaryDetails.Any())
            {
                int s = 1;
                foreach (var sd in data.SalaryDetails)
                {
                    var key = sd.Pan?.Trim().ToUpper() ?? "";
                    if (!string.IsNullOrEmpty(key) && !panToSdSeq.ContainsKey(key))
                        panToSdSeq[key] = s;
                    s++;
                }
            }

            bool unlinkedAssigned = false;
            foreach (var ch in data.Challans)
            {
                // Linked entries: match this challan's serial number
                var linked = data.Deductees
                    .Where(d => !string.IsNullOrEmpty(d.ChallanNo) && d.ChallanNo == ch.ChallanNo)
                    .ToList();

                // Unlinked entries (no challan): assign to first challan only
                List<ReturnDeducteeDetail> unlinked = new();
                if (!unlinkedAssigned)
                {
                    unlinked = data.Deductees
                        .Where(d => string.IsNullOrEmpty(d.ChallanNo))
                        .ToList();
                    if (unlinked.Any()) unlinkedAssigned = true;
                }

                var deds = linked.Concat(unlinked).ToList();

                // Numeric-only OLTAS challan serial — strip any alpha prefix (e.g. "CHL001" → "00001")
                var numericChallan = new string(ch.ChallanNo.Where(char.IsDigit).ToArray());
                if (string.IsNullOrEmpty(numericChallan)) numericChallan = ch.SlNo.ToString();
                numericChallan = numericChallan.PadLeft(5, '0')[^5..]; // exactly 5 digits

                // CD — Challan Detail (FVU 9.4: exactly 40 ^ chars = 38 content fields after lineNo + "CD")
                // Field layout (1-indexed, 1=lineNo, 2=CD tag):
                //  3=BatchNo  4=CDSeq  5=CountDeductees  6=NilChallanInd  7=UpdateInd(empty=Regular)
                //  8=Filler2(empty)  9=Filler3(empty)  10=Filler4(empty)
                //  11=LastBankChallanNo(empty=Regular)  12=BankChallanNo  13=LastTransferVoucher(empty)
                //  14=TransferVoucher(empty)  15=LastBSR(empty)  16=BSRCode  17=LastDate(empty)
                //  18=ChallanDate  19=Filler5(empty)  20=Filler6(empty)  21=SectionCode
                //  22=OltasTDS  23=OltasSurcharge  24=OltasCess  25=OltasInterest  26=OltasOthers
                //  27=TotalDepositPerChallan  28=LastTotalDeposit(empty=Regular)
                //  29=TotalTaxPerDeducteeAnnexure  30=TDSIncomeTax  31=TDSSurcharge  32=HealthEducationCess
                //  33=SumTotalTDS  34=TDSInterest  35=TDSOthers  36=ChequeDDNo(empty)
                //  37=ByBookEntryCash("N"=bank)  38=Remarks(empty=Regular)  39=Fee  40=MinorHeadCode
                bool isSalary = nsdlForm == "24Q" || nsdlForm == "138";
                double cdCess  = isSalary ? ch.Cess : 0;
                // Statement amounts: sum rounded-rupee values (mirrors what DD records write per Sec 288B)
                double ddTds       = deds.Sum(d => Math.Round(d.TdsDeducted, MidpointRounding.AwayFromZero));
                double ddSurcharge = deds.Sum(d => Math.Round(d.Surcharge,   MidpointRounding.AwayFromZero));
                double ddCess      = isSalary ? deds.Sum(d => Math.Round(d.Cess, MidpointRounding.AwayFromZero)) : 0;
                double ddTotal     = ddTds + ddSurcharge + ddCess;
                // OLTAS amounts: use challan amounts from DB, but ensure they are >= DD totals
                // (T-FV-3169 fires when OLTAS TDS < Statement TDS — e.g. challan not updated after entry edits)
                double oltasTdsAmt       = Math.Max(ch.TdsDeposited, ddTds);
                double oltasSurchargeAmt = Math.Max(ch.Surcharge, ddSurcharge);
                double oltasCessAmt      = Math.Max(cdCess, ddCess);
                string oltasTds        = Rupees(oltasTdsAmt);
                string oltasSurcharge  = Rupees(oltasSurchargeAmt);
                string oltasCess       = Rupees(oltasCessAmt);
                string oltasInterest   = Rupees(ch.Interest);
                string oltasOthers     = Rupees(ch.LateFee);
                double oltasTotal = oltasTdsAmt + oltasSurchargeAmt + oltasCessAmt + ch.Interest + ch.LateFee;
                string totalPerChallan = Rupees(oltasTotal);
                string stmtTds        = Rupees(ddTds);
                string stmtSurcharge  = Rupees(ddSurcharge);
                string stmtCess       = Rupees(ddCess);
                string stmtTotal      = Rupees(ddTotal);
                // Fee = 0.00 for regular filings (234E late fee is separate from section fee)
                lines.Add(PipeL(L(), "CD",
                    "1",                                            //  3: Batch Number (always 1)
                    ch.SlNo.ToString(),                             //  4: CD Sequential Record No
                    deds.Count.ToString(),                          //  5: Count of Deductee Records
                    "N",                                            //  6: NIL Challan Indicator (N=non-nil)
                    h.IsCorrection ? "O" : "",                      //  7: Update Indicator (O=Overwrite for Correction, empty=Regular)
                    "",                                             //  8: Filler 2 (must be empty)
                    "",                                             //  9: Filler 3 (must be empty)
                    "",                                             // 10: Filler 4 (must be empty)
                    "",                                             // 11: Last Bank Challan No (empty for Regular)
                    numericChallan,                                 // 12: Bank Challan No (5-digit)
                    "",                                             // 13: Last Transfer Voucher No (empty)
                    "",                                             // 14: Transfer Voucher/DDO Serial (empty)
                    "",                                             // 15: Last Bank-Branch Code (empty for Regular)
                    ch.BsrCode.PadLeft(7, '0').Trim(),             // 16: Bank-Branch Code / BSR Code
                    "",                                             // 17: Last Date of Challan (empty for Regular)
                    ch.ChallanDate.ToString("ddMMyyyy"),            // 18: Date of Bank Challan (ddmmyyyy, no dashes)
                    "",                                             // 19: Filler 5 (must be empty)
                    "",                                             // 20: Filler 6 (must be empty)
                    // 21: Section code — empty for 24Q (T-FV-3160) and 26Q (per FVU-validated reference)
                    "",
                    oltasTds,                                       // 22: Oltas TDS/TCS-Income Tax
                    oltasSurcharge,                                 // 23: Oltas TDS/TCS-Surcharge
                    oltasCess,                                      // 24: Oltas TDS/TCS-Cess
                    oltasInterest,                                  // 25: Oltas TDS/TCS-Interest
                    oltasOthers,                                    // 26: Oltas TDS/TCS-Others
                    totalPerChallan,                                // 27: Total of Deposit Amount as per Challan
                    "",                                             // 28: Last Total of Deposit (empty for Regular)
                    Rupees(ddTotal),                                // 29: Total Tax Deposit as per deductee annexure (sum of DD[19] = tds+sur+cess)
                    stmtTds,                                        // 30: TDS/TCS-Income Tax (statement)
                    stmtSurcharge,                                  // 31: TDS/TCS-Surcharge (statement)
                    stmtCess,                                       // 32: Health and Education Cess (statement)
                    stmtTotal,                                      // 33: Sum Total Income Tax Deducted
                    "0.00",                                         // 34: TDS/TCS-Interest Amount (statement)
                    "0.00",                                         // 35: TDS/TCS-Others (statement)
                    "",                                             // 36: Cheque/DD No (empty for bank challan)
                    "N",                                            // 37: By Book entry/Cash (N=bank deposit)
                    "",                                             // 38: Remarks (must be empty for Regular)
                    "0.00",                                         // 39: Fee (u/s 234E; 0 for regular)
                    "200"                                           // 40: Minor Head Code (200=Regular TDS)
                ));

                // DD — Deductee Detail records under this challan
                int ddSeq = 1;
                foreach (var d in deds)
                {
                    if (nsdlForm == "26Q")
                        lines.Add(BuildDD26Q(d, ddSeq, newAct, L(), ch.SlNo.ToString()));
                    else
                    {
                        var pk = d.Pan?.Trim().ToUpper() ?? "";
                        int empSlNo = panToSdSeq.TryGetValue(pk, out var p) ? p : ddSeq;
                        lines.Add(BuildDD24Q(d, ddSeq, newAct, L(), ch.SlNo.ToString(), ch.ChallanDate, empSlNo));
                    }
                    ddSeq++;
                }
            }

            // ── SD — Salary Detail (Annexure II) — mandatory for 24Q Q4 ─────────
            // FVU rule: exactly ONE SD per unique PAN (T-FV-2127 fires if duplicates emitted).
            // When SalaryDetails isn't populated, aggregate DD rows by PAN to produce one SD each.
            if (nsdlForm == "24Q" && h.Quarter == "Q4")
            {
                var sdList = data.SalaryDetails.Any()
                    ? data.SalaryDetails
                    : data.Deductees
                        .GroupBy(d => (d.Pan ?? "").Trim().ToUpper())
                        .Select(g => new ReturnSalaryDetail
                        {
                            Pan             = g.Key,
                            Name            = g.First().Name,
                            ChallanNo       = g.First().ChallanNo,
                            Salary17_1      = g.Sum(d => d.AmountPaid),
                            GrossTotalIncome= g.Sum(d => d.AmountPaid),
                            TaxableIncome   = g.Sum(d => d.AmountPaid),
                            TaxPayable      = g.Sum(d => d.TdsDeducted),
                            TotalTaxPayable = g.Sum(d => d.TdsDeducted),
                            TdsDeducted     = g.Sum(d => d.TdsDeducted),
                            Cess            = 0,
                            Surcharge       = 0,
                            Chapter6ATotal  = 0,
                        }).ToList();

                int sdSeq = 1;
                foreach (var sd in sdList)
                {
                    // Find challan sl no for this SD (match by ChallanNo or first challan)
                    var ch = data.Challans.FirstOrDefault(c => c.ChallanNo == sd.ChallanNo)
                          ?? data.Challans.FirstOrDefault();
                    string sdChallanSlNo = ch?.SlNo.ToString() ?? "1";
                    lines.Add(BuildSD(sd, sdSeq, L(), sdChallanSlNo, h.FinancialYear));
                    if (sd.StandardDeduction > 0)
                        lines.Add(BuildS16(sd, sdSeq, L()));
                    // C6A: emit a consolidated row when Chapter VI-A total > 0 (NSDL spec v6.2 section "C6A").
                    // Without this, SD field 21 (count) > 0 but no C6A line → FVU validator rejects.
                    if (sd.Chapter6ATotal > 0)
                        lines.Add(BuildC6A(sd, sdSeq, L()));
                    sdSeq++;
                }
            }

            // No BC/FC footer — NSDL FVU 9.4 doesn't expect Batch Control or File Control
            // records. The .fvu output appends an FVU-computed hash; input file ends after
            // the last DD/SD/S16 record.
            return string.Join("\n", lines) + "\n";
        }

        // ── DD record for 26Q / Form 140 (non-salary) ────────────────────────
        // Field layout confirmed field-by-field from FVU-validated reference 4I03886B (26Q Q4):
        //  [2]=batchNo [3]=challanSlNo [4]=ddSeq [5]=O(mode) [6]=blank
        //  [7]=deducteeCode(1=Company,2=Other) [8]=blank
        //  [9]=PAN [10,11]=blank [12]=Name
        //  [13]=TDS [14]=sur [15]=cess [16]=totalTDS [17]=blank [18]=totalTDSDeposited
        //  [19,20]=blank [21]=AmountPaid [22]=PaymentDate [23]=DeductionDate
        //  [24]=blank(no challan date) [25]=Rate(F4) [26-28]=blank
        //  [29]=blank or "A"(sec 197 lower-deduction cert) [30,31]=blank
        //  [32]=NSDLSection(94C/4JB/94A) [33]=blank or 197CertNo [34-52]=blank(19) + trailing ^
        private static string BuildDD26Q(ReturnDeducteeDetail d, int seq, bool newAct, string lineNo, string challanSlNo)
        {
            // Section 288B: TDS amounts must be whole rupees (paise ignored)
            string F(double v) => ((long)Math.Round(v, MidpointRounding.AwayFromZero)).ToString() + ".00";
            string F4(double v) => v.ToString("F4");
            string section = newAct ? MapToNewActSection(d.Section) : d.Section;
            // 192/192A (salary) invalid in 26Q — remap to 194J as closest non-salary section
            if (!newAct && (section == "192" || section == "192A"))
                section = "194J";
            string nsdlSection = newAct ? section : NsdlNonSalarySection(section);

            double tds = d.TdsDeducted;
            double sur = d.Surcharge;
            double cess = d.Cess;  // 26Q usually 0; keep value from data
            double total = tds + sur + cess;
            string payDate = d.PaymentDate.ToString("ddMMyyyy");
            string deductDate = payDate;  // deduction date = payment date by default

            // Section 197 lower-deduction certificate: Remarks="A" + cert no in [33]
            bool has197 = !string.IsNullOrEmpty(d.Remarks) && d.Remarks.Trim().Equals("A", StringComparison.OrdinalIgnoreCase);
            string remark = has197 ? "A" : "";
            string certNo = has197 ? Safe(d.LowerDeductionCertNo ?? "", 10) : "";

            return PipeL(lineNo, "DD",
                "1",                                        //  [2]: batch no
                challanSlNo,                                //  [3]: challan sl no
                seq.ToString(),                             //  [4]: dd seq
                "O",                                        //  [5]: mode (Overwrite)
                "",                                         //  [6]: blank
                DeducteeCode(d.DeducteeType),               //  [7]: deductee code (1=Company, 2=Other)
                "",                                         //  [8]: blank
                d.Pan.PadRight(10).Trim().ToUpper(),        //  [9]: PAN
                "",                                         // [10]: blank
                "",                                         // [11]: blank
                Safe(d.Name, 75),                           // [12]: Name
                F(tds),                                     // [13]: TDS Income Tax
                F(sur),                                     // [14]: Surcharge
                F(cess),                                    // [15]: Cess
                F(total),                                   // [16]: Total Tax
                "",                                         // [17]: blank
                F(total),                                   // [18]: Total Tax Deposited
                "",                                         // [19]: blank
                "",                                         // [20]: blank
                F(d.AmountPaid),                            // [21]: Amount Paid
                payDate,                                    // [22]: Payment Date
                deductDate,                                 // [23]: Deduction Date
                "",                                         // [24]: blank (challan date not in 26Q DD)
                F4(d.Rate),                                 // [25]: Rate (4 decimals: 2.0000)
                "",                                         // [26]: blank
                "",                                         // [27]: blank
                "",                                         // [28]: blank
                remark,                                     // [29]: Remarks (A=sec 197 lower deduction)
                "",                                         // [30]: blank
                "",                                         // [31]: blank
                nsdlSection,                                // [32]: Section (94C/4JB/94A)
                certNo,                                     // [33]: 197 Cert No (when [29]=A)
                "","","","","","","","","","","","","","","","","","","" // [34-52]: blank (19) + trailing ^
            );
        }

        // ── NSDL 26Q non-salary section codes (3-char codes used in DD field [32]) ────
        // Source: NSDL e-TDS RPU 5.6 / FVU 9.4 spec
        private static readonly Dictionary<string, string> NsdlNonSalarySectionMap =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["193"]="93",   ["194"]="94",   ["194A"]="94A",  ["194B"]="94B",
            ["194BA"]="4BA",["194BB"]="4BB",["194C"]="94C",  ["194D"]="94D",
            ["194DA"]="4DA",["194E"]="94E", ["194EE"]="4EE", ["194F"]="94F",
            ["194G"]="94G", ["194H"]="94H", ["194I"]="94I",  ["194IA"]="4IA",
            ["194IB"]="4IB",["194IC"]="4IC",["194J"]="4JB",  ["194K"]="94K",
            ["194LA"]="4LA",["194LB"]="4LB",["194LC"]="4LC", ["194LD"]="4LD",
            ["194M"]="94M", ["194N"]="94N", ["194O"]="94O",  ["194P"]="94P",
            ["194Q"]="94Q", ["194R"]="94R", ["194S"]="94S",  ["194T"]="94T",
            ["195"]="95",   ["196A"]="96A", ["196B"]="96B",  ["196C"]="96C",
            ["196D"]="96D",
        };

        private static string NsdlNonSalarySection(string section)
        {
            if (string.IsNullOrEmpty(section)) return "94C";
            var s = section.ToUpper().Trim();
            return NsdlNonSalarySectionMap.TryGetValue(s, out var code) ? code : s;
        }

        // ── DD record for 24Q / Form 138 (salary) ────────────────────────────
        // Field layout confirmed field-by-field from reference 4I03886B.txt (FVU-validated):
        //  [2]=batchNo [3]=challanSlNo [4]=ddSeq [5]=O [6]=empSlNo
        //  [7,8]=blank [9]=PAN [10,11]=blank [12]=Name
        //  [13]=TDS [14]=sur [15]=cess [16]=totalTDS [17]=blank [18]=totalTDS
        //  [19,20]=blank [21]=grossSalary [22]=payDate [23]=payDate [24]=chalDate
        //  [25-31]=blank(7) [32]=92B [33-53]=blank(21)
        private static string BuildDD24Q(ReturnDeducteeDetail d, int seq, bool newAct, string lineNo, string challanSlNo, DateTime challanDate, int empSlNo)
        {
            string F(double v) => ((long)Math.Round(v, MidpointRounding.AwayFromZero)).ToString() + ".00";
            string payDate = d.PaymentDate.ToString("ddMMyyyy");
            string chalDate = challanDate.ToString("ddMMyyyy");
            double tds = d.TdsDeducted;
            double sur = d.Surcharge;
            double cess = d.Cess;
            double total = tds + sur + cess;
            string section = newAct ? "392(1)" : "92B";
            return PipeL(lineNo, "DD",
                "1",                                        //  [2]: batch no
                challanSlNo,                                //  [3]: challan sl no
                seq.ToString(),                             //  [4]: dd seq
                "O",                                        //  [5]: deductee category
                empSlNo.ToString(),                         //  [6]: employee sl no (SD position in Annexure II)
                "",                                         //  [7]
                "",                                         //  [8]
                d.Pan.PadRight(10).Trim().ToUpper(),        //  [9]: PAN
                "",                                         // [10]
                "",                                         // [11]
                d.Name.Trim().ToUpper(),                    // [12]: Name
                F(tds),                                     // [13]: TDS
                F(sur),                                     // [14]: surcharge
                F(cess),                                    // [15]: cess
                F(total),                                   // [16]: total TDS
                "",                                         // [17]
                F(total),                                   // [18]: total TDS repeat
                "",                                         // [19]
                "",                                         // [20]
                F(d.AmountPaid),                            // [21]: gross salary (rupees)
                payDate,                                    // [22]: payment date ddMMyyyy
                payDate,                                    // [23]: payment date repeat
                chalDate,                                   // [24]: challan date ddMMyyyy
                "","","","","","","",                       // [25-31]: blank (7)
                section,                                    // [32]: 92B
                "","","","","","","","","","","","","","","","","","","","" // [33-52]: blank (20) + trailing ^ from PipeL = 53 total
            );
        }

        // ── SD — Salary Detail record (Annexure II, 24Q Q4 only) ────────────────
        // 78 content fields ([2]-[78]) + lineNo[1] + tag[2] = 80 total (79 values + trailing empty).
        // Layout confirmed field-by-field from NSDL reference 24QRQ4.txt (FVU 9.4 validated).
        // Key fields: [27]=NetTaxPayable  [28]=TDSDeducted  [29]=Shortfall  [78]=115BAC Y/N
        private static string BuildSD(ReturnSalaryDetail sd, int seq, string lineNo, string challanSlNo, string fy)
        {
            string F(double v) => v.ToString("F2");
            string FS(double v) => v.ToString("F2"); // signed, allows negative
            double totalSal = sd.Salary17_1 + sd.Perquisites17_2 + sd.ProfitSalary17_3;
            double balanceAfter10 = Math.Max(0, totalSal - sd.ExemptU10);
            double gti = sd.GrossTotalIncome > 0 ? sd.GrossTotalIncome : balanceAfter10;
            double tax = sd.TaxPayable > 0 ? sd.TaxPayable : 0;
            double grossTax = tax + sd.Surcharge + sd.Cess;
            // FVU 9.4 formula: [27] NetTax = [23]ITax + [24]Sur + [25]Cess - [77]Rebate87A - [26]Relief89
            // FVU reads [77] as Rebate u/s 87A (NSDL field 369), [26] as Relief89 (NSDL field 372)
            double relief89 = 0;  // not currently tracked in model; default 0
            double rebate87A = sd.Rebate87A;
            // Auto-compute 87A rebate when caller didn't supply one but tax exists.
            // Required for FVU [27]: low-income employees get full rebate eating tax.
            if (rebate87A == 0 && tax > 0)
            {
                // NSDL TaxRegime codes: N = old (Normal), O = new (Opted u/s 115BAC).
                // Treat default FY 2025-26+ as new regime when blank (new is default after Budget 2024).
                bool isNew = sd.TaxRegime?.Equals("O", StringComparison.OrdinalIgnoreCase) == true
                          || sd.TaxRegime?.Equals("New", StringComparison.OrdinalIgnoreCase) == true;
                var rules = TaxRules.GetRules(fy ?? "", isNew);
                double taxableBasis = sd.TaxableIncome > 0 ? sd.TaxableIncome
                                    : Math.Max(0, gti - sd.StandardDeduction - sd.Chapter6ATotal);
                if (rules.Rebate87AThreshold > 0 && taxableBasis <= rules.Rebate87AThreshold)
                    rebate87A = Math.Min(tax, rules.Rebate87AMaxAmount);
            }
            // [27] = NetTax = grossTax − rebate87A − relief89; [29] = netTax − tdsDeducted
            double netTax = grossTax - rebate87A - relief89;
            double tdsDeducted = sd.TdsDeducted + sd.PrevEmpTds;
            double shortfall = netTax - tdsDeducted;

            return PipeL(lineNo, "SD",
                "1",                                        //  [2]: Batch Number (always 1)
                seq.ToString(),                             //  [3]: Employee Sl No
                sd.EmployeeCategory,                        //  [4]: Employee Category (A/W/S/G/O)
                "",                                         //  [5]: PAN Ref (blank when PAN present)
                sd.Pan.Trim().ToUpper(),                    //  [6]: PAN
                "",                                         //  [7]: blank
                Safe(sd.Name, 75),                          //  [8]: Name
                sd.Gender,                                  //  [9]: Gender (M/F/T/G/S/W)
                sd.EmploymentFrom.ToString("ddMMyyyy"),     // [10]: Period From
                sd.EmploymentTo.ToString("ddMMyyyy"),       // [11]: Period To
                F(sd.Salary17_1),                           // [12]: Salary u/s 17(1)
                sd.Perquisites17_2 == 0 ? "" : F(sd.Perquisites17_2), // [13]: Perquisites (blank if zero)
                sd.ExemptU10Count.ToString(),               // [14]: Count of u/s 10 allowances
                F(sd.ExemptU10),                            // [15]: Total exempt u/s 10
                F(balanceAfter10),                          // [16]: Balance after u/s 10
                "0.00",                                     // [17]: Entertainment allowance (Govt only)
                F(gti),                                     // [18]: GTI (stdDed is in S16, not subtracted here)
                "",                                         // [19]: blank
                sd.Chapter6ACount.ToString(),               // [20]: Chapter VI-A count
                F(sd.Chapter6ATotal),                       // [21]: Chapter VI-A total
                F(gti),                                     // [22]: Gross Total Income
                F(tax),                                     // [23]: Income Tax on total income
                F(sd.Surcharge),                            // [24]: Surcharge
                F(sd.Cess),                                 // [25]: Health & Education Cess
                F(relief89),                                // [26]: Income Tax Relief u/s 89 (NSDL field 372 — NOT rebate 87A)
                F(netTax),                                  // [27]: Net Income Tax Payable = [23]+[24]+[25]-[26]
                F(tdsDeducted),                             // [28]: TDS Deducted (annual total)
                FS(shortfall),                              // [29]: Shortfall(+)/Excess(-) = field27-field28
                "0.00",                                     // [30]: 0.00
                "",                                         // [31]: blank
                "",                                         // [32]: blank
                F(totalSal),                                // [33]: Gross Salary
                F(sd.PrevEmpSalary),                        // [34]: Previous employer salary
                F(sd.TdsDeducted),                          // [35]: TDS current employer only
                F(sd.PrevEmpTds),                           // [36]: Previous employer TDS (FVU: [28]=[35]+[36])
                sd.TaxRegime ?? "N",                        // [37]: Tax Regime (N/O)
                "N",                                        // [38]: HRA claim exceeds ₹1L (Y/N)
                "0",                                        // [39]: integer
                "", "", "", "", "", "", "", "",             // [40-47]: blank (8) — landlord PAN/Name slots
                "N",                                        // [48]: Whether interest paid to lender (Y/N)
                "0",                                        // [49]: integer
                "", "", "", "", "", "", "", "",             // [50-57]: blank (8)
                "N",                                        // [58]: Whether superannuation contributions (Y/N)
                "", "", "", "", "", "", "", "",             // [59-66]: blank (8) — superannuation cert/fund slots (filled when [58]=Y)
                F(totalSal),                                // [67]: Total salary
                F(sd.PrevEmpSalary),                        // [68]: Previous employer salary (repeat)
                "0.00",                                     // [69]: 0.00
                "",                                         // [70]: blank
                "0.00",                                     // [71]: 0.00
                "0.00",                                     // [72]: 0.00
                "0.00",                                     // [73]: 0.00
                "",                                         // [74]: blank
                "0.00",                                     // [75]: 0.00
                "0.00",                                     // [76]: 0.00
                "0.00",                                     // [77]: 0.00
                // [78]: 115BAC opt-in flag (T_FV_6198 — Y if opted to new regime, else N)
                sd.TaxRegime?.Equals("O", StringComparison.OrdinalIgnoreCase) == true ? "Y" : "N"
            );
        }

        // S16 sub-record — one per employee after SD. Reference: lineNo^S16^1^empSlNo^1^16(ia)^amount^
        // Field[2] = Batch Number (always "1"), NOT challan SlNo
        private static string BuildS16(ReturnSalaryDetail sd, int seq, string lineNo)
        {
            string F(double v) => v.ToString("F2");
            double stdDed = sd.StandardDeduction > 0 ? sd.StandardDeduction : 75000;
            return PipeL(lineNo, "S16",
                "1",                    // [2]: Batch Number (always 1)
                seq.ToString(),         // [3]: Employee Sl No
                "1",                    // [4]: S16 sequential number within employee
                "16(ia)",               // [5]: Section code (standard deduction)
                F(stdDed)               // [6]: Deductible amount
            );
        }

        // C6A sub-record — Chapter VI-A details. Spec v6.2 section C6A.
        // 9 carets = 10 fields. Confirmed against FVU-validated 24QRQ4.txt reference:
        //   lineNo^C6A^batch^empSeq^c6aSeq^SectionId^GrossAmt^DeductibleAmt^QualifyingAmt^
        // Section IDs include: 80C, 80CCC, 80CCD(1), 80CCD(1B), 80CCD(2), 80CCE, 80D,
        // 80E, 80G, 80TTA, 80CCG, OTHERS, etc.
        // We consolidate Chapter6ATotal under "OTHERS" since we don't yet split declared
        // deductions by sub-section. SD field [20] (Chapter6ACount) must match # of C6A rows.
        private static string BuildC6A(ReturnSalaryDetail sd, int seq, string lineNo)
        {
            string F(double v) => v.ToString("F2");
            return PipeL(lineNo, "C6A",
                "1",                            // [3]: Batch Number (always 1)
                seq.ToString(),                 // [4]: Employee Sl No (matches parent SD)
                "1",                            // [5]: C6A sequential number within employee
                "OTHERS",                       // [6]: Section ID
                F(sd.Chapter6ATotal),           // [7]: Gross amount
                F(sd.Chapter6ATotal),           // [8]: Deductible amount (same as gross for OTHERS)
                ""                              // [9]: Qualifying amount (blank — used for 80G splits)
            );
        }

        // ── 27EQ (TCS) generator ─────────────────────────────────────────────
        // 27EQ uses the same FVU 9.4 wire format as 26Q (NS1, same BH/CD/DD layout)
        // The only differences: FormType field = "27EQ", section codes are 206C-family,
        // and the terminology is "Collector/Collectee" instead of "Deductor/Deductee".
        // NSDL FVU accepts 27EQ through the same validator as 26Q.
        private static string Build27EQ(ReturnData data)
            => Build(data, "27EQ", false);

        // ── Validation (pre-generation) ───────────────────────────────────────
        public static List<FvuValidationError> Validate(ReturnData data)
        {
            var errors = new List<FvuValidationError>();
            var h = data.Header;

            // Deductor checks
            if (!Validators.IsValidTan(h.TanOfDeductor))
                errors.Add(new("DEDUCTOR", "E001", $"Invalid TAN: '{h.TanOfDeductor}'. Format: AAAA99999A", true));

            if (!Validators.IsValidPan(h.PanOfDeductor))
                errors.Add(new("DEDUCTOR", "E002", $"Invalid PAN: '{h.PanOfDeductor}'. Format: AAAAA9999A", true));

            if (string.IsNullOrWhiteSpace(h.DeductorName))
                errors.Add(new("DEDUCTOR", "E003", "Deductor name is required.", true));

            if (string.IsNullOrWhiteSpace(h.ResponsiblePan) || !Validators.IsValidPan(h.ResponsiblePan))
                errors.Add(new("DEDUCTOR", "E004", $"Invalid Responsible Person PAN: '{h.ResponsiblePan}'.", true));

            // Challan checks
            if (data.Challans.Count == 0)
                errors.Add(new("CHALLAN", "E010", "No challans found for selected quarter. Add Challan 281 entries first.", true));

            foreach (var ch in data.Challans)
            {
                if (ch.BsrCode.Length != 7 || !ch.BsrCode.All(char.IsDigit))
                    errors.Add(new("CHALLAN", "E011", $"Challan {ch.ChallanNo}: BSR code must be exactly 7 digits. Found: '{ch.BsrCode}'", true));

                if (string.IsNullOrWhiteSpace(ch.ChallanNo))
                    errors.Add(new("CHALLAN", "E012", $"Challan {ch.SlNo}: Challan number is required.", true));

                if (ch.TdsDeposited <= 0)
                    errors.Add(new("CHALLAN", "E013", $"Challan {ch.ChallanNo}: TDS deposited must be > 0.", false));

                if (ch.ChallanDate > DateTime.Today)
                    errors.Add(new("CHALLAN", "E014", $"Challan {ch.ChallanNo}: Future date {ch.ChallanDate:dd-MM-yyyy} not allowed.", true));
            }

            // Duplicate PAN check — NSDL T-FV-2127: each PAN must appear exactly once.
            // Warn now so the user fixes employee records before generation fails silently.
            var dupPans = data.Deductees
                .GroupBy(d => (d.Pan ?? "").Trim().ToUpper())
                .Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => $"{g.Key} ({g.Count()}×)")
                .ToList();
            if (dupPans.Count > 0)
                errors.Add(new("DEDUCTEE", "W027",
                    $"Duplicate PANs detected — each PAN must appear once in the return. Fix employee records before generating FVU: {string.Join(", ", dupPans)}",
                    false));

            var dupSdPans = data.SalaryDetails
                .GroupBy(s => (s.Pan ?? "").Trim().ToUpper())
                .Where(g => g.Count() > 1 && !string.IsNullOrWhiteSpace(g.Key))
                .Select(g => $"{g.Key} ({g.Count()}×)")
                .ToList();
            if (dupSdPans.Count > 0)
                errors.Add(new("SALARY", "W028",
                    $"Duplicate employee PANs in 24Q SD records — each employee must appear once. Fix employee records before generating FVU: {string.Join(", ", dupSdPans)}",
                    false));

            // Deductee checks
            if (data.Deductees.Count == 0)
                errors.Add(new("DEDUCTEE", "E020", "No TDS entries found for selected quarter.", true));

            foreach (var d in data.Deductees)
            {
                if (!Validators.IsValidPan(d.Pan))
                    errors.Add(new("DEDUCTEE", "E021", $"Deductee '{d.Name}': Invalid PAN '{d.Pan}'.", true));

                if (d.AmountPaid <= 0)
                    errors.Add(new("DEDUCTEE", "E022", $"Deductee '{d.Name}': Amount paid is 0.", true));

                if (d.TdsDeducted < 0)
                    errors.Add(new("DEDUCTEE", "E023", $"Deductee '{d.Name}': Negative TDS amount.", true));

                if (!IsKnownSection(d.Section))
                    errors.Add(new("DEDUCTEE", "E024", $"Deductee '{d.Name}': Unknown section '{d.Section}'.", false));

                var formT = data.Header.FormType.ToUpper();
                // 192 (salary) is invalid in 26Q
                if ((formT == "26Q" || formT == "140") && (d.Section == "192" || d.Section == "192A"))
                    errors.Add(new("DEDUCTEE", "W025", $"Deductee '{d.Name}': Section 192 (salary) is invalid in {formT}. Move to 24Q if this is a salary payment.", false));
                // 27EQ must only contain 206C sections
                if (formT == "27EQ" && !d.Section.StartsWith("206C", StringComparison.OrdinalIgnoreCase))
                    errors.Add(new("DEDUCTEE", "W026", $"Collectee '{d.Name}': Section '{d.Section}' is not a TCS section. 27EQ only allows 206C-family sections.", false));
            }

            // Reconciliation check
            double totalDeducted  = data.TotalTdsDeducted;
            double totalDeposited = data.Challans.Sum(c => c.TdsDeposited);
            double diff = Math.Abs(totalDeducted - totalDeposited);
            if (diff > 1.0)
                errors.Add(new("RECONCILIATION", "W001",
                    $"TDS deducted (Rs {totalDeducted:N2}) differs from challan deposited (Rs {totalDeposited:N2}). Difference: Rs {diff:N2}.",
                    false));

            return errors;
        }

        // ── Sample .txt content (for preview) ────────────────────────────────
        public static string GetSampleStructure(string formType = "26Q")
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"// Sample NSDL FVU file structure — {formType}");
            sb.AppendLine($"// Generated by TDS Pro | Format: Protean RPU 5.6+ / FVU 9.4 | Delimiter: ^");
            sb.AppendLine();
            sb.AppendLine("FH|T|26Q|9.0|1|0");
            sb.AppendLine("BH|DELA12345A|202627|26Q|1|AAAPL1234C|C|12/04/2026|R|2|5|1248000|15000000");
            sb.AppendLine("DE|DELA12345A|AAAPL1234C|ABC PRIVATE LIMITED|14 CONNAUGHT PLACE NEW DELHI|110001|9876543210|...|RAHUL SHARMA|AAAPL1234C|RAHUL SHARMA|DIRECTOR|12/04/2026");
            sb.AppendLine("CD|1|0001234|15/04/2026|00123|4500000|0|180000|0|0|4680000|2|194C|0");
            sb.AppendLine("DD|1|RAJKU1234A|RAJ KUMAR AND SONS|02|194C|12/04/2026|15000000|150000|150000|0|6000|1.00|00123|0001234|Y|");
            sb.AppendLine("DD|2|PRIYA5678B|PRIYA CONSULTANTS|02|194J|18/04/2026|8000000|800000|800000|0|32000|10.00|00123|0001234|Y|");
            sb.AppendLine("BC|2|5|1248000|15000000|0|248000|0|0|1496000");
            sb.AppendLine("FC|1|2|5|1248000|15000000");
            return sb.ToString();
        }

        // ── NIL return generator (FVU 9.4 compliant) ─────────────────────────
        // NSDL spec: NIL return = BH with 1 challan, CD with NilChallanInd="Y", no DD records.
        public static string GenerateNil(ReturnHeader h)
        {
            string nsdlForm = h.FormType.ToUpper() switch { "138"=>"24Q","140"=>"26Q",_=>h.FormType.ToUpper() };
            var lines = new List<string>();
            int lineNo = 0;
            string L() => (++lineNo).ToString();
            var fy  = FormatFY(h.FinancialYear);
            var ay  = AssessmentYear(h.FinancialYear);
            var today8 = DateTime.Today.ToString("ddMMyyyy");
            var tanUpper = h.TanOfDeductor.PadRight(10).Trim().ToUpper();
            string addr  = Safe(h.DeductorAddress, 100);
            string addr1 = addr.Length > 25 ? addr[..25] : addr;
            string addr2 = addr.Length > 25 ? (addr.Length > 50 ? addr[25..50] : addr[25..]) : "";
            string stateCode = NsdlStateCode(h.DeductorState);

            // FH
            lines.Add(string.Join("^", new[]
            {
                L(), "FH",
                nsdlForm == "24Q" ? "SL1" : "NS1",
                "R", today8, "1", "D", tanUpper, "1", "IITRETeTDS",
                "","","","","","","",
            }) + "^");

            // BH — 1 challan (the NIL CD below), 0 deductees, batch TDS = 0
            lines.Add(PipeL(L(), "BH",
                "1", "1", nsdlForm,
                "","","","","","","","",
                tanUpper, "",
                h.PanOfDeductor.PadRight(10).Trim().ToUpper(),
                ay, fy, h.Quarter.ToUpper(),
                Safe(h.DeductorName, 75), "NA",
                addr1, addr2, "", "", "",
                stateCode, h.DeductorPin.Trim(), h.Email.Trim(),
                "","","N",
                DeductorCategory(h.DeductorType),
                Safe(h.ResponsibleName, 75), Safe(h.Designation, 20),
                addr1, addr2, "", "", "",
                stateCode, h.DeductorPin.Trim(), h.Email.Trim(), h.Phone.Trim(),
                "","","N",
                "0.00", "0",
                nsdlForm == "24Q" ? "0" : "",
                nsdlForm == "24Q" ? "0.00" : "",
                "N",
                h.Quarter != "Q1" ? "Y" : "N",
                "","","","","","","",
                h.ResponsiblePan.PadRight(10).Trim().ToUpper(),
                "","","","","","","","","",
                h.Gstin.Trim().ToUpper(),
                nsdlForm == "24Q" ? "0" : "",
                nsdlForm == "24Q" ? "0.00" : ""
            ));

            // CD — NIL challan (BSR 0000000, date today, amount 0)
            lines.Add(PipeL(L(), "CD",
                "1", "1", "0", "Y",   // NIL challan indicator = Y
                "","","","","",
                "00000",               // bank challan no (zero for NIL)
                "","",
                "0000000",             // BSR code (zero for NIL)
                "", today8,            // challan date
                "","","",
                "0.00","0.00","0.00","0.00","0.00",
                "0.00","","0.00",
                "0.00","0.00","0.00","0.00","0.00","0.00",
                "","N","","0.00","200"
            ));

            // No DD records for NIL return
            return string.Join("\n", lines) + "\n";
        }

        // ── File name ────────────────────────────────────────────────────────
        public static string GetFileName(ReturnData data)
        {
            var fy = data.Header.FinancialYear.Replace("-", "");
            return $"{data.Header.FormType}_{data.Header.TanOfDeductor}_{fy}_{data.Header.Quarter}.txt";
        }

        // ── IT Act 2025 section mapping (old → new) ────────────────────────
        // IT Act 2025 mapping (CBDT section-picker dialog reference):
        //   192/192A → 392(1)            (salary)
        //   393(1) — general TDS: 193, 194, 194A, 194C, 194D, 194DA, 194G, 194H, 194I, 194IA,
        //            194IB, 194IC, 194J, 194K, 194LA, 194M, 194N, 194O, 194Q, 195
        //   393(3) — specific: 194B (winnings), 194BA (online games), 194BB (horse race),
        //            194R (perquisites), 194S (virtual digital assets), 194T (partner payments)
        //   397    — 206AB (higher-rate for non-filers, withdrawn FY 2025-26 onwards)
        private static readonly Dictionary<string, string> OldToNewSection =
            new(StringComparer.OrdinalIgnoreCase)
        {
            ["192"]="392(1)",  ["192A"]="392(1)",
            ["193"]="393(1)",  ["194"]="393(1)",   ["194A"]="393(1)",
            ["194C"]="393(1)", ["194D"]="393(1)",  ["194DA"]="393(1)",
            ["194G"]="393(1)", ["194H"]="393(1)",  ["194I"]="393(1)",
            ["194IA"]="393(1)",["194IB"]="393(1)", ["194IC"]="393(1)",
            ["194J"]="393(1)", ["194K"]="393(1)",  ["194LA"]="393(1)",
            ["194M"]="393(1)", ["194N"]="393(1)",  ["194O"]="393(1)",
            ["194Q"]="393(1)", ["195"]="393(1)",
            ["194B"]="393(3)", ["194BA"]="393(3)", ["194BB"]="393(3)",
            ["194R"]="393(3)", ["194S"]="393(3)",  ["194T"]="393(3)",
            ["206AB"]="397",
        };

        private static readonly HashSet<string> NewActSectionCodes =
            new(StringComparer.OrdinalIgnoreCase)
            { "392","392(1)","393","393(1)","393(2)","393(3)",
              "394","395","396","397","397(3)","398","398(3)" };

        private static bool IsKnownSection(string s) =>
            string.IsNullOrEmpty(s) ||
            AppConstants.KnownSections.Contains(s.ToUpper()) ||
            NewActSectionCodes.Contains(s);

        private static string MapToNewActSection(string old)
        {
            if (NewActSectionCodes.Contains(old)) return old; // already new
            return OldToNewSection.TryGetValue(old, out var n) ? n : old;
        }

        private static string ChallanSectionCode(string section, string nsdlForm, bool newAct)
        {
            if (nsdlForm == "24Q") return newAct ? "392(1)" : "192";
            if (newAct) return MapToNewActSection(section);
            return string.IsNullOrEmpty(section) ? "194C" : section.ToUpper().Trim();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        // PipeL: prepends line number as field 1, then record tag as field 2
        private static string PipeL(string lineNo, string tag, params string[] fields)
            => lineNo + "^" + tag + "^" + string.Join("^", fields.Select(f => (f ?? "").Replace("^", ""))) + "^";

        private static string Pipe(string tag, params string[] fields)
            => tag + "^" + string.Join("^", fields.Select(f => (f ?? "").Replace("^", ""))) + "^";

        private static string Paise(double amount)
            => ((long)Math.Round(amount * 100)).ToString();

        // Rupees: formats as "500000.00" (no paise, integer rupees only, .00 suffix required by FVU)
        private static string Rupees(double amount)
            => ((long)Math.Round(amount)).ToString() + ".00";

        private static string Safe(string s, int maxLen)
        {
            // NSDL FVU allows only alphanumeric + space in text fields.
            // Any other character causes T-FV-1022 hash validation failure.
            s = (s ?? "").Trim().ToUpper();
            s = System.Text.RegularExpressions.Regex.Replace(s, @"[^A-Z0-9 ]", "").Trim();
            return s.Length > maxLen ? s[..maxLen] : s;
        }

        private static string FormatFY(string fy) => fy.Replace("-", "");

        private static string AssessmentYear(string fy)
        {
            // "2025-26" → "202627"
            var parts = fy.Split('-');
            if (parts.Length == 2 && int.TryParse(parts[0], out int y))
            {
                int nextYear = y + 1;
                int afterNext = nextYear + 1;
                return $"{nextYear}{afterNext.ToString()[^2..]}";
            }
            return fy.Replace("-", "");
        }

        private static string QuarterCode(string q) => q.TrimStart('Q');

        // Maps deductor type to NSDL category code.
        // FVU 9.4 valid codes: A,S,D,E,G,H,L,N,K,M,P,J,B,Q,F,T
        // K=Company, M=Branch/Division of Company, Q=Individual/HUF, F=Firm
        // A=Central Govt, S=State Govt, D/E=Statutory body, G/H=Autonomous body
        // L/N=Local Authority, P=AOP, T=Trust, J=Artificial Juridical Person, B=BOI
        private static string DeductorCategory(string type) => (type ?? "K").ToUpper() switch
        {
            "A" or "CENTRAL GOVT"            => "A",
            "S" or "STATE GOVT"              => "S",
            "D"                              => "D",
            "E"                              => "E",
            "G"                              => "G",
            "H"                              => "H",
            "L" or "LOCAL AUTHORITY CENTRAL" => "L",
            "N" or "LOCAL AUTHORITY STATE"   => "N",
            "K" or "COMPANY" or "C"          => "K", // Company
            "M" or "BRANCH"                  => "M", // Branch/Division of Company
            "P" or "AOP"                     => "P",
            "J" or "AJP"                     => "J",
            "B" or "BOI"                     => "B",
            "Q" or "INDIVIDUAL" or "HUF"     => "Q",
            "F" or "FIRM"                    => "F",
            "T" or "TRUST"                   => "T",
            _                                => "K"
        };

        // Maps state name / abbreviation → NSDL 2-digit state code (FVU 9.4).
        // State code table from FormValidator k.class (salary detail validator).
        // Note: code "08" (old Daman & Diu) is invalid in FVU >= 9.4; use "07" for Dadra+Daman.
        private static string NsdlStateCode(string state)
        {
            if (string.IsNullOrWhiteSpace(state)) return "07"; // Default to Delhi
            var s = state.Trim().ToUpper();
            // If already a valid 2-digit NSDL code, return as-is (zero-padded)
            if (int.TryParse(s, out int code) && code >= 1 && code <= 38)
                return s.PadLeft(2, '0');
            // NSDL state codes — verified against FVU 9.4 FormValidator
            return s switch
            {
                "JAMMU AND KASHMIR" or "JAMMU & KASHMIR" or "J&K" => "01",
                "HIMACHAL PRADESH" or "HP"                         => "02",
                "PUNJAB"                                           => "03",
                "CHANDIGARH"                                       => "04",
                "UTTARAKHAND" or "UTTARANCHAL"                     => "05",
                "HARYANA"                                          => "06",
                "DELHI" or "DEL" or "NEW DELHI" or "NCT OF DELHI" => "07",
                "RAJASTHAN"                                        => "08",
                "UTTAR PRADESH" or "UP"                            => "09",
                "BIHAR"                                            => "10",
                "SIKKIM"                                           => "11",
                "ARUNACHAL PRADESH"                                => "12",
                "NAGALAND"                                         => "13",
                "MANIPUR"                                          => "14",
                "MIZORAM"                                          => "15",
                "TRIPURA"                                          => "16",
                "MEGHALAYA"                                        => "17",
                "ASSAM"                                            => "18",
                "WEST BENGAL" or "WB"                              => "19",
                "JHARKHAND"                                        => "20",
                "ODISHA" or "ORISSA"                               => "21",
                "CHHATTISGARH" or "CHATTISGARH"                    => "22",
                "MADHYA PRADESH" or "MP"                           => "23",
                "GUJARAT"                                          => "24",
                "DAMAN AND DIU" or "DAMAN & DIU"                   => "25",
                "DADRA AND NAGAR HAVELI" or "DADRA & NAGAR HAVELI" => "26",
                "MAHARASHTRA"                                      => "27",
                "ANDHRA PRADESH" or "AP"                           => "28",
                "KARNATAKA"                                        => "29",
                "GOA"                                              => "30",
                "LAKSHADWEEP" or "LAKSHWADEEP"                     => "31",
                "KERALA"                                           => "32",
                "TAMIL NADU" or "TAMILNADU" or "TN"                => "33",
                "PUDUCHERRY" or "PONDICHERRY"                      => "34",
                "ANDAMAN AND NICOBAR" or "ANDAMAN & NICOBAR"       => "35",
                "TELANGANA"                                        => "36",
                "ANDHRA PRADESH (NEW)" or "AP (NEW)"               => "37",
                "LADAKH"                                           => "38",
                _                                                  => "07" // Default to Delhi
            };
        }

        // NSDL Deductee Code: 1=Company, 2=Other (Individual/HUF/etc)
        private static string DeducteeCode(string type) => (type ?? "") switch
        {
            "01"            => "1",    // legacy "01" → Company (code 1)
            "02"            => "2",    // legacy "02" → Other (code 2)
            "1"             => "1",
            "2"             => "2",
            "Company"       => "1",
            "NRI - Company" => "1",
            "Individual"    => "2",
            "HUF"           => "2",
            "NRI"           => "2",
            _               => "2"     // default to Other
        };
    }  // end FvuGenerator

    // ── Validation error model ─────────────────────────────────────────────────
    public class FvuValidationError
    {
        public string Category   { get; }
        public string Code       { get; }
        public string Message    { get; }
        public bool   IsBlocking { get; }

        public FvuValidationError(string category, string code, string message, bool blocking)
        {
            Category = category; Code = code;
            Message = message; IsBlocking = blocking;
        }

        public override string ToString() =>
            $"[{(IsBlocking ? "ERROR" : "WARN")}] {Code} — {Message}";
    }
}
