using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using ShippingAPR.App.Resources;

namespace ShippingAPR.App.Views;

public partial class WelcomeDialog : Window
{
    public string ApiKey { get; private set; } = string.Empty;
    public string SelectedProvider { get; private set; } = "AisStream";

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open browser: {ex.Message}");
        }
    }

    private void GetStartedClick(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyInput.Text.Trim();

        if (string.IsNullOrEmpty(key))
        {
            TestResultText.Text = Strings.ConnectionFailed;
            TestResultText.Foreground = (SolidColorBrush)FindResource("ErrorBrush");
            TestResultText.Visibility = Visibility.Visible;
            return;
        }

        ApiKey = key;
        SelectedProvider = ProviderSelector.SelectedIndex switch
        {
            1 => "Datalastic",
            2 => "DataDocked",
            _ => "AisStream"
        };
        DialogResult = true;
        Close();
    }

    private void SkipClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
