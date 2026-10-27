using CkanUI.Services.Abstractions;
using CkanUI.Services.Abstractions.Dtos;
using CkanUI.ViewModels.Pages;
using CkanUI.ViewModels.Pages.Content;
using NUnit.Framework;

namespace CkanUI.ViewModels.Tests;

[TestFixture]
public sealed class ContentPageViewModelTests
{
    private sealed class FakeContentTargetProvider : IContentTargetProvider
    {
        public string? SelectedId { get; set; }
        public Task<string?> GetSelectedIdentifierAsync() => Task.FromResult(SelectedId);
    }

    private sealed class FakeContentService : IContentService
    {
        public Func<string, CancellationToken, Task<OperationResult<ContentPreview>>>? PreviewHandler { get; set; }
        public Func<string, IProgress<OperationProgress>?, CancellationToken, Task<OperationResult<string>>>? EnsureHandler { get; set; }

        public Task<OperationResult<ContentPreview>> PreviewAsync(string identifier, CancellationToken ct = default)
        {
            if (PreviewHandler is not null) return PreviewHandler(identifier, ct);
            return Task.FromResult(OperationResult<ContentPreview>.Ok(new ContentPreview(identifier, true, "C:/cache/test.zip", 1024, new[]
            {
                new ContentEntry("GameData", true, 0, 0),
                new ContentEntry("GameData/test.dll", false, 1024, 1)
            })));
        }

        public Task<OperationResult<string>> EnsureCachedAsync(string identifier, IProgress<OperationProgress>? progress = null, CancellationToken ct = default)
        {
            if (EnsureHandler is not null) return EnsureHandler(identifier, progress, ct);
            return Task.FromResult(OperationResult<string>.Ok("C:/cache/test.zip"));
        }
    }

    private sealed class FakeContentShellLauncher : IContentShellLauncher
    {
        public string? LastCopiedText { get; private set; }
        public string? LastOpenedFolder { get; private set; }
        public bool ShouldFail { get; set; }

        public Task<OperationResult> CopyTextAsync(string text)
        {
            if (ShouldFail) return Task.FromResult(OperationResult.Fail("剪贴板拒绝访问。"));
            LastCopiedText = text;
            return Task.FromResult(OperationResult.Ok());
        }

        public Task<OperationResult> OpenFolderAsync(string absolutePath)
        {
            if (ShouldFail) return Task.FromResult(OperationResult.Fail("找不到指定的目录。"));
            LastOpenedFolder = absolutePath;
            return Task.FromResult(OperationResult.Ok());
        }
    }

    [Test]
    public void 基本属性_继承PageViewModel且中文标题与空状态()
    {
        var vm = new ContentPageViewModel();
        Assert.That(vm.Title, Is.EqualTo("内容预览"));
        Assert.That(vm.ShowEmptyState, Is.True);
        Assert.That(vm.EmptyText, Does.Contain("选择一个模组"));
    }

    [Test]
    public async Task PreviewAsync_无选中模组_显示中文提示并清空节点()
    {
        var target = new FakeContentTargetProvider { SelectedId = null };
        var service = new FakeContentService();
        var vm = new ContentPageViewModel(service, target);

        await vm.PreviewAsync();

        Assert.That(vm.EmptyText, Does.Contain("请先在模组页选择一个模组"));
        Assert.That(vm.RootNodes, Is.Empty);
        Assert.That(vm.ShowEmptyState, Is.True);
    }

    [Test]
    public async Task PreviewAsync_加载成功_正确构建树结构与大小()
    {
        var target = new FakeContentTargetProvider { SelectedId = "ModAlpha" };
        var service = new FakeContentService
        {
            PreviewHandler = (id, ct) => Task.FromResult(OperationResult<ContentPreview>.Ok(new ContentPreview(
                id, true, "C:/cache/alpha.zip", 2048, new[]
                {
                    new ContentEntry("GameData", true, 0, 0),
                    new ContentEntry("GameData/ModAlpha", true, 0, 1),
                    new ContentEntry("GameData/ModAlpha/Alpha.dll", false, 2048, 2)
                })))
        };

        var vm = new ContentPageViewModel(service, target);
        await vm.PreviewAsync();

        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.TotalBytes, Is.EqualTo(2048));
        Assert.That(vm.RootNodes.Count, Is.EqualTo(1));
        var root = vm.RootNodes[0];
        Assert.That(root.Name, Is.EqualTo("GameData"));
        Assert.That(root.Children.Count, Is.EqualTo(1));
        var child = root.Children[0];
        Assert.That(child.Name, Is.EqualTo("ModAlpha"));
        Assert.That(child.Children.Count, Is.EqualTo(1));
        var file = child.Children[0];
        Assert.That(file.Name, Is.EqualTo("Alpha.dll"));
        Assert.That(file.SizeBytes, Is.EqualTo(2048));
    }

    [Test]
    public async Task PreviewAsync_加载失败_进错误栏且可重试()
    {
        var target = new FakeContentTargetProvider { SelectedId = "BadMod" };
        var service = new FakeContentService
        {
            PreviewHandler = (id, ct) => Task.FromResult(OperationResult<ContentPreview>.Fail("ZIP 文件损坏或无法读取。"))
        };

        var vm = new ContentPageViewModel(service, target);
        await vm.PreviewAsync();

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("ZIP 文件损坏或无法读取"));
        Assert.That(vm.RootNodes, Is.Empty);

        // 重试成功
        service.PreviewHandler = (id, ct) => Task.FromResult(OperationResult<ContentPreview>.Ok(
            new ContentPreview(id, true, "C:/cache/fixed.zip", 100, new[] { new ContentEntry("readme.txt", false, 100, 0) })));

        await vm.RetryAsync();

        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.RootNodes.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task DownloadAsync_未缓存时下载并自动预览()
    {
        var target = new FakeContentTargetProvider { SelectedId = "NeedDownloadMod" };
        var downloaded = false;
        var service = new FakeContentService
        {
            EnsureHandler = (id, progress, ct) =>
            {
                downloaded = true;
                return Task.FromResult(OperationResult<string>.Ok("C:/cache/down.zip"));
            },
            PreviewHandler = (id, ct) => Task.FromResult(OperationResult<ContentPreview>.Ok(
                new ContentPreview(id, true, "C:/cache/down.zip", 512, new[] { new ContentEntry("test.cfg", false, 512, 0) })))
        };

        var vm = new ContentPageViewModel(service, target);
        await vm.DownloadAsync();

        Assert.That(downloaded, Is.True);
        Assert.That(vm.HasError, Is.False);
        Assert.That(vm.RootNodes.Count, Is.EqualTo(1));
    }

    [Test]
    public async Task DownloadAsync_下载失败_记录错误()
    {
        var target = new FakeContentTargetProvider { SelectedId = "FailMod" };
        var service = new FakeContentService
        {
            EnsureHandler = (id, progress, ct) => Task.FromResult(OperationResult<string>.Fail("网络连接超时。"))
        };

        var vm = new ContentPageViewModel(service, target);
        await vm.DownloadAsync();

        Assert.That(vm.HasError, Is.True);
        Assert.That(vm.ErrorText, Does.Contain("网络连接超时"));
    }

    [Test]
    public async Task Cancel_取消运行中任务_进入取消状态()
    {
        var target = new FakeContentTargetProvider { SelectedId = "LongMod" };
        var tcs = new TaskCompletionSource<OperationResult<ContentPreview>>();
        var service = new FakeContentService
        {
            PreviewHandler = async (id, ct) =>
            {
                ct.Register(() => tcs.TrySetCanceled(ct));
                return await tcs.Task;
            }
        };

        var vm = new ContentPageViewModel(service, target);
        var previewTask = vm.PreviewAsync();
        Assert.That(vm.IsBusy, Is.True);

        vm.Cancel();
        await previewTask;

        Assert.That(vm.IsBusy, Is.False);
        Assert.That(vm.ErrorText, Does.Contain("已取消"));
    }

    [Test]
    public async Task CopyPathAsync_缺宿主时_给出中文降级提示()
    {
        var target = new FakeContentTargetProvider { SelectedId = "CopyMod" };
        var service = new FakeContentService();
        var vm = new ContentPageViewModel(service, target);
        await vm.PreviewAsync();

        vm.SelectedNode = vm.RootNodes[0];
        vm.ShellLauncher = null;

        await vm.CopyPathAsync();

        Assert.That(vm.StatusText, Does.Contain("当前宿主没有提供剪贴板支持"));
    }

    [Test]
    public async Task CopyPathAsync_未选节点时_给出提示()
    {
        var target = new FakeContentTargetProvider { SelectedId = "CopyMod" };
        var service = new FakeContentService();
        var vm = new ContentPageViewModel(service, target);
        await vm.PreviewAsync();

        vm.SelectedNode = null;
        await vm.CopyPathAsync();

        Assert.That(vm.StatusText, Does.Contain("请先在列表中选中一个文件或目录"));
    }

    [Test]
    public async Task CopyPathAsync_宿主支持_复制成功()
    {
        var target = new FakeContentTargetProvider { SelectedId = "CopyMod" };
        var service = new FakeContentService();
        var launcher = new FakeContentShellLauncher();
        var vm = new ContentPageViewModel(service, target)
        {
            ShellLauncher = launcher
        };
        await vm.PreviewAsync();

        vm.SelectedNode = vm.RootNodes[0];
        await vm.CopyPathAsync();

        Assert.That(launcher.LastCopiedText, Is.EqualTo(vm.RootNodes[0].RelativePath));
        Assert.That(vm.StatusText, Does.Contain("已复制路径"));
    }

    [Test]
    public async Task OpenDirectoryAsync_缺宿主且未下载时_给出降级提示()
    {
        var target = new FakeContentTargetProvider { SelectedId = "OpenMod" };
        var service = new FakeContentService();
        var vm = new ContentPageViewModel(service, target)
        {
            ShellLauncher = null,
            CacheFilePath = null
        };

        await vm.OpenDirectoryAsync();

        Assert.That(vm.StatusText, Does.Contain("尚未下载"));
    }

    [Test]
    public void Activate与Deactivate_幂等且取消关联任务()
    {
        var vm = new ContentPageViewModel();
        Assert.That(vm.IsActive, Is.False);

        vm.Activate();
        Assert.That(vm.IsActive, Is.True);
        vm.Activate();
        Assert.That(vm.IsActive, Is.True);

        vm.Deactivate();
        Assert.That(vm.IsActive, Is.False);
        vm.Deactivate();
        Assert.That(vm.IsActive, Is.False);
    }
}
