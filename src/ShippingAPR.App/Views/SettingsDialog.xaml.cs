using System.Diagnostics;
using System.Windows;
using ShippingAPR.App.ViewModels;

namespace ShippingAPR.App.Views;

public partial class SettingsDialog : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsDialog(SettingsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;

        MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); }
                catch (InvalidOperationException) { }
            }
        };
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SaveToAppsettings())
        {
            // Provider/key/poll changes are read once at startup (IOptions), so they
            // only take effect after a restart. Offer to restart now rather than
            // claiming "Save & Restart" and doing nothing.
            var restart = MessageBox.Show(
                "Settings saved. Restart now to apply the changes?",
                "ShippingAPR", MessageBoxButton.YesNo, MessageBoxImage.Question);

            DialogResult = true;
            Close();

            if (restart == MessageBoxResult.Yes)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    Process.Start(exePath);
                Application.Current.Shutdown();
            }
        }
        else
        {
            MessageBox.Show(
                "Failed to save settings to appsettings.json.\nYou can edit the file manually.",
                "ShippingAPR", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void CancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
