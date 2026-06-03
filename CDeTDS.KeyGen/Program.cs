// ╔══════════════════════════════════════════════════════════════════════════╗
// ║  CDeTDS — License Key Generator  (Admin Tool — DO NOT DISTRIBUTE)      ║
// ║  Private key lives here only. Never ship this tool to customers.         ║
// ╚══════════════════════════════════════════════════════════════════════════╝
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CDeTDS.DAL;

internal static class EntryPoint
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new KeyGenForm());
    }
}

// ── Key generation logic (private key ONLY here, never in DAL) ───────────────
internal static class KeySigner
{
    // ECDSA P-256 private key — NEVER distribute this tool
    // Generated once offline; matching public key is embedded in LicenseService.cs
    private const string PrivateKeyPem =
        "-----BEGIN EC PRIVATE KEY-----\n" +
        "MHcCAQEEIGHPsO9zUS+CoSif8Za4ZcfWgxxskcqmuvzr2xwlh2jQoAoGCCqGSM49\n" +
        "AwEHoUQDQgAESLlbrTuxUvQhVDZ8eN/oxMZte9E+/TLuWxg7sLGKutgEVSd9v97U\n" +
        "c8W65wCm/wzNdAOo/xBtO4bTeBTjTXnf9w==\n" +
        "-----END EC PRIVATE KEY-----";

    public static string GenerateKey(string tier, int validDays, int maxDed, int maxEnt, int maxUsr, string mid)
    {
        mid = (mid?.Trim().ToUpper() ?? "").PadRight(12, '0')[..12];

        var expiry = validDays >= 36500
            ? "99991231"
            : DateTime.Today.AddDays(validDays).ToString("yyyyMMdd");

        var payload = JsonSerializer.Serialize(new
        {
            tid = tier.ToUpper(),
            exp = expiry,
            ded = maxDed,
            ent = maxEnt == int.MaxValue ? 999999 : maxEnt,
            usr = maxUsr,
            mid,
        });

        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(PrivateKeyPem);
        var sig = ecdsa.SignData(payloadBytes, HashAlgorithmName.SHA256);

        // Pack: [4-byte payload length (big-endian)][payload bytes][signature bytes]
        var lenBytes = new byte[]
        {
            (byte)(payloadBytes.Length >> 24),
            (byte)(payloadBytes.Length >> 16),
            (byte)(payloadBytes.Length >> 8),
            (byte)(payloadBytes.Length),
        };

        var raw = lenBytes.Concat(payloadBytes).Concat(sig).ToArray();
        var b32 = LicenseService.Base32Encode(raw);

        // Format as TDSPRO-XXXXXX-XXXXXX-...
        var sb = new StringBuilder("TDSPRO");
        for (int i = 0; i < b32.Length; i++)
        {
            if (i % 6 == 0) sb.Append('-');
            sb.Append(b32[i]);
        }
        return sb.ToString();
    }
}

// ── Form ─────────────────────────────────────────────────────────────────────
public class KeyGenForm : Form
{
    private static readonly string LogPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "CDeTDS_KeyLog.csv");

    private ComboBox      _cmbTier  = null!;
    private NumericUpDown _nudDays  = null!;
    private NumericUpDown _nudDed   = null!;
    private NumericUpDown _nudUsr   = null!;
    private TextBox       _txtMid   = null!;
    private TextBox       _txtCust  = null!;
    private TextBox       _txtKey   = null!;
    private DataGridView  _grid     = null!;
    private Label?        _statusLbl;

    public KeyGenForm()
    {
        Text            = "CDeTDS  ·  License Key Generator  [ADMIN — DO NOT DISTRIBUTE]";
        Size            = new Size(1000, 760);
        MinimumSize     = new Size(900, 680);
        StartPosition   = FormStartPosition.CenterScreen;
        BackColor       = Color.FromArgb(15, 23, 42);
        ForeColor       = Color.White;
        Font            = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.Sizable;
        BuildUI();
        LoadLog();
    }

    private void BuildUI()
    {
        // Warning banner
        var warn = new Panel { Height = 32, Dock = DockStyle.Top, BackColor = Color.FromArgb(153, 0, 0) };
        warn.Controls.Add(new Label
        {
            Text      = "⚠  INTERNAL ADMIN TOOL — PRIVATE KEY INSIDE — DO NOT DISTRIBUTE TO CUSTOMERS",
            Dock      = DockStyle.Fill, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.White, TextAlign = ContentAlignment.MiddleCenter,
        });
        Controls.Add(warn);

        // Log panel (bottom)
        var logPanel = new Panel { Height = 270, Dock = DockStyle.Bottom, BackColor = Color.FromArgb(10, 16, 30), Padding = new Padding(16, 8, 16, 10) };
        logPanel.Controls.Add(new Label
        {
            Text = "Key Log — double-click a row to copy key to clipboard",
            Dock = DockStyle.Top, Height = 24, Font = new Font("Segoe UI", 9f, FontStyle.Bold),
            ForeColor = Color.FromArgb(100, 160, 255),
        });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill, BackgroundColor = Color.FromArgb(15, 23, 42),
            GridColor = Color.FromArgb(30, 41, 59), ForeColor = Color.FromArgb(200, 210, 220),
            Font = new Font("Consolas", 8.5f), ReadOnly = true, AllowUserToAddRows = false,
            RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            BorderStyle = BorderStyle.None, CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
            ColumnHeadersHeight = 28,
        };
        _grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(30, 41, 59);
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(148, 163, 184);
        _grid.ColumnHeadersDefaultCellStyle.Font      = new Font("Segoe UI", 9f, FontStyle.Bold);
        _grid.DefaultCellStyle.BackColor              = Color.FromArgb(15, 23, 42);
        _grid.DefaultCellStyle.ForeColor              = Color.FromArgb(200, 220, 200);
        _grid.DefaultCellStyle.SelectionBackColor     = Color.FromArgb(37, 99, 235);
        _grid.DefaultCellStyle.SelectionForeColor     = Color.White;

        foreach (var (h, w) in new[] { ("DateTime",130),("Tier",60),("Expires",100),("Ded",50),("Usr",50),("Machine ID",120),("Customer",140),("License Key",320) })
            _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = h, Width = w, ReadOnly = true });

        _grid.CellDoubleClick += (s, e) =>
        {
            if (e.RowIndex < 0) return;
            var key = _grid.Rows[e.RowIndex].Cells[7].Value?.ToString() ?? "";
            if (!string.IsNullOrEmpty(key)) { Clipboard.SetText(key); Flash("Key copied."); }
        };

        var logBtns = new Panel { Height = 36, Dock = DockStyle.Bottom, BackColor = Color.FromArgb(10, 16, 30) };
        var bExport  = Btn("Export CSV",        Color.FromArgb(30, 41, 59));
        var bCopyRow = Btn("Copy Selected Key", Color.FromArgb(30, 41, 59));
        var bClear   = Btn("Clear Log",         Color.FromArgb(100, 20, 20));
        bExport.Left = 0; bCopyRow.Left = 130; bClear.Left = 270;
        bExport.Click   += ExportCsv;
        bCopyRow.Click  += (s, e) => { if (_grid.CurrentRow != null) { Clipboard.SetText(_grid.CurrentRow.Cells[7].Value?.ToString() ?? ""); Flash("Copied."); } };
        bClear.Click    += ClearLog;
        logBtns.Controls.AddRange(new Control[] { bExport, bCopyRow, bClear });
        logPanel.Controls.Add(_grid);
        logPanel.Controls.Add(logBtns);
        Controls.Add(logPanel);

        // Input panel — added after bottom-docked panels so Fill claims remaining space
        var top = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 23, 42) };
        Controls.Add(top);

        // Inner panel — fixed size, centred both ways via Resize
        const int LW = 160; const int FW = 320; const int RH = 36; const int G = 10;
        const int INNER_W = LW + 10 + FW + 80; // enough for label + field + copy btn
        const int INNER_H = 9 * (RH + G) + 54 + 44 + 20; // rows + gen btn + key output + status
        var inner = new Panel { Width = INNER_W, Height = INNER_H, BackColor = Color.FromArgb(15, 23, 42) };
        top.Controls.Add(inner);
        top.Resize += (s, e) =>
        {
            inner.Left = Math.Max(0, (top.ClientSize.Width  - INNER_W) / 2);
            inner.Top  = Math.Max(0, (top.ClientSize.Height - INNER_H) / 2);
        };

        const int X = 0;
        int y = 0;

        Label Lbl(string t) => new()
        {
            Text = t, Left = X, Top = y + (RH - 18) / 2, Width = LW, Height = 18,
            ForeColor = Color.FromArgb(148, 163, 184), Font = new Font("Segoe UI", 9.5f),
            TextAlign = ContentAlignment.MiddleRight,
        };

        // Tier
        _cmbTier = new ComboBox { Left = X + LW + 10, Top = y, Width = FW, Height = RH, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        _cmbTier.Items.AddRange(new[] { "Pro" });
        _cmbTier.SelectedIndex = 0;
        _cmbTier.SelectedIndexChanged += (s, e) => ApplyTierDefaults();
        inner.Controls.Add(Lbl("License Tier *")); inner.Controls.Add(_cmbTier); y += RH + G;

        // Valid days
        _nudDays = Nud(X + LW + 10, y, 1, 36500, 365);
        inner.Controls.Add(Lbl("Valid Days")); inner.Controls.Add(_nudDays); y += RH + G;

        // Max deductors
        _nudDed = Nud(X + LW + 10, y, 1, 9999, 5);
        inner.Controls.Add(Lbl("Max Deductors")); inner.Controls.Add(_nudDed); y += RH + G;

        // Max users
        _nudUsr = Nud(X + LW + 10, y, 1, 99, 3);
        inner.Controls.Add(Lbl("Max Users")); inner.Controls.Add(_nudUsr); y += RH + G;

        // Machine ID
        _txtMid = Txt(X + LW + 10, y, FW, "Required for Pro — paste customer's 12-char Machine ID from their Activate page");
        _txtMid.CharacterCasing = CharacterCasing.Upper;
        _txtMid.Font = new Font("Consolas", 10f);
        inner.Controls.Add(Lbl("Machine ID *")); inner.Controls.Add(_txtMid); y += RH + G;

        // Customer
        _txtCust = Txt(X + LW + 10, y, FW, "Customer name / order ref");
        inner.Controls.Add(Lbl("Customer / Notes")); inner.Controls.Add(_txtCust); y += RH + G + 4;

        // Generate button
        var bGen = new Button
        {
            Text = "⚡  Generate License Key", Left = X + LW + 10, Top = y, Width = FW, Height = 44,
            BackColor = Color.FromArgb(37, 99, 235), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 12f, FontStyle.Bold), Cursor = Cursors.Hand,
        };
        bGen.FlatAppearance.BorderSize = 0;
        bGen.Click += GenerateKey;
        inner.Controls.Add(bGen); y += 54;

        // Key output
        _txtKey = new TextBox
        {
            Left = X + LW + 10, Top = y, Width = FW, Height = 36,
            BackColor = Color.FromArgb(4, 80, 60), ForeColor = Color.FromArgb(74, 222, 128),
            BorderStyle = BorderStyle.FixedSingle, ReadOnly = true,
            Font = new Font("Consolas", 10f, FontStyle.Bold), TextAlign = HorizontalAlignment.Center,
        };
        var bCopy2 = new Button
        {
            Text = "Copy", Left = X + LW + 10 + FW + 8, Top = y, Width = 72, Height = 36,
            BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.FromArgb(74, 222, 128),
            FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9f, FontStyle.Bold), Cursor = Cursors.Hand,
        };
        bCopy2.FlatAppearance.BorderSize = 1;
        bCopy2.Click += (s, e) => { if (!string.IsNullOrEmpty(_txtKey.Text)) { Clipboard.SetText(_txtKey.Text); Flash("Copied!"); } };
        inner.Controls.Add(_txtKey); inner.Controls.Add(bCopy2); y += 44;

        _statusLbl = new Label { Left = X + LW + 10, Top = y, Width = FW + 80, Height = 20, ForeColor = Color.FromArgb(100, 180, 100), Font = new Font("Segoe UI", 8.5f) };
        inner.Controls.Add(_statusLbl);

        ApplyTierDefaults();
    }

    private void ApplyTierDefaults()
    {
        // Pro defaults
        _nudDays.Value = 365; _nudDed.Value = 5; _nudUsr.Value = 3;
    }

    private void GenerateKey(object? sender, EventArgs e)
    {
        var tierName = "PRO";
        var days     = (int)_nudDays.Value;
        var maxDed   = (int)_nudDed.Value;
        var maxEnt   = 999999; // unlimited for all Pro keys
        var maxUsr   = (int)_nudUsr.Value;
        var mid      = _txtMid.Text.Trim().ToUpper();
        var cust     = _txtCust.Text.Trim();

        // Pro keys must be machine-locked
        if (tierName == "PRO" && string.IsNullOrEmpty(mid))
        {
            MessageBox.Show("Machine ID is required for Pro keys.\nAsk the customer to send their Machine ID from the Activate page.", "Machine ID Required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtMid.Focus();
            return;
        }
        if (string.IsNullOrEmpty(mid)) mid = "000000000000"; // Trial floating

        try
        {
            var key     = KeySigner.GenerateKey(tierName, days, maxDed, maxEnt, maxUsr, mid);
            var expDate = days >= 36500 ? "Lifetime" : DateTime.Today.AddDays(days).ToString("dd-MMM-yyyy");
            var now     = DateTime.Now.ToString("yyyy-MM-dd HH:mm");

            _txtKey.Text = key;
            Clipboard.SetText(key);

            AppendLog(now, tierName, expDate, maxDed.ToString(), maxUsr.ToString(), mid, cust, key);
            _grid.Rows.Insert(0, now, tierName, expDate, maxDed.ToString(), maxUsr.ToString(), mid, cust, key);
            _grid.Rows[0].DefaultCellStyle.ForeColor = Color.FromArgb(74, 222, 128);

            Flash($"Generated & copied!  Tier: {tierName}  |  Expires: {expDate}  |  Ded: {maxDed}  Usr: {maxUsr}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Key generation failed:\n{ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void Flash(string msg)
    {
        if (_statusLbl == null) return;
        _statusLbl.Text = msg;
        var t = new System.Windows.Forms.Timer { Interval = 3000, Enabled = true };
        t.Tick += (s, e) => { _statusLbl.Text = ""; t.Stop(); };
    }

    private NumericUpDown Nud(int left, int top, int min, int max, int val) =>
        new() { Left = left, Top = top, Width = 320, Height = 30, Minimum = min, Maximum = max, Value = val, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };

    private TextBox Txt(int left, int top, int width, string placeholder) =>
        new() { Left = left, Top = top, Width = width, Height = 30, BackColor = Color.FromArgb(30, 41, 59), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, PlaceholderText = placeholder };

    private Button Btn(string text, Color bg) =>
        new() { Text = text, Width = 120, Height = 30, BackColor = bg, ForeColor = Color.FromArgb(180, 190, 200), FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 8.5f), Cursor = Cursors.Hand };

    private void AppendLog(string dt, string tier, string exp, string ded, string usr, string mid, string cust, string key)
    {
        try
        {
            bool newFile = !File.Exists(LogPath);
            using var w = new StreamWriter(LogPath, append: true);
            if (newFile) w.WriteLine("DateTime,Tier,Expires,MaxDed,MaxUsr,MachineID,Customer,LicenseKey");
            w.WriteLine($"\"{dt}\",\"{tier}\",\"{exp}\",\"{ded}\",\"{usr}\",\"{mid}\",\"{cust.Replace("\"", "'")}\",\"{key}\"");
        }
        catch { }
    }

    private void LoadLog()
    {
        if (!File.Exists(LogPath)) return;
        try
        {
            foreach (var line in File.ReadAllLines(LogPath).Skip(1).Reverse())
            {
                var cols = ParseCsvLine(line);
                if (cols.Length >= 8) _grid.Rows.Add(cols[0], cols[1], cols[2], cols[3], cols[4], cols[5], cols[6], cols[7]);
            }
        }
        catch { }
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>(); bool inQ = false; var f = new StringBuilder();
        foreach (char c in line)
        {
            if (c == '"') inQ = !inQ;
            else if (c == ',' && !inQ) { result.Add(f.ToString()); f.Clear(); }
            else f.Append(c);
        }
        result.Add(f.ToString());
        return result.ToArray();
    }

    private void ExportCsv(object? sender, EventArgs e)
    {
        if (!File.Exists(LogPath)) { MessageBox.Show("No log yet.", "Export"); return; }
        using var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"CDeTDS_Keys_{DateTime.Today:yyyyMMdd}.csv" };
        if (dlg.ShowDialog() == DialogResult.OK) { File.Copy(LogPath, dlg.FileName, true); MessageBox.Show("Exported: " + dlg.FileName); }
    }

    private void ClearLog(object? sender, EventArgs e)
    {
        if (MessageBox.Show("Delete all key history?", "Clear Log", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { if (File.Exists(LogPath)) File.Delete(LogPath); } catch { }
        _grid.Rows.Clear(); Flash("Log cleared.");
    }
}
