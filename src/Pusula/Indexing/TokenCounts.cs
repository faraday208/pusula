namespace Pusula.Indexing;

/// <summary>Estimated token counts of one file (see <see cref="TokenEstimator"/>).</summary>
/// <param name="Total">Tokens of the whole file.</param>
/// <param name="EverySession">Tokens this file contributes to every session.</param>
/// <param name="ProjectSession">Tokens this file contributes to every session of its own project.</param>
internal readonly record struct TokenCounts(int Total, int EverySession, int ProjectSession);
