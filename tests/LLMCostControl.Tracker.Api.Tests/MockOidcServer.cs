using System.Net;
using System.Text;

namespace LLMCostControl.Tracker.Api.Tests;

/// <summary>
/// A lightweight HTTP server that serves OIDC discovery and JWKS documents
/// for auth tests. Runs on localhost on a random port.
/// </summary>
public sealed class MockOidcServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly JwtTestHelper _jwtHelper;
    private Task? _serverTask;

    /// <summary>The base URL of the mock server.</summary>
    public string BaseUrl { get; }

    /// <summary>The OIDC discovery document URL.</summary>
    public string DiscoveryUrl => $"{BaseUrl}/.well-known/openid-configuration";

    /// <summary>The JWKS URL.</summary>
    public string JwksUrl => $"{BaseUrl}/.well-known/jwks.json";

    /// <summary>The issuer (matches the discovery document's issuer field).</summary>
    public string Issuer => _jwtHelper.Issuer;

    /// <summary>The expected audience.</summary>
    public string Audience => _jwtHelper.Audience;

    /// <summary>Creates and starts the mock OIDC server.</summary>
    public MockOidcServer(JwtTestHelper jwtHelper)
    {
        _jwtHelper = jwtHelper;
        _listener = new HttpListener();
        _listener.Prefixes.Add("http://localhost:0/");
        // HttpListener doesn't support port 0 for auto-assign; find a free port.
        var port = GetFreePort();
        _listener.Prefixes.Clear();
        _listener.Prefixes.Add($"http://localhost:{port}/");
        BaseUrl = $"http://localhost:{port}";
    }

    /// <summary>Starts the server.</summary>
    public void Start()
    {
        _listener.Start();
        _serverTask = ServeAsync(_cts.Token);
    }

    private async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }

            try
            {
                var path = ctx.Request.Url!.AbsolutePath;
                var json = path switch
                {
                    "/.well-known/openid-configuration" => $$"""
                        {
                          "issuer": "{{_jwtHelper.Issuer}}",
                          "jwks_uri": "{{JwksUrl}}"
                        }
                        """,
                    "/.well-known/jwks.json" => _jwtHelper.GetJwksJson(),
                    _ => "{}",
                };

                ctx.Response.ContentType = "application/json";
                ctx.Response.ContentEncoding = Encoding.UTF8;
                var buffer = Encoding.UTF8.GetBytes(json);
                ctx.Response.ContentLength64 = buffer.Length;
                await ctx.Response.OutputStream.WriteAsync(buffer, ct);
                ctx.Response.Close();
            }
            catch
            {
                try { ctx.Response.Close(); } catch { }
            }
        }
    }

    private static int GetFreePort()
    {
        using var sock = new System.Net.Sockets.Socket(
            System.Net.Sockets.AddressFamily.InterNetwork,
            System.Net.Sockets.SocketType.Stream,
            System.Net.Sockets.ProtocolType.Tcp);
        sock.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)sock.LocalEndPoint!).Port;
    }

    /// <summary>Stops the server and releases resources.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        GC.SuppressFinalize(this);
    }
}
