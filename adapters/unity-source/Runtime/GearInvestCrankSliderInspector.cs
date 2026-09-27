using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GearInvest.Unity
{
    /// <summary>Optional authoring form. SDK owns length closure, branch, envelope, comparison and all numerical admission.</summary>
    public sealed class GearInvestCrankSliderInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public CrankSliderDraft Draft { get; private set; }
        public CrankSliderDraft Before { get; private set; }
        public CrankSliderAnalysis Analysis { get; private set; }
        public CrankSliderEvaluation Evaluation { get; private set; }
        public CrankSliderEditResult LastEdit { get; private set; }
        public CrankSliderOutputEquivalenceResult Comparison { get; private set; }
        public CrankSliderCompatibilityResult ConnectionQuery { get; private set; }
        public CrankSliderFinalizationResult Finalization { get; private set; }
        public GearInvestCrankSliderView View { get; private set; }
        public MechanicalDraft Source { get; private set; }
        public string Notice { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public string DisplayUnavailableReason { get; private set; }
        public int PendingOperationCount => pending.Count;
        public bool IsPlaying { get; private set; }
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<CrankSliderEditBatch> history = new List<CrankSliderEditBatch>();
        private readonly List<CrankSliderEditOperation> pending = new List<CrankSliderEditOperation>();
        private readonly List<CrankSliderOutputComparisonRequest> comparisons = new List<CrankSliderOutputComparisonRequest>();
        private CrankSliderDraft initial;
        private Font font; private Text notice, readback; private Camera sceneCamera;
        private CameraState previousCamera; private bool ownsSceneCamera, comparisonReadOnly;
        private double playStarted; private Rational playRoot;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";

        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
            { var e = new GameObject("Crank-slider EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); e.transform.SetParent(transform, false); }
            sceneCamera = Camera.main; ownsSceneCamera = sceneCamera == null;
            if (ownsSceneCamera) { var go = new GameObject("Crank-slider scene camera"); go.tag = "MainCamera"; go.transform.SetParent(transform, false); sceneCamera = go.AddComponent<Camera>(); }
            else previousCamera = new CameraState(sceneCamera);
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.035f, .055f, .085f);
            sceneCamera.orthographic = true; sceneCamera.rect = new Rect(700f / 1600, 520f / 1050, 900f / 1600, 530f / 1050);
            var uiCamera = new GameObject("Crank-slider UI camera").AddComponent<Camera>(); uiCamera.transform.SetParent(transform, false);
            uiCamera.cullingMask = 1 << 5; uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.depth = sceneCamera.depth + 10;
            uiCamera.transform.position = new Vector3(0, 0, -1000); uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 30;
            var canvasObject = new GameObject("Crank-slider authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Crank-slider controls", 0, 0, 700, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "PLANAR CRANK-SLIDER / CERTIFIED POSITION", 12, 9, 675, 28, 20);
            Label(panel, "Exact length closure, explicit branch and finite guide. No force, inertia or solid-clearance claim.", 12, 40, 675, 30, 12);
            var viewport = GearInvestAuthoringWidgets.Box(panel, "Scrollable crank-slider parameters", 0, 74, 700, 610, new Color(.055f, .07f, .1f));
            viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var form = GearInvestAuthoringWidgets.Rect(viewport, "Explicit crank-slider inputs", 0, 0, 700, 1100); scroll.content = form; var y = 0f;
            void Field(string key, string title, string value) { Inputs.Add(key, GearInvestAuthoringWidgets.Field(font, form, key, title, value, y)); y += 32; }
            Field("file", "Source / draft / session / artifact file", ""); Field("shaft", "Actual retained source shaft", "output"); Field("port", "Optional retained port (no second sign)", "");
            Field("scale", "Explicit mm per source unit", "1"); Field("pose", "Source pose origin(mm);X;Y;Z", Identity);
            Field("radius", "Crank radius r (mm)", "30"); Field("rod", "Fixed connecting rod length l (mm)", "50");
            Field("station", "Signed pivot station along actual shaft (mm)", "40"); Field("pivot", "Explicit world crank pivot O (mm)", "60,0,40");
            Field("normal", "Mechanism plane normal n", "0,0,1"); Field("guide-frame", "Guide frame origin(mm);X;Y;Z [Z=g]", "60,0,40;0,1,0;0,0,1;1,0,0");
            Field("offset", "Signed transverse guide offset e (mm)", "0"); Field("datum", "Longitudinal guide datum d (mm)", "0");
            Field("branch", "Assembly branch +1/-1 (none is invalid)", "1"); Field("travel", "Declared finite guide lower,upper (mm)", "20,80");
            Field("stroke", "Independent required stroke mm / none", "none"); Field("reference", "Required terminal mm / none, reference root", "none,0");
            Field("terminal", "Readout sign,datum (body unchanged)", "1,0"); Field("mounting", "Signed crank mounting phase (turns)", "0");
            Field("required", "Additional required validation domains (comma)", "");
            Field("operation", "Typed edit: Radius/Rod/GuideOffset/Branch...", "Radius"); Field("value", "Edit value / source contact / boolean flags", "");
            Field("comparison", "Scope; mapped-output sign,datum (mm)", "SliderScalar;1,0"); Field("input-map", "Explicit uAfter=alpha*uBefore+beta", "1,0");
            Field("world-map", "After-world to before-world origin;X;Y;Z", Identity);
            Field("root", "Absolute unwrapped root turns", "0"); Field("width", "Requested absolute enclosure width (mm)", "1/1000000000");
            Field("work", "Deterministic numeric work budget", "8192"); Field("precision", "Maximum dyadic precision bits", "512"); Field("refinements", "Maximum refinement passes", "4");
            Field("save", "New file path (never overwrite)", ""); form.sizeDelta = new Vector2(700, y + 8);
            var row = 698f;
            void Four(string a, string at, Action aa, string b, string bt, Action ba, string c, string ct, Action ca, string d, string dt, Action da)
            { Button(panel, a, at, 12, row, 162, aa); Button(panel, b, bt, 182, row, 162, ba); Button(panel, c, ct, 352, row, 162, ca); Button(panel, d, dt, 522, row, 164, da); row += 39; }
            Four("example", "Public 20T -> 40T", PublicSource, "load-source", "Load whole source", LoadSource, "next-shaft", "Next actual shaft", NextShaft, "standalone", "Standalone shaft", StandaloneSource);
            Four("compose", "Compose / Analyze", Compose, "queue", "Queue explicit edit", Queue, "apply", "Apply whole batch", Apply, "clear-queue", "Clear queued edits", () => { pending.Clear(); Notice = "Queue cleared; current definition unchanged."; });
            Four("reanalyze", "Reanalyze", Analyze, "query", "Local query", Query, "compare", "Compare outputs", Compare, "mark-before", "Mark before", MarkBefore);
            Four("evaluate", "Evaluate root", Evaluate, "play", "Play / Pause", PlayPause, "finalize", "Finalize", FinalizeDraft, "save-artifact", "Save artifact", SaveArtifact);
            Four("load-draft", "Load draft", () => Adopt(sdk.LoadCrankSliderDraft(Inputs["file"].text)), "load-session", "Load session", LoadSession, "save-draft", "Save draft", SaveDraft, "save-session", "Save history", SaveSession);
            Four("reapply", "Reapply history", Reapply, "rebuild", "Rebuild artifact", Rebuild, "load-artifact", "Load artifact", () => Adopt(sdk.LoadCrankSliderArtifact(Inputs["file"].text).Request), "close", "Close current", Close);
            notice = Label(panel, "Choose source and Compose. Every later dimension, guide and target change is explicit.", 12, 950, 674, 85, 13);
            var results = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Exact crank-slider readback", 710, 540, 878, 499, new Color(.065f, .085f, .115f));
            results.gameObject.AddComponent<RectMask2D>(); var resultScroll = results.gameObject.AddComponent<ScrollRect>(); resultScroll.viewport = results; resultScroll.horizontal = false; resultScroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Rect(results, "Exact crank-slider results", 0, 0, 855, 485); resultScroll.content = content;
            readback = Label(content, "No current mechanism.", 7, 4, 845, 480, 14);
            View = new GameObject("Crank-slider position presentation").AddComponent<GearInvestCrankSliderView>(); View.transform.SetParent(transform, false);
            Notice = "Exact SDK position kinematics. Display midpoints never certify the mechanical result.";
        }
        private void PublicSource()
        {
            var kinematic = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") }, new[] { new ExternalGearCoupling("mesh", "input", "output", 20, 40) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", 0, 0), new SpatialAxis("output-axis", 60, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, 20, 20), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, 40, 40) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("crank-slider-public-spur-20-40", kinematic, spatial));
            if (!generated.IsSuccess) throw new InvalidOperationException("Public source generation failed.");
            Defaults(); SetSource(sdk.ImportMechanicalArtifact(sdk.WriteArtifact(generated.Candidates.Single()).Bytes), "output", 40);
        }
        private void StandaloneSource()
        {
            Defaults(); SetSource(new MechanicalDraft(new MechanicalDefinition("input", new[] { new OrientedShaft("input", OrientedFrame.Identity, true) },
                Array.Empty<OrientedGearBody>(), Array.Empty<MechanicalContact>(), clearancePolicy: MechanicalAuthoringProfile.PlanarClearance)), "input", 0);
        }
        private void Defaults()
        {
            Inputs["scale"].text = "1"; Inputs["pose"].text = Identity; Inputs["radius"].text = "30"; Inputs["rod"].text = "50";
            Inputs["offset"].text = Inputs["datum"].text = Inputs["mounting"].text = "0"; Inputs["branch"].text = "1"; Inputs["travel"].text = "20,80";
            Inputs["stroke"].text = "none"; Inputs["reference"].text = "none,0"; Inputs["terminal"].text = "1,0"; Inputs["required"].text = ""; Inputs["root"].text = "0";
        }
        private void SetSource(MechanicalDraft source, string shaft, Rational station)
        { Close(); Source = source; Inputs["shaft"].text = shaft; MountFields(station); Notice = "Complete source retained; shown initial mounting proposal is editable before Compose."; }
        private void LoadSource() { var source = sdk.ImportMechanicalArtifact(ReadSource()); SetSource(source, source.Definition.Outputs.FirstOrDefault()?.ShaftId ?? source.Definition.RootShaftId, 40); }
        private byte[] ReadSource()
        {
            var path = Inputs["file"].text; var f = new FileInfo(path);
            if (f.Length > 4 * 1024 * 1024 || (f.Attributes & FileAttributes.ReparsePoint) != 0) throw new FormatException("Bounded ordinary source file required.");
            var bytes = File.ReadAllBytes(path); if (bytes.Length > 4 * 1024 * 1024) throw new FormatException("Source byte bound exceeded."); return bytes;
        }
        private void NextShaft()
        { if (Source == null) throw new InvalidOperationException("Load a source first."); var shafts = Source.Definition.Shafts.ToArray(); var i = Array.FindIndex(shafts, s => s.Id == Inputs["shaft"].text); Inputs["shaft"].text = shafts[(i + 1) % shafts.Length].Id; MountFields(40); }
        private void MountFields(Rational station)
        {
            var frame = Mapping().FrameMm(Source.Definition.Shafts.Single(s => s.Id == Inputs["shaft"].text).Frame);
            var pivot = frame.Origin + frame.Z * station;
            Inputs["station"].text = station.ToString(); Inputs["pivot"].text = Point(pivot); Inputs["normal"].text = Point(frame.Z);
            Inputs["guide-frame"].text = FrameText(new OrientedFrame(pivot, frame.Y, frame.Z, frame.X));
            Inputs["offset"].text = Inputs["datum"].text = "0"; Inputs["port"].text = Source.Definition.Outputs.FirstOrDefault(o => o.ShaftId == Inputs["shaft"].text)?.PortId ?? "";
        }
        private SourceLengthMapping Mapping() => new SourceLengthMapping(Rational.Parse(Inputs["scale"].text), Frame(Inputs["pose"].text));
        private CrankSliderRequirement Requirement()
        { var reference = Parts(Inputs["reference"].text, ',', 2); return new CrankSliderRequirement(OptionalMm(Inputs["stroke"].text), OptionalMm(reference[0]), ExactQuantity.Turns(Rational.Parse(reference[1]))); }
        private ExactQuantityInterval Travel() { var p = Parts(Inputs["travel"].text, ',', 2); return new ExactQuantityInterval(Mm(p[0]), Mm(p[1])); }
        private void Compose()
        {
            if (Source == null) throw new InvalidOperationException("Load or generate an actual source first.");
            var terminal = Parts(Inputs["terminal"].text, ',', 2);
            var d = new PlanarCrankSliderDefinition("crank-slider", Inputs["shaft"].text, "crank-slider/crank", "crank-slider/rod", "crank-slider/slider", "crank-slider/guide",
                "crank-slider/linear", "crank-slider/crank-pin", "crank-slider/slider-pin", Mm(Inputs["radius"].text), Mm(Inputs["rod"].text), Vector(Inputs["pivot"].text),
                Mm(Inputs["station"].text), Vector(Inputs["normal"].text), Frame(Inputs["guide-frame"].text), ExactQuantity.Turns(Rational.Parse(Inputs["mounting"].text)),
                Branch(), Travel(), sourcePortId: string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text);
            Adopt(sdk.ComposeCrankSliderDraft(new CrankSliderDefinition(Source, Mapping(), d,
                new PrismaticOutputDefinition("slider-output", d.LinearDofId, d.SliderBodyId, d.SliderPinId, Integer(terminal[0]), Mm(terminal[1])), Requirement(), RequiredDomains())));
        }
        private void Adopt(CrankSliderDraft draft) { Close(); initial = Before = Draft = draft; Hydrate(); AllowComparison(); Analyze(); Notice = "Current declarations adopted; Analyze and Evaluate do not submit form changes."; }
        private void Hydrate()
        {
            var d = Draft.Definition; var c = d.Device; Source = d.Source;
            Inputs["shaft"].text = c.SourceShaftId; Inputs["port"].text = c.SourcePortId ?? ""; Inputs["scale"].text = d.SourceMapping?.MillimetersPerSourceUnit.ToString() ?? "";
            Inputs["pose"].text = d.SourceMapping == null ? "" : FrameText(d.SourceMapping.PoseMm);
            Inputs["radius"].text = c.CrankRadius.Value.ToString(); Inputs["rod"].text = c.RodLength.Value.ToString(); Inputs["station"].text = c.PivotStation.Value.ToString();
            Inputs["pivot"].text = Point(c.PivotMm); Inputs["normal"].text = Point(c.PlaneNormal); Inputs["guide-frame"].text = FrameText(c.GuideFrameMm);
            var displacement = c.GuideFrameMm.Origin - c.PivotMm;
            Inputs["datum"].text = displacement.Dot(c.GuideFrameMm.Z).ToString(); Inputs["offset"].text = displacement.Dot(c.PlaneNormal.Cross(c.GuideFrameMm.Z)).ToString();
            Inputs["branch"].text = c.AssemblyBranch?.ToString(CultureInfo.InvariantCulture) ?? "none"; Inputs["travel"].text = c.GuideTravel.Lower.Value + "," + c.GuideTravel.Upper.Value;
            Inputs["mounting"].text = c.MountingTurns.Value.ToString(); Inputs["stroke"].text = d.Requirement.RequiredStroke?.Value.ToString() ?? "none";
            Inputs["reference"].text = (d.Requirement.RequiredReferencePosition?.Value.ToString() ?? "none") + "," + d.Requirement.ReferenceRoot.Value;
            Inputs["terminal"].text = d.Output.TerminalSign + "," + d.Output.TerminalDatum.Value; Inputs["required"].text = string.Join(",", d.RequiredValidationDomains);
        }
        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Compose or load a crank-slider draft first."); }
        private void Analyze()
        {
            NeedDraft(); Analysis = sdk.AnalyzeCrankSliderDraft(Draft); Finalization = null; Comparison = null; ConnectionQuery = null; DisplayUnavailableReason = null;
            try { View.Build(Draft, Analysis); Fit(); } catch (CrankSliderDisplayUnavailableException e) { DisplayError(e); }
            Evaluate(); Notice = "Analyzed current submitted definition; full-cycle travel=" + Analysis.GuideTravelCoverage + ", target=" + Analysis.Target;
        }
        private CrankSliderNumericRequest NumericRequest() => new CrankSliderNumericRequest(Mm(Inputs["width"].text), Integer(Inputs["work"].text), Integer(Inputs["precision"].text), Integer(Inputs["refinements"].text));
        private void Evaluate()
        {
            NeedDraft(); if (Analysis == null) throw new InvalidOperationException("Analyze first.");
            Evaluation = sdk.EvaluateCrankSliderAnalysis(Analysis, ExactQuantity.Turns(Rational.Parse(Inputs["root"].text)), NumericRequest());
            if (DisplayUnavailableReason == null) try { View.Apply(Evaluation); } catch (CrankSliderDisplayUnavailableException e) { DisplayError(e); }
            Notice = "Absolute Evaluate " + Evaluation.Status + "; travel=" + Evaluation.TravelDecision + "; numeric=" + Evaluation.NumericComputation?.Status;
        }
        private void PlayPause()
        { NeedDraft(); IsPlaying = !IsPlaying; if (IsPlaying) { playRoot = Rational.Parse(Inputs["root"].text); playStarted = Time.realtimeSinceStartupAsDouble; } }
        private void Queue()
        {
            NeedDraft(); var c = Draft.Definition.Device; var list = new List<CrankSliderEditOperation>();
            // All fields, including every member of a compound input, parse before this queue is mutated.
            switch (Inputs["operation"].text)
            {
                case "Radius": list.Add(new SetCrankSliderRadiusEdit(c.Id, Mm(Inputs["radius"].text))); break;
                case "Rod": list.Add(new SetCrankSliderRodLengthEdit(c.Id, Mm(Inputs["rod"].text))); break;
                case "Guide": list.Add(new SetCrankSliderGuidePlacementEdit(c.Id, Frame(Inputs["guide-frame"].text), Vector(Inputs["normal"].text))); break;
                case "GuideOffset":
                    var frame = Frame(Inputs["guide-frame"].text); var normal = Vector(Inputs["normal"].text);
                    var origin = Vector(Inputs["pivot"].text) + frame.Z * Rational.Parse(Inputs["datum"].text) + normal.Cross(frame.Z) * Rational.Parse(Inputs["offset"].text);
                    list.Add(new SetCrankSliderGuidePlacementEdit(c.Id, new OrientedFrame(origin, frame.X, frame.Y, frame.Z), normal)); break;
                case "Pivot": list.Add(new SetCrankSliderPivotEdit(c.Id, Vector(Inputs["pivot"].text), Mm(Inputs["station"].text))); break;
                case "Mounting": list.Add(new SetCrankSliderMountingPhaseEdit(c.Id, ExactQuantity.Turns(Rational.Parse(Inputs["mounting"].text)))); break;
                case "Branch": list.Add(new SetCrankSliderBranchEdit(c.Id, Branch())); break;
                case "Travel": list.Add(new SetCrankSliderGuideTravelEdit(c.Id, Travel())); break;
                case "Terminal": var terminal = Parts(Inputs["terminal"].text, ',', 2); list.Add(new SetCrankSliderTerminalEdit(Draft.Definition.Output.Key, Integer(terminal[0]), Mm(terminal[1]))); break;
                case "Targets": list.Add(new SetCrankSliderRequirementsEdit(Draft.Definition.Output.Key, Requirement())); break;
                case "Geometry":
                    list.Add(new SetCrankSliderRadiusEdit(c.Id, Mm(Inputs["radius"].text))); list.Add(new SetCrankSliderRodLengthEdit(c.Id, Mm(Inputs["rod"].text)));
                    list.Add(new SetCrankSliderGuidePlacementEdit(c.Id, Frame(Inputs["guide-frame"].text), Vector(Inputs["normal"].text)));
                    list.Add(new SetCrankSliderPivotEdit(c.Id, Vector(Inputs["pivot"].text), Mm(Inputs["station"].text))); break;
                case "Transmission": list.Add(new SetCrankSliderTransmissionEdit(c.Id, Boolean(Inputs["value"].text))); break;
                case "Grounding": var flags = Parts(Inputs["value"].text, ',', 4).Select(Boolean).ToArray(); list.Add(new SetCrankSliderGroundingEdit(c.Id, flags[0], flags[1], flags[2], flags[3])); break;
                case "JointPresence": var pins = Parts(Inputs["value"].text, ',', 2).Select(Boolean).ToArray(); list.Add(new SetCrankSliderJointPresenceEdit(c.Id, pins[0], pins[1])); break;
                case "InputTopology": list.Add(new SetCrankSliderInputTopologyEdit(c.Id, Boolean(Inputs["value"].text))); break;
                case "Binding": list.Add(new SetCrankSliderBindingEdit(c.Id, Inputs["shaft"].text, string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text)); break;
                case "Mapping": list.Add(new SetCrankSliderSourceLengthMappingEdit(Mapping())); break;
                case "Required": list.Add(new SetCrankSliderRequiredValidationDomainsEdit(RequiredDomains())); break;
                case "SourceRemove": list.Add(SourceEdit(new RemoveContactEdit(Inputs["value"].text))); break;
                case "SourceRestore": list.Add(SourceEdit(new AddContactEdit(initial.Definition.Source.Definition.Contacts.Single(k => k.Id == Inputs["value"].text)))); break;
                default: throw new FormatException("Unknown typed crank-slider operation.");
            }
            if (pending.Count + list.Count > 128) throw new FormatException("Pending operation bound exceeded.");
            // Validate the whole prospective typed batch before publishing queued operations.
            var checkedBatch = new CrankSliderEditBatch(Draft.Revision, Draft.DefinitionId, pending.Concat(list));
            pending.AddRange(list); Notice = "Queued " + list.Count + " explicit operations; " + checkedBatch.Operations.Count + " pending. No automatic travel or target repair.";
        }
        private CrankSliderEditOperation SourceEdit(MechanicalEditOperation operation)
        {
            var source = Draft.Definition.Source;
            foreach (var queued in pending.OfType<ApplyCrankSliderSourceEditsEdit>())
            { var preview = sdk.ApplyMechanicalEdits(source, queued.Batch); if (preview.Draft == null) throw new InvalidOperationException("Earlier source transaction is rejected."); source = preview.Draft; }
            return new ApplyCrankSliderSourceEditsEdit(new MechanicalEditBatch(source.Revision, source.DefinitionId, new[] { operation }));
        }
        private void Apply()
        {
            NeedDraft(); var batch = new CrankSliderEditBatch(Draft.Revision, Draft.DefinitionId, pending); LastEdit = sdk.ApplyCrankSliderEdits(Draft, batch);
            history.Add(batch); pending.Clear(); Finalization = null;
            if (LastEdit.Draft != null) { Draft = LastEdit.Draft; Hydrate(); Analyze(); }
            Notice = LastEdit.Status + "; revision=" + Draft.Revision + "; current guide and target changed only when explicitly submitted.";
        }
        private void Query() { NeedDraft(); ConnectionQuery = sdk.QueryCrankSliderCompatibility(Draft); Notice = "Local " + ConnectionQuery.Verdict + "; exact geometry and strict full-cycle assembly, not solid contacts or force."; }
        private void MarkBefore() { NeedDraft(); Before = Draft; Comparison = null; AllowComparison(); Notice = "Marked before; only initial-to-current comparisons are persisted in this history."; }
        private void AllowComparison()
        { comparisonReadOnly = false; Inputs["comparison"].interactable = Inputs["input-map"].interactable = Inputs["world-map"].interactable = true; Buttons["compare"].interactable = true; Inputs["comparison"].text = "SliderScalar;1,0"; Inputs["input-map"].text = "1,0"; Inputs["world-map"].text = Identity; }
        private void Compare()
        {
            NeedDraft(); if (comparisonReadOnly) throw new InvalidOperationException("Saved extended comparison mapping is read-only; Mark before begins a new explicit request.");
            var parts = Parts(Inputs["comparison"].text, ';', 2); var output = Parts(parts[1], ',', 2); var input = Parts(Inputs["input-map"].text, ',', 2);
            if (!Enum.TryParse(parts[0], false, out CrankSliderOutputComparisonMode mode) || !Enum.IsDefined(typeof(CrankSliderOutputComparisonMode), mode)) throw new FormatException("Unknown output comparison mode.");
            var request = new CrankSliderOutputComparisonRequest(Before.Definition.Output.Key, Draft.Definition.Output.Key,
                Before.Definition.Source.Definition.RootShaftId, Draft.Definition.Source.Definition.RootShaftId,
                ExactQuantity.TurnsPerTurn(Rational.Parse(input[0])), ExactQuantity.Turns(Rational.Parse(input[1])), Integer(output[0]), Mm(output[1]), mode,
                Before.Definition.Output.ReferencePointId, Draft.Definition.Output.ReferencePointId,
                mode == CrankSliderOutputComparisonMode.WorldSliderPath ? Frame(Inputs["world-map"].text) : null, numericRequest: NumericRequest());
            Comparison = sdk.CompareCrankSliderOutputMotion(sdk.AnalyzeCrankSliderDraft(Before), Analysis, request);
            if (Before.DraftId == initial.DraftId && !comparisons.Any(r => r.RequestId == request.RequestId))
            { if (comparisons.Count >= CrankSliderProfile.MaxComparisons) throw new InvalidOperationException("Comparison history bound exceeded."); comparisons.Add(request); }
            Notice = "Slider-only " + Comparison.Verdict + " / " + Comparison.ProofRule + ". Sample agreement is not a function proof; crank/rod equality is not claimed.";
        }
        private void HydrateComparison()
        {
            AllowComparison(); if (comparisons.Count == 0) return; var r = comparisons[comparisons.Count - 1]; Before = initial;
            Comparison = sdk.CompareCrankSliderOutputMotion(sdk.AnalyzeCrankSliderDraft(initial), Analysis, r);
            // Preserve extended witness/domain/numeric mappings exactly; never replace them by a default request.
            comparisonReadOnly = true; Inputs["comparison"].text = r.Mode + ";" + r.Sign + "," + r.Delta.Value; Inputs["input-map"].text = r.Alpha.Value + "," + r.Beta.Value;
            Inputs["world-map"].text = r.AfterWorldToBeforeWorldMm == null ? Identity : FrameText(r.AfterWorldToBeforeWorldMm);
            Inputs["comparison"].interactable = Inputs["input-map"].interactable = Inputs["world-map"].interactable = false; Buttons["compare"].interactable = false;
        }
        private void FinalizeDraft() { NeedDraft(); Finalization = sdk.TryFinalizeCrankSliderDraft(Draft); Notice = "Finalize " + Finalization.Status + "; exact reference recipe only, numerical precision is a separate request."; }
        private void SaveArtifact()
        { NeedDraft(); if (Finalization == null || !Finalization.IsFinalized || Finalization.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Finalize this exact current definition first."); sdk.SaveCrankSliderFinalization(Finalization, Inputs["save"].text); Notice = "Source-aware artifact saved with staged exact readback."; }
        private void SaveDraft() { NeedDraft(); sdk.SaveCrankSliderDraft(Draft, Inputs["save"].text); Notice = "Current declarations saved; saved draft is not automatically an admitted artifact."; }
        private void SaveSession() { NeedDraft(); sdk.SaveCrankSliderEditSession(sdk.CreateCrankSliderEditSession(initial, history, comparisons), Inputs["save"].text); Notice = "Original plus ordered actual edits and explicit comparisons saved."; }
        private void LoadSession()
        { var session = sdk.LoadCrankSliderEditSession(Inputs["file"].text); Adopt(session.InitialDraft); history.AddRange(session.Batches); comparisons.AddRange(session.ComparisonRequests); Draft = session.CurrentDraft; Hydrate(); Analyze(); HydrateComparison(); Notice = "Loaded and freshly replayed session. Current exact fields hydrated."; }
        private void Reapply()
        { NeedDraft(); Draft = sdk.ReapplyCrankSliderEditSession(sdk.CreateCrankSliderEditSession(initial, history, comparisons)); Hydrate(); Analyze(); HydrateComparison(); Notice = "Ordered Reapply and fresh Analyze; current fields hydrated from actual resulting declarations."; }
        private void Rebuild()
        { var rebuilt = sdk.RebuildCrankSliderArtifact(sdk.LoadCrankSliderArtifact(Inputs["file"].text)); sdk.SaveCrankSliderArtifact(rebuilt.Artifact, Inputs["save"].text); if (!File.ReadAllBytes(Inputs["save"].text).SequenceEqual(rebuilt.Bytes)) throw new IOException("Saved rebuild differs."); Notice = "Current source, geometry, branch, full-cycle proof and root-zero exact recipe rebuilt from request."; }
        private void Close()
        {
            Draft = Before = initial = null; Source = null; Analysis = null; Evaluation = null; LastEdit = null; Comparison = null; ConnectionQuery = null; Finalization = null;
            history.Clear(); pending.Clear(); comparisons.Clear(); comparisonReadOnly = false; IsPlaying = false; DisplayUnavailableReason = null; if (View != null) View.Clear();
            Notice = "Closed. No cached crank, rod or slider frame is current.";
        }
        private void Update()
        {
            if (IsPlaying) try { var micros = (long)((Time.realtimeSinceStartupAsDouble - playStarted) * 1000000); Inputs["root"].text = (playRoot + new Rational(micros, 1000000)).ToString(); Evaluate(); } catch (Exception e) { InputError(e); }
            notice.text = Notice;
            if (Draft == null || Analysis == null) { readback.text = "No current crank-slider analysis. Complete source loaded=" + (Source != null); return; }
            var c = Draft.Definition.Device; var m = Analysis.Descriptor; var envelope = Analysis.Envelope;
            var text = "CURRENT " + Draft.DefinitionId + "  revision " + Draft.Revision + "\n" +
                "SOURCE shafts=" + Draft.Definition.Source.Definition.Shafts.Count + "; gear bodies=" + Draft.Definition.Source.Definition.Bodies.Count +
                "; separate crank/rod/slider=3; linear coordinate=1; prescribed slider=" + c.SliderIsPrescribed + "\n" +
                "r=" + c.CrankRadius + "; l=" + c.RodLength + "; branch=" + c.AssemblyBranch + "; rod present=" + c.TransmissionPresent + "\n" +
                "Source law=" + Law(Analysis.SourceRelation) + "; physical phase=" + (m == null ? "unknown" : Law(m.PhysicalPhaseRelation)) + "\n" +
                "O=" + c.PivotMm + "; guide G=" + c.GuideFrameMm.Origin + "; n=" + c.PlaneNormal + "; g=" + c.GuideFrameMm.Z + "\n" +
                "d=" + m?.GuideDatumMm + "; e=" + m?.GuideOffsetMm + "; Dmin=" + envelope?.MinimumRadicandMmSquared + " mm^2\n" +
                "x = r*cos(2*pi*phi) + B*sqrt(l^2-(e-r*sin(2*pi*phi))^2) - d\n" +
                "EXACT RANGE " + envelope?.Lower.CanonicalRepresentation + " .. " + envelope?.Upper.CanonicalRepresentation + " mm\n" +
                "Stroke=" + (envelope?.ExactStrokeMm?.ToString() ?? "sqrt(Aplus)-sqrt(Aminus)") + "; Aminus=" + envelope?.AminusMmSquared + "; Aplus=" + envelope?.AplusMmSquared + "\n" +
                "TRAVEL " + c.GuideTravel + " / " + Analysis.GuideTravelCoverage + "; target=" + Analysis.Target + "; determinacy=" + Analysis.Determinacy + "; export=" + Analysis.ExportAdmission + "\n";
            if (Evaluation != null)
            {
                text += "ABSOLUTE ROOT " + Evaluation.RootTurns + " -> " + Evaluation.Status + "; travel=" + Evaluation.TravelDecision + "\n";
                foreach (var r in Evaluation.Rotary) text += "ROTARY " + r.ShaftId + "=" + r.Turns + " turns\n";
                var numeric = Evaluation.NumericComputation; var p = Evaluation.Pose;
                text += "Recipe=" + Evaluation.Recipe?.RecipeId + "; source unwrapped=" + Evaluation.Recipe?.SourceTurns + "; trig reduced=" + Evaluation.Recipe?.ReducedPhaseTurns + "\n";
                text += "NUMERIC " + numeric?.Status + "; work=" + numeric?.Work + "; bits=" + numeric?.PrecisionBits + "; requested width=" + Evaluation.Request.AbsoluteWidth + "\n";
                text += p == null ? "NO NORMAL CONNECTED POSE. Diagnostic enclosures are not current geometry.\n" :
                    "CERTIFIED x(mm)=" + p.GuidePositionMm.CanonicalRepresentation + "\nCERTIFIED terminal(mm)=" + p.TerminalPositionMm.CanonicalRepresentation + "\n";
            }
            else text += "NO CURRENT EVALUATION; no previous success frame.\n";
            if (DisplayUnavailableReason != null) text += "DISPLAY_UNAVAILABLE: " + DisplayUnavailableReason + "\n";
            if (LastEdit != null) text += "EDIT " + LastEdit.Status + "; failed index=" + LastEdit.FailingOperationIndex + "; queued=" + pending.Count + "\n";
            if (Comparison != null) text += "COMPARE " + Comparison.Verdict + " / " + Comparison.ProofRule + "; " + Comparison.Scope + "; operating domains=" + Comparison.OperatingDomainsEqual + "\n";
            if (Finalization != null) text += "FINALIZE " + Finalization.Status + "\n";
            foreach (var check in Analysis.Checks) text += check.Domain + ": " + check.Verdict + (check.Required ? " [required]" : "") + "\n";
            foreach (var d in Analysis.Diagnostics.Concat(Evaluation == null ? Enumerable.Empty<MechanicalDiagnostic>() : Evaluation.Diagnostics)) text += d.Stage + "/" + d.Code + "\n";
            text += "Crank pivot fixed; rod derived from actual endpoints; slider orientation fixed. No inverse actuation, dynamics, physical guide contact or swept-solid proof.";
            readback.text = text; var height = Mathf.Max(485, readback.preferredHeight + 14); readback.rectTransform.sizeDelta = new Vector2(845, height); ((RectTransform)readback.transform.parent).sizeDelta = new Vector2(855, height + 10);
        }
        private void InputError(Exception error)
        {
            if (error is CrankSliderDisplayUnavailableException e) { DisplayError(e); return; }
            IsPlaying = false; Evaluation = null; Comparison = null; Finalization = null; ConnectionQuery = null; if (View != null) View.HideCurrentMotion();
            Notice = "INPUT/OPERATION ERROR: " + error.Message + "; no cached successful frame is current.";
        }
        private void DisplayError(CrankSliderDisplayUnavailableException error)
        { IsPlaying = false; DisplayUnavailableReason = error.Message; if (View != null) View.Clear(); Notice = "DISPLAY_UNAVAILABLE: " + error.Message + "; SDK exact result remains independent."; }
        private void Fit()
        {
            var center = View.ViewCenter; var radius = View.ViewRadius;
            sceneCamera.transform.position = center + new Vector3(.35f, -.65f, 1.5f).normalized * radius * 3; sceneCamera.transform.LookAt(center);
            var bounds = View.ViewBounds; var halfHeight = 0f; var halfWidth = 0f;
            foreach (var x in new[] { bounds.min.x, bounds.max.x }) foreach (var y in new[] { bounds.min.y, bounds.max.y }) foreach (var z in new[] { bounds.min.z, bounds.max.z })
            {
                var delta = new Vector3(x, y, z) - center;
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(delta, sceneCamera.transform.up)));
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(delta, sceneCamera.transform.right)));
            }
            sceneCamera.orthographicSize = Mathf.Max(15, Mathf.Max(halfHeight, halfWidth / sceneCamera.aspect) * 1.08f);
            sceneCamera.nearClipPlane = .1f; sceneCamera.farClipPlane = radius * 8 + 100;
        }
        private void OnDestroy() { if (!ownsSceneCamera && sceneCamera != null && previousCamera != null) previousCamera.Restore(sceneCamera); }
        private int? Branch() => Inputs["branch"].text == "none" ? (int?)null : Integer(Inputs["branch"].text);
        private string[] RequiredDomains() => Inputs["required"].text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        private static string Law(ExactAffineRelation? r) => r.HasValue ? r.Value.Coefficient + "*root+" + r.Value.Phase : "undetermined";
        private static int Integer(string value) => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var v) ? v : throw new FormatException("Bounded integer required.");
        private static bool Boolean(string value) => value == "true" ? true : value == "false" ? false : throw new FormatException("Explicit true or false required.");
        private static ExactQuantity Mm(string value) => ExactQuantity.Millimeters(Rational.Parse(value));
        private static ExactQuantity? OptionalMm(string value) => value == "none" ? (ExactQuantity?)null : Mm(value);
        private static string[] Parts(string value, char separator, int count) { var p = value.Split(separator); if (p.Length != count) throw new FormatException("Expected " + count + " explicit fields."); return p; }
        private static ExactVector3 Vector(string value) { var p = Parts(value, ',', 3).Select(Rational.Parse).ToArray(); return new ExactVector3(p[0], p[1], p[2]); }
        private static OrientedFrame Frame(string value) { var p = Parts(value, ';', 4).Select(Vector).ToArray(); return new OrientedFrame(p[0], p[1], p[2], p[3]); }
        private static string Point(ExactVector3 p) => p.X + "," + p.Y + "," + p.Z;
        private static string FrameText(OrientedFrame f) => Point(f.Origin) + ";" + Point(f.X) + ";" + Point(f.Y) + ";" + Point(f.Z);
        private Text Label(Transform parent, string text, float x, float y, float w, float h, int size) => GearInvestAuthoringWidgets.Label(font, parent, text, x, y, w, h, size, new Color(.9f, .94f, 1));
        private void Button(Transform parent, string key, string title, float x, float y, float w, Action action)
        { Buttons.Add(key, GearInvestAuthoringWidgets.Button(font, parent, key, title, x, y, w, () => { try { action(); } catch (Exception e) { InputError(e); } })); }
        private sealed class CameraState
        {
            private readonly Rect rect; private readonly Color background; private readonly CameraClearFlags clear; private readonly int mask;
            private readonly bool orthographic; private readonly float size, near, far; private readonly Vector3 position; private readonly Quaternion rotation;
            internal CameraState(Camera c) { rect = c.rect; background = c.backgroundColor; clear = c.clearFlags; mask = c.cullingMask; orthographic = c.orthographic; size = c.orthographicSize; near = c.nearClipPlane; far = c.farClipPlane; position = c.transform.position; rotation = c.transform.rotation; }
            internal void Restore(Camera c) { c.rect = rect; c.backgroundColor = background; c.clearFlags = clear; c.cullingMask = mask; c.orthographic = orthographic; c.orthographicSize = size; c.nearClipPlane = near; c.farClipPlane = far; c.transform.SetPositionAndRotation(position, rotation); }
        }
    }
}
