using System.Globalization;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using DynamicIsland.Core.Island;
using DynamicIsland.UI.Localization;

namespace DynamicIsland.App.Island;

/// <summary>State shown by the island chrome itself (clock, date, mode).</summary>
internal sealed partial class IslandViewModel : ObservableObject, IDisposable
{
    private readonly TimeProvider _time;
    private readonly DispatcherTimer _clock;

    [ObservableProperty]
    private string _time24 = string.Empty;

    [ObservableProperty]
    private string _dateText = string.Empty;

    [ObservableProperty]
    private IslandMode _mode = IslandMode.Compact;

    public IslandViewModel(TimeProvider time, StatusViewModel status, LiveActivityHost live, ModuleHost modules)
    {
        _time = time;
        Status = status;
        Live = live;
        Modules = modules;
        modules.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ModuleHost.Pages))
            {
                OnPropertyChanged(nameof(HasSeveralPages));
            }
        };
        // One tick per minute boundary, not a polling timer: the island sleeps between minutes.
        _clock = new DispatcherTimer(DispatcherPriority.Background);
        _clock.Tick += (_, _) => RefreshClock();
        Loc.Instance.CultureChanged += OnCultureChanged;
        RefreshClock();
    }

    /// <summary>Battery and privacy indicators.</summary>
    public StatusViewModel Status { get; }

    /// <summary>Island pages.</summary>
    public ModuleHost Modules { get; }

    /// <summary>Whether arrows and dots make sense.</summary>
    public bool HasSeveralPages => Modules.Pages.Count > 1;

    /// <summary>Live activity shown in the pill.</summary>
    public LiveActivityHost Live { get; }

    /// <summary>Re-reads the clock (after sleep or a time-zone change).</summary>
    public void RefreshClock()
    {
        var now = _time.GetLocalNow();
        var culture = Loc.Instance.Culture;
        Time24 = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        var date = now.ToString("ddd, d MMMM", culture);
        DateText = string.Concat(char.ToUpper(date[0], culture).ToString(), date.AsSpan(1));

        _clock.Stop();
        _clock.Interval = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(20);
        _clock.Start();
    }

    public void Dispose()
    {
        _clock.Stop();
        Loc.Instance.CultureChanged -= OnCultureChanged;
    }

    private void OnCultureChanged(object? sender, EventArgs e) => RefreshClock();
}
