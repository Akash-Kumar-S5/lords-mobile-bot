using App.UI.Commands;
using Bot.Core.Enums;
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
    private readonly ITaskSchedulerService _taskSchedulerService;
    private readonly IBotModeController _modeController;
    private readonly DispatcherQueue? _dispatcherQueue;
    private string _statusText = "Stopped";
    private string _maxArmyLimitText = "1";
    private bool _searchStone;
    private bool _searchWood;
    private bool _searchOre;
    private bool _searchFood;
    private bool _searchRune;

    public MainViewModel(
        IBotEngine botEngine,
        ITemplateVerifier templateVerifier,
        ILogEventStream logEventStream,
        IRuntimeBotSettings runtimeBotSettings,
        ITaskSchedulerService taskSchedulerService,
        IBotModeController modeController)
    {
        _botEngine = botEngine;
        _templateVerifier = templateVerifier;
        _logEventStream = logEventStream;
        _runtimeBotSettings = runtimeBotSettings;
        _taskSchedulerService = taskSchedulerService;
        _modeController = modeController;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        _maxArmyLimitText = _runtimeBotSettings.MaxActiveMarches.ToString();
        _searchStone = _runtimeBotSettings.SearchStone;
        _searchWood = _runtimeBotSettings.SearchWood;
        _searchOre = _runtimeBotSettings.SearchOre;
        _searchFood = _runtimeBotSettings.SearchFood;
        _searchRune = _runtimeBotSettings.SearchRune;

        LogEntries = new ObservableCollection<string>(_logEventStream.Snapshot());
        _logEventStream.LogAppended += OnLogAppended;
        _taskSchedulerService.SchedulerStateChanged += OnSchedulerStateChanged;
        _modeController.ModeChanged += OnModeChanged;

        StartCommand = new AsyncRelayCommand(StartAsync, () => !_botEngine.IsRunning);
        StopCommand = new AsyncRelayCommand(StopAsync, () => _botEngine.IsRunning);
        CheckArmyLimitCommand = new AsyncRelayCommand(CheckArmyLimitAsync, () => _botEngine.IsRunning);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> LogEntries { get; }
    public ICommand StartCommand { get; }
    public ICommand StopCommand { get; }
    public ICommand CheckArmyLimitCommand { get; }

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

    public bool SearchStone
    {
        get => _searchStone;
        set
        {
            if (_searchStone == value) return;
            _searchStone = value;
            _runtimeBotSettings.SearchStone = value;
            OnPropertyChanged();
        }
    }

    public bool SearchWood
    {
        get => _searchWood;
        set
        {
            if (_searchWood == value) return;
            _searchWood = value;
            _runtimeBotSettings.SearchWood = value;
            OnPropertyChanged();
        }
    }

    public bool SearchOre
    {
        get => _searchOre;
        set
        {
            if (_searchOre == value) return;
            _searchOre = value;
            _runtimeBotSettings.SearchOre = value;
            OnPropertyChanged();
        }
    }

    public bool SearchFood
    {
        get => _searchFood;
        set
        {
            if (_searchFood == value) return;
            _searchFood = value;
            _runtimeBotSettings.SearchFood = value;
            OnPropertyChanged();
        }
    }

    public bool SearchRune
    {
        get => _searchRune;
        set
        {
            if (_searchRune == value) return;
            _searchRune = value;
            _runtimeBotSettings.SearchRune = value;
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
        _runtimeBotSettings.SearchStone = SearchStone;
        _runtimeBotSettings.SearchWood = SearchWood;
        _runtimeBotSettings.SearchOre = SearchOre;
        _runtimeBotSettings.SearchFood = SearchFood;
        _runtimeBotSettings.SearchRune = SearchRune;

        var verify = await _templateVerifier.VerifyAsync();
        if (!verify.IsValid)
        {
            StatusText = "Missing Templates";
            RefreshCommands();
            return;
        }

        await _botEngine.StartAsync();
        StatusText = BuildStatusText();
        RefreshCommands();
    }

    private async Task StopAsync()
    {
        await _botEngine.StopAsync();
        StatusText = "Stopped";
        RefreshCommands();
    }

    private Task CheckArmyLimitAsync()
    {
        _taskSchedulerService.RequestImmediateArmyCheck();
        StatusText = BuildStatusText();
        RefreshCommands();
        return Task.CompletedTask;
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

        if (CheckArmyLimitCommand is AsyncRelayCommand check)
        {
            check.RaiseCanExecuteChanged();
        }
    }

    private void OnLogAppended(string message)
    {
        if (_dispatcherQueue is null)
        {
            LogEntries.Add(message);
            return;
        }

        _ = _dispatcherQueue.TryEnqueue(() => LogEntries.Add(message));
    }

    private void OnModeChanged(BotRunMode _, string __)
    {
        if (_dispatcherQueue is null)
        {
            StatusText = BuildStatusText();
            RefreshCommands();
            return;
        }

        var _queued = _dispatcherQueue.TryEnqueue(() =>
        {
            StatusText = BuildStatusText();
            RefreshCommands();
        });
    }

    private void OnSchedulerStateChanged()
    {
        if (_dispatcherQueue is null)
        {
            StatusText = BuildStatusText();
            RefreshCommands();
            return;
        }

        _ = _dispatcherQueue.TryEnqueue(() =>
        {
            StatusText = BuildStatusText();
            RefreshCommands();
        });
    }

    private string BuildStatusText()
    {
        if (!_botEngine.IsRunning)
        {
            return "Stopped";
        }

        var mode = _modeController.CurrentMode;
        if (mode == BotRunMode.ArmyMonitor)
        {
            if (_taskSchedulerService.NextArmyCheckUtc is { } next)
            {
                var remaining = next - DateTimeOffset.UtcNow;
                var minutes = Math.Max(0, (int)Math.Ceiling(remaining.TotalMinutes));
                return $"Army Monitor (next check in {minutes} min)";
            }

            return "Army Monitor";
        }

        return "Running";
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
