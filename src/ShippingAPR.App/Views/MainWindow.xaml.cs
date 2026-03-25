using System.Windows;
using System.Windows.Input;
using ShippingAPR.App.Configuration;
using ShippingAPR.App.ViewModels;

namespace ShippingAPR.App.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private readonly UserPreferences? _preferences;

    public MainWindow(MainViewModel viewModel, UserPreferences? preferences = null)
    {
        InitializeComponent();
        DataContext = viewModel;
        _viewModel = viewModel;
        _preferences = preferences;

        // Restore window size/position from preferences
        if (_preferences is not null)
        {
            Width = _preferences.WindowWidth;
            Height = _preferences.WindowHeight;
            if (!double.IsNaN(_preferences.WindowLeft) && !double.IsNaN(_preferences.WindowTop))
            {
                Left = _preferences.WindowLeft;
                Top = _preferences.WindowTop;
                WindowStartupLocation = WindowStartupLocation.Manual;
            }
        }

        // Register keyboard shortcuts
        InputBindings.Add(new KeyBinding(viewModel.SearchViewModel.FocusSearchCommand, Key.F, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(viewModel.DismissNotificationCommand, Key.Escape, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(viewModel.MapViewModel.ZoomInCommand, Key.OemPlus, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(viewModel.MapViewModel.ZoomOutCommand, Key.OemMinus, ModifierKeys.None));
        InputBindings.Add(new KeyBinding(viewModel.MapViewModel.ToggleMeasurementCommand, Key.M, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(viewModel.MapViewModel.ToggleWeatherOverlayCommand, Key.W, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(viewModel.ToggleNotificationCenterCommand, Key.N, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(viewModel.ExportCsvCommand, Key.E, ModifierKeys.Control));
        InputBindings.Add(new KeyBinding(viewModel.ShowKeyboardShortcutsCommand, Key.F1, ModifierKeys.None));

        // Tab shortcuts: 1-5 for right panels
        PreviewKeyDown += (_, args) =>
        {
            if (args.KeyboardDevice.Modifiers != ModifierKeys.None) return;
            var panelIdx = args.Key switch
            {
                Key.D1 => 0,
                Key.D2 => 1,
                Key.D3 => 2,
                Key.D4 => 3,
                Key.D5 => 4,
                Key.D6 => 5,
                Key.D7 => 6,
                Key.D8 => 7,
                Key.D9 => 8,
                _ => -1
            };
            if (panelIdx >= 0 &&
                args.OriginalSource is not System.Windows.Controls.TextBox &&
                args.OriginalSource is not System.Windows.Controls.ComboBox &&
                args.OriginalSource is not System.Windows.Controls.RichTextBox &&
                args.OriginalSource is not System.Windows.Controls.PasswordBox)
            {
                viewModel.ShowRightPanelCommand.Execute(panelIdx.ToString());
                args.Handled = true;
            }

            // F11 fullscreen toggle
            if (args.Key == Key.F11)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                args.Handled = true;
            }
        };

        Closing += OnWindowClosing;

        // Window drag support via top bar
        TopBar.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
            }
            else
            {
                DragMove();
            }
        };
    }

    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeClick(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_preferences is null) return;

        _preferences.WindowWidth = Width;
        _preferences.WindowHeight = Height;
        _preferences.WindowLeft = Left;
        _preferences.WindowTop = Top;
        _preferences.IsDarkTheme = _viewModel.IsDarkTheme;
        _preferences.Language = _viewModel.CurrentLanguage;

        // Save filter state
        _preferences.ShowCargo = _viewModel.FilterViewModel.ShowCargo;
        _preferences.ShowTanker = _viewModel.FilterViewModel.ShowTanker;
        _preferences.ShowPassenger = _viewModel.FilterViewModel.ShowPassenger;
        _preferences.ShowFishing = _viewModel.FilterViewModel.ShowFishing;
        _preferences.ShowTugPilot = _viewModel.FilterViewModel.ShowTugPilot;
        _preferences.ShowOther = _viewModel.FilterViewModel.ShowOther;
        _preferences.MaxSpeed = _viewModel.FilterViewModel.MaxSpeed;

        _preferences.Save();
    }
}
