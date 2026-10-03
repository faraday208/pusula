using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace Pusula.IntegrationTests.Support;

/// <summary>
/// Gives every request of the in-memory test server the connection that a real one has: the address of the client and
/// the address of the server it connected to, which the test server leaves empty. A test says who is calling with two
/// request headers: <c>X-Test-Remote-Ip</c> and <c>X-Test-Local-Ip</c>. Without them the request comes from the
/// loopback address, like one from a browser on the machine the server runs on; the word <c>none</c> leaves an
/// address out, like a connection that has none.
/// </summary>
internal sealed class SimulatedConnection : IStartupFilter
{
    public const string RemoteHeader = "X-Test-Remote-Ip";
    public const string LocalHeader = "X-Test-Local-Ip";
    public const string None = "none";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            app.Use((context, pipeline) =>
            {
                context.Connection.RemoteIpAddress = Read(context, RemoteHeader);
                context.Connection.LocalIpAddress = Read(context, LocalHeader);
                return pipeline(context);
            });
            next(app);
        };

    private static IPAddress? Read(HttpContext context, string header)
    {
        string value = context.Request.Headers[header].ToString();
        if (value.Length == 0)
        {
            return IPAddress.Loopback;
        }

        return string.Equals(value, None, StringComparison.Ordinal) ? null : IPAddress.Parse(value);
    }
}
