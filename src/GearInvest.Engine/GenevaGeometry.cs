using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Layout;

namespace GearInvest.Engine;

public static partial class GenevaAnalyzer
{
    public static GenevaCompatibilityResult Query(GenevaDraft draft, GenevaNumericRequest? geometryRequest = null)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var request = geometryRequest ?? GenevaNumericRequest.Default;
        var d = draft.Definition;
        return QueryLocal(d.Device, d.Output, BoundMechanicalInputContext.FromLegacy(d.Source, d.SourceMapping, d.Device.SourceShaftId, d.Device.SourcePortId), request, draft.DraftId);
    }

    internal static GenevaCompatibilityResult QueryLocal(GenevaDeviceDefinition g, GenevaOutputDefinition output,
        BoundMechanicalInputContext input, GenevaNumericRequest request, string identity)
    {
        var source = input.LegacySource?.Definition; var wheel = g.Wheel; var arc = g.IdealLock;
        var checks = new List<OrientedDomainCheck>(); var facts = new List<MechanicalExactFact>(); var issues = new List<MechanicalDiagnostic>();
        var comparisons = new List<GenevaLengthComparison>(); var unsupported = false; var driverMounting = false; var pinSlot = false; var lockMate = false;
        Rational? distance = null, gammaIn = null, epsilonIn = null, gammaOut = null, epsilonOut = null; OrientedFrame? mapped = null; GenevaIdealLockProof? proof = null;
        CrankSliderNumerics.Context? context = null;
        void Check(string domain, bool pass, string code, string detail, bool outside = false)
        {
            checks.Add(new(domain, g.Id, pass ? OrientedCheckVerdict.Pass : OrientedCheckVerdict.Fail, true, detail));
            if (!pass) { unsupported |= outside; issues.Add(new(code, domain, related: new[] { new MechanicalReference("Geneva", g.Id) }, affectedOutputs: new[] { output.Key }, detail: detail)); }
        }
        void Undecided(string domain, string code, string detail)
        { checks.Add(new(domain, g.Id, OrientedCheckVerdict.Inconclusive, true, detail)); issues.Add(new(code, domain, affectedOutputs: new[] { output.Key }, detail: detail)); }
        GenevaCompatibilityResult Result() => new(identity, g.Id, request,
            checks.All(x => x.Verdict == OrientedCheckVerdict.Pass) ? ConnectionCompatibilityVerdict.CompatibleWithinProfile :
            unsupported ? ConnectionCompatibilityVerdict.Unsupported : checks.Any(x => x.Verdict == OrientedCheckVerdict.Fail) ? ConnectionCompatibilityVerdict.Incompatible : ConnectionCompatibilityVerdict.Inconclusive,
            driverMounting, pinSlot, lockMate, distance, mapped, gammaIn, epsilonIn, gammaOut, epsilonOut, proof, context?.Work ?? 0, comparisons, checks, facts, issues);
        try
        {
            var standalone = source is not null && source.Shafts.Count == 1 && source.Shafts[0].IsPrescribed && source.RootShaftId == source.Shafts[0].Id &&
                source.RootShaftId == g.SourceShaftId && source.Bodies.Count == 0 && source.Contacts.Count == 0 && source.Connections.Count == 0 &&
                source.Outputs.Count == 0 && source.KeepOuts.Count == 0 && source.Ports.All(p => p.ShaftId == source.RootShaftId);
            Check("SupportedSourceProfile", !input.IsLegacy ? input.BindingAdmitted : input.LegacySource!.OriginalArtifactBytes is not null ? input.LegacySource.ImportedProfile == MechanicalAuthoringProfile.PlanarExport ||
                OrientedTwoOutputProfile.IsSupported(input.LegacySource.ImportedProfile ?? "") : standalone, "UnsupportedSourceProfile", "Retain a complete supported actual source, never an extracted scalar surrogate.", true);
            Check("SupportedGenevaProfile", wheel.SlotCount >= 4 && wheel.SlotCount <= 12 && g.MechanismKind == "External" &&
                g.PinKind == "Point" && g.PinCount == 1 && wheel.SlotKind == "StraightRadial", "UnsupportedGenevaProfile", "External N4..12, one point pin and equally spaced radial slot centerlines only.", true);
            Check("SinglePrescribedInput", !g.OutputShaft.IsPrescribed && (source is null ? input.RootInputId is not null : source.Shafts.Count(s => s.IsPrescribed) == 1 && source.Shafts.Any(s => s.Id == source.RootShaftId && s.IsPrescribed)),
                "UnsupportedGenevaProfile", "The retained prescribed source is the sole input; output shaft is not an independent driver.", true);
            Check("SeparateShaftsAndBodies", g.DriverBodyId != g.WheelBodyId && !input.HasBodyId(g.DriverBodyId) && !input.HasBodyId(g.WheelBodyId) &&
                !input.HasShaftId(g.OutputShaft.Id), "InvalidBodyIdentity", "Preserve original gears and shafts; add distinct driver, non-gear wheel and output shaft.");
            Check("OutputBinding", output.ShaftId == g.OutputShaft.Id && output.BodyId == g.WheelBodyId &&
                (output.TerminalSign == 1 || output.TerminalSign == -1) && output.TerminalDatum.Kind == QuantityKind.AngularPosition,
                "InvalidOutputBinding", "Terminal binds the new actual output shaft; sign/datum calibrate readout, not world rotation.");
            var mappingValid = input.MappingValid;
            Check("SourceLengthMapping", mappingValid, "InvalidSourceLengthMapping", "Map source positions to mm once with an explicit positive scale and proper cardinal pose.");
            var shaft = input.Shaft;
            var sourceMount = shaft?.Frame.IsProperCardinal == true && mappingValid;
            if (sourceMount)
            {
                mapped = input.FixedFrameMm!;
                driverMounting = g.DriverStation.Kind == QuantityKind.LinearPosition && g.DriverMountingTurns.Kind == QuantityKind.AngularPosition && g.AxesFixed &&
                    g.DriverCenterMm == mapped.Origin + mapped.Z * g.DriverStation.Value;
                if (g.SourcePortId is not null)
                {
                    var port = input.Port;
                    var validPort = port is not null && port.ShaftId == g.SourceShaftId && port.Frame.IsProperCardinal && MechanicalConnectionPredicates.PortMounting(port, shaft!);
                    Check("SourcePortBinding", validPort, "InvalidDriverMounting", "An optional source port is mounting evidence, not a second phase sign.");
                    driverMounting &= validPort;
                    if (validPort) facts.Add(new("sourcePortSign.evidenceOnly", null, shaft!.Frame.Z.Dot(port!.Frame.Z)));
                }
            }
            Check("DriverMounting", driverMounting, "InvalidDriverMounting", "Od equals actual mapped source origin plus explicit signed mm station; driver is an added fixed-axis body.");
            var basis = g.PlaneNormal.IsCardinal && g.CenterDirection.IsCardinal && g.PlaneNormal.Dot(g.CenterDirection) == 0;
            var outputFrame = g.OutputShaft.Frame;
            var axes = basis && mapped is not null && mapped.Z.Cross(g.PlaneNormal) == ExactVector3.Zero && outputFrame.IsProperCardinal && outputFrame.Z.Cross(g.PlaneNormal) == ExactVector3.Zero && g.AxesFixed;
            Check("PlanarAxes", axes, "InvalidPlanarAxes", "Both fixed actual shafts are parallel to n; (e,n cross e,n) is proper cardinal.");
            if (basis)
            {
                var delta = g.WheelCenterMm - g.DriverCenterMm; var c = delta.Dot(g.CenterDirection); GenevaProfile.Derived(c);
                if (c > 0 && delta == g.CenterDirection * c) distance = c;
            }
            Check("CenterPlacement", distance.HasValue, "InvalidCenterDistance", "Ow-Od is the positive rational distance C along e in the same mechanism plane.");
            var wheelMount = outputFrame.IsProperCardinal && g.WheelStation.Kind == QuantityKind.LinearPosition && g.WheelMountingTurns.Kind == QuantityKind.AngularPosition &&
                g.OutputReferenceTurns.Kind == QuantityKind.AngularPosition && g.WheelCenterMm == outputFrame.Origin + outputFrame.Z * g.WheelStation.Value;
            Check("WheelMounting", wheelMount, "InvalidWheelMounting", "Ow is derived from the real output-shaft frame and signed wheel station.");
            // A bad output placement must not erase an independently determined input body.
            if (basis && mapped is not null && mapped.Z.Cross(g.PlaneNormal) == ExactVector3.Zero)
            {
                epsilonIn = mapped.Z.Dot(g.PlaneNormal);
                gammaIn = PitchChainGeometry.QuarterTurn(mapped.X, g.CenterDirection, g.TransverseDirection) + epsilonIn.Value * g.DriverMountingTurns.Value;
                GenevaProfile.Derived(gammaIn.Value);
            }
            if (basis && outputFrame.IsProperCardinal && outputFrame.Z.Cross(g.PlaneNormal) == ExactVector3.Zero)
            {
                epsilonOut = outputFrame.Z.Dot(g.PlaneNormal);
                gammaOut = PitchChainGeometry.QuarterTurn(outputFrame.X, g.CenterDirection, g.TransverseDirection) + epsilonOut.Value * g.WheelMountingTurns.Value;
                GenevaProfile.Derived(gammaOut.Value);
            }
            var countValid = wheel.SlotCount >= 4 && wheel.SlotCount <= 12;
            Check("MaterialSlotInventory", countValid && wheel.SlotIds.Count == wheel.SlotCount, "InvalidSlotInventory", "Exactly N ordered stable material slot IDs, independent of active cycle.");
            var featureIds = new[] { g.PinId, arc.DriverFeatureId }.Concat(wheel.SlotIds).Concat(arc.RecessIds).ToArray();
            Check("DistinctFeatureIdentity", featureIds.Distinct(StringComparer.Ordinal).Count() == featureIds.Length, "InvalidFeatureIdentity", "Pin, integral locking feature, slots and recesses have distinct stable feature IDs.");
            var registration = countValid && g.RegistrationSlot >= 0 && g.RegistrationSlot < wheel.SlotCount && gammaOut.HasValue && epsilonOut.HasValue &&
                GenevaProfile.ModOne(gammaOut.Value + epsilonOut.Value * g.OutputReferenceTurns.Value + new Rational(g.RegistrationSlot, wheel.SlotCount)).IsZero;
            Check("AssemblyRegistration", registration, "InvalidAssemblyRegistration", "At physical phi0 material slot j0 points Ow->Od using actual output zero ray, mounting and reference.");
            if (!GenevaNumerics.Valid(request)) { Undecided("GeometryNumericPolicy", "InvalidNumericRequest", "Invalid bounded geometry comparison request; no inferred mechanical failure."); return Result(); }
            context = new CrankSliderNumerics.Context(request.MaximumWork);
            if (distance.HasValue && countValid)
            {
                var c = distance.Value; var b = new Rational(1, 2 * wheel.SlotCount);
                var orbit = GenevaLength.SinTurns(c, b); var mouth = GenevaLength.CosTurns(c, b);
                var orbitDecision = CompareLength("orbit", g.OrbitRadius, orbit, context, request); comparisons.Add(orbitDecision);
                var mouthDecision = CompareLength("mouth", wheel.MouthRadius, mouth, context, request); comparisons.Add(mouthDecision);
                void Equality(string domain, GenevaLengthComparison comparison, string code)
                {
                    if (comparison.Order == GenevaLengthOrder.Unresolved) Undecided(domain, comparison.Status.ToString(), comparison.Detail);
                    else Check(domain, comparison.Order == GenevaLengthOrder.Equal, code, "Selected specification is compared to the current independently derived standard construction; it is not reinterpreted.");
                }
                Equality("TangentEntryGeometry", orbitDecision, "IncompatibleTangentEntryGeometry");
                Equality("SlotMouthGeometry", mouthDecision, "InvalidSlotMouth");
                if (wheel.SlotRoot.Kind != QuantityKind.LinearPosition || wheel.SlotRoot.Value <= 0)
                    Check("FullIndexSlotCoverage", false, "InvalidSlotRoot", "Slot root is an explicit positive mm radius.");
                else
                {
                    var rootLimit = GenevaLength.Millimeters(c - wheel.SlotRoot.Value);
                    var rootDecision = CompareLength("root-clearance", g.OrbitRadius, rootLimit, context, request); comparisons.Add(rootDecision);
                    if (rootDecision.Order == GenevaLengthOrder.Unresolved) Undecided("FullIndexSlotCoverage", rootDecision.Status.ToString(), rootDecision.Detail);
                    else Check("FullIndexSlotCoverage", rootDecision.Order == GenevaLengthOrder.Less || rootDecision.Order == GenevaLengthOrder.Equal,
                        "SlotRootInterference", "Whole index radial range [C-r,Rm] must contain the finite slot root. Boundary samples do not prove this.");
                }
                facts.Add(new("centerDistance.mm", null, c)); facts.Add(new("indexFraction.driverAngle", null, new Rational(1, 2) - new Rational(1, wheel.SlotCount)));
                facts.Add(new("dwellFraction.driverAngle", null, new Rational(1, 2) + new Rational(1, wheel.SlotCount)));
            }
            pinSlot = checks.All(x => x.Verdict == OrientedCheckVerdict.Pass);
            Check("IdealLockPolicy", arc.Policy == GenevaProfile.IdealPhaseReleasedArcLock && arc.DriverSurfaceKind == "FullCircularConvexLocus",
                "UnsupportedIdealLock", "Driver full circular convex locus is rotation invariant; release is ideal and phase-dependent, not a certified relief solid.", true);
            var lockUnits = arc.DriverRadius.Kind == QuantityKind.LinearPosition && arc.RecessRadius.Kind == QuantityKind.LinearPosition &&
                arc.RecessCenterDistance.Kind == QuantityKind.LinearPosition && arc.PatchHalfWidth.Kind == QuantityKind.AngularPosition && arc.RecessMountingTurns.Kind == QuantityKind.AngularPosition;
            Check("IdealLockDimensions", lockUnits && arc.DriverRadius.Value > 0 && arc.DriverRadius == arc.RecessRadius,
                "InvalidLockMate", "Both actual surfaces have identical positive radius with opposite declared material sides.");
            Check("LockRecessRegistration", countValid && arc.RecessPitchCount == wheel.SlotCount && arc.RecessIds.Count == wheel.SlotCount &&
                distance.HasValue && arc.RecessCenterDistance.Value == distance.Value && GenevaProfile.ModOne(arc.RecessMountingTurns.Value).IsZero,
                "InvalidLockMate", "N matching half-pitch material recess centers have the selected distance C and explicit unshifted slot-relative registration.");
            var patchDomain = lockUnits && distance.HasValue && arc.DriverRadius.Value >= distance.Value / 3 && arc.DriverRadius.Value <= 2 * distance.Value / 3 &&
                arc.PatchHalfWidth.Value >= new Rational(1, 96) && arc.PatchHalfWidth.Value <= new Rational(1, 24);
            Check("FiniteArcLocalDomain", patchDomain, "UnsupportedLockLocalDomain", "Sufficient local domain rho/C in [1/3,2/3], h in [1/96,1/24] turns, not a general impossibility classification.", true);
            if (patchDomain && countValid && checks.Any(x => x.Domain == "SlotMouthGeometry" && x.Verdict == OrientedCheckVerdict.Pass))
                proof = new(distance!.Value, arc.DriverRadius.Value, arc.PatchHalfWidth.Value);
            Check("FiniteArcEnvelopeAndBilateralNormals", proof is not null && proof.EnvelopeSquaredMarginUpperMm2 <= 0,
                "InvalidLockMate", "Actual envelope margin is strictly below this nonpositive bound because cos(2pi h)>11/12. Whole finite patch lies strictly inside the selected standard mouth; witnesses at +/-h/2 exclude opposite local rotations. Driver rotation is tangent to the full circular locus.");
            var a = countValid ? new Rational(1, 4) - new Rational(1, 2 * wheel.SlotCount) : Rational.Zero;
            Check("DeclaredPhaseReleaseConsistency", countValid && arc.ReleaseWindow.Kind == QuantityKind.AngularPosition && arc.ReleaseWindow.Lower.Value == -a && arc.ReleaseWindow.Upper.Value == a,
                "LockWindowMismatch", "Selected release window exactly equals tangent-entry phase boundaries; no N-dependent silent replacement.");
            lockMate = checks.Where(x => x.Domain == "IdealLockPolicy" || x.Domain == "IdealLockDimensions" || x.Domain == "LockRecessRegistration" ||
                x.Domain == "FiniteArcLocalDomain" || x.Domain == "FiniteArcEnvelopeAndBilateralNormals" || x.Domain == "DeclaredPhaseReleaseConsistency").All(x => x.Verdict == OrientedCheckVerdict.Pass) && registration;
            Check("IndexingPinPresence", g.PinPresent, "MissingIndexingPin", "Without the actual pin, a lock cannot establish accumulated indexing.");
            Check("DwellLockPresence", arc.Present, "MissingDwellLock", "Without the lock, no retained full-cycle output is admitted, even at an indexing sample.");
            return Result();
        }
        catch (CrankSliderNumerics.Stop stop)
        { Undecided("GeometryResourceBounds", GenevaNumerics.Status(stop).ToString(), stop.Message); return Result(); }
        catch (ArgumentException exception) when (exception.Message.Contains("bound") || exception.Message.Contains("digit"))
        { Undecided("GeometryResourceBounds", "NumericResourceLimit", exception.Message); return Result(); }
    }

    private static GenevaLengthComparison CompareLength(string subject, GenevaLength left, GenevaLength right,
        CrankSliderNumerics.Context c, GenevaNumericRequest request)
    {
        var start = c.Work; var bits = 0; GenevaInterval? difference = null;
        GenevaLengthComparison Result(GenevaLengthOrder order, GenevaNumericStatus status, string detail) => new(subject, left, right, order, status, c.Work - start, bits, difference, detail);
        if (left.Equals(right)) return Result(GenevaLengthOrder.Equal, GenevaNumericStatus.ExactValue, "Identical normalized exact construction.");
        if (left.Kind == right.Kind && left.AngleTurns == right.AngleTurns)
            return Result(left.CoefficientMm < right.CoefficientMm ? GenevaLengthOrder.Less : GenevaLengthOrder.Greater, GenevaNumericStatus.ExactValue,
                "Same positive sine factor or rational unit; exact coefficient ordering.");
        var passes = 0;
        foreach (var precision in CamNumericalPrimitives.Precisions)
        {
            if (precision > request.MaximumPrecisionBits) break;
            if (passes++ == request.MaximumRefinements) return Result(GenevaLengthOrder.Unresolved, GenevaNumericStatus.IncompleteNumericBudget, "Geometry comparison refinement budget exhausted.");
            c.Step(); c.SetPrecision(precision); bits = precision;
            var interval = GenevaNumerics.LengthDifference(c, left, right); difference = new(interval);
            if (interval.Lower > 0) return Result(GenevaLengthOrder.Greater, GenevaNumericStatus.EnclosedValue, "Certified selected-length difference is strictly positive.");
            if (interval.Upper < 0) return Result(GenevaLengthOrder.Less, GenevaNumericStatus.EnclosedValue, "Certified selected-length difference is strictly negative.");
            if (interval.IsExact) return Result(GenevaLengthOrder.Equal, GenevaNumericStatus.ExactValue, "Proven exact rational collapse.");
        }
        return Result(GenevaLengthOrder.Unresolved, GenevaNumericStatus.NumericResourceLimit, "Supported normalization and bounded inequality did not establish equality or difference.");
    }

    public static GenevaMatchingGeometryProposal CreateMatchingGeometry(GenevaDraft draft)
    {
        if (draft is null) throw new ArgumentNullException(nameof(draft));
        var g = draft.Definition.Device; var count = g.Wheel.SlotCount; var delta = g.WheelCenterMm - g.DriverCenterMm; var c = delta.Dot(g.CenterDirection);
        if (count < 4 || count > 12 || !g.CenterDirection.IsCardinal || c <= 0 || delta != g.CenterDirection * c ||
            g.Wheel.SlotIds.Count != count || g.IdealLock.RecessIds.Count != count) throw new ArgumentException("Matching proposal requires supported count, actual positive aligned centers and explicit material IDs.");
        var b = new Rational(1, 2 * count); var a = new Rational(1, 4) - b; var old = g.IdealLock;
        var wheel = new GenevaWheelSpecification(count, GenevaLength.CosTurns(c, b), g.Wheel.SlotRoot, g.Wheel.SlotIds, g.Wheel.SlotKind);
        var arc = new GenevaIdealLockSpecification(old.DriverFeatureId, old.RecessIds, count, ExactQuantity.Millimeters(c), old.DriverRadius, old.DriverRadius,
            old.PatchHalfWidth, ExactQuantity.Turns(0), new ExactQuantityInterval(ExactQuantity.Turns(-a), ExactQuantity.Turns(a)), old.Present, old.Policy, old.DriverSurfaceKind);
        return new(draft, c, GenevaLength.SinTurns(c, b), wheel, arc);
    }
}
