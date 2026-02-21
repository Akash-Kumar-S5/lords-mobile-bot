using App.UI.ViewModels;
using Microsoft.UI.Xaml;

namespace App.UI;

public sealed partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        RootGrid.DataContext = viewModel;
    }
}
