using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.Windows;

namespace CDeTDS.App.Services
{
    /// <summary>
    /// Auto-logs into the IT Portal using saved TAN + password, then reveals the same
    /// WebView2 window (already authenticated) on the TDS filing menu so the user can
    /// pick the .fvu file and submit manually.
    ///
    /// Uses a single WebView2 — hidden during login, shown after success — so no
    /// cookie-transfer is needed between separate browser contexts.
    /// </summary>
    public class ItPortalUploadService
    {
        private const string PortalBase = "https://eportal.incometax.gov.in";
        private const string LoginPage  = "/iec/foservices/#/login";
        private const string TdsMenu    = "/iec/foservices/#/tdsmenu/tdsMainMenu";

        public record UploadLoginResult(bool Success, string? Error);

        public Task<UploadLoginResult> LoginAndOpenAsync(int deductorId = 0, IProgress<string>? progress = null)
        {
            var key      = deductorId > 0 ? $"ItPortalUserId_{deductorId}" : "ItPortalUserId";
            var passKey  = deductorId > 0 ? $"ItPortalPassword_{deductorId}" : "ItPortalPassword";
            var tan      = CDeTDS.DAL.Database.GetSetting(key, "").Trim().ToUpper();
            var password = CDeTDS.DAL.AesEncryption.LoadCredential(passKey, "");

            if (string.IsNullOrWhiteSpace(tan))
                return Task.FromResult(new UploadLoginResult(false,
                    "IT Portal TAN not configured. Set it in Settings → IT Portal."));
            if (string.IsNullOrWhiteSpace(password))
                return Task.FromResult(new UploadLoginResult(false,
                    "IT Portal password not saved. Set it in Settings → IT Portal."));

            var tcs = new TaskCompletionSource<UploadLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                Window?   win = null;
                WebView2? wv  = null;
                try
                {
                    // ── Create window — tiny + off-screen during login ──────────
                    win = new Window
                    {
                        Title         = "IT Portal — Upload TDS Return",
                        Width         = 1,
                        Height        = 1,
                        Left          = -99999,
                        Top           = -99999,
                        ShowInTaskbar = false,
                        WindowStyle   = WindowStyle.None,
                        ResizeMode    = ResizeMode.NoResize,
                        ShowActivated = false,
                        Opacity       = 0,
                    };
                    wv          = new WebView2();
                    win.Content = wv;
                    win.Show();

                    await wv.EnsureCoreWebView2Async();
                    var cw = wv.CoreWebView2;
                    cw.Settings.UserAgent =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";
                    await cw.Profile.ClearBrowsingDataAsync();

                    // ── Step 1: load login page ─────────────────────────────────
                    progress?.Report("Loading IT Portal...");
                    cw.Navigate(PortalBase + LoginPage);

                    var nav1 = new TaskCompletionSource();
                    cw.NavigationCompleted += OnNav1;
                    void OnNav1(object? s, CoreWebView2NavigationCompletedEventArgs e)
                    { cw.NavigationCompleted -= OnNav1; nav1.TrySetResult(); }
                    await nav1.Task;
                    await Task.Delay(3000);

                    // ── Step 2: wait for TAN field ──────────────────────────────
                    progress?.Report("Waiting for login form...");
                    bool formReady = false;
                    for (int i = 0; i < 20; i++)
                    {
                        await Task.Delay(1000);
                        var has = await cw.ExecuteScriptAsync(
                            "document.querySelector('input[name=\"panAdhaarUserId\"]') ? 'yes' : 'no'");
                        if (has.Contains("yes")) { formReady = true; break; }
                        progress?.Report($"Waiting for login form... ({i + 1}s)");
                    }
                    if (!formReady)
                    {
                        tcs.TrySetResult(new UploadLoginResult(false,
                            "Login form did not appear. Check internet connection."));
                        Cleanup(wv, win); return;
                    }

                    // ── Step 3: fill TAN ────────────────────────────────────────
                    progress?.Report("Filling credentials...");
                    await cw.ExecuteScriptAsync($$"""
                        (function() {
                            var el = document.querySelector('input[name="panAdhaarUserId"]');
                            if (!el) return;
                            var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                            setter.call(el, '{{tan}}');
                            el.dispatchEvent(new Event('input',  {bubbles:true}));
                            el.dispatchEvent(new Event('change', {bubbles:true}));
                        })()
                        """);
                    await Task.Delay(600);

                    // ── Step 4: click Continue ──────────────────────────────────
                    await cw.ExecuteScriptAsync(@"
                        (function() {
                            var btn = Array.from(document.querySelectorAll('button'))
                                .find(b => /continue/i.test(b.textContent.trim()));
                            if (btn) btn.click();
                        })()");
                    await Task.Delay(2500);

                    // ── Step 5: fill password + checkbox ───────────────────────
                    progress?.Report("Filling password...");
                    await Task.Delay(2000);
                    await cw.ExecuteScriptAsync($$"""
                        (function() {
                            var cb = document.querySelector('input[type="checkbox"]');
                            if (cb && !cb.checked) cb.click();
                            var el = document.querySelector('input[type="password"]');
                            if (!el) return;
                            var setter = Object.getOwnPropertyDescriptor(window.HTMLInputElement.prototype, 'value').set;
                            setter.call(el, '{{password}}');
                            el.dispatchEvent(new Event('input',  {bubbles:true}));
                            el.dispatchEvent(new Event('change', {bubbles:true}));
                        })()
                        """);
                    await Task.Delay(600);

                    // ── Step 6: click Login ─────────────────────────────────────
                    progress?.Report("Submitting login...");
                    await cw.ExecuteScriptAsync(@"
                        (function() {
                            var btn = Array.from(document.querySelectorAll('button'))
                                .find(b => /login|sign in|continue/i.test(b.textContent.trim()));
                            if (!btn) btn = document.querySelector('button[type=""submit""]');
                            if (btn) btn.click();
                        })()");

                    // ── Step 7: wait for AuthToken cookie ───────────────────────
                    progress?.Report("Waiting for login to complete...");
                    bool loggedIn = false;
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(1000);
                        var url = cw.Source;

                        // Dismiss "Dual Login Detected" / "Login Here" modal
                        await cw.ExecuteScriptAsync(@"
                            (function() {
                                var all = Array.from(document.querySelectorAll('button, a, span, div'));
                                var el = all.find(e => e.textContent.trim() === 'Login Here');
                                if (el) { el.click(); return; }
                                el = all.find(e => /login here/i.test(e.textContent.trim())
                                                   && e.textContent.trim().length < 20);
                                if (el) el.click();
                            })()");

                        var cookies1 = await cw.CookieManager.GetCookiesAsync(PortalBase);
                        var cookies2 = await cw.CookieManager.GetCookiesAsync("https://incometax.gov.in");
                        var hasAuth  = cookies1.Any(c => c.Name == "AuthToken")
                                    || cookies2.Any(c => c.Name == "AuthToken");
                        var onDash   = url.Contains("dashboard") || url.Contains("home")
                                    || url.Contains("myaccount");
                        progress?.Report($"Waiting for login... ({i + 1}s)");
                        if (hasAuth || onDash) { loggedIn = true; break; }
                    }

                    if (!loggedIn)
                    {
                        tcs.TrySetResult(new UploadLoginResult(false,
                            "Login did not complete — check credentials in Settings → IT Portal."));
                        Cleanup(wv, win); return;
                    }

                    // ── Step 8: navigate to TDS filing menu ─────────────────────
                    progress?.Report("Login successful. Navigating to TDS menu...");
                    cw.Navigate(PortalBase + TdsMenu);
                    await Task.Delay(2000);

                    // ── Step 9: reveal the window ───────────────────────────────
                    win.WindowStyle   = WindowStyle.SingleBorderWindow;
                    win.ResizeMode    = ResizeMode.CanResize;
                    win.ShowInTaskbar = true;
                    win.Opacity       = 1;
                    win.Width         = 1280;
                    win.Height        = 820;
                    win.Left          = (SystemParameters.WorkArea.Width  - 1280) / 2;
                    win.Top           = (SystemParameters.WorkArea.Height - 820)  / 2;
                    win.Activate();

                    tcs.TrySetResult(new UploadLoginResult(true, null));
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(new UploadLoginResult(false, "Unexpected error: " + ex.Message));
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
                    try { win?.Close(); }  catch { }
                });
            }
            catch { }
        }
    }
}
