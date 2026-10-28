using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Commands;

namespace CkanUI.Services.Commands;

/// <summary>
/// 命令目录实现（UI-40）：线程安全的注册表 + 委托给 <see cref="IFuzzyMatcher"/> 的检索。
/// </summary>
/// <remarks>
/// <para>注册来源有两类：App 外壳（内置导航与界面动作）和插件（<c>ICkanUiPlugin</c> 声明自己的命令）。
/// 插件在后台线程加载，因此注册用一把锁保护；检索前先拷一份快照，避免边注册边遍历。</para>
/// <para>重复 Id 一律拒绝而不是覆盖：覆盖会让「先注册的内置命令」被插件悄悄顶掉，
/// 排查时极难定位。批量注册是 all-or-nothing，失败时目录保持原样。</para>
/// </remarks>
public sealed class CommandCatalog : ICommandCatalog
{
    private readonly IFuzzyMatcher _matcher;
    private readonly List<CommandDescriptor> _commands = new();
    private readonly Dictionary<string, CommandDescriptor> _byId = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    /// <summary>创建目录，使用默认的 <see cref="FuzzyMatcher"/>。</summary>
    public CommandCatalog()
        : this(new FuzzyMatcher())
    {
    }

    /// <summary>创建目录并注入匹配算法（测试与替换算法实现时用）。</summary>
    /// <param name="matcher">匹配算法实例。</param>
    public CommandCatalog(IFuzzyMatcher matcher)
    {
        _matcher = matcher ?? throw new ArgumentNullException(nameof(matcher));
    }

    /// <inheritdoc />
    public IReadOnlyList<CommandDescriptor> Snapshot()
    {
        lock (_gate)
        {
            return _commands.Count == 0 ? Array.Empty<CommandDescriptor>() : _commands.ToArray();
        }
    }

    /// <inheritdoc />
    public OperationResult Register(CommandDescriptor command)
    {
        ArgumentNullException.ThrowIfNull(command);

        lock (_gate)
        {
            if (_byId.ContainsKey(command.Id))
            {
                return OperationResult.Fail("命令 Id 已被占用", "Id = " + command.Id);
            }

            _commands.Add(command);
            _byId.Add(command.Id, command);
            return OperationResult.Ok();
        }
    }

    /// <inheritdoc />
    public OperationResult Register(IEnumerable<CommandDescriptor> commands)
    {
        ArgumentNullException.ThrowIfNull(commands);

        var batch = commands as IReadOnlyList<CommandDescriptor> ?? commands.ToArray();
        if (batch.Count == 0)
        {
            return OperationResult.Ok();
        }

        lock (_gate)
        {
            // 第一遍只校验：批内重复或与既有冲突都整批不生效，避免"注册了一半"的状态。
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var command in batch)
            {
                if (command is null)
                {
                    return OperationResult.Fail("命令集合里有空项");
                }

                if (!seen.Add(command.Id))
                {
                    return OperationResult.Fail("同一批次里命令 Id 重复", "Id = " + command.Id);
                }

                if (_byId.ContainsKey(command.Id))
                {
                    return OperationResult.Fail("命令 Id 已被占用", "Id = " + command.Id);
                }
            }

            foreach (var command in batch)
            {
                _commands.Add(command);
                _byId.Add(command.Id, command);
            }

            return OperationResult.Ok();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<CommandMatch> Search(string? query, int limit = FuzzyMatcher.DefaultLimit)
    {
        var snapshot = Snapshot();
        return snapshot.Count == 0 ? Array.Empty<CommandMatch>() : _matcher.Search(snapshot, query, limit);
    }
}
