
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RyotaQC.App.Enums;
using RyotaQC.App.Services;

namespace RyotaQC.App.ViewModels.Pages;

public sealed partial class QualityControlPageViewModel : ObservableObject
{
    private readonly IQualityControlRunner _runner;
    private readonly IUiFeedbackService _feedback;
    private readonly IAppLogger _logger;
    private readonly INavigationService _navigationService;
    private readonly string _workspaceRoot;
    private readonly string _qcRoot;
    private readonly string _hdsStatusPath;
    private readonly string _driverInstallStampPath;
    private readonly DispatcherTimer _clockTimer;
    private bool _isAutoUpdatingChecks;
    private DateTime? _qcStartDate;
    private DateTime? _driverInstallDate;
    private string _cachedSystemModel = string.Empty;
    private string _cachedSystemManufacturer = string.Empty;

    private int? _batteryHealthPercent;
    private string _batteryHealthNote = string.Empty;
    private int? _storageHealthPercent;
    private PortCheckResult? _lastPortResult;
    private bool _touchpadOk;
    private bool _wifiOk;
    private bool _backlightOk;
    private bool _micOk;

    [ObservableProperty]
    private string lastRefreshText = string.Empty;

    [ObservableProperty]
    private string checklistSummary = string.Empty;

    [ObservableProperty]
    private string notes = string.Empty;

    [ObservableProperty]
    private bool isSavingPdf;

    [ObservableProperty]
    private BenchmarkOption? selectedBenchmark;

    [ObservableProperty]
    private string benchmarkActionStatus = "Idle";

    public QualityControlPageViewModel(
        IQualityControlRunner runner,
        IUiFeedbackService feedback,
        IAppLogger logger,
        INavigationService navigationService)
    {
        _runner = runner;
        _feedback = feedback;
        _logger = logger;
        _navigationService = navigationService;

        _workspaceRoot = WorkspacePathService.FindWorkspaceRoot();
        _qcRoot = Path.Combine(_workspaceRoot, "tools", "qc");
        _hdsStatusPath = Path.Combine(_qcRoot, "Hard Disk Sentinel", "HDSentinel.sta");
        _driverInstallStampPath = Path.Combine(_workspaceRoot, "logs", "driver-pack-install.txt");
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) => UpdateRealtimeClock();
        _clockTimer.Start();

        ChecklistItems = new ObservableCollection<QcChecklistItem>();
        DisplayChecklistItems = new ObservableCollection<QcChecklistItem>();
        BenchmarkOptions = new ObservableCollection<BenchmarkOption>();

        RefreshCommand = new RelayCommand(RefreshData);
        BackToHomeCommand = new RelayCommand(() => _navigationService.Navigate(AppPage.Dashboard));
        SavePdfCommand = new AsyncRelayCommand(SavePdfAsync, () => !IsSavingPdf);
        InstallBenchmarkCommand = new AsyncRelayCommand(InstallBenchmarkAsync, () => SelectedBenchmark is not null);
        RemoveBenchmarkCommand = new AsyncRelayCommand(RemoveBenchmarkAsync, () => SelectedBenchmark is not null);
        ShowQcResultsCommand = new AsyncRelayCommand(ShowQcResultsAsync);
        ShowOverallQcResultsCommand = new AsyncRelayCommand(ShowOverallQcResultsAsync);
        SaveNotepadCommand = new AsyncRelayCommand(SaveNotepadAsync, () => !IsSavingPdf);

        BuildChecklistItems();
        OnPropertyChanged(nameof(BenchmarkChecklistItem));
        OnPropertyChanged(nameof(RunningTestChecklistItem));
        BuildBenchmarkOptions();
        RefreshData();
    }

    public ObservableCollection<QcChecklistItem> ChecklistItems { get; }

    public ObservableCollection<QcChecklistItem> DisplayChecklistItems { get; }

    public ObservableCollection<BenchmarkOption> BenchmarkOptions { get; }

    public QcChecklistItem? BenchmarkChecklistItem =>
        ChecklistItems.FirstOrDefault(x => x.Kind == QcChecklistKind.Benchmark);

    public QcChecklistItem? RunningTestChecklistItem =>
        ChecklistItems.FirstOrDefault(x => x.Kind == QcChecklistKind.RunningTest);

    public IRelayCommand RefreshCommand { get; }

    public IRelayCommand BackToHomeCommand { get; }

    public IAsyncRelayCommand SavePdfCommand { get; }

    public IAsyncRelayCommand InstallBenchmarkCommand { get; }

    public IAsyncRelayCommand RemoveBenchmarkCommand { get; }

    public IAsyncRelayCommand ShowQcResultsCommand { get; }

    public IAsyncRelayCommand ShowOverallQcResultsCommand { get; }

    public IAsyncRelayCommand SaveNotepadCommand { get; }

    partial void OnIsSavingPdfChanged(bool value) => SavePdfCommand.NotifyCanExecuteChanged();

    partial void OnSelectedBenchmarkChanged(BenchmarkOption? value)
    {
        InstallBenchmarkCommand.NotifyCanExecuteChanged();
        RemoveBenchmarkCommand.NotifyCanExecuteChanged();
    }

    private void BuildChecklistItems()
    {
        foreach (var existing in ChecklistItems)
        {
            existing.PropertyChanged -= ItemOnPropertyChanged;
        }

        ChecklistItems.Clear();
        DisplayChecklistItems.Clear();

        AddItem("LCD Test", QcChecklistKind.LcdTest, "Uji warna layar (merah, hijau, biru, putih, hitam).", "lcd-test.png");
        AddItem("Storage Health", QcChecklistKind.StorageHealth, "Baca health dari Hard Disk Sentinel.", "storage-health.png");
        AddItem("Battery Health", QcChecklistKind.BatteryHealth, "Baca persentase battery health.", "battery-health.png");
        AddItem("Battery Report", QcChecklistKind.BatteryTest, "Jalankan battery report.", "battery-test.png");
        AddItem("Keyboard Test", QcChecklistKind.KeyboardTest, "Uji penekanan semua tombol keyboard.", "keyboard-test.png");
        AddItem("Audio Test", QcChecklistKind.AudioTest, "Audio 1/2 normal dan Left/Right channel.", "audio-test.png");
        AddItem("Benchmark", QcChecklistKind.Benchmark, "Jalankan benchmark yang dipilih.", "benchmark.png");
        AddItem("Running Test", QcChecklistKind.RunningTest, "Jalankan VLC dengan RT.mp4 dan log battery ke Desktop.", "battery-test.png");
        AddItem("Camera", QcChecklistKind.Camera, "Buka aplikasi Camera Windows.", "camera-test.png");
        AddItem("Touchscreen Test", QcChecklistKind.TouchscreenTest, "Uji sentuhan layar.", "touchscreen-test.png");
        AddItem("Port Test", QcChecklistKind.PortTest, "Cek USB, audio jack, charger.", "port-test.png");
        AddItem("Touchpad", QcChecklistKind.Touchpad, "Buka program test touchpad (klik kiri/kanan, scroll, sensitivitas).", "touchpad-test.png");
        AddItem("Wifi", QcChecklistKind.Wifi, "Cek koneksi Wi-Fi aktif.", "wifi-test.png");
        AddItem("Backlight", QcChecklistKind.Backlight, "Cek brightness/backlight monitor.", "backlight-test.png");
        AddItem("Mic", QcChecklistKind.Mic, "Cek mikrofon terdeteksi.", "mic-test.png");
    }

    private void AddItem(string name, QcChecklistKind kind, string info, string iconFileName)
    {
        var item = new QcChecklistItem
        {
            Name = name,
            Kind = kind,
            InfoText = info,
            StatusText = "-",
            IconPath = ResolveQcIconPath(iconFileName)
        };

        item.RunCommand = new AsyncRelayCommand(() => RunItemAsync(item), () => !item.IsRunning);
        item.PropertyChanged += ItemOnPropertyChanged;
        ChecklistItems.Add(item);
        if (kind is not QcChecklistKind.Benchmark and not QcChecklistKind.RunningTest)
        {
            DisplayChecklistItems.Add(item);
        }
    }

    private string ResolveQcIconPath(string iconFileName)
    {
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Icons", "QC", iconFileName);
        if (File.Exists(runtimePath))
        {
            return runtimePath;
        }

        var sourcePath = Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Assets", "Images", "Icons", "QC", iconFileName);
        if (File.Exists(sourcePath))
        {
            return sourcePath;
        }

        return runtimePath;
    }

    private void BuildBenchmarkOptions()
    {
        BenchmarkOptions.Clear();

        var names = new[] { "HW Information", "CPU Heavy Load", "FurMark GPU Test", "Monitor Test", "SSD Test Speed" };
        foreach (var name in names)
        {
            var folder = FindQcFolder(name);
            BenchmarkOptions.Add(new BenchmarkOption
            {
                Name = name,
                FolderPath = folder ?? string.Empty,
                ExecutablePath = string.IsNullOrWhiteSpace(folder) ? string.Empty : (ResolveExecutable(folder) ?? string.Empty)
            });
        }

        SelectedBenchmark = BenchmarkOptions.FirstOrDefault();
    }

    private void ItemOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not QcChecklistItem item)
        {
            return;
        }

        if (e.PropertyName == nameof(QcChecklistItem.IsChecked))
        {
            if (item.IsChecked && !_isAutoUpdatingChecks)
            {
                EnsureQcStartDate();
            }

            UpdateStatusText(item);
            UpdateChecklistSummary();
        }
    }

    private void RefreshData()
    {
        BuildBenchmarkOptions();
        _driverInstallDate = TryReadDriverInstallDate();
        _cachedSystemModel = TryReadWmiValue("SELECT Model FROM Win32_ComputerSystem", "Model");
        _cachedSystemManufacturer = TryReadWmiValue("SELECT Manufacturer FROM Win32_ComputerSystem", "Manufacturer");
        _storageHealthPercent = TryReadStorageHealthPercent();
        _touchpadOk = DetectTouchpadAvailable();
        _wifiOk = DetectWifiConnected();
        _backlightOk = DetectBacklightActive();
        _micOk = DetectMicrophoneAvailable();
        _lastPortResult = null;
        BenchmarkActionStatus = "Idle";

        _isAutoUpdatingChecks = true;
        try
        {
            SetItemChecked(QcChecklistKind.Wifi, _wifiOk);
        }
        finally
        {
            _isAutoUpdatingChecks = false;
        }

        EnsureNotesTemplate();
        UpdateRealtimeClock();
        UpdateAllStatuses();
    }

    private void UpdateRealtimeClock()
    {
        LastRefreshText = $"Realtime: {DateTime.Now.ToString("dd MMMM yyyy HH:mm:ss", CultureInfo.GetCultureInfo("id-ID"))}";
    }

    private async Task RunItemAsync(QcChecklistItem item)
    {
        if (item.IsRunning)
        {
            return;
        }

        EnsureQcStartDate();
        item.IsRunning = true;
        item.RunCommand.NotifyCanExecuteChanged();

        try
        {
            switch (item.Kind)
            {
                case QcChecklistKind.LcdTest:
                    _runner.RunLcdTest();
                    item.InfoText = "LCD Test selesai.";
                    await ShowResultPopupAsync(item, "LCD Test - Hasil:", "Apakah hasil LCD test OK?");
                    break;

                case QcChecklistKind.StorageHealth:
                    _runner.RunSentinel();
                    item.InfoText = "Hard Disk Sentinel dibuka. Baca nilai Health lalu input di popup.";
                    // Show manual input popup for the health percentage
                    var healthPercent = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        Views.Windows.QcNumberInputPopupWindow.Show(
                            "SSD Health",
                            "Masukkan nilai Health dari Hard Disk Sentinel:"));

                    if (healthPercent >= 0)
                    {
                        _storageHealthPercent = healthPercent;
                        item.SetPercentageResult(healthPercent);
                        item.InfoText = $"Storage Health: {healthPercent}%";
                    }
                    else
                    {
                        await ShowResultPopupAsync(item, "Storage Health - Hasil:",
                            "Hard Disk Sentinel sudah dibuka. Apakah hasil OK?");
                    }
                    break;

                case QcChecklistKind.BatteryHealth:
                    _runner.RunBatteryHealth();
                    await Task.Delay(900);
                    _batteryHealthPercent = await TryReadBatteryHealthPercentAsync();
                    if (_batteryHealthPercent.HasValue && _batteryHealthPercent.Value > 0)
                    {
                        item.SetPercentageResult(_batteryHealthPercent.Value);
                        item.InfoText = $"Battery Health: {_batteryHealthPercent.Value}%";
                    }
                    else
                    {
                        await ShowResultPopupAsync(item, "Battery Health - Hasil:", "Battery Health sudah dicek. Apakah hasil OK?");
                        item.InfoText = "Battery Health: manual review";
                    }
                    break;

                case QcChecklistKind.BatteryTest:
                    await _runner.RunBatteryReportAsync();
                    item.InfoText = "Battery Report dijalankan.";
                    // Show result popup (OK/X/-) then time input
                    var batResult = Views.Windows.QcResultPopupWindow.Show(
                        "Battery Report - Hasil:",
                        "Battery Report sudah terbuka. Apakah hasil OK?");
                    if (batResult != Views.Windows.QcResultPopupWindow.QcResult.None)
                    {
                        var (timeText, totalMin) = Views.Windows.QcTimeInputPopupWindow.Show(
                            "Berapa lama hasil test?",
                            "Masukkan durasi hasil battery report:");
                        if (!string.IsNullOrEmpty(timeText))
                        {
                            item.ResultLabel = timeText;
                            item.ResultStatus = totalMin >= 60 ? "PASS" : "FAIL";
                            item.ResultColor = totalMin >= 60 ? "#FF1E7A4B" : "#FFB91C1C";
                            item.HasResult = true;
                            item.InfoText = $"Battery Report: {timeText}";
                        }
                        else
                        {
                            item.SetResult(batResult);
                        }
                    }
                    break;

                case QcChecklistKind.KeyboardTest:
                    _runner.RunKeyboardTest();
                    item.InfoText = "Keyboard test dibuka.";
                    await ShowResultPopupAsync(item, "Keyboard Test - Hasil:",
                        "PassMark KeyboardTest sudah dibuka dan ditutup. Apakah hasil OK?");
                    break;

                case QcChecklistKind.AudioTest:
                    _runner.RunAudioTest();
                    item.InfoText = "Audio test selesai.";
                    await ShowResultPopupAsync(item, "Audio Test - Hasil:",
                        "Audio test sudah selesai. Apakah hasil OK?");
                    break;

                case QcChecklistKind.Benchmark:
                    await InstallBenchmarkAsync();
                    item.InfoText = SelectedBenchmark is not null
                        ? $"Benchmark dijalankan: {SelectedBenchmark.Name}."
                        : "Benchmark belum dipilih.";
                    await ShowResultPopupAsync(item, "Benchmark - Hasil:",
                        "Benchmark sudah dijalankan. Apakah hasil OK?");
                    break;

                case QcChecklistKind.RunningTest:
                    _runner.RunRunningTest();
                    item.InfoText = "Running Test dibuka di VLC. Menunggu video tampil...";
                    // Wait for VLC to start playing the video
                    await Task.Delay(5000);
                    // Show result popup asking if video is playing
                    var rtResult = Views.Windows.QcResultPopupWindow.Show(
                        "Running Test - Hasil:",
                        "Video Running Test sudah tampil? Apakah hasil OK?");
                    if (rtResult != Views.Windows.QcResultPopupWindow.QcResult.None)
                    {
                        // Show time input popup for duration
                        var (rtTime, rtMin) = Views.Windows.QcTimeInputPopupWindow.Show(
                            "Running Test - Durasi:",
                            "Masukkan durasi running test (VLC video):",
                            redBelowMinutes: 120,
                            yellowBelowMinutes: 180,
                            greenBelowMinutes: 240);
                        if (!string.IsNullOrEmpty(rtTime))
                        {
                            item.ResultLabel = rtTime;
                            if (rtMin < 120)
                            {
                                item.ResultColor = "#FFB91C1C";
                                item.ResultStatus = "FAIL";
                            }
                            else if (rtMin < 240)
                            {
                                item.ResultColor = "#FFE0A800";
                                item.ResultStatus = "SKIP";
                            }
                            else
                            {
                                item.ResultColor = "#FF1E7A4B";
                                item.ResultStatus = "PASS";
                            }
                            item.HasResult = true;
                            item.InfoText = $"Running Test: {rtTime}";
                        }
                        else
                        {
                            item.SetResult(rtResult);
                            item.InfoText = "Running Test selesai.";
                        }
                    }
                    break;

                case QcChecklistKind.Camera:
                    _runner.RunCameraTest();
                    item.InfoText = "Camera dibuka.";
                    await ShowResultPopupAsync(item, "Camera - Hasil:",
                        "Camera sudah dibuka dan ditutup. Apakah hasil OK?");
                    break;

                case QcChecklistKind.TouchscreenTest:
                    var touchscreenAllOk = _runner.RunTouchscreenTest();
                    if (touchscreenAllOk)
                    {
                        // All boxes tapped green — auto Pass
                        item.SetResult(Views.Windows.QcResultPopupWindow.QcResult.Pass);
                        item.InfoText = "Touchscreen: Test Done Bang Aman! Semua kotak hijau.";
                    }
                    else
                    {
                        // Some boxes not tapped — show popup
                        item.InfoText = "Touchscreen: beberapa kotak belum di-tap.";
                        await ShowResultPopupAsync(item, "Touchscreen Test - Hasil:",
                            "Beberapa kotak belum hijau. Apakah hasil OK?");
                    }
                    break;

                case QcChecklistKind.PortTest:
                    _lastPortResult = DetectPortStatus();
                    item.InfoText = _lastPortResult.Value.ToDetailText();
                    await ShowResultPopupAsync(item, "Port Test - Hasil:",
                        $"Port: {_lastPortResult.Value.ToDetailText()}. Apakah hasil OK?");
                    break;

                case QcChecklistKind.Touchpad:
                    var touchpadResult = _runner.RunTouchpadTest();
                    _touchpadOk = touchpadResult.Passed;
                    item.InfoText = touchpadResult.Passed
                        ? $"Touchpad valid. {touchpadResult.ToInfoText()}"
                        : $"Touchpad belum valid. {touchpadResult.ToInfoText()}";
                    await ShowResultPopupAsync(item, "Touchpad - Hasil:",
                        "Touchpad test sudah selesai. Apakah hasil OK?");
                    break;

                case QcChecklistKind.Wifi:
                    _wifiOk = DetectWifiConnected();
                    item.InfoText = _wifiOk ? "Wi-Fi terhubung." : "Wi-Fi tidak terhubung.";
                    await ShowResultPopupAsync(item, "WiFi - Hasil:",
                        $"WiFi: {(_wifiOk ? "terhubung" : "tidak terhubung")}. Apakah hasil OK?");
                    break;

                case QcChecklistKind.Backlight:
                    _backlightOk = DetectBacklightActive();
                    item.InfoText = _backlightOk ? "Backlight aktif." : "Backlight tidak terdeteksi.";
                    await ShowResultPopupAsync(item, "Backlight - Hasil:",
                        $"Backlight: {(_backlightOk ? "aktif" : "tidak terdeteksi")}. Apakah hasil OK?");
                    break;

                case QcChecklistKind.Mic:
                    _micOk = _runner.RunMicTest();
                    item.InfoText = _micOk ? "Mic test selesai, rekaman berhasil diputar." : "Mic test selesai, rekaman belum valid.";
                    await ShowResultPopupAsync(item, "Mic Test - Hasil:",
                        "Mic test sudah selesai. Apakah hasil OK?");
                    break;
            }

            item.IsChecked = true;
            UpdateAllStatuses();
            UpdateChecklistSummary();
        }
        catch (Exception ex)
        {
            _logger.Error($"QC run error: {item.Name}", ex);
            _feedback.ShowToast($"{item.Name} gagal dijalankan.", false);
        }
        finally
        {
            item.IsRunning = false;
            item.RunCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>
    /// Shows the QC result popup (OK/X/-), and if the result is Fail (X),
    /// shows a follow-up note popup to capture the failure reason.
    /// Stores the result in the checklist item.
    /// </summary>
    private async Task ShowResultPopupAsync(QcChecklistItem item, string title, string message)
    {
        // Run on UI thread since popups are WPF windows
        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = Views.Windows.QcResultPopupWindow.Show(title, message);

            if (result == Views.Windows.QcResultPopupWindow.QcResult.Fail)
            {
                // Show note popup for failure reason
                var note = Views.Windows.QcNotePopupWindow.Show($"Catatan: Mengapa {item.Name} gagal (X)?");
                item.SetResult(result, note);
            }
            else if (result != Views.Windows.QcResultPopupWindow.QcResult.None)
            {
                item.SetResult(result);
            }
        });
    }

    private void UpdateAllStatuses()
    {
        foreach (var item in ChecklistItems)
        {
            UpdateStatusText(item);
        }

        UpdateChecklistSummary();
    }

    private void UpdateStatusText(QcChecklistItem item)
    {
        if (!item.IsChecked)
        {
            item.StatusText = "-";
            return;
        }

        switch (item.Kind)
        {
            case QcChecklistKind.StorageHealth:
                item.StatusText = _storageHealthPercent.HasValue ? $"{_storageHealthPercent.Value}%" : "PENDING";
                break;
            case QcChecklistKind.BatteryHealth:
                item.StatusText = _batteryHealthPercent.HasValue ? $"{_batteryHealthPercent.Value}%" : "PENDING";
                break;
            case QcChecklistKind.BatteryTest:
                item.StatusText = "OK";
                break;
            case QcChecklistKind.RunningTest:
                item.StatusText = "OK";
                break;
            case QcChecklistKind.PortTest:
                item.StatusText = _lastPortResult.HasValue ? (_lastPortResult.Value.AllOk ? "OK" : "CHECK") : "PENDING";
                break;
            case QcChecklistKind.Touchpad:
                item.StatusText = _touchpadOk ? "OK" : "CHECK";
                break;
            case QcChecklistKind.Wifi:
                item.StatusText = _wifiOk ? "OK" : "CHECK";
                break;
            case QcChecklistKind.Backlight:
                item.StatusText = _backlightOk ? "OK" : "CHECK";
                break;
            case QcChecklistKind.Mic:
                item.StatusText = _micOk ? "OK" : "CHECK";
                break;
            default:
                item.StatusText = "OK";
                break;
        }
    }

    /// <summary>
    /// Shows the standard QC result popup (OK / - / X + failure note) for the Benchmark item.
    /// Bound to the "Status" button next to Uninstall in the Benchmark section.
    /// Same behaviour as the popup shown after the LCD Test.
    /// </summary>
    private async Task ShowQcResultsAsync()
    {
        var benchItem = ChecklistItems.FirstOrDefault(x => x.Kind == QcChecklistKind.Benchmark);
        if (benchItem is null) return;

        await ShowResultPopupAsync(
            benchItem,
            "Benchmark - Status:",
            "Apakah hasil Benchmark OK?");
    }

    /// <summary>
    /// Opens a popup showing ALL QC test results (overall checklist).
    /// Bound to the "Hasil QC" button below Catatan Tambahan.
    /// Also fills the Catatan Otomatis with the results.
    /// The popup contains Save PDF and Save Notepad buttons.
    /// </summary>
    private async Task ShowOverallQcResultsAsync()
    {
        var rows = BuildOverallResultRows();

        // Fill the Catatan Otomatis with current results
        UpdateChecklistSummary();

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            Views.Windows.QcResultsPopupWindow.Show(
                rows,
                savePdfCallback: async () => await SavePdfAsync(),
                saveNotepadCallback: async () => await SaveNotepadAsync());
        });
    }

    /// <summary>
    /// Builds the note for a row, including Running Test duration.
    /// </summary>
    private string BuildRowNote(QcChecklistItem item, QcChecklistKind kind)
    {
        // For Running Test, always include the duration
        if (kind == QcChecklistKind.RunningTest && item.HasResult && !string.IsNullOrWhiteSpace(item.ResultLabel))
        {
            var timeNote = $"Durasi: {item.ResultLabel}";
            if (item.ResultStatus == "FAIL" && !string.IsNullOrWhiteSpace(item.ResultNote))
                return $"{timeNote} | {item.ResultNote}";
            return timeNote;
        }

        return item.ResultNote ?? "";
    }

    /// <summary>
    /// Builds result rows for all QC items in the QC list order.
    /// </summary>
    private List<Views.Windows.QcResultRow> BuildOverallResultRows()
    {
        var orderedKinds = new[]
        {
            QcChecklistKind.LcdTest,
            QcChecklistKind.StorageHealth,
            QcChecklistKind.BatteryHealth,
            QcChecklistKind.BatteryTest,
            QcChecklistKind.KeyboardTest,
            QcChecklistKind.AudioTest,
            QcChecklistKind.Benchmark,
            QcChecklistKind.RunningTest,
            QcChecklistKind.Camera,
            QcChecklistKind.TouchscreenTest,
            QcChecklistKind.PortTest,
            QcChecklistKind.Touchpad,
            QcChecklistKind.Wifi,
            QcChecklistKind.Backlight,
            QcChecklistKind.Mic
        };

        var rows = new List<Views.Windows.QcResultRow>();
        foreach (var kind in orderedKinds)
        {
            var item = ChecklistItems.FirstOrDefault(x => x.Kind == kind);
            if (item is null) continue;

            rows.Add(new Views.Windows.QcResultRow
            {
                Name = item.Name,
                ResultLabel = item.HasResult ? item.ResultLabel : "",
                ResultColor = item.HasResult ? item.ResultColor : "Transparent",
                Status = item.ResultStatus,
                Note = BuildRowNote(item, kind)
            });
        }
        return rows;
    }

    private void UpdateChecklistSummary()
    {
        var map = ChecklistItems.ToDictionary(item => item.Kind, item => item);
        string R(QcChecklistKind kind)
        {
            if (!map.TryGetValue(kind, out var item) || !item.HasResult)
                return "-";
            var label = item.ResultLabel;
            if (item.ResultStatus == "FAIL" && !string.IsNullOrWhiteSpace(item.ResultNote))
                return $"X ({item.ResultNote})";
            return label;
        }

        // Order must match the QC list on the right panel exactly
        ChecklistSummary = string.Join(Environment.NewLine,
        [
            $"LCD = {R(QcChecklistKind.LcdTest)}",
            $"Storage Health = {R(QcChecklistKind.StorageHealth)}",
            $"Battery Health = {R(QcChecklistKind.BatteryHealth)}",
            $"Battery Report = {R(QcChecklistKind.BatteryTest)}",
            $"Keyboard = {R(QcChecklistKind.KeyboardTest)}",
            $"Audio = {R(QcChecklistKind.AudioTest)}",
            $"Benchmark = {R(QcChecklistKind.Benchmark)}",
            $"Running Test = {R(QcChecklistKind.RunningTest)}",
            $"Camera = {R(QcChecklistKind.Camera)}",
            $"Touchscreen = {R(QcChecklistKind.TouchscreenTest)}",
            $"Port = {R(QcChecklistKind.PortTest)}",
            $"Touchpad = {R(QcChecklistKind.Touchpad)}",
            $"Wifi = {R(QcChecklistKind.Wifi)}",
            $"Backlight = {R(QcChecklistKind.Backlight)}",
            $"Mic = {R(QcChecklistKind.Mic)}"
        ]);
    }

    private void SetItemChecked(QcChecklistKind kind, bool value)
    {
        var item = ChecklistItems.FirstOrDefault(x => x.Kind == kind);
        if (item is null || item.IsChecked == value)
        {
            return;
        }

        item.IsChecked = value;
    }

    private void EnsureQcStartDate()
    {
        if (_qcStartDate.HasValue)
        {
            return;
        }

        _qcStartDate = DateTime.Now;
        EnsureNotesTemplate();
    }

    private DateTime? TryReadDriverInstallDate()
    {
        try
        {
            if (!File.Exists(_driverInstallStampPath))
            {
                return null;
            }

            var raw = File.ReadAllText(_driverInstallStampPath).Trim();
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed.ToLocalTime();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Read driver install timestamp failed.", ex);
        }

        return null;
    }

    private void EnsureNotesTemplate()
    {
        var text = string.IsNullOrWhiteSpace(Notes) ? string.Empty : Notes;
        var lines = text
            .Split([Environment.NewLine, "\n"], StringSplitOptions.None)
            .ToList();

        var kodeUnit = GetLineValue(lines, "Kode Unit =");
        if (string.IsNullOrWhiteSpace(kodeUnit))
        {
            kodeUnit = "( Di Isi Manual )";
        }

        var tanggalTest = FormatDate(_qcStartDate);
        var modelLaptop = ResolveModelLaptopForNotes(_cachedSystemModel, _cachedSystemManufacturer);

        UpsertLine(lines, "Kode Unit =", $"Kode Unit = {kodeUnit}");
        UpsertLine(lines, "Tanggal Test =", $"Tanggal Test = {tanggalTest}");
        UpsertLine(lines, "Model Laptop =", $"Model Laptop = {modelLaptop}");

        Notes = string.Join(Environment.NewLine, lines).TrimEnd();
    }

    /// <summary>
    /// Untuk Dell dan HP, System Model WMI sudah menampilkan nama model yang bisa dibaca
    /// (mis. "HP EliteBook 845 G8" atau "Dell Latitude 3210") → isi automatis.
    /// Untuk Thinkpad, Asus, dll yang System Model-nya berupa kode acak
    /// (mis. "20JSJSDSJL") → kosongkan agar diisi manual oleh user.
    /// </summary>
    private static string ResolveModelLaptopForNotes(string systemModel, string manufacturer)
    {
        var lowerMfr = (manufacturer ?? string.Empty).ToLowerInvariant();
        var isDellOrHp = lowerMfr.Contains("dell") || lowerMfr.Contains("hewlett");

        if (isDellOrHp)
        {
            if (!string.IsNullOrWhiteSpace(systemModel) &&
                !string.Equals(systemModel, "N/A", StringComparison.OrdinalIgnoreCase))
            {
                return systemModel.Trim();
            }
        }

        return "( Di Isi Manual )";
    }

    private static string? GetLineValue(IReadOnlyList<string> lines, string prefix)
    {
        foreach (var line in lines)
        {
            if (!line.TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var index = line.IndexOf('=');
            return index >= 0 ? line[(index + 1)..].Trim() : string.Empty;
        }

        return null;
    }

    private static void UpsertLine(IList<string> lines, string prefix, string newLine)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!lines[i].TrimStart().StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            lines[i] = newLine;
            return;
        }

        var insertIndex = 0;
        if (prefix.StartsWith("Model Laptop", StringComparison.OrdinalIgnoreCase))
        {
            insertIndex = Math.Min(2, lines.Count);
        }
        else if (prefix.StartsWith("Tanggal Test", StringComparison.OrdinalIgnoreCase))
        {
            insertIndex = Math.Min(1, lines.Count);
        }

        lines.Insert(insertIndex, newLine);
    }

    private static string FormatDate(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("dd MMMM yyyy", CultureInfo.GetCultureInfo("id-ID"))
            : "-";
    }

    private async Task InstallBenchmarkAsync()
    {
        if (SelectedBenchmark is null)
        {
            return;
        }

        if (!SelectedBenchmark.IsAvailable)
        {
            BenchmarkActionStatus = $"Tidak ditemukan: {SelectedBenchmark.Name}";
            _feedback.ShowToast($"Benchmark {SelectedBenchmark.Name} tidak ditemukan.", false);
            return;
        }

        BenchmarkActionStatus = $"Menjalankan instalasi: {SelectedBenchmark.Name}";
        _runner.RunExternalTarget(SelectedBenchmark.ExecutablePath, true, SelectedBenchmark.Name);
        _ = RemoveDesktopShortcut(SelectedBenchmark.Name);
        SetItemChecked(QcChecklistKind.Benchmark, true);
        _feedback.ShowToast($"{SelectedBenchmark.Name} dijalankan.", true);
        BenchmarkActionStatus = $"Selesai: {SelectedBenchmark.Name}";

        await Task.CompletedTask;
    }

    private async Task RemoveBenchmarkAsync()
    {
        if (SelectedBenchmark is null)
        {
            return;
        }

        var uninstallStarted = TryRunUninstall(SelectedBenchmark.Name);

        if (uninstallStarted)
        {
            BenchmarkActionStatus = $"Uninstall dijalankan: {SelectedBenchmark.Name}";
            _feedback.ShowToast($"Uninstall {SelectedBenchmark.Name} dijalankan.", true);
        }
        else
        {
            BenchmarkActionStatus = $"Uninstall tidak tersedia: {SelectedBenchmark.Name}";
            _feedback.ShowToast($"Uninstall {SelectedBenchmark.Name} tidak tersedia.", false);
        }

        await Task.CompletedTask;
    }

    private bool RemoveDesktopShortcut(string shortcutName)
    {
        try
        {
            var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            var linkPath = Path.Combine(desktopPath, $"{SanitizeFileName(shortcutName)}.lnk");
            if (!File.Exists(linkPath))
            {
                return false;
            }

            File.Delete(linkPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Remove shortcut failed: {shortcutName}", ex);
            return false;
        }
    }

    private bool TryRunUninstall(string appName)
    {
        try
        {
            var uninstallCommand = FindUninstallCommand(appName);
            if (string.IsNullOrWhiteSpace(uninstallCommand))
            {
                return false;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {uninstallCommand}",
                UseShellExecute = true,
                Verb = "runas"
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.Error($"Uninstall failed: {appName}", ex);
            return false;
        }
    }

    private static string? FindUninstallCommand(string appName)
    {
        foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                    using var uninstallRoot = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (uninstallRoot is null)
                    {
                        continue;
                    }

                    foreach (var subKeyName in uninstallRoot.GetSubKeyNames())
                    {
                        using var appKey = uninstallRoot.OpenSubKey(subKeyName);
                        var displayName = appKey?.GetValue("DisplayName")?.ToString() ?? string.Empty;
                        if (!displayName.Contains(appName, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var uninstallString = appKey?.GetValue("UninstallString")?.ToString();
                        if (!string.IsNullOrWhiteSpace(uninstallString))
                        {
                            return uninstallString;
                        }
                    }
                }
                catch
                {
                    // ignore
                }
            }
        }

        return null;
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        var sanitized = new string(chars).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "Shortcut" : sanitized;
    }

    private async Task<int?> TryReadBatteryHealthPercentAsync()
    {
        try
        {
            _batteryHealthNote = string.Empty;
            var logsPath = Path.Combine(_workspaceRoot, "logs");
            Directory.CreateDirectory(logsPath);

            var reportPath = Path.Combine(logsPath, $"battery-health-{DateTime.Now:yyyyMMdd_HHmmss}.html");

            var generated = await TryGenerateBatteryReportAsync(reportPath, elevated: false);

            if (!generated && !IsRunningAsAdmin())
            {
                generated = await TryGenerateBatteryReportAsync(reportPath, elevated: true);
            }

            if (!generated || !File.Exists(reportPath))
            {
                if (string.IsNullOrWhiteSpace(_batteryHealthNote))
                {
                    _batteryHealthNote = "Battery Health: report tidak berhasil dibuat.";
                }

                return null;
            }

            var html = await File.ReadAllTextAsync(reportPath);
            var design = ExtractCapacity(html, "DESIGN CAPACITY");
            var full = ExtractCapacity(html, "FULL CHARGE CAPACITY");
            if (design is not > 0 || full is not > 0)
            {
                return null;
            }

            var percent = (int)Math.Round((double)full.Value / design.Value * 100.0d);
            return Math.Clamp(percent, 0, 100);
        }
        catch (Exception ex)
        {
            _logger.Error("Read battery health failed.", ex);
            return null;
        }
    }

    private async Task<bool> TryGenerateBatteryReportAsync(string reportPath, bool elevated)
    {
        if (!elevated)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /output \"{reportPath}\"",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            await process.WaitForExitAsync();
            if (process.ExitCode == 0)
            {
                return true;
            }

            _logger.Error($"Battery health: powercfg gagal (exit code {process.ExitCode}).");
            return false;
        }

        try
        {
            using var elevatedProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "powercfg",
                Arguments = $"/batteryreport /output \"{reportPath}\"",
                UseShellExecute = true,
                Verb = "runas"
            });

            if (elevatedProcess is null)
            {
                return false;
            }

            await elevatedProcess.WaitForExitAsync();
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            _batteryHealthNote = "Battery Health butuh hak admin (izinkan UAC).";
            return false;
        }
    }

    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static long? ExtractCapacity(string html, string label)
    {
        var pattern = $@"{Regex.Escape(label)}.*?<td>\s*([0-9\.,\s]+)\s*mWh";
        var match = Regex.Match(html, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        if (!match.Success)
        {
            return null;
        }

        var digits = new string(match.Groups[1].Value.Where(char.IsDigit).ToArray());
        return long.TryParse(digits, out var value) ? value : null;
    }

    private int? TryReadStorageHealthPercent()
    {
        try
        {
            var statusPath = ResolveSentinelStatusPath();
            if (string.IsNullOrWhiteSpace(statusPath) || !File.Exists(statusPath))
            {
                return null;
            }

            var healthValues = new List<int>();
            foreach (var line in File.ReadAllLines(statusPath))
            {
                // Format umum HDSentinel.sta:
                // 46115=40,40.00,12,97,40,22169792 -> health berada di index ke-4 (97)
                if (Regex.IsMatch(line, @"^\d{5}="))
                {
                    var parts = line[(line.IndexOf('=') + 1)..].Split(',');
                    if (parts.Length >= 4 && int.TryParse(parts[3], out var valueFromCsv))
                    {
                        healthValues.Add(Math.Clamp(valueFromCsv, 0, 100));
                        continue;
                    }
                }

                // Fallback jika format berbeda, misal ada teks "Health 97%" pada baris status.
                var healthMatch = Regex.Match(line, @"\bHealth\b[^0-9]{0,20}(\d{1,3})\s*%?", RegexOptions.IgnoreCase);
                if (healthMatch.Success &&
                    int.TryParse(healthMatch.Groups[1].Value, out var valueFromText))
                {
                    healthValues.Add(Math.Clamp(valueFromText, 0, 100));
                }
            }

            return healthValues.Count == 0 ? null : healthValues.Min();
        }
        catch (Exception ex)
        {
            _logger.Error("Read storage health failed.", ex);
            return null;
        }
    }

    private async Task<int?> TryReadStorageHealthPercentAsync()
    {
        // Sentinel butuh waktu menulis file status pada beberapa laptop.
        // Retry singkat agar hasil health lebih konsisten terbaca.
        const int retries = 8;
        for (var i = 0; i < retries; i++)
        {
            var value = TryReadStorageHealthPercent();
            if (value.HasValue)
            {
                return value.Value;
            }

            await Task.Delay(700);
        }

        return null;
    }

    private string? ResolveSentinelStatusPath()
    {
        var candidatePaths = new List<string>
        {
            _hdsStatusPath,
            Path.Combine(_qcRoot, "Hard Disk Sentinel", "HDSentinel.sta"),
            Path.Combine(_qcRoot, "Sentinel", "HDSentinel.sta"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Hard Disk Sentinel", "HDSentinel.sta"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Hard Disk Sentinel", "HDSentinel.sta"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Hard Disk Sentinel", "HDSentinel.sta")
        };

        if (Directory.Exists(_qcRoot))
        {
            candidatePaths.AddRange(
                Directory.EnumerateFiles(_qcRoot, "HDSentinel.sta", SearchOption.AllDirectories));
        }

        return candidatePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

    private static PortCheckResult DetectPortStatus()
    {
        return new PortCheckResult(DetectUsbConnected(), DetectAudioJackConnected(), DetectChargerConnected());
    }

    private static bool DetectUsbConnected()
    {
        try
        {
            if (DriveInfo.GetDrives().Any(d => d.DriveType == DriveType.Removable && d.IsReady))
            {
                return true;
            }

            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE PNPDeviceID LIKE 'USB%'");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) &&
                    !name.Contains("Root Hub", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Controller", StringComparison.OrdinalIgnoreCase) &&
                    !name.Contains("Composite Device", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool DetectAudioJackConnected()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name,Status FROM Win32_SoundDevice");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                var status = item["Status"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name) &&
                    (status.Equals("OK", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(status)))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool DetectChargerConnected()
    {
        try
        {
            return GetSystemPowerStatus(out var powerStatus) && powerStatus.ACLineStatus == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectTouchpadAvailable()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PointingDevice");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                if (name.Contains("touchpad", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("precision", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("synaptics", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("elan", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private static bool DetectWifiConnected()
    {
        try
        {
            return NetworkInterface
                .GetAllNetworkInterfaces()
                .Any(net =>
                    net.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 &&
                    net.OperationalStatus == OperationalStatus.Up);
        }
        catch
        {
            return false;
        }
    }

    private static bool DetectBacklightActive()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(@"root\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var brightness = item["CurrentBrightness"];
                if (brightness is byte byteValue)
                {
                    return byteValue > 0;
                }

                if (brightness is ushort ushortValue)
                {
                    return ushortValue > 0;
                }
            }
        }
        catch
        {
            // ignore
        }

        return true;
    }

    private static bool DetectMicrophoneAvailable()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%Microphone%' OR Name LIKE '%Mic%'");
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var name = item["Name"]?.ToString() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return true;
                }
            }
        }
        catch
        {
            // ignore
        }

        return false;
    }

    private string? FindQcFolder(string requestedName)
    {
        if (!Directory.Exists(_qcRoot))
        {
            return null;
        }

        var exact = Path.Combine(_qcRoot, requestedName);
        if (Directory.Exists(exact))
        {
            return exact;
        }

        var token = NormalizeToken(requestedName);
        return Directory
            .EnumerateDirectories(_qcRoot)
            .FirstOrDefault(path => NormalizeToken(Path.GetFileName(path)) == token)
            ?? Directory
                .EnumerateDirectories(_qcRoot)
                .FirstOrDefault(path => NormalizeToken(Path.GetFileName(path)).Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveExecutable(string folderPath)
    {
        if (!Directory.Exists(folderPath))
        {
            return null;
        }

        return Directory
            .EnumerateFiles(folderPath, "*.exe", SearchOption.AllDirectories)
            .Where(path => !Path.GetFileName(path).StartsWith("unins", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string NormalizeToken(string value)
    {
        return new string(value.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
    }

    private static string TryReadWmiValue(string query, string propertyName)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(query);
            foreach (var item in searcher.Get().Cast<ManagementObject>())
            {
                var value = item[propertyName]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }
        }
        catch
        {
            // ignore
        }

        return "N/A";
    }

    private async Task SavePdfAsync()
    {
        if (IsSavingPdf)
        {
            return;
        }

        IsSavingPdf = true;

        try
        {
            QuestPDF.Settings.License = LicenseType.Community;

            var saveDialog = new SaveFileDialog
            {
                Title = "Simpan Hasil QC ke PDF",
                Filter = "PDF File (*.pdf)|*.pdf",
                DefaultExt = "pdf",
                AddExtension = true,
                FileName = $"qc-report-{DateTime.Now:yyyyMMdd_HHmmss}.pdf",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            var selected = saveDialog.ShowDialog();
            if (selected != true || string.IsNullOrWhiteSpace(saveDialog.FileName))
            {
                _feedback.ShowToast("Simpan PDF dibatalkan.", false);
                return;
            }

            var reportPath = saveDialog.FileName;
            var systemModel = TryReadWmiValue("SELECT Model FROM Win32_ComputerSystem", "Model");
            var serial = TryReadWmiValue("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber");
            var generatedAt = DateTime.Now;
            var checklistRows = BuildReportChecklistRows();
            var cardRows = BuildReportCardRows();
            var notesLines = (Notes ?? string.Empty).Split([Environment.NewLine, "\n"], StringSplitOptions.None);
            var codeUnit = GetLineValue(notesLines, "Kode Unit =") ?? "( Di Isi Manual )";
            var modelLaptop = GetLineValue(notesLines, "Model Laptop =") ?? "( Di Isi Manual )";
            var tanggalTest = FormatDate(_qcStartDate);
            var additionalNotes = BuildAdditionalNotes(Notes);

            await Task.Run(() =>
            {
                var tempPath = Path.Combine(Path.GetTempPath(), $"ryotaqc-{Guid.NewGuid():N}.pdf");

                try
                {
                    GenerateStyledReportPdf(
                        tempPath,
                        generatedAt,
                        systemModel,
                        serial,
                        checklistRows,
                        cardRows,
                        codeUnit,
                        tanggalTest,
                        modelLaptop,
                        additionalNotes);
                    _logger.Info("Styled QC PDF template generated.");

                    if (!IsPdfFileValid(tempPath))
                    {
                        throw new InvalidDataException("Generated PDF invalid or empty.");
                    }

                    File.Copy(tempPath, reportPath, overwrite: true);
                }
                finally
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
            });

            _feedback.ShowToast("PDF report QC berhasil disimpan.", true);
            _logger.Info($"QC PDF saved: {reportPath}");

            Process.Start(new ProcessStartInfo
            {
                FileName = reportPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Save QC PDF failed.", ex);
            _feedback.ShowToast("Gagal menyimpan PDF report QC.", false);
        }
        finally
        {
            IsSavingPdf = false;
        }
    }

    /// <summary>
    /// Saves the QC results as a Notepad (.txt) file with a readable table format.
    /// </summary>
    private async Task SaveNotepadAsync()
    {
        if (IsSavingPdf)
        {
            return;
        }

        IsSavingPdf = true;

        try
        {
            var saveDialog = new SaveFileDialog
            {
                Title = "Simpan Hasil QC ke Notepad",
                Filter = "Text File (*.txt)|*.txt",
                DefaultExt = "txt",
                AddExtension = true,
                FileName = $"qc-report-{DateTime.Now:yyyyMMdd_HHmmss}.txt",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            var selected = saveDialog.ShowDialog();
            if (selected != true || string.IsNullOrWhiteSpace(saveDialog.FileName))
            {
                _feedback.ShowToast("Simpan Notepad dibatalkan.", false);
                return;
            }

            var reportPath = saveDialog.FileName;
            var systemModel = TryReadWmiValue("SELECT Model FROM Win32_ComputerSystem", "Model");
            var serial = TryReadWmiValue("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber");
            var generatedAt = DateTime.Now;
            var rows = BuildOverallResultRows();
            var notesLines = (Notes ?? string.Empty).Split([Environment.NewLine, "\n"], StringSplitOptions.None);
            var modelLaptop = GetLineValue(notesLines, "Model Laptop =") ?? "( Di Isi Manual )";

            var sb = new StringBuilder();

            // Header
            sb.AppendLine("============================================");
            sb.AppendLine("           RYOTAQC V5 - HASIL QC");
            sb.AppendLine("============================================");
            sb.AppendLine();
            sb.AppendLine($"Tanggal Test   : {FormatDate(_qcStartDate)}");
            sb.AppendLine($"Model Laptop   : {modelLaptop}");
            sb.AppendLine($"Dibuat         : {generatedAt:dd MMMM yyyy HH:mm:ss}");
            sb.AppendLine($"System Model   : {systemModel}");
            sb.AppendLine($"Serial Number  : {serial}");
            sb.AppendLine();

            // Table header
            sb.AppendLine("============================================");
            sb.AppendLine($"{"Kategori QC",-22} {"Pass",-6} {"-",6} {"X Gagal"}");
            sb.AppendLine("============================================");

            // Data rows
            foreach (var row in rows)
            {
                var passMark = row.Status == "PASS" ? "[V]" : "";
                var skipMark = row.Status == "SKIP" ? "[V]" : "";
                // For FAIL: show [X] + note. For non-FAIL with note (Running Test): show note (duration).
                var failMark = row.Status == "FAIL"
                    ? "[X]" + (string.IsNullOrWhiteSpace(row.Note) ? "" : $" - {row.Note}")
                    : (!string.IsNullOrWhiteSpace(row.Note) ? row.Note : "");

                sb.AppendLine($"{row.Name,-22} {passMark,-6} {skipMark,-6} {failMark}");
            }

            sb.AppendLine("============================================");
            sb.AppendLine();

            // Summary
            var passCount = rows.Count(r => r.Status == "PASS");
            var failCount = rows.Count(r => r.Status == "FAIL");
            var skipCount = rows.Count(r => r.Status == "SKIP");
            var noResult = rows.Count(r => string.IsNullOrEmpty(r.Status));

            sb.AppendLine($"Total: {rows.Count}  |  Pass: {passCount}  |  Skip: {skipCount}  |  Fail: {failCount}  |  Belum: {noResult}");
            sb.AppendLine();

            // Additional notes (filtered: tidak menampilkan Kode Unit / Tanggal Test / Model Laptop
            // karena sudah punya field dedicated di header Notepad / Info section PDF)
            var additionalNotes = BuildAdditionalNotes(Notes);
            if (additionalNotes.Count > 0)
            {
                sb.AppendLine("============================================");
                sb.AppendLine("Catatan Tambahan:");
                sb.AppendLine("============================================");
                foreach (var noteLine in additionalNotes)
                {
                    sb.AppendLine(noteLine);
                }
            }

            await Task.Run(() => File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8));

            _feedback.ShowToast("Notepad QC berhasil disimpan.", true);
            _logger.Info($"QC Notepad saved: {reportPath}");

            Process.Start(new ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = $"\"{reportPath}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _logger.Error("Save QC Notepad failed.", ex);
            _feedback.ShowToast("Gagal menyimpan Notepad QC.", false);
        }
        finally
        {
            IsSavingPdf = false;
        }
    }

    private void GenerateStyledReportPdf(
        string outputPath,
        DateTime generatedAt,
        string systemModel,
        string serial,
        IReadOnlyList<(string Label, string Status, string Note)> checklistRows,
        IReadOnlyList<ReportCard[]> cardRows,
        string codeUnit,
        string tanggalTest,
        string modelLaptop,
        IReadOnlyList<string> additionalNotes)
    {
        var generatedText = generatedAt.ToString("dd MMM yyyy", CultureInfo.GetCultureInfo("id-ID"))
                            + " \u00B7 "
                            + generatedAt.ToString("HH:mm", CultureInfo.GetCultureInfo("id-ID"));

        var passCount = checklistRows.Count(r => r.Status == "OK");
        var failCount = checklistRows.Count(r => r.Status == "X");
        var otherCount = checklistRows.Count - passCount - failCount;

        Document
            .Create(document =>
            {
                document.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(30);
                    page.PageColor("#FFFFFF");
                    page.DefaultTextStyle(t => t.FontSize(10).FontColor("#111827").LineHeight(1.2f));

                    page.Content().Column(root =>
                    {
                        root.Spacing(0);

                        // ---------- Header ----------
                        root.Item().PaddingTop(2).Text("RYOTAQC V / QUALITY ASSURANCE")
                            .FontSize(9.5f).SemiBold().FontColor("#2E4B8F").LetterSpacing(0.08f);

                        root.Item().PaddingTop(4).Text("Quality Control Report")
                            .FontSize(24).Bold().FontColor("#0F1B47");

                        root.Item().PaddingTop(1).Text("Laporan pemeriksaan unit laptop")
                            .FontSize(10).FontColor("#4B5563");

                        // ---------- Info grid ----------
                        root.Item().PaddingTop(14).Table(infoTable =>
                        {
                            infoTable.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn();
                                cols.RelativeColumn();
                                cols.RelativeColumn();
                            });

                            void InfoLabel(string text)
                            {
                                infoTable.Cell().Border(0.8f).BorderColor("#C9D3E0")
                                    .Background("#F3F6FB").PaddingVertical(5).PaddingHorizontal(10)
                                    .Text(text).FontSize(8.5f).SemiBold().FontColor("#5A6B85").LetterSpacing(0.06f);
                            }

                            void InfoValue(string text)
                            {
                                infoTable.Cell().Border(0.8f).BorderColor("#C9D3E0")
                                    .Background("#FFFFFF").PaddingVertical(6).PaddingHorizontal(10)
                                    .Text(string.IsNullOrWhiteSpace(text) ? "-" : text)
                                    .FontSize(11).SemiBold().FontColor("#0F1B47");
                            }

                            InfoLabel("MODEL SYSTEM");
                            InfoLabel("SERIAL NUMBER");
                            InfoLabel("TANGGAL TEST");
                            InfoValue(systemModel);
                            InfoValue(serial);
                            InfoValue(tanggalTest);

                            InfoLabel("GENERATED");
                            InfoLabel("KODE UNIT");
                            InfoLabel("MODEL LAPTOP");
                            InfoValue(generatedText);
                            InfoValue(string.IsNullOrWhiteSpace(codeUnit) ? "Diisi manual" : codeUnit);
                            InfoValue(string.IsNullOrWhiteSpace(modelLaptop) ? "Diisi manual" : modelLaptop);
                        });

                        // ---------- Hasil checklist ----------
                        root.Item().PaddingTop(14).Text("HASIL CHECKLIST")
                            .FontSize(11).Bold().FontColor("#0F1B47").LetterSpacing(0.05f);

                        root.Item().PaddingTop(6).Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn(3.1f);   // Kategori QC
                                cols.RelativeColumn(1.15f);  // PASS
                                cols.RelativeColumn(1.15f);  // -
                                cols.RelativeColumn(1.15f);  // FAIL
                                cols.RelativeColumn(4.6f);   // Catatan
                            });

                            // Header row
                            table.Header(header =>
                            {
                                void Head(string text, bool center = false)
                                {
                                    if (center)
                                    {
                                        header.Cell().Background("#16244B")
                                            .BorderColor("#16244B").BorderTop(0.8f).BorderBottom(0.8f)
                                            .BorderLeft(0.8f).BorderRight(0.8f)
                                            .PaddingVertical(7).PaddingHorizontal(8)
                                            .Text(text).FontSize(9).Bold().FontColor("#FFFFFF")
                                            .LetterSpacing(0.05f).AlignCenter();
                                    }
                                    else
                                    {
                                        header.Cell().Background("#16244B")
                                            .BorderColor("#16244B").BorderTop(0.8f).BorderBottom(0.8f)
                                            .BorderLeft(0.8f).BorderRight(0.8f)
                                            .PaddingVertical(7).PaddingHorizontal(8)
                                            .Text(text).FontSize(9).Bold().FontColor("#FFFFFF")
                                            .LetterSpacing(0.05f);
                                    }
                                }

                                Head("KATEGORI QC");
                                Head("PASS", center: true);
                                Head("\u2014", center: true);
                                Head("FAIL", center: true);
                                Head("CATATAN / KETERANGAN FAIL", center: true);
                            });

                            for (var i = 0; i < checklistRows.Count; i++)
                            {
                                var (label, status, note) = checklistRows[i];
                                var isPass = status == "OK";
                                var isFail = status == "X";

                                var rowBg = isFail ? "#FDF2F2" : isPass ? "#F3FBF5" : "#FFFFFF";
                                var passBg = isPass ? "#E6F7EA" : "#FFFFFF";
                                var failBg = isFail ? "#FDE8E8" : "#FFFFFF";

                                QuestPDF.Infrastructure.IContainer CellBase(QuestPDF.Infrastructure.IContainer c, string bg)
                                {
                                    return c.Border(0.7f).BorderColor("#C9D3E0")
                                        .BorderTop(0).BorderBottom(0.7f)
                                        .BorderLeft(0).BorderRight(0.7f)
                                        .Background(bg).PaddingVertical(5).PaddingHorizontal(8);
                                }

                                // Kategori
                                CellBase(table.Cell(), rowBg)
                                    .Text(label).FontSize(10.5f).Bold().FontColor("#0F1B47");

                                // PASS
                                if (isPass)
                                {
                                    table.Cell().Border(0.7f).BorderColor("#C9D3E0")
                                        .BorderTop(0).BorderBottom(0.7f)
                                        .BorderLeft(0).BorderRight(0.7f)
                                        .Background(passBg).PaddingVertical(3)
                                        .AlignCenter().AlignMiddle()
                                        .Text("\u2713").FontSize(14).Bold().FontColor("#1F9D55");
                                }
                                else
                                {
                                    table.Cell().Border(0.7f).BorderColor("#C9D3E0")
                                        .BorderTop(0).BorderBottom(0.7f)
                                        .BorderLeft(0).BorderRight(0.7f)
                                        .Background(passBg).PaddingVertical(3)
                                        .AlignCenter().AlignMiddle()
                                        .Text(string.Empty);
                                }

                                // -
                                if (status == "-")
                                {
                                    table.Cell().Border(0.7f).BorderColor("#C9D3E0")
                                        .BorderTop(0).BorderBottom(0.7f)
                                        .BorderLeft(0).BorderRight(0.7f)
                                        .Background("#FFFFFF").PaddingVertical(3)
                                        .AlignCenter().AlignMiddle()
                                        .Text("\u2014").FontSize(11).Bold().FontColor("#8A94A6");
                                }
                                else
                                {
                                    table.Cell().Border(0.7f).BorderColor("#C9D3E0")
                                        .BorderTop(0).BorderBottom(0.7f)
                                        .BorderLeft(0).BorderRight(0.7f)
                                        .Background("#FFFFFF").PaddingVertical(3)
                                        .AlignCenter().AlignMiddle()
                                        .Text(string.Empty);
                                }

                                // FAIL
                                if (isFail)
                                {
                                    table.Cell().Border(0.7f).BorderColor("#C9D3E0")
                                        .BorderTop(0).BorderBottom(0.7f)
                                        .BorderLeft(0).BorderRight(0.7f)
                                        .Background(failBg).PaddingVertical(3)
                                        .AlignCenter().AlignMiddle()
                                        .Text("\u2713").FontSize(14).Bold().FontColor("#DC2626");
                                }
                                else
                                {
                                    table.Cell().Border(0.7f).BorderColor("#C9D3E0")
                                        .BorderTop(0).BorderBottom(0.7f)
                                        .BorderLeft(0).BorderRight(0.7f)
                                        .Background(failBg).PaddingVertical(3)
                                        .AlignCenter().AlignMiddle()
                                        .Text(string.Empty);
                                }

                                // Catatan
                                var noteText = note;
                                if (string.IsNullOrWhiteSpace(noteText))
                                {
                                    noteText = "\u2014";
                                }

                                table.Cell().Border(0.7f).BorderColor("#C9D3E0")
                                    .BorderTop(0).BorderBottom(0.7f)
                                    .BorderLeft(0).BorderRight(0.7f)
                                    .Background(rowBg).PaddingVertical(5).PaddingHorizontal(8)
                                    .Text(noteText)
                                    .FontSize(9.5f)
                                    .FontColor(isFail ? "#0F1B47" : "#8A94A6");
                            }
                        });

                        // ---------- Summary bar ----------
                        root.Item().PaddingTop(9).Table(summary =>
                        {
                            summary.ColumnsDefinition(cols =>
                            {
                                cols.RelativeColumn();
                                cols.RelativeColumn();
                                cols.RelativeColumn();
                            });

                            summary.Cell().Border(0.8f).BorderColor("#C9D3E0")
                                .Background("#F3F6FB").PaddingVertical(9).PaddingHorizontal(12)
                                .Text(t =>
                                {
                                    t.Span($"{passCount} ").FontSize(14).Bold().FontColor("#1F9D55");
                                    t.Span("PASS").FontSize(11).Bold().FontColor("#1F9D55");
                                });

                            summary.Cell().Border(0.8f).BorderColor("#C9D3E0")
                                .Background("#F3F6FB").PaddingVertical(9).PaddingHorizontal(12)
                                .Text(t =>
                                {
                                    t.Span($"{otherCount} ").FontSize(14).Bold().FontColor("#5A6B85");
                                    t.Span("BELUM / \u2014").FontSize(11).Bold().FontColor("#5A6B85");
                                });

                            summary.Cell().Border(0.8f).BorderColor("#C9D3E0")
                                .Background("#F3F6FB").PaddingVertical(9).PaddingHorizontal(12)
                                .Text(t =>
                                {
                                    t.Span($"{failCount} ").FontSize(14).Bold().FontColor("#DC2626");
                                    t.Span("FAIL").FontSize(11).Bold().FontColor("#DC2626");
                                });
                        });

                        // ---------- Catatan tambahan ----------
                        if (additionalNotes is { Count: > 0 })
                        {
                            var visibleNotes = additionalNotes
                                .Where(n => !string.IsNullOrWhiteSpace(n))
                                .Take(4)
                                .ToList();

                            if (visibleNotes.Count > 0)
                            {
                                root.Item().PaddingTop(10).Column(notesCol =>
                                {
                                    notesCol.Spacing(1);
                                    foreach (var noteLine in visibleNotes)
                                    {
                                        notesCol.Item().Text(noteLine).FontSize(8.5f).FontColor("#4B5563");
                                    }
                                });
                            }
                        }

                        // ---------- Legend ----------
                        root.Item().PaddingTop(9).Text(t =>
                        {
                            t.Span("Catatan: ").FontSize(9).FontColor("#6B7280");
                            t.Span("\u2713").FontSize(9).SemiBold().FontColor("#1F9D55");
                            t.Span(" menandakan hasil sesuai kolom. Tanda ").FontSize(9).FontColor("#6B7280");
                            t.Span("\u2014").FontSize(9).SemiBold().FontColor("#8A94A6");
                            t.Span(" menunjukkan item belum diuji / tidak ada hasil pada laporan sumber.")
                                .FontSize(9).FontColor("#6B7280");
                        });

                        root.Item().PaddingTop(10).LineHorizontal(0.8f).LineColor("#E5E7EB");

                        // ---------- Footer ----------
                        root.Item().PaddingTop(8).Row(footer =>
                        {
                            footer.RelativeItem().Text(t =>
                            {
                                t.Span("Dibuat oleh ").FontSize(9).FontColor("#6B7280");
                                t.Span("IllhamRyota").FontSize(9).SemiBold().FontColor("#0F1B47");
                                t.Span("  \u00B7  Copyright \u00A9 RyotaQC V").FontSize(9).FontColor("#6B7280");
                            });

                            footer.ConstantItem(200).AlignRight()
                                .Text("RYOTAQC \u00B7 QC & MAINTENANCE")
                                .FontSize(9).Bold().FontColor("#2E4B8F").LetterSpacing(0.05f);
                        });
                    });
                });
            })
            .GeneratePdf(outputPath);
    }

    private static bool IsPdfFileValid(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        var info = new FileInfo(path);
        if (info.Length < 32)
        {
            return false;
        }

        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[5];
        var read = stream.Read(header);
        if (!(read == 5 &&
              header[0] == (byte)'%' &&
              header[1] == (byte)'P' &&
              header[2] == (byte)'D' &&
              header[3] == (byte)'F' &&
              header[4] == (byte)'-'))
        {
            return false;
        }

        var seekBack = Math.Min(4096, (int)stream.Length);
        stream.Seek(-seekBack, SeekOrigin.End);
        var tailBuffer = new byte[seekBack];
        var tailRead = stream.Read(tailBuffer, 0, seekBack);
        if (tailRead <= 0)
        {
            return false;
        }

        var tailText = System.Text.Encoding.ASCII.GetString(tailBuffer, 0, tailRead);
        return tailText.Contains("%%EOF", StringComparison.Ordinal);
    }

    private List<(string Label, string Status, string Note)> BuildReportChecklistRows()
    {
        return
        [
            ("LCD", GetChecklistStatus(QcChecklistKind.LcdTest), GetChecklistNote(QcChecklistKind.LcdTest)),
            ("Storage Health", GetChecklistStatus(QcChecklistKind.StorageHealth), GetChecklistNote(QcChecklistKind.StorageHealth)),
            ("Battery Health", GetChecklistStatus(QcChecklistKind.BatteryHealth), GetChecklistNote(QcChecklistKind.BatteryHealth)),
            ("Battery Report", GetChecklistStatus(QcChecklistKind.BatteryTest), GetChecklistNote(QcChecklistKind.BatteryTest)),
            ("Keyboard", GetChecklistStatus(QcChecklistKind.KeyboardTest), GetChecklistNote(QcChecklistKind.KeyboardTest)),
            ("Audio", GetChecklistStatus(QcChecklistKind.AudioTest), GetChecklistNote(QcChecklistKind.AudioTest)),
            ("Benchmark", GetChecklistStatus(QcChecklistKind.Benchmark), GetChecklistNote(QcChecklistKind.Benchmark)),
            ("Running Test", GetChecklistStatus(QcChecklistKind.RunningTest), GetChecklistNote(QcChecklistKind.RunningTest)),
            ("Touchscreen", GetChecklistStatus(QcChecklistKind.TouchscreenTest), GetChecklistNote(QcChecklistKind.TouchscreenTest)),
            ("Port", GetChecklistStatus(QcChecklistKind.PortTest), GetChecklistNote(QcChecklistKind.PortTest)),
            ("Touchpad", GetChecklistStatus(QcChecklistKind.Touchpad), GetChecklistNote(QcChecklistKind.Touchpad)),
            ("Wifi", GetChecklistStatus(QcChecklistKind.Wifi), GetChecklistNote(QcChecklistKind.Wifi)),
            ("Camera", GetChecklistStatus(QcChecklistKind.Camera), GetChecklistNote(QcChecklistKind.Camera)),
            ("Backlight", GetChecklistStatus(QcChecklistKind.Backlight), GetChecklistNote(QcChecklistKind.Backlight)),
            ("Mic", GetChecklistStatus(QcChecklistKind.Mic), GetChecklistNote(QcChecklistKind.Mic))
        ];
    }

    private List<ReportCard[]> BuildReportCardRows()
    {
        var wifiStatus = GetChecklistStatus(QcChecklistKind.Wifi);
        var cameraStatus = GetChecklistStatus(QcChecklistKind.Camera);
        var combinedWifiCamera = (wifiStatus, cameraStatus) switch
        {
            ("OK", "OK") => "OK",
            ("-", "-") => "-",
            _ => "CHECK"
        };

        return
        [
            [new ReportCard("Storage Health", GetChecklistStatus(QcChecklistKind.StorageHealth), ["benchmark.png", "storage-health.png"]),
             new ReportCard("Battery Health", GetChecklistStatus(QcChecklistKind.BatteryHealth), ["battery-test.png", "battery-health.png"])],
            [new ReportCard("Keyboard", GetChecklistStatus(QcChecklistKind.KeyboardTest), ["keyboard-test.png", "audio-test.png"]),
             new ReportCard("Touchscreen", GetChecklistStatus(QcChecklistKind.TouchscreenTest), ["port-test.png", "touchscreen-test.png"])],
            [new ReportCard("WiFi / Camera", combinedWifiCamera, ["wifi-test.png", "camera-test.png"]),
             new ReportCard("Backlight", GetChecklistStatus(QcChecklistKind.Backlight), ["mic-test.png", "backlight-test.png"])]
        ];
    }

    private static List<string> BuildAdditionalNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
        {
            return [];
        }

        var result = new List<string>();
        var lines = notes.Split([Environment.NewLine, "\n"], StringSplitOptions.None);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (line.StartsWith("Kode Unit =", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Tanggal Test =", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Tanggal Test Instalasi Driver =", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Tanggal Driver =", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Model Laptop =", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            result.Add(line);
        }

        return result.Take(5).ToList();
    }

    private string GetChecklistStatus(QcChecklistKind kind)
    {
        var item = ChecklistItems.FirstOrDefault(x => x.Kind == kind);
        if (item is null || !item.HasResult)
        {
            return "-";
        }

        // Use the new result system: PASS → "OK", SKIP → "-", FAIL → "X"
        return item.ResultStatus switch
        {
            "PASS" => "OK",
            "SKIP" => "-",
            "FAIL" => "X",
            _ => NormalizeReportStatus(item.StatusText)
        };
    }

    /// <summary>
    /// Returns the failure note for a checklist item (if result is FAIL).
    /// Used in the PDF report to show the reason next to the X mark.
    /// </summary>
    private string GetChecklistNote(QcChecklistKind kind)
    {
        var item = ChecklistItems.FirstOrDefault(x => x.Kind == kind);
        if (item is null)
        {
            return string.Empty;
        }

        // For Running Test, always include the duration (time input from popup)
        if (kind == QcChecklistKind.RunningTest && item.HasResult && !string.IsNullOrWhiteSpace(item.ResultLabel))
        {
            var timeNote = $"Durasi: {item.ResultLabel}";
            if (item.ResultStatus == "FAIL" && !string.IsNullOrWhiteSpace(item.ResultNote))
                return $"{timeNote} | {item.ResultNote}";
            return timeNote;
        }

        if (item.ResultStatus != "FAIL")
        {
            return string.Empty;
        }
        return item.ResultNote ?? string.Empty;
    }

    private static string NormalizeReportStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return "-";
        }

        var value = status.Trim();
        if (value.Equals("-", StringComparison.OrdinalIgnoreCase))
        {
            return "-";
        }

        if (value.Contains("OK", StringComparison.OrdinalIgnoreCase))
        {
            return "OK";
        }

        if (value.Contains("CHECK", StringComparison.OrdinalIgnoreCase))
        {
            return "CHECK";
        }

        if (value.EndsWith("%", StringComparison.Ordinal))
        {
            return value;
        }

        if (value.Contains("test", StringComparison.OrdinalIgnoreCase))
        {
            return "TEST";
        }

        return value;
    }

    private static void ComposeReportStatusBadge(QuestPDF.Infrastructure.IContainer container, string status)
    {
        var normalized = NormalizeReportStatus(status);
        var background = normalized switch
        {
            "OK" => "#2CAD52",
            "-" => "#A8B0B7",
            "CHECK" => "#C9893A",
            _ when normalized.EndsWith("%", StringComparison.Ordinal) => "#2E8AC8",
            _ => "#7B8B98"
        };

        container
            .Border(0.8f)
            .BorderColor("#9FB2BE")
            .CornerRadius(6)
            .Background(background)
            .PaddingVertical(2.5f)
            .PaddingHorizontal(7)
            .AlignCenter()
            .Text(normalized)
            .SemiBold()
            .FontSize(9.8f)
            .FontColor(Colors.White);
    }

    private byte[]? LoadPdfIconBytes(string iconFileName)
    {
        var runtimePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Images", "Icons", "QC", iconFileName);
        if (File.Exists(runtimePath))
        {
            return File.ReadAllBytes(runtimePath);
        }

        var sourcePath = Path.Combine(_workspaceRoot, "src", "RyotaQC.App", "Assets", "Images", "Icons", "QC", iconFileName);
        if (File.Exists(sourcePath))
        {
            return File.ReadAllBytes(sourcePath);
        }

        return null;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetSystemPowerStatus(out SystemPowerStatus systemPowerStatus);
}

public sealed record ReportCard(string Title, string Status, string[] IconFiles);

public sealed partial class QcChecklistItem : ObservableObject
{
    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string infoText = string.Empty;

    [ObservableProperty]
    private string statusText = "-";

    [ObservableProperty]
    private bool isChecked;

    [ObservableProperty]
    private bool isRunning;

    [ObservableProperty]
    private string iconPath = string.Empty;

    /// <summary>
    /// Result status: "PASS" (green), "SKIP" (neutral), "FAIL" (red), or "" (none).
    /// </summary>
    [ObservableProperty]
    private string resultStatus = string.Empty;

    /// <summary>
    /// Note entered by user when test result is FAIL.
    /// </summary>
    [ObservableProperty]
    private string resultNote = string.Empty;

    /// <summary>
    /// Result label shown in the colored box next to Run button.
    /// "OK", "-", "X", or "".
    /// </summary>
    [ObservableProperty]
    private string resultLabel = string.Empty;

    /// <summary>
    /// Background color hex for the result box.
    /// Green #1E7A4B, Red #B91C1C, Neutral #44444444, or transparent.
    /// </summary>
    [ObservableProperty]
    private string resultColor = "Transparent";

    /// <summary>
    /// Whether a result has been recorded (for showing the result box).
    /// </summary>
    [ObservableProperty]
    private bool hasResult;

    public QcChecklistKind Kind { get; init; }

    public IAsyncRelayCommand RunCommand { get; set; } = null!;

    /// <summary>
    /// Sets the result from a QcResultPopupWindow.QcResult value.
    /// Updates ResultLabel, ResultColor, ResultStatus, and HasResult.
    /// </summary>
    public void SetResult(Views.Windows.QcResultPopupWindow.QcResult result, string note = "")
    {
        ResultNote = note;
        switch (result)
        {
            case Views.Windows.QcResultPopupWindow.QcResult.Pass:
                ResultLabel = "OK";
                ResultColor = "#FF1E7A4B";
                ResultStatus = "PASS";
                HasResult = true;
                break;
            case Views.Windows.QcResultPopupWindow.QcResult.Skip:
                ResultLabel = "-";
                ResultColor = "#66444444";
                ResultStatus = "SKIP";
                HasResult = true;
                break;
            case Views.Windows.QcResultPopupWindow.QcResult.Fail:
                ResultLabel = "X";
                ResultColor = "#FFB91C1C";
                ResultStatus = "FAIL";
                HasResult = true;
                break;
            default:
                ResultLabel = string.Empty;
                ResultColor = "Transparent";
                ResultStatus = string.Empty;
                HasResult = false;
                break;
        }
    }

    /// <summary>
    /// Sets a percentage-based result with color thresholds.
    /// 0-50%: red (Fail), 60-70%: yellow (Skip), 70-100%: green (Pass).
    /// </summary>
    public void SetPercentageResult(int percent)
    {
        ResultLabel = $"{percent}%";
        if (percent <= 50)
        {
            ResultColor = "#FFB91C1C";
            ResultStatus = "FAIL";
        }
        else if (percent <= 70)
        {
            ResultColor = "#FFE0A800";
            ResultStatus = "SKIP";
        }
        else
        {
            ResultColor = "#FF1E7A4B";
            ResultStatus = "PASS";
        }
        HasResult = true;
    }
}

public sealed class BenchmarkOption
{
    public string Name { get; init; } = string.Empty;

    public string FolderPath { get; init; } = string.Empty;

    public string ExecutablePath { get; init; } = string.Empty;

    public bool IsAvailable => !string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath);

    public override string ToString() => Name;
}

public enum QcChecklistKind
{
    LcdTest,
    StorageHealth,
    BatteryHealth,
    BatteryTest,
    KeyboardTest,
    AudioTest,
    Benchmark,
    RunningTest,
    Camera,
    TouchscreenTest,
    PortTest,
    Touchpad,
    Wifi,
    Backlight,
    Mic
}

public readonly record struct PortCheckResult(bool UsbConnected, bool AudioJackConnected, bool ChargerConnected)
{
    public bool AllOk => UsbConnected && AudioJackConnected && ChargerConnected;

    public string ToDetailText()
    {
        return $"USB={(UsbConnected ? "OK" : "CHECK")}, Jack Audio={(AudioJackConnected ? "OK" : "CHECK")}, Charger={(ChargerConnected ? "OK" : "CHECK")}";
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct SystemPowerStatus
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public int BatteryLifeTime;
    public int BatteryFullLifeTime;
}