namespace Pusula.Sources;

/// <summary>The folder to add as a source.</summary>
public sealed record CreateSourceRequest
{
    /// <summary>Required. The path of the folder: absolute, or starting with <c>~</c> for the home directory of the user the server runs as. It must exist, and it cannot be the root of the file system or the home directory itself.</summary>
    public string? Path { get; init; }

    /// <summary>The name to show for the source; the name of the folder when it is left out or blank. At most 80 characters, no control characters.</summary>
    public string? Name { get; init; }

    /// <summary>How the folder is scanned: <c>auto</c> (the default: decided by what the folder holds), <c>claude</c>, <c>vault</c> or <c>markdown</c>, in any case.</summary>
    public string? Profile { get; init; }
}
