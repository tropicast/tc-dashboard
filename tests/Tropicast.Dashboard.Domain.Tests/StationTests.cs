using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;

namespace Tropicast.Dashboard.Domain.Tests;

public sealed class StationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Public_ids_are_short_url_safe_and_unique()
    {
        var ids = Enumerable.Range(0, 10_000).Select(_ => StationPublicId.New()).ToList();
        Assert.All(ids, id => Assert.True(StationPublicId.IsValid(id), id));
        Assert.Equal(ids.Count, ids.Distinct().Count());
        // Fits the gateway and Icecast mount pattern ^/stations/[A-Za-z0-9-]{1,64}/live\.(mp3|opus)$.
        Assert.All(ids, id => Assert.Matches("^[a-z0-9]{10}$", id));
    }

    [Theory]
    [InlineData("ABCDEFGHJK")]
    [InlineData("abcdefghi")]
    [InlineData("abcdefghil")]
    [InlineData("abcdefghij/")]
    [InlineData(null)]
    public void Invalid_public_ids_are_rejected(string? id) => Assert.False(StationPublicId.IsValid(id));

    [Fact]
    public void Delete_is_soft_and_idempotent()
    {
        var station = Station.Create(Guid.NewGuid(), "Radio Mada", "radio-mada", Now);
        station.Delete(Now.AddHours(1));
        station.Delete(Now.AddHours(2));
        Assert.True(station.IsDeleted);
        Assert.Equal(Now.AddHours(1), station.DeletedAt);
    }

    [Theory]
    [InlineData("Ab")]
    [InlineData("-radio")]
    [InlineData("radio-")]
    [InlineData("radio--mada")]
    [InlineData("Radio")]
    public void Invalid_slugs_are_rejected(string slug)
        => Assert.ThrowsAny<ArgumentException>(() => Station.Create(Guid.NewGuid(), "Radio", slug, Now));

    [Fact]
    public void Directory_listing_needs_a_plan_that_includes_it()
    {
        var station = Station.Create(Guid.NewGuid(), "Radio", "radio", Now);
        station.SetDirectoryListing(true, Plan.Free);
        Assert.False(station.ListInDirectory);
        station.SetDirectoryListing(true, Plan.Starter);
        Assert.True(station.ListInDirectory);
    }

    [Fact]
    public void Assignment_mounts_follow_the_listener_url_scheme()
    {
        var station = Station.Create(Guid.NewGuid(), "Radio", "radio", Now);
        var assignment = StreamAssignment.Create(station, "tc-stream-1", Now);
        Assert.Equal($"/stations/{station.PublicId}/live.mp3", assignment.Mount(AudioFormat.Mp3));
        Assert.Equal($"/stations/{station.PublicId}/live.opus", assignment.Mount(AudioFormat.Opus));
    }

    [Fact]
    public void Credentials_store_only_a_sha256_hash_and_revoke_once()
    {
        Assert.ThrowsAny<ArgumentException>(() => BroadcastCredential.Create(Guid.NewGuid(), "Studio PC", "plain-secret", Now));
        var credential = BroadcastCredential.Create(Guid.NewGuid(), "Studio PC", new string('a', 64), Now);
        credential.Revoke(Now.AddMinutes(1));
        credential.Revoke(Now.AddMinutes(2));
        Assert.False(credential.IsActive);
        Assert.Equal(Now.AddMinutes(1), credential.RevokedAt);
    }
}
