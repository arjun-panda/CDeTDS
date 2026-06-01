using Microsoft.Win32;
using System.Windows;

namespace CDeTDS.App.Services
{
    /// <summary>
    /// Native WPF file picker — must be called from Blazor via InvokeAsync on the UI thread.
    /// </summary>
    public class FilePickerService
    {
        /// <summary>Open a file picker and return the selected path, or null if cancelled.</summary>
        public string? PickFile(string title, string filter = "Excel Files|*.xlsx|All Files|*.*")
        {
            string? result = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new OpenFileDialog
                {
                    Title            = title,
                    Filter           = filter,
                    CheckFileExists  = true,
                    CheckPathExists  = true,
                };
                if (dlg.ShowDialog(Application.Current.MainWindow) == true)
                    result = dlg.FileName;
            });
            return result;
        }

        /// <summary>Open a save-file picker and return the selected path, or null if cancelled.</summary>
        public string? PickSaveFile(string title, string defaultName, string filter = "Excel Files|*.xlsx")
        {
            string? result = null;
            Application.Current.Dispatcher.Invoke(() =>
            {
                var dlg = new SaveFileDialog
                {
                    Title           = title,
                    FileName        = defaultName,
                    Filter          = filter,
                    OverwritePrompt = true,
                };
                if (dlg.ShowDialog(Application.Current.MainWindow) == true)
                    result = dlg.FileName;
            });
            return result;
        }
    }
}
