using System;
using System.Collections.Generic;
using CkanUI.Services.Abstractions.Dtos;

namespace CkanUI.Tools.PerfProbe;

/// <summary>
/// 快速生成 3,000 个真实且多样化的模组测试数据集。
/// 用于 PERF-01 帧率门禁与性能探针基准测试。
/// </summary>
public static class MockModDataGenerator
{
    private static readonly string[] SamplePrefixes =
    [
        "Kerbal", "Mech", "Scatterer", "FAR", "EVE", "NearFuture", "FarFuture",
        "Modular", "Tweak", "KSP", "Community", "Realism", "Planetary", "Astronomer",
        "B9", "Interstellar", "Shuttle", "Station", "SCAN", "Toolbar", "Trajectories",
        "TransferWindow", "Waypoint", "Contract", "Universal", "Docking", "Parachute"
    ];

    private static readonly string[] SampleSuffixes =
    [
        "Engineer", "Jeb", "Atmosphere", "Aerodynamics", "Enhancement", "Solar",
        "Propulsion", "FlightIntegrator", "Scale", "Core", "ResourcePack", "Overhaul",
        "Base", "VisualPack", "Aerospace", "Extended", "Orbiter", "Parts", "Satellites",
        "Controller", "Planner", "Manager", "Config", "System", "Dynamics", "HUD"
    ];

    private static readonly string[] SampleAuthors =
    [
        "sarbian", "jplrepo", "blackrack", "RoverDude", "Nertea", "linuxgurugamer",
        "TriggerAu", "shadowzone", "ferram4", "blowfish", "magico13", "DMagic", "mumeiman"
    ];

    private static readonly string[] SampleTags =
    [
        "Parts", "Plugin", "Graphics", "Science", "Career", "Physics",
        "TechTree", "Sound", "Crew", "Control", "Convenience", "Information"
    ];

    private static readonly string[] SampleLicenses =
    [
        "MIT", "GPL-3.0", "CC-BY-NC-SA-4.0", "Restricted", "BSD-2-Clause", "Apache-2.0"
    ];

    /// <summary>
    /// 生成指定数量（默认 3,000）的模组元数据。
    /// </summary>
    public static (IReadOnlyList<ModRowData> Mods, IReadOnlyDictionary<string, InstalledModuleInfo> Installed) Generate(int count = 3000)
    {
        var mods = new List<ModRowData>(count);
        var installed = new Dictionary<string, InstalledModuleInfo>(StringComparer.OrdinalIgnoreCase);

        var random = new Random(42); // 固定种子确保每次基准测试数据完全一致且可复现

        for (var i = 1; i <= count; i++)
        {
            var prefix = SamplePrefixes[random.Next(SamplePrefixes.Length)];
            var suffix = SampleSuffixes[random.Next(SampleSuffixes.Length)];
            var id = $"Mod_{prefix}{suffix}_{i:D4}";
            var name = $"{prefix} {suffix} #{i}";
            var author = SampleAuthors[random.Next(SampleAuthors.Length)];
            var tagCount = random.Next(1, 4);
            var tags = new List<string>(tagCount);
            for (var t = 0; t < tagCount; t++)
            {
                var tag = SampleTags[random.Next(SampleTags.Length)];
                if (!tags.Contains(tag)) tags.Add(tag);
            }

            var downloadCount = (long)random.Next(500, 2_000_000);
            var sizeBytes = (long)random.Next(50_000, 150_000_000); // 50KB - 150MB
            var major = random.Next(1, 4);
            var minor = random.Next(0, 15);
            var patch = random.Next(0, 10);
            var latestVer = $"v{major}.{minor}.{patch}";

            // 约 1/6 的模组处于已安装状态 (~500 个)
            var isInstalled = (i % 6 == 0);
            string? installedVer = null;
            var hasUpdate = false;
            var isAutoInstalled = false;

            if (isInstalled)
            {
                if (patch > 0 && i % 3 == 0)
                {
                    installedVer = $"v{major}.{minor}.{patch - 1}";
                    hasUpdate = true;
                }
                else
                {
                    installedVer = latestVer;
                    hasUpdate = false;
                }

                isAutoInstalled = (i % 12 == 0);
                installed[id] = new InstalledModuleInfo(
                    Identifier: id,
                    Version: installedVer,
                    IsAutoInstalled: isAutoInstalled,
                    SourceRepository: "default-ksp-repo",
                    InstallTime: DateTime.UtcNow.AddDays(-random.Next(1, 100)));
            }

            var facts = new ModSearchFacts
            {
                Abstract = $"A high performance kerbal modification providing {name} functionalities.",
                Description = $"Full description of {name} created by {author}. Enhances game stability and physics mechanics.",
                Licenses = [SampleLicenses[random.Next(SampleLicenses.Length)]],
                Localizations = ["en-us", "zh-cn"]
            };

            var mod = new ModRowData(
                Identifier: id,
                Name: name,
                Author: author,
                InstalledVersion: installedVer,
                LatestVersion: latestVer,
                DownloadCount: downloadCount,
                Tags: tags,
                SizeBytes: sizeBytes,
                IsInstalled: isInstalled,
                HasUpdate: hasUpdate,
                IsCompatible: true,
                IsAutoInstalled: isAutoInstalled,
                IsSelected: false,
                IsImplicit: false)
            {
                Facts = facts
            };

            mods.Add(mod);
        }

        return (mods, installed);
    }
}
