using Pusula.Indexing;

namespace Pusula.Sources;

/// <summary>A folder to show, as it is configured.</summary>
/// <param name="Id">The part of the URL after <c>/api/sources/</c>: lowercase letters and digits joined by single hyphens, unique among the sources.</param>
/// <param name="Name">The name that is shown for the source.</param>
/// <param name="Path">Full path of the folder.</param>
/// <param name="Profile">How the folder is scanned and its links are resolved; never decided by "auto" any more.</param>
/// <param name="IsRequired">True for a folder the user named on the command line or in <c>Pusula:Root</c>: when it does not exist the application does not start. A folder of a sources file only makes its source unavailable.</param>
internal sealed record SourceDefinition(string Id, string Name, string Path, SourceProfile Profile, bool IsRequired = false);
