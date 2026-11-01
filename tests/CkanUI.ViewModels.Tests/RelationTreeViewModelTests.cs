using CkanUI.ViewModels.Relations;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

/// <summary>
/// 模组依赖关系树增强单元测试（F-28 / UI-18）。
/// 覆盖九种类别生成、反向关系展开、九色图例状态、循环依赖截断保护及双击导航。
/// </summary>
[TestFixture]
public sealed class RelationTreeViewModelTests
{
    private static RelationTreeViewModel Create(
        Dictionary<string, IReadOnlyList<RelationEdge>> graph,
        IEnumerable<string>? known = null,
        IReadOnlyDictionary<string, string>? names = null,
        Action<string>? nav = null,
        string root = "A")
    {
        return new RelationTreeViewModel(
            root,
            graph,
            names: names,
            knownIdentifiers: known ?? graph.Keys.Append(root).Append("Z"),
            navigate: nav);
    }

    private static Dictionary<string, IReadOnlyList<RelationEdge>> Graph(params (string, RelationKind, string)[] edges)
    {
        return edges.GroupBy(x => x.Item1).ToDictionary(
            g => g.Key,
            g => (IReadOnlyList<RelationEdge>)g.Select(x => new RelationEdge(x.Item2, x.Item3)).ToArray(),
            StringComparer.OrdinalIgnoreCase);
    }

    [Test]
    public void NineKindsAreClassified_WithBadgesAndColors()
    {
        // 覆盖全部 9 种关系语义类别
        var allKinds = Enum.GetValues<RelationKind>();
        Assert.That(allKinds, Has.Length.EqualTo(9));

        var edges = allKinds.Select((k, i) => ("A", k, $"Mod_{k}")).ToArray();
        var known = new[] { "A" }.Concat(edges.Select(e => e.Item3));
        var vm = Create(Graph(edges), known);

        var root = vm.Roots[0];
        Assert.That(root.Children.Count, Is.EqualTo(9));

        var classifiedKinds = root.Children.Select(x => x.Kind).Distinct().ToList();
        Assert.That(classifiedKinds, Has.Count.EqualTo(9));

        foreach (var category in root.Children)
        {
            Assert.That(category.IsCategory, Is.True);
            Assert.That(category.ShowBadge, Is.True);
            Assert.That(category.ShowDot, Is.False);
            Assert.That(category.BadgeText, Is.Not.Empty);
            Assert.That(category.ColorTokenKey, Does.StartWith("CkanRelation"));
            Assert.That(category.Children, Has.Count.EqualTo(1));

            var leaf = category.Children[0];
            Assert.That(leaf.IsCategory, Is.False);
            Assert.That(leaf.ShowBadge, Is.False);
            Assert.That(leaf.ShowDot, Is.True);
            Assert.That(leaf.Kind, Is.EqualTo(category.Kind));
        }
    }

    [Test]
    public void Legends_ExposeNineColorsWithCompleteMetadata()
    {
        var vm = Create(new());
        Assert.That(vm.Legends, Has.Count.EqualTo(9));

        var distinctKinds = vm.Legends.Select(l => l.Kind).Distinct().ToList();
        Assert.That(distinctKinds, Has.Count.EqualTo(9));

        foreach (var legend in vm.Legends)
        {
            Assert.That(legend.Label, Is.Not.Null.And.Not.Empty);
            Assert.That(legend.BadgeText, Does.StartWith("[").And.EndsWith("]"));
            Assert.That(legend.ColorTokenKey, Does.StartWith("CkanRelation"));
            Assert.That(legend.Description, Is.Not.Null.And.Not.Empty);
        }

        // 验证特定核心语义文案
        var dependsLegend = vm.Legends.First(l => l.Kind == RelationKind.Depends);
        Assert.That(dependsLegend.Label, Is.EqualTo("依赖"));
        Assert.That(dependsLegend.BadgeText, Is.EqualTo("[依赖]"));

        var conflictsLegend = vm.Legends.First(l => l.Kind == RelationKind.Conflicts);
        Assert.That(conflictsLegend.Label, Is.EqualTo("冲突"));
        Assert.That(conflictsLegend.BadgeText, Is.EqualTo("[冲突]"));
    }

    [Test]
    public void DownwardTraversal_IncludesChildrenAndNestedHierarchy()
    {
        var graph = Graph(
            ("A", RelationKind.Depends, "B"),
            ("B", RelationKind.Recommends, "C"));

        var vm = Create(graph, new[] { "A", "B", "C" });
        var root = vm.Roots[0];
        Assert.That(root.Children, Has.Count.EqualTo(1));

        var depCat = root.Children[0];
        Assert.That(depCat.Kind, Is.EqualTo(RelationKind.Depends));
        Assert.That(depCat.Name, Is.EqualTo("依赖"));

        var bNode = depCat.Children[0];
        Assert.That(bNode.Identifier, Is.EqualTo("B"));
        Assert.That(bNode.Children, Has.Count.EqualTo(1));

        var recCat = bNode.Children[0];
        Assert.That(recCat.Kind, Is.EqualTo(RelationKind.Recommends));
        Assert.That(recCat.Name, Is.EqualTo("推荐"));

        var cNode = recCat.Children[0];
        Assert.That(cNode.Identifier, Is.EqualTo("C"));
    }

    [Test]
    public void ReverseTraversal_BuildsReverseEdgesAndUsesReverseLabels()
    {
        // B 依赖 A，C 冲突 A
        var graph = Graph(
            ("B", RelationKind.Depends, "A"),
            ("C", RelationKind.Conflicts, "A"));

        var vm = Create(graph, new[] { "A", "B", "C" }, root: "A");

        // 正向模式：A 没有出度边，Roots[0].Children 为空
        Assert.That(vm.Roots[0].Children, Is.Empty);
        Assert.That(vm.IsReverse, Is.False);
        Assert.That(vm.DirectionStatusText, Does.Contain("正向"));

        // 切换为反向关系
        vm.ToggleDirectionCommand.Execute(null);
        Assert.That(vm.IsReverse, Is.True);
        Assert.That(vm.DirectionButtonText, Does.Contain("正向"));
        Assert.That(vm.DirectionStatusText, Does.Contain("反向"));

        // 反向模式：应检索出依赖了 A 的 B 以及与 A 冲突的 C
        var root = vm.Roots[0];
        Assert.That(root.Children, Has.Count.EqualTo(2));

        var dependsCat = root.Children.FirstOrDefault(c => c.Kind == RelationKind.Depends);
        Assert.That(dependsCat, Is.Not.Null);
        Assert.That(dependsCat!.Name, Is.EqualTo("谁依赖了我"));
        Assert.That(dependsCat.Children[0].Identifier, Is.EqualTo("B"));

        var conflictsCat = root.Children.FirstOrDefault(c => c.Kind == RelationKind.Conflicts);
        Assert.That(conflictsCat, Is.Not.Null);
        Assert.That(conflictsCat!.Name, Is.EqualTo("谁与我冲突"));
        Assert.That(conflictsCat.Children[0].Identifier, Is.EqualTo("C"));

        // 切回正向
        vm.ToggleDirectionCommand.Execute(null);
        Assert.That(vm.IsReverse, Is.False);
        Assert.That(vm.Roots[0].Children, Is.Empty);
    }

    [Test]
    public void ReverseTraversal_LabelMapping_CoversAllCategories()
    {
        Assert.That(RelationTreeViewModel.Label(RelationKind.Depends, isReverse: true), Is.EqualTo("谁依赖了我"));
        Assert.That(RelationTreeViewModel.Label(RelationKind.Recommends, isReverse: true), Is.EqualTo("谁推荐了我"));
        Assert.That(RelationTreeViewModel.Label(RelationKind.Suggests, isReverse: true), Is.EqualTo("谁建议了我"));
        Assert.That(RelationTreeViewModel.Label(RelationKind.Supports, isReverse: true), Is.EqualTo("谁支持了我"));
        Assert.That(RelationTreeViewModel.Label(RelationKind.Conflicts, isReverse: true), Is.EqualTo("谁与我冲突"));
        Assert.That(RelationTreeViewModel.Label(RelationKind.Provides, isReverse: true), Is.EqualTo("谁提供了我"));
        Assert.That(RelationTreeViewModel.Label(RelationKind.Incompatible, isReverse: true), Is.EqualTo("谁与我不兼容"));
        Assert.That(RelationTreeViewModel.Label(RelationKind.Installed, isReverse: true), Is.EqualTo("已安装"));
        Assert.That(RelationTreeViewModel.Label(RelationKind.Virtual, isReverse: true), Is.EqualTo("虚拟"));
    }

    [Test]
    public void CycleDetection_DoesNotRecurseForever()
    {
        // A -> B -> A 循环依赖
        var graph = Graph(
            ("A", RelationKind.Depends, "B"),
            ("B", RelationKind.Depends, "A"));

        var vm = Create(graph, new[] { "A", "B" });
        var b = vm.Roots[0].Children[0].Children[0];
        Assert.That(b.Identifier, Is.EqualTo("B"));

        var again = b.Children[0].Children[0];
        Assert.That(again.Identifier, Is.EqualTo("A"), "环上的节点应当被列出提示");
        Assert.That(again.Children, Is.Empty, "环上的重复节点不得再次递归展开");
    }

    [Test]
    public void MaximumDepth_IsTruncatedAtEight()
    {
        var edges = Enumerable.Range(0, 10).Select(i => ($"M{i}", RelationKind.Depends, $"M{i + 1}")).ToArray();
        var known = Enumerable.Range(0, 12).Select(i => $"M{i}");

        var vm = Create(Graph(edges), known, root: "M0");
        var node = vm.Roots[0];

        for (var i = 0; i < RelationTreeViewModel.MaximumDepth; i++)
        {
            Assert.That(node.Children, Has.Count.GreaterThan(0));
            node = node.Children[0].Children[0];
        }

        Assert.That(node.Truncated, Is.True, "达到最大深度 8 时应当截断");
        Assert.That(node.Children, Is.Empty, "截断节点不得再展开子节点");
    }

    [Test]
    public void UnknownNode_IsLabeled_AndDoesNotNavigate()
    {
        string? navigated = null;
        var vm = Create(Graph(("A", RelationKind.Depends, "Missing")), new[] { "A" }, nav: x => navigated = x);

        var unknownLeaf = vm.Roots[0].Children[0].Children[0];
        Assert.That(unknownLeaf.IsKnown, Is.False);
        Assert.That(unknownLeaf.Name, Does.Contain("未知"));

        vm.Navigate(unknownLeaf);
        Assert.That(navigated, Is.Null, "未知模组不应触发导航");
    }

    [Test]
    public void DoubleClickNavigation_DelegatesIdentifier()
    {
        string? target = null;
        var vm = Create(Graph(("A", RelationKind.Depends, "B")), new[] { "A", "B" }, nav: x => target = x);

        var root = vm.Roots[0];
        var category = root.Children[0];
        var bNode = category.Children[0];

        // 双击分类节点不导航
        vm.Navigate(category);
        Assert.That(target, Is.Null);

        // 双击根节点不导航
        vm.Navigate(root);
        Assert.That(target, Is.Null);

        // 双击已知目标模组节点委托导航
        vm.Navigate(bNode);
        Assert.That(target, Is.EqualTo("B"));
    }

    [Test]
    public void EmptyRelation_KeepsRootOnly()
    {
        var vm = Create(new());
        Assert.That(vm.Roots, Has.Count.EqualTo(1));
        Assert.That(vm.Roots[0].Children, Is.Empty);
    }

    [Test]
    public void ToggleExpanded_InvertsNodeState()
    {
        var vm = Create(new());
        var root = vm.Roots[0];
        Assert.That(root.IsExpanded, Is.True);

        vm.ToggleExpanded(root);
        Assert.That(root.IsExpanded, Is.False);

        vm.ToggleExpanded(root);
        Assert.That(root.IsExpanded, Is.True);
    }

    [Test]
    public void UnknownRoot_ShowsUnknownStatus()
    {
        var vm = Create(new(), Array.Empty<string>(), root: "UnknownRoot");
        Assert.That(vm.StatusText, Is.EqualTo("未知"));
    }

    [Test]
    public void NameLookup_UsesFriendlyNameWhenAvailable()
    {
        var names = new Dictionary<string, string>
        {
            ["B"] = "MechJeb 2 (自动驾驶仪)"
        };

        var vm = Create(Graph(("A", RelationKind.Depends, "B")), new[] { "A", "B" }, names: names);
        var bNode = vm.Roots[0].Children[0].Children[0];
        Assert.That(bNode.Name, Is.EqualTo("MechJeb 2 (自动驾驶仪)"));
    }

    [Test]
    public void VisualRelationNetwork_ConstructsOrbitRingsAndEdges_Correctly()
    {
        var vm = Create(Graph(
            ("A", RelationKind.Depends, "Dep1"),
            ("A", RelationKind.Recommends, "Rec1"),
            ("A", RelationKind.Conflicts, "Conf1")),
            new[] { "A", "Dep1", "Rec1", "Conf1" });

        Assert.That(vm.IsGraphView, Is.True, "默认应开启可视化关系网");
        Assert.That(vm.GraphNodes, Has.Count.EqualTo(4));
        Assert.That(vm.GraphEdges, Has.Count.EqualTo(3));

        var rootNode = vm.GraphNodes.First(n => n.IsRoot);
        Assert.That(rootNode.Identifier, Is.EqualTo("A"));
        Assert.That(rootNode.OrbitRing, Is.EqualTo(0));
        Assert.That(rootNode.X, Is.EqualTo(0));
        Assert.That(rootNode.Y, Is.EqualTo(0));

        var depNode = vm.GraphNodes.First(n => n.Identifier == "Dep1");
        Assert.That(depNode.OrbitRing, Is.EqualTo(1), "Depends 位于内圈轨道 1");
        Assert.That(depNode.BadgeText, Is.EqualTo("[依赖]"));

        var recNode = vm.GraphNodes.First(n => n.Identifier == "Rec1");
        Assert.That(recNode.OrbitRing, Is.EqualTo(2), "Recommends 位于中圈轨道 2");
        Assert.That(recNode.BadgeText, Is.EqualTo("[推荐]"));

        var confNode = vm.GraphNodes.First(n => n.Identifier == "Conf1");
        Assert.That(confNode.OrbitRing, Is.EqualTo(3), "Conflicts 位于外圈轨道 3");
        Assert.That(confNode.BadgeText, Is.EqualTo("[冲突]"));
    }

    [Test]
    public void VisualRelationNetwork_ToggleViewMode_SwitchesBetweenGraphAndTree()
    {
        var vm = Create(new());
        Assert.That(vm.IsGraphView, Is.True);
        Assert.That(vm.ViewModeButtonText, Is.EqualTo("树状图"));

        vm.ToggleViewModeCommand.Execute(null);
        Assert.That(vm.IsGraphView, Is.False);
        Assert.That(vm.ViewModeButtonText, Is.EqualTo("关系网"));

        vm.ToggleViewModeCommand.Execute(null);
        Assert.That(vm.IsGraphView, Is.True);
    }

    [Test]
    public void VisualRelationNetwork_FilterKind_FiltersNonMatchingNodes()
    {
        var vm = Create(Graph(
            ("A", RelationKind.Depends, "Dep1"),
            ("A", RelationKind.Conflicts, "Conf1")),
            new[] { "A", "Dep1", "Conf1" });

        var depNode = vm.GraphNodes.First(n => n.Identifier == "Dep1");
        var confNode = vm.GraphNodes.First(n => n.Identifier == "Conf1");

        // 默认无筛选
        Assert.That(depNode.IsFiltered, Is.False);
        Assert.That(confNode.IsFiltered, Is.False);

        // 仅筛选 Depends
        vm.FilterKindCommand.Execute(RelationKind.Depends);
        Assert.That(vm.ActiveKindFilter, Is.EqualTo(RelationKind.Depends));
        Assert.That(depNode.IsFiltered, Is.False);
        Assert.That(confNode.IsFiltered, Is.True, "非 Depends 节点应被标记为 IsFiltered");

        // 再次点击取消筛选
        vm.FilterKindCommand.Execute(RelationKind.Depends);
        Assert.That(vm.ActiveKindFilter, Is.Null);
        Assert.That(depNode.IsFiltered, Is.False);
        Assert.That(confNode.IsFiltered, Is.False);
    }

    [Test]
    public void VisualRelationNetwork_NavigateById_NavigatesKnownMods()
    {
        string? navigated = null;
        var vm = Create(Graph(("A", RelationKind.Depends, "KnownMod")), new[] { "A", "KnownMod" }, nav: id => navigated = id);

        vm.NavigateByIdCommand.Execute("UnknownMod");
        Assert.That(navigated, Is.Null, "未知模组不触发导航");

        vm.NavigateByIdCommand.Execute("KnownMod");
        Assert.That(navigated, Is.EqualTo("KnownMod"), "已知模组应触发导航");
    }
}
