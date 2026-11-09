using DynamicIsland.App.Services;
using DynamicIsland.App.Settings;
using DynamicIsland.Core.Threading;

namespace DynamicIsland.Tests.UI;

[Collection("Translator WPF localization")]
public sealed class AboutUpdateViewModelTests
{
    [Fact]
    public void Latest_available_and_progress_are_distinct_and_use_actual_installed_version()
    {
        var controller = new Controller();
        using var model = Attach(controller);
        controller.Publish(UpdateState.Latest);
        Assert.Contains("v0.3.2", model.UpdateStatus, StringComparison.Ordinal);
        Assert.False(model.ShowDownloadUpdate);
        controller.Publish(UpdateState.Available);
        Assert.Contains("v0.3.3", model.UpdateStatus, StringComparison.Ordinal);
        Assert.Contains("v0.3.2", model.UpdateDetail, StringComparison.Ordinal);
        Assert.True(model.ShowDownloadUpdate);
        controller.Publish(UpdateState.Downloading);
        Assert.True(model.IsUpdateProgressIndeterminate);
        Assert.True(model.IsUpdateBusy);
        controller.Publish(UpdateState.Downloading, 37);
        Assert.False(model.IsUpdateProgressIndeterminate);
        Assert.Equal(37, model.UpdateProgress);
        Assert.Contains("37%", model.UpdateStatus, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manual_check_does_not_download_and_duplicate_command_is_disabled()
    {
        var controller = new Controller();
        var finish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.Check = async cancellation =>
        {
            controller.Publish(UpdateState.Checking);
            await finish.Task.WaitAsync(cancellation);
            controller.Publish(UpdateState.Available);
        };
        using var model = Attach(controller);
        var pending = model.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.False(model.CheckForUpdatesCommand.CanExecute(null));
        Assert.False(model.DownloadUpdateCommand.CanExecute(null));
        Assert.True(model.ShowCancelUpdate);
        finish.SetResult();
        await pending;
        Assert.Equal(1, controller.CheckCalls);
        Assert.Equal(0, controller.DownloadCalls);
        Assert.True(model.DownloadUpdateCommand.CanExecute(null));
    }

    [Fact]
    public async Task Closing_manual_download_cancels_its_token_without_global_background_cancel()
    {
        var controller = new Controller();
        controller.Publish(UpdateState.Available);
        controller.Download = async cancellation =>
        {
            controller.Publish(UpdateState.Downloading);
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellation); }
            catch (OperationCanceledException) { controller.Publish(UpdateState.Cancelled); }
        };
        using var model = Attach(controller);
        var pending = model.DownloadUpdateCommand.ExecuteAsync(null);
        model.CancelManualUpdate();
        await pending;
        Assert.Equal(UpdateState.Cancelled, controller.Current.State);
        Assert.Equal(0, controller.GlobalCancels);
        Assert.False(model.IsUpdateBusy);
        Assert.True(model.CheckForUpdatesCommand.CanExecute(null));
    }

    [Fact]
    public void Hiding_settings_does_not_cancel_an_automatic_download()
    {
        var controller = new Controller();
        controller.Publish(UpdateState.Downloading, 24);
        using var model = Attach(controller);
        model.CancelManualUpdate();
        Assert.Equal(0, controller.GlobalCancels);
        Assert.True(model.IsUpdateBusy);
        Assert.False(model.ShowCancelUpdate);
    }

    [Fact]
    public async Task Download_error_retry_retries_download_and_ready_exits_only_on_user_action()
    {
        var controller = new Controller();
        controller.Publish(UpdateState.Error, failure: new(UpdateStage.Downloading, UpdateFailureReason.Network));
        using var model = Attach(controller);
        var exits = 0;
        model.ExitForUpdate = () => { exits++; return Task.CompletedTask; };
        await model.CheckForUpdatesCommand.ExecuteAsync(null);
        Assert.Equal(0, controller.CheckCalls);
        Assert.Equal(1, controller.DownloadCalls);
        Assert.True(model.ShowApplyUpdate);
        Assert.NotEmpty(model.ReadyInstruction);
        Assert.Equal(0, exits);
        await model.ExitAndInstallCommand.ExecuteAsync(null);
        Assert.Equal(1, exits);
    }

    [Fact]
    public void Portable_is_explicit_and_release_links_remain_on_the_project()
    {
        var controller = new Controller();
        controller.Publish(UpdateState.Unsupported);
        using var model = Attach(controller);
        Assert.True(model.ShowReleaseLink);
        Assert.False(model.ShowDownloadUpdate);
        Assert.False(model.ShowApplyUpdate);
        controller.Current = controller.Current with { ReleaseUrl = "https://example.com/releases/tag/v0.3.3" };
        controller.Raise();
        Assert.Equal(AboutViewModel.RepositoryUrl + "/releases/latest", model.ReleasePageUrl);
        Assert.False(AboutViewModel.IsProjectReleaseUrl("https://github.com.evil.test/zab1yak/dynamic-island-windows/releases/latest"));
        Assert.False(AboutViewModel.IsProjectReleaseUrl("http://github.com/zab1yak/dynamic-island-windows/releases/latest"));
        Assert.True(AboutViewModel.IsProjectReleaseUrl("https://github.com/zab1yak/dynamic-island-windows/releases/tag/v0.3.3"));
    }

    [Fact]
    public void Background_snapshot_changes_are_marshaled_to_the_UI_dispatcher()
    {
        var controller = new Controller();
        var dispatcher = new QueuedDispatcher();
        using var model = new AboutViewModel();
        model.AttachUpdates(controller, dispatcher);
        var before = model.UpdateStatus;
        dispatcher.Access = false;
        controller.Publish(UpdateState.Available);
        Assert.Equal(before, model.UpdateStatus);
        Assert.Single(dispatcher.Pending);
        dispatcher.Drain();
        Assert.Contains("v0.3.3", model.UpdateStatus, StringComparison.Ordinal);
    }

    private static AboutViewModel Attach(Controller controller)
    {
        var model = new AboutViewModel();
        model.AttachUpdates(controller, new QueuedDispatcher());
        return model;
    }

    private sealed class Controller : IApplicationUpdateController
    {
        public UpdateSnapshot Current { get; set; } = new(UpdateState.Idle, "0.3.2", "0.3.3",
            AboutViewModel.RepositoryUrl + "/releases/tag/v0.3.3", null, DateTimeOffset.Parse("2026-11-09T12:00:00Z", System.Globalization.CultureInfo.InvariantCulture), null);
        public event EventHandler? Changed;
        public bool IsInstalled => Current.State != UpdateState.Unsupported;
        public bool IsBusy => Current.State is UpdateState.Checking or UpdateState.Downloading;
        public int CheckCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public int GlobalCancels { get; private set; }
        public Func<CancellationToken, Task>? Check { get; set; }
        public Func<CancellationToken, Task>? Download { get; set; }
        public async Task CheckAsync(bool notify = false, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            if (Check is not null) await Check(cancellationToken);
            else Publish(UpdateState.Available);
        }
        public async Task DownloadAsync(CancellationToken cancellationToken = default)
        {
            DownloadCalls++;
            if (Download is not null) await Download(cancellationToken);
            else Publish(UpdateState.Ready, 100);
        }
        public void Cancel() => GlobalCancels++;
        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);
        public void Publish(UpdateState state, int? progress = null, UpdateFailure? failure = null)
        { Current = Current with { State = state, ProgressPercent = progress, Failure = failure }; Raise(); }
    }

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        public bool Access { get; set; } = true;
        public List<Action> Pending { get; } = [];
        public bool CheckAccess() => Access;
        public void Post(Action action) => Pending.Add(action);
        public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; }
        public void Drain()
        {
            Access = true;
            foreach (var action in Pending) action();
            Pending.Clear();
        }
    }
}
