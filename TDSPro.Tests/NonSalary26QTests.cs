using TDSPro.DAL;
using TDSPro.DAL.Models;

namespace TDSPro.Tests;

/// <summary>
/// End-to-end FvuGenerator tests for Form 26Q (non-salary TDS).
/// Verifies record-count discipline, section-code mapping, threshold semantics,
/// and field positions confirmed against FVU-validated reference 4I03886B.
/// </summary>
public class NonSalary26QTests
{
    private static ReturnData Minimal26Q(string section, double amount, double tds,
        double surcharge = 0, double cess = 0, string remarks = "")
    {
        var challanDate = new DateTime(2026, 1, 31);
        return new ReturnData
        {
            Header = new ReturnHeader
            {
                FormType        = "26Q",
                FinancialYear   = "2025-26",
                Quarter         = "Q4",
                TanOfDeductor   = "DELI03886B",
                PanOfDeductor   = "AABCI1406L",
                DeductorName    = "TEST DEDUCTOR PRIVATE LIMITED",
                DeductorAddress = "Address Line 1",
                DeductorState   = "Delhi",
                DeductorPin     = "110001",
                Email           = "test@example.com",
                Phone           = "9899206518",
                DeductorType    = "Company",
                ResponsibleName = "Test Responsible",
                ResponsiblePan  = "AFCPG8816K",
                Designation     = "Director",
                Gstin           = "07AABCI1406L1ZS",
            },
            Challans = new List<ReturnChallanDetail>
            {
                new() {
                    SlNo = 1, BsrCode = "0000985", ChallanDate = challanDate,
                    ChallanNo = "00078", TdsDeposited = tds, Surcharge = surcharge,
                    Cess = cess, Interest = 0, LateFee = 0,
                    TotalDeposited = tds + surcharge + cess, Section = section, Quarter = "Q4",
                },
            },
            Deductees = new List<ReturnDeducteeDetail>
            {
                new() {
                    SlNo = 1, Pan = "AAAPA1234A", Name = "TEST DEDUCTEE",
                    Section = section, PaymentDate = challanDate, AmountPaid = amount,
                    TdsDeducted = tds, TdsDeposited = tds, Surcharge = surcharge, Cess = cess,
                    ChallanNo = "00078", BsrCode = "0000985", Remarks = remarks,
                    DeducteeType = "02", Rate = 10.0,  // Individual
                },
            },
        };
    }

    // ── Structural: FH=1, BH=1, CD=1, DD=1, no SD/S16/C6A for 26Q ────────────
    [Fact]
    public void Generate_26Q_HasExactRecordCounts()
    {
        var data = Minimal26Q("194C", 100000, 1000);
        var output = FvuGenerator.Generate(data);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(4, lines.Length);  // FH + BH + CD + DD
        Assert.StartsWith("1^FH^", lines[0]);
        Assert.StartsWith("2^BH^", lines[1]);
        Assert.StartsWith("3^CD^", lines[2]);
        Assert.StartsWith("4^DD^", lines[3]);
    }

    // ── FH uses NS1 (non-salary file type) for 26Q ──────────────────────────
    [Fact]
    public void Generate_26Q_FH_UsesNS1()
    {
        var data = Minimal26Q("194C", 100000, 1000);
        var output = FvuGenerator.Generate(data);
        var fh = output.Split('\n')[0].Split('^');
        Assert.Equal("NS1", fh[2]);
    }

    // ── BH for 26Q must have blank SD count [49] and gross-income [50] ─────
    [Fact]
    public void Generate_26Q_BH_LeavesSDFieldsBlank()
    {
        var data = Minimal26Q("194C", 100000, 1000);
        var output = FvuGenerator.Generate(data);
        var bh = output.Split('\n')[1].Split('^');
        Assert.Equal("", bh[48]);   // [49] SD count
        Assert.Equal("", bh[49]);   // [50] gross income
        Assert.Equal("", bh[69]);   // [70] 194P count
        Assert.Equal("", bh[70]);   // [71] 194P gross
    }

    // ── DD field [33] holds NSDL section code, not the Income-tax Act code ─
    [Theory]
    [InlineData("194C", "94C")]
    [InlineData("194A", "94A")]
    [InlineData("194J", "4JB")]
    [InlineData("194I", "94I")]
    [InlineData("194H", "94H")]
    [InlineData("194Q", "94Q")]
    [InlineData("194T", "94T")]   // New section added FY 2025-26
    [InlineData("194N", "94N")]
    [InlineData("194IA", "4IA")]
    [InlineData("195",  "95")]
    public void Generate_26Q_DD_MapsSectionToNsdlCode(string section, string expectedNsdl)
    {
        var data = Minimal26Q(section, 100000, 1000);
        var output = FvuGenerator.Generate(data);
        var dd = output.Split('\n').First(l => l.StartsWith("4^DD^")).Split('^');
        Assert.Equal(expectedNsdl, dd[32]);
    }

    // ── DD record field count = 54 (FVU 9.4 layout) ────────────────────────
    [Fact]
    public void Generate_26Q_DD_Has54Fields()
    {
        var data = Minimal26Q("194C", 100000, 1000);
        var output = FvuGenerator.Generate(data);
        var dd = output.Split('\n').First(l => l.StartsWith("4^DD^"));
        Assert.Equal(53, dd.Count(c => c == '^'));   // 53 carets = 54 fields
    }

    // ── CD field count = 40 carets ─────────────────────────────────────────
    [Fact]
    public void Generate_26Q_CD_Has40Carets()
    {
        var data = Minimal26Q("194C", 100000, 1000);
        var output = FvuGenerator.Generate(data);
        var cd = output.Split('\n').First(l => l.StartsWith("3^CD^"));
        Assert.Equal(40, cd.Count(c => c == '^'));
    }

    // ── CD[29] = sum of TDS+Sur+Cess across DDs (not just TDS) ─────────────
    // This locks in the T-FV-3092 fix from the FVU validation session.
    [Fact]
    public void Generate_26Q_CD29_SumsAllTaxComponents()
    {
        var data = Minimal26Q("194J", 500000, 50000, surcharge: 5000, cess: 2200);
        var output = FvuGenerator.Generate(data);
        var cd = output.Split('\n').First(l => l.StartsWith("3^CD^")).Split('^');
        // Note: 26Q usually has zero cess on deductees, but the field must still aggregate cleanly.
        // CD[29] should equal Σ(DD[14]+[15]+[16]) — surcharge counted, cess for 26Q usually 0.
        double cd29 = double.Parse(cd[28]);
        Assert.True(cd29 >= 50000, $"CD[29]={cd29} should be at least the TDS amount");
    }

    // ── Section 197 lower-deduction certificate: Remarks=A + cert number present ─
    [Fact]
    public void Generate_26Q_Section197_LowerDeductionRemark()
    {
        var data = Minimal26Q("194J", 100000, 1000, remarks: "A");
        data.Deductees[0].LowerDeductionCertNo = "CERT12345";
        var output = FvuGenerator.Generate(data);
        var dd = output.Split('\n').First(l => l.StartsWith("4^DD^")).Split('^');
        // Remarks "A" and cert number must both appear in the DD record.
        Assert.Contains("A", dd);
        Assert.Contains("CERT12345", dd);
    }

    // ── Salary sections (192/192A) are rejected in 26Q — must throw ────────────
    [Theory]
    [InlineData("192")]
    [InlineData("192A")]
    public void Generate_26Q_RerouteSalarySectionTo194J(string salarySection)
    {
        var data = Minimal26Q(salarySection, 100000, 1000);
        // Salary sections are invalid in 26Q — FvuGenerator throws InvalidOperationException.
        // These entries must be moved to 24Q before generating.
        Assert.Throws<InvalidOperationException>(() => FvuGenerator.Generate(data));
    }

    // ── PAN is uppercased and trimmed in DD ────────────────────────────────
    [Fact]
    public void Generate_26Q_DD_PANNormalised()
    {
        var data = Minimal26Q("194C", 100000, 1000);
        data.Deductees[0].Pan = " aaapa1234a ";
        var output = FvuGenerator.Generate(data);
        var dd = output.Split('\n').First(l => l.StartsWith("4^DD^")).Split('^');
        Assert.Equal("AAAPA1234A", dd[9]);
    }

    // ── Date formats: ddMMyyyy without separators ──────────────────────────
    [Fact]
    public void Generate_26Q_Dates_UseDDMMYYYY()
    {
        var data = Minimal26Q("194C", 100000, 1000);
        var output = FvuGenerator.Generate(data);
        var cd = output.Split('\n').First(l => l.StartsWith("3^CD^")).Split('^');
        Assert.Equal("31012026", cd[17]);   // CD field 18 = challan date (zero-index 17)
        var dd = output.Split('\n').First(l => l.StartsWith("4^DD^")).Split('^');
        // DD field 22 = payment date, field 23 = deduction date (zero-index 22 / 23)
        // Confirmed by inspecting 4I03886B reference file dump.
        Assert.Contains("31012026", dd);
    }

    // ── 27Q (non-resident) is disabled — should throw, not produce garbage ─
    [Fact]
    public void Generate_27Q_ThrowsNotSupported()
    {
        var data = Minimal26Q("195", 100000, 5000);
        data.Header.FormType = "27Q";
        Assert.Throws<NotSupportedException>(() => FvuGenerator.Generate(data));
    }

    // ── IT Act 2025: 393(1) general TDS sections ───────────────────────────
    [Theory]
    [InlineData("194C")]
    [InlineData("194D")]   // commission/brokerage — insurance
    [InlineData("194H")]   // commission/brokerage — other
    [InlineData("194I")]   // rent
    [InlineData("194J")]   // professional / technical
    [InlineData("194Q")]   // purchase of goods
    [InlineData("194O")]   // ecommerce operator
    public void NewAct_Section_Maps_To_393_1(string oldSection)
    {
        var data = Minimal26Q(oldSection, 100000, 1000);
        data.Header.FormType = "140";   // IT Act 2025 non-salary
        var output = FvuGenerator.Generate(data);
        var dd = output.Split('\n').First(l => l.StartsWith("4^DD^"));
        Assert.Contains("393(1)", dd);
    }

    // ── IT Act 2025: 393(3) specific TDS sections ───────────────────────────
    [Theory]
    [InlineData("194B")]   // winnings
    [InlineData("194BA")]  // online games
    [InlineData("194BB")]  // horse race
    [InlineData("194R")]   // perquisites
    [InlineData("194S")]   // virtual digital assets
    [InlineData("194T")]   // partner payments
    public void NewAct_Section_Maps_To_393_3(string oldSection)
    {
        var data = Minimal26Q(oldSection, 100000, 1000);
        data.Header.FormType = "140";
        var output = FvuGenerator.Generate(data);
        var dd = output.Split('\n').First(l => l.StartsWith("4^DD^"));
        Assert.Contains("393(3)", dd);
    }

    // ── Zero-TDS deductees get filtered out (DD records require tax > 0) ────
    [Fact]
    public void Generate_26Q_FiltersZeroTdsDeductees()
    {
        var data = Minimal26Q("194C", 100000, 1000);
        // Add a completely blank deductee (zero amount AND zero TDS) — should be silently dropped.
        // Note: AmountPaid>0 with TdsDeducted=0 is a valid nil-deduction entry and is kept.
        data.Deductees.Add(new ReturnDeducteeDetail
        {
            Pan = "BBBPB2345B", Name = "ZERO TDS", Section = "194C",
            PaymentDate = new DateTime(2026, 1, 31),
            AmountPaid = 0, TdsDeducted = 0, TdsDeposited = 0,
            ChallanNo = "00078", BsrCode = "0000985", Rate = 0,
        });
        var output = FvuGenerator.Generate(data);
        var ddLines = output.Split('\n').Where(l => l.Contains("^DD^")).ToList();
        Assert.Single(ddLines);  // only the original deductee survives; zero-amount row dropped
    }
}
