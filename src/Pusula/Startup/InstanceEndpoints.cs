using System.Buffers.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Pusula.Sources;

namespace Pusula.Startup;

/// <summary>The answer of <c>GET /api/instance</c>.</summary>
/// <param name="Proof">The proof of the challenge, as base64url (see <see cref="InstanceKey.Proof"/>).</param>
internal sealed record InstanceProofResponse(string Proof);

/// <summary>
/// The <c>/api/instance</c> endpoint: how a second pusula, started while this one holds the port, makes sure that it is a pusula of the
/// same user before it opens the page in the browser (see <see cref="InstanceKey"/>).
/// </summary>
internal static class InstanceEndpoints
{
    /// <summary>Maps <c>GET /api/instance</c>.</summary>
    /// <param name="routes">The route builder.</param>
    public static IEndpointRouteBuilder MapInstance(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/instance", GetInstance)
            .WithName("GetInstance")
            .WithSummary("Prove that this server is a pusula of this user")
            .WithDescription(
                "Used by a second pusula that finds the port taken, to tell a pusula of the same user from another program that "
                + "listens on the port. 'challenge' is the base64url (no padding, 43 characters) of 32 random bytes. The answer is "
                + "'proof': the base64url of the HMAC-SHA256, with the instance key of this user, of the text "
                + "'pusula-instance-proof-v1', a line feed, the challenge as it was sent, a line feed and the endpoint of the "
                + "connection the request arrived at ('127.0.0.1:5190', '[::1]:5190'; an IPv4 address written as IPv6 is the IPv4 "
                + "one). The instance key is the file 'instance-key' in pusula's state folder ($XDG_STATE_HOME/pusula or "
                + "~/.local/state/pusula on Linux and macOS, %LOCALAPPDATA%\\pusula on Windows): the base64 of 32 random bytes, "
                + "made by a pusula that opens the browser and readable by the user alone; it is read at every request. Only a "
                + "program that can read the key can give the proof, and the endpoint in it ties the proof to the address it was "
                + "given at. Only a request from the machine the server runs on is answered, whatever Pusula:AllowRemoteEdit says "
                + "(a request that a reverse proxy forwarded is not from this machine: a Forwarded, X-Forwarded-For, "
                + "X-Forwarded-Host or X-Real-IP header). The answer is never cached. Errors are problem details whose 'code' says "
                + "what is wrong: 400 InvalidChallenge (missing, not base64url, or not 32 bytes); 403 Remote; 404 NoKey (this "
                + "pusula has no key that can be trusted: it was not started to open the browser, has no state folder, or the "
                + "key file or its folder can be changed by others).")
            .WithTags("Instance")
            .Produces<InstanceProofResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);
        return routes;
    }

    internal static Results<Ok<InstanceProofResponse>, ProblemHttpResult> GetInstance(HttpContext http, string? challenge, UserDirectories directories)
    {
        // No answer is cached, the refusals included.
        http.Response.Headers.CacheControl = "no-store";

        if (!SourceEditAccess.IsSameMachine(http))
        {
            return Problem(StatusCodes.Status403Forbidden, "Remote", "Not from this machine", "Only a request from the machine the server runs on is answered.");
        }

        if (!InstanceKey.IsChallenge(challenge))
        {
            return Problem(StatusCodes.Status400BadRequest, "InvalidChallenge", "Invalid challenge", "'challenge' must be the base64url of 32 bytes.");
        }

        byte[]? key = InstanceKey.Read(directories.State);
        if (key is null || http.Connection.LocalIpAddress is not { } local)
        {
            return Problem(StatusCodes.Status404NotFound, "NoKey", "No instance key", "This pusula has no instance key that can be trusted.");
        }

        string endpoint = InstanceKey.EndpointOf(local, http.Connection.LocalPort);
        return TypedResults.Ok(new InstanceProofResponse(Base64Url.EncodeToString(InstanceKey.Proof(key, challenge!, endpoint))));
    }

    private static ProblemHttpResult Problem(int status, string code, string title, string detail) =>
        TypedResults.Problem(title: title, detail: detail, statusCode: status, extensions: new Dictionary<string, object?> { ["code"] = code });
}
