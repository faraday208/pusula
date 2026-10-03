namespace Pusula.Indexing;

/// <summary>When Claude Code loads a file into the context window.</summary>
public enum LoadMode
{
    /// <summary>The whole file is loaded at the start of every session.</summary>
    EverySession,

    /// <summary>Only the name and description are loaded every session; the body is loaded on use.</summary>
    DescriptionEverySession,

    /// <summary>The whole file is loaded every session, but only inside its own project.</summary>
    ProjectSession,

    /// <summary>The file is loaded when a file matching its <c>paths</c> globs is read.</summary>
    Conditional,

    /// <summary>The file is loaded only when it is explicitly requested.</summary>
    OnDemand,

    /// <summary>The file is loaded only when the user invokes it.</summary>
    UserInvoked,

    /// <summary>The file is not loaded (for example an output style that is not selected).</summary>
    Inactive,
}
