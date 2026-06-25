using System.Net;

namespace LLMCostControl.Tracker.Api.Endpoints;

/// <summary>
/// Endpoint filter that restricts an endpoint to loopback (localhost) callers
/// only (§8.3). Requests whose remote IP is missing or not a loopback address
/// receive <c>404 Not Found</c>, so the endpoint is not reachable — nor
/// discoverable — from any non-localhost address.
/// </summary>
public sealed class LocalhostOnlyEndpointFilter : IEndpointFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var remoteIp = context.HttpContext.Connection.RemoteIpAddress;

        if (remoteIp is null || !IPAddress.IsLoopback(remoteIp))
        {
            return Results.NotFound();
        }

        return await next(context);
    }
}
