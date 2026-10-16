using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using DesktopDynamicIsland.Models;
using DesktopDynamicIsland.Services;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace DesktopDynamicIsland;

public partial class MainWindow : Window
{
    private const double MessageWidth = 300;
    private const double MessageHeight = 64;
    private const int MaxPendingMessages = 20;

    private readonly AppLogger _logger;
    private readonly SettingsService _settingsService;
    private readonly IslandSettings _settings;
    private readonly SystemStatusService _systemStatusService = new();
    private readonly FullscreenDetector _fullscreenDetector = new();
    private readonly FullscreenSuppressionGate _fullscreenSuppressionGate = new();
    private readonly RemoteSessionDetector _remoteSessionDetector = new();
    private readonly PerformanceAlertEngine _performanceAlertEngine = new();
    private readonly MetricPairController _metricPairController;
    private readonly IslandLayoutProfile _layoutProfile;
    private readonly bool _dataMotionEnabled;
    private readonly DesktopPlacementService _placementService = new();
    private readonly ObservableCollection<IslandMessage> _recentMessages = new();
    private readonly Queue<IslandMessage> _pendingMessages = new();
    private readonly Dictionary<DependencyObject, SolidColorBrush> _animatedSeverityBrushes = new();
    private readonly HashSet<DependencyObject> _activeDataAnimationTargets = new();
    private int _activeDataAnimationCount;
    private readonly DispatcherTimer _autoCollapseTimer;
    private readonly DispatcherTimer _messageTimer;
    private readonly DispatcherTimer _fullscreenTimer;
    private readonly DispatcherTimer _placementRecoveryTimer;

    private Forms.NotifyIcon? _notifyIcon;
    private Forms.ToolStripMenuItem? _autoStartMenuItem;
    private Forms.ToolStripMenuItem? _cpuRamMenuItem;
    private Forms.ToolStripMenuItem? _gpuVramMenuItem;
    private Icon? _ownedTrayIcon;
    private IslandVisualMode _mode = IslandVisualMode.Collapsed;
    private IslandDisplayState _displayState = IslandDisplayState.Collapsed;
    private DesktopPlacementSnapshot? _placementSnapshot;
    private CancellationTokenSource? _samplingCancellation;
    private Task? _samplingTask;
    private int _activeSamplingLoops;
    private int _sampleInProgress;
    private DateTimeOffset _lastSampleLog = DateTimeOffset.MinValue;
    private DateTimeOffset _lastSampleStarted = DateTimeOffset.MinValue;
    private int _placementRecoveryAttempts;
    private DateTimeOffset _lastSampleCompleted = DateTimeOffset.MinValue;
    private bool _isExiting;
    private bool _hiddenByUser;
    private bool _suppressedByFullscreen;
    private bool _isMessageShowing;
    private bool _temperatureAvailabilityLogged;
    private bool _startupAnimationPlayed;
    private bool _isGpuTemperatureVisible;
    private double _temperatureSegmentTargetOpacity;
    private bool _isMetricPairSwitching;
    private DateTimeOffset _resumeAlertsAfter = DateTimeOffset.MinValue;
    private SystemStatus? _lastStatus;
    private string? _lastRecordedLoadStatus;

    public MainWindow()
    {
        AppPaths.EnsureDirectories();
        _logger = new AppLogger(AppPaths.LogFile);
        _settingsService = new SettingsService(_logger);
        _settings = _settingsService.Load();
        _settings.CollapsedX = null;
        _settings.CollapsedY = null;
        _settings.ExpandedX = null;
        _settings.ExpandedY = null;
        _settings.Position = "bottom-right";
        _metricPairController = new MetricPairController(
            string.Equals(_settings.CollapsedMetricPair, "gpu-vram", StringComparison.OrdinalIgnoreCase)
                ? MetricPair.GpuVram
                : MetricPair.CpuRam);
        _dataMotionEnabled = SystemParameters.ClientAreaAnimation;
        _layoutProfile = _dataMotionEnabled
            ? IslandLayoutProfile.Normal
            : IslandLayoutProfile.ReducedMotion;

        InitializeComponent();

        Topmost = _settings.AlwaysOnTop;
        Opacity = 1;
        RecentMessagesList.ItemsSource = _recentMessages;

        BuildWindowContextMenu();
        BuildTrayIcon();

        UpdateMessageEmptyState();

        _autoCollapseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(8)
        };
        _autoCollapseTimer.Tick += (_, _) => CollapseAfterInactivity();

        _messageTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(5.5)
        };
        _messageTimer.Tick += (_, _) => FinishCurrentMessage();

        _fullscreenTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2)
        };
        _fullscreenTimer.Tick += (_, _) => UpdateFullscreenSuppression();

        _placementRecoveryTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _placementRecoveryTimer.Tick += OnPlacementRecoveryTick;
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        DpiChanged += OnWindowDpiChanged;
    }

    private enum IslandVisualMode
    {
        Collapsed,
        Message,
        Expanded
    }

    private bool IsGeometryTransitioning =>
        _displayState is IslandDisplayState.Expanding or IslandDisplayState.Collapsing;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SetMode(IslandVisualMode.Collapsed, animate: false);
        AnimateStartupEntrance();
        StartSamplingLoop();
        _fullscreenTimer.Start();
        UpdateFullscreenSuppression();
        RestartTaskbarRecoveryIfNeeded();
        _logger.Info("性能岛启动完成。");
    }

    private void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (_isExiting)
        {
            return;
        }

        e.Cancel = true;
        HideIsland();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        StopSamplingLoop(waitForCompletion: true);
        _autoCollapseTimer.Stop();
        _messageTimer.Stop();
        _fullscreenTimer.Stop();
        _placementRecoveryTimer.Stop();
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        DpiChanged -= OnWindowDpiChanged;
        _systemStatusService.Dispose();

        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        _ownedTrayIcon?.Dispose();
    }

    private void StartSamplingLoop()
    {
        if (_samplingTask is { IsCompleted: false })
        {
            _logger.Warn($"采样循环启动被忽略：已有活动循环，count={Volatile.Read(ref _activeSamplingLoops)}。");
            return;
        }

        _samplingCancellation?.Dispose();
        _samplingCancellation = new CancellationTokenSource();
        _samplingTask = RunSamplingLoopAsync(_samplingCancellation.Token);
    }

    private void StopSamplingLoop(bool waitForCompletion = false)
    {
        var cancellation = _samplingCancellation;
        var task = _samplingTask;
        _samplingCancellation = null;
        _samplingTask = null;
        cancellation?.Cancel();

        if (waitForCompletion && task is not null)
        {
            try
            {
                _ = task.Wait(TimeSpan.FromSeconds(2));
            }
            catch (AggregateException exception) when (exception.InnerExceptions.All(item => item is OperationCanceledException))
            {
            }
        }

        cancellation?.Dispose();
        _logger.Info($"采样停止请求完成：activeLoops={Volatile.Read(ref _activeSamplingLoops)}, cancelled={cancellation is not null}。");
    }

    private async Task RunSamplingLoopAsync(CancellationToken cancellationToken)
    {
        var loopCount = Interlocked.Increment(ref _activeSamplingLoops);
        _logger.Info($"采样循环启动：activeLoops={loopCount}。");
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var started = DateTimeOffset.Now;
                var previousStarted = _lastSampleStarted;
                _lastSampleStarted = started;
                var interval = SamplingCadence.ForState(_displayState);
                if (Interlocked.Exchange(ref _sampleInProgress, 1) != 0)
                {
                    _logger.Warn("检测到采样重入，已跳过本轮。");
                    await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    var status = await Task.Run(_systemStatusService.ReadStatus, cancellationToken).ConfigureAwait(false);
                    var completed = DateTimeOffset.Now;
                    _lastSampleCompleted = completed;
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        _ = Dispatcher.BeginInvoke(() => ApplySystemStatus(status), DispatcherPriority.DataBind);
                    }

                    if (completed - _lastSampleLog >= TimeSpan.FromSeconds(5))
                    {
                        _lastSampleLog = completed;
                        var actualInterval = previousStarted == DateTimeOffset.MinValue
                            ? TimeSpan.Zero
                            : started - previousStarted;
                        _logger.Info(
                            $"采样诊断：state={_displayState}, targetMs={interval.TotalMilliseconds:0}, actualMs={actualInterval.TotalMilliseconds:0}, " +
                            $"durationMs={(completed - started).TotalMilliseconds:0.0}, inProgress={Volatile.Read(ref _sampleInProgress)}, activeLoops={Volatile.Read(ref _activeSamplingLoops)}, " +
                            $"dataMotion={_dataMotionEnabled}, activeDataAnimations={Volatile.Read(ref _activeDataAnimationCount)}, " +
                            $"started={started:O}, completed={completed:O}。");
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    _logger.Error("系统状态采样失败，将在退避后继续。", exception);
                    await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    Interlocked.Exchange(ref _sampleInProgress, 0);
                }

                interval = SamplingCadence.ForState(_displayState);
                await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            var remaining = Interlocked.Decrement(ref _activeSamplingLoops);
            _logger.Info($"采样循环退出：activeLoops={remaining}, lastCompleted={_lastSampleCompleted:O}。");
        }
    }

    private void BuildTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("展开 / 收起", null, (_, _) => ToggleExpandedFromTray());
        menu.Items.Add("显示 / 隐藏", null, (_, _) => ToggleVisibility());
        menu.Items.Add("复位到右下角", null, (_, _) => ResetPosition());
        var metricMenu = new Forms.ToolStripMenuItem("收起态指标");
        _cpuRamMenuItem = new Forms.ToolStripMenuItem("CPU + RAM");
        _gpuVramMenuItem = new Forms.ToolStripMenuItem("GPU + VRAM");
        _cpuRamMenuItem.Click += (_, _) => Dispatcher.Invoke(() => SelectMetricPair(MetricPair.CpuRam));
        _gpuVramMenuItem.Click += (_, _) => Dispatcher.Invoke(() => SelectMetricPair(MetricPair.GpuVram));
        metricMenu.DropDownItems.Add(_cpuRamMenuItem);
        metricMenu.DropDownItems.Add(_gpuVramMenuItem);
        menu.Items.Add(metricMenu);
        UpdateMetricPairMenuChecks();
        menu.Items.Add(new Forms.ToolStripSeparator());

        _autoStartMenuItem = new Forms.ToolStripMenuItem("开机自启动")
        {
            CheckOnClick = false,
            Checked = StartupService.IsEnabled()
        };
        _autoStartMenuItem.Click += (_, _) => ToggleAutoStart();
        menu.Items.Add(_autoStartMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _ownedTrayIcon = CreateTrayIcon();
        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _ownedTrayIcon,
            Text = "性能岛",
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ToggleExpandedFromTray);
    }

    private void BuildWindowContextMenu()
    {
        var contextMenu = new System.Windows.Controls.ContextMenu();
        contextMenu.Items.Add(CreateMenuItem("复位位置", ResetPosition));
        contextMenu.Items.Add(CreateMenuItem("隐藏到托盘", HideIsland));
        contextMenu.Items.Add(new System.Windows.Controls.Separator());
        contextMenu.Items.Add(CreateMenuItem("退出", ExitApplication));
        IslandShell.ContextMenu = contextMenu;
    }

    private static System.Windows.Controls.MenuItem CreateMenuItem(string header, Action action)
    {
        var item = new System.Windows.Controls.MenuItem { Header = header };
        item.Click += (_, _) => action();
        return item;
    }

    private void ApplySystemStatus(SystemStatus status)
    {
        var now = DateTimeOffset.Now;
        _lastStatus = status;
        _performanceAlertEngine.Update(status, now);
        RecordLoadStatusChange(status);

        if (!_suppressedByFullscreen &&
            !_hiddenByUser &&
            now >= _resumeAlertsAfter &&
            _performanceAlertEngine.TryTakeNotification() is PerformanceAlert alert)
        {
            _metricPairController.Override(alert.TargetPair, now, TimeSpan.FromSeconds(18));
            EnqueueMessage(new IslandMessage
            {
                Type = "performance-alert",
                Title = alert.Title,
                Content = alert.Content,
                Level = alert.Level,
                Time = DateTime.Now
            });
        }

        UpdateCollapsedMetrics(status, _metricPairController.GetEffectivePair(now));
        UpdateExpandedText(ExpandedCpuText, MetricValue(status.CpuText, "CPU"));
        UpdateExpandedText(ExpandedRamText, MetricValue(status.RamText, "RAM"));
        UpdateExpandedText(ExpandedGpuText, MetricValue(status.GpuText, "GPU"));
        UpdateExpandedText(ExpandedVramText, MetricValue(status.VramText, "VRAM"));
        UpdateExpandedText(ExpandedGpuTemperatureText, status.GpuTemperatureValueText);
        UpdateExpandedText(ExpandedDiskText, MetricValue(status.DiskWriteText, "写入"));
        UpdateExpandedText(ExpandedStatusText, status.LoadStatusText);
        if (!_temperatureAvailabilityLogged)
        {
            _temperatureAvailabilityLogged = true;
            _logger.Info(
                $"温度段诊断：sample={status.GpuTemperatureText}, valid={_isGpuTemperatureVisible}, visibility={TemperatureBorderSegment.Visibility}, " +
                $"opacity={_temperatureSegmentTargetOpacity:0.##}, color={TemperatureSegmentBrush.Color}, path={TemperatureBorderSegment.Data}; CPU temperature source unavailable.");
        }

        ApplyValueSeverity(status.CpuSeverity, ExpandedCpuText);
        ApplyValueSeverity(status.RamSeverity, ExpandedRamText);
        ApplyValueSeverity(status.GpuSeverity, ExpandedGpuText);
        ApplyValueSeverity(status.VramSeverity, ExpandedVramText);
        ApplyValueSeverity(status.GpuTemperatureSeverity, ExpandedGpuTemperatureText);
        ApplyValueSeverity(status.LoadStatusSeverity, ExpandedStatusText);
        ApplyAnimatedSeverity(status.LoadStatusSeverity, ExpandedStatusIndicator, isFill: true);
    }

    private void UpdateCollapsedMetrics(SystemStatus status, MetricPair pair)
    {
        if (pair == MetricPair.CpuRam)
        {
            CollapsedPrimaryLabel.Text = "CPU";
            CollapsedPrimaryText.Text = MetricValue(status.CpuText, "CPU");
            CollapsedSecondaryLabel.Text = "RAM";
            CollapsedSecondaryText.Text = MetricValue(status.RamText, "RAM");
            ApplyCollapsedSeverity(status.CpuSeverity, CollapsedPrimaryIndicator, CollapsedPrimaryText);
            ApplyCollapsedSeverity(status.RamSeverity, CollapsedSecondaryIndicator, CollapsedSecondaryText);
        }
        else
        {
            CollapsedPrimaryLabel.Text = "GPU";
            CollapsedPrimaryText.Text = MetricValue(status.GpuText, "GPU");
            CollapsedSecondaryLabel.Text = "VRAM";
            CollapsedSecondaryText.Text = MetricValue(status.VramText, "VRAM");
            ApplyCollapsedSeverity(status.GpuSeverity, CollapsedPrimaryIndicator, CollapsedPrimaryText);
            ApplyCollapsedSeverity(status.VramSeverity, CollapsedSecondaryIndicator, CollapsedSecondaryText);
        }

        var hasGpuTemperature = status.GpuTemperatureCelsius is > 0 and < 125;
        UpdateCollapsedGpuTemperatureAppearance(status, hasGpuTemperature);
        if (_isGpuTemperatureVisible != hasGpuTemperature)
        {
            _isGpuTemperatureVisible = hasGpuTemperature;
            AnimateCollapsedGpuTemperature(hasGpuTemperature);
        }
    }

    private void UpdateCollapsedGpuTemperatureAppearance(SystemStatus status, bool isAvailable)
    {
        if (!isAvailable || status.GpuTemperatureCelsius is not double temperature)
        {
            IslandShell.ToolTip = null;
            _temperatureSegmentTargetOpacity = 0;
            return;
        }

        var severity = status.GpuTemperatureSeverity;
        var targetColor = severity switch
        {
            MetricSeverity.Danger => System.Windows.Media.Color.FromRgb(207, 102, 105),
            MetricSeverity.Warning => System.Windows.Media.Color.FromRgb(205, 151, 78),
            _ => System.Windows.Media.Color.FromRgb(114, 148, 155)
        };

        IslandShell.ToolTip = $"GPU 温度：{temperature:0}°C";
        TemperatureSegmentBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        if (SystemParameters.ClientAreaAnimation && TemperatureSegmentBrush.Color != targetColor)
        {
            TemperatureSegmentBrush.BeginAnimation(
                SolidColorBrush.ColorProperty,
                new ColorAnimation(TemperatureSegmentBrush.Color, targetColor, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                },
                HandoffBehavior.SnapshotAndReplace);
        }

        TemperatureSegmentBrush.Color = targetColor;
        _temperatureSegmentTargetOpacity = severity switch
        {
            MetricSeverity.Danger => 0.96,
            MetricSeverity.Warning => 0.9,
            _ => 0.78
        };

        if (_isGpuTemperatureVisible && _mode == IslandVisualMode.Collapsed && !IsGeometryTransitioning)
        {
            TemperatureBorderSegment.Opacity = _temperatureSegmentTargetOpacity;
        }
    }

    private void EnqueueMessage(IslandMessage message)
    {
        message.Time = message.Time == default ? DateTime.Now : message.Time;
        message.Type = string.IsNullOrWhiteSpace(message.Type) ? "system-status" : message.Type;
        message.Level = string.IsNullOrWhiteSpace(message.Level) ? "info" : message.Level;

        AddHistoryMessage(message);
        if (_suppressedByFullscreen || _hiddenByUser)
        {
            _logger.Info($"静默状态下仅记录性能告警：{message.Title}");
            return;
        }

        _pendingMessages.Enqueue(message);
        while (_pendingMessages.Count > MaxPendingMessages)
        {
            _pendingMessages.Dequeue();
        }
        _logger.Info($"性能告警入队：{message.Title}");

        if (!_isMessageShowing &&
            _mode != IslandVisualMode.Expanded &&
            !_hiddenByUser &&
            !_suppressedByFullscreen &&
            IsVisible)
        {
            ShowNextQueuedMessage();
        }
    }

    private void RecordLoadStatusChange(SystemStatus status)
    {
        if (string.Equals(_lastRecordedLoadStatus, status.LoadStatusText, StringComparison.Ordinal))
        {
            return;
        }

        _lastRecordedLoadStatus = status.LoadStatusText;
        AddHistoryMessage(new IslandMessage
        {
            Type = "system-status",
            Title = status.LoadStatusText,
            Content = $"{status.CpuText} · {status.RamText} · {status.GpuText} · {status.VramText}",
            Level = status.LoadStatusSeverity switch
            {
                MetricSeverity.Danger => "error",
                MetricSeverity.Warning => "warning",
                _ => "success"
            },
            Time = DateTime.Now
        });
    }

    private void AddHistoryMessage(IslandMessage message)
    {
        message.Time = message.Time == default ? DateTime.Now : message.Time;

        var duplicate = _recentMessages.FirstOrDefault(item =>
            item.Title == message.Title &&
            item.Content == message.Content);
        if (duplicate is not null)
        {
            _recentMessages.Remove(duplicate);
        }

        _recentMessages.Insert(0, message);
        while (_recentMessages.Count > 1)
        {
            _recentMessages.RemoveAt(_recentMessages.Count - 1);
        }

        UpdateMessageEmptyState();
    }

    private void ShowNextQueuedMessage()
    {
        if (_pendingMessages.Count == 0 || _suppressedByFullscreen || _hiddenByUser)
        {
            return;
        }

        var message = _pendingMessages.Dequeue();
        var visual = MessageVisual.From(message);
        MessageTitleText.Text = message.Title;
        MessageContentText.Text = message.Content;
        MessageIconText.Text = visual.Icon;
        MessageBadge.Background = visual.Brush;

        _isMessageShowing = true;
        SetMode(IslandVisualMode.Message);
        _messageTimer.Stop();
        _messageTimer.Start();
    }

    private void FinishCurrentMessage()
    {
        _messageTimer.Stop();
        _isMessageShowing = false;

        if (_pendingMessages.Count > 0)
        {
            ShowNextQueuedMessage();
            return;
        }

        if (!_hiddenByUser && !_suppressedByFullscreen)
        {
            SetMode(IslandVisualMode.Collapsed);
        }
    }

    private void UpdateMessageEmptyState()
    {
        EmptyMessagesText.Visibility = _recentMessages.Count == 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        RecentMessagesList.Visibility = _recentMessages.Count == 0
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void SetMode(IslandVisualMode mode, bool animate = true)
    {
        if (animate && IsGeometryTransitioning)
        {
            _logger.Warn($"窗口状态切换重入被忽略：state={_displayState}, requested={mode}。");
            return;
        }

        var targetWidth = mode switch
        {
            IslandVisualMode.Collapsed => _layoutProfile.CollapsedWidth,
            IslandVisualMode.Message => MessageWidth,
            IslandVisualMode.Expanded => _layoutProfile.ExpandedWidth,
            _ => _layoutProfile.CollapsedWidth
        };
        var targetHeight = mode switch
        {
            IslandVisualMode.Collapsed => _layoutProfile.CollapsedHeight,
            IslandVisualMode.Message => MessageHeight,
            IslandVisualMode.Expanded => _layoutProfile.ExpandedHeight,
            _ => _layoutProfile.CollapsedHeight
        };

        var previousMode = _mode;
        _mode = mode;

        if (animate && IsLoaded)
        {
            if (mode == IslandVisualMode.Expanded && previousMode != IslandVisualMode.Expanded)
            {
                SetDisplayState(IslandDisplayState.Expanding);
            }
            else if (previousMode == IslandVisualMode.Expanded && mode != IslandVisualMode.Expanded)
            {
                SetDisplayState(IslandDisplayState.Collapsing);
            }
        }

        if (!animate || !IsLoaded)
        {
            StopModeAnimations();
            Width = targetWidth;
            Height = targetHeight;
            ApplyTargetPosition(mode, targetWidth, targetHeight);
            SetFinalViewState(mode);
            return;
        }

        AnimateModeTransition(previousMode, mode, targetWidth, targetHeight);

        if (mode == IslandVisualMode.Expanded)
        {
            ResetAutoCollapseTimer();
        }
        else
        {
            _autoCollapseTimer.Stop();
        }
    }

    private void SetFinalViewState(IslandVisualMode mode)
    {
        var finalWidth = mode switch
        {
            IslandVisualMode.Collapsed => _layoutProfile.CollapsedWidth,
            IslandVisualMode.Message => MessageWidth,
            IslandVisualMode.Expanded => _layoutProfile.ExpandedWidth,
            _ => _layoutProfile.CollapsedWidth
        };
        var finalHeight = mode switch
        {
            IslandVisualMode.Collapsed => _layoutProfile.CollapsedHeight,
            IslandVisualMode.Message => MessageHeight,
            IslandVisualMode.Expanded => _layoutProfile.ExpandedHeight,
            _ => _layoutProfile.CollapsedHeight
        };

        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        Width = finalWidth;
        Height = finalHeight;
        IslandShell.Height = GetShellHeight(mode, finalHeight);
        ApplyTargetPosition(mode, finalWidth, finalHeight);

        CollapsedView.Visibility = mode == IslandVisualMode.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        MessageView.Visibility = mode == IslandVisualMode.Message
            ? Visibility.Visible
            : Visibility.Collapsed;
        ExpandedView.Visibility = mode == IslandVisualMode.Expanded
            ? Visibility.Visible
            : Visibility.Collapsed;

        CollapsedView.Opacity = mode == IslandVisualMode.Collapsed ? 1 : 0;
        MessageView.Opacity = mode == IslandVisualMode.Message ? 1 : 0;
        ExpandedView.Opacity = mode == IslandVisualMode.Expanded ? 1 : 0;
        TemperatureBorderSegment.Visibility = mode == IslandVisualMode.Collapsed
            ? Visibility.Visible
            : Visibility.Collapsed;
        TemperatureBorderSegment.Opacity = mode == IslandVisualMode.Collapsed && _isGpuTemperatureVisible
            ? _temperatureSegmentTargetOpacity
            : 0;
        IslandShell.CornerRadius = new CornerRadius(mode == IslandVisualMode.Collapsed ? 16 : 15);
        ShellShadow.BlurRadius = mode == IslandVisualMode.Expanded ? 24 : 9;
        ShellShadow.Opacity = mode == IslandVisualMode.Expanded ? 0.42 : 0.28;
        ShellScaleTransform.ScaleX = 1;
        ShellScaleTransform.ScaleY = 1;
        ShellTranslateTransform.Y = 0;
        ExpandedHeaderTranslate.Y = 0;
        ExpandedMetricsTranslate.Y = 0;
        ExpandedHistoryTranslate.Y = 0;
        if (mode == IslandVisualMode.Collapsed)
        {
            _settingsService.Save(_settings);
        }
        SetDisplayState(mode == IslandVisualMode.Expanded
            ? IslandDisplayState.Expanded
            : IslandDisplayState.Collapsed);
    }

    private void AnimateModeTransition(
        IslandVisualMode previousMode,
        IslandVisualMode mode,
        double targetWidth,
        double targetHeight)
    {
        var fromWidth = ActualWidth > 0 ? ActualWidth : Width;
        var fromHeight = ActualHeight > 0 ? ActualHeight : Height;
        var fromShellHeight = IslandShell.ActualHeight > 0 ? IslandShell.ActualHeight : IslandShell.Height;
        var fromLeft = Left;
        var fromTop = Top;
        StopModeAnimations();

        var targetPosition = GetTargetPosition(mode, targetWidth, targetHeight);
        var isExpandedTransition = mode == IslandVisualMode.Expanded || previousMode == IslandVisualMode.Expanded;
        if (!isExpandedTransition)
        {
            Width = targetWidth;
            Height = targetHeight;
            Left = targetPosition.X;
            Top = targetPosition.Y;
            SetFinalViewState(mode);
            AnimateShellEntrance();
            return;
        }

        CollapsedView.Visibility = Visibility.Visible;
        ExpandedView.Visibility = Visibility.Visible;
        MessageView.Visibility = Visibility.Collapsed;

        var expanding = mode == IslandVisualMode.Expanded;
        var duration = expanding ? _layoutProfile.ExpandDuration : _layoutProfile.CollapseDuration;
        var pressDelay = TimeSpan.Zero;
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };

        Width = targetWidth;
        Height = targetHeight;
        var targetShellHeight = GetShellHeight(mode, targetHeight);
        IslandShell.Height = targetShellHeight;
        Left = targetPosition.X;
        Top = targetPosition.Y;
        IslandShell.CornerRadius = new CornerRadius(expanding ? 15 : 16);

        BeginWindowAnimation(WidthProperty, fromWidth, targetWidth, duration, pressDelay, easing);
        BeginWindowAnimation(HeightProperty, fromHeight, targetHeight, duration, pressDelay, easing);
        BeginWindowAnimation(FrameworkElement.HeightProperty, fromShellHeight, targetShellHeight, duration, pressDelay, easing);
        BeginWindowAnimation(LeftProperty, fromLeft, targetPosition.X, duration, pressDelay, easing);
        BeginWindowAnimation(TopProperty, fromTop, targetPosition.Y, duration, pressDelay, easing);

        AnimateShadow(expanding, duration, pressDelay);
        AnimateTransitionContent(expanding, duration, pressDelay);

        var completion = new DoubleAnimation(fromWidth, targetWidth, duration)
        {
            BeginTime = pressDelay,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        completion.Completed += (_, _) =>
        {
            if (_mode == mode)
            {
                SetFinalViewState(mode);
            }
        };
        BeginAnimation(WidthProperty, completion, HandoffBehavior.SnapshotAndReplace);
    }

    private void BeginWindowAnimation(
        DependencyProperty property,
        double from,
        double to,
        TimeSpan duration,
        TimeSpan beginTime,
        IEasingFunction easing)
    {
        var animation = new DoubleAnimation(from, to, duration)
        {
            BeginTime = beginTime,
            EasingFunction = easing,
            FillBehavior = FillBehavior.Stop
        };
        BeginAnimation(property, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateShadow(bool expanding, TimeSpan duration, TimeSpan beginTime)
    {
        var blur = new DoubleAnimation(
            ShellShadow.BlurRadius,
            expanding ? 24 : 9,
            duration)
        {
            BeginTime = beginTime,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        var opacity = new DoubleAnimation(
            ShellShadow.Opacity,
            expanding ? 0.42 : 0.28,
            duration)
        {
            BeginTime = beginTime,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        ShellShadow.BlurRadius = expanding ? 24 : 9;
        ShellShadow.Opacity = expanding ? 0.42 : 0.28;
        ShellShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, blur, HandoffBehavior.SnapshotAndReplace);
        ShellShadow.BeginAnimation(DropShadowEffect.OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateTransitionContent(bool expanding, TimeSpan duration, TimeSpan beginTime)
    {
        var reduced = !SystemParameters.ClientAreaAnimation;
        var revealDelay = expanding && !reduced ? TimeSpan.FromMilliseconds(55) : TimeSpan.Zero;
        var contentDuration = reduced ? duration : TimeSpan.FromMilliseconds(expanding ? 140 : 110);

        AnimateOpacity(CollapsedView, expanding ? 1 : 0, expanding ? 0 : 1, contentDuration, expanding ? TimeSpan.Zero : TimeSpan.FromMilliseconds(35));
        AnimateOpacity(ExpandedView, expanding ? 0 : 1, expanding ? 1 : 0, contentDuration, revealDelay);
        TemperatureBorderSegment.Visibility = Visibility.Visible;
        AnimateOpacity(
            TemperatureBorderSegment,
            expanding ? _temperatureSegmentTargetOpacity : 0,
            expanding ? 0 : (_isGpuTemperatureVisible ? _temperatureSegmentTargetOpacity : 0),
            contentDuration,
            expanding ? TimeSpan.Zero : TimeSpan.FromMilliseconds(35));
        ExpandedHeaderTranslate.Y = 0;
        ExpandedMetricsTranslate.Y = 0;
        ExpandedHistoryTranslate.Y = 0;
    }

    private static void AnimateOpacity(
        UIElement element,
        double from,
        double to,
        TimeSpan duration,
        TimeSpan beginTime)
    {
        element.Opacity = to;
        element.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(from, to, duration)
            {
                BeginTime = beginTime,
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void StopModeAnimations()
    {
        BeginAnimation(WidthProperty, null);
        BeginAnimation(HeightProperty, null);
        BeginAnimation(LeftProperty, null);
        BeginAnimation(TopProperty, null);
        IslandShell.BeginAnimation(HeightProperty, null);
        CollapsedView.BeginAnimation(OpacityProperty, null);
        MessageView.BeginAnimation(OpacityProperty, null);
        ExpandedView.BeginAnimation(OpacityProperty, null);
        ShellShadow.BeginAnimation(DropShadowEffect.BlurRadiusProperty, null);
        ShellShadow.BeginAnimation(DropShadowEffect.OpacityProperty, null);
        ExpandedHeaderTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ExpandedMetricsTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        ExpandedHistoryTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        TemperatureBorderSegment.BeginAnimation(OpacityProperty, null);
    }

    private double GetShellHeight(IslandVisualMode mode, double windowHeight) =>
        mode == IslandVisualMode.Collapsed
            ? _layoutProfile.CollapsedShellHeight
            : Math.Max(1, windowHeight - 6);

    private System.Windows.Point GetTargetPosition(IslandVisualMode mode, double width, double height)
    {
        _placementSnapshot ??= _placementService.Capture(
            this,
            new System.Windows.Size(_layoutProfile.CollapsedWidth, _layoutProfile.CollapsedHeight),
            new System.Windows.Size(_layoutProfile.ExpandedWidth, _layoutProfile.ExpandedHeight));

        if (mode == IslandVisualMode.Collapsed)
        {
            return _placementSnapshot.ActiveCollapsedTargetDip;
        }

        if (mode == IslandVisualMode.Message)
        {
            return TaskbarAnchorCalculator.GetWorkAreaBottomRightTarget(
                _placementSnapshot.WorkAreaRectDip,
                width,
                height);
        }

        return _placementSnapshot.ExpandedTargetDip;
    }

    private void ApplyTargetPosition(IslandVisualMode mode, double width, double height)
    {
        var target = GetTargetPosition(mode, width, height);
        Left = target.X;
        Top = target.Y;
    }

    private void RefreshPlacementSnapshot()
    {
        try
        {
            _placementSnapshot = _placementService.Capture(
                this,
                new System.Windows.Size(_layoutProfile.CollapsedWidth, _layoutProfile.CollapsedHeight),
                new System.Windows.Size(_layoutProfile.ExpandedWidth, _layoutProfile.ExpandedHeight));
            LogPlacementSnapshot(_placementSnapshot);
        }
        catch (Exception exception)
        {
            _logger.Error("读取当前显示器或任务栏布局失败，使用主工作区右下角回退。", exception);
            var workArea = SystemParameters.WorkArea;
            var collapsedFallback = TaskbarAnchorCalculator.GetWorkAreaBottomRightTarget(
                workArea,
                _layoutProfile.CollapsedWidth,
                _layoutProfile.CollapsedHeight);
            var expanded = TaskbarAnchorCalculator.GetWorkAreaBottomRightTarget(
                workArea,
                _layoutProfile.ExpandedWidth,
                _layoutProfile.ExpandedHeight);
            var dpi = VisualTreeHelper.GetDpi(this);
            _placementSnapshot = new DesktopPlacementSnapshot(
                "primary-fallback",
                Rect.Empty,
                Rect.Empty,
                workArea,
                Rect.Empty,
                TaskbarEdge.Unknown,
                dpi.DpiScaleX,
                dpi.DpiScaleY,
                default,
                collapsedFallback,
                CollapsedPlacementMode.WorkAreaBottomRightFallback,
                collapsedFallback,
                expanded,
                exception.Message);
            LogPlacementSnapshot(_placementSnapshot);
        }
    }

    private void LogPlacementSnapshot(DesktopPlacementSnapshot snapshot)
    {
        _logger.Info(
            $"定位诊断：monitor={snapshot.MonitorDeviceName}, monitorPx={snapshot.MonitorRectPx}, workPx={snapshot.WorkAreaRectPx}, " +
            $"taskbarPx={snapshot.TaskbarRectPx}, edge={snapshot.TaskbarEdge}, dpi=({snapshot.DpiScaleX:0.###},{snapshot.DpiScaleY:0.###}), " +
            $"collapsedSizeDip=({_layoutProfile.CollapsedWidth:0.#},{_layoutProfile.CollapsedHeight:0.#}), taskbarTargetDip={snapshot.CollapsedTaskbarTargetDip}, " +
            $"fallbackTargetDip={snapshot.CollapsedFallbackTargetDip}, mode={snapshot.ActiveCollapsedPlacementMode}, activeTargetDip={snapshot.ActiveCollapsedTargetDip}, " +
            $"expandedTargetDip={snapshot.ExpandedTargetDip}, state={_displayState}, reason={snapshot.PlacementReason}。");
    }

    private void SetDisplayState(IslandDisplayState state)
    {
        if (_displayState == state)
        {
            return;
        }

        _displayState = state;
        _logger.Info($"窗口状态：{state}；采样模式={SamplingCadence.ForState(state).TotalMilliseconds:0}ms。");
    }

    private void AnimateShellEntrance()
    {
        IslandShell.Opacity = 1;
        var opacityAnimation = new DoubleAnimation
        {
            From = 0.82,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        IslandShell.BeginAnimation(
            OpacityProperty,
            opacityAnimation,
            HandoffBehavior.SnapshotAndReplace);

        var positionAnimation = new DoubleAnimation
        {
            From = 1.5,
            To = 0,
            Duration = TimeSpan.FromMilliseconds(120),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };
        ShellTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            positionAnimation,
            HandoffBehavior.SnapshotAndReplace);
    }

    private void AnimateStartupEntrance()
    {
        if (_startupAnimationPlayed)
        {
            return;
        }

        _startupAnimationPlayed = true;
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        IslandShell.Opacity = 1;
        ShellTranslateTransform.Y = 0;
        IslandShell.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        ShellTranslateTransform.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(2, 0, TimeSpan.FromMilliseconds(160))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
    }

    private void AnimateCollapsedGpuTemperature(bool show)
    {
        TemperatureBorderSegment.BeginAnimation(OpacityProperty, null);

        if (_mode != IslandVisualMode.Collapsed)
        {
            TemperatureBorderSegment.Opacity = 0;
            return;
        }

        TemperatureBorderSegment.Visibility = Visibility.Visible;
        var targetOpacity = show ? _temperatureSegmentTargetOpacity : 0;

        if (!SystemParameters.ClientAreaAnimation)
        {
            TemperatureBorderSegment.Opacity = targetOpacity;
            return;
        }

        var duration = TimeSpan.FromMilliseconds(show ? 180 : 120);
        var currentOpacity = TemperatureBorderSegment.Opacity;
        TemperatureBorderSegment.Opacity = targetOpacity;
        TemperatureBorderSegment.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(currentOpacity, targetOpacity, duration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            },
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnMetricPairToggleClick(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        if (_isMetricPairSwitching || IsGeometryTransitioning || _mode != IslandVisualMode.Collapsed)
        {
            return;
        }

        var targetPair = _metricPairController.SelectedPair == MetricPair.CpuRam
            ? MetricPair.GpuVram
            : MetricPair.CpuRam;
        _logger.Info(
            $"切换显示按钮：visibility={MetricPairToggleButton.Visibility}, event=OnMetricPairToggleClick, " +
            $"current={_metricPairController.SelectedPair}, target={targetPair}。");

        if (!SystemParameters.ClientAreaAnimation)
        {
            SelectMetricPair(targetPair);
            return;
        }

        _isMetricPairSwitching = true;
        MetricPairToggleButton.IsEnabled = false;
        MetricPairToggleRotation.Angle = MetricToggleMotion.RestingAngleDegrees;
        var rotation = new DoubleAnimationUsingKeyFrames
        {
            Duration = MetricToggleMotion.Duration,
            FillBehavior = FillBehavior.Stop
        };
        rotation.KeyFrames.Add(new EasingDoubleKeyFrame(
            MetricToggleMotion.RotationDegrees,
            KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120)),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        rotation.KeyFrames.Add(new EasingDoubleKeyFrame(
            MetricToggleMotion.RestingAngleDegrees,
            KeyTime.FromTimeSpan(MetricToggleMotion.Duration),
            new CubicEase { EasingMode = EasingMode.EaseOut }));
        MetricPairToggleRotation.BeginAnimation(
            RotateTransform.AngleProperty,
            rotation,
            HandoffBehavior.SnapshotAndReplace);

        var fadeOut = new DoubleAnimation(1, MetricToggleMotion.MinimumOpacity, MetricToggleMotion.FadeOutDuration)
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        fadeOut.Completed += (_, _) =>
        {
            SelectMetricPair(targetPair);
            CollapsedPrimaryGroup.Opacity = 1;
            CollapsedSecondaryGroup.Opacity = 1;
            var fadeIn = new DoubleAnimation(
                MetricToggleMotion.MinimumOpacity,
                1,
                MetricToggleMotion.FadeInDuration)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            };
            fadeIn.Completed += (_, _) =>
            {
                CollapsedPrimaryGroup.BeginAnimation(OpacityProperty, null);
                CollapsedSecondaryGroup.BeginAnimation(OpacityProperty, null);
                CollapsedPrimaryGroup.Opacity = 1;
                CollapsedSecondaryGroup.Opacity = 1;
                _isMetricPairSwitching = false;
                MetricPairToggleButton.IsEnabled = true;
            };
            CollapsedPrimaryGroup.BeginAnimation(OpacityProperty, fadeIn, HandoffBehavior.SnapshotAndReplace);
            CollapsedSecondaryGroup.BeginAnimation(
                OpacityProperty,
                fadeIn.Clone(),
                HandoffBehavior.SnapshotAndReplace);
        };
        CollapsedPrimaryGroup.BeginAnimation(OpacityProperty, fadeOut, HandoffBehavior.SnapshotAndReplace);
        CollapsedSecondaryGroup.BeginAnimation(
            OpacityProperty,
            fadeOut.Clone(),
            HandoffBehavior.SnapshotAndReplace);
    }

    private void OnIslandMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsGeometryTransitioning)
        {
            e.Handled = true;
            return;
        }

        if (_mode is IslandVisualMode.Collapsed or IslandVisualMode.Message)
        {
            _messageTimer.Stop();
            _isMessageShowing = false;
            SetMode(IslandVisualMode.Expanded);
            e.Handled = true;
        }
    }

    private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode == IslandVisualMode.Expanded)
        {
            ResetAutoCollapseTimer();
        }
    }

    private void OnWindowPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_mode == IslandVisualMode.Expanded)
        {
            ResetAutoCollapseTimer();
        }

        if (e.Key == Key.Escape && _mode == IslandVisualMode.Expanded)
        {
            SetMode(IslandVisualMode.Collapsed);
            e.Handled = true;
        }
    }

    private void ResetAutoCollapseTimer()
    {
        if (_mode != IslandVisualMode.Expanded)
        {
            return;
        }

        _autoCollapseTimer.Stop();
        _autoCollapseTimer.Start();
    }

    private void CollapseAfterInactivity()
    {
        _autoCollapseTimer.Stop();
        SetMode(IslandVisualMode.Collapsed);
    }

    private void OnMoveButtonMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_mode != IslandVisualMode.Expanded || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        _autoCollapseTimer.Stop();
        e.Handled = true;
        try
        {
            DragMove();
        }
        catch (InvalidOperationException)
        {
            // The button was released before WPF entered the native move loop.
        }

        ClampExpandedWindowToVisibleWorkArea();
        ResetAutoCollapseTimer();
    }

    private void OnResetPositionClick(object sender, RoutedEventArgs e)
    {
        ResetPosition();
        ResetAutoCollapseTimer();
        e.Handled = true;
    }

    private void OnHideClick(object sender, RoutedEventArgs e)
    {
        HideIsland();
        e.Handled = true;
    }

    private void ResetPosition()
    {
        _settings.ExpandedX = null;
        _settings.ExpandedY = null;
        _settings.Position = "bottom-right";
        RefreshPlacementSnapshot();
        if (_mode == IslandVisualMode.Expanded)
        {
            ApplyTargetPosition(IslandVisualMode.Expanded, Width, Height);
        }
        _settingsService.Save(_settings);
    }

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SystemParameters.WorkArea) or nameof(SystemParameters.PrimaryScreenWidth) or nameof(SystemParameters.PrimaryScreenHeight))
        {
            Dispatcher.BeginInvoke(RepositionForDisplayChange);
        }
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RepositionForDisplayChange();
        });
    }

    private void OnWindowDpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            RepositionForDisplayChange();
        });
    }

    private void RepositionForDisplayChange()
    {
        if (IsGeometryTransitioning)
        {
            _logger.Info($"显示器变化发生在 {_displayState}，坐标更新延后到状态完成后。");
            return;
        }

        RefreshPlacementSnapshot();
        ApplyTargetPosition(_mode, Width, Height);
        RestartTaskbarRecoveryIfNeeded();
    }

    private void RestartTaskbarRecoveryIfNeeded()
    {
        _placementRecoveryTimer.Stop();
        _placementRecoveryAttempts = 0;

        if (!ShouldRetryTaskbarPlacement())
        {
            return;
        }

        _logger.Warn("显示切换时任务栏窗口暂不可用，开始延迟重试任务栏定位。");
        _placementRecoveryTimer.Start();
    }

    private void OnPlacementRecoveryTick(object? sender, EventArgs e)
    {
        _placementRecoveryAttempts++;
        RefreshPlacementSnapshot();
        ApplyTargetPosition(_mode, Width, Height);

        if (ShouldRetryTaskbarPlacement())
        {
            return;
        }

        _placementRecoveryTimer.Stop();
        if (_placementSnapshot?.ActiveCollapsedPlacementMode == CollapsedPlacementMode.TaskbarOverlay)
        {
            _logger.Info($"任务栏窗口已恢复，第 {_placementRecoveryAttempts} 次重试后重新锚定到任务栏。");
        }
        else
        {
            _logger.Warn($"任务栏定位恢复重试结束：attempts={_placementRecoveryAttempts}。");
        }
    }

    private bool ShouldRetryTaskbarPlacement() =>
        _placementSnapshot is not null &&
        TaskbarRecoveryPolicy.ShouldRetry(
            _placementSnapshot.ActiveCollapsedPlacementMode,
            !_placementSnapshot.TaskbarRectPx.IsEmpty,
            _placementRecoveryAttempts);

    private void ClampExpandedWindowToVisibleWorkArea()
    {
        if (_mode != IslandVisualMode.Expanded)
        {
            return;
        }

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero || !GetWindowRect(handle, out var nativeRectangle))
        {
            return;
        }

        var windowRectangle = System.Drawing.Rectangle.FromLTRB(
            nativeRectangle.Left,
            nativeRectangle.Top,
            nativeRectangle.Right,
            nativeRectangle.Bottom);
        var workArea = Forms.Screen.FromRectangle(windowRectangle).WorkingArea;
        var dpiScale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var inset = Math.Max(1, (int)Math.Round(TaskbarAnchorCalculator.MinimumScreenInset * dpiScale));
        var clamped = TaskbarAnchorCalculator.ClampPixelRectangleToWorkArea(windowRectangle, workArea, inset);

        if (clamped.X != windowRectangle.Left || clamped.Y != windowRectangle.Top)
        {
            _ = SetWindowPos(
                handle,
                IntPtr.Zero,
                clamped.X,
                clamped.Y,
                0,
                0,
                SetWindowPosFlags.NoSize |
                SetWindowPosFlags.NoZOrder |
                SetWindowPosFlags.NoActivate |
                SetWindowPosFlags.NoOwnerZOrder);
        }

    }

    private void UpdateFullscreenSuppression()
    {
        var ownWindow = new WindowInteropHelper(this).Handle;
        var now = DateTimeOffset.Now;
        var fullscreenDetected = FullscreenSuppressionPolicy.ShouldSuppress(
            _fullscreenDetector.IsForegroundFullscreen(ownWindow),
            _remoteSessionDetector.IsCurrentSessionRemote(now),
            _fullscreenDetector.IsForegroundRemoteClient(ownWindow),
            _fullscreenDetector.IsForegroundBrowser(ownWindow));
        var shouldSuppress = _fullscreenSuppressionGate.Update(fullscreenDetected);

        if (shouldSuppress)
        {
            if (_suppressedByFullscreen)
            {
                return;
            }

            _suppressedByFullscreen = true;
            _autoCollapseTimer.Stop();
            _messageTimer.Stop();
            _pendingMessages.Clear();
            _isMessageShowing = false;
            StopSamplingLoop();
            if (IsVisible)
            {
                Hide();
            }

            _logger.Info("检测到前台窗口覆盖整块显示器和任务栏，性能岛已进入静默。");
            return;
        }

        if (!_suppressedByFullscreen)
        {
            ReassertTaskbarOverlayZOrder(ownWindow);
            return;
        }

        _suppressedByFullscreen = false;
        _resumeAlertsAfter = now.AddSeconds(15);
        if (_hiddenByUser)
        {
            return;
        }

        Show();
        SetMode(IslandVisualMode.Collapsed, animate: false);
        StartSamplingLoop();
        ReassertTaskbarOverlayZOrder(ownWindow);

        _logger.Info("前台窗口已退出覆盖任务栏的全屏状态，性能岛恢复且不补播旧消息。");
    }

    private void ReassertTaskbarOverlayZOrder(IntPtr ownWindow)
    {
        if (TaskbarOverlayZOrder.ShouldReassertTopmost(
                _settings.AlwaysOnTop,
                IsVisible,
                _suppressedByFullscreen,
                _hiddenByUser))
        {
            _ = TaskbarOverlayZOrder.ReassertTopmost(ownWindow);
        }
    }

    private void HideIsland()
    {
        _hiddenByUser = true;
        _isMessageShowing = false;
        _autoCollapseTimer.Stop();
        _messageTimer.Stop();
        StopSamplingLoop();
        Hide();
    }

    private void RestoreIsland()
    {
        _hiddenByUser = false;

        var ownWindow = new WindowInteropHelper(this).Handle;
        var now = DateTimeOffset.Now;
        if (FullscreenSuppressionPolicy.ShouldSuppress(
                _fullscreenDetector.IsForegroundFullscreen(ownWindow),
                _remoteSessionDetector.IsCurrentSessionRemote(now),
                _fullscreenDetector.IsForegroundRemoteClient(ownWindow),
                _fullscreenDetector.IsForegroundBrowser(ownWindow)))
        {
            _suppressedByFullscreen = true;
            return;
        }

        _suppressedByFullscreen = false;
        Show();
        SetMode(IslandVisualMode.Collapsed, animate: false);
        StartSamplingLoop();
    }

    private void SelectMetricPair(MetricPair pair)
    {
        _metricPairController.Select(pair);
        _settings.CollapsedMetricPair = pair == MetricPair.GpuVram ? "gpu-vram" : "cpu-ram";
        _settingsService.Save(_settings);
        UpdateMetricPairMenuChecks();
        if (_lastStatus is not null)
        {
            UpdateCollapsedMetrics(_lastStatus, _metricPairController.GetEffectivePair(DateTimeOffset.Now));
        }
        _logger.Info($"收起态显示模式已切换并保存：selected={pair}, setting={_settings.CollapsedMetricPair}。");
    }

    private void UpdateMetricPairMenuChecks()
    {
        if (_cpuRamMenuItem is not null)
        {
            _cpuRamMenuItem.Checked = _metricPairController.SelectedPair == MetricPair.CpuRam;
        }

        if (_gpuVramMenuItem is not null)
        {
            _gpuVramMenuItem.Checked = _metricPairController.SelectedPair == MetricPair.GpuVram;
        }
    }

    private void ToggleVisibility()
    {
        if (_hiddenByUser || !IsVisible)
        {
            RestoreIsland();
        }
        else
        {
            HideIsland();
        }
    }

    private void ToggleExpandedFromTray()
    {
        if (_hiddenByUser || !IsVisible)
        {
            RestoreIsland();
        }

        if (_suppressedByFullscreen)
        {
            return;
        }

        if (_mode == IslandVisualMode.Expanded)
        {
            SetMode(IslandVisualMode.Collapsed);
        }
        else
        {
            _messageTimer.Stop();
            _isMessageShowing = false;
            SetMode(IslandVisualMode.Expanded);
        }
    }

    private void ToggleAutoStart()
    {
        var target = !StartupService.IsEnabled();
        if (!StartupService.SetEnabled(target, _logger, out var error))
        {
            System.Windows.MessageBox.Show(error, "性能岛", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        _settings.AutoStart = target;
        _settingsService.Save(_settings);
        if (_autoStartMenuItem is not null)
        {
            _autoStartMenuItem.Checked = target;
        }
    }

    private void ExitApplication()
    {
        _isExiting = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    private static SolidColorBrush FreezeBrush(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }

    private static string MetricValue(string text, string label)
    {
        var prefix = $"{label} ";
        return text.StartsWith(prefix, StringComparison.Ordinal)
            ? text[prefix.Length..]
            : text;
    }

    private void UpdateExpandedText(System.Windows.Controls.TextBlock textBlock, string value)
    {
        if (string.Equals(textBlock.Text, value, StringComparison.Ordinal))
        {
            return;
        }

        textBlock.Text = value;
        textBlock.BeginAnimation(OpacityProperty, null);
        textBlock.Opacity = 1;
        if (_displayState != IslandDisplayState.Expanded || !SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        _activeDataAnimationTargets.Add(textBlock);
        Volatile.Write(ref _activeDataAnimationCount, _activeDataAnimationTargets.Count);
        var animation = new DoubleAnimation(0.76, 1, TimeSpan.FromMilliseconds(140))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            _activeDataAnimationTargets.Remove(textBlock);
            Volatile.Write(ref _activeDataAnimationCount, _activeDataAnimationTargets.Count);
        };
        textBlock.BeginAnimation(OpacityProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private void ApplyCollapsedSeverity(
        MetricSeverity severity,
        System.Windows.Shapes.Ellipse indicator,
        System.Windows.Controls.TextBlock value)
    {
        var severityBrush = SeverityBrush(severity);
        var valueBrush = severity == MetricSeverity.Normal
            ? (SolidColorBrush)FindResource("InkBrush")
            : severityBrush;

        indicator.Fill = severityBrush;
        value.Foreground = valueBrush;
    }

    private void ApplyValueSeverity(MetricSeverity severity, System.Windows.Controls.TextBlock value)
    {
        var target = severity == MetricSeverity.Normal
            ? ((SolidColorBrush)FindResource("InkBrush")).Color
            : SeverityBrush(severity).Color;
        ApplyAnimatedBrush(value, target, brush => value.Foreground = brush);
    }

    private void ApplyAnimatedSeverity(
        MetricSeverity severity,
        System.Windows.Shapes.Shape shape,
        bool isFill)
    {
        var target = SeverityBrush(severity).Color;
        ApplyAnimatedBrush(shape, target, brush =>
        {
            if (isFill)
            {
                shape.Fill = brush;
            }
            else
            {
                shape.Stroke = brush;
            }
        });
    }

    private void ApplyAnimatedBrush(
        DependencyObject target,
        System.Windows.Media.Color targetColor,
        Action<SolidColorBrush> assign)
    {
        if (!_animatedSeverityBrushes.TryGetValue(target, out var brush))
        {
            brush = new SolidColorBrush(targetColor);
            _animatedSeverityBrushes[target] = brush;
            assign(brush);
            return;
        }

        assign(brush);
        if (brush.Color == targetColor)
        {
            return;
        }

        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        var from = brush.Color;
        brush.Color = targetColor;
        if (!SystemParameters.ClientAreaAnimation)
        {
            return;
        }

        _activeDataAnimationTargets.Add(target);
        Volatile.Write(ref _activeDataAnimationCount, _activeDataAnimationTargets.Count);
        var animation = new ColorAnimation(from, targetColor, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        animation.Completed += (_, _) =>
        {
            _activeDataAnimationTargets.Remove(target);
            Volatile.Write(ref _activeDataAnimationCount, _activeDataAnimationTargets.Count);
        };
        brush.BeginAnimation(SolidColorBrush.ColorProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }

    private SolidColorBrush SeverityBrush(MetricSeverity severity) => severity switch
    {
        MetricSeverity.Danger => (SolidColorBrush)FindResource("DangerBrush"),
        MetricSeverity.Warning => (SolidColorBrush)FindResource("WarningBrush"),
        _ => (SolidColorBrush)FindResource("SuccessBrush")
    };

    private static Icon CreateTrayIcon()
    {
        using var bitmap = new Bitmap(32, 32, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);

        using var shell = CreateRoundedRectangle(new RectangleF(3, 8, 26, 16), 8);
        using var shellBrush = new SolidBrush(System.Drawing.Color.FromArgb(245, 12, 12, 15));
        using var outlinePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(225, 245, 245, 248), 1.4f);
        using var accentBrush = new SolidBrush(System.Drawing.Color.FromArgb(255, 94, 155, 255));
        graphics.FillPath(shellBrush, shell);
        graphics.DrawPath(outlinePen, shell);
        graphics.FillEllipse(accentBrush, 8, 13, 6, 6);
        using var linePen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(230, 245, 245, 248), 1.6f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(linePen, 18, 14, 24, 14);
        graphics.DrawLine(linePen, 18, 18, 22, 18);

        var handle = bitmap.GetHicon();
        try
        {
            using var temporary = System.Drawing.Icon.FromHandle(handle);
            return (System.Drawing.Icon)temporary.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF rectangle, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr windowHandle, out NativeWindowRectangle rectangle);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        SetWindowPosFlags flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeWindowRectangle
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [Flags]
    private enum SetWindowPosFlags : uint
    {
        NoSize = 0x0001,
        NoZOrder = 0x0004,
        NoActivate = 0x0010,
        NoOwnerZOrder = 0x0200
    }

    private sealed record MessageVisual(SolidColorBrush Brush, string Icon)
    {
        public static MessageVisual From(IslandMessage message)
        {
            var key = $"{message.Level}:{message.Type}".ToLowerInvariant();
            if (key.Contains("error"))
            {
                return new MessageVisual(FreezeBrush(255, 107, 114), "!");
            }

            if (key.Contains("warn"))
            {
                return new MessageVisual(FreezeBrush(255, 180, 84), "!");
            }

            if (key.Contains("success"))
            {
                return new MessageVisual(FreezeBrush(97, 223, 168), "✓");
            }

            return new MessageVisual(FreezeBrush(94, 155, 255), "•");
        }
    }
}
