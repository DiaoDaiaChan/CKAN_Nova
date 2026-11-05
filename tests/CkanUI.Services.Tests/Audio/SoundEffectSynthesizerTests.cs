using System;
using System.Text;
using CkanUI.Services.Abstractions.Audio;
using CkanUI.Services.Audio;
using NUnit.Framework;

namespace CkanUI.Services.Tests.Audio;

[TestFixture]
public sealed class SoundEffectSynthesizerTests
{
    [Test]
    public void GetWavBytes_AllSoundEffectTypes_ProduceValidWavBytes(
        [Values(
            SoundEffectType.Select,
            SoundEffectType.Check,
            SoundEffectType.Uncheck,
            SoundEffectType.Error,
            SoundEffectType.Warning,
            SoundEffectType.Ok,
            SoundEffectType.Click,
            SoundEffectType.Hover)] SoundEffectType type)
    {
        var wav = SoundEffectSynthesizer.GetWavBytes(type);

        Assert.That(wav, Is.Not.Null);
        Assert.That(wav.Length, Is.GreaterThan(44), "WAV 必须包含 44 字节头和音频数据");

        // RIFF header
        string riff = Encoding.ASCII.GetString(wav, 0, 4);
        Assert.That(riff, Is.EqualTo("RIFF"));

        // WAVE header
        string wave = Encoding.ASCII.GetString(wav, 8, 4);
        Assert.That(wave, Is.EqualTo("WAVE"));

        // fmt chunk
        string fmt = Encoding.ASCII.GetString(wav, 12, 4);
        Assert.That(fmt, Is.EqualTo("fmt "));

        // Audio format: 1 (PCM)
        short audioFormat = BitConverter.ToInt16(wav, 20);
        Assert.That(audioFormat, Is.EqualTo(1));

        // Channels: 1 (Mono)
        short channels = BitConverter.ToInt16(wav, 22);
        Assert.That(channels, Is.EqualTo(1));

        // Sample rate: 44100
        int sampleRate = BitConverter.ToInt32(wav, 24);
        Assert.That(sampleRate, Is.EqualTo(44100));

        // Bits per sample: 16
        short bitsPerSample = BitConverter.ToInt16(wav, 34);
        Assert.That(bitsPerSample, Is.EqualTo(16));

        // data chunk
        string data = Encoding.ASCII.GetString(wav, 36, 4);
        Assert.That(data, Is.EqualTo("data"));

        int dataSize = BitConverter.ToInt32(wav, 40);
        Assert.That(dataSize, Is.EqualTo(wav.Length - 44));
    }

    [Test]
    public void GetWavBytes_RepeatedCalls_ReturnsCachedInstance()
    {
        var wav1 = SoundEffectSynthesizer.GetWavBytes(SoundEffectType.Ok);
        var wav2 = SoundEffectSynthesizer.GetWavBytes(SoundEffectType.Ok);

        Assert.That(ReferenceEquals(wav1, wav2), Is.True, "相同音效类型应当命中内存缓存");
    }

    [Test]
    public void GetWavBytes_DifferentTypes_HaveDifferentAudioCharacteristics()
    {
        var select = SoundEffectSynthesizer.GetWavBytes(SoundEffectType.Select);
        var error = SoundEffectSynthesizer.GetWavBytes(SoundEffectType.Error);

        Assert.That(select.Length, Is.Not.EqualTo(error.Length));
    }
}
