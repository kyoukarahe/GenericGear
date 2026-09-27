using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public sealed class CardinalSpatialLayoutEngine
{
    private static readonly CardinalDirection[] DirectionOrder =
    {
        CardinalDirection.East,
        CardinalDirection.North,
        CardinalDirection.West,
        CardinalDirection.South,
    };

    public SpatialLayoutResult CreateLayouts(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest request,
        CancellationToken cancellationToken = default)
    {
        if (sourceCandidate is null)
        {
            throw new ArgumentNullException(nameof(sourceCandidate));
        }

        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var canonicalRequest = BuildRequestCanonicalRepresentation(sourceCandidate, request);
        var requestId = Hash("spatial-request-sha256:", canonicalRequest);
        var fingerprint = new SpatialLayoutFingerprint(
            SpatialLayoutContract.BackendId,
            SpatialLayoutContract.BackendVersion,
            request.DeterminismProfile);
        var inputDiagnostics = ValidateInput(sourceCandidate, request);
        if (inputDiagnostics.Count > 0)
        {
            return CreateTerminalResult(
                sourceCandidate,
                request,
                canonicalRequest,
                requestId,
                SpatialLayoutStatus.InvalidInput,
                inputDiagnostics,
                fingerprint,
                searchComplete: false);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return CreateTerminalResult(
                sourceCandidate,
                request,
                canonicalRequest,
                requestId,
                SpatialLayoutStatus.Cancelled,
                new[]
                {
                    new Diagnostic(
                        DiagnosticCodes.LayoutCancelled,
                        DiagnosticSeverity.Warning,
                        "Spatial layout was cancelled before search began."),
                },
                fingerprint,
                searchComplete: false);
        }

        var directionAssignments = EnumerateDirections(sourceCandidate.OrderedStages.Count);
        var layerAssignments = EnumerateLayers(sourceCandidate.IntermediateRoles, request.MaxLayers);
        if (layerAssignments.Count == 0)
        {
            return CreateTerminalResult(
                sourceCandidate,
                request,
                canonicalRequest,
                requestId,
                SpatialLayoutStatus.Infeasible,
                new[]
                {
                    new Diagnostic(
                        DiagnosticCodes.LayoutInsufficientLayers,
                        DiagnosticSeverity.Info,
                        "No canonical layer assignment satisfies the intermediate shaft roles within maxLayers.",
                        "maxLayers"),
                },
                fingerprint,
                searchComplete: true,
                directionAssignments.Count,
                layerAssignments.Count);
        }

        var topCandidates = new List<SpatialLayoutCandidate>();
        var signatures = new HashSet<string>(StringComparer.Ordinal);
        var rawFeasible = 0L;
        var deduplicated = 0L;
        var expansions = 0L;
        var budgetExhausted = false;
        var cancelled = false;

        foreach (var directions in directionAssignments)
        {
            foreach (var layers in layerAssignments)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    cancelled = true;
                    break;
                }

                if (expansions >= request.MaxPlacementExpansions)
                {
                    budgetExhausted = true;
                    break;
                }

                expansions++;
                if (!SpatialLayoutCandidateFactory.TryCreate(
                        sourceCandidate,
                        request,
                        fingerprint,
                        directions,
                        layers,
                        out var candidate,
                        out _) || candidate is null)
                {
                    continue;
                }

                rawFeasible++;
                if (!signatures.Add(candidate.CanonicalSignature))
                {
                    continue;
                }

                deduplicated++;
                AddToTopCandidates(topCandidates, candidate, request.MaxReturnedLayouts);
            }

            if (cancelled || budgetExhausted)
            {
                break;
            }
        }

        var diagnostics = new List<Diagnostic>();
        SpatialLayoutStatus status;
        var searchComplete = !cancelled && !budgetExhausted;
        if (cancelled)
        {
            status = SpatialLayoutStatus.Cancelled;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutCancelled,
                DiagnosticSeverity.Warning,
                "Spatial layout was cancelled before the bounded search completed."));
        }
        else if (budgetExhausted)
        {
            status = SpatialLayoutStatus.IncompleteBudget;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutBudgetExhausted,
                DiagnosticSeverity.Warning,
                "The deterministic placement budget of '" +
                request.MaxPlacementExpansions.ToString(CultureInfo.InvariantCulture) +
                "' was exhausted before search completed.",
                "maxPlacementExpansions"));
        }
        else if (deduplicated == 0)
        {
            status = SpatialLayoutStatus.Infeasible;
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutInfeasible,
                DiagnosticSeverity.Info,
                "No valid fixed-topology cardinal Spatial embodiment exists within the requested bounds."));
        }
        else
        {
            status = SpatialLayoutStatus.Complete;
        }

        topCandidates.Sort(SpatialLayoutCandidateComparer.Instance);
        var resultTruncated = deduplicated > topCandidates.Count;
        if (resultTruncated)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutResultTruncated,
                DiagnosticSeverity.Info,
                "Only the highest-ranked '" +
                request.MaxReturnedLayouts.ToString(CultureInfo.InvariantCulture) +
                "' layouts among discovered valid embodiments were returned.",
                "maxReturnedLayouts"));
        }

        return new SpatialLayoutResult(
            sourceCandidate,
            request,
            canonicalRequest,
            requestId,
            status,
            topCandidates,
            diagnostics,
            new SpatialLayoutSearchSummary(
                directionAssignments.Count,
                layerAssignments.Count,
                expansions,
                rawFeasible,
                deduplicated,
                topCandidates.Count,
                resultTruncated,
                searchComplete),
            fingerprint);
    }

    internal static string BuildRequestCanonicalRepresentation(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest request)
    {
        return SpatialLayoutContract.BackendSemanticVersion +
            "|profile=" + request.DeterminismProfile.Length.ToString(CultureInfo.InvariantCulture) + ":" + request.DeterminismProfile +
            "|kinematicCandidateId=" + sourceCandidate.KinematicCandidateId +
            "|rootAxisX=" + request.RootAxisX.ToString(CultureInfo.InvariantCulture) +
            "|rootAxisY=" + request.RootAxisY.ToString(CultureInfo.InvariantCulture) +
            "|pitchRadiusTicksPerTooth=" + request.PitchRadiusTicksPerTooth.ToString(CultureInfo.InvariantCulture) +
            "|maxLayers=" + request.MaxLayers.ToString(CultureInfo.InvariantCulture) +
            "|maxReturnedLayouts=" + request.MaxReturnedLayouts.ToString(CultureInfo.InvariantCulture) +
            "|maxPlacementExpansions=" + request.MaxPlacementExpansions.ToString(CultureInfo.InvariantCulture) +
            "|clearanceTicks=" + request.ClearanceTicks.ToString(CultureInfo.InvariantCulture);
    }

    private static List<Diagnostic> ValidateInput(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest request)
    {
        var diagnostics = new List<Diagnostic>();
        if (request.PitchRadiusTicksPerTooth.Sign <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutInvalidPitchScale,
                DiagnosticSeverity.Error,
                "pitchRadiusTicksPerTooth must be positive.",
                "pitchRadiusTicksPerTooth"));
        }

        if (request.MaxLayers < 1 || request.MaxLayers > 3)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutInvalidLayerLimit,
                DiagnosticSeverity.Error,
                "The built-in cardinal backend supports maxLayers within 1..3.",
                "maxLayers"));
        }

        if (request.MaxReturnedLayouts <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutInvalidReturnLimit,
                DiagnosticSeverity.Error,
                "maxReturnedLayouts must be positive.",
                "maxReturnedLayouts"));
        }

        if (request.MaxPlacementExpansions <= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutInvalidSearchBudget,
                DiagnosticSeverity.Error,
                "maxPlacementExpansions must be positive.",
                "maxPlacementExpansions"));
        }

        if (request.ClearanceTicks.Sign < 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutInvalidClearance,
                DiagnosticSeverity.Error,
                "clearanceTicks must be non-negative.",
                "clearanceTicks"));
        }

        if (!StringComparer.Ordinal.Equals(
                request.DeterminismProfile,
                SpatialLayoutContract.DefaultDeterminismProfile))
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutUnsupportedDeterminismProfile,
                DiagnosticSeverity.Error,
                "The built-in cardinal backend supports only determinism profile '" +
                SpatialLayoutContract.DefaultDeterminismProfile + "'.",
                "determinismProfile"));
        }

        ValidateSourceCandidate(sourceCandidate, diagnostics);
        return diagnostics;
    }

    private static void ValidateSourceCandidate(
        KinematicSynthesisCandidate candidate,
        ICollection<Diagnostic> diagnostics)
    {
        var stageCount = candidate.OrderedStages.Count;
        var solved = KinematicSolver.Solve(candidate.KinematicMechanism);
        var structurallyValid =
            candidate.Validation.IsValid &&
            stageCount >= 1 && stageCount <= 3 &&
            candidate.IntermediateRoles.Count == stageCount - 1 &&
            candidate.KinematicMechanism.Dofs.Count == stageCount + 1 &&
            candidate.KinematicMechanism.Couplings.Count == stageCount &&
            solved.IsValid && solved.Solution is not null;
        if (structurallyValid)
        {
            for (var index = 0; index < stageCount; index++)
            {
                var stage = candidate.OrderedStages[index];
                var coupling = candidate.KinematicMechanism.Couplings[index];
                if (stage.Index != index + 1 ||
                    stage.DriverTeeth != coupling.DriverTeeth ||
                    stage.DrivenTeeth != coupling.DrivenTeeth)
                {
                    structurallyValid = false;
                    break;
                }
            }
        }

        if (!structurallyValid)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.LayoutInvalidKinematicCandidate,
                DiagnosticSeverity.Error,
                "The source must be a validated Milestone 6A serial 1..3-stage Kinematic candidate.",
                candidate.KinematicCandidateId));
        }
    }

    private static List<IReadOnlyList<CardinalDirection>> EnumerateDirections(int stageCount)
    {
        var results = new List<IReadOnlyList<CardinalDirection>>();
        var current = new CardinalDirection[stageCount];
        current[0] = CardinalDirection.East;
        EnumerateDirectionSuffix(1, current, results);
        return results;
    }

    private static void EnumerateDirectionSuffix(
        int index,
        CardinalDirection[] current,
        ICollection<IReadOnlyList<CardinalDirection>> results)
    {
        if (index == current.Length)
        {
            results.Add((CardinalDirection[])current.Clone());
            return;
        }

        foreach (var direction in DirectionOrder)
        {
            if (direction == Opposite(current[index - 1]))
            {
                continue;
            }

            current[index] = direction;
            EnumerateDirectionSuffix(index + 1, current, results);
        }
    }

    private static List<IReadOnlyList<int>> EnumerateLayers(
        IReadOnlyList<IntermediateShaftRole> roles,
        int maxLayers)
    {
        var stageCount = roles.Count + 1;
        var results = new List<IReadOnlyList<int>>();
        var current = new int[stageCount];
        current[0] = 0;
        EnumerateLayerSuffix(1, maxSeen: 0, current, roles, maxLayers, results);
        return results;
    }

    private static void EnumerateLayerSuffix(
        int stageIndex,
        int maxSeen,
        int[] current,
        IReadOnlyList<IntermediateShaftRole> roles,
        int maxLayers,
        ICollection<IReadOnlyList<int>> results)
    {
        if (stageIndex == current.Length)
        {
            results.Add((int[])current.Clone());
            return;
        }

        var role = roles[stageIndex - 1];
        if (role == IntermediateShaftRole.SimpleIdlerCompatible)
        {
            current[stageIndex] = current[stageIndex - 1];
            EnumerateLayerSuffix(stageIndex + 1, maxSeen, current, roles, maxLayers, results);
            return;
        }

        var maximumCanonicalLayer = Math.Min(maxSeen + 1, maxLayers - 1);
        for (var layer = 0; layer <= maximumCanonicalLayer; layer++)
        {
            if (layer == current[stageIndex - 1])
            {
                continue;
            }

            current[stageIndex] = layer;
            EnumerateLayerSuffix(
                stageIndex + 1,
                Math.Max(maxSeen, layer),
                current,
                roles,
                maxLayers,
                results);
        }
    }

    private static CardinalDirection Opposite(CardinalDirection direction)
    {
        return direction switch
        {
            CardinalDirection.East => CardinalDirection.West,
            CardinalDirection.North => CardinalDirection.South,
            CardinalDirection.West => CardinalDirection.East,
            CardinalDirection.South => CardinalDirection.North,
            _ => throw new InvalidOperationException("Unsupported cardinal direction '" + direction + "'."),
        };
    }

    private static void AddToTopCandidates(
        List<SpatialLayoutCandidate> candidates,
        SpatialLayoutCandidate candidate,
        int maximumCount)
    {
        if (candidates.Count < maximumCount)
        {
            candidates.Add(candidate);
            candidates.Sort(SpatialLayoutCandidateComparer.Instance);
            return;
        }

        var worstIndex = candidates.Count - 1;
        if (SpatialLayoutCandidateComparer.Instance.Compare(candidate, candidates[worstIndex]) >= 0)
        {
            return;
        }

        candidates[worstIndex] = candidate;
        candidates.Sort(SpatialLayoutCandidateComparer.Instance);
    }

    private static SpatialLayoutResult CreateTerminalResult(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest request,
        string canonicalRequest,
        string requestId,
        SpatialLayoutStatus status,
        IEnumerable<Diagnostic> diagnostics,
        SpatialLayoutFingerprint fingerprint,
        bool searchComplete,
        long directionAssignmentsConsidered = 0,
        long layerAssignmentsConsidered = 0)
    {
        return new SpatialLayoutResult(
            sourceCandidate,
            request,
            canonicalRequest,
            requestId,
            status,
            Array.Empty<SpatialLayoutCandidate>(),
            diagnostics,
            new SpatialLayoutSearchSummary(
                directionAssignmentsConsidered,
                layerAssignmentsConsidered,
                0,
                0,
                0,
                0,
                false,
                searchComplete),
            fingerprint);
    }

    private static string Hash(string prefix, string value)
    {
        byte[] digest;
        using (var algorithm = SHA256.Create())
        {
            digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(value));
        }

        var builder = new StringBuilder(prefix, prefix.Length + (digest.Length * 2));
        foreach (var item in digest)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}

internal static class SpatialLayoutCandidateFactory
{
    public static bool TryCreate(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest request,
        SpatialLayoutFingerprint fingerprint,
        IReadOnlyList<CardinalDirection> directions,
        IReadOnlyList<int> stageLayers,
        out SpatialLayoutCandidate? candidate,
        out SpatialValidationBundle validation)
    {
        var spatial = BuildSpatialMechanism(sourceCandidate, request, directions, stageLayers);
        return TryCreateFromSpatial(
            sourceCandidate,
            request,
            fingerprint,
            spatial,
            directions,
            stageLayers,
            out candidate,
            out validation);
    }

    internal static bool TryCreateFromSpatial(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest request,
        SpatialLayoutFingerprint fingerprint,
        SpatialMechanism spatial,
        IReadOnlyList<CardinalDirection> directions,
        IReadOnlyList<int> stageLayers,
        out SpatialLayoutCandidate? candidate,
        out SpatialValidationBundle validation)
    {
        validation = SpatialValidator.ValidateDetailed(
            spatial,
            sourceCandidate.KinematicMechanism,
            new SpatialValidationOptions(
                request.PitchRadiusTicksPerTooth,
                request.MaxLayers,
                request.ClearanceTicks));
        if (!validation.IsValid)
        {
            candidate = null;
            return false;
        }

        var metrics = BuildMetrics(spatial, directions);
        var signature = BuildCanonicalSignature(
            sourceCandidate.KinematicCandidateId,
            spatial,
            request.ClearanceTicks,
            fingerprint);
        candidate = new SpatialLayoutCandidate(
            sourceCandidate.KinematicCandidateId,
            HashSpatialCandidate(signature),
            signature,
            spatial,
            directions,
            stageLayers,
            validation,
            metrics);
        return true;
    }

    internal static SpatialMechanism BuildSpatialMechanism(
        KinematicSynthesisCandidate sourceCandidate,
        SpatialLayoutRequest request,
        IReadOnlyList<CardinalDirection> directions,
        IReadOnlyList<int> stageLayers)
    {
        var stages = sourceCandidate.OrderedStages;
        if (directions.Count != stages.Count || stageLayers.Count != stages.Count)
        {
            throw new ArgumentException("Direction and layer assignments must match the ordered stage count.");
        }

        var couplings = sourceCandidate.KinematicMechanism.Couplings;
        var token = CandidateToken(sourceCandidate.KinematicCandidateId);
        var axisPositions = new List<(BigInteger X, BigInteger Y)>
        {
            (request.RootAxisX, request.RootAxisY),
        };
        for (var index = 0; index < stages.Count; index++)
        {
            var stage = stages[index];
            var distance = (new BigInteger(stage.DriverTeeth) + stage.DrivenTeeth) * request.PitchRadiusTicksPerTooth;
            var delta = DirectionDelta(directions[index], distance);
            var previous = axisPositions[index];
            axisPositions.Add((previous.X + delta.X, previous.Y + delta.Y));
        }

        var axes = new List<SpatialAxis>();
        var pathDofs = new List<string> { couplings[0].DriverDofId };
        pathDofs.AddRange(couplings.Select(coupling => coupling.DrivenDofId));
        for (var index = 0; index < pathDofs.Count; index++)
        {
            axes.Add(new SpatialAxis(AxisId(token, pathDofs[index]), axisPositions[index].X, axisPositions[index].Y));
        }

        var bodies = new Dictionary<string, SpatialBody>(StringComparer.Ordinal);
        var driverBodyIds = new string[stages.Count];
        var drivenBodyIds = new string[stages.Count];
        for (var index = 0; index < stages.Count; index++)
        {
            var coupling = couplings[index];
            if (index == 0 || sourceCandidate.IntermediateRoles[index - 1] == IntermediateShaftRole.Compound)
            {
                driverBodyIds[index] = BodyId(token, index + 1, "driver");
                AddBody(
                    bodies,
                    driverBodyIds[index],
                    AxisId(token, coupling.DriverDofId),
                    coupling.DriverDofId,
                    stageLayers[index],
                    stages[index].DriverTeeth,
                    request.PitchRadiusTicksPerTooth);
            }
            else
            {
                driverBodyIds[index] = drivenBodyIds[index - 1];
            }

            drivenBodyIds[index] = BodyId(token, index + 1, "driven");
            AddBody(
                bodies,
                drivenBodyIds[index],
                AxisId(token, coupling.DrivenDofId),
                coupling.DrivenDofId,
                stageLayers[index],
                stages[index].DrivenTeeth,
                request.PitchRadiusTicksPerTooth);
        }

        var contacts = new List<SpatialContact>();
        for (var index = 0; index < stages.Count; index++)
        {
            contacts.Add(new SpatialContact(
                ContactId(token, couplings[index].Id),
                SpatialContactKind.ExternalGearMesh,
                couplings[index].Id,
                driverBodyIds[index],
                drivenBodyIds[index]));
        }

        return new SpatialMechanism(axes, bodies.Values, contacts);
    }

    internal static string CandidateToken(string kinematicCandidateId)
    {
        const string prefix = "kinematic-sha256:";
        return kinematicCandidateId.StartsWith(prefix, StringComparison.Ordinal)
            ? kinematicCandidateId.Substring(prefix.Length)
            : kinematicCandidateId;
    }

    internal static string AxisId(string token, string dofId) => "axis:" + token + ":" + dofId;

    internal static string BodyId(string token, int stageOrdinal, string role) =>
        "body:" + token + ":stage:" + stageOrdinal.ToString("D2", CultureInfo.InvariantCulture) + ":" + role;

    internal static string ContactId(string token, string couplingId) => "contact:" + token + ":" + couplingId;

    private static void AddBody(
        IDictionary<string, SpatialBody> bodies,
        string bodyId,
        string axisId,
        string dofId,
        int layer,
        int toothCount,
        BigInteger pitchScale)
    {
        if (bodies.ContainsKey(bodyId))
        {
            return;
        }

        bodies.Add(bodyId, new SpatialBody(
            bodyId,
            SpatialBodyKind.Gear,
            axisId,
            dofId,
            layer,
            toothCount,
            new BigInteger(toothCount) * pitchScale,
            Rational.Zero));
    }

    private static (BigInteger X, BigInteger Y) DirectionDelta(
        CardinalDirection direction,
        BigInteger distance)
    {
        return direction switch
        {
            CardinalDirection.East => (distance, BigInteger.Zero),
            CardinalDirection.North => (BigInteger.Zero, distance),
            CardinalDirection.West => (BigInteger.Negate(distance), BigInteger.Zero),
            CardinalDirection.South => (BigInteger.Zero, BigInteger.Negate(distance)),
            _ => throw new InvalidOperationException("Unsupported cardinal direction '" + direction + "'."),
        };
    }

    private static SpatialLayoutMetrics BuildMetrics(
        SpatialMechanism spatial,
        IReadOnlyList<CardinalDirection> directions)
    {
        var axes = spatial.Axes.ToDictionary(axis => axis.Id, StringComparer.Ordinal);
        var first = spatial.Bodies[0];
        var firstAxis = axes[first.AxisId];
        var minimumX = firstAxis.X - first.PitchRadius;
        var maximumX = firstAxis.X + first.PitchRadius;
        var minimumY = firstAxis.Y - first.PitchRadius;
        var maximumY = firstAxis.Y + first.PitchRadius;
        foreach (var body in spatial.Bodies.Skip(1))
        {
            var axis = axes[body.AxisId];
            minimumX = BigInteger.Min(minimumX, axis.X - body.PitchRadius);
            maximumX = BigInteger.Max(maximumX, axis.X + body.PitchRadius);
            minimumY = BigInteger.Min(minimumY, axis.Y - body.PitchRadius);
            maximumY = BigInteger.Max(maximumY, axis.Y + body.PitchRadius);
        }

        var directionChanges = 0;
        for (var index = 1; index < directions.Count; index++)
        {
            if (directions[index] != directions[index - 1])
            {
                directionChanges++;
            }
        }

        return new SpatialLayoutMetrics(
            spatial.Bodies.Select(body => body.Layer).Distinct().Count(),
            spatial.Bodies.Count,
            spatial.Contacts.Count,
            maximumX - minimumX,
            maximumY - minimumY,
            directionChanges);
    }

    private static string BuildCanonicalSignature(
        string kinematicCandidateId,
        SpatialMechanism spatial,
        BigInteger clearanceTicks,
        SpatialLayoutFingerprint fingerprint)
    {
        var builder = new StringBuilder();
        builder.Append(fingerprint.SemanticVersion);
        builder.Append("|profile=").Append(fingerprint.DeterminismProfile.Length.ToString(CultureInfo.InvariantCulture));
        builder.Append(':').Append(fingerprint.DeterminismProfile);
        builder.Append("|kinematicCandidateId=").Append(kinematicCandidateId);
        builder.Append("|clearanceTicks=").Append(clearanceTicks.ToString(CultureInfo.InvariantCulture));
        builder.Append("|axes=");
        for (var index = 0; index < spatial.Axes.Count; index++)
        {
            if (index > 0) builder.Append(';');
            var axis = spatial.Axes[index];
            builder.Append(axis.Id).Append('(')
                .Append(axis.X.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(axis.Y.ToString(CultureInfo.InvariantCulture)).Append(')');
        }

        builder.Append("|bodies=");
        for (var index = 0; index < spatial.Bodies.Count; index++)
        {
            if (index > 0) builder.Append(';');
            var body = spatial.Bodies[index];
            builder.Append(body.Id).Append('(')
                .Append(body.AxisId).Append(',')
                .Append(body.DofId).Append(',')
                .Append(body.Layer.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(body.ToothCount.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(body.PitchRadius.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(body.ExactMountingPhase).Append(')');
        }

        builder.Append("|contacts=");
        for (var index = 0; index < spatial.Contacts.Count; index++)
        {
            if (index > 0) builder.Append(';');
            var contact = spatial.Contacts[index];
            builder.Append(contact.Id).Append('(')
                .Append(contact.ConstraintId).Append(',')
                .Append(contact.BodyAId).Append(',')
                .Append(contact.BodyBId).Append(')');
        }

        return builder.ToString();
    }

    private static string HashSpatialCandidate(string signature)
    {
        byte[] digest;
        using (var algorithm = SHA256.Create())
        {
            digest = algorithm.ComputeHash(Encoding.UTF8.GetBytes(signature));
        }

        var builder = new StringBuilder("spatial-sha256:");
        foreach (var item in digest)
        {
            builder.Append(item.ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }
}
