using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Media;
using MDSM.Audio;
using MDSM.Core;
using MDSM.Gameplay;
using MDSM.Rendering;
using MDSM.Worlds;

namespace MDSM.Testing;

internal static class WorldSoundtrackChecks
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    internal static void Weather()
    {
        var world = new World(173, WorldSize.Infinite, false);
        world.Climate.VariedGeneration = true;
        foreach (Biome biome in Enum.GetValues<Biome>())
        {
            Vector3? location = null;
            for (int z = 0; z < 2048 && location == null; z += 16)
            for (int x = 0; x < 2048 && location == null; x += 16)
                if (world.Climate.At(x, z) == biome) location = new Vector3(x + .5f, 64, z + .5f);
            Check(location.HasValue, $"Missing biome test fixture for {biome}.");
            Vector3 p = location.Value;
            world.Weather.Restore(new(100, 150, p.X, p.Z, 32, 1));
            Check(world.Weather.RainAt(p) == (biome == Biome.Temperate), $"Wrong rain routing in {biome}.");
            Check(!world.Weather.RainAt(p + Vector3.Right * 33), "Rain crossed its local weather boundary.");
        }

        Color clear = world.Time.Sky;
        float daylight = world.Time.Daylight;
        world.Time.UpdateRainVisual(true, 1);
        Color startingRain = world.Time.Sky;
        world.Time.UpdateRainVisual(true, 4);
        Color overcast = world.Time.Sky;
        Check(clear.B > startingRain.B && startingRain.B > overcast.B && overcast.B - overcast.R < 24,
            "Rain did not fade toward a darker, less saturated sky.");
        world.Time.UpdateRainVisual(false, 1);
        Check(world.Time.Sky.B > overcast.B && world.Time.Sky.B < clear.B, "Rain fade-out skipped its transition.");
        world.Time.UpdateRainVisual(false, 4);
        Check(world.Time.Sky == clear && world.Time.Daylight == daylight, "Clear sky restoration changed gameplay sunlight.");

        var chamber = new World(173, WorldSize.Small, false);
        var sounds = new SoundEvents();
        var source = new Vector3(20.5f, 10.5f, 20.5f);
        sounds.SetListener(source + Vector3.Up * 3);
        Check(sounds.IdleAudible(chamber, source), "Clear nearby idle sound was occluded.");
        chamber.SetBlock(20, 12, 20, BlockType.Stone);
        Check(!sounds.IdleAudible(chamber, source), "Idle sound leaked through the cave roof.");
        int skeleton = SoundCatalog.Find(SoundKind.MobIdle, species:5);
        Check(!sounds.Nearby(source + Vector3.Right * 9, SoundCatalog.Groups[skeleton].Range)
            && SoundCatalog.IdleDelay(5, 0) >= 90 && SoundCatalog.IdleDelay(4, 0) > SoundCatalog.IdleDelay(0, 0),
            "Hostile idle sound tuning is outside the intended range or frequency.");
        Console.WriteLine("PASS: Local temperate rain only, gradual overcast and clear recovery, unchanged sunlight, and cave-roof idle occlusion.");
    }

    internal static int Run()
    {
        try { Weather(); using var game = new Preview(); game.Run(); return 0; }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private sealed class Preview : Game
    {
        internal Preview()
        {
            _ = new GraphicsDeviceManager(this) { PreferredBackBufferWidth = 640, PreferredBackBufferHeight = 360 };
            Content.RootDirectory = Path.Combine(AppContext.BaseDirectory, "Content");
        }

        protected override void Draw(GameTime time)
        {
            var settings = new GameSettings { MasterVolume = .04f, MusicVolume = .6f };
            using var soundtrack = new WorldSoundtrack(Content.RootDirectory, settings, new Random(173));
            using var effects = new AudioManager(Content.RootDirectory, settings);
            void Step(double seconds, bool rain = false, bool playing = true, bool inWorld = true, bool foreground = true)
                => soundtrack.Update(seconds, inWorld, playing, rain, foreground);
            void Fade(bool rain = false, bool playing = true, bool inWorld = true)
            {
                for (int i = 0; i < 17; i++) Step(.25, rain, playing, inWorld);
            }

            Step(900);
            double initialWait = soundtrack.SilenceRemaining;
            Check(soundtrack.CurrentTrack == -1 && initialWait is >= 300 and <= 900, "World entry started immediate music.");
            Step(900, playing: false);
            Check(soundtrack.CurrentTrack == -1 && soundtrack.SilenceRemaining == initialWait, "Paused time consumed the music delay.");
            Step(initialWait - 1);
            Check(soundtrack.CurrentTrack == -1, "A song started early.");
            Step(1);
            var firstSong = MediaPlayer.Queue.ActiveSong;
            Check(soundtrack.CurrentTrack is >= 0 and < 5 && MediaPlayer.State == MediaState.Playing
                && MediaPlayer.Volume == 0 && firstSong.Duration.TotalSeconds >= 25,
                $"First stream failed to start silently with correct duration ({firstSong?.Duration}).");
            int firstTrack = soundtrack.CurrentTrack;
            Step(.25);
            Check(MediaPlayer.Volume > 0 && MediaPlayer.Volume < settings.MasterVolume * settings.MusicVolume, "No gradual music fade-in.");
            Fade();
            Check(Math.Abs(MediaPlayer.Volume - settings.MasterVolume * settings.MusicVolume) < .0001, "Music volume did not reach requested level.");
            settings.MusicVolume = .3f;
            Step(.01);
            Check(Math.Abs(MediaPlayer.Volume - .012f) < .0001, "Live Music slider did not update playback.");
            Check(effects.Play(new(SoundCatalog.Find(SoundKind.Pickup),default,false),default,Vector3.Right)
                && effects.ActiveVoices == 1 && MediaPlayer.State == MediaState.Playing,
                "Stream playback competed with the effects voice pool.");

            MediaPlayer.Play(firstSong, firstSong.Duration - TimeSpan.FromSeconds(2));
            Step(.01);
            Check(MediaPlayer.Volume is > 0 and < .009f, $"Song ending did not fade from stream position {MediaPlayer.PlayPosition}.");
            MediaPlayer.Play(firstSong);
            Step(.25, rain:true);
            Check(soundtrack.CurrentTrack < 5 && MediaPlayer.Volume is > 0 and < .012f, "Rain interrupted music abruptly.");
            Fade(rain:true);
            Check(soundtrack.CurrentTrack == 5 && MediaPlayer.IsRepeating && MediaPlayer.Queue.ActiveSong.Duration.TotalMinutes > 4,
                "Rain did not take over the shared channel.");
            Fade(rain:true);
            Check(MediaPlayer.Volume > 0, "Rain did not fade in.");
            Fade(rain:true, playing:false);
            Check(MediaPlayer.State == MediaState.Paused && MediaPlayer.Volume == 0, "Pausing left ambient audio audible.");
            Step(.25,rain:true);
            Check(MediaPlayer.State == MediaState.Playing && MediaPlayer.Volume > 0, "Rain did not fade back in after pause.");
            Step(.01, rain:true, foreground:false);
            Check(MediaPlayer.State == MediaState.Paused, "Rain kept playing after the window lost focus.");
            Step(.25, rain:true);
            Check(MediaPlayer.State == MediaState.Playing && MediaPlayer.Volume > 0, "Rain did not resume after focus returned.");
            Fade();
            Check(soundtrack.CurrentTrack == -1 && MediaPlayer.State == MediaState.Stopped
                && soundtrack.SilenceRemaining is >= 295 and <= 900, "Rain exit did not stop playback and preserve song cooldown.");

            // Force the backend's stopped state at each end so ordering and the
            // full randomized silence range can be checked without waiting an hour.
            var played = new HashSet<int> { firstTrack };
            int previousTrack = firstTrack;
            for (int i = 0; i < 4; i++)
            {
                Step(soundtrack.SilenceRemaining);
                Check(soundtrack.CurrentTrack is >= 0 and < 5, "A playlist entry did not decode/play.");
                Check(played.Add(soundtrack.CurrentTrack), "The playlist repeated a track within its first shuffle.");
                previousTrack = soundtrack.CurrentTrack;
                MediaPlayer.Stop();
                Step(0);
                Check(soundtrack.SilenceRemaining is >= 300 and <= 900, "Song completion skipped the randomized delay.");
            }
            Step(soundtrack.SilenceRemaining);
            Check(soundtrack.CurrentTrack >= 0 && soundtrack.CurrentTrack != previousTrack, "The next shuffle repeated the last song.");
            Fade();
            Step(.25, playing:false, inWorld:false);
            Check(MediaPlayer.Volume > 0, "World exit cut off the soundtrack instead of fading.");
            Fade(playing:false, inWorld:false);
            Check(soundtrack.CurrentTrack == -1 && MediaPlayer.State == MediaState.Stopped, "Leaving the world leaked a music stream.");

            using var missing = new WorldSoundtrack(Path.Combine(Content.RootDirectory, "missing-test-assets"), settings);
            missing.Update(0, true, true, true, true);
            missing.Update(900, true, true, true, true);
            Check(missing.CurrentTrack == -1 && missing.SilenceRemaining is >= 300 and <= 900,
                "Unavailable files left a stream or caused the scheduler to retry every frame.");

            SkyPreview();
            Console.WriteLine("PASS: Real DesktopGL streams, delayed shuffle, 5–15 minute waits, natural-end and state fades, rain exclusivity, independent effects, pause, and world exit.");
            Exit();
        }

        private void SkyPreview()
        {
            using var atlas = new TextureAtlas(Content);
            using var target = new RenderTarget2D(GraphicsDevice, 640, 360, false, SurfaceFormat.Color, DepthFormat.Depth24);
            var world = new World(173, WorldSize.Small, false);
            using var renderer = new WorldRenderer(GraphicsDevice, atlas, world);
            var camera = new Camera { Position = new(20, 64, 20) };
            camera.RestoreOrientation(0, .15f);
            Color[] Frame(string name)
            {
                GraphicsDevice.SetRenderTarget(target);
                GraphicsDevice.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, world.Time.Sky, 1, 0);
                renderer.Draw(camera, null);
                GraphicsDevice.SetRenderTarget(null);
                var pixels = new Color[target.Width * target.Height];
                target.GetData(pixels);
                using var file = File.Create(Path.Combine(AppContext.BaseDirectory, name));
                target.SaveAsPng(file, target.Width, target.Height);
                return pixels;
            }
            var clear = Frame("weather-clear.png");
            world.Time.UpdateRainVisual(true, 2);
            var blend = Frame("weather-transition.png");
            world.Time.UpdateRainVisual(true, 3);
            var overcast = Frame("weather-overcast.png");
            Check(clear[0].B > blend[0].B && blend[0].B > overcast[0].B, "The rendered sky missed the overcast transition.");
        }
    }
}
