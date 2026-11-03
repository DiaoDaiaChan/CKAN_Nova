using System.Globalization;
using CkanUI.Sfs;

namespace CkanUI.Plugin.Rp1.Career;

/// <summary>把 RP-1 的五个场景节点折成一条按 UT 排序的时间线。</summary>
public static class Rp1TimelineBuilder
{
    /// <summary>构建时间线。缺失场景只记诊断，不抛异常。</summary>
    public static Rp1CareerTimeline Build(
        string gameDir,
        string saveName,
        string savePath,
        Rp1ScenarioSet scenario,
        Rp1DefinitionCatalog catalog,
        string? rp1Version,
        string? gameVersion)
    {
        var diagnostics = new List<string>(scenario.Diagnostics);
        diagnostics.AddRange(catalog.Diagnostics);

        var events = new List<Rp1CareerEvent>();
        var contracts = ReadContracts(scenario, out var contractTimes);
        AddContractEvents(contracts, contractTimes, catalog, events);

        var milestones = ReadMilestones(scenario);
        AddMilestoneEvents(milestones, contractTimes, catalog, events);

        var programs = ReadPrograms(scenario);
        AddProgramEvents(programs, catalog, events);
        AddCompletedProgramEvents(scenario, catalog, events);

        var retired = ReadRetiredCrew(scenario);
        AddRetiredEvents(retired, events);

        var periods = ReadPeriods(scenario, diagnostics);
        AddCrewLostEvents(periods, events);

        foreach (var milestone in milestones.Keys)
        {
            if (milestone.Equals("CrewKilledMilestone", StringComparison.OrdinalIgnoreCase)
                && events.Any(item => item.Kind == Rp1EventKind.Milestone
                    && item.InternalName.Equals(milestone, StringComparison.OrdinalIgnoreCase)))
            {
                var firstLoss = periods.FirstOrDefault(period => period.CrewLost > 0);
                if (firstLoss is not null)
                {
                    var index = events.FindIndex(item => item.Kind == Rp1EventKind.Milestone
                        && item.InternalName.Equals(milestone, StringComparison.OrdinalIgnoreCase)
                        && item.Ut is null);
                    if (index >= 0)
                    {
                        events[index] = events[index] with
                        {
                            Ut = firstLoss.EndUt,
                            DateText = Rp1GameDate.Format(firstLoss.EndUt),
                        };
                    }
                }
            }
        }

        events = Sort(events);
        var last = periods.Count > 0 ? periods[^1] : null;
        var currentUt = scenario.CurrentUt ?? last?.EndUt;
        var contractCount = events.Count(item => item.Kind == Rp1EventKind.Contract);
        var currentFunds = ScenarioDouble(scenario, "Funding", "funds") ?? last?.Funds;
        var currentScience = ScenarioDouble(scenario, "ResearchAndDevelopment", "sci") ?? last?.Science;
        var currentConfidence = ScenarioDouble(scenario, "Confidence", "confidence") ?? last?.Confidence;
        var currentReputation = ScenarioDouble(scenario, "Reputation", "rep") ?? last?.Reputation;
        var milestoneCount = milestones.Count;
        var programCount = events.Count(item => item.Kind == Rp1EventKind.ProgramStart);
        var retiredCount = events.Count(item => item.Kind == Rp1EventKind.CrewRetired);
        var lostCount = periods.Sum(period => period.CrewLost);

        return new Rp1CareerTimeline(
            saveName,
            savePath,
            gameDir,
            rp1Version,
            gameVersion ?? scenario.GameVersion,
            currentUt,
            Rp1GameDate.Format(currentUt),
            Directory.Exists(Path.Combine(gameDir, "GameData", "RP-1")),
            events,
            periods,
            contractCount,
            milestoneCount,
            programCount,
            retiredCount,
            lostCount,
            currentFunds,
            currentScience,
            currentConfidence,
            currentReputation,
            diagnostics);
    }

    private static Dictionary<string, double> ReadContracts(Rp1ScenarioSet scenario, out Dictionary<string, double> contractTimes)
    {
        var titles = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        contractTimes = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        if (scenario.Scenarios.TryGetValue("RP1ContractTracker", out var tracker))
        {
            foreach (var entry in tracker.Children.OfType<SfsNode>())
            {
                foreach (var contract in entry.Children.OfType<SfsNode>()
                             .Where(node => node.Name.Equals("Contract", StringComparison.OrdinalIgnoreCase)))
                {
                    AddContractTime(contract, "Name", "CompletionTime", titles);
                }
            }
        }

        if (scenario.Scenarios.TryGetValue("CareerLog", out var log)
            && Child(log, "_contractDict") is { } dict)
        {
            foreach (var contract in dict.Children.OfType<SfsNode>()
                         .Where(node => node.Name.Equals("ContractEvent", StringComparison.OrdinalIgnoreCase)))
            {
                if (!string.Equals(Pair(contract, "Type"), "Complete", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                AddContractTime(contract, "InternalName", "UT", titles);
            }
        }

        foreach (var pair in titles)
        {
            contractTimes[pair.Key] = pair.Value;
        }

        return titles;
    }

    private static void AddContractTime(SfsNode node, string nameKey, string timeKey, Dictionary<string, double> titles)
    {
        var name = Pair(node, nameKey);
        var time = PairDouble(node, timeKey);
        if (string.IsNullOrWhiteSpace(name) || time is null || time.Value <= 0)
        {
            return;
        }

        if (name.StartsWith("exclude_", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 同一合同可能因为读档/回滚出现多条完成记录：取最早那次，时间线不重复。
        if (!titles.TryGetValue(name, out var existing) || time.Value < existing)
        {
            titles[name] = time.Value;
        }
    }

    private static void AddContractEvents(
        IReadOnlyDictionary<string, double> contracts,
        IReadOnlyDictionary<string, double> contractTimes,
        Rp1DefinitionCatalog catalog,
        List<Rp1CareerEvent> events)
    {
        foreach (var pair in contracts)
        {
            var definition = catalog.Contracts.TryGetValue(pair.Key, out var found) ? found : null;
            events.Add(Event(
                pair.Value,
                Rp1EventKind.Contract,
                definition?.Title ?? pair.Key,
                definition?.Group,
                definition?.Synopsis ?? definition?.Description,
                pair.Key,
                "合同"));
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> ReadMilestones(Rp1ScenarioSet scenario)
    {
        var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (!scenario.Scenarios.TryGetValue("MilestoneHandler", out var handler))
        {
            return result;
        }

        var seen = Child(handler, "seenMilestones");
        if (seen is not null)
        {
            foreach (var value in seen.Pairs().Where(pair => pair.Key.Equals("item", StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value))
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    result[value.Trim()] = [];
                }
            }
        }

        var data = Child(handler, "milestoneData");
        if (data is null)
        {
            return result;
        }

        var keys = Child(data, "Keys")?.Pairs()
            .Where(pair => pair.Key.Equals("key", StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Value.Trim())
            .Where(value => value.Length > 0)
            .ToList() ?? [];
        var values = Child(data, "Values")?.Children.OfType<SfsNode>()
            .Where(node => node.Name.StartsWith("PersistentListValueType", StringComparison.OrdinalIgnoreCase))
            .ToList() ?? [];

        for (var index = 0; index < keys.Count && index < values.Count; index++)
        {
            var items = values[index].Pairs()
                .Where(pair => pair.Key.Equals("item", StringComparison.OrdinalIgnoreCase))
                .Select(pair => Unquote(pair.Value))
                .Where(value => value.Length > 0)
                .ToList();
            result[keys[index]] = items;
        }

        return result;
    }

    private static void AddMilestoneEvents(
        IReadOnlyDictionary<string, IReadOnlyList<string>> milestones,
        IReadOnlyDictionary<string, double> contractTimes,
        Rp1DefinitionCatalog catalog,
        List<Rp1CareerEvent> events)
    {
        foreach (var pair in milestones)
        {
            var definition = catalog.Milestones.TryGetValue(pair.Key, out var found) ? found : null;
            double? time = null;
            if (definition?.ContractName is { Length: > 0 } contractName
                && contractTimes.TryGetValue(contractName, out var completed))
            {
                time = completed;
            }

            var meta = pair.Value.Count == 0 ? null : string.Join("、", pair.Value);
            events.Add(Event(
                time,
                Rp1EventKind.Milestone,
                definition?.Headline ?? definition?.Name ?? pair.Key,
                meta,
                definition?.Article ?? definition?.Description,
                pair.Key,
                "里程碑"));
        }
    }

    private static Dictionary<string, double> ReadPrograms(Rp1ScenarioSet scenario)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!scenario.Scenarios.TryGetValue("ProgramHandler", out var handler))
        {
            return result;
        }

        var activated = Child(handler, "ActivatedStrategies");
        if (activated is not null)
        {
            foreach (var pair in activated.Pairs())
            {
                if (pair.Key.StartsWith("leader", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!double.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var time) || time <= 0)
                {
                    continue;
                }

                result[pair.Key] = time;
            }
        }

        foreach (var program in handler.Children.OfType<SfsNode>().Where(node => node.Name.Equals("ACTIVEPROGRAM", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Pair(program, "name");
            var accepted = PairDouble(program, "acceptedUT");
            if (string.IsNullOrWhiteSpace(name) || accepted is null || accepted.Value <= 0)
            {
                continue;
            }

            if (!result.TryGetValue(name, out var existing) || accepted.Value < existing)
            {
                result[name] = accepted.Value;
            }
        }

        return result;
    }

    private static void AddProgramEvents(
        IReadOnlyDictionary<string, double> programs,
        Rp1DefinitionCatalog catalog,
        List<Rp1CareerEvent> events)
    {
        foreach (var pair in programs)
        {
            var definition = catalog.Programs.TryGetValue(pair.Key, out var found) ? found : null;
            events.Add(Event(
                pair.Value,
                Rp1EventKind.ProgramStart,
                definition?.Title ?? pair.Key,
                null,
                definition?.Description,
                pair.Key,
                "项目"));
        }
    }

    private static void AddCompletedProgramEvents(
        Rp1ScenarioSet scenario,
        Rp1DefinitionCatalog catalog,
        List<Rp1CareerEvent> events)
    {
        if (!scenario.Scenarios.TryGetValue("ProgramHandler", out var handler))
        {
            return;
        }

        foreach (var program in handler.Children.OfType<SfsNode>()
                     .Where(node => node.Name.Equals("ACTIVEPROGRAM", StringComparison.OrdinalIgnoreCase)))
        {
            var name = Pair(program, "name");
            var completed = PairDouble(program, "completedUT");
            if (string.IsNullOrWhiteSpace(name) || completed is null || completed.Value <= 0)
            {
                continue;
            }

            var definition = catalog.Programs.TryGetValue(name, out var found) ? found : null;
            events.Add(Event(
                completed.Value,
                Rp1EventKind.ProgramCompleted,
                (definition?.Title ?? name) + " 完成",
                null,
                definition?.Description,
                name,
                "项目"));
        }
    }

    private static Dictionary<string, double> ReadRetiredCrew(Rp1ScenarioSet scenario)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        if (!scenario.Scenarios.TryGetValue("CrewHandler", out var handler))
        {
            return result;
        }

        var retireTimes = Child(handler, "_retireTimes");
        if (retireTimes is null)
        {
            return result;
        }

        foreach (var pair in retireTimes.Pairs())
        {
            if (double.TryParse(pair.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var time) && time > 0)
            {
                result[pair.Key] = time;
            }
        }

        return result;
    }

    private static void AddRetiredEvents(IReadOnlyDictionary<string, double> retired, List<Rp1CareerEvent> events)
    {
        foreach (var pair in retired)
        {
            events.Add(Event(
                pair.Value,
                Rp1EventKind.CrewRetired,
                pair.Key + " 退役",
                "人员",
                null,
                pair.Key,
                "退役"));
        }
    }

    private static List<Rp1CareerPeriod> ReadPeriods(Rp1ScenarioSet scenario, List<string> diagnostics)
    {
        var result = new List<Rp1CareerPeriod>();
        if (!scenario.Scenarios.TryGetValue("CareerLog", out var log))
        {
            return result;
        }

        var periodDict = Child(log, "_periodDict");
        var values = periodDict is null ? null : Child(periodDict, "Values");
        if (values is null)
        {
            diagnostics.Add("CareerLog 没有 _periodDict/Values：时间线缺少预算周期。");
            return result;
        }

        var index = 0;
        foreach (var period in values.Children.OfType<SfsNode>().Where(node => node.Name.Equals("LogPeriod", StringComparison.OrdinalIgnoreCase)))
        {
            var start = PairDouble(period, "StartUT");
            var end = PairDouble(period, "EndUT");
            if (start is null || end is null)
            {
                continue;
            }

            var killed = PairInt(period, "NumNautsKilled") ?? 0;
            result.Add(new Rp1CareerPeriod(
                index++,
                start.Value,
                end.Value,
                Rp1GameDate.Format(end),
                PairDouble(period, "CurrentFunds"),
                PairDouble(period, "CurrentSci"),
                PairDouble(period, "ScienceEarned"),
                PairDouble(period, "Confidence"),
                PairDouble(period, "Reputation"),
                killed,
                PairDouble(period, "SubsidyPaidOut")));
        }

        return result;
    }

    private static void AddCrewLostEvents(IReadOnlyList<Rp1CareerPeriod> periods, List<Rp1CareerEvent> events)
    {
        foreach (var period in periods.Where(period => period.CrewLost > 0))
        {
            events.Add(Event(
                period.EndUt,
                Rp1EventKind.CrewLost,
                $"损失 {period.CrewLost} 名乘员",
                "人员",
                $"预算周期 {period.StartUt.ToString("N0", CultureInfo.InvariantCulture)} – {period.EndUt.ToString("N0", CultureInfo.InvariantCulture)}",
                "CrewLost" + period.Index.ToString(CultureInfo.InvariantCulture),
                "事故"));
        }
    }

    private static List<Rp1CareerEvent> Sort(List<Rp1CareerEvent> events)
        => events
            .OrderBy(item => item.Ut ?? double.MaxValue)
            .ThenBy(item => item.Kind)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static Rp1CareerEvent Event(
        double? ut,
        Rp1EventKind kind,
        string title,
        string? subtitle,
        string? detail,
        string internalName,
        string? meta)
        => new(ut, Rp1GameDate.Format(ut), kind, title, subtitle, detail, internalName, meta);

    private static double? ScenarioDouble(Rp1ScenarioSet set, string scenarioName, string key)
    {
        if (!set.Scenarios.TryGetValue(scenarioName, out var node))
        {
            return null;
        }

        return PairDouble(node, key);
    }

    private static string Unquote(string? raw)
    {
        var value = raw?.Trim() ?? string.Empty;
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static SfsNode? Child(SfsNode node, string name)
        => node.Children.OfType<SfsNode>().FirstOrDefault(child => child.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string? Pair(SfsNode node, string key)
    {
        var value = node.FindPair(key)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static double? PairDouble(SfsNode node, string key)
    {
        var value = Pair(node, key);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && !double.IsNaN(parsed)
            ? parsed
            : null;
    }

    private static int? PairInt(SfsNode node, string key)
    {
        var value = Pair(node, key);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
