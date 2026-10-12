using System.IO;
using System.Windows.Media;
using System.Windows.Threading;

namespace CS2Cheat.Features;

public sealed class HitSoundPlayer : IDisposable
{
    private readonly string _path;
    private readonly ManualResetEventSlim _ready = new();
    private Thread? _thread;
    private Dispatcher? _dispatcher;
    private MediaPlayer? _player;
    private bool _pendingPlay;
    private bool _disposed;

    public HitSoundPlayer(string path)
    {
        _path = path;
        if (!File.Exists(path))
        {
            Console.WriteLine($"[WARN] Hit sound file not found: {path}");
            return;
        }

        _thread = new Thread(AudioThread) { IsBackground = true, Name = "Hit sound playback" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait();
    }

    private void AudioThread()
    {
        try
        {
            _dispatcher = Dispatcher.CurrentDispatcher;
            _player = new MediaPlayer();
            _player.MediaOpened += (_, _) =>
            {
                if (!_pendingPlay) return;
                _pendingPlay = false;
                _player.Play();
            };
            _player.MediaFailed += (_, e) => Console.WriteLine($"[WARN] Hit sound playback failed: {e.ErrorException?.Message ?? "Windows could not decode the MP3."}");
            _player.Open(new Uri(_path, UriKind.Absolute));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[WARN] Hit sound playback unavailable: {ex.Message}");
        }
        finally
        {
            _ready.Set();
        }

        Dispatcher.Run();
        _player?.Close();
    }

    public void Play()
    {
        var dispatcher = _dispatcher;
        if (_disposed || dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvoke(() =>
        {
            if (_player == null) return;
            if (_player.NaturalDuration.HasTimeSpan)
                _player.Position = TimeSpan.Zero;
            _pendingPlay = true;
            if (_player.NaturalDuration.HasTimeSpan)
            {
                _pendingPlay = false;
                _player.Play();
            }
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var dispatcher = _dispatcher;
        if (dispatcher == null || dispatcher.HasShutdownStarted) return;
        dispatcher.BeginInvokeShutdown(DispatcherPriority.Send);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
