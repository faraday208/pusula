namespace Pusula.Sources;

/// <summary>Why a request cannot add or remove sources. The name is what <c>editBlocked</c> of <c>GET /api/sources</c> says.</summary>
internal enum EditBlock
{
    /// <summary>The request does not come from the machine the server runs on (sources can only be changed from there), or a reverse proxy forwarded it.</summary>
    Remote,

    /// <summary>The folders were named on the command line or in <c>Pusula:Root</c>, so there is no sources file to change.</summary>
    CommandLine,
}
