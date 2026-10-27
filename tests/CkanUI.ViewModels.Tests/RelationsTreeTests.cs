using CkanUI.ViewModels.Relations;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class RelationsTreeTests
{
    private static RelationTreeViewModel Create(Dictionary<string,IReadOnlyList<RelationEdge>> graph,IEnumerable<string>? known=null,Action<string>? nav=null)=>new("A",graph,knownIdentifiers:known??graph.Keys.Append("Z"),navigate:nav);
    private static Dictionary<string,IReadOnlyList<RelationEdge>> Graph(params (string,RelationKind,string)[] edges)=>edges.GroupBy(x=>x.Item1).ToDictionary(g=>g.Key,g=>(IReadOnlyList<RelationEdge>)g.Select(x=>new RelationEdge(x.Item2,x.Item3)).ToArray());
    [Test] public void DownwardTraversalIncludesChild()=>Assert.That(Create(Graph(("A",RelationKind.Depends,"B"))).Roots[0].Children[0].Children[0].Identifier,Is.EqualTo("B"));
    [Test] public void ReverseTraversalFindsDependent(){var vm=Create(Graph(("B",RelationKind.Depends,"A")));vm.IsReverse=true;Assert.That(vm.Roots[0].Children[0].Children[0].Identifier,Is.EqualTo("B"));}
    [Test] public void CycleDoesNotRecurseForever(){var vm=Create(Graph(("A",RelationKind.Depends,"B"),("B",RelationKind.Depends,"A")));var b=vm.Roots[0].Children[0].Children[0];Assert.That(b.Identifier,Is.EqualTo("B"));var again=b.Children[0].Children[0];Assert.That(again.Identifier,Is.EqualTo("A"),"环上的节点必须被重新列出");Assert.That(again.Children,Is.Empty,"环上的节点不得再展开");}
    [Test] public void DepthIsTruncatedAtEight(){var edges=Enumerable.Range(0,9).Select(i=>($"{(char)('A'+i)}",RelationKind.Depends,$"{(char)('B'+i)}")).ToArray();var vm=Create(Graph(edges),Enumerable.Range(0,10).Select(i=>((char)('A'+i)).ToString()));var n=vm.Roots[0];for(var i=0;i<8;i++)n=n.Children[0].Children[0];Assert.That(n.Truncated,Is.True);}
    [Test] public void UnknownNodeIsLabeled(){var vm=Create(Graph(("A",RelationKind.Depends,"Missing")),new[]{"A"});Assert.That(vm.Roots[0].Children[0].Children[0].Name,Does.Contain("未知"));}
    [Test] public void NineKindsAreClassified(){var edges=Enum.GetValues<RelationKind>().Select((k,i)=>("A",k,$"M{i}")).ToArray();var vm=Create(Graph(edges),new[]{"A"}.Concat(edges.Select(e=>e.Item3)));Assert.That(vm.Roots[0].Children.Select(x=>x.Kind).Distinct().Count(),Is.EqualTo(9));}
    [Test] public void EmptyRelationKeepsRootOnly()=>Assert.That(Create(new()).Roots[0].Children,Is.Empty);
    [Test] public void DoubleClickNavigationDelegatesIdentifier(){string? target=null;var vm=Create(Graph(("A",RelationKind.Depends,"B")),new[]{"A","B"},nav:x=>target=x);vm.Navigate(vm.Roots[0].Children[0].Children[0]);Assert.That(target,Is.EqualTo("B"));}
    [Test] public void UnknownNodeDoesNotNavigate(){string? target=null;var vm=Create(Graph(("A",RelationKind.Depends,"B")),new[]{"A"},nav:x=>target=x);vm.Navigate(vm.Roots[0].Children[0].Children[0]);Assert.That(target,Is.Null,"未知模组没有详情页可跳");}
    [Test] public void CanCollapseNode(){var vm=Create(new());var root=vm.Roots[0];vm.ToggleExpanded(root);Assert.That(root.IsExpanded,Is.False);}
    [Test] public void ReverseToggleRebuildsTree(){var vm=Create(Graph(("B",RelationKind.Depends,"A")));vm.IsReverse=true;Assert.That(vm.Roots[0].Children,Is.Not.Empty);}
    [Test] public void UnknownRootShowsUnknownStatus()=>Assert.That(Create(new(),Array.Empty<string>()).StatusText,Is.EqualTo("未知"));
}
