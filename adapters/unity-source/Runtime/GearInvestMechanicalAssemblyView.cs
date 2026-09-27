using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using UnityEngine;

namespace GearInvest.Unity
{
    /// <summary>Projection of one current SDK assembly readback. This view never solves mechanics or creates member source drafts.</summary>
    public sealed class GearInvestMechanicalAssemblyView : MonoBehaviour
    {
        public sealed class MemberViewState
        {
            internal readonly Dictionary<string, Transform> bodies = new Dictionary<string, Transform>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Transform> features = new Dictionary<string, Transform>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Vector3> points = new Dictionary<string, Vector3>(StringComparer.Ordinal);
            internal readonly Dictionary<string, Vector3[]> samples = new Dictionary<string, Vector3[]>(StringComparer.Ordinal);
            internal Transform root;
            internal Bounds authoredBounds;
            internal string geometryUnavailable;
            internal bool ambiguousBodies;
            internal AssemblyMemberAnalysis analysis;
            public string InstanceId => analysis.InstanceId;
            public IReadOnlyDictionary<string, Transform> Bodies => bodies;
            public IReadOnlyDictionary<string, Transform> Features => features;
            public IReadOnlyDictionary<string, Vector3> CurrentFeaturePoints => points;
            public IReadOnlyDictionary<string, Vector3[]> CurrentLineSamples => samples;
            public string Status { get; internal set; }
            public string DisplayUnavailableReason { get; internal set; }
            public bool HasCurrentInput { get; internal set; }
            public bool HasCurrentOutput { get; internal set; }
            public bool NumericAvailable { get; internal set; }
            public bool DisplayAvailable { get; internal set; }
            public int MaterialPinCount { get; internal set; }
            public int MaterialLinkCount { get; internal set; }
            public int ContourPointCount { get; internal set; }
            public int WormHelixCount { get; internal set; }
            public int WheelTraceCount { get; internal set; }
            public int BeltMaterialMarkCount { get; internal set; }
            public string ActiveSlotId { get; internal set; }
            public string ActiveRecessId { get; internal set; }
            public Bounds ViewBounds { get; internal set; }
            public bool HasVisibleGeometry { get; internal set; }
            public int VisibleBodyCount => bodies.Values.Count(t => t.gameObject.activeSelf);
            public int VisibleFeatureCount => features.Values.Count(t => t.gameObject.activeSelf);
        }

        private readonly Dictionary<string, MemberViewState> members = new Dictionary<string, MemberViewState>(StringComparer.Ordinal);
        private readonly Dictionary<AssemblyComponentReference, Transform> bodies = new Dictionary<AssemblyComponentReference, Transform>();
        private readonly Dictionary<AssemblyComponentReference, Vector3> points = new Dictionary<AssemblyComponentReference, Vector3>();
        private readonly Dictionary<AssemblyComponentReference, LineRenderer> shaftLines = new Dictionary<AssemblyComponentReference, LineRenderer>();
        private readonly Dictionary<string, string> unavailable = new Dictionary<string, string>(StringComparer.Ordinal);
        private OrientedShaftEvaluation[] rootChannels = Array.Empty<OrientedShaftEvaluation>();
        private GameObject content;
        private Bounds rootBounds;
        private string rootGeometryUnavailable;
        public GearInvestOrientedMechanismView SourceView { get; private set; }
        public MechanicalAssemblyDraft Draft { get; private set; }
        public MechanicalAssemblyAnalysis Analysis { get; private set; }
        public MechanicalAssemblyEvaluation CurrentEvaluation { get; private set; }
        public string RootDisplayUnavailableReason { get; private set; }
        public IReadOnlyDictionary<string, MemberViewState> MemberViews => members;
        public IReadOnlyDictionary<AssemblyComponentReference, Transform> BodyTransforms => bodies;
        public IReadOnlyDictionary<AssemblyComponentReference, Vector3> CurrentFeaturePoints => points;
        public IReadOnlyDictionary<AssemblyComponentReference, LineRenderer> ShaftLines => shaftLines;
        public IReadOnlyDictionary<string, string> DisplayUnavailableReasons => unavailable;
        public Bounds ViewBounds { get; private set; }
        public Vector3 ViewCenter => ViewBounds.center;
        public float ViewRadius => Mathf.Max(10, ViewBounds.extents.magnitude);

        public void Build(MechanicalAssemblyDraft draft, MechanicalAssemblyAnalysis analysis)
        {
            Clear();
            if (draft == null || analysis == null) throw new ArgumentNullException();
            if (analysis.Draft.DraftId != draft.DraftId) throw new ArgumentException("Assembly analysis does not match the current draft.");
            Draft = draft; Analysis = analysis;
            content = new GameObject("Actual single-root mechanical assembly"); content.transform.SetParent(transform, false);
            var root = new GameObject("Root (one actual geometry)"); root.transform.SetParent(content.transform, false);
            SourceView = root.AddComponent<GearInvestOrientedMechanismView>();
            var mapping = draft.Definition.RootMapping;
            rootBounds = new Bounds(transform.position, Vector3.one * 20);
            if (mapping != null && mapping.PoseMm.IsProperCardinal && mapping.MillimetersPerSourceUnit > 0)
            {
                try
                {
                    root.transform.localPosition = V(mapping.PoseMm.Origin);
                    root.transform.localRotation = Rotation(mapping.PoseMm);
                    root.transform.localScale = Vector3.one * Number(mapping.MillimetersPerSourceUnit);
                    // The existing geometry view receives only channels already computed in this assembly evaluation.
                    // Its source-local axes are transformed exactly once by this root mapping transform.
                    SourceView.BuildDraft(draft.Definition.Root.Definition, requested =>
                        CurrentEvaluation != null && CurrentEvaluation.RootInput.Value == requested ? rootChannels : Array.Empty<OrientedShaftEvaluation>());
                    rootBounds = new Bounds(SourceView.ViewCenter, Vector3.one * SourceView.ViewRadius * 2);
                    foreach (var pair in SourceView.BodyTransforms)
                        bodies.Add(AssemblyComponentReference.Root(AssemblyComponentKind.Body, pair.Key), pair.Value);
                }
                catch (Exception e) when (DisplayException(e)) { SourceView.Clear(); rootGeometryUnavailable = e.Message; }
            }
            foreach (var local in analysis.Members)
            {
                var state = new MemberViewState { analysis = local, Status = "NoCurrentEvaluation" };
                state.root = new GameObject(local.InstanceId).transform; state.root.SetParent(content.transform, false);
                state.authoredBounds = new Bounds(transform.position, Vector3.one * 10);
                members.Add(local.InstanceId, state);
                foreach (var item in analysis.Inventory.Where(i => i.Reference.Owner == AssemblyOwnerKind.Member && i.Reference.MemberId == local.InstanceId && i.Reference.Kind == AssemblyComponentKind.Body))
                {
                    if (state.bodies.ContainsKey(item.Reference.LocalId))
                    { state.ambiguousBodies = true; state.geometryUnavailable = "Duplicate local body identity cannot authorize a normal body projection."; continue; }
                    var body = new GameObject(item.Reference.LocalId).transform; body.SetParent(state.root, false);
                    state.bodies.Add(item.Reference.LocalId, body); bodies.Add(item.Reference, body);
                    Marker(body, "body-axis-center", Vector3.zero, Color.white, 2.4f);
                }
                try { BuildGeometry(state); }
                catch (Exception e) when (DisplayException(e)) { state.geometryUnavailable = "Authored geometry cannot be projected: " + e.Message; }
            }
            HideCurrentMotion(); UpdateBounds();
        }

        public void Apply(MechanicalAssemblyEvaluation evaluation)
        {
            HideCurrentMotion();
            if (evaluation == null || Analysis == null || evaluation.AnalysisId != Analysis.AnalysisId)
                throw new ArgumentException("A current evaluation of the built assembly analysis is required; prior motion has been cleared.");
            CurrentEvaluation = evaluation;
            var rootShafts = Draft.Definition.Root.Definition.Shafts.ToDictionary(s => s.Id, StringComparer.Ordinal);
            rootChannels = evaluation.Shafts.Where(s => s.Reference.Owner == AssemblyOwnerKind.Root && rootShafts.ContainsKey(s.Reference.LocalId))
                .Select(s => new OrientedShaftEvaluation(s.Reference.LocalId, s.Turns.Value, rootShafts[s.Reference.LocalId].Frame.Z, s.Relation.Coefficient)).ToArray();
            if (SourceView != null && SourceView.IsDraftProjection)
            {
                try
                {
                    SourceView.gameObject.SetActive(true); SourceView.Apply(evaluation.RootInput.Value);
                    var known = new HashSet<string>(rootChannels.Select(s => s.ShaftId), StringComparer.Ordinal);
                    foreach (var body in Draft.Definition.Root.Definition.Bodies)
                        if (SourceView.BodyTransforms.TryGetValue(body.Id, out var shown)) shown.gameObject.SetActive(known.Contains(body.ShaftId));
                }
                catch (Exception ex) when (DisplayException(ex))
                { SourceView.gameObject.SetActive(false); RootDisplayUnavailableReason = ex.Message; }
            }
            else RootDisplayUnavailableReason = rootGeometryUnavailable ?? "A proper positive root length mapping is required for root geometry display.";
            foreach (var value in evaluation.Members)
            {
                if (!members.TryGetValue(value.InstanceId, out var state)) continue;
                state.Status = value.Status; state.HasCurrentInput = value.InputTurns.HasValue || value.GenevaSuffix != null;
                state.HasCurrentOutput = value.AffineOutput != null || value.NumericAvailable && (value.Recipe != null || value.GenevaSuffix != null);
                state.NumericAvailable = value.NumericAvailable;
                if (state.geometryUnavailable != null) Refuse(state, state.geometryUnavailable);
                try { if (!state.ambiguousBodies) ApplyBodies(state, value); }
                catch (Exception e) when (DisplayException(e)) { Refuse(state, "Current body projection unavailable: " + e.Message); }
                try
                {
                    ApplyFeatures(state, value);
                    var materialPresent = value.Display != null || state.analysis.Member.Declaration is AssemblyCrankSliderDeclaration;
                    state.DisplayAvailable = value.DisplayAvailable && materialPresent && state.DisplayUnavailableReason == null;
                    if (!state.DisplayAvailable && state.DisplayUnavailableReason == null)
                        Refuse(state, string.Join("; ", value.Diagnostics.Select(d => d.Code + ": " + d.Detail).DefaultIfEmpty("Optional display unavailable or not requested (" + value.Status + ").")));
                }
                catch (Exception e) when (DisplayException(e))
                {
                    // Optional display failure never hides already-known rigid bodies or changes another member.
                    foreach (var f in state.features.Values) f.gameObject.SetActive(false);
                    state.points.Clear(); state.samples.Clear(); state.MaterialPinCount = state.MaterialLinkCount = state.ContourPointCount = 0;
                    state.WormHelixCount = state.WheelTraceCount = state.BeltMaterialMarkCount = 0;
                    Refuse(state, "Optional display unavailable: " + e.Message);
                }
                foreach (var p in state.points) points[AssemblyComponentReference.Member(state.InstanceId, AssemblyComponentKind.Feature, p.Key)] = p.Value;
            }
            ApplyShaftSpans(evaluation); UpdateBounds();
        }

        public void HideCurrentMotion()
        {
            CurrentEvaluation = null; RootDisplayUnavailableReason = null; rootChannels = Array.Empty<OrientedShaftEvaluation>(); points.Clear(); unavailable.Clear();
            if (SourceView != null) SourceView.gameObject.SetActive(false);
            foreach (var body in bodies.Values) body.gameObject.SetActive(false);
            foreach (var line in shaftLines.Values) line.gameObject.SetActive(false);
            foreach (var state in members.Values)
            {
                foreach (var f in state.features.Values) f.gameObject.SetActive(false);
                state.points.Clear(); state.samples.Clear(); state.Status = "NoCurrentEvaluation"; state.DisplayUnavailableReason = null;
                state.HasCurrentInput = state.HasCurrentOutput = state.NumericAvailable = state.DisplayAvailable = false;
                state.MaterialPinCount = state.MaterialLinkCount = state.ContourPointCount = 0;
                state.WormHelixCount = state.WheelTraceCount = state.BeltMaterialMarkCount = 0;
                state.ActiveSlotId = state.ActiveRecessId = null;
            }
            UpdateBounds();
        }

        public void Clear()
        {
            HideCurrentMotion(); if (SourceView != null) SourceView.Clear();
            if (content != null) { content.SetActive(false); Destroy(content); }
            content = null; SourceView = null; Draft = null; Analysis = null;
            rootGeometryUnavailable = null;
            members.Clear(); bodies.Clear(); points.Clear(); shaftLines.Clear(); unavailable.Clear(); ViewBounds = new Bounds(transform.position, Vector3.one * 20);
        }
        public bool TryGetMemberBounds(string id, out Bounds bounds)
        { if (members.TryGetValue(id, out var state) && state.HasVisibleGeometry) { bounds = state.ViewBounds; return true; } bounds = default; return false; }

        private void BuildGeometry(MemberViewState s)
        {
            var input = s.analysis.Binding.FixedInputFrameMm;
            if (input != null) Anchor(s, input.Origin, 5);
            switch (s.analysis.Member.Declaration)
            {
                case AssemblyWormDeclaration a:
                    if (input != null) SetupRotor(s, a.Device.InputWormBodyId, input.Origin + input.Z * a.Device.InputPitchStation.Value, Number(a.Device.Worm.PitchRadius.Value));
                    SetupRotor(s, a.Device.OutputWheelBodyId, a.Device.OutputPitchCenterMm,
                        s.analysis.WormCompatibility?.Geometry == null ? 0 : Number(s.analysis.WormCompatibility.Geometry.WheelPitchRadiusMm)); break;
                case AssemblyOpenBeltDeclaration a:
                    SetupRotor(s, a.Device.InputPulleyBodyId, a.Device.InputPulleyCenterMm, Number(a.Device.InputPitchRadius.Value));
                    SetupRotor(s, a.Device.OutputPulleyBodyId, a.Device.OutputPulleyCenterMm, Number(a.Device.OutputPitchRadius.Value)); break;
                case AssemblyPitchChainDeclaration a:
                    var geometry = s.analysis.ChainCompatibility?.Geometry;
                    var radius = geometry == null ? 0 : Finite(PitchChainDisplay.ApproximateRadius(geometry.Pitch));
                    if (input != null) SetupRotor(s, a.Device.InputSprocketBodyId, input.Origin + input.Z * a.Device.InputSprocketStation.Value, radius);
                    SetupRotor(s, a.Device.OutputSprocketBodyId, a.Device.OutputSprocketCenterMm, radius); break;
                case AssemblyGenevaDeclaration a:
                    Anchor(s, a.Device.DriverCenterMm, Number((a.Device.WheelCenterMm - a.Device.DriverCenterMm).Dot(a.Device.CenterDirection)));
                    Anchor(s, a.Device.WheelCenterMm, Number((a.Device.WheelCenterMm - a.Device.DriverCenterMm).Dot(a.Device.CenterDirection))); break;
                case AssemblyCrankSliderDeclaration a:
                    SetupRotor(s, a.Device.CrankBodyId, a.Device.PivotMm, Number(a.Device.CrankRadius.Value));
                    Anchor(s, a.Device.PivotMm, Number(a.Device.CrankRadius.Value + a.Device.RodLength.Value));
                    GuideBounds(s, a.Device.GuideFrameMm, a.Device.GuideTravel); break;
                case AssemblyCamFollowerDeclaration a:
                    SetupRotor(s, a.Device.CamBodyId, a.Device.CenterMm, 0);
                    Anchor(s, a.Device.CenterMm, 20); GuideBounds(s, a.Device.GuideFrameMm, a.Device.GuideTravel); break;
            }
        }

        private void ApplyBodies(MemberViewState s, AssemblyMemberEvaluation e)
        {
            var input = s.analysis.Binding.FixedInputFrameMm;
            if (e.GenevaSuffix != null)
            {
                if (!(e.Display is AssemblyGenevaSuffixDisplay display) || input == null) return;
                if (s.analysis.Member.Declaration is AssemblyWormDeclaration worm)
                {
                    RotorApproximate(s, worm.Device.InputWormBodyId, input.Origin + input.Z * worm.Device.InputPitchStation.Value, input, display.InputMaterialTurnsModuloOne);
                    RotorApproximate(s, worm.Device.OutputWheelBodyId, worm.Device.OutputPitchCenterMm, worm.Device.OutputShaft.Frame, display.ShaftMaterialTurnsModuloOne);
                }
                if (s.analysis.Member.Declaration is AssemblyOpenBeltDeclaration belt)
                {
                    RotorApproximate(s, belt.Device.InputPulleyBodyId, belt.Device.InputPulleyCenterMm, input, display.InputMaterialTurnsModuloOne);
                    RotorApproximate(s, belt.Device.OutputPulleyBodyId, belt.Device.OutputPulleyCenterMm, belt.Device.OutputShaft.Frame, display.ShaftMaterialTurnsModuloOne);
                }
                return;
            }
            switch (s.analysis.Member.Declaration)
            {
                case AssemblyWormDeclaration a:
                    if (e.InputTurns.HasValue && input != null) Rotor(s, a.Device.InputWormBodyId, input.Origin + input.Z * a.Device.InputPitchStation.Value, input, e.InputTurns.Value.Value + a.Device.InputMountingPhase.Value);
                    if (e.WormOutput != null) Rotor(s, a.Device.OutputWheelBodyId, e.WormOutput.WheelCenterMm, a.Device.OutputShaft.Frame, e.WormOutput.ShaftTurns.Value + a.Device.OutputMountingPhase.Value); break;
                case AssemblyOpenBeltDeclaration a:
                    if (e.InputTurns.HasValue && input != null) Rotor(s, a.Device.InputPulleyBodyId, a.Device.InputPulleyCenterMm, input, e.InputTurns.Value.Value);
                    if (e.BeltOutput != null) Rotor(s, a.Device.OutputPulleyBodyId, a.Device.OutputPulleyCenterMm, a.Device.OutputShaft.Frame, e.BeltOutput.ShaftTurns.Value); break;
                case AssemblyPitchChainDeclaration a:
                    if (e.InputTurns.HasValue && input != null) Rotor(s, a.Device.InputSprocketBodyId, input.Origin + input.Z * a.Device.InputSprocketStation.Value, input, e.InputTurns.Value.Value + a.Device.InputMountingPhase.Value);
                    if (e.ChainOutput != null) Rotor(s, a.Device.OutputSprocketBodyId, a.Device.OutputSprocketCenterMm, a.Device.OutputShaft.Frame, e.ChainOutput.ShaftTurns.Value + a.Device.OutputMountingPhase.Value); break;
                case AssemblyGenevaDeclaration a:
                    if (e.InputObservation is GenevaDriverObservation driver && driver.PhysicalPhaseTurns.HasValue)
                        PhysicalRotor(s, driver.BodyId, driver.CenterMm, a.Device.PlaneNormal, a.Device.TransverseDirection, driver.PhysicalPhaseTurns.Value);
                    var gp = e.NumericAvailable ? (e.Numeric as GenevaNumericComputation)?.Pose : null;
                    if (gp != null) Pose(s, a.Device.WheelBodyId, V(a.Device.WheelCenterMm), Quaternion.LookRotation(V(a.Device.PlaneNormal), GM(gp.WheelF))); break;
                case AssemblyCrankSliderDeclaration a:
                    if (e.InputObservation is CrankSliderCrankEvaluation crank && crank.PhysicalPhaseTurns.HasValue)
                        PhysicalRotor(s, crank.BodyId, crank.PivotMm, a.Device.PlaneNormal, a.Device.PlaneNormal.Cross(a.Device.GuideFrameMm.Z), crank.PhysicalPhaseTurns.Value);
                    var cp = e.NumericAvailable ? (e.Numeric as CrankSliderNumericComputation)?.Pose : null;
                    if (cp != null && e.Recipe is CrankSliderPoseRecipe cr)
                    {
                        Pose(s, a.Device.RodBodyId, CM(cp.RodMidpointMm), Quaternion.LookRotation(V(cr.Descriptor.PlaneNormal), CM(cp.RodTransverseDirection)));
                        Pose(s, a.Device.SliderBodyId, CM(cp.SliderPinMm), Rotation(cr.SliderOrientation));
                    }
                    break;
                case AssemblyCamFollowerDeclaration a:
                    if (e.InputObservation is CamFollowerCamEvaluation cam && cam.PhysicalPhaseTurns.HasValue)
                        PhysicalRotor(s, cam.BodyId, cam.CenterMm, a.Device.PlaneNormal, a.Device.PlaneNormal.Cross(a.Device.GuideFrameMm.Z), cam.PhysicalPhaseTurns.Value);
                    if (e.NumericAvailable && e.Recipe is CamFollowerPoseRecipe recipe)
                        Pose(s, a.Device.FollowerBodyId, V(recipe.FollowerReferencePointMm), Rotation(a.Device.GuideFrameMm)); break;
            }
        }

        private void ApplyFeatures(MemberViewState s, AssemblyMemberEvaluation e)
        {
            var worm = e.Display as WormDriveDisplayPose ?? (e.Display as AssemblyGenevaSuffixDisplay)?.Worm;
            if (worm != null)
            {
                foreach (var p in worm.WormHelices.Concat(worm.WheelToothTraces)) FeatureLine(s, p.Id, p.Points.Select(WP), Color.yellow);
                FeatureDot(s, "worm-material-zero", WP(worm.WormMaterialMarker), Color.white);
                FeatureDot(s, "wheel-material-zero", WP(worm.WheelMaterialMarker), Color.cyan);
                s.WormHelixCount = worm.WormHelices.Count; s.WheelTraceCount = worm.WheelToothTraces.Count;
            }
            if (e.Display is OpenBeltDisplayRoute belt && e.BeltOutput != null)
            {
                FeatureLine(s, "belt-loop", belt.SampleLoop().Select(BP), new Color(.9f, .7f, .25f));
                for (var i = 0; i < 12; i++) FeatureDot(s, "belt-material-" + i, BP(belt.SampleMaterialMark(e.BeltOutput.BeltMaterialTravelPiCoefficientMm, new Rational(i, 12))), i == 0 ? Color.white : Color.yellow);
                s.BeltMaterialMarkCount = 12;
            }
            if (e.Display is AssemblyGenevaSuffixDisplay suffix && suffix.Belt != null)
            {
                FeatureLine(s, "belt-loop", suffix.Belt.SampleLoop().Select(BP), new Color(.9f, .7f, .25f));
                for (var i = 0; i < suffix.BeltMaterialMarksMm.Count; i++) FeatureDot(s, "belt-material-" + i, BP(suffix.BeltMaterialMarksMm[i]), i == 0 ? Color.white : Color.yellow);
                s.BeltMaterialMarkCount = suffix.BeltMaterialMarksMm.Count;
            }
            if (e.Display is PitchChainDisplayPose chain)
            {
                foreach (var pin in chain.Pins) FeatureDot(s, pin.PinId, PP(pin.Position), pin.MaterialIndex == 0 ? Color.white : Color.yellow);
                foreach (var link in chain.Links) FeatureLine(s, link.LinkId, new[] { PP(link.Start), PP(link.End) }, link.MaterialIndex % 2 == 0 ? Color.cyan : Color.magenta, false, 1.4f);
                s.MaterialPinCount = chain.Pins.Count; s.MaterialLinkCount = chain.Links.Count;
            }
            if (s.analysis.Member.Declaration is AssemblyGenevaDeclaration g) GenevaFeatures(s, e, g.Device);
            if (s.analysis.Member.Declaration is AssemblyCrankSliderDeclaration c)
            {
                if (c.Device.GuidePresent) Guide(s, c.Device.GuideId, c.Device.GuideFrameMm, c.Device.GuideTravel);
                var pose = e.NumericAvailable ? (e.Numeric as CrankSliderNumericComputation)?.Pose : null;
                if (pose != null)
                {
                    FeatureDot(s, c.Device.CrankPinId, CM(pose.CrankPinMm), Color.yellow);
                    FeatureDot(s, c.Device.SliderPinId, CM(pose.SliderPinMm), Color.cyan);
                    FeatureLine(s, "rod-current", new[] { CM(pose.CrankPinMm), CM(pose.SliderPinMm) }, Color.white, false, 2);
                    FeatureLine(s, "crank-current", new[] { V(c.Device.PivotMm), CM(pose.CrankPinMm) }, Color.yellow, false, 2);
                }
            }
            if (s.analysis.Member.Declaration is AssemblyCamFollowerDeclaration ca)
            {
                if (ca.Device.GuidePresent) Guide(s, ca.Device.GuideId, ca.Device.GuideFrameMm, ca.Device.GuideTravel);
                if (e.Display is AssemblyCamDisplaySamples display && display.IsAvailable)
                {
                    FeatureLine(s, "cam-current-contour", display.ContourPointsMm.Select(AM), Color.yellow, true, 1.2f);
                    FeatureDot(s, "cam-material-zero", AM(display.MaterialMarkerMm), Color.white); s.ContourPointCount = display.ContourPointsMm.Count;
                }
                if (e.NumericAvailable && e.Recipe is CamFollowerPoseRecipe recipe && e.Numeric is CamNumericResult numeric && numeric.IsAvailable && numeric.Point != null)
                {
                    var p = recipe.FollowerReferencePointMm; var f = recipe.Descriptor.InPlanePerpendicular;
                    FeatureLine(s, "follower-face", new[] { V(p + f * ca.Device.FollowerFace.Lower.Value), V(p + f * ca.Device.FollowerFace.Upper.Value) }, Color.cyan, false, 2);
                    FeatureDot(s, "cam-contact", AM(numeric.Point), Color.green);
                    FeatureDot(s, "follower-reference", V(p), Color.white);
                }
            }
        }

        private void GenevaFeatures(MemberViewState s, AssemblyMemberEvaluation e, GenevaDeviceDefinition g)
        {
            var pose = e.NumericAvailable ? (e.Numeric as GenevaNumericComputation)?.Pose : null;
            var driver = e.Display as GenevaDriverNumericComputation;
            var display = e.Display as GenevaNumericComputation;
            var materialPose = display != null && display.IsAvailable ? display.Pose : null;
            var features = materialPose?.FeaturePoints ?? (driver != null && driver.IsAvailable ? driver.FeaturePoints : null);
            if (features != null) foreach (var p in features) s.points[p.Id] = GM(p.PointMm);
            var pin = pose?.PinMm ?? (driver != null && driver.IsAvailable ? driver.PinMm : null);
            if (pin != null)
            {
                FeatureLine(s, "driver-material-ray", new[] { V(g.DriverCenterMm), GM(pin) }, Color.yellow);
                if (g.PinPresent) FeatureDot(s, g.PinId, GM(pin), Color.yellow);
                if (g.IdealLock.Present) NamedLine(s, g.IdealLock.DriverFeatureId, Enumerable.Range(0, 16).Select(i => g.IdealLock.DriverFeatureId + "/circle-" + i.ToString("D2")), Color.cyan, true);
            }
            if (pose == null) return;
            var recipe = e.Recipe as GenevaPoseRecipe; s.ActiveSlotId = recipe?.SlotId; s.ActiveRecessId = recipe?.RecessId;
            foreach (var id in g.Wheel.SlotIds) NamedLine(s, id, new[] { id + "/root", id + "/mouth" }, id == s.ActiveSlotId ? Color.yellow : Color.cyan);
            if (g.IdealLock.Present) foreach (var id in g.IdealLock.RecessIds) NamedLine(s, id, Enumerable.Range(0, 3).Select(i => id + "/arc-" + i), id == s.ActiveRecessId ? Color.green : Color.magenta);
            NamedLine(s, "wheel-envelope", Enumerable.Range(0, 24).Select(i => g.WheelBodyId + "/schematic-mouth-" + i.ToString("D2")), Color.white, true);
            if (s.points.TryGetValue(g.Wheel.SlotIds[0] + "/mouth", out var marker))
            { FeatureDot(s, "wheel-material-zero", marker, Color.white); FeatureLine(s, "wheel-material-ray", new[] { V(g.WheelCenterMm), marker }, Color.white); }
        }

        private void SetupRotor(MemberViewState s, string id, ExactVector3 center, float radius)
        {
            if (!s.bodies.TryGetValue(id, out var body)) return;
            body.localPosition = V(center); radius = Mathf.Abs(radius); Anchor(s, center, radius + 4);
            if (radius == 0) return; // Unknown geometry gets no fabricated pitch contour.
            var circle = Enumerable.Range(0, 48).Select(i => new Vector3(Mathf.Cos(2 * Mathf.PI * i / 48) * radius, Mathf.Sin(2 * Mathf.PI * i / 48) * radius, 0)).ToArray();
            var rim = Line(body, "schematic-pitch-rim", Color.gray, .7f, true); rim.positionCount = circle.Length; rim.SetPositions(circle);
            var ray = Line(body, "physical-material-zero-ray", Color.white, 1); ray.positionCount = 2; ray.SetPositions(new[] { Vector3.zero, Vector3.right * radius });
        }
        private void Rotor(MemberViewState s, string id, ExactVector3 center, OrientedFrame frame, Rational turns)
        { if (frame.IsProperCardinal) Pose(s, id, V(center), Quaternion.AngleAxis(360 * Wrapped(turns), V(frame.Z)) * Rotation(frame)); }
        private void RotorApproximate(MemberViewState s, string id, ExactVector3 center, OrientedFrame frame, double boundedTurns)
        { if (frame.IsProperCardinal) Pose(s, id, V(center), Quaternion.AngleAxis(360 * Finite(boundedTurns), V(frame.Z)) * Rotation(frame)); }
        private void PhysicalRotor(MemberViewState s, string id, ExactVector3 center, ExactVector3 normal, ExactVector3 zeroTransverse, Rational physicalPhase)
        { Pose(s, id, V(center), Quaternion.AngleAxis(360 * Wrapped(physicalPhase), V(normal)) * Quaternion.LookRotation(V(normal), V(zeroTransverse))); }
        private void Pose(MemberViewState s, string id, Vector3 center, Quaternion rotation)
        { if (s.bodies.TryGetValue(id, out var body)) { body.localPosition = center; body.localRotation = rotation; body.gameObject.SetActive(true); } }
        private void Anchor(MemberViewState s, ExactVector3 p, float radius)
        { var scale = transform.lossyScale; s.authoredBounds.Encapsulate(new Bounds(transform.TransformPoint(V(p)), Vector3.one * Mathf.Max(4, Mathf.Abs(radius) * 2 * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z))))); }
        private void GuideBounds(MemberViewState s, OrientedFrame f, ExactQuantityInterval range)
        { Anchor(s, f.Origin + f.Z * range.Lower.Value, 8); Anchor(s, f.Origin + f.Z * range.Upper.Value, 8); }
        private void Guide(MemberViewState s, string id, OrientedFrame f, ExactQuantityInterval range)
        { FeatureLine(s, id + "/authored-guide", new[] { V(f.Origin + f.Z * range.Lower.Value), V(f.Origin + f.Z * range.Upper.Value) }, Color.gray, false, .6f); }
        private void NamedLine(MemberViewState s, string id, IEnumerable<string> ids, Color color, bool loop = false)
        { var names = ids.ToArray(); if (names.All(s.points.ContainsKey)) FeatureLine(s, id, names.Select(n => s.points[n]), color, loop); }
        private void FeatureLine(MemberViewState s, string id, IEnumerable<Vector3> values, Color color, bool loop = false, float width = 1)
        {
            var poolKey = "line:" + id;
            if (!s.features.TryGetValue(poolKey, out var t)) { t = Line(s.root, id, color, width, loop).transform; s.features.Add(poolKey, t); }
            var line = t.GetComponent<LineRenderer>(); var samples = values.ToArray(); line.loop = loop;
            line.positionCount = samples.Length; line.SetPositions(samples); GearInvestPresentationMaterial.Apply(line, color); t.gameObject.SetActive(samples.Length > 0);
            s.samples[id] = samples;
        }
        private void FeatureDot(MemberViewState s, string id, Vector3 p, Color color)
        {
            // The feature map is separate from the body map; a feature never becomes an upstream shaft or driver.
            var poolKey = "point:" + id;
            if (!s.features.TryGetValue(poolKey, out var t)) { t = Marker(s.root, id, p, color, 2); s.features.Add(poolKey, t); }
            t.localPosition = p; t.gameObject.SetActive(true); s.points[id] = p;
        }
        private void Refuse(MemberViewState s, string reason)
        { s.DisplayAvailable = false; s.DisplayUnavailableReason = reason; unavailable[s.InstanceId] = reason; }
        private void ApplyShaftSpans(MechanicalAssemblyEvaluation evaluation)
        {
            // Visual extents only: endpoints are existing current body centers and declared valid mounting stations.
            // No connectivity, ratio, phase, or geometric admission is inferred here.
            var spans = evaluation.Shafts.Select(x => new KeyValuePair<AssemblyComponentReference, OrientedFrame>(x.Reference, x.FixedFrameMm)).ToList();
            foreach (var node in Analysis.Members.Where(x => x.GenevaShaftMotion != null))
            {
                if (!evaluation.Members.Any(x => x.InstanceId == node.InstanceId && x.NumericAvailable)) continue;
                var frame = node.Member.Declaration is AssemblyGenevaDeclaration g ? g.Device.OutputShaft.Frame :
                    node.Member.Declaration is AssemblyWormDeclaration w ? w.Device.OutputShaft.Frame : ((AssemblyOpenBeltDeclaration)node.Member.Declaration).Device.OutputShaft.Frame;
                var id = node.Member.Declaration is AssemblyGenevaDeclaration gd ? gd.Device.OutputShaft.Id :
                    node.Member.Declaration is AssemblyWormDeclaration wd ? wd.Device.OutputShaft.Id : ((AssemblyOpenBeltDeclaration)node.Member.Declaration).Device.OutputShaft.Id;
                spans.Add(new KeyValuePair<AssemblyComponentReference, OrientedFrame>(AssemblyComponentReference.Member(node.InstanceId, AssemblyComponentKind.Shaft, id), frame));
            }
            foreach (var shaft in spans)
            {
                try
                {
                    var origin = V(shaft.Value.Origin); var axis = V(shaft.Value.Z);
                    var stations = new List<float> { 0 };
                    foreach (var m in Analysis.Members.Where(m => m.Binding.MountingValidity && m.Member.InputBinding != null && m.Member.InputBinding.UpstreamShaft.Equals(shaft.Key)))
                        stations.Add(Number(m.Member.InputBinding.MountingStation.Value));
                    foreach (var item in Analysis.Inventory.Where(i => i.Reference.Kind == AssemblyComponentKind.Body && i.MountedShaft != null && i.MountedShaft.Equals(shaft.Key)))
                        if (bodies.TryGetValue(item.Reference, out var body) && body.gameObject.activeInHierarchy)
                            stations.Add(Vector3.Dot(transform.InverseTransformPoint(body.position) - origin, axis));
                    if (!shaftLines.TryGetValue(shaft.Key, out var line))
                    { line = Line(content.transform, "actual-shaft-span-" + shaft.Key.LocalId, Color.gray, .7f); shaftLines.Add(shaft.Key, line); }
                    line.positionCount = 2; line.SetPositions(new[] { origin + axis * (stations.Min() - 4), origin + axis * (stations.Max() + 4) }); line.gameObject.SetActive(true);
                }
                catch (Exception ex) when (DisplayException(ex))
                {
                    if (shaft.Key.Owner == AssemblyOwnerKind.Member && members.TryGetValue(shaft.Key.MemberId, out var state)) Refuse(state, "Shaft span display unavailable: " + ex.Message);
                    else RootDisplayUnavailableReason = "Shaft span display unavailable: " + ex.Message;
                }
            }
        }
        private void UpdateBounds()
        {
            foreach (var state in members.Values)
            {
                state.HasVisibleGeometry = VisibleBounds(state.root, out var current);
                state.ViewBounds = current;
            }
            // Camera framing follows actual current material, including every live branch and shaft span.
            // Hidden or unavailable members' authored extents cannot make the surviving geometry illegible.
            ViewBounds = content != null && VisibleBounds(content.transform, out var bounds) ? bounds : new Bounds(transform.position, Vector3.one * 20);
        }
        private static bool VisibleBounds(Transform owner, out Bounds bounds)
        {
            bounds = default; var found = false;
            foreach (var renderer in owner.GetComponentsInChildren<Renderer>())
            {
                if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                if (renderer is LineRenderer line && line.positionCount == 0) continue;
                if (found) bounds.Encapsulate(renderer.bounds); else { bounds = renderer.bounds; found = true; }
            }
            return found;
        }
        private static LineRenderer Line(Transform parent, string id, Color color, float width, bool loop = false)
        {
            var t = new GameObject(id).transform; t.SetParent(parent, false); var line = t.gameObject.AddComponent<LineRenderer>();
            line.useWorldSpace = false; line.loop = loop; line.positionCount = 0; line.startWidth = line.endWidth = width;
            GearInvestPresentationMaterial.Apply(line, color); return line;
        }
        private static Transform Marker(Transform parent, string id, Vector3 point, Color color, float size)
        {
            var t = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform; t.name = id; t.SetParent(parent, false); t.localPosition = point; t.localScale = Vector3.one * size;
            var collider = t.GetComponent<Collider>(); if (collider != null) Destroy(collider); GearInvestPresentationMaterial.Apply(t.GetComponent<Renderer>(), color); return t;
        }
        private static bool DisplayException(Exception e) => e is ArgumentException || e is ArithmeticException || e is WormDriveDisplayUnavailableException || e is OpenBeltDisplayUnavailableException || e is PitchChainDisplayUnavailableException;
        private static float Wrapped(Rational turns) => Number(new Rational(turns.Numerator % turns.Denominator, turns.Denominator));
        private static float Number(Rational v) => GearInvestOrientedMechanismView.N(v);
        private static Vector3 V(ExactVector3 v) => GearInvestOrientedMechanismView.V(v);
        private static Quaternion Rotation(OrientedFrame f) => GearInvestOrientedMechanismView.Rotation(f);
        private static float Finite(double v) { if (double.IsNaN(v) || double.IsInfinity(v) || Math.Abs(v) > 1e8) throw new ArgumentException("Display coordinate is not finitely representable."); return (float)v; }
        private static Vector3 WP(WormDriveDisplayPoint p) => new Vector3(Finite(p.X), Finite(p.Y), Finite(p.Z));
        private static Vector3 BP(OpenBeltDisplayPoint p) => new Vector3(Finite(p.X), Finite(p.Y), Finite(p.Z));
        private static Vector3 PP(PitchChainDisplayPoint p) => new Vector3(Finite(p.X), Finite(p.Y), Finite(p.Z));
        private static Vector3 GM(GenevaVectorInterval p) => GearInvestGenevaView.Mid(p);
        private static Vector3 CM(CrankSliderVectorInterval p) => GearInvestCrankSliderView.Mid(p);
        private static Vector3 AM(CamVectorInterval p) => GearInvestCamFollowerView.Mid(p);
    }
}
