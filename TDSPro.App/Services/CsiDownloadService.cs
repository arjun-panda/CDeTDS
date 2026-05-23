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

                    cw.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";

                    // Clear any previous session so we don't hit "session already active"
                    await cw.Profile.ClearBrowsingDataAsync();




                    // Step 1: navigate to portal, let Angular fully load + set all WAF cookies
                    progress?.Report("Loading IT portal...");
                    cw.Navigate(PortalBase + "/iec/foservices/#/login");

                    // Wait for the page to fully load (NavigationCompleted + Angular bootstrap)
                    var navDone = new TaskCompletionSource();
                    cw.NavigationCompleted += OnFirstNav;
                    void OnFirstNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
                    {
                        cw.NavigationCompleted -= OnFirstNav;
                        navDone.TrySetResult();
                    }
                    await navDone.Task;

                    // Wait for Angular to render the login form
                    progress?.Report("Waiting for login form...");
                    await Task.Delay(3000);

                    // Poll for the user ID input — also log URL and input count each second
                    bool formReady = false;
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(1000);
                        var hasField = await cw.ExecuteScriptAsync("document.querySelector('input[name=\"panAdhaarUserId\"]') ? 'yes' : 'no'");
                        progress?.Report($"Waiting for login form... ({i+1}s)");
                        if (hasField.Contains("yes")) { formReady = true; break; }
                    }

                    if (!formReady)
                    {
                        tcs.TrySetResult(new CsiResult(false, null, "Login form did not appear — see progress for last URL."));
                        Cleanup(wv, win);
                        return;
                    }

                    // Fill TAN into the user ID field
                    progress?.Report("Filling credentials...");
                    await cw.ExecuteScriptAsync($$"""
                        (function() {
                            var el = document.querySelector('input[name="panAdhaarUserId"]');
                            if (!el) return;
                            var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                            setter.call(el, '{{tan}}');
                            el.dispatchEvent(new Event('input', {bubbles:true}));
                            el.dispatchEvent(new Event('change', {bubbles:true}));
                        })()
                        """);
                    await Task.Delay(600);

                    // Click Continue
                    await cw.ExecuteScriptAsync(@"
                        (function() {
                            var btn = Array.from(document.querySelectorAll('button')).find(b => /continue/i.test(b.textContent.trim()));
                            if (btn) btn.click();
                        })()");
                    await Task.Delay(2500);

                    // Wait for password page to load, then tick checkbox + fill password
                    progress?.Report("Filling password...");
                    await Task.Delay(2000);

                    await cw.ExecuteScriptAsync($$"""
                        (function() {
                            // Tick the secure access message checkbox
                            var cb = document.querySelector('input[type="checkbox"]');
                            if (cb && !cb.checked) {
                                cb.click();
                            }
                            // Fill password
                            var el = document.querySelector('input[type="password"]');
                            if (!el) return;
                            var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                            setter.call(el, '{{password}}');
                            el.dispatchEvent(new Event('input', {bubbles:true}));
                            el.dispatchEvent(new Event('change', {bubbles:true}));
                        })()
                        """);
                    await Task.Delay(600);

                    // Click Login button
                    progress?.Report("Submitting login...");
                    await cw.ExecuteScriptAsync(@"
                        (function() {
                            var btn = Array.from(document.querySelectorAll('button')).find(b => /login|sign in|continue/i.test(b.textContent.trim()));
                            if (!btn) btn = document.querySelector('button[type=""submit""]');
                            if (btn) btn.click();
                        })()
                        ");

                    // Wait for AuthToken cookie to appear (means login succeeded)
                    progress?.Report("Waiting for login to complete...");
                    // Wait for login to complete — handle "session already active" prompt
                    bool loggedIn = false;
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(1000);
                        var url = cw.Source;

                        // Handle "Dual Login Detected" modal — click "Login Here"
                        await cw.ExecuteScriptAsync(@"
                            (function() {
                                var all = Array.from(document.querySelectorAll('button, a, span, div'));
                                var el = all.find(e => e.textContent.trim() === 'Login Here');
                                if (el) { el.click(); return; }
                                // fallback: any element containing the text
                                el = all.find(e => /login here/i.test(e.textContent.trim()) && e.textContent.trim().length < 20);
                                if (el) el.click();
                            })()");

                        var cookies1 = await cw.CookieManager.GetCookiesAsync(PortalBase);
                        var cookies2 = await cw.CookieManager.GetCookiesAsync("https://incometax.gov.in");
                        var hasAuth = cookies1.Any(c => c.Name == "AuthToken") || cookies2.Any(c => c.Name == "AuthToken");
                        var onDashboard = url.Contains("dashboard") || url.Contains("home") || url.Contains("myaccount");
                        progress?.Report($"Waiting for login... ({i+1}s)");
                        if (hasAuth || onDashboard) { loggedIn = true; break; }
                    }

                    if (!loggedIn)
                    {
                        tcs.TrySetResult(new CsiResult(false, null, "Login did not complete — check credentials in Settings → IT Portal."));
                        Cleanup(wv, win);
                        return;
                    }

                    progress?.Report("Logged in. Fetching CSI...");
                    await Task.Delay(1000);

                    // Fetch CSI in JS, convert to base64, store in window._csiData
                    var csiScript = $$"""
                        window._csiData = undefined;
                        (async () => {
                            try {
                                const r = await fetch('{{CsiApiUrl}}', {
                                    method:'POST',
                                    credentials:'include',
                                    headers:{'Content-Type':'application/json','sn':'NA'},
                                    body:JSON.stringify({
                                        header:{formName:'PO-03-PYMNT'},
                                        formData:{pan:'{{tan}}',fromDate:'{{fromDate}}',toDate:'{{toDate}}',
                                                  actType:'O',loggedInUserID:'{{tan}}',loggedInUserType:'TDS'}
                                    })
                                });
                                const buf = await r.arrayBuffer();
                                const bytes = new Uint8Array(buf);
                                let binary = '';
                                for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
                                window._csiData = btoa(binary);
                            } catch(e) { window._csiData = 'ERROR:' + e.message; }
                        })();
                        true
                        """;
                    await cw.ExecuteScriptAsync(csiScript);

                    // Poll for _csiData (max 60s)
                    progress?.Report("Waiting for CSI data...");
                    string csiBase64 = "";
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(1000);
                        var poll = await cw.ExecuteScriptAsync("typeof window._csiData === 'undefined' ? '__PENDING__' : String(window._csiData)");
                        var val = poll.Trim('"');
                        if (val != "__PENDING__")
                        {
                            csiBase64 = System.Text.RegularExpressions.Regex.Unescape(val);
                            break;
                        }
                        progress?.Report($"Waiting for CSI... ({i+1}s)");
                    }

                    Cleanup(wv, win);

                    if (string.IsNullOrEmpty(csiBase64))
                    {
                        tcs.TrySetResult(new CsiResult(false, null, "CSI download timed out."));
                        return;
                    }
                    if (csiBase64.StartsWith("ERROR:"))
                    {
                        tcs.TrySetResult(new CsiResult(false, null, "CSI fetch failed: " + csiBase64));
                        return;
                    }

                    try
                    {
                        var bytes = Convert.FromBase64String(csiBase64);
                        if (bytes.Length < 10)
                        {
                            tcs.TrySetResult(new CsiResult(false, null, "CSI response empty: " + System.Text.Encoding.UTF8.GetString(bytes)));
                            return;
                        }
                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
                        await File.WriteAllBytesAsync(outputPath, bytes);
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
            try
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try { wv?.Dispose(); } catch { }
                    try { win?.Close(); } catch { }
                });
            }
            catch { }
        }
    }
}
