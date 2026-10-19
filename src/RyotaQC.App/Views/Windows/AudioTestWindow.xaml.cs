using System.IO;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;

namespace RyotaQC.App.Views.Windows;

public partial class AudioTestWindow : Window
{
    private readonly string? _audio1Path;
    private readonly string? _audio2Path;
    private readonly string? _leftPath;
    private readonly string? _rightPath;
    private readonly string _tonePath;

    public AudioTestWindow(string? audio1Path = null, string? audio2Path = null, string? leftPath = null, string? rightPath = null)
    {
        InitializeComponent();

        _audio1Path = NormalizePath(audio1Path);
        _audio2Path = NormalizePath(audio2Path);
        _leftPath = NormalizePath(leftPath);
        _rightPath = NormalizePath(rightPath);

        _tonePath = Path.Combine(Path.GetTempPath(), "ryotaqc-tone.wav");
        EnsureToneWave(_tonePath);

        PlayAudio1Command = new RelayCommand(() => PlayMedia(_audio1Path, 0.0, "AudioTest.mp4 (Normal Balance)"));
        PlayAudio2Command = new RelayCommand(() => PlayMedia(_audio2Path, 0.0, "AudioTest 2.mp4 (Normal Balance)"));
        PlayLeftCommand = new RelayCommand(() => PlayMedia(_leftPath, -1.0, "Left.mp3 (Full Left)"));
        PlayRightCommand = new RelayCommand(() => PlayMedia(_rightPath, 1.0, "Right.mp3 (Full Right)"));

        DataContext = this;
    }

    public IRelayCommand PlayAudio1Command { get; }

    public IRelayCommand PlayAudio2Command { get; }

    public IRelayCommand PlayLeftCommand { get; }

    public IRelayCommand PlayRightCommand { get; }

    private void PlayMedia(string? mediaPath, double balance, string label)
    {
        try
        {
            var sourcePath = !string.IsNullOrWhiteSpace(mediaPath) && File.Exists(mediaPath)
                ? mediaPath
                : _tonePath;

            PreviewPlayer.Stop();
            PreviewPlayer.Close();

            PreviewPlayer.Balance = balance;
            PreviewPlayer.Volume = 1.0;
            PreviewPlayer.Source = new Uri(sourcePath);
            PreviewPlayer.Position = TimeSpan.Zero;
            PreviewPlayer.Play();

            PreviewHint.Visibility = Path.GetExtension(sourcePath).Equals(".mp4", StringComparison.OrdinalIgnoreCase)
                ? Visibility.Collapsed
                : Visibility.Visible;

            NowPlayingText.Text = $"Now Playing: {label}";
            Title = $"Audio Test - {label}";
        }
        catch
        {
            MessageBox.Show("Gagal memutar media.", "RyotaQC V", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureToneWave(string path)
    {
        if (File.Exists(path))
        {
            return;
        }

        const int sampleRate = 44100;
        const short bitsPerSample = 16;
        const short channels = 2;
        const int durationMs = 450;
        const double frequency = 880;

        var sampleCount = sampleRate * durationMs / 1000;
        var dataSize = sampleCount * channels * (bitsPerSample / 8);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var writer = new BinaryWriter(fs);

        writer.Write("RIFF".ToCharArray());
        writer.Write(36 + dataSize);
        writer.Write("WAVE".ToCharArray());
        writer.Write("fmt ".ToCharArray());
        writer.Write(16);
        writer.Write((short)1);
        writer.Write(channels);
        writer.Write(sampleRate);
        writer.Write(sampleRate * channels * (bitsPerSample / 8));
        writer.Write((short)(channels * (bitsPerSample / 8)));
        writer.Write(bitsPerSample);
        writer.Write("data".ToCharArray());
        writer.Write(dataSize);

        for (var i = 0; i < sampleCount; i++)
        {
            var sample = (short)(Math.Sin((2 * Math.PI * i * frequency) / sampleRate) * short.MaxValue * 0.24);
            writer.Write(sample);
            writer.Write(sample);
        }
    }

    private void Stop_OnClick(object sender, RoutedEventArgs e)
    {
        PreviewPlayer.Stop();
        NowPlayingText.Text = "Now Playing: -";
        PreviewHint.Visibility = Visibility.Visible;
    }

    private void Close_OnClick(object sender, RoutedEventArgs e)
    {
        PreviewPlayer.Stop();
        PreviewPlayer.Close();
        Close();
    }
}


