using System.Reflection;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using YDot.IAM.Application.Common.Results;

namespace YDot.IAM.Api.Filters;

/// <summary>
/// Runs the FluentValidation validator for every action argument that has one, before the
/// action itself.
///
/// WHY A FILTER RATHER THAN A CALL IN EACH HANDLER. A validator invoked by hand is a validator
/// somebody eventually forgets. Doing it here means a new endpoint is validated the moment its
/// request type has a validator, with nothing to remember — and the failure comes back in the
/// same envelope as every other error.
///
/// IT ALSO OWNS BINDING FAILURES, and that part is not optional. The framework's automatic 400
/// is switched off in ServiceContainer so that every failure shares one envelope — but with it
/// off, a body that cannot be parsed (malformed JSON, "" where a Guid belongs, an unknown enum
/// name) arrives at the action as a NULL argument and the handler dereferences it. That is a
/// 500 for what is plainly a client mistake. Checking ModelState here is what turns it back
/// into the 400 it always was.
/// </summary>
public sealed class FluentValidationFilter(IServiceProvider serviceProvider) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        // ---- Binding first --------------------------------------------------------------
        //
        // Before any validator runs: a request that did not bind has nothing meaningful to
        // validate, and its arguments are null.
        if (!context.ModelState.IsValid)
        {
            var bindingFailures = context.ModelState
                .Where(entry => entry.Value?.Errors.Count > 0)
                .SelectMany(entry => entry.Value!.Errors.Select(modelError =>
                    new ValidationError(
                        ToCamelCase(NormaliseKey(entry.Key)),
                        DescribeBindingError(modelError.ErrorMessage))))
                .ToList();

            Reject(context, "The request could not be read.", bindingFailures);
            return;
        }

        var failures = new List<ValidationError>();

        foreach (var argument in context.ActionArguments.Values.Where(value => value is not null))
        {
            var validatorType = typeof(IValidator<>).MakeGenericType(argument!.GetType());

            if (serviceProvider.GetService(validatorType) is not IValidator validator)
            {
                continue;
            }

            var validationContext = new ValidationContext<object>(argument);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                // EVERY failure is collected, from every argument, rather than stopping at the
                // first. Sending them one at a time makes people fix a form four times.
                failures.AddRange(result.Errors.Select(
                    failure => new ValidationError(ToCamelCase(failure.PropertyName), failure.ErrorMessage)));
            }
        }

        if (failures.Count > 0)
        {
            Reject(context, "Some of the details are not valid.", failures);
            return;
        }

        // A required body that bound to null WITHOUT a model-state error - an entirely absent
        // body on a [FromBody] parameter behaves this way. Caught here so the handler is never
        // handed a null it is not written to expect.
        var missing = context.ActionDescriptor.Parameters
            .Where(parameter =>
                context.ActionArguments.TryGetValue(parameter.Name, out var value)
                && value is null
                && !IsOptional(parameter))
            .Select(parameter => new ValidationError(ToCamelCase(parameter.Name), "A value is required."))
            .ToList();

        if (missing.Count > 0)
        {
            Reject(context, "The request body is missing.", missing);
            return;
        }

        await next();
    }

    private static void Reject(
        ActionExecutingContext context, string message, IReadOnlyList<ValidationError> failures)
    {
        var error = Error.Validation(message, failures);

        var correlationId = context.HttpContext.Items["CorrelationId"] as string
                            ?? context.HttpContext.TraceIdentifier;

        context.Result = new ObjectResult(ApiResponse.Fail(error, correlationId))
        {
            StatusCode = error.StatusCode
        };
    }

    /// <summary>
    /// A nullable parameter, or one with a default, is allowed to arrive null - several
    /// endpoints take an optional body such as <c>{ "reason": "..." }</c>.
    /// </summary>
    private static bool IsOptional(ParameterDescriptor parameter) =>
        parameter is not ControllerParameterDescriptor descriptor
        || descriptor.ParameterInfo.HasDefaultValue
        || new NullabilityInfoContext().Create(descriptor.ParameterInfo).WriteState
            != NullabilityState.NotNull;

    /// <summary>
    /// System.Text.Json reports the path as "$.tenantId"; the client knows the field as
    /// "tenantId". Stripping the prefix is what lets the Angular form bind the message to the
    /// control that produced it.
    /// </summary>
    private static string NormaliseKey(string key) =>
        string.IsNullOrEmpty(key) ? "request"
        : key.StartsWith("$.", StringComparison.Ordinal) ? key[2..]
        : key == "$" ? "request"
        : key;

    /// <summary>
    /// The framework's binding messages name .NET types and JSON paths. Useful in a log, not on
    /// a form, so the common cases are restated in the terms the caller sent.
    /// </summary>
    private static string DescribeBindingError(string message) =>
        string.IsNullOrWhiteSpace(message)
            ? "The value is not in the expected format."
            : message.Contains("could not be converted", StringComparison.OrdinalIgnoreCase)
                ? "The value is not in the expected format."
                : message.Contains("is invalid", StringComparison.OrdinalIgnoreCase)
                    ? "The value is not valid."
                    : message;

    /// <summary>
    /// "MobileNumber" becomes "mobileNumber", matching the JSON the client sent — so a field
    /// error binds straight back to the form control that produced it.
    ///
    /// Nested paths such as "DataScopes[0].ScopeValue" are converted segment by segment, which
    /// is what Angular reactive forms expect for a nested control.
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
