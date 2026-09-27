using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

internal sealed class ExactRatioMechanismGenerator
{
    private readonly ExactRatioSynthesizer _synthesizer;
    private readonly CardinalSpatialLayoutEngine _layoutEngine;

    public ExactRatioMechanismGenerator(
        ExactRatioSynthesizer synthesizer,
        CardinalSpatialLayoutEngine layoutEngine)
    {
        _synthesizer = synthesizer ?? throw new ArgumentNullException(nameof(synthesizer));
        _layoutEngine = layoutEngine ?? throw new ArgumentNullException(nameof(layoutEngine));
    }

    public ExactRatioMechanismGenerationResult Generate(
        ExactRatioMechanismGenerationRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var canonicalRequest = ExactRatioMechanismGenerationRequestIdentity.BuildCanonicalRepresentation(request);
        var requestId = ExactRatioMechanismGenerationRequestIdentity.ComputeRequestId(canonicalRequest);
        var fingerprint = new ExactRatioMechanismGenerationFingerprint(
            ExactRatioMechanismGenerationContract.BackendId,
            ExactRatioMechanismGenerationContract.BackendVersion,
            request.DeterminismProfile);
        var diagnostics = ValidateRequest(request);
        if (diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return Terminal(
                request,
                canonicalRequest,
                requestId,
                fingerprint,
                ExactRatioMechanismGenerationStatus.InvalidInput,
                diagnostics,
                null,
                Array.Empty<ExactRatioMechanismLayoutAttempt>());
        }

        var synthesis = _synthesizer.Synthesize(request.SynthesisRequest, cancellationToken);
        diagnostics.AddRange(synthesis.Diagnostics);
        if (synthesis.Status == KinematicSynthesisStatus.InvalidInput)
        {
            return Terminal(request, canonicalRequest, requestId, fingerprint,
                ExactRatioMechanismGenerationStatus.InvalidInput, diagnostics, synthesis,
                Array.Empty<ExactRatioMechanismLayoutAttempt>());
        }

        if (synthesis.Status == KinematicSynthesisStatus.Cancelled)
        {
            return Terminal(request, canonicalRequest, requestId, fingerprint,
                ExactRatioMechanismGenerationStatus.Cancelled, diagnostics, synthesis,
                Array.Empty<ExactRatioMechanismLayoutAttempt>());
        }

        var selectedKinematic = synthesis.Candidates
            .Take(request.MaxKinematicCandidatesToLayout)
            .ToList();
        var layoutAttempts = new List<ExactRatioMechanismLayoutAttempt>();
        var composed = new List<ExactRatioMechanismCandidate>();
        var sawIncompleteBudget = synthesis.Status == KinematicSynthesisStatus.IncompleteBudget;
        var sawCancellation = false;
        var sawInvalidLayoutRequest = false;

        for (var kinematicRank = 0; kinematicRank < selectedKinematic.Count; kinematicRank++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                sawCancellation = true;
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.GenerationCancelled,
                    DiagnosticSeverity.Warning,
                    "Exact-ratio mechanism generation was cancelled between deterministic stages."));
                break;
            }

            var kinematic = selectedKinematic[kinematicRank];
            var layoutResult = _layoutEngine.CreateLayouts(kinematic, request.LayoutRequest, cancellationToken);
            diagnostics.AddRange(layoutResult.Diagnostics);
            var selectedLayouts = layoutResult.Candidates
                .Take(request.MaxLayoutsPerKinematicCandidate)
                .ToList();
            layoutAttempts.Add(new ExactRatioMechanismLayoutAttempt(
                kinematicRank,
                kinematic.KinematicCandidateId,
                layoutResult,
                selectedLayouts.Count));

            sawIncompleteBudget |= layoutResult.Status == SpatialLayoutStatus.IncompleteBudget;
            sawCancellation |= layoutResult.Status == SpatialLayoutStatus.Cancelled;
            sawInvalidLayoutRequest |= layoutResult.Status == SpatialLayoutStatus.InvalidInput;
            if (sawCancellation || sawInvalidLayoutRequest)
            {
                break;
            }

            for (var spatialRank = 0; spatialRank < selectedLayouts.Count; spatialRank++)
            {
                var spatial = selectedLayouts[spatialRank];
                if (!TryCompose(
                        request,
                        canonicalRequest,
                        requestId,
                        fingerprint,
                        synthesis,
                        kinematic,
                        kinematicRank,
                        layoutResult,
                        spatial,
                        spatialRank,
                        out var candidate,
                        out var compositionDiagnostics))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.GenerationCandidateValidationFailed,
                        DiagnosticSeverity.Warning,
                        "A staged candidate was excluded after final composition validation: " +
                        string.Join(",", compositionDiagnostics.Select(item => item.Code).Distinct().OrderBy(value => value, StringComparer.Ordinal)),
                        spatial.SpatialCandidateId));
                    continue;
                }

                composed.Add(candidate!);
            }
        }

        var returned = composed.Take(request.MaxReturnedMechanisms).ToList();
        var finalTruncated = composed.Count > returned.Count;
        if (finalTruncated)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationResultTruncated,
                DiagnosticSeverity.Info,
                "Final mechanism candidates were truncated by maxReturnedMechanisms.",
                "maxReturnedMechanisms"));
        }

        var skippedKinematic = Math.Max(0, synthesis.Candidates.Count - selectedKinematic.Count);
        if (skippedKinematic > 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationKinematicCandidatesSkipped,
                DiagnosticSeverity.Info,
                skippedKinematic.ToString(CultureInfo.InvariantCulture) +
                " returned Kinematic candidate(s) were outside the declared orchestration fan-out.",
                "maxKinematicCandidatesToLayout"));
        }

        var status = ResolveStatus(
            sawInvalidLayoutRequest,
            sawCancellation,
            sawIncompleteBudget,
            returned.Count);
        if (status == ExactRatioMechanismGenerationStatus.Infeasible)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationInfeasible,
                DiagnosticSeverity.Info,
                "No valid final mechanism exists within the declared bounded orchestration scope."));
        }

        var searchComplete =
            synthesis.SearchSummary.SearchComplete &&
            layoutAttempts.All(attempt => attempt.Result.SearchSummary.SearchComplete) &&
            !sawCancellation &&
            !sawInvalidLayoutRequest;
        var summary = BuildSummary(
            synthesis,
            selectedKinematic.Count,
            layoutAttempts,
            composed.Count,
            returned.Count,
            finalTruncated,
            searchComplete);
        return new ExactRatioMechanismGenerationResult(
            request,
            canonicalRequest,
            requestId,
            fingerprint,
            status,
            returned,
            diagnostics,
            summary,
            synthesis,
            layoutAttempts);
    }

    private static bool TryCompose(
        ExactRatioMechanismGenerationRequest request,
        string canonicalRequest,
        string requestId,
        ExactRatioMechanismGenerationFingerprint generationFingerprint,
        KinematicSynthesisResult synthesis,
        KinematicSynthesisCandidate kinematic,
        int kinematicRank,
        SpatialLayoutResult layoutResult,
        SpatialLayoutCandidate spatial,
        int spatialRank,
        out ExactRatioMechanismCandidate? result,
        out IReadOnlyCollection<Diagnostic> diagnostics)
    {
        var composition = ComposeExactRatioMechanism(
            kinematic,
            spatial,
            request.LayoutRequest,
            requestId,
            request.SynthesisRequest);
        diagnostics = composition.Diagnostics;
        if (!composition.IsSuccess)
        {
            result = null;
            return false;
        }

        var candidate = composition.Candidates.Single();
        var provenance = new ExactRatioMechanismCandidateProvenance(
            request,
            canonicalRequest,
            requestId,
            generationFingerprint,
            synthesis.GeneratorFingerprint,
            synthesis.SearchSummary,
            kinematic.KinematicCandidateId,
            kinematicRank,
            layoutResult.BackendFingerprint,
            layoutResult.RequestCanonicalRepresentation,
            layoutResult.RequestId,
            layoutResult.SearchSummary,
            spatial.SpatialCandidateId,
            spatialRank);
        result = new ExactRatioMechanismCandidate(
            candidate,
            kinematic,
            spatial,
            provenance,
            new ExactRatioMechanismMetrics(kinematic.Metrics, spatial.Metrics, kinematicRank, spatialRank));
        return true;
    }

    internal static GenerationResult ComposeExactRatioMechanism(
        KinematicSynthesisCandidate kinematic,
        SpatialLayoutCandidate spatial,
        SpatialLayoutRequest layoutRequest,
        string sourceId,
        ExactRatioSynthesisRequest synthesisRequest)
    {
        if (kinematic is null) throw new ArgumentNullException(nameof(kinematic));
        if (spatial is null) throw new ArgumentNullException(nameof(spatial));
        if (layoutRequest is null) throw new ArgumentNullException(nameof(layoutRequest));
        if (synthesisRequest is null) throw new ArgumentNullException(nameof(synthesisRequest));
        if (string.IsNullOrWhiteSpace(sourceId)) throw new ArgumentException("A source ID is required.", nameof(sourceId));

        if (!StringComparer.Ordinal.Equals(kinematic.KinematicCandidateId, spatial.KinematicCandidateId))
        {
            return new GenerationResult(
                Array.Empty<GenerationCandidate>(),
                new[]
                {
                    new Diagnostic(
                        DiagnosticCodes.GenerationCandidateValidationFailed,
                        DiagnosticSeverity.Error,
                        "The Spatial candidate was not produced from the supplied Kinematic candidate.",
                        spatial.SpatialCandidateId),
                });
        }

        var canonicalSpatial = CanonicalMechanicalObjectIdRemapper.Remap(kinematic, spatial.SpatialMechanism);
        var playback = ResolvedPlaybackBuilder.Build(kinematic.ExactSolution, canonicalSpatial);
        var provisional = new GenerationCandidate(
            sourceId,
            kinematic.KinematicMechanism,
            kinematic.ExactSolution,
            canonicalSpatial,
            playback,
            new ValidationBundle(Array.Empty<Diagnostic>()));
        var validation = GenerationEngine.ValidateGeneratedMechanismCandidate(
            provisional,
            new ExactRatioMechanismGenerationRequest(
                synthesisRequest,
                layoutRequest,
                1,
                1,
                1));
        if (!validation.IsValid)
        {
            return new GenerationResult(Array.Empty<GenerationCandidate>(), validation.Diagnostics);
        }

        return new GenerationResult(new[] { provisional.WithValidation(validation) }, validation.Diagnostics);
    }

    private static ExactRatioMechanismGenerationStatus ResolveStatus(
        bool invalidInput,
        bool cancelled,
        bool incompleteBudget,
        int candidateCount)
    {
        if (invalidInput) return ExactRatioMechanismGenerationStatus.InvalidInput;
        if (cancelled) return ExactRatioMechanismGenerationStatus.Cancelled;
        if (incompleteBudget) return ExactRatioMechanismGenerationStatus.IncompleteBudget;
        return candidateCount > 0
            ? ExactRatioMechanismGenerationStatus.Complete
            : ExactRatioMechanismGenerationStatus.Infeasible;
    }

    private static List<Diagnostic> ValidateRequest(ExactRatioMechanismGenerationRequest request)
    {
        var diagnostics = new List<Diagnostic>();
        if (request.MaxKinematicCandidatesToLayout <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationInvalidKinematicLimit,
                DiagnosticSeverity.Error,
                "maxKinematicCandidatesToLayout must be positive.",
                "maxKinematicCandidatesToLayout"));
        }

        if (request.MaxLayoutsPerKinematicCandidate <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationInvalidLayoutLimit,
                DiagnosticSeverity.Error,
                "maxLayoutsPerKinematicCandidate must be positive.",
                "maxLayoutsPerKinematicCandidate"));
        }

        if (request.MaxReturnedMechanisms <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationInvalidMechanismLimit,
                DiagnosticSeverity.Error,
                "maxReturnedMechanisms must be positive.",
                "maxReturnedMechanisms"));
        }

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                ExactRatioMechanismGenerationContract.DefaultDeterminismProfile))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.GenerationUnsupportedDeterminismProfile,
                DiagnosticSeverity.Error,
                "The built-in generation orchestrator supports only determinism profile '" +
                ExactRatioMechanismGenerationContract.DefaultDeterminismProfile + "'.",
                "determinismProfile"));
        }

        return diagnostics;
    }

    private static ExactRatioMechanismGenerationResult Terminal(
        ExactRatioMechanismGenerationRequest request,
        string canonicalRequest,
        string requestId,
        ExactRatioMechanismGenerationFingerprint fingerprint,
        ExactRatioMechanismGenerationStatus status,
        IEnumerable<Diagnostic> diagnostics,
        KinematicSynthesisResult? synthesis,
        IEnumerable<ExactRatioMechanismLayoutAttempt> attempts)
    {
        var available = synthesis?.Candidates.Count ?? 0;
        var summary = new ExactRatioMechanismSearchSummary(
            synthesis?.SearchSummary.ResultTruncated ?? false,
            available,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            false);
        return new ExactRatioMechanismGenerationResult(
            request,
            canonicalRequest,
            requestId,
            fingerprint,
            status,
            Array.Empty<ExactRatioMechanismCandidate>(),
            diagnostics,
            summary,
            synthesis,
            attempts);
    }

    private static ExactRatioMechanismSearchSummary BuildSummary(
        KinematicSynthesisResult synthesis,
        int selectedKinematicCount,
        IReadOnlyCollection<ExactRatioMechanismLayoutAttempt> attempts,
        int composedCount,
        int returnedCount,
        bool finalTruncated,
        bool searchComplete)
    {
        return new ExactRatioMechanismSearchSummary(
            synthesis.SearchSummary.ResultTruncated,
            synthesis.Candidates.Count,
            selectedKinematicCount,
            Math.Max(0, synthesis.Candidates.Count - selectedKinematicCount),
            attempts.Count,
            attempts.Count(attempt => attempt.Result.Status == SpatialLayoutStatus.IncompleteBudget),
            attempts.Count(attempt => attempt.Result.Status == SpatialLayoutStatus.Infeasible),
            attempts.Count(attempt => attempt.Result.SearchSummary.ResultTruncated),
            attempts.Sum(attempt => attempt.SelectedLayoutCount),
            composedCount,
            returnedCount,
            finalTruncated,
            searchComplete);
    }

}

internal static class CanonicalMechanicalObjectIdRemapper
{
    public static SpatialMechanism Remap(
        KinematicSynthesisCandidate kinematic,
        SpatialMechanism spatial)
    {
        var oldBodies = spatial.Bodies.ToDictionary(body => body.Id, StringComparer.Ordinal);
        var bodyIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var oldContacts = spatial.Contacts
            .GroupBy(contact => contact.ConstraintId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Single(), StringComparer.Ordinal);
        var couplings = kinematic.KinematicMechanism.Couplings
            .OrderBy(coupling => coupling.Id, StringComparer.Ordinal)
            .ToList();

        for (var index = 0; index < couplings.Count; index++)
        {
            var coupling = couplings[index];
            var contact = oldContacts[coupling.Id];
            var contactBodies = new[] { oldBodies[contact.BodyAId], oldBodies[contact.BodyBId] };
            var driver = contactBodies.Single(body => StringComparer.Ordinal.Equals(body.DofId, coupling.DriverDofId));
            var driven = contactBodies.Single(body => StringComparer.Ordinal.Equals(body.DofId, coupling.DrivenDofId));
            if (!bodyIdMap.ContainsKey(driver.Id))
            {
                bodyIdMap.Add(driver.Id, BodyId(index + 1, "driver"));
            }

            if (!bodyIdMap.ContainsKey(driven.Id))
            {
                bodyIdMap.Add(driven.Id, BodyId(index + 1, "driven"));
            }
        }

        var axisIdMap = spatial.Bodies
            .GroupBy(body => body.AxisId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => "axis:" + AxisRole(group.Select(body => body.DofId).Distinct(StringComparer.Ordinal).Single()),
                StringComparer.Ordinal);
        var axes = spatial.Axes.Select(axis => new SpatialAxis(axisIdMap[axis.Id], axis.X, axis.Y));
        var bodies = spatial.Bodies.Select(body => new SpatialBody(
            bodyIdMap[body.Id],
            body.Kind,
            axisIdMap[body.AxisId],
            body.DofId,
            body.Layer,
            body.ToothCount,
            body.PitchRadius,
            body.ExactMountingPhase));
        var contacts = spatial.Contacts.Select(contact => new SpatialContact(
            "contact:" + contact.ConstraintId,
            contact.Kind,
            contact.ConstraintId,
            bodyIdMap[contact.BodyAId],
            bodyIdMap[contact.BodyBId]));
        return new SpatialMechanism(axes, bodies, contacts);
    }

    public static IEnumerable<Diagnostic> ValidateCanonicalIds(
        KinematicSpecification kinematic,
        SpatialMechanism spatial)
    {
        var diagnostics = new List<Diagnostic>();
        foreach (var axisGroup in spatial.Bodies.GroupBy(body => body.AxisId, StringComparer.Ordinal))
        {
            var dofIds = axisGroup.Select(body => body.DofId).Distinct(StringComparer.Ordinal).ToList();
            if (dofIds.Count == 1)
            {
                var expected = "axis:" + AxisRole(dofIds[0]);
                if (!StringComparer.Ordinal.Equals(axisGroup.Key, expected))
                {
                    diagnostics.Add(ObjectIdDiagnostic(axisGroup.Key, expected));
                }
            }
        }

        var bodies = spatial.Bodies.ToDictionary(body => body.Id, StringComparer.Ordinal);
        var contacts = spatial.Contacts
            .GroupBy(contact => contact.ConstraintId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        var expectedBodyIds = new Dictionary<string, string>(StringComparer.Ordinal);
        var couplings = kinematic.Couplings.OrderBy(coupling => coupling.Id, StringComparer.Ordinal).ToList();
        for (var index = 0; index < couplings.Count; index++)
        {
            var coupling = couplings[index];
            if (!contacts.TryGetValue(coupling.Id, out var matches) || matches.Count != 1)
            {
                continue;
            }

            var contact = matches[0];
            if (!StringComparer.Ordinal.Equals(contact.Id, "contact:" + coupling.Id))
            {
                diagnostics.Add(ObjectIdDiagnostic(contact.Id, "contact:" + coupling.Id));
            }

            if (!bodies.TryGetValue(contact.BodyAId, out var bodyA) ||
                !bodies.TryGetValue(contact.BodyBId, out var bodyB))
            {
                continue;
            }

            var driver = StringComparer.Ordinal.Equals(bodyA.DofId, coupling.DriverDofId) ? bodyA : bodyB;
            var driven = StringComparer.Ordinal.Equals(bodyA.DofId, coupling.DrivenDofId) ? bodyA : bodyB;
            AddExpectedBodyId(expectedBodyIds, driver.Id, BodyId(index + 1, "driver"));
            AddExpectedBodyId(expectedBodyIds, driven.Id, BodyId(index + 1, "driven"));
        }

        foreach (var pair in expectedBodyIds)
        {
            if (!StringComparer.Ordinal.Equals(pair.Key, pair.Value))
            {
                diagnostics.Add(ObjectIdDiagnostic(pair.Key, pair.Value));
            }
        }

        return diagnostics;
    }

    private static void AddExpectedBodyId(
        IDictionary<string, string> expected,
        string actualId,
        string expectedId)
    {
        if (!expected.ContainsKey(actualId))
        {
            expected.Add(actualId, expectedId);
        }
    }

    private static Diagnostic ObjectIdDiagnostic(string actual, string expected) =>
        new(
            DiagnosticCodes.GenerationObjectIdMismatch,
            DiagnosticSeverity.Error,
            "Final mechanical object ID '" + actual + "' must be canonical role/path ID '" + expected + "'.",
            actual);

    private static string BodyId(int stageOrdinal, string role) =>
        "body:stage:" + stageOrdinal.ToString("D2", CultureInfo.InvariantCulture) + ":" + role;

    private static string AxisRole(string dofId) =>
        dofId.StartsWith("dof:", StringComparison.Ordinal) ? dofId.Substring(4) : dofId;
}
