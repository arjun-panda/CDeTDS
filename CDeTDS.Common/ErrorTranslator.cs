namespace CDeTDS.Common
{
    /// <summary>
    /// Converts raw exceptions into user-friendly messages.
    /// Without this, the UI shows things like "SQLite Error 19: UNIQUE constraint failed:
    /// employees.pan" — confusing and scary. With this, the user sees: "An employee with
    /// this PAN already exists."
    ///
    /// Rules:
    ///   - Recognises common SQLite error codes (UNIQUE, NOT NULL, FK, BUSY, READONLY)
    ///   - Recognises File I/O exceptions (not found, denied, in use)
    ///   - Falls back to a clean version of the raw message if nothing matches
    ///   - Never returns null or empty
    /// </summary>
    public static class ErrorTranslator
    {
        /// <summary>Translate any exception into a single user-friendly line.</summary>
        public static string UserFriendly(Exception? ex)
        {
            if (ex == null) return "Unknown error.";
            // Walk inner exceptions — innermost is usually the most specific (real DB error)
            var inner = ex;
            while (inner.InnerException != null) inner = inner.InnerException;

            var msg     = inner.Message ?? "";
            var msgLow  = msg.ToLowerInvariant();
            var typeName = inner.GetType().Name;

            // ── SQLite specific patterns (the .Message contains the keyword) ─────
            if (msgLow.Contains("unique constraint"))
            {
                // Try to extract column name: "UNIQUE constraint failed: employees.pan"
                var field = ExtractAfter(msgLow, "failed:");
                if (!string.IsNullOrEmpty(field))
                    return $"A record with the same {PrettifyField(field)} already exists.";
                return "A record with the same key already exists.";
            }
            if (msgLow.Contains("not null constraint"))
            {
                var field = ExtractAfter(msgLow, "failed:");
                return string.IsNullOrEmpty(field)
                    ? "A required field is missing."
                    : $"Required field '{PrettifyField(field)}' is missing.";
            }
            if (msgLow.Contains("foreign key constraint"))
                return "Cannot complete — this record is referenced by other data. Remove the dependents first.";
            if (msgLow.Contains("database is locked") || msgLow.Contains("sqlite_busy"))
                return "Database is busy — another operation is in progress. Wait a moment and try again.";
            if (msgLow.Contains("readonly") || msgLow.Contains("attempt to write a readonly database"))
                return "Database file is read-only. Check folder permissions.";
            if (msgLow.Contains("disk i/o error") || msgLow.Contains("disk is full"))
                return "Disk error — check that the drive has space and isn't disconnected.";
            if (msgLow.Contains("no such table") || msgLow.Contains("no such column"))
                return "Database schema mismatch — restart the app to run migrations.";

            // ── File I/O ─────────────────────────────────────────────────────────
            if (typeName == "FileNotFoundException")
                return $"File not found: {ExtractPath(msg)}";
            if (typeName == "DirectoryNotFoundException")
                return $"Folder not found: {ExtractPath(msg)}";
            if (typeName == "UnauthorizedAccessException")
                return "Access denied — the file is in use or you don't have permission.";
            if (typeName == "IOException")
            {
                if (msgLow.Contains("being used by another process"))
                    return "File is open in another program. Close it and try again.";
                if (msgLow.Contains("there is not enough space"))
                    return "Drive is out of space.";
                return $"File operation failed: {msg}";
            }

            // ── Network ──────────────────────────────────────────────────────────
            if (typeName == "HttpRequestException" || typeName == "SocketException")
                return "Network error — check your internet connection.";
            if (typeName == "TaskCanceledException" || typeName == "OperationCanceledException")
                return "Operation timed out. Try again.";

            // ── Argument / parsing ───────────────────────────────────────────────
            if (typeName == "FormatException")
                return $"Invalid format: {msg}";
            if (typeName == "ArgumentNullException" || typeName == "ArgumentException")
                return msg;   // these usually already have the field name baked in

            // ── Fallback: strip noise from the raw message ────────────────────────
            // SQLite messages often start with "SQLite Error N: " — trim that
            var clean = msg;
            int colon = clean.IndexOf(":");
            if (colon > 0 && colon < 25 && clean.StartsWith("SQLite", StringComparison.OrdinalIgnoreCase))
                clean = clean.Substring(colon + 1).Trim();
            return string.IsNullOrWhiteSpace(clean) ? "An error occurred." : clean;
        }

        /// <summary>Convenience wrapper: returns a tuple suitable for `(bool ok, string msg)` patterns.</summary>
        public static (bool ok, string msg) Fail(Exception ex)
            => (false, UserFriendly(ex));

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string ExtractAfter(string text, string marker)
        {
            int i = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return "";
            return text.Substring(i + marker.Length).Trim().TrimEnd('.', ' ');
        }

        /// <summary>"employees.pan" → "PAN" ; "challan_no" → "Challan No"</summary>
        private static string PrettifyField(string raw)
        {
            // Strip table prefix
            int dot = raw.IndexOf('.');
            var col = dot >= 0 ? raw.Substring(dot + 1) : raw;
            // Replace underscores → spaces, title-case
            var parts = col.Split('_', StringSplitOptions.RemoveEmptyEntries);
            return string.Join(" ", parts.Select(p =>
                p.Equals("pan", StringComparison.OrdinalIgnoreCase) ? "PAN" :
                p.Equals("tan", StringComparison.OrdinalIgnoreCase) ? "TAN" :
                p.Equals("ifsc", StringComparison.OrdinalIgnoreCase) ? "IFSC" :
                p.Equals("tds", StringComparison.OrdinalIgnoreCase) ? "TDS" :
                p.Length > 0 ? char.ToUpper(p[0]) + p.Substring(1) : p));
        }

        private static string ExtractPath(string msg)
        {
            // FileNotFoundException.Message often: "Could not find file 'C:\path\to\file.ext'."
            int q1 = msg.IndexOf('\'');
            int q2 = msg.LastIndexOf('\'');
            if (q1 >= 0 && q2 > q1) return msg.Substring(q1 + 1, q2 - q1 - 1);
            return msg;
        }
    }
}
