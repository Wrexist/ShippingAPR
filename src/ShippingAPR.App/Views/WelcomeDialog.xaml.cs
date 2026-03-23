using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace ShippingAPR.App.Views;

public partial class WelcomeDialog : Window
{
    public string ApiKey { get; private set; } = string.Empty;

    public WelcomeDialog()
    {
        InitializeComponent();

        // Allow window dragging
        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { /* DragMove can throw if called during button click routing */ }
            }
        };
    }

    private void OpenAisStreamSite(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://aisstream.io",
                UseShellExecute = true
            });
        }
        catch
        {
            // Silently fail if browser can't be opened
        }
    }

    private void GetStartedClick(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyInput.Text.Trim();

        if (string.IsNullOrEmpty(key))
        {
            TestResultText.Text = "Please enter an API key to continue.";
            TestResultText.Foreground = new SolidColorBrush(Color.FromRgb(255, 61, 113));
            TestResultText.Visibility = Visibility.Visible;
            return;
        }

        ApiKey = key;
        DialogResult = true;
        Close();
    }

    private void SkipClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
