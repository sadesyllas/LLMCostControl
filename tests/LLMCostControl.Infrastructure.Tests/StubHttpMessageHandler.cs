using System.Net;
using System.Text;

namespace LLMCostControl.Infrastructure.Tests;

/// <summary>
/// Stub <see cref="HttpMessageHandler"/> that returns a fixed JSON body, or
/// throws to simulate a fetch failure.
/// </summary>
public class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly string _responseBody;
    private bool _throwOnRequest;

    /// <summary>Creates a handler that returns the given JSON body.</summary>
    public StubHttpMessageHandler(string responseBody)
    {
        _responseBody = responseBody;
        _throwOnRequest = false;
    }

    /// <summary>Creates a handler that throws on every request (simulates fetch failure).</summary>
    public static StubHttpMessageHandler Throwing() => new(null!) { _throwOnRequest = true };

    /// <summary>Returns the stub response or throws.</summary>
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_throwOnRequest)
        {
            throw new HttpRequestException("Simulated fetch failure.");
        }

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_responseBody, Encoding.UTF8, "application/json"),
        };

        return Task.FromResult(response);
    }
}
