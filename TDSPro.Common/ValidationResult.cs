namespace CDeTDS.Common
{
    /// <summary>
    /// Outcome of a validation pass. Aggregates all errors for one entity so the UI
    /// can show them together instead of one-at-a-time.
    /// </summary>
    public class ValidationResult
    {
        public List<string> Errors  { get; } = new();
        public List<string> Warnings{ get; } = new();
        public bool         Ok      => Errors.Count == 0;

        public void AddError(string msg)
        {
            if (!string.IsNullOrWhiteSpace(msg)) Errors.Add(msg.Trim());
        }
        public void AddWarning(string msg)
        {
            if (!string.IsNullOrWhiteSpace(msg)) Warnings.Add(msg.Trim());
        }
        public void Merge(ValidationResult other)
        {
            if (other == null) return;
            Errors.AddRange(other.Errors);
            Warnings.AddRange(other.Warnings);
        }

        public string ErrorSummary   => string.Join("; ", Errors);
        public string WarningSummary => string.Join("; ", Warnings);

        public static ValidationResult Pass => new();
    }
}
