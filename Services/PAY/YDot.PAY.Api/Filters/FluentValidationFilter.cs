using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using YDot.PAY.Application.Common.Results;

namespace YDot.PAY.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator for every action argument that has one, before the
/// action executes.
///
/// WHY A FILTER RATHER THAN A CALL IN EACH HANDLER. A validator invoked by the handler is one a
/// handler can forget to invoke, and the endpoint that forgets is the one nobody reviews. A
/// filter applies to every action by construction, so a request body reaching a handler has
/// always been validated.
///
/// THE VALIDATOR IS RESOLVED BY THE ARGUMENT'S TYPE, so an action with no validator for its
/// body simply passes through - which is the right behaviour for the lifecycle endpoints whose
/// only real rule lives in the handler.
///
/// IT WRITES THE SAME ENVELOPE as everything else, so a validation failure and a business
/// refusal are the same shape to the client. Without that the Angular error handler would need
/// a second code path for exactly one kind of failure.
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider services) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        var errors = new List<ValidationError>();

        foreach (var argument in context.ActionArguments.Values)
        {
            if (argument is null)
            {
                continue;
            }

            var validatorType = typeof(IValidator<>).MakeGenericType(argument.GetType());

            if (services.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                // Camel-cased to match the JSON the client sent, so a form can attach each
                // message to the control it came from without translating names.
                errors.AddRange(result.Errors.Select(failure => new ValidationError(
                    ToCamelCase(failure.PropertyName), failure.ErrorMessage)));
            }
        }

        if (errors.Count > 0)
        {
            var correlationId = context.HttpContext.Items["CorrelationId"] as string
                                ?? context.HttpContext.TraceIdentifier;

            var error = Error.Validation("Some of the details are not valid.", errors);

            context.Result = new ObjectResult(ApiResponse.Fail(error, correlationId))
            {
                StatusCode = error.StatusCode
            };

            return;
        }

        await next();
    }

    /// <summary>
    /// "OwnerIds" becomes "ownerIds"; "Places[0].PlaceName" becomes "places[0].placeName".
    ///
    /// The nested case matters: FluentValidation reports a child collection failure with the
    /// full path, and a form binding to that path needs every segment camel-cased, not just the
    /// first.
    /// </summary>
    private static string ToCamelCase(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return propertyName;
        }

        var segments = propertyName.Split('.');

        for (var index = 0; index < segments.Length; index++)
        {
            var segment = segments[index];

            if (segment.Length > 0 && char.IsUpper(segment[0]))
            {
                segments[index] = char.ToLowerInvariant(segment[0]) + segment[1..];
            }
        }

        return string.Join('.', segments);
    }
}
