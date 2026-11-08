using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace SmsService.Api.Validation;

/// <summary>
/// Runs the DataAnnotations attributes of the <typeparamref name="T"/> argument before the endpoint,
/// and rejects the request through <see cref="InvalidInput"/>: a 400 with all format errors per field, also logged.
/// Used instead of the built-in AddValidation(), which reports keys in C# casing ("To") while
/// the JSON fields, and the errors MessageService returns, are camelCase ("to").
/// </summary>
public sealed class ValidationFilter<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (context.Arguments.OfType<T>().FirstOrDefault() is not { } argument)
            return await next(context);

        var results = new List<ValidationResult>();
        if (Validator.TryValidateObject(argument, new ValidationContext(argument), results, validateAllProperties: true))
            return await next(context);

        var errors = results
            .SelectMany(r => r.MemberNames.Select(member => (Field: JsonNamingPolicy.CamelCase.ConvertName(member), r.ErrorMessage)))
            .GroupBy(e => e.Field)
            .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage ?? "").ToArray());

        return InvalidInput.Reject(context.HttpContext, errors);
    }
}
