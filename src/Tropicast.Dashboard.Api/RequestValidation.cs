using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace Tropicast.Dashboard.Api;

/// <summary>
/// Validates request records (<c>…Request</c> types) with their DataAnnotations before the handler runs:
/// a missing or invalid field answers a 400 validation problem instead of reaching Identity or the database.
/// </summary>
internal sealed class RequestValidation : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is null || !argument.GetType().Name.EndsWith("Request", StringComparison.Ordinal))
            {
                continue;
            }
            var results = new List<ValidationResult>();
            if (!Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
            {
                return TypedResults.ValidationProblem(results
                    .SelectMany(r => r.MemberNames.DefaultIfEmpty("").Select(member => (Member: member, r.ErrorMessage)))
                    .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.Member), e => e.ErrorMessage ?? "Invalid value.")
                    .ToDictionary(g => g.Key, g => g.ToArray()));
            }
        }
        return await next(context);
    }
}
