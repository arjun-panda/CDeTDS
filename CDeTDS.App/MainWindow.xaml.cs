using Microsoft.AspNetCore.Components.WebView;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace CDeTDS.App
{
    public partial class MainWindow : Window
    {
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CDeTDS", "Logs", "circuit.log");

        public MainWindow()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            blazorWebView.BlazorWebViewInitialized += OnBlazorInitialized;
        }

        private void OnBlazorInitialized(object? sender, BlazorWebViewInitializedEventArgs e)
        {
            // Log WebView2 process crashes — these cause "socket closed unexpectedly"
            e.WebView.CoreWebView2.ProcessFailed += (s, args) =>
            {
                var msg = $"WebView2 process failed: Kind={args.ProcessFailedKind} Reason={args.Reason}";
                LogCircuit(msg);
                // Reload the WebView2 to recover
                Dispatcher.InvokeAsync(async () =>
                {
                    await Task.Delay(500);
                    try { e.WebView.CoreWebView2.Reload(); } catch { }
                });
            };

            e.WebView.CoreWebView2.NavigationCompleted += (s, args) =>
            {
                if (!args.IsSuccess)
                    LogCircuit($"Navigation failed: HttpStatusCode={args.HttpStatusCode}");
            };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            blazorWebView.Focus();
            Keyboard.Focus(blazorWebView);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            blazorWebView.Focus();
        }

        private static void LogCircuit(string msg)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {msg}\n");
            }
            catch { }
        }
    }
}
