using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Tropicast.Dashboard.Application.Stations;
using Tropicast.Dashboard.Domain.Plans;

namespace Tropicast.Dashboard.Application.Tests;

public sealed class StationRulesTests
{
    private static readonly IValidator<CreateStationCommand> Create = new ServiceCollection().AddApplication()
        .BuildServiceProvider().GetRequiredService<IValidator<CreateStationCommand>>();

    [Fact]
    public void A_complete_station_is_valid()
        => Assert.True(Create.Validate(new CreateStationCommand("Radio Mada", "radio-mada", "News", "Talk", "MG", "mg-MG",
            new Uri("https://cdn.example.test/logo.png"), new Uri("https://radio.example.test"))).IsValid);

    [Theory]
    [InlineData("", "radio", null, null, nameof(CreateStationCommand.Name))]
    [InlineData("Radio", "Radio Mada", null, null, nameof(CreateStationCommand.Slug))]
    [InlineData("Radio", "radio", "MDG", null, nameof(CreateStationCommand.Country))]
    [InlineData("Radio", "radio", null, "malagasy language", nameof(CreateStationCommand.Language))]
    public void Invalid_fields_are_named(string name, string slug, string? country, string? language, string field)
    {
        var result = Create.Validate(new CreateStationCommand(name, slug, Country: country, Language: language));
        Assert.Contains(result.Errors, e => e.PropertyName == field);
    }

    [Fact]
    public void Only_web_addresses_are_accepted()
        => Assert.False(Create.Validate(new CreateStationCommand("Radio", "radio", Website: new Uri("ftp://example.test"))).IsValid);

    [Theory]
    [InlineData("starter", 0, true)]
    [InlineData("starter", 1, false)]
    [InlineData("growth", 2, true)]
    [InlineData("growth", 3, false)]
    public void The_plan_caps_the_station_count(string planId, int existing, bool allowed)
        => Assert.Equal(allowed, StationQuota.AllowsAnother(Plan.Catalogue.Single(p => p.Id == planId), existing));
}
