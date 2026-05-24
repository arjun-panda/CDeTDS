using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Windows;

namespace TDSPro.App.Services
{
    /// <summary>
    /// Auto-logs into the IT Portal using saved TAN + password (same mechanism as CSI download),
    /// then opens a visible browser window on the TDS return upload page so the user can
    /// pick the .fvu file and submit manually.
    /// </summary>
    public class ItPortalUploadService
    {
        private const string PortalBase   = "https://eportal.incometax.gov.in";
        private const string LoginPage    = "/iec/foservices/#/login";
        private const string UploadPage   = "/iec/foservices/#/dashboard";

        public record UploadLoginResult(bool Success, string? Error);

        public Task<UploadLoginResult> LoginAndOpenAsync(IProgress<string>? progress = null)
        {
            var tan      = TDSPro.DAL.Database.GetSetting("ItPortalUserId", "").Trim().ToUpper();
            var password = TDSPro.DAL.AesEncryption.LoadCredential("ItPortalPassword", "");

            if (string.IsNullOrWhiteSpace(tan))
                return Task.FromResult(new UploadLoginResult(false, "IT Portal TAN not configured. Set it in Settings → IT Portal."));
            if (string.IsNullOrWhiteSpace(password))
                return Task.FromResult(new UploadLoginResult(false, "IT Portal password not saved. Set it in Settings → IT Portal."));

            var tcs = new TaskCompletionSource<UploadLoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);

            Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                WebView2? hiddenWv  = null;
                Window?   hiddenWin = null;
                WebView2? visibleWv  = null;
                Window?   visibleWin = null;
                try
                {
                    // ── Phase 1: hidden window — login ──────────────────────────
                    hiddenWin = new Window
                    {
                        Width = 1, Height = 1, Left = -99999, Top = -99999,
                        ShowInTaskbar = false, WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize, ShowActivated = false, Opacity = 0
                    };
                    hiddenWv = new WebView2();
                    hiddenWin.Content = hiddenWv;
                    hiddenWin.Show();

                    await hiddenWv.EnsureCoreWebView2Async();
                    var cw = hiddenWv.CoreWebView2;
                    cw.Settings.UserAgent =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";
                    await cw.Profile.ClearBrowsingDataAsync();

                    // Navigate to login page
                    progress?.Report("Loading IT Portal...");
                    cw.Navigate(PortalBase + LoginPage);

                    var navDone = new TaskCompletionSource();
                    cw.NavigationCompleted += OnFirstNav;
                    void OnFirstNav(object? s, CoreWebView2NavigationCompletedEventArgs e)
                    { cw.NavigationCompleted -= OnFirstNav; navDone.TrySetResult(); }
                    await navDone.Task;
                    await Task.Delay(3000);

                    // Wait for TAN input field
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
                        tcs.TrySetResult(new UploadLoginResult(false, "Login form did not appear. Check internet connection."));
                        Cleanup(hiddenWv, hiddenWin); return;
                    }

                    // Fill TAN
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
                            var btn = Array.from(document.querySelectorAll('button'))
                                .find(b => /continue/i.test(b.textContent.trim()));
                            if (btn) btn.click();
                        })()");
                    await Task.Delay(2500);

                    // Fill password + tick checkbox
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
                            el.dispatchEvent(new Event('input', {bubbles:true}));
                            el.dispatchEvent(new Event('change', {bubbles:true}));
                        })()
                        """);
                    await Task.Delay(600);

                    // Click Login
                    progress?.Report("Submitting login...");
                    await cw.ExecuteScriptAsync(@"
                        (function() {
                            var btn = Array.from(document.querySelectorAll('button'))
                                .find(b => /login|sign in|continue/i.test(b.textContent.trim()));
                            if (!btn) btn = document.querySelector('button[type=""submit""]');
                            if (btn) btn.click();
                        })()");

                    // Wait for AuthToken cookie
                    progress?.Report("Waiting for login to complete...");
                    bool loggedIn = false;
                    for (int i = 0; i < 60; i++)
                    {
                        await Task.Delay(1000);
                        var url = cw.Source;

                        // Dismiss "Dual Login Detected" modal if present
                        await cw.ExecuteScriptAsync(@"
                            (function() {
                                var all = Array.from(document.querySelectorAll('button, a, span, div'));
                                var el = all.find(e => e.textContent.trim() === 'Login Here');
                                if (el) { el.click(); return; }
                                el = all.find(e => /login here/i.test(e.textContent.trim()) && e.textContent.trim().length < 20);
                                if (el) el.click();
                            })()");

                        var cookies1 = await cw.CookieManager.GetCookiesAsync(PortalBase);
                        var cookies2 = await cw.CookieManager.GetCookiesAsync("https://incometax.gov.in");
                        var hasAuth  = cookies1.Any(c => c.Name == "AuthToken") || cookies2.Any(c => c.Name == "AuthToken");
                        var onDash   = url.Contains("dashboard") || url.Contains("home") || url.Contains("myaccount");
                        progress?.Report($"Waiting for login... ({i + 1}s)");
                        if (hasAuth || onDash) { loggedIn = true; break; }
                    }

                    if (!loggedIn)
                    {
                        tcs.TrySetResult(new UploadLoginResult(false,
                            "Login did not complete — check credentials in Settings → IT Portal."));
                        Cleanup(hiddenWv, hiddenWin); return;
                    }

                    progress?.Report("Login successful. Opening upload portal...");

                    // ── Phase 2: copy cookies into a new visible WebView2 ───────
                    var allCookies1 = await cw.CookieManager.GetCookiesAsync(PortalBase);
                    var allCookies2 = await cw.CookieManager.GetCookiesAsync("https://incometax.gov.in");

                    Cleanup(hiddenWv, hiddenWin);
                    hiddenWv = null; hiddenWin = null;

                    visibleWin = new Window
                    {
                        Title         = "IT Portal — Upload TDS Return",
                        Width         = 1280,
                        Height        = 800,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        WindowState   = WindowState.Normal,
                    };
                    visibleWv = new WebView2();
                    visibleWin.Content = visibleWv;
                    visibleWin.Show();

                    await visibleWv.EnsureCoreWebView2Async();
                    var vcw = visibleWv.CoreWebView2;
                    vcw.Settings.UserAgent =
                        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/148.0.0.0 Safari/537.36";

                    // Transfer cookies from hidden session to visible window
                    foreach (var c in allCookies1.Concat(allCookies2))
                    {
                        try
                        {
                            var nc = vcw.CookieManager.CreateCookie(c.Name, c.Value, c.Domain, c.Path);
                            nc.IsSecure   = c.IsSecure;
                            nc.IsHttpOnly = c.IsHttpOnly;
                            nc.SameSite   = c.SameSite;
                            vcw.CookieManager.AddOrUpdateCookie(nc);
                        }
                        catch { }
                    }

                    // Navigate to the e-TDS filing upload page
                    vcw.Navigate(PortalBase + "/iec/foservices/#/tdsmenu/tdsMainMenu");

                    tcs.TrySetResult(new UploadLoginResult(true, null));
                }
                catch (Exception ex)
                {
                    tcs.TrySetResult(new UploadLoginResult(false, "Unexpected error: " + ex.Message));
                    Cleanup(hiddenWv, hiddenWin);
                    Cleanup(visibleWv, visibleWin);
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
