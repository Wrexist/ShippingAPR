using System.Windows;

namespace ShippingAPR.App;

/// <summary>
/// Custom entry point to catch crashes that happen before OnStartup
/// (e.g. XAML resource dictionary loading, theme parsing, etc.).
/// </summary>
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            var app = new App();
            app.InitializeComponent();
            app.Run();
        }
        catch (Exception ex)
        {
            var message = $"ShippingAPR failed to start.\n\n{ex.GetType().Name}: {ex.Message}";
            if (ex.InnerException is not null)
                message += $"\n\nInner: {ex.InnerException.Message}";
            message += $"\n\nStack trace:\n{ex.StackTrace}";

            MessageBox.Show(message, "ShippingAPR - Startup Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
