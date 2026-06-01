namespace CDeTDS.DAL.Models
{
    public class Deductor
    {
        public int Id { get; set; }
        public string CompanyName { get; set; } = "";
        public string Tan { get; set; } = "";
        public string Pan { get; set; } = "";
        public string Address { get; set; } = "";
        public string City { get; set; } = "";
        public string State { get; set; } = "";
        public string Pincode { get; set; } = "";
        public string ContactPerson { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string FinancialYear { get; set; } = "2024-25";
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        // Responsible person for return filing
        public string ResponsibleName { get; set; } = "";
        public string ResponsiblePan  { get; set; } = "";
        public string Designation     { get; set; } = "";
        public string Gstin           { get; set; } = "";
        public string DeductorType    { get; set; } = "Company";
        // Bank defaults — auto-filled when adding challans
        public string DefaultBsrCode  { get; set; } = "";
        public string DefaultBankName { get; set; } = "";
        // Portal credentials (encrypted at storage layer)
        public string CpcPassword { get; set; } = "";   // TRACES (TAN login)
        public string ItPassword  { get; set; } = "";   // incometax.gov.in (PAN login)
    }

    public class Deductee
    {
        public int Id { get; set; }
        public string DeducteeCode { get; set; } = "";
        public string Name { get; set; } = "";
        public string Pan { get; set; } = "";
        public string Section { get; set; } = "";
        public double Rate { get; set; }
        public string DeducteeType { get; set; } = "Individual";
        public bool IsResident { get; set; } = true;
        public bool ItrFiled { get; set; } = true;
        public string LowerCertNo { get; set; } = "";
        public double LowerCertRate { get; set; }
        public string LowerCertTill { get; set; } = "";
        public string Remarks { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // PAN verification
        public bool   PanVerified           { get; set; }
        public string PanVerificationStatus { get; set; } = "";  // Valid/Invalid/Deactivated/NotFound/FormatError
        public string PanVerifiedName       { get; set; } = "";  // Name returned by IT authority
        public string PanVerifiedAt         { get; set; } = "";  // ISO datetime of last check
    }

    public class TdsEntry
    {
        public int Id { get; set; }
        public string EntryNo { get; set; } = "";
        public DateTime EntryDate { get; set; } = DateTime.Today;
        public int DeductorId { get; set; }
        public int DeducteeId { get; set; }
        public string Section { get; set; } = "";
        public string PaymentNature { get; set; } = "";
        public double Amount { get; set; }
        public double Rate { get; set; }
        public double TdsAmount { get; set; }
        public double Surcharge { get; set; }
        public double Cess { get; set; }
        public double TotalTds { get; set; }
        public DateTime? DueDate { get; set; }
        public DateTime? PaymentDate { get; set; }
        public double Interest { get; set; }
        public double LateFee { get; set; }
        public string ChallanNo { get; set; } = "";
        public string Remarks { get; set; } = "";
        public string Status { get; set; } = "Pending";
        public string FinancialYear { get; set; } = "2024-25";
        public string Quarter { get; set; } = "Q1";
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation (joined)
        public string DeductorName { get; set; } = "";
        public string DeducteeName { get; set; } = "";
        public string DeducteePan { get; set; } = "";
    }

    public class Challan
    {
        public int Id { get; set; }
        public string ChallanNo { get; set; } = "";
        public DateTime ChallanDate { get; set; } = DateTime.Today;
        public int? DeductorId { get; set; }
        public string BsrCode { get; set; } = "";
        public string Section { get; set; } = "";
        public double Amount { get; set; }
        public double TdsAmount { get; set; }
        public double Surcharge { get; set; }
        public double Cess { get; set; }
        public double Interest { get; set; }
        public double LateFee { get; set; }
        public double TotalAmount { get; set; }
        public string BankName { get; set; } = "";
        public string AckNo { get; set; } = "";
        public string Quarter { get; set; } = "Q1";
        public string FinancialYear { get; set; } = "2024-25";
        public string Status { get; set; } = "Paid";
        public string Remarks { get; set; } = "";
        public string MinorHeadCode { get; set; } = "200";   // 200=TDS payable, 400=regular assessment
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation
        public string DeductorName { get; set; } = "";
    }

    public class DashboardStats
    {
        public int TotalEntries { get; set; }
        public double TotalTds { get; set; }
        public int TotalChallans { get; set; }
        public int TotalDeductees { get; set; }
        public int PendingEntries { get; set; }
        public int TotalDeductors { get; set; }
        public double TotalAmount { get; set; }
    }
}
