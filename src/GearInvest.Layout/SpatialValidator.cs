using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using GearInvest.Core;

namespace GearInvest.Layout;

public static class SpatialValidator
{
    /// <summary>Exact same-plane pitch-disk nonpenetration. Positive radii and nonnegative clearance are caller-validated; boundary tangency is allowed.</summary>
    public static bool NonPenetratingPitchDisks(Rational centerDistanceSquared, Rational radiusA, Rational radiusB, Rational clearance = default)
    {
        var required = radiusA + radiusB + clearance;
        return centerDistanceSquared >= required * required;
    }

    public static ValidationReport Validate(SpatialMechanism spatial, KinematicSpecification kinematic)
    {
        return ValidateDetailed(spatial, kinematic).Report;
    }

    public static SpatialValidationBundle ValidateDetailed(
        SpatialMechanism spatial,
        KinematicSpecification kinematic,
        SpatialValidationOptions? options = null)
    {
        if (spatial is null)
        {
            throw new ArgumentNullException(nameof(spatial));
        }

        if (kinematic is null)
        {
            throw new ArgumentNullException(nameof(kinematic));
        }

        var diagnostics = new List<Diagnostic>();
        var readbacks = new List<SpatialContactReadback>();
        var axes = BuildMap(
            spatial.Axes,
            axis => axis.Id,
            DiagnosticCodes.DuplicateAxisId,
            "Axis",
            diagnostics);
        var bodies = BuildMap(
            spatial.Bodies,
            body => body.Id,
            DiagnosticCodes.DuplicateBodyId,
            "Body",
            diagnostics);
        var contacts = BuildMap(
            spatial.Contacts,
            contact => contact.Id,
            DiagnosticCodes.DuplicateContactId,
            "Contact",
            diagnostics);
        var dofs = kinematic.Dofs
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        var couplings = kinematic.Couplings
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);

        ValidateOptions(options, diagnostics);
        ValidateAxes(axes, diagnostics);
        ValidateBodies(bodies, axes, dofs, options, diagnostics);

        var contactsByConstraint = contacts.Values
            .GroupBy(contact => contact.ConstraintId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(contact => contact.Id, StringComparer.Ordinal).ToList(),
                StringComparer.Ordinal);
        ValidateContactCoverage(couplings, contactsByConstraint, diagnostics);

        var contactedBodyIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var contact in contacts.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!bodies.TryGetValue(contact.BodyAId, out var bodyA) ||
                !bodies.TryGetValue(contact.BodyBId, out var bodyB))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownBodyReference,
                    DiagnosticSeverity.Error,
                    $"Contact '{contact.Id}' references a body that does not exist.",
                    contact.Id));
                continue;
            }

            contactedBodyIds.Add(bodyA.Id);
            contactedBodyIds.Add(bodyB.Id);
            if (!couplings.TryGetValue(contact.ConstraintId, out var coupling))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownConstraintReference,
                    DiagnosticSeverity.Error,
                    $"Contact '{contact.Id}' references unknown constraint '{contact.ConstraintId}'.",
                    contact.Id));
                continue;
            }

            ValidateContact(contact, bodyA, bodyB, coupling, axes, diagnostics, readbacks);
        }

        foreach (var body in bodies.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!contactedBodyIds.Contains(body.Id))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.DanglingBody,
                    DiagnosticSeverity.Error,
                    $"Body '{body.Id}' does not participate in any contact.",
                    body.Id));
            }
        }

        ValidateIntermediateRealizations(
            kinematic.Couplings,
            contactsByConstraint,
            bodies,
            diagnostics);
        var collisionChecks = ValidateUnrelatedSameLayerPairs(
            bodies,
            axes,
            contacts.Values,
            options?.ClearanceTicks ?? BigInteger.Zero,
            diagnostics);

        return new SpatialValidationBundle(diagnostics, readbacks, collisionChecks);
    }

    private static void ValidateOptions(
        SpatialValidationOptions? options,
        ICollection<Diagnostic> diagnostics)
    {
        if (options is null)
        {
            return;
        }

        if (options.PitchRadiusTicksPerTooth.Sign <= 0 ||
            options.MaxLayers <= 0 ||
            options.ClearanceTicks.Sign < 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.InvalidSpatialValue,
                DiagnosticSeverity.Error,
                "Strict Spatial validation requires a positive pitch scale and layer limit, and non-negative clearance.",
                "validationOptions"));
        }
    }

    private static void ValidateAxes(
        IReadOnlyDictionary<string, SpatialAxis> axes,
        ICollection<Diagnostic> diagnostics)
    {
        var ordered = axes.Values.OrderBy(axis => axis.Id, StringComparer.Ordinal).ToList();
        for (var leftIndex = 0; leftIndex < ordered.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < ordered.Count; rightIndex++)
            {
                var left = ordered[leftIndex];
                var right = ordered[rightIndex];
                if (left.X == right.X && left.Y == right.Y)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.CoincidentAxes,
                        DiagnosticSeverity.Error,
                        $"Axes '{left.Id}' and '{right.Id}' occupy the same canonical coordinate.",
                        PairId(left.Id, right.Id)));
                }
            }
        }
    }

    private static void ValidateBodies(
        IReadOnlyDictionary<string, SpatialBody> bodies,
        IReadOnlyDictionary<string, SpatialAxis> axes,
        IReadOnlyDictionary<string, RotationalDof> dofs,
        SpatialValidationOptions? options,
        ICollection<Diagnostic> diagnostics)
    {
        foreach (var body in bodies.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!axes.ContainsKey(body.AxisId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownAxisReference,
                    DiagnosticSeverity.Error,
                    $"Body '{body.Id}' references unknown axis '{body.AxisId}'.",
                    body.Id));
            }

            if (!dofs.ContainsKey(body.DofId))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.UnknownBodyDofReference,
                    DiagnosticSeverity.Error,
                    $"Body '{body.Id}' references unknown DOF '{body.DofId}'.",
                    body.Id));
            }

            if (body.Layer < 0 || body.ToothCount <= 0 || body.PitchRadius.Sign <= 0)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.InvalidSpatialValue,
                    DiagnosticSeverity.Error,
                    $"Body '{body.Id}' must have a non-negative layer, positive tooth count, and positive pitch radius.",
                    body.Id));
            }

            if (options is not null)
            {
                if (body.Layer >= options.MaxLayers)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.LayerLimitExceeded,
                        DiagnosticSeverity.Error,
                        $"Body '{body.Id}' uses layer {body.Layer}, outside the configured 0..{options.MaxLayers - 1} range.",
                        body.Id));
                }

                var expectedRadius = new BigInteger(body.ToothCount) * options.PitchRadiusTicksPerTooth;
                if (body.PitchRadius != expectedRadius)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.PitchScaleMismatch,
                        DiagnosticSeverity.Error,
                        $"Body '{body.Id}' pitch radius does not equal toothCount × pitchRadiusTicksPerTooth.",
                        body.Id));
                }
            }
        }

        foreach (var axisGroup in bodies.Values.GroupBy(body => body.AxisId, StringComparer.Ordinal))
        {
            var dofIds = axisGroup.Select(body => body.DofId).Distinct(StringComparer.Ordinal).ToList();
            if (dofIds.Count > 1)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CompoundDofMismatch,
                    DiagnosticSeverity.Error,
                    $"Bodies on axis '{axisGroup.Key}' do not share one DOF: {string.Join(", ", dofIds.OrderBy(id => id, StringComparer.Ordinal))}.",
                    axisGroup.Key));
            }
        }
    }

    private static void ValidateContactCoverage(
        IReadOnlyDictionary<string, ExternalGearCoupling> couplings,
        IReadOnlyDictionary<string, List<SpatialContact>> contactsByConstraint,
        ICollection<Diagnostic> diagnostics)
    {
        foreach (var coupling in couplings.Values.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            if (!contactsByConstraint.TryGetValue(coupling.Id, out var matching))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.MissingConstraintContact,
                    DiagnosticSeverity.Error,
                    $"Constraint '{coupling.Id}' has no Spatial contact.",
                    coupling.Id));
            }
            else if (matching.Count != 1)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.DuplicateConstraintContact,
                    DiagnosticSeverity.Error,
                    $"Constraint '{coupling.Id}' must have exactly one Spatial contact but has {matching.Count}.",
                    coupling.Id));
            }
        }
    }

    private static void ValidateContact(
        SpatialContact contact,
        SpatialBody bodyA,
        SpatialBody bodyB,
        ExternalGearCoupling coupling,
        IReadOnlyDictionary<string, SpatialAxis> axes,
        ICollection<Diagnostic> diagnostics,
        ICollection<SpatialContactReadback> readbacks)
    {
        var forward = StringComparer.Ordinal.Equals(bodyA.DofId, coupling.DriverDofId) &&
                      StringComparer.Ordinal.Equals(bodyB.DofId, coupling.DrivenDofId);
        var reverse = StringComparer.Ordinal.Equals(bodyB.DofId, coupling.DriverDofId) &&
                      StringComparer.Ordinal.Equals(bodyA.DofId, coupling.DrivenDofId);

        if (!forward && !reverse)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ContactDofMismatch,
                DiagnosticSeverity.Error,
                $"Contact '{contact.Id}' bodies do not correspond to constraint '{coupling.Id}' DOFs.",
                contact.Id));
        }
        else
        {
            var driverBody = forward ? bodyA : bodyB;
            var drivenBody = forward ? bodyB : bodyA;
            if (driverBody.ToothCount != coupling.DriverTeeth || drivenBody.ToothCount != coupling.DrivenTeeth)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.ToothCountMismatch,
                    DiagnosticSeverity.Error,
                    $"Contact '{contact.Id}' body tooth counts do not match constraint '{coupling.Id}'.",
                    contact.Id));
            }
        }

        if (coupling.DriverTeeth > 0 && coupling.DrivenTeeth > 0 && coupling.Transfer.Sign >= 0)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ExternalMeshDirectionMismatch,
                DiagnosticSeverity.Error,
                $"External mesh constraint '{coupling.Id}' must reverse direction.",
                contact.Id));
        }

        if (bodyA.Layer != bodyB.Layer)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticCodes.ContactLayerMismatch,
                DiagnosticSeverity.Error,
                $"Meshing bodies '{bodyA.Id}' and '{bodyB.Id}' are on different layers.",
                contact.Id));
        }

        if (bodyA.PitchRadius.Sign > 0 && bodyB.PitchRadius.Sign > 0 &&
            bodyA.ToothCount > 0 && bodyB.ToothCount > 0)
        {
            var radiusRatio = new Rational(bodyA.PitchRadius, bodyB.PitchRadius);
            var toothRatio = new Rational(bodyA.ToothCount, bodyB.ToothCount);
            if (radiusRatio != toothRatio)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.PitchRatioMismatch,
                    DiagnosticSeverity.Error,
                    $"Contact '{contact.Id}' pitch-radius ratio does not match its tooth-count ratio.",
                    contact.Id));
            }
        }

        if (axes.TryGetValue(bodyA.AxisId, out var axisA) && axes.TryGetValue(bodyB.AxisId, out var axisB))
        {
            var dx = axisA.X - axisB.X;
            var dy = axisA.Y - axisB.Y;
            var actualSquared = (dx * dx) + (dy * dy);
            var expected = bodyA.PitchRadius + bodyB.PitchRadius;
            var expectedSquared = expected * expected;
            readbacks.Add(new SpatialContactReadback(contact.Id, actualSquared, expectedSquared));
            if (actualSquared != expectedSquared)
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticCodes.CenterDistanceMismatch,
                    DiagnosticSeverity.Error,
                    $"Contact '{contact.Id}' center distance does not equal the sum of pitch radii.",
                    contact.Id));
            }
        }
    }

    private static void ValidateIntermediateRealizations(
        IEnumerable<ExternalGearCoupling> couplings,
        IReadOnlyDictionary<string, List<SpatialContact>> contactsByConstraint,
        IReadOnlyDictionary<string, SpatialBody> bodies,
        ICollection<Diagnostic> diagnostics)
    {
        var ordered = couplings.OrderBy(coupling => coupling.Id, StringComparer.Ordinal).ToList();
        foreach (var incoming in ordered)
        {
            foreach (var outgoing in ordered)
            {
                if (!StringComparer.Ordinal.Equals(incoming.DrivenDofId, outgoing.DriverDofId) ||
                    !TryGetSingleContact(incoming.Id, contactsByConstraint, out var incomingContact) ||
                    !TryGetSingleContact(outgoing.Id, contactsByConstraint, out var outgoingContact) ||
                    !TryGetBodyForDof(incomingContact!, incoming.DrivenDofId, bodies, out var previousDriven) ||
                    !TryGetBodyForDof(outgoingContact!, outgoing.DriverDofId, bodies, out var nextDriver))
                {
                    continue;
                }

                var expectedIdler = incoming.DrivenTeeth == outgoing.DriverTeeth;
                var sharedAxisAndDof =
                    StringComparer.Ordinal.Equals(previousDriven!.AxisId, nextDriver!.AxisId) &&
                    StringComparer.Ordinal.Equals(previousDriven.DofId, nextDriver.DofId);
                var isSameBody = StringComparer.Ordinal.Equals(previousDriven.Id, nextDriver.Id);
                var valid = expectedIdler
                    ? sharedAxisAndDof && isSameBody && previousDriven.Layer == nextDriver.Layer
                    : sharedAxisAndDof && !isSameBody && previousDriven.Layer != nextDriver.Layer;
                if (!valid)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.IntermediateRealizationMismatch,
                        DiagnosticSeverity.Error,
                        expectedIdler
                            ? $"Intermediate DOF '{incoming.DrivenDofId}' must reuse one idler-compatible body on one layer."
                            : $"Intermediate DOF '{incoming.DrivenDofId}' must use distinct coaxial compound bodies on different layers.",
                        incoming.DrivenDofId));
                }
            }
        }
    }

    private static int ValidateUnrelatedSameLayerPairs(
        IReadOnlyDictionary<string, SpatialBody> bodies,
        IReadOnlyDictionary<string, SpatialAxis> axes,
        IEnumerable<SpatialContact> contacts,
        BigInteger clearance,
        ICollection<Diagnostic> diagnostics)
    {
        var intendedPairs = new HashSet<string>(
            contacts.Select(contact => PairId(contact.BodyAId, contact.BodyBId)),
            StringComparer.Ordinal);
        var ordered = bodies.Values.OrderBy(body => body.Id, StringComparer.Ordinal).ToList();
        var checks = 0;
        for (var leftIndex = 0; leftIndex < ordered.Count; leftIndex++)
        {
            for (var rightIndex = leftIndex + 1; rightIndex < ordered.Count; rightIndex++)
            {
                var left = ordered[leftIndex];
                var right = ordered[rightIndex];
                if (left.Layer != right.Layer || intendedPairs.Contains(PairId(left.Id, right.Id)) ||
                    !axes.TryGetValue(left.AxisId, out var leftAxis) ||
                    !axes.TryGetValue(right.AxisId, out var rightAxis) ||
                    left.PitchRadius.Sign <= 0 || right.PitchRadius.Sign <= 0)
                {
                    continue;
                }

                checks++;
                var dx = leftAxis.X - rightAxis.X;
                var dy = leftAxis.Y - rightAxis.Y;
                var actualSquared = (dx * dx) + (dy * dy);
                if (!NonPenetratingPitchDisks(new Rational(actualSquared), new Rational(left.PitchRadius), new Rational(right.PitchRadius), new Rational(clearance)))
                {
                    var pairId = PairId(left.Id, right.Id);
                    diagnostics.Add(new Diagnostic(
                        DiagnosticCodes.SameLayerCollision,
                        DiagnosticSeverity.Error,
                        $"Unrelated bodies '{left.Id}' and '{right.Id}' overlap or violate clearance on layer {left.Layer}.",
                        pairId));
                }
            }
        }

        return checks;
    }

    private static bool TryGetSingleContact(
        string constraintId,
        IReadOnlyDictionary<string, List<SpatialContact>> contactsByConstraint,
        out SpatialContact? contact)
    {
        if (contactsByConstraint.TryGetValue(constraintId, out var contacts) && contacts.Count == 1)
        {
            contact = contacts[0];
            return true;
        }

        contact = null;
        return false;
    }

    private static bool TryGetBodyForDof(
        SpatialContact contact,
        string dofId,
        IReadOnlyDictionary<string, SpatialBody> bodies,
        out SpatialBody? body)
    {
        if (bodies.TryGetValue(contact.BodyAId, out var bodyA) &&
            StringComparer.Ordinal.Equals(bodyA.DofId, dofId))
        {
            body = bodyA;
            return true;
        }

        if (bodies.TryGetValue(contact.BodyBId, out var bodyB) &&
            StringComparer.Ordinal.Equals(bodyB.DofId, dofId))
        {
            body = bodyB;
            return true;
        }

        body = null;
        return false;
    }

    private static string PairId(string left, string right)
    {
        return StringComparer.Ordinal.Compare(left, right) <= 0
            ? left + "|" + right
            : right + "|" + left;
    }

    private static Dictionary<string, T> BuildMap<T>(
        IEnumerable<T> source,
        Func<T, string> idSelector,
        string duplicateCode,
        string subjectKind,
        ICollection<Diagnostic> diagnostics)
    {
        var result = new Dictionary<string, T>(StringComparer.Ordinal);
        foreach (var item in source)
        {
            var id = idSelector(item);
            if (!result.TryAdd(id, item))
            {
                diagnostics.Add(new Diagnostic(
                    duplicateCode,
                    DiagnosticSeverity.Error,
                    $"{subjectKind} ID '{id}' is duplicated.",
                    id));
            }
        }

        return result;
    }
}
