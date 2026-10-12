using System.Runtime.InteropServices;
using System.Text;

namespace CS2Cheat.Features;

public sealed class HitSoundPlayer : IDisposable
{
    private const string Alias = "fullyexternalcs2_hit";
    private readonly object _gate = new();
    private bool _opened;
    private bool _reportedError;

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern int mciSendString(string command, IntPtr returnValue, int returnLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode)]
    private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextLength);

    public HitSoundPlayer(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                Report($"Hit sound file was not found: {path}");
                return;
            }

            var result = mciSendString($"open \"{path}\" type MPEGVideo alias {Alias}", IntPtr.Zero, 0, IntPtr.Zero);
            if (result != 0)
            {
                Report(GetError(result));
                return;
            }

            _opened = true;
        }
        catch (Exception ex)
        {
            Report(ex.Message);
        }
    }

    public void Play()
    {
        lock (_gate)
        {
            if (!_opened) return;
            mciSendString($"stop {Alias}", IntPtr.Zero, 0, IntPtr.Zero);
            mciSendString($"seek {Alias} to start", IntPtr.Zero, 0, IntPtr.Zero);
            var result = mciSendString($"play {Alias}", IntPtr.Zero, 0, IntPtr.Zero);
            if (result != 0) Report(GetError(result));
        }
    }

    private static string GetError(int code)
    {
        var message = new StringBuilder(256);
        return mciGetErrorString(code, message, message.Capacity)
            ? message.ToString()
            : $"Windows audio error {code}.";
    }

    private void Report(string message)
    {
        if (_reportedError) return;
        _reportedError = true;
        Console.WriteLine($"[WARN] Hit sound unavailable: {message}");
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (!_opened) return;
            mciSendString($"close {Alias}", IntPtr.Zero, 0, IntPtr.Zero);
            _opened = false;
        }
    }
}
