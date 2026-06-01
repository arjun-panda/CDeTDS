namespace CDeTDS.DAL.Models
{
    /// <summary>
    /// Dynamic TDS Rules Engine — one row per effective rule.
    /// Rates are stored here, never hardcoded in C# code.
    /// </summary>
    public class TdsRule
    {
        public int    Id              { get; set; }
        public string SectionCode     { get; set; } = "";
        public string NatureOfPayment { get; set; } = "";
        public string DeducteeType    { get; set; } = "All";  // Individual/Company/NRI/All
        public bool   IsResident      { get; set; } = true;
        public double ThresholdLimit  { get; set; }           // Annual threshold
        public double TdsRate         { get; set; }           // Percentage
        public double SurchargeRate   { get; set; }           // Percentage on TDS
        public double CessRate        { get; set; }            // Health & Education Cess — explicit per rule, never assumed
        public DateTime EffectiveFrom { get; set; }
        public DateTime? EffectiveTo  { get; set; }           // null = currently active
        public string ReferenceAct    { get; set; } = "";     // e.g. "Income-tax Act 2025 s.194C"
        public string Notes           { get; set; } = "";
        public bool   IsActive        { get; set; } = true;
    }

    /// <summary>
    /// Result returned by the rules engine for a given transaction.
    /// </summary>
    public class TdsCalculationResult
    {
        public string SectionCode       { get; set; } = "";
        public string NatureOfPayment   { get; set; } = "";
        public double GrossAmount       { get; set; }
        public double ApplicableRate    { get; set; }
        public double TdsAmount         { get; set; }
        public double SurchargeAmount   { get; set; }
        public double CessAmount        { get; set; }
        public double TotalTds          { get; set; }
        public bool   PanAvailable      { get; set; }
        public bool   HigherRateApplied { get; set; }  // 206AA / 206AB
        public string HigherRateReason  { get; set; } = "";
        public bool   BelowThreshold    { get; set; }
        public string RuleApplied       { get; set; } = "";
        public List<string> Warnings    { get; set; } = new();
    }

    /// <summary>
    /// TRACES / FVU file format configuration — no hardcoded layout.
    /// </summary>
    public class FvuFormatConfig
    {
        public int    Id          { get; set; }
        public string ConfigKey   { get; set; } = "";  // e.g. FVU_FORMAT_26Q
        public string FormType    { get; set; } = "";  // 26Q / 24Q
        public string Version     { get; set; } = "";  // e.g. 9.0
        public string FieldLayout { get; set; } = "";  // JSON field definitions
        public string Delimiter   { get; set; } = "^";
        public DateTime EffectiveFrom { get; set; }
        public string Notes       { get; set; } = "";
    }

    /// <summary>
    /// Audit trail entry.
    /// </summary>
    public class AuditEntry
    {
        public int    Id        { get; set; }
        public string Username  { get; set; } = "";
        public string Action    { get; set; } = "";
        public string Module    { get; set; } = "";
        public string Details   { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}
