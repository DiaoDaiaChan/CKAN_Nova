using System.Collections.Concurrent;
using System.Text;
using CkanUI.Services.Abstractions.Audio;

namespace CkanUI.Services.Audio;

/// <summary>
/// 程序化交互音效合成器。
/// 纯数学合成标准 44.1kHz 16-Bit Mono RIFF PCM WAV 音频流，无需外置资源文件，
/// 零加载延迟，具备极高保真度与音质表现。
/// </summary>
public static class SoundEffectSynthesizer
{
    private const int SampleRate = 44100;
    private static readonly ConcurrentDictionary<SoundEffectType, byte[]> Cache = new();

    /// <summary>
    /// 获取对应音效类型的预合成 WAV 字节数组（含完整 RIFF 头）。
    /// </summary>
    public static byte[] GetWavBytes(SoundEffectType type)
    {
        return Cache.GetOrAdd(type, Synthesize);
    }

    private static byte[] Synthesize(SoundEffectType type)
    {
        return type switch
        {
            SoundEffectType.Select => GenerateSelect(),
            SoundEffectType.Check => GenerateCheck(),
            SoundEffectType.Uncheck => GenerateUncheck(),
            SoundEffectType.Error => GenerateError(),
            SoundEffectType.Warning => GenerateWarning(),
            SoundEffectType.Ok => GenerateOk(),
            SoundEffectType.Click => GenerateClick(),
            SoundEffectType.Hover => GenerateHover(),
            _ => GenerateClick()
        };
    }

    /// <summary>
    /// 选中/切换页签：22ms 柔和温润微触感音（580Hz 微正弦，快速指数衰减，低振幅不喧宾夺主）。
    /// </summary>
    private static byte[] GenerateSelect()
    {
        const double duration = 0.022;
        var samples = new short[(int)(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double progress = t / duration;
            // 温润固定频点，无滑频卡通感
            const double freq = 580.0;
            double envelope = Math.Sin(Math.PI * Math.Pow(progress, 0.35)) * Math.Exp(-progress * 8.5);
            double wave = Math.Sin(2.0 * Math.PI * freq * t);
            // 振幅严格控制在 6500（约为原先三分之一），轻巧不刺耳
            samples[i] = (short)(wave * envelope * 6500);
        }
        return BuildWav(samples);
    }

    /// <summary>
    /// 勾选/激活：48ms 上扬清脆微双音阶（600Hz 升至 880Hz，温和干脆）。
    /// </summary>
    private static byte[] GenerateCheck()
    {
        const double duration = 0.048;
        var samples = new short[(int)(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double progress = t / duration;
            double freq = 600.0 + (280.0 * Math.Pow(progress, 0.7));
            double envelope = Math.Sin(Math.PI * progress) * Math.Exp(-progress * 3.0);
            double fundamental = Math.Sin(2.0 * Math.PI * freq * t);
            double harmonic = 0.15 * Math.Sin(4.0 * Math.PI * freq * t);
            samples[i] = (short)((fundamental + harmonic) * envelope * 10500);
        }
        return BuildWav(samples);
    }

    /// <summary>
    /// 取消勾选/关闭：42ms 下行微音（700Hz 降至 500Hz，柔和阻尼）。
    /// </summary>
    private static byte[] GenerateUncheck()
    {
        const double duration = 0.042;
        var samples = new short[(int)(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double progress = t / duration;
            double freq = 700.0 - (200.0 * progress);
            double envelope = Math.Sin(Math.PI * progress) * Math.Exp(-progress * 3.5);
            double wave = Math.Sin(2.0 * Math.PI * freq * t);
            samples[i] = (short)(wave * envelope * 8500);
        }
        return BuildWav(samples);
    }

    /// <summary>
    /// 错误/失败：160ms 醒目温润的双脉冲低音（220Hz 混频 165Hz，不刺耳）。
    /// </summary>
    private static byte[] GenerateError()
    {
        const double duration = 0.160;
        var samples = new short[(int)(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double progress = t / duration;
            double pulse = (progress < 0.45)
                ? Math.Sin(Math.PI * (progress / 0.45))
                : (progress > 0.55 ? Math.Sin(Math.PI * ((progress - 0.55) / 0.45)) : 0.0);

            double tone1 = Math.Sin(2.0 * Math.PI * 220.0 * t);
            double tone2 = Math.Sin(2.0 * Math.PI * 164.81 * t);
            double overtone = 0.2 * Math.Sin(2.0 * Math.PI * 440.0 * t);
            double mixed = (tone1 * 0.5) + (tone2 * 0.4) + overtone;
            samples[i] = (short)(mixed * pulse * 15000);
        }
        return BuildWav(samples);
    }

    /// <summary>
    /// 警告：130ms 双音大三度钟音（440Hz A4 + 554.37Hz C#5）。
    /// </summary>
    private static byte[] GenerateWarning()
    {
        const double duration = 0.130;
        var samples = new short[(int)(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double progress = t / duration;
            double envelope = Math.Exp(-progress * 5.0);
            double note1 = Math.Sin(2.0 * Math.PI * 440.0 * t);
            double note2 = Math.Sin(2.0 * Math.PI * 554.37 * t);
            double mixed = (note1 + note2) * 0.5;
            samples[i] = (short)(mixed * envelope * 14000);
        }
        return BuildWav(samples);
    }

    /// <summary>
    /// OK / 成功：210ms 明快的大三和弦上扬琶音（C5 523.25Hz -> E5 659.25Hz -> G5 783.99Hz）。
    /// </summary>
    private static byte[] GenerateOk()
    {
        const double duration = 0.210;
        var samples = new short[(int)(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;

            // Note 1: C5 (0.00 ~ 0.12)
            double n1 = 0;
            if (t is >= 0.0 and <= 0.12)
            {
                double p = t / 0.12;
                n1 = Math.Sin(2.0 * Math.PI * 523.25 * t) * Math.Sin(Math.PI * p) * Math.Exp(-p * 2.2);
            }

            // Note 2: E5 (0.04 ~ 0.16)
            double n2 = 0;
            if (t is >= 0.04 and <= 0.16)
            {
                double p = (t - 0.04) / 0.12;
                n2 = Math.Sin(2.0 * Math.PI * 659.25 * t) * Math.Sin(Math.PI * p) * Math.Exp(-p * 2.2);
            }

            // Note 3: G5 (0.08 ~ 0.21)
            double n3 = 0;
            if (t is >= 0.08 and <= 0.21)
            {
                double p = (t - 0.08) / 0.13;
                n3 = Math.Sin(2.0 * Math.PI * 783.99 * t) * Math.Sin(Math.PI * p) * Math.Exp(-p * 1.8);
            }

            double sum = (n1 * 0.35) + (n2 * 0.40) + (n3 * 0.45);
            samples[i] = (short)(sum * 16500);
        }
        return BuildWav(samples);
    }

    /// <summary>
    /// 普通点击：20ms 轻量级触觉物理微咔哒（750Hz 指数阻尼，纯净不喧闹）。
    /// </summary>
    private static byte[] GenerateClick()
    {
        const double duration = 0.020;
        var samples = new short[(int)(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double progress = t / duration;
            double envelope = Math.Exp(-progress * 26.0);
            double wave = Math.Sin(2.0 * Math.PI * 750.0 * t);
            samples[i] = (short)(wave * envelope * 8000);
        }
        return BuildWav(samples);
    }

    /// <summary>
    /// 划过/悬停：16ms 极轻微清脆的晶体微滴音（1400Hz 衰减至 1150Hz），避免划过密集控件时产生听觉疲劳。
    /// </summary>
    private static byte[] GenerateHover()
    {
        const double duration = 0.016;
        var samples = new short[(int)(SampleRate * duration)];
        for (int i = 0; i < samples.Length; i++)
        {
            double t = (double)i / SampleRate;
            double progress = t / duration;
            double freq = 1400.0 - (250.0 * progress);
            double envelope = Math.Exp(-progress * 30.0);
            double wave = Math.Sin(2.0 * Math.PI * freq * t);
            samples[i] = (short)(wave * envelope * 9000);
        }
        return BuildWav(samples);
    }

    /// <summary>
    /// 封装为标准 RIFF 44-Byte Header 的 16-Bit PCM WAV。
    /// </summary>
    public static byte[] BuildWav(short[] samples)
    {
        int dataSize = samples.Length * sizeof(short);
        int totalSize = 44 + dataSize;
        var bytes = new byte[totalSize];
        using var ms = new MemoryStream(bytes);
        using var bw = new BinaryWriter(ms);

        bw.Write(Encoding.ASCII.GetBytes("RIFF"));
        bw.Write(36 + dataSize);
        bw.Write(Encoding.ASCII.GetBytes("WAVE"));
        bw.Write(Encoding.ASCII.GetBytes("fmt "));
        bw.Write(16); // Subchunk1Size
        bw.Write((short)1); // AudioFormat: PCM
        bw.Write((short)1); // NumChannels: Mono
        bw.Write(SampleRate);
        bw.Write(SampleRate * sizeof(short)); // ByteRate
        bw.Write((short)sizeof(short)); // BlockAlign
        bw.Write((short)16); // BitsPerSample
        bw.Write(Encoding.ASCII.GetBytes("data"));
        bw.Write(dataSize);

        for (int i = 0; i < samples.Length; i++)
        {
            bw.Write(samples[i]);
        }

        return bytes;
    }
}
