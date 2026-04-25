using MediaHandler.Domain.Common;
using MediaHandler.Domain.Enums;

namespace MediaHandler.Domain.Entities;

/// <summary>
/// A single rule that the <c>IExclusionEvaluator</c> checks against each NAS entry.
/// Rules are seeded from <c>KodiRegexCatalog</c> at migration time (Kodi-equivalent
/// default exclusions) and are read-only for this feature.
/// </summary>
/// <remarks>
/// Each row carries a <see cref="Scope"/> that determines which part of a file-system
/// path the <see cref="Pattern"/> is matched against.
/// </remarks>
public class ExclusionRule : BaseEntity
{
    /// <summary>Human-readable name used for diagnostics and UI display (e.g., <c>"sample-files"</c>).</summary>
    public required string Name { get; set; }

    /// <summary>
    /// .NET regular expression (or plain extension/filename for <see cref="ExclusionScope.Extension"/>
    /// and <see cref="ExclusionScope.MarkerFile"/> scopes).
    /// </summary>
    public required string Pattern { get; set; }

    /// <summary>Determines which part of a path entry the pattern is matched against.</summary>
    public required ExclusionScope Scope { get; set; }

    /// <summary>
    /// Short, stable identifier referenced in <see cref="ScanItemDecision.RuleId"/> so
    /// admins can trace an exclusion back to this row without a JOIN.
    /// </summary>
    public required string RuleId { get; set; }

    /// <summary>When <c>false</c> the rule is skipped by the evaluator.</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>Lower value = evaluated first; used to control tie-breaking and display order.</summary>
    public int Priority { get; set; }
}

