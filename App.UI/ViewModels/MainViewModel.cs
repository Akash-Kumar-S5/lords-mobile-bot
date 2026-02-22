using App.UI.Commands;
using Bot.Core.Interfaces;
using Bot.Infrastructure.Configuration;
using Bot.Infrastructure.Logging;
using Bot.Tasks.Interfaces;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace App.UI.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly IBotEngine _botEngine;
    private readonly ITemplateVerifier _templateVerifier;
    private readonly ILogEventStream _logEventStream;
    private readonly IRuntimeBotSettings _runtimeBotSettings;
    private readonly DispatcherQueue _dispatcherQueue;
    private string _statusText = "Stopped";
    private string _maxArmyLimitText = "1";

    public MainViewModel(
        IBotEngine botEngine,
        ITemplateVerifier templateVerifier,
        ILogEventStream logEventStream,
        IRuntimeBotSettings runtimeBotSettings)
    {
        _botEngine = botEngine;
        _templateVerifier = templateVerifier;
        _logEventStream = logEventStream;
        _runtimeBotSettings = runtimeBotSettings;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? throw new InvalidOperationException("UI dispatcher unavailable.");
        _maxArmyLimitText = _runtimeBotSettings.MaxActiveMarches.ToString();

        LogEntries = new ObservableCollection<string>(_logEventStream.Snapshot());
        _logEventStream.LogAppended += OnLogAppended;

        StartCommand = new AsyncRelayCommand(StartAsync, () => !_botEngine.IsRunning);
        StopCommand = new AsyncRelayCommand(StopAsync, () => _botEngine.IsRunning);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> LogEntries { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
            {
                return;
            }

            _statusText = value;
            OnPropertyChanged();
        }
    }

    public string MaxArmyLimitText
    {
        get => _maxArmyLimitText;
        set
        {
            if (_maxArmyLimitText == value)
            {
                return;
            }

            _maxArmyLimitText = value;
            OnPropertyChanged();
        }
    }

    private async Task StartAsync()
    {
        if (!int.TryParse(MaxArmyLimitText, out var maxArmyLimit) || maxArmyLimit < 1)
        {
            StatusText = "Invalid Max Army";
            RefreshCommands();
            return;
        }

        _runtimeBotSettings.MaxActiveMarches = maxArmyLimit;

        var verify = await _templateVerifier.VerifyAsync();
        if (!verify.IsValid)
        {
            StatusText = "Missing Templates";
            RefreshCommands();
            return;
        }

        await _botEngine.StartAsync();
        StatusText = "Running";
        RefreshCommands();
    }

    private async Task StopAsync()
    {
        await _botEngine.StopAsync();
        StatusText = "Stopped";
        RefreshCommands();
    }

    private void RefreshCommands()
    {
        if (StartCommand is AsyncRelayCommand start)
        {
            start.RaiseCanExecuteChanged();
        }

        if (StopCommand is AsyncRelayCommand stop)
        {
            stop.RaiseCanExecuteChanged();
        }
    }

    private void OnLogAppended(string message)
    {
        _ = _dispatcherQueue.TryEnqueue(() => LogEntries.Add(message));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
