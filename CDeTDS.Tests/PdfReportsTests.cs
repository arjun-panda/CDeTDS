using QuestPDF.Fluent;
using CDeTDS.DAL;
using CDeTDS.DAL.Models;

namespace CDeTDS.Tests;

/// <summary>
/// Smoke tests: verify each PDF generator produces a non-empty, valid-looking PDF byte stream
/// for sample input. Doesn't verify pixel layout — but catches "report crashes on real data".
/// </summary>
public class PdfReportsTests
{
    public PdfReportsTests()
    {
        PdfReports.Initialize();   // ensure QuestPDF community licence set
    }

    private static byte[] BuildSampleSlip()
    {
        var entry = new MonthlySalaryEntry
        {
            EmployeeName = "Ajay Sharma", Pan = "ABCDE1234F",
            Month = 5, Year = 2026, FinancialYear = "2026-27",
            Basic = 30000, HRA = 15000, SpecialAllowance = 5000,
            PfEmployee = 1800, ProfessionalTax = 200,
            TdsDeducted = 0,
        };
        entry.RecalcGross();
        var emp = new Employee { EmployeeCode = "EMP-001", Name = "Ajay Sharma", Pan = "ABCDE1234F", Designation = "Engineer", BankAccount = "1234567890" };
        var ded = new Deductor { CompanyName = "Acme Corp" };
        var tmp = Path.Combine(Path.GetTempPath(), "cdetds_test_" + Guid.NewGuid().ToString("N"));
        var path = SalarySlipExport.GeneratePdf(entry, new AnnualComputation(), emp, ded, tmp);
        var bytes = File.ReadAllBytes(path);
        // Cleanup
        try { File.Delete(path); Directory.Delete(tmp); } catch { }
        return bytes;
    }

    [Fact]
    public void SalarySlip_Pdf_Produces_Valid_File()
    {
        var bytes = BuildSampleSlip();
        Assert.True(bytes.Length > 1000, "PDF is too small to be valid");
        // PDF files start with "%PDF-"
        Assert.Equal((byte)'%', bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'D', bytes[2]);
        Assert.Equal((byte)'F', bytes[3]);
    }

    [Fact]
    public void ComputationOfIncome_Pdf_Produces_Valid_File()
    {
        var data = new IncomeComputationData
        {
            EmployerName = "Acme Corp",
            EmployeeName = "Ajay Sharma",
            EmployeePan  = "ABCDE1234F",
            EmployeeDob  = "15-Jan-1985",
            FinancialYear = "2026-27",
            Regime = "New",
            GrossSalary = 600000, NetTaxableSalary = 600000,
            StdDeduction = 75000, IncomeFromSalary = 525000, TaxableIncome = 525000,
            TaxOnIncome = 6250, Rebate87A = 6250, Cess = 0, TotalTax = 0,
        };
        data.Lines.Add(new IncomeLine { Name = "Basic", Annual = 360000 });
        data.Lines.Add(new IncomeLine { Name = "HRA",   Annual = 180000 });

        var tmp = Path.Combine(Path.GetTempPath(), "cdetds_test_" + Guid.NewGuid().ToString("N"));
        var path = IncomeComputationGenerator.SavePdf(data, tmp);
        var bytes = File.ReadAllBytes(path);
        try { File.Delete(path); Directory.Delete(tmp); } catch { }

        Assert.True(bytes.Length > 1000);
        Assert.Equal((byte)'%', bytes[0]);
    }

    [Fact]
    public void Form16_Pdf_Produces_Valid_File()
    {
        var data = new Form16SalaryData
        {
            FinancialYear = "2026-27",
            DeductorName  = "Acme Corp",
            DeductorTan   = "DELA12345A",
            DeductorPan   = "ABCDE9876Z",
            EmployeeName  = "Ajay Sharma",
            EmployeePan   = "ABCDE1234F",
            EmployeeCode  = "EMP-001",
            Designation   = "Engineer",
            TaxRegime     = "New",
            GrossSalary   = 600000,
            StandardDeduction = 75000,
            TaxableIncome = 525000,
            TaxOnIncome   = 6250,
            Rebate87A     = 6250,
            Cess          = 0,
            TotalAnnualTax = 0,
            TdsDeducted    = 0,
        };

        var tmp = Path.Combine(Path.GetTempPath(), "cdetds_test_" + Guid.NewGuid().ToString("N"));
        var path = Form16Generator.SaveForm16SalaryPdf(data, tmp);
        var bytes = File.ReadAllBytes(path);
        try { File.Delete(path); Directory.Delete(tmp); } catch { }

        Assert.True(bytes.Length > 1000);
        Assert.Equal((byte)'%', bytes[0]);
    }

    [Fact]
    public void PdfReports_BuildA4_BasicRender()
    {
        // Just verify the helper itself works in isolation
        var bytes = PdfReports.BuildA4("Test", c => c.Text("Hello world"));
        Assert.True(bytes.Length > 500);
        Assert.Equal((byte)'%', bytes[0]);
    }

    private static AnnualComputation SampleAnnual()
    {
        var old = new RegimeResult
        {
            RegimeName = "Old", GrossSalary = 1404000, HraExemption = 228720,
            StandardDeduction = 50000, Chapter6A = 225000, TotalIncome = 720280,
            TaxOnIncome = 56556, TaxAfterRebate = 56556, Cess = 2262, TotalTax = 58818,
            Sec10Items = { new Sec10Item { Name = "Conveyance", RuleRef = "10(14)(i)", OldRegime = 180000, NewRegime = 180000 } },
        };
        var nw = new RegimeResult
        {
            RegimeName = "New", GrossSalary = 1404000, StandardDeduction = 75000,
            TotalIncome = 1149000, TaxOnIncome = 54900, Rebate87A = 54900,
            Sec10Items = { new Sec10Item { Name = "Conveyance", RuleRef = "10(14)(i)", OldRegime = 180000, NewRegime = 180000 } },
        };
        return new AnnualComputation { OldRegime = old, NewRegime = nw, ChosenRegime = "Old" };
    }

    private static EmployeeYearSummary SampleYearSummary()
    {
        var ys = new EmployeeYearSummary { EmployeeId = 1, EmployeeName = "Ajay Sharma" };
        for (int m = 4; m <= 12; m++)
            ys.MonthlyRuns[m] = new PayrollRun
            {
                Month = m, Year = 2025, GrossSalary = 117000,
                TdsDeducted = 5000, PfEmployee = 1800,
            };
        return ys;
    }

    [Fact]
    public void AnnualComputation_Pdf_Produces_Valid_File()
    {
        var emp = new Employee { EmployeeCode = "EMP-001", Name = "Ajay Sharma", Pan = "ABCDE1234F", Designation = "Engineer" };
        var ded = new Deductor { CompanyName = "Acme Corp", Tan = "DELA12345A" };

        var tmp = Path.Combine(Path.GetTempPath(), "cdetds_test_" + Guid.NewGuid().ToString("N"));
        var path = SalarySlipExport.GenerateAnnualPdf(SampleAnnual(), emp, "2025-26", tmp, ded, SampleYearSummary());
        var bytes = File.ReadAllBytes(path);
        try { File.Delete(path); Directory.Delete(tmp); } catch { }

        Assert.True(bytes.Length > 1000);
        Assert.Equal((byte)'%', bytes[0]);
    }

    [Fact]
    public void MonthlySalaryStatement_Pdf_Produces_Valid_File()
    {
        var emp = new Employee { EmployeeCode = "EMP-001", Name = "Ajay Sharma", Pan = "ABCDE1234F" };
        var ded = new Deductor { CompanyName = "Acme Corp" };
        string[] mn = { "Apr","May","Jun","Jul","Aug","Sep" };
        var rows = mn.Select(m => new MonthlySalaryStatRow(
            MonthLabel: m,
            Basic: 60000, Hra: 30000, Da: 0, Special: 12000, Medical: 0, Lta: 0,
            Bonus: 5000, Commission: 0, AdvanceSalary: 0, Arrears: 0,
            NpsEmployer: 0, PerqTaxable: 0, LeaveEncTaxable: 0,
            Other: 10000, GrossTotal: 117000,
            HraEx: 19060, OtherEx: 15000, TotalEx: 34060,
            NetTaxableSalary: 82940,
            StdDed: 4167, ProfTax: 200, Chap6A: 18750, Nps80CCD2: 0, TotalDed: 23117,
            OtherSources: 0, NetTaxableIncome: 59823,
            TaxOnIncome: 4713, Rebate87A: 0, TaxAfterRebate: 4713,
            Surcharge: 0, Cess: 189, TotalTax: 4902, TdsDeducted: 5000,
            Pf: 1800, Esi: 0, NetPay: 110000)).ToList();

        var tmp = Path.Combine(Path.GetTempPath(), "cdetds_test_" + Guid.NewGuid().ToString("N"));
        var path = SalarySlipExport.GenerateMonthlySalaryPdf(emp, "2025-26", tmp, ded, null, rows);
        var bytes = File.ReadAllBytes(path);
        try { File.Delete(path); Directory.Delete(tmp); } catch { }

        Assert.True(bytes.Length > 1000);
        Assert.Equal((byte)'%', bytes[0]);
    }
}
