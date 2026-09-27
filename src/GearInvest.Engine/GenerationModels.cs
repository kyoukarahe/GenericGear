using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed class LowLevelMechanicalSpecification
{
    public LowLevelMechanicalSpecification(
        string sourceId,
        KinematicSpecification kinematic,
        SpatialMechanism spatial)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
        {
            throw new ArgumentException("A source ID is required.", nameof(sourceId));
        }

        SourceId = sourceId;
        Kinematic = kinematic ?? throw new ArgumentNullException(nameof(kinematic));
        Spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
    }

    public string SourceId { get; }

    public KinematicSpecification Kinematic { get; }

    public SpatialMechanism Spatial { get; }
}

public sealed class ValidationBundle
{
    public ValidationBundle(IEnumerable<Diagnostic> diagnostics)
    {
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
    }

    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }

    public bool IsValid => Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}

public sealed class GenerationCandidate
{
    public GenerationCandidate(
        string sourceId,
        KinematicSpecification kinematic,
        KinematicSolution solution,
        SpatialMechanism spatial,
        ResolvedPlayback resolvedPlayback,
        ValidationBundle validation)
    {
        SourceId = sourceId ?? throw new ArgumentNullException(nameof(sourceId));
        Kinematic = kinematic ?? throw new ArgumentNullException(nameof(kinematic));
        Solution = solution ?? throw new ArgumentNullException(nameof(solution));
        Spatial = spatial ?? throw new ArgumentNullException(nameof(spatial));
        ResolvedPlayback = resolvedPlayback ?? throw new ArgumentNullException(nameof(resolvedPlayback));
        Validation = validation ?? throw new ArgumentNullException(nameof(validation));
    }

    public string SourceId { get; }

    public KinematicSpecification Kinematic { get; }

    public KinematicSolution Solution { get; }

    public SpatialMechanism Spatial { get; }

    public ResolvedPlayback ResolvedPlayback { get; }

    public ValidationBundle Validation { get; }

    public GenerationCandidate WithValidation(ValidationBundle validation)
    {
        return new GenerationCandidate(SourceId, Kinematic, Solution, Spatial, ResolvedPlayback, validation);
    }
}

public sealed class GenerationResult
{
    public GenerationResult(IEnumerable<GenerationCandidate> candidates, IEnumerable<Diagnostic> diagnostics)
    {
        var orderedCandidates = candidates?.ToList() ?? throw new ArgumentNullException(nameof(candidates));
        Candidates = orderedCandidates.AsReadOnly();
        Diagnostics = DiagnosticOrdering.Canonicalize(diagnostics);
    }

    public ReadOnlyCollection<GenerationCandidate> Candidates { get; }

    public ReadOnlyCollection<Diagnostic> Diagnostics { get; }

    public bool IsSuccess => Candidates.Count > 0 && Diagnostics.All(item => item.Severity != DiagnosticSeverity.Error);
}
