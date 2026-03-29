using App.UI.ViewModels;
using Avalonia.Controls;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Threading;
using System.Collections.Specialized;

namespace App.UI;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow() : this(App.Services.GetRequiredService<MainViewModel>()) { }

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.LogEntries.CollectionChanged += OnLogEntriesChanged;
        Closed += OnClosed;
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not NotifyCollectionChangedAction.Add and not NotifyCollectionChangedAction.Reset)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            LogsScrollViewer.Offset = new Avalonia.Vector(LogsScrollViewer.Offset.X, double.MaxValue);
        });
    }

    private void OnClosed(object? sender, EventArgs args)
    {
        _viewModel.LogEntries.CollectionChanged -= OnLogEntriesChanged;
        Closed -= OnClosed;
    }
}
