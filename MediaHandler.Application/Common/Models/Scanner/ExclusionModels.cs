using MediaHandler.Domain.Entities;

namespace MediaHandler.Application.Common.Models.Scanner;

/// <summary>
/// Input context supplied to <c>IExclusionEvaluator.Evaluate</c>.
/// Carries all exclusion rules applicable to the current library root.
/// </summary>
public record ExclusionContext(
    /// <summary>The library root under which the entry was found.</summary>
    LibraryRoot Root,
    /// <summary>Enabled rules to evaluate, ordered by priority (ascending).</summary>
    IReadOnlyList<ExclusionRule> Rules);

/// <summary>
/// Decision returned by <c>IExclusionEvaluator.Evaluate</c> for a single NAS entry.
/// </summary>
public record ExclusionVerdict(
    bool IsExcluded,
    /// <summary>Human-readable explanation; set only when <see cref="IsExcluded"/> is <c>true</c>.</summary>
    string? Reason = null,
    /// <summary>Stable identifier of the <see cref="ExclusionRule"/> that triggered the exclusion.</summary>
    string? RuleId = null);

