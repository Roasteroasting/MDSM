using System;
using System.IO;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Media;
using MDSM.Core;

namespace MDSM.Audio;

// Owns the single streamed channel. Effects still use AudioManager's voice pool.
internal sealed class WorldSoundtrack(string contentRoot, GameSettings settings, Random random = null) : IDisposable
{
    private const int SongCount = 5;
    private const int Rain = SongCount;
    private const double FadeSeconds = 4;
    private readonly string _root = Path.GetFullPath(contentRoot, AppContext.BaseDirectory);
    private readonly Random _random = random ?? Random.Shared;
    private readonly bool[] _unavailable = new bool[Rain + 1];
    private readonly int[] _order = new int[SongCount];
    private Song _stream;
    private int _track = -1, _lastSong = -1, _next = SongCount;
    private double _routeFade;
    private bool _inWorld, _disabled;
    internal double SilenceRemaining { get; private set; }
    internal int CurrentTrack => _track;

    internal void Update(double elapsed, bool inWorld, bool playing, bool raining, bool foreground)
    {
        if (_disabled || !double.IsFinite(elapsed) || elapsed < 0) return;
        try
        {
            bool entering = inWorld && !_inWorld;
            if (entering)
            {
                Stop();
                ResetSilence();
            }
            _inWorld = inWorld;

            if (!foreground)
            {
                if (!inWorld) Stop();
                else if (_stream != null && MediaPlayer.State == MediaState.Playing) MediaPlayer.Pause();
                _routeFade = 0;
                return;
            }

            bool active = inWorld && playing;
            if (active && !entering && (_track < 0 || _track == Rain))
                SilenceRemaining = Math.Max(0, SilenceRemaining - elapsed);
            bool wantsRain = raining && settings.EffectsVolume > 0 && !_unavailable[Rain];

            if (_stream != null)
            {
                if (MediaPlayer.State == MediaState.Stopped)
                    Finish();
                else
                {
                    bool keep = inWorld && (wantsRain ? _track == Rain : _track != Rain);
                    if (active && keep && MediaPlayer.State == MediaState.Paused)
                        MediaPlayer.Resume();

                    double step = Math.Min(elapsed, .25) / FadeSeconds;
                    _routeFade = Math.Clamp(_routeFade + (active && keep ? step : -step), 0, 1);
                    double endFade = _track == Rain ? 1 :
                        Math.Clamp((_stream.Duration - MediaPlayer.PlayPosition).TotalSeconds / FadeSeconds, 0, 1);
                    SetVolume(_routeFade * endFade);

                    if (_routeFade > 0) return;
                    if (!inWorld || (active && !keep)) Finish();
                    else if (!active && MediaPlayer.State == MediaState.Playing) MediaPlayer.Pause();
                    if (_stream != null) return;
                }
            }

            if (!active || settings.MasterVolume <= 0) return;
            if (wantsRain) Start(Rain);
            else if (SilenceRemaining <= 0 && settings.MusicVolume > 0)
            {
                for (int attempt = 0; attempt < SongCount; attempt++)
                {
                    int song = NextSong();
                    if (Start(song)) { _lastSong = song; return; }
                }
                ResetSilence();
            }
        }
        catch (Exception error) when (error is NoAudioHardwareException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        {
            _disabled = true;
            Dispose();
            Console.Error.WriteLine("WORLD AUDIO UNAVAILABLE: " + error.Message);
        }
    }

    private int NextSong()
    {
        if (_next == SongCount)
        {
            for (int i = 0; i < SongCount; i++) _order[i] = i;
            _random.Shuffle(_order);
            if (_order[0] == _lastSong) (_order[0], _order[1]) = (_order[1], _order[0]);
            _next = 0;
        }
        return _order[_next++];
    }

    private void ResetSilence() => SilenceRemaining = 300 + _random.NextDouble() * 600;

    private bool Start(int track)
    {
        if (_unavailable[track]) return false;
        string asset = track == Rain ? "Sounds/Ambient/Rain-Thunder-Ambience.ogg" : $"Sounds/Music/song-{track}.ogg";
        string path = Path.Combine(_root, asset);
        try
        {
            _stream = Song.FromUri(Path.GetFileNameWithoutExtension(path), new Uri(path));
            _track = track;
            _routeFade = 0;
            MediaPlayer.IsShuffled = false;
            MediaPlayer.IsRepeating = track == Rain;
            SetVolume(0);
            MediaPlayer.Play(_stream);
            return true;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Stop();
            _unavailable[track] = true;
            Console.Error.WriteLine($"WORLD AUDIO SKIPPED {asset}: {error.Message}");
            return false;
        }
    }

    private void SetVolume(double fade)
    {
        double gain = _track == Rain ? settings.EffectsVolume * .32 : settings.MusicVolume;
        double level = Math.Clamp(fade, 0, 1);
        float volume = (float)(settings.MasterVolume * gain * level * level * (3 - 2 * level));
        if (MediaPlayer.Volume != volume) MediaPlayer.Volume = volume;
    }

    private void Finish()
    {
        if (_track >= 0 && _track != Rain) ResetSilence();
        Stop();
    }

    private void Stop()
    {
        Song stream = _stream;
        if (stream == null) return;
        try { MediaPlayer.Stop(); }
        finally
        {
            _stream = null;
            _track = -1;
            _routeFade = 0;
            stream.Dispose();
        }
    }

    public void Dispose()
    {
        try { Stop(); }
        catch (Exception error) when (error is NoAudioHardwareException or InvalidOperationException or DllNotFoundException or TypeInitializationException)
        { Console.Error.WriteLine("WORLD AUDIO STOP FAILED: " + error.Message); }
    }
}
