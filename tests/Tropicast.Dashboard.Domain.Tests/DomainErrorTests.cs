
namespace Tropicast.Dashboard.Domain.Tests;

public sealed class DomainErrorTests
{
    [Fact]
    public void Errors_with_the_same_code_and_description_are_equal()
        => Assert.Equal(new DomainError("station.not_found", "Station not found."), new DomainError("station.not_found", "Station not found."));

    [Theory]
    [InlineData("", "Description")]
    [InlineData(" ", "Description")]
    [InlineData("code", "")]
    public void Code_and_description_are_required(string code, string description)
        => Assert.ThrowsAny<ArgumentException>(() => new DomainError(code, description));
}
