using System.Text.RegularExpressions;

using CkanUI.Services.Abstractions;
using CkanUI.Services.Platform;

using log4net;

namespace CkanUI.Services.Onboarding;

/// <summary>
/// 首次启动指引的安装目录扫描（02 篇 §1 Ⅱ-①）。
/// </summary>
/// <remarks>
/// <para>
/// 顺序是刻意的：先问 Steam（<see cref="ISteamLibraryLocator"/> 已经处理了 libraryfolders.vdf 的三种写法），
/// 再问注册表（Steam App 220200 / GOG 的安装记录），最后才去猜常见路径。
/// 常见路径只查「盘根 / Games / SteamLibrary」这类低成本位置，不做全盘遍历——
/// 一次全盘扫在机械盘上能把首启拖到分钟级，用户体感就是「卡住了」。
/// </para>
/// <para>
/// 所有文件系统/注册表访问都可注入：单测不需要真的存在 KSP 目录，也不需要 Windows。
/// </para>
/// </remarks>
public sealed class KspInstallScanner : IKspInstallScanner
{
    /// <summary>KSP 在 Steam 库里的目录名。</summary>
    public const string GameFolderName = "Kerbal Space Program";

    /// <summary>Steam App ID（KSP 的 appid）。</summary>
    public const string SteamAppId = "220200";

    private const string SteamAppKey =
        @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Steam App " + SteamAppId;

    private const string GogKey = @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\GOG.com\Games\1429867978";

    private static readonly Regex VersionPattern = new(@"\b(\d+\.\d+(?:\.\d+){0,2})\b",
                                                        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly ILog Log = LogManager.GetLogger(typeof(KspInstallScanner));

    private readonly ISteamLibraryLocator _steam;
    private readonly Func<string, bool> _directoryExists;
    private readonly Func<string, bool> _fileExists;
    private readonly Func<string, string?> _readTextFile;
    private readonly Func<string, string?> _readRegistryValue;
    private readonly Func<IEnumerable<string>> _driveRoots;

    /// <summary>构造扫描器。</summary>
    /// <param name="steam">Steam 库定位器。</param>
    /// <param name="directoryExists">目录存在判定（测试注入）。</param>
    /// <param name="fileExists">文件存在判定（测试注入）。</param>
    /// <param name="readTextFile">文本读取（测试注入）；读不到返回 <c>null</c>。</param>
    /// <param name="readRegistryValue">注册表读取（测试注入），签名同 <c>Registry.GetValue</c>。</param>
    /// <param name="driveRoots">盘符根列表（测试注入）；为空时枚举本机固定盘。</param>
    public KspInstallScanner(
        ISteamLibraryLocator steam,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null,
        Func<string, string?>? readTextFile = null,
        Func<string, string?>? readRegistryValue = null,
        Func<IEnumerable<string>>? driveRoots = null)
    {
        _steam = steam ?? throw new ArgumentNullException(nameof(steam));
        _directoryExists = directoryExists ?? Directory.Exists;
        _fileExists = fileExists ?? File.Exists;
        _readTextFile = readTextFile ?? ReadTextOrNull;
        _readRegistryValue = readRegistryValue ?? ReadRegistryOrNull;
        _driveRoots = driveRoots ?? EnumerateDriveRoots;
    }

    /// <inheritdoc />
    public async Task<OperationResult<IReadOnlyList<KspCandidate>>> ScanAsync(CancellationToken ct = default)
    {
        var found = new List<KspCandidate>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Remember(string? gameDir, KspCandidateSource source)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(gameDir))
            {
                return;
            }

            var full = gameDir.Trim().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!seen.Add(full) || !IsKspDirectory(full))
            {
                return;
            }

            found.Add(new KspCandidate(full, ReadGameVersion(full), source));
        }

        try
        {
            // ① Steam：库定位器已经把 libraryfolders.vdf 的写法差异吃掉了。
            var steam = await _steam.DiscoverGameDirsAsync(ct).ConfigureAwait(false);
            if (steam.IsSuccess && steam.Value is { } steamDirs)
            {
                foreach (var dir in steamDirs)
                {
                    Remember(dir, KspCandidateSource.Steam);
                }
            }
            else if (!steam.IsSuccess)
            {
                Log.Info($"Steam 库扫描跳过：{steam.Message}");
            }

            // ② 注册表：Steam 的 InstallLocation 与 GOG 的安装路径。
            Remember(ReadRegistry(SteamAppKey, "InstallLocation"), KspCandidateSource.Registry);
            Remember(ReadRegistry(GogKey, "path"), KspCandidateSource.Registry);

            // ③ 常见路径：只查低成本位置，不做全盘遍历。
            foreach (var root in BuildCommonPaths())
            {
                Remember(root, KspCandidateSource.CommonPath);
            }

            return OperationResult<IReadOnlyList<KspCandidate>>.Ok(found);
        }
        catch (OperationCanceledException)
        {
            return OperationResult<IReadOnlyList<KspCandidate>>.Fail("扫描 KSP 安装目录已取消。", retryable: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            Log.Warn("扫描 KSP 安装目录失败。", ex);
            return OperationResult<IReadOnlyList<KspCandidate>>.Fail("扫描 KSP 安装目录失败。", ex.Message, retryable: true);
        }
    }

    /// <inheritdoc />
    public bool IsKspDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            // 判据取「GameData + 可执行文件」：只看目录名会把空的同名目录也算进来，
            // 只看 GameData 又会把 CKAN 的测试目录误认成游戏。
            return _directoryExists(path)
                   && _directoryExists(Path.Combine(path, "GameData"))
                   && (_fileExists(Path.Combine(path, "KSP_x64.exe")) || _fileExists(Path.Combine(path, "KSP.exe")));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public string? ReadGameVersion(string gameDir)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return null;
        }

        // readme.txt 首行是「Kerbal Space Program 1.12.5」，buildID.txt 里有 buildId/version 行；
        // 两个都读不到就返回 null —— 界面按「版本待确认」处理，比编一个版本号去解依赖安全得多。
        foreach (var name in new[] { "readme.txt", "buildID.txt", "KSP_x64_Data\app.info" })
        {
            var text = _readTextFile(Path.Combine(gameDir, name));
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            var match = VersionPattern.Match(text);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    private IEnumerable<string> BuildCommonPaths()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var roots = new List<string>();
        var seenRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddRoot(string? root)
        {
            if (!string.IsNullOrWhiteSpace(root) && seenRoots.Add(root.Trim()))
            {
                roots.Add(root.Trim());
            }
        }

        AddRoot(programFiles);
        AddRoot(programFilesX86);

        try
        {
            foreach (var drive in _driveRoots())
            {
                AddRoot(drive);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Info("枚举盘符失败，仅使用 Program Files。");
        }

        foreach (var root in roots)
        {
            yield return Path.Combine(root, GameFolderName);
            yield return Path.Combine(root, "Games", GameFolderName);
            yield return Path.Combine(root, "SteamLibrary", "steamapps", "common", GameFolderName);
        }
    }

    private string? ReadRegistry(string key, string valueName)
    {
        try
        {
            var raw = _readRegistryValue($"{key}\\{valueName}");
            return string.IsNullOrWhiteSpace(raw) ? null : raw.Trim().Replace('/', '\\');
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException
                                       or PlatformNotSupportedException or IOException)
        {
            return null;
        }
    }

    private static string? ReadTextOrNull(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            // 版本号永远在头几行，读多了只是浪费首启时间（buildID.txt 里还有几 KB 的 build 信息）。
            var buffer = new char[512];
            var read = reader.Read(buffer, 0, buffer.Length);
            return read <= 0 ? null : new string(buffer, 0, read);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? ReadRegistryOrNull(string keyWithValue)
    {
        // 注入点把「键 + 值名」压成一个字符串，实现侧再拆开：这样默认实现只依赖 BCL。
        var split = keyWithValue.LastIndexOf('\\');
        if (split <= 0)
        {
            return null;
        }

        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        var key = keyWithValue[..split];
        var valueName = keyWithValue[(split + 1)..];
        return RegistryAccessor.GetString(key, valueName);
    }

    private static IEnumerable<string> EnumerateDriveRoots()
    {
        var roots = new List<string>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            try
            {
                if (drive.IsReady && drive.DriveType is DriveType.Fixed or DriveType.Removable)
                {
                    roots.Add(drive.RootDirectory.FullName);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 光驱/断开的网络盘在枚举瞬间就可能消失，跳过即可。
            }
        }

        return roots;
    }
}
