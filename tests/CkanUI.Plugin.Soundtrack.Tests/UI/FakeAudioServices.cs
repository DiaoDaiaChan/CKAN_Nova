using System;
using System.Collections.Generic;
using CkanUI.Services.Abstractions.Audio;

namespace CkanUI.Plugin.Soundtrack.Tests.UI;

public sealed class FakeSoundEffectService : ISoundEffectService
{
    public bool IsEnabled { get; set; } = true;
    public double Volume { get; set; } = 1.0;
    public bool IsMuted { get; set; } = false;

    public List<SoundEffectType> PlayedEffects { get; } = new();
    public List<byte[]> PlayedCustomBytes { get; } = new();

    public void Play(SoundEffectType type)
    {
        if (IsEnabled && !IsMuted && Volume > 0.001)
        {
            PlayedEffects.Add(type);
        }
    }

    public void PlayCustom(byte[] wavBytes)
    {
        if (IsEnabled && !IsMuted && Volume > 0.001)
        {
            PlayedCustomBytes.Add(wavBytes);
        }
    }
}

public sealed class FakeMusicPlayerService : IMusicPlayerService
{
    public bool IsPlaying { get; set; }
    public bool IsPaused { get; set; }
    public bool IsMuted { get; set; }
    public double Volume { get; set; } = 0.8;
    public string? CurrentFilePath { get; set; }
    public string? CurrentTrackName { get; set; }
    public string? CurrentPlaylistName { get; set; }
    public TimeSpan Position { get; set; } = TimeSpan.Zero;
    public TimeSpan Duration { get; set; } = TimeSpan.FromMinutes(3);
    public IReadOnlyList<string> CurrentPlaylistTracks { get; set; } = Array.Empty<string>();
    public int CurrentTrackIndex { get; set; } = -1;

    public event EventHandler? PlaybackStateChanged;
    public event EventHandler? TrackChanged;
    public event EventHandler? PositionChanged;

    public List<string> PlayedFiles { get; } = new();
    public int PlayCount { get; private set; }
    public int PauseCount { get; private set; }
    public int ResumeCount { get; private set; }
    public int StopCount { get; private set; }
    public int NextCount { get; private set; }
    public int PreviousCount { get; private set; }
    public TimeSpan? LastSeek { get; private set; }

    public void Play(string filePath, string? trackName = null, string? playlistName = null)
    {
        CurrentFilePath = filePath;
        CurrentTrackName = trackName ?? filePath;
        CurrentPlaylistName = playlistName;
        IsPlaying = true;
        IsPaused = false;
        PlayCount++;
        PlayedFiles.Add(filePath);
        TrackChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void PlayPlaylist(IEnumerable<string> filePaths, IEnumerable<string>? trackNames = null, int startIndex = 0, string? playlistName = null)
    {
        var list = new List<string>(filePaths);
        CurrentPlaylistTracks = list;
        CurrentPlaylistName = playlistName;
        CurrentTrackIndex = startIndex;
        if (startIndex >= 0 && startIndex < list.Count)
        {
            Play(list[startIndex], playlistName: playlistName);
        }
    }

    public void Pause()
    {
        IsPlaying = false;
        IsPaused = true;
        PauseCount++;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Resume()
    {
        IsPlaying = true;
        IsPaused = false;
        ResumeCount++;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Stop()
    {
        IsPlaying = false;
        IsPaused = false;
        StopCount++;
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Next()
    {
        NextCount++;
    }

    public void Previous()
    {
        PreviousCount++;
    }

    public void Seek(TimeSpan position)
    {
        Position = position;
        LastSeek = position;
        PositionChanged?.Invoke(this, EventArgs.Empty);
        PlaybackStateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
    }
}
