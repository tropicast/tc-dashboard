using System.Text.Json;
using Tropicast.Dashboard.Application.Directories;
using Tropicast.Dashboard.Application.Provisioning;
using Tropicast.Dashboard.Domain.Plans;
using Tropicast.Dashboard.Domain.Stations;

namespace Tropicast.Dashboard.Application.Tests;

public sealed class DirectoryTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 11, 9, 0, 0, TimeSpan.Zero);
    private static readonly Uri Stream = new("https://listen.tropicastradio.com/stations/k3m9x2p7qa/live.mp3");
    private static readonly Uri Page = new("https://app.tropicastradio.com/listen/mada/radio-mada");

    private static Station Station(bool listed = true, string? country = "mg", string? language = "fr-MG", Uri? website = null)
    {
        var station = Domain.Stations.Station.Create(Guid.NewGuid(), "Radio Mada", "radio-mada", Now);
        station.Describe("Salegy\nall\u0085day\u2028.", "Salegy / Hira Gasy; Salegy", country, language, new Uri("https://cdn.example.test/logo.png"),
            website, Now);
        station.SetDirectoryListing(listed, Plan.Starter, Now);
        return station;
    }

    [Fact]
    public void A_listed_station_sends_only_public_metadata()
    {
        var entry = DirectoryEntry.For(Station(), Plan.Starter, submitted: false, Stream, Page)!;

        Assert.True(entry.Listed);
        Assert.Equal(("Radio Mada", "salegy,hira gasy", "MG", "fr"), (entry.Name, entry.Genre, entry.CountryCode, entry.LanguageCodes));
        Assert.Equal(Stream, entry.StreamUrl);
        // No website: the public page is the homepage.
        Assert.Equal(Page, entry.Homepage);
        Assert.Null(entry.Problem);
    }

    [Fact]
    public void Not_listed_and_never_submitted_has_no_entry_but_a_submitted_one_is_delisted()
    {
        Assert.Null(DirectoryEntry.For(Station(listed: false), Plan.Starter, submitted: false, Stream, Page));
        Assert.False(DirectoryEntry.For(Station(listed: false), Plan.Starter, submitted: true, Stream, Page)!.Listed);
        // The plan lost directory listing.
        Assert.False(DirectoryEntry.For(Station(), Plan.Free, submitted: true, Stream, Page)!.Listed);
        Assert.Null(DirectoryEntry.For(Station(), Plan.Free, submitted: false, Stream, Page));
    }

    [Fact]
    public void A_station_without_country_cannot_be_submitted()
    {
        var entry = DirectoryEntry.For(Station(country: null, language: "zz"), Plan.Starter, false, Stream, Page)!;
        Assert.Contains("country", entry.Problem, StringComparison.Ordinal);
        // Not an ISO 639-1 code: left out rather than refused by the node.
        Assert.Null(entry.LanguageCodes);
        Assert.Null(DirectoryEntry.For(Station(language: "haw"), Plan.Starter, false, Stream, Page)!.LanguageCodes);
    }

    [Fact]
    public void Stations_json_carries_the_directory_metadata_for_the_node()
    {
        var listed = DirectoryEntry.For(Station(), Plan.Starter, false, Stream, Page);
        var delisted = DirectoryEntry.For(Station(listed: false), Plan.Starter, true, Stream, Page);

        var json = StationLimitsDocument.Render([new StationLimits("aaa", Plan.Starter, listed), new StationLimits("bbb", Plan.Starter, delisted),
            new StationLimits("ccc", Plan.Free)], 100).Json;

        using var document = JsonDocument.Parse(json);
        var stations = document.RootElement.GetProperty("stations");
        var directory = stations.GetProperty("aaa").GetProperty("directory");
        Assert.True(directory.GetProperty("listed").GetBoolean());
        Assert.Equal("Salegy all day .", directory.GetProperty("description").GetString());
        Assert.Equal("MG", directory.GetProperty("country_code").GetString());
        Assert.Equal(Stream.AbsoluteUri, directory.GetProperty("main_stream_url").GetString());
        Assert.Equal("""{"listed":false}""", stations.GetProperty("bbb").GetProperty("directory").GetRawText().Replace(" ", "", StringComparison.Ordinal)
            .Replace("\n", "", StringComparison.Ordinal));
        Assert.False(stations.GetProperty("ccc").TryGetProperty("directory", out _));
    }

    [Fact]
    public void Values_the_node_would_refuse_are_left_out()
    {
        var station = Station(country: "1A", website: new Uri("https://example.test/" + new string('a', 600)));
        var entry = DirectoryEntry.For(station, Plan.Starter, false, Stream, Page)!;
        Assert.Null(entry.CountryCode);

        var json = StationLimitsDocument.Render([new StationLimits("aaa", Plan.Starter, entry)], 100).Json;
        using var document = JsonDocument.Parse(json);
        var directory = document.RootElement.GetProperty("stations").GetProperty("aaa").GetProperty("directory");
        Assert.False(directory.TryGetProperty("homepage", out _));
        Assert.False(directory.TryGetProperty("country_code", out _));
    }
}
