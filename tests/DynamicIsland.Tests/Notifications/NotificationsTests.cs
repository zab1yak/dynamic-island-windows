using DynamicIsland.Modules.Notifications;
using DynamicIsland.Modules.Notifications.Services;
using DynamicIsland.Platform.Windows.Packaging;

namespace DynamicIsland.Tests.Notifications;

public sealed class NotificationsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false, NotificationAccess.Unavailable, false, NotificationsPageState.NeedsSetup)]
    [InlineData(false, NotificationAccess.Unavailable, true, NotificationsPageState.NeedsRestart)]
    [InlineData(true, NotificationAccess.Unknown, true, NotificationsPageState.NeedsPermission)]
    [InlineData(true, NotificationAccess.Denied, true, NotificationsPageState.Denied)]
    [InlineData(true, NotificationAccess.Allowed, true, NotificationsPageState.Ready)]
    [InlineData(true, NotificationAccess.Unavailable, true, NotificationsPageState.NeedsSetup)]
    public void Page_state_follows_identity_and_access(bool identity, NotificationAccess access, bool registered, NotificationsPageState expected) =>
        Assert.Equal(expected, NotificationsViewModel.DecideState(identity, access, registered));

    [Fact]
    public void Cleared_notifications_stay_hidden_and_newer_ones_show_newest_first()
    {
        NotificationEntry[] entries =
        [
            new(1, "Telegram", "Anna", "hi", T0.AddMinutes(-10)),
            new(2, "Telegram", "Boris", "ok", T0.AddMinutes(5)),
            new(3, "Mail", "News", "x", T0.AddMinutes(1)),
        ];

        Assert.Equal([2u, 3u, 1u], NotificationHistory.Visible(entries, null).Select(e => e.Id));
        Assert.Equal([2u, 3u], NotificationHistory.Visible(entries, T0).Select(e => e.Id));
        Assert.Empty(NotificationHistory.Visible(entries, T0.AddHours(1)));
    }

    [Fact]
    public void List_is_limited()
    {
        var many = Enumerable.Range(0, 100).Select(i => new NotificationEntry((uint)i, "A", "T", "B", T0.AddSeconds(i)));
        Assert.Equal(NotificationHistory.Limit, NotificationHistory.Visible(many, null).Count);
    }

    [Theory]
    [InlineData(unchecked((int)0x800B0109), true)]
    [InlineData(unchecked((int)0x800B010A), true)]
    [InlineData(unchecked((int)0x80073CFF), false)]
    [InlineData(unchecked((int)0x80073CF3), false)]
    [InlineData(0, false)]
    public void Only_certificate_chain_errors_lead_to_the_trust_step(int hresult, bool untrusted) =>
        Assert.Equal(untrusted, PackageIdentityService.IsUntrustedCertificate(hresult));

    [Fact]
    public void Swapped_identity_files_are_refused()
    {
        var someCertificate = new byte[] { 1, 2, 3 };
        var someHash = System.Security.Cryptography.SHA256.HashData(new byte[] { 4, 5, 6 });
        Assert.False(PackageIdentityService.MatchesPinned(someCertificate, someHash));
    }
}
