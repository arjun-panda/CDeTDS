using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Windows;

namespace TDSPro.App.Services
{
    public class CsiDownloadService
    {
        private const string PortalBase = "https://eportal.incometax.gov.in";
        private const string CsiApiUrl  = PortalBase + "/iec/paymentapi/auth/challan/downloadCSI";

        public record CsiResult(bool Success, string? FilePath, string? Error);

        public Task<CsiResult> DownloadAsync(
            string tan, string password,
            string fromDate, string toDate,
            string outputPath,
            IProgress<string>? progress = null)
        {
            var tcs = new TaskCompletionSource<CsiResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                WebView2? wv  = null;
                Window?   win = null;
                try
                {
                    win = new Window
                    {
                        Width         = 1,
                        Height        = 1,
                        Left          = -99999,
                        Top           = -99999,
                        ShowInTaskbar = false,
                        WindowStyle   = WindowStyle.None,
                        ResizeMode    = ResizeMode.NoResize,
                        ShowActivated = false,
                        Opacity       = 0
                    };
                    wv = new WebView2();
                    win.Content = wv;
                    win.Show();

                    await wv.EnsureCoreWebView2Async();
                    var cw = wv.CoreWebView2;

                    cw.Settings.UserAgent =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
                        "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

                    // Clear previous session
                    await cw.Profile.ClearBrowsingDataAsync();

                    // ── Step 1: load login page ───────────────────────────────
                    progress?.Report("Loading IT Portal login page...");
                    var navDone = new TaskCompletionSource();
                    cw.NavigationCompleted += OnNav;
                    void OnNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
                    { cw.NavigationCompleted -= OnNav; navDone.TrySetResult(); }
                    cw.Navigate(PortalBase + "/iec/foservices/#/login");
                    await navDone.Task;

                    // Wait for Angular to render login form
                    progress?.Report("Waiting for login form...");
                    bool formReady = false;
                    for (int i = 0; i < 30; i++)
                    {
                        await Task.Delay(1000);
                        var check = await cw.ExecuteScriptAsync(
                            "document.querySelector('input[name=\"panAdhaarUserId\"]') !== null ? 'yes' : 'no'");
                        progress?.Report($"Waiting for login form... ({i+1}s) url={cw.Source}");
                        if (check.Contains("yes")) { formReady = true; break; }
                    }

                    if (!formReady)
                    {
                        tcs.TrySetResult(new CsiResult(false, null,
                            $"Login form did not appear. Last URL: {cw.Source}"));
                        Cleanup(wv, win); return;
                    }

                    // ── Step 2: fill TAN and click Continue ───────────────────
                    progress?.Report("Filling TAN...");
                    await cw.ExecuteScriptAsync($$"""
                        (function() {
                            var el = document.querySelector('input[name="panAdhaarUserId"]');
                            if (!el) return;
                            var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set;
                            setter.call(el, '{{tan}}');
                            el.dispatchEvent(new Event('input',  {bubbles:true}));
                            el.dispatchEvent(new Event('change', {bubbles:true}));
                            el.dispatchEvent(new Event('blur',   {bubbles:true}));
                        })()
                        """);
                    await Task.Delay(800);

                    await cw.ExecuteScriptAsync(@"
                        (function() {
                            var btn = Array.from(document.querySelectorAll('button'))
                                .find(b => /continue/i.test(b.textContent.trim()));
                            if (btn) btn.click();
                        })()");
                    await Task.Delay(3000);

                    // ── Step 3: fill password and login ──────────────────────
                    progress?.Report("Filling password...");

                    // Wait up to 10s for password field
                    bool passReady = false;
                    for (int i = 0; i < 10; i++)
                    {
                        await Task.Delay(1000);
                        var hasPass = await cw.ExecuteScriptAsync(
                            "document.querySelector('input[type=\"password\"]') !== null ? 'yes' : 'no'");
                        if (hasPass.Contains("yes")) { passReady = true; break; }
                    }

                    if (!passReady)
                    {
                        tcs.TrySetResult(new CsiResult(false, null,
                            $"Password field did not appear. URL: {cw.Source}"));
                        Cleanup(wv, win); return;
                    }

                    await cw.ExecuteScriptAsync($$"""
                        (function() {
                            // Tick secure access checkbox if present
                            var cb = document.querySelector('input[type="checkbox"]');
                            if (cb && !cb.checked) cb.click();

                            // Fill password
                            var el = document.querySelector('input[type="password"]');
                            if (!el) return;
                            var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype,'value').set;
                            setter.call(el, '{{password}}');
                            el.dispatchEvent(new Event('input',  {bubbles:true}));
                            el.dispatchEvent(new Event('change', {bubbles:true}));
                            el.dispatchEvent(new Event('blur',   {bubbles:true}));
                        })()
                        """);
                    await Task.Delay(800);

                    // Click Login / Continue
                    await cw.ExecuteScriptAsync(@"
                        (function() {
                            var btn = Array.from(document.querySelectorAll('button'))
                                .find(b => /^(login|sign in|continue)$/i.test(b.textContent.trim()));
                            if (!btn) btn = document.querySelector('button[type=""submit""]');
                            if (btn) btn.click();
                        })()");

                    // ── Step 4: wait for login to succeed ─────────────────────
                    progress?.Report("Waiting for login...");
                    bool loggedIn = false;
                    for (int i = 0; i < 90; i++)
                    {
                        await Task.Delay(1000);

                        // Dismiss "session already active / dual login" modal
                        await cw.ExecuteScriptAsync(@"
                            (function() {
                                var els = Array.from(document.querySelectorAll('button,a,span'));
                                var el  = els.find(e => /login\s*here/i.test(e.textContent.trim())
                                                     && e.textContent.trim().length < 25);
                                if (el) el.click();
                            })()");

                        // Also dismiss any other confirmation dialogs
                        await cw.ExecuteScriptAsync(@"
                            (function() {
                                var els = Array.from(document.querySelectorAll('button'));
                                var el  = els.find(e => /^(ok|proceed|yes|confirm)$/i.test(e.textContent.trim()));
                                if (el) el.click();
                            })()");

                        var url = cw.Source ?? "";
                        var urlOk = url.Contains("dashboard") || url.Contains("myaccount")
                                 || url.Contains("home")      || url.Contains("foservices/#/");

                        // Check for AuthToken cookie
                        var c1 = await cw.CookieManager.GetCookiesAsync(PortalBase);
                        var c2 = await cw.CookieManager.GetCookiesAsync("https://incometax.gov.in");
                        var hasAuth = c1.Any(c => c.Name == "AuthToken")
                                   || c2.Any(c => c.Name == "AuthToken")
                                   || c1.Any(c => c.Name.Contains("token", StringComparison.OrdinalIgnoreCase)
                                                  && c.Value.Length > 20);

                        // Check if we left the login page (Angular SPA — hash changes)
                        var leftLogin = !url.Contains("#/login") && url.Contains("foservices");

                        progress?.Report($"Login wait {i+1}s | auth={hasAuth} leftLogin={leftLogin} url={url[..Math.Min(80,url.Length)]}");

                        if (hasAuth || leftLogin) { loggedIn = true; break; }
                    }

                    if (!loggedIn)
                    {
                        // Capture page state for diagnosis
                        var finalUrl  = cw.Source ?? "";
                        var pageTitle = await cw.ExecuteScriptAsync("document.title");
                        tcs.TrySetResult(new CsiResult(false, null,
                            $"Login did not complete after 90s. URL: {finalUrl} | Title: {pageTitle}\n" +
                            "Check credentials in Portals page. The portal may require OTP — log in manually once to clear any security prompt."));
                        Cleanup(wv, win); return;
                    }

                    // ── Step 5: fetch CSI via fetch() inside the logged-in session ──
                    progress?.Report("Logged in. Fetching CSI file...");
                    await Task.Delay(1500);

                    var escapedTan  = tan.Replace("'", "\\'");
                    var escapedFrom = fromDate.Replace("'", "\\'");
                    var escapedTo   = toDate.Replace("'", "\\'");

                    var csiScript = $@"
                        window._csiData = undefined;
                        (async () => {{
                            try {{
                                const r = await fetch('{CsiApiUrl}', {{
                                    method: 'POST',
                                    credentials: 'include',
                                    headers: {{ 'Content-Type': 'application/json', 'sn': 'NA' }},
                                    body: JSON.stringify({{
                                        header:   {{ formName: 'PO-03-PYMNT' }},
                                        formData: {{
                                            pan:              '{escapedTan}',
                                            fromDate:         '{escapedFrom}',
                                            toDate:           '{escapedTo}',
                                            actType:          'O',
                                            loggedInUserID:   '{escapedTan}',
                                            loggedInUserType: 'TDS'
                                        }}
                                    }})
                                }});
                                const ct  = r.headers.get('content-type') || '';
                                const buf = await r.arrayBuffer();
                                const bytes = new Uint8Array(buf);
                                let bin = '';
                                for (let i = 0; i < bytes.byteLength; i++) bin += String.fromCharCode(bytes[i]);
                                window._csiData = btoa(bin) + '|' + ct + '|' + r.status;
                            }} catch(e) {{
                                window._csiData = 'ERROR:' + e.message;
                            }}
                        }})();
                        true";

                    await cw.ExecuteScriptAsync(csiScript);

                    // Poll for result (max 60s)
                    string csiRaw = "";
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(1000);
                        var poll = await cw.ExecuteScriptAsync(
                            "typeof window._csiData === 'undefined' ? '__PENDING__' : String(window._csiData)");
                        var val = poll.Trim('"');
                        if (val != "__PENDING__")
                        {
                            csiRaw = System.Text.RegularExpressions.Regex.Unescape(val);
                            break;
                        }
                        progress?.Report($"Waiting for CSI data... ({i+1}s)");
                    }

                    Cleanup(wv, win);

                    if (string.IsNullOrEmpty(csiRaw))
                    { tcs.TrySetResult(new CsiResult(false, null, "CSI request timed out.")); return; }

                    if (csiRaw.StartsWith("ERROR:"))
                    { tcs.TrySetResult(new CsiResult(false, null, "CSI fetch error: " + csiRaw[6..])); return; }

                    // Parse base64|contentType|statusCode
                    var parts     = csiRaw.Split('|');
                    var b64       = parts[0];
                    var ct        = parts.Length > 1 ? parts[1] : "";
                    var httpCode  = parts.Length > 2 ? parts[2] : "";

                    byte[] fileBytes;
                    try { fileBytes = Convert.FromBase64String(b64); }
                    catch (Exception ex)
                    { tcs.TrySetResult(new CsiResult(false, null, "Base64 decode failed: " + ex.Message)); return; }

                    // If portal returned JSON error body
                    if (ct.Contains("json") || (fileBytes.Length < 500 && fileBytes.Length > 0
                        && fileBytes[0] == '{'))
                    {
                        var body = System.Text.Encoding.UTF8.GetString(fileBytes);
                        tcs.TrySetResult(new CsiResult(false, null,
                            $"CSI API error (HTTP {httpCode}): {body[..Math.Min(300, body.Length)]}"));
                        return;
                    }

                    if (fileBytes.Length < 10)
                    { tcs.TrySetResult(new CsiResult(false, null, $"CSI response too small ({fileBytes.Length} bytes, HTTP {httpCode}).")); return; }

                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                        await File.WriteAllBytesAsync(outputPath, fileBytes);
                        tcs.TrySetResult(new CsiResult(true, outputPath, null));
                    }
                    catch (Exception ex)
                    {
                        tcs.TrySetResult(new CsiResult(false, null, "Save failed: " + ex.Message));
                    }
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(new CsiResult(false, null, "Unexpected error: " + ex.Message));
                    Cleanup(wv, win);
                }
            });

            return tcs.Task;
        }

        private static void Cleanup(WebView2? wv, Window? win)
        {
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                try { wv?.Dispose(); } catch { }
                try { win?.Close();  } catch { }
            });
        }
    }
}
