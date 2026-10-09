using System.Text.Json;
using FluentValidation;

namespace Tropicast.Dashboard.Api;

/// <summary>Runs the Application validator registered for each argument; failures answer a 400 validation problem.</summary>
internal sealed class CommandValidation : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        foreach (var argument in context.Arguments)
        {
            if (argument is null
                || context.HttpContext.RequestServices.GetService(typeof(IValidator<>).MakeGenericType(argument.GetType())) is not IValidator validator)
            {
                continue;
            }
            var result = await validator.ValidateAsync(new ValidationContext<object>(argument), context.HttpContext.RequestAborted);
            if (!result.IsValid)
            {
                return TypedResults.ValidationProblem(result.Errors
                    .GroupBy(e => JsonNamingPolicy.CamelCase.ConvertName(e.PropertyName), e => e.ErrorMessage)
                    .ToDictionary(g => g.Key, g => g.ToArray()));
            }
        }
        return await next(context);
    }
}
