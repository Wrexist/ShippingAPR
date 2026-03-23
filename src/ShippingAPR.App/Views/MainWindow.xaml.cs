using System.Windows;
using ShippingAPR.App.ViewModels;

namespace ShippingAPR.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
