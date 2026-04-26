using MediaHandler.Application.Common.Models.Scanner;

namespace MediaHandler.Application.Common.Interfaces;

/// <summary>
/// Evaluates a single NAS filesystem entry against the current set of exclusion rules
/// and returns a verdict indicating whether the entry should be skipped by the pipeline.
/// </summary>
public interface IExclusionEvaluator
{
    /// <summary>
    /// Determines whether <paramref name="entry"/> should be excluded.
    /// </summary>
    /// <param name="entry">The NAS entry to evaluate.</param>
    /// <param name="ctx">Contextual data including the library root and active rules.</param>
    /// <returns>
    /// An <see cref="ExclusionVerdict"/> with <c>IsExcluded = false</c> for entries that
    /// should pass through; <c>IsExcluded = true</c> + <c>RuleId</c> for excluded ones.
    /// </returns>
    ExclusionVerdict Evaluate(NasFileEntry entry, ExclusionContext ctx);
}

