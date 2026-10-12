namespace Pusula.Sources;

/// <summary>Which Claude Code configuration a source is (see <c>SourceEndpoints.ScopeOf</c>); the API says it as text.</summary>
internal enum ClaudeScope
{
    /// <summary>The configuration of the user: the <c>.claude</c> folder in the home directory, which every session loads.</summary>
    Global,

    /// <summary>The configuration of a project: a folder called <c>.claude</c> anywhere else, which the sessions in that project load.</summary>
    Project,
}
