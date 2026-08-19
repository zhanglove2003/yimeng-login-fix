using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using YiMengFix.Core;

namespace YiMengFix.UI;

public partial class MainWindow : Window
{
    private const int DwmWindowCornerPreference = 33;
    private const int DwmRoundCorner = 2;
    private readonly SettingsStore _settingsStore = new();
    private readonly FileLogger _logger = new();
    private readonly IFixService _fixService;
    private AppSettings _settings = new();
    private PreflightResult? _preflight;
    private CancellationTokenSource? _operationCancellation;
    private string? _lastBackupPath;
    private BackupInfo? _latestRepairBackup;
    private bool _isLoaded;
    private bool _isBusy;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        _fixService = new FixService(log: AppendLog);
        SystemParameters.StaticPropertyChanged += SystemParameters_StaticPropertyChanged;
    }

    public ObservableCollection<CheckRow> CheckRows { get; } = [];

    public ObservableCollection<string> LogEntries { get; } = [];

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _settings = _settingsStore.Load();
        ThemeToggle.IsChecked = _settings.DarkMode;
        ThemeManager.Apply(_settings.DarkMode);
        GameRootBox.Text = !string.IsNullOrWhiteSpace(_settings.LastGameRoot) && Directory.Exists(_settings.LastGameRoot)
            ? _settings.LastGameRoot
            : FixConstants.DefaultGameRoot;
        _isLoaded = true;
        AppendLog($"程序启动。日志：{_logger.LogPath}");
        await RunPreflightAsync(savePath: false);
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        SystemParameters.StaticPropertyChanged -= SystemParameters_StaticPropertyChanged;
    }

    private async void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择一梦江湖游戏目录",
            InitialDirectory = Directory.Exists(GameRootBox.Text) ? GameRootBox.Text : null,
            Multiselect = false,
        };

        if (dialog.ShowDialog(this) == true)
        {
            GameRootBox.Text = dialog.FolderName;
            await RunPreflightAsync();
        }
    }

    private async void CheckButton_Click(object sender, RoutedEventArgs e) => await RunPreflightAsync();

    private async void GameRootBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (_isLoaded && !_isBusy)
        {
            await RunPreflightAsync();
        }
    }

    private async void GameRootBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !_isBusy)
        {
            e.Handled = true;
            await RunPreflightAsync();
        }
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_preflight?.CanApply != true)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            $"即将修改以下游戏目录中的两个文件：\n\n{_preflight.GameRoot}\n\n程序会先创建完整备份。是否继续？",
            "确认开始修复",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        BeginOperation("正在准备修复…", showProgress: true);
        try
        {
            var progress = CreateProgressReporter();
            var result = await _fixService.ApplyAsync(
                _preflight.GameRoot,
                progress,
                _operationCancellation!.Token);

            _lastBackupPath = result.BackupPath;
            ShowOperationResult(result.Succeeded, result.Message, result.BackupPath);
            if (!result.Succeeded)
            {
                MessageBox.Show(result.Message, "修复未完成", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消。", "WarningBrush");
        }
        finally
        {
            EndOperation();
            await RunPreflightAsync();
        }
    }

    private async void RollbackButton_Click(object sender, RoutedEventArgs e)
    {
        if (_latestRepairBackup is not { IsValid: true } backup || _isBusy)
        {
            return;
        }

        var confirmation = MessageBox.Show(
            "将回退最近一次修复，并恢复修复前的两个游戏文件。\n\n回退前会自动备份当前文件。是否继续？",
            "确认回退",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        BeginOperation("正在准备恢复…", showProgress: true);
        try
        {
            var progress = CreateProgressReporter();
            var result = await _fixService.RestoreAsync(
                GameRootBox.Text,
                backup,
                progress,
                _operationCancellation!.Token);

            _lastBackupPath = result.SafetyBackupPath ?? backup.Path;
            ShowOperationResult(result.Succeeded, result.Message, _lastBackupPath);
            if (!result.Succeeded)
            {
                MessageBox.Show(result.Message, "恢复未完成", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        catch (OperationCanceledException)
        {
            SetStatus("操作已取消。", "WarningBrush");
        }
        finally
        {
            EndOperation();
            await RunPreflightAsync();
        }
    }

    private void OpenBackupButton_Click(object sender, RoutedEventArgs e)
    {
        var backupPath = _lastBackupPath;
        if (!string.IsNullOrWhiteSpace(backupPath) && Directory.Exists(backupPath))
        {
            OpenPath(backupPath);
        }
    }

    private void OpenLogButton_Click(object sender, RoutedEventArgs e) => OpenPath(_logger.LogPath);

    private void ThemeToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (!_isLoaded)
        {
            return;
        }

        _settings.DarkMode = ThemeToggle.IsChecked == true;
        ThemeManager.Apply(_settings.DarkMode);
        SaveSettings();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.MinimizeWindow(this);

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
        }
        else
        {
            SystemCommands.MaximizeWindow(this);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        var maximized = WindowState == WindowState.Maximized;
        MaximizeGlyph.Text = maximized ? "\uE923" : "\uE922";
        WindowFrame.CornerRadius = maximized ? new CornerRadius(0) : new CornerRadius(8);
        WindowFrame.BorderThickness = maximized ? new Thickness(0) : new Thickness(1);
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e)
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var preference = DwmRoundCorner;
            _ = DwmSetWindowAttribute(
                handle,
                DwmWindowCornerPreference,
                ref preference,
                Marshal.SizeOf<int>());
        }
        catch
        {
            // Windows 10 does not expose the Windows 11 corner preference attribute.
        }
    }

    private void SystemParameters_StaticPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast))
        {
            Dispatcher.Invoke(() => ThemeManager.Apply(_settings.DarkMode));
        }
    }

    private async Task RunPreflightAsync(bool savePath = true)
    {
        if (_isBusy)
        {
            return;
        }

        BeginOperation("正在读取中…", showProgress: false);
        try
        {
            var cancellationToken = _operationCancellation!.Token;
            await Task.Yield();
            await Task.Delay(90, cancellationToken);
            SetStatus("检测游戏位置…", "PrimaryTextBrush");
            await Task.Delay(90, cancellationToken);
            _preflight = await _fixService.PreflightAsync(
                GameRootBox.Text,
                cancellationToken);
            AppendLog($"预检查：{_preflight.GameRoot}");
            foreach (var check in _preflight.Checks)
            {
                AppendLog($"[{check.Status}] {GetCheckLabel(check)}：{check.Detail}");
            }
            RenderPreflight(_preflight);

            if (savePath && Directory.Exists(_preflight.GameRoot))
            {
                _settings.LastGameRoot = _preflight.GameRoot;
                GameRootBox.Text = _preflight.GameRoot;
                SaveSettings();
            }

            SetStatus("正在读取备份记录…", "PrimaryTextBrush");
            await Task.Delay(70, cancellationToken);
            await RefreshLatestBackupAsync(_preflight.GameRoot, cancellationToken);
            RenderPreflight(_preflight);
        }
        catch (OperationCanceledException)
        {
            SetStatus("检查已取消。", "WarningBrush");
        }
        catch (Exception ex)
        {
            AppendLog($"检查失败：{ex.Message}");
            SetStatus("检查失败，请查看详细日志。", "ErrorBrush");
            ApplyButton.IsEnabled = false;
        }
        finally
        {
            EndOperation(preserveStatus: true);
        }
    }

    private void RenderPreflight(PreflightResult result)
    {
        CheckRows.Clear();
        foreach (var check in result.Checks)
        {
            CheckRows.Add(new CheckRow(
                GetCheckLabel(check),
                check.Detail,
                check.Status));
        }

        ApplyButton.IsEnabled = result.CanApply;
        OperationProgress.Value = 0;
        if (result.IsAlreadyApplied)
        {
            SetStatus("修复已经应用，无需重复处理。", "SuccessBrush");
        }
        else if (result.CanApply)
        {
            SetStatus("环境检查通过，可以开始修复。", "SuccessBrush");
        }
        else
        {
            SetStatus("检查未通过，请处理上方问题。", "ErrorBrush");
        }
    }

    private async Task RefreshLatestBackupAsync(string gameRoot, CancellationToken cancellationToken)
    {
        var backups = await _fixService.ListBackupsAsync(gameRoot, cancellationToken);
        _latestRepairBackup = backups.FirstOrDefault(backup => backup.IsValid && backup.Purpose.Equals("fix", StringComparison.OrdinalIgnoreCase));
        LatestBackupText.Text = _latestRepairBackup is null
            ? "暂无可回退备份"
            : $"{_latestRepairBackup.Name} · 可安全回退";
        RollbackButton.IsEnabled = !_isBusy && _latestRepairBackup is not null;
    }

    private IProgress<FixProgress> CreateProgressReporter() =>
        new Progress<FixProgress>(progress =>
        {
            OperationProgress.Visibility = Visibility.Visible;
            var percentage = progress.TotalSteps == 0
                ? 0
                : progress.CompletedSteps * 100d / progress.TotalSteps;
            OperationProgress.Value = percentage;
            SetStatus(progress.Message, progress.Stage == FixStage.Completed ? "SuccessBrush" : "PrimaryTextBrush");
            AppendLog(progress.Message);
        });

    private void BeginOperation(string message, bool showProgress)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = new CancellationTokenSource();
        _isBusy = true;
        BrowseButton.IsEnabled = false;
        CheckButton.IsEnabled = false;
        GameRootBox.IsEnabled = false;
        ApplyButton.IsEnabled = false;
        RollbackButton.IsEnabled = false;
        OperationProgress.Value = 0;
        OperationProgress.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        SetLoadingAnimation(!showProgress);
        SetStatus(message, "PrimaryTextBrush");
    }

    private void EndOperation(bool preserveStatus = false)
    {
        _isBusy = false;
        BrowseButton.IsEnabled = true;
        CheckButton.IsEnabled = true;
        GameRootBox.IsEnabled = true;
        ApplyButton.IsEnabled = _preflight?.CanApply == true;
        RollbackButton.IsEnabled = _latestRepairBackup is { IsValid: true };
        if (!preserveStatus && OperationProgress.Value >= 100)
        {
            SetStatus("操作完成。", "SuccessBrush");
        }
        OperationProgress.Value = 0;
        OperationProgress.Visibility = Visibility.Collapsed;
        SetLoadingAnimation(false);
    }

    private void SetLoadingAnimation(bool isLoading)
    {
        LoadingSpinner.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
        StatusDot.Visibility = isLoading ? Visibility.Collapsed : Visibility.Visible;
        SpinnerRotate.BeginAnimation(
            RotateTransform.AngleProperty,
            isLoading
                ? new DoubleAnimation(0, 360, new Duration(TimeSpan.FromMilliseconds(900)))
                {
                    RepeatBehavior = RepeatBehavior.Forever,
                }
                : null);
    }

    private void ShowOperationResult(bool succeeded, string message, string? backupPath)
    {
        SetStatus(message, succeeded ? "SuccessBrush" : "ErrorBrush");
        AppendLog(message);
        SuccessPanel.Visibility = succeeded && !string.IsNullOrWhiteSpace(backupPath)
            ? Visibility.Visible
            : Visibility.Collapsed;
        BackupPathText.Text = backupPath is null ? string.Empty : $"已自动备份：{backupPath}";
    }

    private void SetStatus(string message, string brushKey)
    {
        StatusText.Text = message;
        StatusText.Foreground = (Brush)FindResource(brushKey);
        StatusDot.Fill = (Brush)FindResource(brushKey);
        System.Windows.Automation.AutomationProperties.SetHelpText(StatusText, message);
    }

    private void AppendLog(string message)
    {
        void Append()
        {
            _logger.Write(message);
            LogEntries.Add(message);
            if (LogEntries.Count > 300)
            {
                LogEntries.RemoveAt(0);
            }

            LogList?.ScrollIntoView(LogEntries.LastOrDefault());
        }

        if (Dispatcher.CheckAccess())
        {
            Append();
        }
        else
        {
            Dispatcher.BeginInvoke(Append);
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsStore.Save(_settings);
        }
        catch (Exception ex)
        {
            AppendLog($"保存设置失败：{ex.Message}");
        }
    }

    private static string GetCheckLabel(PreflightCheck check) => check.Key switch
    {
        "game-root" => "游戏目录",
        "payload" => "内置修复数据",
        "process" => "游戏进程",
        "write" => "写入权限",
        var key when key.Contains("Win32", StringComparison.OrdinalIgnoreCase) => "32 位目标文件",
        var key when key.Contains("Win64", StringComparison.OrdinalIgnoreCase) => "64 位目标文件",
        _ => check.Label,
    };

    private static void OpenPath(string path)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = path,
            UseShellExecute = true,
        });
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        nint hwnd,
        int attribute,
        ref int attributeValue,
        int attributeSize);
}

public sealed record CheckRow(string Label, string Detail, CheckStatus Status)
{
    public string IconGlyph => Status switch
    {
        CheckStatus.Passed => "\uE73E",
        CheckStatus.Warning => "\uE7BA",
        _ => "\uE783",
    };
}
