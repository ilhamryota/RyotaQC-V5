using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace RyotaQC.App.Views.Windows;

public partial class MicTestWindow : Window
{
    private const string RecordAlias = "ryotaqcmic";
    private readonly DispatcherTimer _timer;
    private readonly Stopwatch _stopwatch;
    private readonly MediaPlayer _player;
    private string _recordPath = string.Empty;
    private bool _isRecording;
    private bool _mciOpened;

    public MicTestWindow()
    {
        InitializeComponent();
        _stopwatch = new Stopwatch();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => ElapsedText.Text = $"Elapsed: {_stopwatch.Elapsed:mm\\:ss}";
        _player = new MediaPlayer();
        UpdateButtons();
    }

    public bool Passed { get; private set; }

    private void StartRecording_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isRecording)
        {
            return;
        }

        try
        {
            _recordPath = BuildRecordingPath();
            var folder = Path.GetDirectoryName(_recordPath);
            Directory.CreateDirectory(string.IsNullOrWhiteSpace(folder)
                ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                : folder);
            if (File.Exists(_recordPath))
            {
                File.Delete(_recordPath);
            }

            ExecuteMci("close all");
            ExecuteMci($"open new Type waveaudio Alias {RecordAlias}");
            _mciOpened = true;
            ExecuteMci($"record {RecordAlias}");

            _isRecording = true;
            _stopwatch.Reset();
            _stopwatch.Start();
            _timer.Start();
            StatusText.Text = "Recording berjalan... silakan bicara ke mikrofon.";
            SavedFileText.Text = "File rekaman: (proses recording)";
            UpdateButtons();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Gagal mulai recording: {ex.Message}";
            _isRecording = false;
            _mciOpened = false;
            _stopwatch.Reset();
            _timer.Stop();
            UpdateButtons();
        }
    }

    private void StopRecording_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_isRecording)
        {
            return;
        }

        try
        {
            ExecuteMci($"stop {RecordAlias}");
            ExecuteMci($"save {RecordAlias} \"{_recordPath}\"");
            ExecuteMci($"close {RecordAlias}");
            _mciOpened = false;

            _isRecording = false;
            _stopwatch.Stop();
            _timer.Stop();

            var fileOk = File.Exists(_recordPath) && new FileInfo(_recordPath).Length > 1024;
            Passed = fileOk;
            StatusText.Text = fileOk
                ? "Recording selesai. Klik Play Recording untuk cek suara."
                : "Recording selesai tapi file kosong / tidak valid.";
            SavedFileText.Text = $"File rekaman: {_recordPath}";
            UpdateButtons();
        }
        catch (Exception ex)
        {
            _isRecording = false;
            _stopwatch.Stop();
            _timer.Stop();
            StatusText.Text = $"Gagal stop recording: {ex.Message}";
            UpdateButtons();
        }
    }

    private void PlayRecording_OnClick(object sender, RoutedEventArgs e)
    {
        if (!File.Exists(_recordPath))
        {
            StatusText.Text = "File rekaman belum ada.";
            return;
        }

        try
        {
            _player.Stop();
            _player.Open(new Uri(_recordPath, UriKind.Absolute));
            _player.Play();
            StatusText.Text = "Memutar hasil recording mic...";
            Passed = true;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Gagal play recording: {ex.Message}";
        }
    }

    private void OpenFolder_OnClick(object sender, RoutedEventArgs e)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "RyotaQC");
        Directory.CreateDirectory(folder);

        if (File.Exists(_recordPath))
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{_recordPath}\"",
                UseShellExecute = true
            });
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = folder,
            UseShellExecute = true
        });
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        _timer.Stop();
        _stopwatch.Stop();
        _player.Stop();
        _player.Close();

        if (_isRecording && _mciOpened)
        {
            try
            {
                ExecuteMci($"stop {RecordAlias}");
                if (!string.IsNullOrWhiteSpace(_recordPath))
                {
                    ExecuteMci($"save {RecordAlias} \"{_recordPath}\"");
                }
            }
            catch
            {
                // ignore
            }
        }

        if (_mciOpened)
        {
            try
            {
                ExecuteMci($"close {RecordAlias}");
            }
            catch
            {
                // ignore
            }
        }

        base.OnClosing(e);
    }

    private void UpdateButtons()
    {
        StartButton.IsEnabled = !_isRecording;
        StopButton.IsEnabled = _isRecording;
        PlayButton.IsEnabled = !_isRecording && File.Exists(_recordPath);
    }

    private static string BuildRecordingPath()
    {
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var folder = Path.Combine(desktop, "RyotaQC");
        return Path.Combine(folder, $"MicTest_{DateTime.Now:yyyyMMdd_HHmmss}.wav");
    }

    private static void ExecuteMci(string command)
    {
        var errorCode = mciSendString(command, null, 0, IntPtr.Zero);
        if (errorCode == 0)
        {
            return;
        }

        var message = new StringBuilder(256);
        if (mciGetErrorString(errorCode, message, message.Capacity))
        {
            throw new InvalidOperationException(message.ToString());
        }

        throw new InvalidOperationException($"MCI error code: {errorCode}");
    }

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern int mciSendString(string command, StringBuilder? buffer, int bufferSize, IntPtr hwndCallback);

    [DllImport("winmm.dll", CharSet = CharSet.Auto)]
    private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);
}
