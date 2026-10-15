using Tropicast.Dashboard.Domain.Media;

namespace Tropicast.Dashboard.Domain.Tests;

public sealed class AutoDjTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_playlist_keeps_the_given_order_and_repeats()
    {
        var playlist = Playlist.Create(Guid.NewGuid(), "Night", shuffle: false, Now);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();

        playlist.SetTracks([b, a, b], Now.AddMinutes(1));

        Assert.Equal([b, a, b], playlist.Items.OrderBy(i => i.Position).Select(i => i.TrackId));
        Assert.Equal([0, 1, 2], playlist.Items.Select(i => i.Position));
        Assert.Equal(Now.AddMinutes(1), playlist.UpdatedAt);
        Assert.Throws<ArgumentOutOfRangeException>(() => playlist.SetTracks([.. Enumerable.Repeat(a, Playlist.MaxItems + 1)], Now));
    }

    [Fact]
    public void Autodj_needs_a_playlist_to_turn_on_and_records_only_real_changes()
    {
        var settings = AutoDjSettings.For(Guid.NewGuid(), Now);
        var playlist = Guid.NewGuid();

        Assert.Throws<InvalidOperationException>(() => settings.Set(true, null, Now));
        settings.Set(true, playlist, Now);
        settings.Set(true, playlist, Now);
        settings.Set(false, playlist, Now);

        Assert.Equal([true, false], settings.DomainEvents.OfType<AutoDjChanged>().Select(e => e.Enabled));
    }

    [Fact]
    public void A_track_is_confirmed_once_with_what_the_server_read()
    {
        var track = Track.StartUpload(Guid.NewGuid(), "song.mp3", 100, Now, TimeSpan.FromMinutes(30));
        Assert.Equal((TrackStatus.Pending, Now.AddMinutes(30)), (track.Status, track.UploadExpiresAt!.Value));

        track.Confirm(TrackFormat.Flac, TimeSpan.FromSeconds(2.5), 2048, new string('t', 300), "  Artist ");

        Assert.Equal((TrackStatus.Ready, 2500L, 2048L, 200, "Artist"), (track.Status, track.DurationMs!.Value, track.SizeBytes, track.Title.Length, track.Artist));
        Assert.Null(track.UploadExpiresAt);
        Assert.Throws<InvalidOperationException>(() => track.Confirm(TrackFormat.Mp3, TimeSpan.FromSeconds(1), 1, "", ""));
    }
}
