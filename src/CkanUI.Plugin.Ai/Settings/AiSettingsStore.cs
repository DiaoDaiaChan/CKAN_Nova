using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Security;

namespace CkanUI.Plugin.Ai.Settings;

/// <summary>
/// AI 设置的读写（<c>%LocalAppData%/CKAN/ai-assistant.json</c>）。
/// </summary>
/// <remarks>
/// <para>写盘口径与宿主其它设置文件一致：**先写同目录临时文件，再 <c>File.Move(overwrite: true)</c> 顶替**。
/// 同卷改名是原子的，进程被杀不会留下半截 JSON（半截 JSON 会让用户丢掉整份设置）。</para>
/// <para>读盘口径：任何异常（坏 JSON、权限、目标被占用）都回落默认值，绝不把异常抛给界面。
/// 「设置文件坏了」是用户可恢复的状态，不是崩溃理由。</para>
/// <para>Key 纪律：明文只在内存里（<see cref="SaveWithApiKey"/> 的入参与 <see cref="LoadApiKey"/> 的返回值）。
/// 落盘的是 <see cref="ISecretProtector.Protect"/> 的输出；加密不可用时不落盘，宁可让用户下次重填，
/// 也不把明文 Key 写进用户家目录。</para>
/// </remarks>
public sealed class AiSettingsStore
{
    /// <summary>设置文件名。</summary>
    public const string FileName = "ai-assistant.json";

    /// <summary>替换目标文件时的重试次数（Windows 上杀毒/另一个读者会短暂占用）。</summary>
    private const int MoveAttempts = 5;

    /// <summary>当任意实例保存新设置时触发，便于主窗口与独立窗口双向同步设置。</summary>
    public static event Action<AiSettings>? SettingsChanged;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // 中文提示词/标题不转成 \uXXXX：文件是给人看的，也是给用户手改的。
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly ISecretProtector? _protector;

    /// <summary>创建读写器。</summary>
    /// <param name="filePath">设置文件路径；<c>null</c> 时用 <see cref="DefaultPath"/>。</param>
    /// <param name="protector">密钥保护器；<c>null</c> 时无法加解密（只读默认值，保存 Key 会失败）。</param>
    public AiSettingsStore(string? filePath = null, ISecretProtector? protector = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath) ? DefaultPath : filePath;
        _protector = protector;
    }

    /// <summary>默认设置文件路径：<c>%LocalAppData%/CKAN/ai-assistant.json</c>。</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CKAN",
        FileName);

    /// <summary>本实例实际读写的文件路径。</summary>
    public string FilePath { get; }

    /// <summary>读设置；文件不存在/读坏/字段越界都回落默认值。</summary>
    /// <returns>规范化后的设置，永不为 <c>null</c>。</returns>
    public AiSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                return new AiSettings();
            }

            var bytes = File.ReadAllBytes(FilePath);
            var settings = JsonSerializer.Deserialize<AiSettings>(StripUtf8Bom(bytes), JsonOptions);
            return (settings ?? new AiSettings()).Normalized();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            return new AiSettings();
        }
    }

    /// <summary>读设置并解出明文 Key（只在内存里用，不要写进日志）。</summary>
    /// <returns>明文 Key；没配或解不开时返回 <c>null</c>。</returns>
    public string? LoadApiKey()
    {
        if (_protector is null)
        {
            return null;
        }

        var protectedText = Load().ApiKeyProtected;
        return string.IsNullOrEmpty(protectedText) ? null : _protector.Unprotect(protectedText);
    }

    /// <summary>整体保存（<see cref="AiSettings.ApiKeyProtected"/> 原样保留）。</summary>
    /// <param name="settings">要保存的设置。</param>
    /// <returns>成功/失败（失败带用户可读原因，不抛异常）。</returns>
    public OperationResult Save(AiSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return Write(settings.Normalized());
    }

    /// <summary>保存设置并替换 API Key（唯一会加密的入口）。</summary>
    /// <param name="settings">要保存的设置。</param>
    /// <param name="plainApiKey">明文 Key：<c>null</c> 表示保持不变；空串表示清除。</param>
    /// <returns>成功/失败；加密不可用时**不落盘**并返回失败原因。</returns>
    public OperationResult SaveWithApiKey(AiSettings settings, string? plainApiKey)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (plainApiKey is null)
        {
            return Write(settings.Normalized());
        }

        var trimmed = plainApiKey.Trim();
        if (trimmed.Length == 0)
        {
            return Write(settings.Normalized() with { ApiKeyProtected = null });
        }

        var protectedText = _protector?.Protect(trimmed);
        if (string.IsNullOrEmpty(protectedText))
        {
            return OperationResult.Fail("当前平台无法加密保存 API Key，设置未写入（不会把明文 Key 落盘）。");
        }

        return Write(settings.Normalized() with { ApiKeyProtected = protectedText });
    }

    private OperationResult Write(AiSettings settings)
    {
        var directory = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                return OperationResult.Fail("无法创建设置目录：" + ex.Message);
            }
        }

        var temp = FilePath + ".tmp-" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            // SerializeToUtf8Bytes 不写 BOM：JsonSerializer 读回时把 0xEF 当非法起始字符会让整份设置回落默认值。
            File.WriteAllBytes(temp, JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions));
            MoveWithRetry(temp, FilePath);
            try
            {
                SettingsChanged?.Invoke(settings);
            }
            catch
            {
            }
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or JsonException)
        {
            TryDelete(temp);
            return OperationResult.Fail("保存 AI 设置失败：" + ex.Message);
        }
    }

    /// <summary>去掉 UTF-8 BOM：JsonSerializer 按字节解析时会把 0xEF 当成非法起始字符。</summary>
    private static ReadOnlySpan<byte> StripUtf8Bom(byte[] bytes)
        => bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF
            ? bytes.AsSpan(3)
            : bytes;

    // Windows 上目标文件正被读取（杀毒、另一个读者）时替换会短暂失败，退避重试几次。
    private static void MoveWithRetry(string source, string destination)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(source, destination, overwrite: true);
                return;
            }
            catch (Exception ex) when (attempt < MoveAttempts && ex is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(20 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 临时文件清不掉不影响失败语义：下次写入会用新的临时名。
        }
    }
}
