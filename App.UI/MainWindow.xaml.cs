using App.UI.ViewModels;
using Microsoft.UI.Xaml;
using System.Collections.Specialized;

namespace App.UI;

public sealed partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        RootGrid.DataContext = viewModel;
        _viewModel.LogEntries.CollectionChanged += OnLogEntriesChanged;
        Closed += OnClosed;
    }

    private void OnLogEntriesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is not NotifyCollectionChangedAction.Add and not NotifyCollectionChangedAction.Reset)
        {
            return;
        }

        _ = DispatcherQueue.TryEnqueue(() =>
        {
            LogsScrollViewer.ChangeView(null, LogsScrollViewer.ScrollableHeight, null, true);
        });
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _viewModel.LogEntries.CollectionChanged -= OnLogEntriesChanged;
        Closed -= OnClosed;
    }
}
