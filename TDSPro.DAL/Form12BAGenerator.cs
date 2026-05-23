using TDSPro.DAL.Models;

namespace TDSPro.DAL
{
    /// <summary>
    /// Form 12BA — Statement of perquisites and profits in lieu of salary (Section 17(2)).
    /// Annexure to Form 16 Part B, required when employer provides perquisites.
    /// Pulls data from salary_line_items (category='perq') aggregated across the FY.
    /// </summary>
    public static class Form12BAGenerator
    {
        public static Form12BAData Build(int deductorId, int employeeId, string fy)
        {
            var data = new Form12BAData { FinancialYear = fy };
            using var conn = Database.GetConnection();

            // Deductor
            using var d1 = conn.CreateCommand();
            d1.CommandText = "SELECT * FROM deductors WHERE id=@id";
            d1.Parameters.AddWithValue("@id", deductorId);
            using var r1 = d1.ExecuteReader();
            if (r1.Read())
            {
                data.EmployerName    = r1["company_name"]?.ToString() ?? "";
                data.EmployerAddress = (r1["address"]?.ToString() ?? "") + ", " + (r1["city"]?.ToString() ?? "") + " - " + (r1["pincode"]?.ToString() ?? "");
                data.EmployerTan     = r1["tan"]?.ToString() ?? "";
                data.EmployerTdsCircle= r1["tax_circle"]?.ToString() ?? "";
            }
            r1.Close();

            // Employee
            using var d2 = conn.CreateCommand();
            d2.CommandText = "SELECT * FROM employees WHERE id=@id";
            d2.Parameters.AddWithValue("@id", employeeId);
            using var r2 = d2.ExecuteReader();
            if (r2.Read())
            {
                data.EmployeeName        = r2["name"]?.ToString() ?? "";
                data.EmployeePan         = r2["pan"]?.ToString() ?? "";
                data.EmployeeDesignation = r2["designation"]?.ToString() ?? "";
            }
            r2.Close();

            // Aggregate perquisite line items across all months in this FY
            using var d3 = conn.CreateCommand();
            d3.CommandText = @"
                SELECT li.name, li.rule_ref, SUM(li.taxable) AS amt_taxable, SUM(li.exempt) AS amt_exempt
                FROM salary_line_items li
                JOIN monthly_salary_entries m ON m.id = li.monthly_salary_entry_id
                WHERE li.category='perq'
                  AND m.employee_id=@e AND m.financial_year=@f
                GROUP BY li.name, li.rule_ref
                ORDER BY li.name";
            d3.Parameters.AddWithValue("@e", employeeId);
            d3.Parameters.AddWithValue("@f", fy);
            using var r3 = d3.ExecuteReader();
            while (r3.Read())
            {
                var taxable = r3.IsDBNull(2) ? 0 : Convert.ToDouble(r3[2]);
                var exempt  = r3.IsDBNull(3) ? 0 : Convert.ToDouble(r3[3]);
                data.Perquisites.Add(new Form12BAPerquisite
                {
                    Name        = r3.GetString(0),
                    RuleRef     = r3.IsDBNull(1) ? "" : r3.GetString(1),
                    Value       = taxable + exempt,   // value of perquisite
                    Recovered   = exempt,              // amount recovered/exempt
                    Chargeable  = taxable,             // taxable portion
                });
            }
            r3.Close();

            data.TotalChargeable = data.Perquisites.Sum(p => p.Chargeable);
            return data;
        }

        public static string RenderHtml(Form12BAData d)
        {
            var rows = string.Join("", d.Perquisites.Select((p, i) => $@"
                <tr>
                    <td style='text-align:center'>{i + 1}</td>
                    <td>{p.Name}</td>
                    <td>{p.RuleRef}</td>
                    <td style='text-align:right'>{p.Value:N2}</td>
                    <td style='text-align:right'>{p.Recovered:N2}</td>
                    <td style='text-align:right'>{p.Chargeable:N2}</td>
                </tr>"));

            return $@"<!DOCTYPE html><html><head><meta charset='utf-8'/><title>Form 12BA — {d.EmployeeName}</title>
<style>
body{{font-family:Arial,sans-serif;font-size:11px;margin:18px}}
h2,h3{{text-align:center;margin:6px 0}}
table{{width:100%;border-collapse:collapse;margin:10px 0}}
th,td{{border:1px solid #333;padding:5px 8px;vertical-align:top}}
th{{background:#e2e8f0;text-align:left}}
.hdr td{{border:none;padding:2px 6px}}
.sig{{margin-top:30px;display:flex;justify-content:space-between}}
</style></head><body>
<h2>FORM No. 12BA</h2>
<h3>(See rule 26A(2)(b))</h3>
<p style='text-align:center'><em>Statement showing particulars of perquisites, other fringe benefits or amenities and profits in lieu of salary with value thereof</em></p>

<table>
  <tr class='hdr'><td style='width:35%'><strong>1. Name and address of employer</strong></td><td>{d.EmployerName}<br/>{d.EmployerAddress}</td></tr>
  <tr class='hdr'><td><strong>2. TAN</strong></td><td>{d.EmployerTan}</td></tr>
  <tr class='hdr'><td><strong>3. TDS Assessment Range</strong></td><td>{d.EmployerTdsCircle}</td></tr>
  <tr class='hdr'><td><strong>4. Name, designation and PAN of employee</strong></td><td>{d.EmployeeName}, {d.EmployeeDesignation}<br/>PAN: {d.EmployeePan}</td></tr>
  <tr class='hdr'><td><strong>5. Is the employee a director or person with substantial interest in the company (where employer is a company)?</strong></td><td>No</td></tr>
  <tr class='hdr'><td><strong>6. Income under the head ""Salaries"" (other than from perquisites)</strong></td><td>As per Form 16 Part B</td></tr>
  <tr class='hdr'><td><strong>7. Financial year</strong></td><td>{d.FinancialYear}</td></tr>
  <tr class='hdr'><td><strong>8. Valuation of Perquisites</strong></td><td></td></tr>
</table>

<table>
  <thead>
    <tr>
      <th style='width:5%'>S.No.</th>
      <th style='width:35%'>Nature of perquisite (see rule 3)</th>
      <th style='width:20%'>Rule / Section</th>
      <th style='width:13%;text-align:right'>Value of perquisite (₹)</th>
      <th style='width:13%;text-align:right'>Amount recovered from employee (₹)</th>
      <th style='width:14%;text-align:right'>Chargeable to tax (₹)</th>
    </tr>
  </thead>
  <tbody>
    {(d.Perquisites.Count == 0 ? "<tr><td colspan='6' style='text-align:center;color:#64748b'>No perquisites recorded for this employee in this FY.</td></tr>" : rows)}
    <tr style='font-weight:700;background:#f1f5f9'>
      <td colspan='5' style='text-align:right'>Total value of perquisites</td>
      <td style='text-align:right'>{d.TotalChargeable:N2}</td>
    </tr>
  </tbody>
</table>

<p><strong>9. Details of tax</strong> — included in Form 16 Part B.</p>

<div style='margin-top:20px'>
  <p><strong>DECLARATION BY EMPLOYER</strong></p>
  <p>I, _________________, working as ____________ (designation), do hereby declare on behalf of {d.EmployerName} that the information given above is based on the books of account, documents and other relevant records or information available with us and the details of value of each such perquisite are in accordance with section 17 and rules framed thereunder and that such information is true and correct.</p>
</div>
<div class='sig'>
  <div>Place: ____________<br/>Date: ____________</div>
  <div>Signature of the person responsible for deduction of tax<br/><br/>Full Name: ____________<br/>Designation: ____________</div>
</div>
</body></html>";
        }

        public static string SaveHtml(Form12BAData d, string outputDir)
        {
            Directory.CreateDirectory(outputDir);
            var safeName = string.Concat((d.EmployeeName ?? "employee").Split(Path.GetInvalidFileNameChars()));
            var path = Path.Combine(outputDir, $"Form12BA_{safeName}_{d.FinancialYear}.html");
            File.WriteAllText(path, RenderHtml(d), System.Text.Encoding.UTF8);
            return path;
        }
    }

    public class Form12BAData
    {
        public string EmployerName    { get; set; } = "";
        public string EmployerAddress { get; set; } = "";
        public string EmployerTan     { get; set; } = "";
        public string EmployerTdsCircle{get; set; } = "";
        public string EmployeeName    { get; set; } = "";
        public string EmployeePan     { get; set; } = "";
        public string EmployeeDesignation { get; set; } = "";
        public string FinancialYear   { get; set; } = "";
        public List<Form12BAPerquisite> Perquisites { get; set; } = new();
        public double TotalChargeable { get; set; }
    }

    public class Form12BAPerquisite
    {
        public string Name       { get; set; } = "";
        public string RuleRef    { get; set; } = "";
        public double Value      { get; set; }
        public double Recovered  { get; set; }
        public double Chargeable { get; set; }
    }
}
