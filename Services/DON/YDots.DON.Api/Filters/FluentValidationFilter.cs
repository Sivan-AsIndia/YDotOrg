using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using YDots.DON.Application.Common.Abstractions.Security;
using YDots.DON.Application.Common.Results;

namespace YDots.DON.Api.Filters;

/// <summary>
/// Runs FluentValidation for every request DTO before the action executes.
///
/// One filter means no handler has to remember to validate, and every field error comes back in
/// exactly the same VALIDATION_FAILED shape. Field names are camel-cased so they match the
/// property names the Angular form already binds to.
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider serviceProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var errors = new List<ValidationError>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());
            if (serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                errors.AddRange(result.Errors.Select(failure =>
                    new ValidationError(ToCamelCase(failure.PropertyName), failure.ErrorMessage)));
            }
        }

        if (errors.Count > 0)
        {
            var correlationId = context.HttpContext.RequestServices
                .GetRequiredService<ICurrentUser>().CorrelationId;

            var error = Error.Validation("Review the highlighted fields before continuing.", errors);

            context.Result = new ObjectResult(ApiResponse.Fail(error, correlationId)) { StatusCode = error.StatusCode };
            return;
        }

        await next();
    }

    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return propertyName;
        }

        var parts = propertyName.Split('.');
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 0)
            {
                parts[index] = char.ToLowerInvariant(parts[index][0]) + parts[index][1..];
            }
        }

        return string.Join('.', parts);
    }
}
