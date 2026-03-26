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
            MessageBox.Show(
                "Settings saved. Restart the application for changes to take effect.",
                "ShippingAPR", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
            Close();
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
