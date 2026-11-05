using System;
using System.Collections.Generic;
using System.IO;
using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Audio;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.Services.Audio;
using NUnit.Framework;

namespace CkanUI.Services.Tests.Audio;

[TestFixture]
public sealed class AudioServiceTests
{
    private sealed class FakeSettingsStore : IUiSettingsStore
    {
        public UiSettings Current { get; set; } = new();

        public List<UiSettings> Saved { get; } = new();

        public event Action<UiSettings>? Changed;

        public UiSettings Load() => Current;

        public OperationResult<bool> Save(UiSettings settings)
        {
            Saved.Add(settings);
            Current = settings;
            Changed?.Invoke(settings);
            return OperationResult<bool>.Ok(true);
        }

        public void TriggerChange(UiSettings settings)
        {
            Current = settings;
            Changed?.Invoke(settings);
        }
    }

    [Test]
    public void AudioService_InitializesFromStore()
    {
        var store = new FakeSettingsStore
        {
            Current = new UiSettings
            {
                MasterMuted = true,
                SoundEffectsEnabled = false,
                SoundEffectsVolume = 0.42,
                MusicEnabled = false,
                MusicVolume = 0.88,
            }
        };

        var soundEffects = new SoundEffectService(store);
        var musicPlayer = new MusicPlayerService(store);
        var audioService = new AudioService(soundEffects, musicPlayer, store);

        Assert.Multiple(() =>
        {
            Assert.That(audioService.MasterMuted, Is.True);
            Assert.That(soundEffects.IsEnabled, Is.False);
            Assert.That(soundEffects.Volume, Is.EqualTo(0.42).Within(0.001));
            Assert.That(musicPlayer.Volume, Is.EqualTo(0.88).Within(0.001));
        });
    }

    [Test]
    public void AudioService_ToggleMasterMute_TogglesMuteStateAndPersists()
    {
        var store = new FakeSettingsStore
        {
            Current = new UiSettings { MasterMuted = false }
        };

        var soundEffects = new SoundEffectService(store);
        var musicPlayer = new MusicPlayerService(store);
        var audioService = new AudioService(soundEffects, musicPlayer, store);

        bool eventFired = false;
        bool lastEventVal = false;
        audioService.MasterMuteChanged += (s, muted) =>
        {
            eventFired = true;
            lastEventVal = muted;
        };

        // Act: toggle
        audioService.ToggleMasterMute();

        Assert.Multiple(() =>
        {
            Assert.That(audioService.MasterMuted, Is.True);
            Assert.That(store.Current.MasterMuted, Is.True);
            Assert.That(eventFired, Is.True);
            Assert.That(lastEventVal, Is.True);
        });

        // Act: toggle again
        eventFired = false;
        audioService.ToggleMasterMute();

        Assert.Multiple(() =>
        {
            Assert.That(audioService.MasterMuted, Is.False);
            Assert.That(store.Current.MasterMuted, Is.False);
            Assert.That(eventFired, Is.True);
            Assert.That(lastEventVal, Is.False);
        });
    }

    [Test]
    public void AudioService_SoundEffectService_RespectsEnabledAndClamping()
    {
        var store = new FakeSettingsStore
        {
            Current = new UiSettings { SoundEffectsEnabled = false }
        };

        var soundEffects = new SoundEffectService(store);

        // Volume clamping
        soundEffects.Volume = 1.5;
        Assert.That(soundEffects.Volume, Is.EqualTo(1.0).Within(0.001));

        soundEffects.Volume = -0.2;
        Assert.That(soundEffects.Volume, Is.EqualTo(0.0).Within(0.001));

        soundEffects.IsEnabled = true;
        Assert.That(soundEffects.IsEnabled, Is.True);
    }

    [Test]
    public void AudioService_MusicPlayerService_PlaylistQueueAndNavigation()
    {
        var tempDir = Path.Combine(TestContext.CurrentContext.WorkDirectory, "test-audio-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var t1 = Path.Combine(tempDir, "track1.mp3");
            var t2 = Path.Combine(tempDir, "track2.mp3");
            var t3 = Path.Combine(tempDir, "track3.mp3");
            File.WriteAllBytes(t1, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(t2, new byte[] { 4, 5, 6 });
            File.WriteAllBytes(t3, new byte[] { 7, 8, 9 });

            var store = new FakeSettingsStore();
            using var musicPlayer = new MusicPlayerService(store);

            var tracks = new List<string> { t1, t2, t3 };
            var titles = new List<string> { "Launch", "Orbit", "Reentry" };

            musicPlayer.PlayPlaylist(tracks, titles, startIndex: 1, playlistName: "Space Flight");

            Assert.Multiple(() =>
            {
                Assert.That(musicPlayer.CurrentPlaylistName, Is.EqualTo("Space Flight"));
                Assert.That(musicPlayer.CurrentTrackName, Is.EqualTo("Orbit"));
                Assert.That(musicPlayer.CurrentPlaylistTracks, Has.Count.EqualTo(3));
                Assert.That(musicPlayer.CurrentTrackIndex, Is.EqualTo(1));
            });

            // Next Track
            musicPlayer.Next();
            Assert.That(musicPlayer.CurrentTrackIndex, Is.EqualTo(2));
            Assert.That(musicPlayer.CurrentTrackName, Is.EqualTo("Reentry"));

            // Loop to start
            musicPlayer.Next();
            Assert.That(musicPlayer.CurrentTrackIndex, Is.EqualTo(0));
            Assert.That(musicPlayer.CurrentTrackName, Is.EqualTo("Launch"));

            // Previous track (loops back to end)
            musicPlayer.Previous();
            Assert.That(musicPlayer.CurrentTrackIndex, Is.EqualTo(2));
            Assert.That(musicPlayer.CurrentTrackName, Is.EqualTo("Reentry"));

            // Volume clamping
            musicPlayer.Volume = 1.2;
            Assert.That(musicPlayer.Volume, Is.EqualTo(1.0).Within(0.001));

            musicPlayer.Volume = -0.5;
            Assert.That(musicPlayer.Volume, Is.EqualTo(0.0).Within(0.001));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }
}
