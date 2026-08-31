using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace YDot.IAM.Api.ServiceContainer;

/// <summary>
/// Adds the development Organisation header to every operation in Swagger.
///
/// WHY THIS IS SAFE TO ADVERTISE: the server honours <c>X-Tenant</c> ONLY when the request
/// arrives from loopback AND the setting permits it. Off a developer machine the header is
/// ignored entirely, so documenting it does not widen anything — see TenantResolutionMiddleware.
///
/// Without it, exercising a Tenant endpoint from Swagger means editing the hosts file so that
/// ten1.localhost resolves, which is a poor first five minutes for anybody picking the service up.
/// </summary>
public sealed class TenantHeaderOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        ArgumentNullException.ThrowIfNull(operation);

        operation.Parameters ??= [];

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Tenant",
            In = ParameterLocation.Header,
            Required = false,
            Description =
                "Development only, loopback only: the Organisation subdomain to operate in " +
                "(for example \"ten1\"). Ignored on any non-loopback request, and always " +
                "outranked by the tenant_id claim on a signed token.",
            Schema = new OpenApiSchema
            {
                Type = JsonSchemaType.String,
                Example = "ten1"
            }
        });
    }
}
