using Velopack;

namespace TDSPro.App
{
    public class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            // Must be first — handles Velopack install/uninstall/update hooks
            VelopackApp.Build().Run();

            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
    }
}
