using System.Net.Http;
using DynamicIsland.App.Services;
using Microsoft.Extensions.Time.Testing;

namespace DynamicIsland.Tests.Updates;

public sealed class UpdateCoordinatorTests
{
    [Fact]
    public async Task Manual_check_exposes_available_without_downloading_or_scheduling()
    {
        var backend = new Backend { Candidate = NewVersion };
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Available, controller.Current.State);
        Assert.Equal("0.3.10", controller.Current.AvailableVersion);
        Assert.Equal("0.3.9", controller.Current.InstalledVersion);
        Assert.Equal(0, backend.Downloads);
        Assert.Equal(0, backend.Preparations);
        Assert.NotNull(controller.Current.CheckedAt);
    }

    [Fact]
    public async Task Latest_records_success_only_and_unsupported_never_uses_backend()
    {
        var time = new FakeTimeProvider();
        var backend = new Backend();
        using var controller = new UpdateCoordinator(backend, time);
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Latest, controller.Current.State);
        Assert.Equal(time.GetUtcNow(), controller.Current.CheckedAt);
        backend.IsInstalled = false;
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Unsupported, controller.Current.State);
        Assert.Equal(1, backend.Checks);
    }

    [Fact]
    public async Task Download_uses_real_progress_and_installs_only_during_actual_exit_once()
    {
        var backend = new Backend { Candidate = NewVersion };
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        var progress = new List<int?>();
        controller.Changed += (_, _) => { if (controller.Current.State == UpdateState.Downloading) progress.Add(controller.Current.ProgressPercent); };
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        await controller.DownloadAsync(TestContext.Current.CancellationToken);
        Assert.Contains(null, progress);
        Assert.Contains(42, progress);
        Assert.Equal(UpdateState.Ready, controller.Current.State);
        Assert.Equal(0, backend.Preparations);
        Assert.True(controller.PrepareForExit());
        Assert.True(controller.PrepareForExit());
        Assert.Equal(1, backend.Preparations);
    }

    [Fact]
    public async Task Background_check_still_downloads_and_notifies_but_does_not_start_exit_helper_early()
    {
        var backend = new Backend { Candidate = NewVersion };
        var notifications = 0;
        using var controller = new UpdateCoordinator(backend, TimeProvider.System, _ => notifications++);
        await controller.CheckAsync(notify: true, TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Ready, controller.Current.State);
        Assert.Equal(1, backend.Downloads);
        Assert.Equal(1, notifications);
        Assert.Equal(0, backend.Preparations);
    }

    [Fact]
    public async Task Cancelled_uncancellable_check_keeps_single_flight_until_backend_actually_finishes()
    {
        var deferred = new TaskCompletionSource<UpdateCandidate?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { Check = () => deferred.Task };
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        using var cancellation = new CancellationTokenSource();
        var first = controller.CheckAsync(cancellationToken: cancellation.Token);
        cancellation.Cancel();
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        await controller.DownloadAsync(TestContext.Current.CancellationToken);
        Assert.True(controller.IsBusy);
        Assert.Equal(1, backend.Checks);
        deferred.SetResult(NewVersion);
        await first;
        Assert.False(controller.IsBusy);
        Assert.Equal(UpdateState.Cancelled, controller.Current.State);
        Assert.Null(controller.Current.CheckedAt);
        Assert.Equal(0, backend.Downloads);
    }

    [Fact]
    public async Task Cancel_during_download_never_reports_ready_or_schedules_install_and_retry_works()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend
        {
            Candidate = NewVersion,
            Download = async token => { entered.SetResult(); await Task.Delay(Timeout.InfiniteTimeSpan, token); },
        };
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        var download = controller.DownloadAsync(TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TestContext.Current.CancellationToken);
        controller.Cancel();
        await download;
        Assert.Equal(UpdateState.Cancelled, controller.Current.State);
        Assert.Equal(0, backend.Preparations);
        backend.Download = null;
        await controller.DownloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Ready, controller.Current.State);
    }

    [Theory]
    [InlineData("Network")]
    [InlineData("InvalidFeed")]
    [InlineData("Timeout")]
    public async Task Check_failures_are_not_latest_and_preserve_previous_success_time(string reasonName)
    {
        var reason = Enum.Parse<UpdateFailureReason>(reasonName);
        var backend = new Backend();
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        var checkedAt = controller.Current.CheckedAt;
        backend.Check = () => Task.FromException<UpdateCandidate?>(new UpdateBackendException(reason, "synthetic failure"));
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Error, controller.Current.State);
        Assert.Equal(new(UpdateStage.Checking, reason), controller.Current.Failure);
        Assert.Equal(checkedAt, controller.Current.CheckedAt);
    }

    [Fact]
    public async Task Integrity_failure_keeps_checked_candidate_for_explicit_retry()
    {
        var backend = new Backend { Candidate = NewVersion, Download = _ => Task.FromException(new UpdateBackendException(UpdateFailureReason.Integrity, "synthetic checksum")) };
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        await controller.DownloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new(UpdateStage.Downloading, UpdateFailureReason.Integrity), controller.Current.Failure);
        Assert.Equal(0, backend.Preparations);
        backend.Download = null;
        await controller.DownloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(UpdateState.Ready, controller.Current.State);
    }

    [Fact]
    public async Task Repeated_manual_and_background_actions_do_not_duplicate_a_download()
    {
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var backend = new Backend { Candidate = NewVersion, Download = _ => complete.Task };
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        var download = controller.DownloadAsync(TestContext.Current.CancellationToken);
        await controller.CheckAsync(notify: true, TestContext.Current.CancellationToken);
        await controller.DownloadAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.Downloads);
        complete.SetResult();
        await download;
        Assert.Equal(UpdateState.Ready, controller.Current.State);
    }

    [Fact]
    public async Task Exit_preparation_failure_is_visible_and_can_be_retried_without_touching_current_version()
    {
        var backend = new Backend { Candidate = NewVersion, PrepareFails = true };
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        await controller.CheckAsync(notify: true, TestContext.Current.CancellationToken);
        Assert.False(controller.PrepareForExit());
        Assert.Equal(new(UpdateStage.Preparing, UpdateFailureReason.Unavailable), controller.Current.Failure);
        Assert.Equal("0.3.9", controller.Current.InstalledVersion);
        backend.PrepareFails = false;
        Assert.True(controller.PrepareForExit());
        Assert.Equal(2, backend.Preparations);
    }

    [Fact]
    public async Task Transport_and_timeout_exceptions_have_distinct_user_reasons()
    {
        var backend = new Backend { Check = () => Task.FromException<UpdateCandidate?>(new HttpRequestException("offline")) };
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(UpdateFailureReason.Network, controller.Current.Failure?.Reason);
        backend.Check = () => Task.FromException<UpdateCandidate?>(new TaskCanceledException("HTTP timeout"));
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        Assert.Equal(UpdateFailureReason.Timeout, controller.Current.Failure?.Reason);
    }

    [Fact]
    public async Task Late_progress_from_failed_operation_cannot_overwrite_retry_progress()
    {
        Action<int>? oldProgress = null;
        var backend = new Backend { Candidate = NewVersion };
        backend.ProgressObserved = progress => oldProgress = progress;
        backend.Download = _ => Task.FromException(new HttpRequestException("synthetic interrupted download"));
        using var controller = new UpdateCoordinator(backend, TimeProvider.System);
        await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
        await controller.DownloadAsync(TestContext.Current.CancellationToken);
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        backend.ProgressObserved = null;
        backend.Download = _ => complete.Task;
        var retry = controller.DownloadAsync(TestContext.Current.CancellationToken);
        oldProgress!(98);
        Assert.Equal(42, controller.Current.ProgressPercent);
        complete.SetResult();
        await retry;
    }

    [Fact]
    public async Task Concurrent_progress_and_completion_preserve_terminal_ready_state()
    {
        for (var iteration = 0; iteration < 128; iteration++)
        {
            Action<int>? report = null;
            var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var backend = new Backend
            {
                Candidate = NewVersion, ProgressObserved = callback => report = callback, Download = _ => complete.Task,
            };
            using var controller = new UpdateCoordinator(backend, TimeProvider.System);
            await controller.CheckAsync(cancellationToken: TestContext.Current.CancellationToken);
            var download = controller.DownloadAsync(TestContext.Current.CancellationToken);
            using var start = new Barrier(2);
            var progress = Task.Run(() =>
            {
                start.SignalAndWait(TestContext.Current.CancellationToken);
                for (var percent = 0; percent < 100; percent++) report!(percent);
            }, TestContext.Current.CancellationToken);
            start.SignalAndWait(TestContext.Current.CancellationToken);
            complete.SetResult();
            await Task.WhenAll(download, progress).WaitAsync(TestContext.Current.CancellationToken);
            Assert.Equal(UpdateState.Ready, controller.Current.State);
            Assert.Equal(100, controller.Current.ProgressPercent);
        }
    }

    private static readonly UpdateCandidate NewVersion = new("0.3.10", "https://github.com/zab1yak/dynamic-island-windows/releases/tag/v0.3.10");
    private sealed class Backend : IUpdateBackend
    {
        public bool IsInstalled { get; set; } = true;
        public string InstalledVersion => "0.3.9";
        public UpdateCandidate? Candidate { get; set; }
        public Func<Task<UpdateCandidate?>>? Check { get; set; }
        public Func<CancellationToken, Task>? Download { get; set; }
        public Action<Action<int>>? ProgressObserved { get; set; }
        public bool PrepareFails { get; set; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public int Preparations { get; private set; }
        public Task<UpdateCandidate?> CheckAsync() { Checks++; return Check?.Invoke() ?? Task.FromResult(Candidate); }
        public Task DownloadAsync(UpdateCandidate candidate, Action<int> progress, CancellationToken cancellationToken)
        { Downloads++; ProgressObserved?.Invoke(progress); progress(42); return Download?.Invoke(cancellationToken) ?? Task.CompletedTask; }
        public void PrepareForExit(UpdateCandidate candidate)
        { Preparations++; if (PrepareFails) throw new InvalidOperationException("synthetic apply-helper failure"); }
    }
}
