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
    /// <summary>Optional editable SDK consumer. No Geneva law, phase counter, numerical solver or geometry validator lives here.</summary>
    public sealed class GearInvestGenevaInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public GenevaDraft Draft { get; private set; }
        public GenevaDraft Before { get; private set; }
        public GenevaAnalysis Analysis { get; private set; }
        public GenevaEvaluation Evaluation { get; private set; }
        public GenevaEditResult LastEdit { get; private set; }
        public GenevaMotionComparisonResult Comparison { get; private set; }
        public GenevaCompatibilityResult ConnectionQuery { get; private set; }
        public GenevaMatchingGeometryProposal Proposal { get; private set; }
        public GenevaFinalizationResult Finalization { get; private set; }
        public GearInvestGenevaView View { get; private set; }
        public MechanicalDraft Source { get; private set; }
        public string Notice { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public string DisplayUnavailableReason { get; private set; }
        public bool IsPlaying { get; private set; }
        public int PendingOperationCount => pending.Count;
        public Camera SceneCamera => sceneCamera;
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<GenevaEditBatch> history = new List<GenevaEditBatch>();
        private readonly List<GenevaEditOperation> pending = new List<GenevaEditOperation>();
        private readonly List<GenevaMotionComparisonRequest> comparisons = new List<GenevaMotionComparisonRequest>();
        private GenevaDraft initial;
        private Font font; private Text notice, readback; private Camera sceneCamera;
        private CameraState previousCamera; private bool ownsCamera, comparisonReadOnly;
        private double playStarted; private Rational playRoot;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";
        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
            { var e = new GameObject("Geneva EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); e.transform.SetParent(transform, false); }
            sceneCamera = Camera.main; ownsCamera = sceneCamera == null;
            if (ownsCamera) { var go = new GameObject("Geneva scene camera"); go.tag = "MainCamera"; go.transform.SetParent(transform, false); sceneCamera = go.AddComponent<Camera>(); }
            else previousCamera = new CameraState(sceneCamera);
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.035f, .055f, .085f);
            sceneCamera.orthographic = true; sceneCamera.rect = new Rect(700f / 1600, 520f / 1050, 900f / 1600, 530f / 1050);
            var uiCamera = new GameObject("Geneva UI camera").AddComponent<Camera>(); uiCamera.transform.SetParent(transform, false);
            uiCamera.cullingMask = 1 << 5; uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.depth = sceneCamera.depth + 10;
            uiCamera.transform.position = new Vector3(0, 0, -1000); uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 30;
            var canvasObject = new GameObject("Geneva authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Geneva controls", 0, 0, 700, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "EXTERNAL GENEVA / IDEAL DWELL LOCK", 12, 9, 675, 28, 20);
            Label(panel, "Point pin / radial slot schematic. Phase release is ideal; relief solids and forces are NOT validated.", 12, 40, 675, 30, 12);
            var viewport = GearInvestAuthoringWidgets.Box(panel, "Scrollable Geneva parameters", 0, 74, 700, 590, new Color(.055f, .07f, .1f));
            viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var form = GearInvestAuthoringWidgets.Rect(viewport, "Explicit Geneva declarations", 0, 0, 700, 1200); scroll.content = form; var y = 0f;
            void Field(string key, string title, string value) { Inputs.Add(key, GearInvestAuthoringWidgets.Field(font, form, key, title, value, y)); y += 32; }
            Field("file", "Whole source / draft / session / artifact file", ""); Field("shaft", "Actual retained source shaft", "output"); Field("port", "Optional retained source port", "");
            Field("scale", "Explicit mm per source unit", "1"); Field("pose", "Source pose origin(mm);X;Y;Z", Identity);
            Field("count", "Selected slot count N (does NOT resize parts)", "4"); Field("orbit", "Selected orbit: mm,c or sin,c,turns", "sin,100,1/8");
            Field("mouth", "Selected slot mouth: mm,c or sin,c,turns", "sin,100,1/8"); Field("slot-root", "Selected slot root radius (mm)", "25");
            Field("driver-center", "Actual driver center Od (mm)", "60,0,40"); Field("driver-station", "Signed driver station on source shaft (mm)", "40");
            Field("normal", "Mechanism plane normal n", "0,0,1"); Field("direction", "Od to Ow unit center direction e", "1,0,0");
            Field("wheel-center", "Actual wheel center Ow (mm)", "160,0,40"); Field("wheel-frame", "Output shaft origin(mm);X;Y;Z", "160,0,40;1,0,0;0,1,0;0,0,1");
            Field("wheel-station", "Signed wheel station (mm)", "0"); Field("mounting", "Driver, wheel mounting (turns)", "0,0");
            Field("registration", "Output reference turns, material slot j0", "0,0"); Field("terminal", "Output readout sign,datum (turns)", "1,0");
            Field("lock", "Lock rhoD,rhoW,C,h,recessMount,pitchN", "50,50,100,1/48,0,4"); Field("window", "Selected ideal release lower,upper turns", "-1/8,1/8");
            Field("presence", "Pin present, lock present (true/false)", "true,true"); Field("targets", "Required step,dwellFraction / none", "1/4,3/4");
            Field("reference", "Required output turns / none, root turns", "none,0"); Field("required", "Additional required validation domains", "");
            Field("operation", "Edit: Count/Orbit/Mouth/Root/Lock/Targets/...", "Count"); Field("value", "Edit value / actual source contact ID", "");
            Field("comparison", "Scope; mapped-output sign,datum turns", "UnwrappedAngularOutput;1,0"); Field("input-map", "Explicit uAfter=alpha*uBefore+beta", "1,0");
            Field("root", "Absolute unwrapped input turns", "0"); Field("angular-width", "Requested angular width (turns)", "1/1000000000000");
            Field("linear-width", "Requested point width (mm)", "1/1000000000"); Field("direction-width", "Requested unit-direction width", "1/1000000000000");
            Field("work", "One aggregate numeric work budget", "32768"); Field("precision", "Maximum dyadic precision bits", "512"); Field("refinements", "Maximum refinement passes", "4");
            Field("save", "New output file (create only)", ""); form.sizeDelta = new Vector2(700, y + 8);
            var row = 674f;
            void Four(string a, string at, Action aa, string b, string bt, Action ba, string c, string ct, Action ca, string d, string dt, Action da)
            { Button(panel, a, at, 12, row, 162, aa); Button(panel, b, bt, 182, row, 162, ba); Button(panel, c, ct, 352, row, 162, ca); Button(panel, d, dt, 522, row, 164, da); row += 37; }
            Four("example", "Public 20T -> 40T", PublicSource, "load-source", "Load whole source", LoadSource, "standalone", "Standalone shaft", StandaloneSource, "compose", "Compose / Analyze", Compose);
            Four("proposal", "Propose matching", Propose, "queue-matching", "Queue proposal", QueueMatching, "queue", "Queue field edit", Queue, "apply", "Apply whole batch", Apply);
            Four("reanalyze", "Analyze current", Analyze, "query", "Local query", Query, "mark-before", "Mark before", MarkBefore, "compare", "Compare outputs", Compare);
            Four("evaluate", "Evaluate root", Evaluate, "play", "Play / Pause", PlayPause, "finalize", "Finalize", FinalizeDraft, "save-artifact", "Save artifact", SaveArtifact);
            Four("load-draft", "Load draft", () => Adopt(sdk.LoadGenevaDraft(Inputs["file"].text)), "load-session", "Load history", LoadSession, "save-draft", "Save draft", SaveDraft, "save-session", "Save history", SaveSession);
            Four("reapply", "Reapply history", Reapply, "rebuild", "Rebuild artifact", Rebuild, "load-artifact", "Load artifact", () => Adopt(sdk.LoadGenevaArtifact(Inputs["file"].text).Request), "close", "Close current", Close);
            Button(panel, "clear-queue", "Clear queued operations", 12, 900, 330, () => { pending.Clear(); Notice = "Queue cleared; authored definition unchanged."; });
            Button(panel, "fit", "Fit actual visible points", 352, 900, 334, Fit);
            notice = Label(panel, "Choose whole source and compose; each later dimensional correction is explicit.", 12, 947, 674, 88, 13);
            var results = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Geneva exact readback", 710, 540, 878, 499, new Color(.065f, .085f, .115f));
            results.gameObject.AddComponent<RectMask2D>(); var resultScroll = results.gameObject.AddComponent<ScrollRect>(); resultScroll.viewport = results; resultScroll.horizontal = false; resultScroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Rect(results, "Current SDK results", 0, 0, 855, 485); resultScroll.content = content;
            readback = Label(content, "No current mechanism.", 7, 4, 845, 480, 14);
            View = new GameObject("SDK Geneva material presentation").AddComponent<GearInvestGenevaView>(); View.transform.SetParent(transform, false);
            Notice = "Exact SDK kinematics with bounded point enclosures; display midpoints do not certify geometry.";
        }
        private void PublicSource()
        {
            var kinematic = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") }, new[] { new ExternalGearCoupling("mesh", "input", "output", 20, 40) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", 0, 0), new SpatialAxis("output-axis", 60, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, 20, 20), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, 40, 40) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("geneva-public-spur-20-40", kinematic, spatial));
            if (!generated.IsSuccess) throw new InvalidOperationException("Public source generation failed.");
            SetSource(sdk.ImportMechanicalArtifact(sdk.WriteArtifact(generated.Candidates.Single()).Bytes), "output", 40);
        }
        private void StandaloneSource() => SetSource(new MechanicalDraft(new MechanicalDefinition("input", new[] { new OrientedShaft("input", OrientedFrame.Identity, true) },
            Array.Empty<OrientedGearBody>(), Array.Empty<MechanicalContact>(), clearancePolicy: MechanicalAuthoringProfile.PlanarClearance)), "input", 0);
        private void LoadSource()
        {
            var path = Inputs["file"].text; var f = new FileInfo(path);
            if (f.Length > 4 * 1024 * 1024 || (f.Attributes & FileAttributes.ReparsePoint) != 0) throw new FormatException("Bounded ordinary source artifact required.");
            var bytes = File.ReadAllBytes(path); if (bytes.Length > 4 * 1024 * 1024) throw new FormatException("Source byte bound exceeded.");
            var source = sdk.ImportMechanicalArtifact(bytes); SetSource(source, source.Definition.Outputs.FirstOrDefault()?.ShaftId ?? source.Definition.RootShaftId, 40);
        }
        private void SetSource(MechanicalDraft source, string shaft, Rational station)
        {
            Close(); Source = source; Inputs["shaft"].text = shaft; Inputs["scale"].text = "1"; Inputs["pose"].text = Identity;
            var frame = Mapping().FrameMm(source.Definition.Shafts.Single(s => s.Id == shaft).Frame); var center = frame.Origin + frame.Z * station;
            Inputs["driver-station"].text = station.ToString(); Inputs["driver-center"].text = Point(center); Inputs["normal"].text = Point(frame.Z); Inputs["direction"].text = Point(frame.X);
            Inputs["wheel-center"].text = Point(center + frame.X * 100); Inputs["wheel-frame"].text = FrameText(new OrientedFrame(center + frame.X * 100, frame.X, frame.Y, frame.Z)); Inputs["wheel-station"].text = "0";
            Inputs["port"].text = source.Definition.Outputs.FirstOrDefault(o => o.ShaftId == shaft)?.PortId ?? "";
            Notice = "Whole source loaded. Editable initial mounting proposal only; compose submits independently selected parts.";
        }
        private SourceLengthMapping Mapping() => new SourceLengthMapping(R(Inputs["scale"].text), Frame(Inputs["pose"].text));
        private GenevaRequirement Requirement()
        { var p = Parts(Inputs["targets"].text, ',', 2); var reference = Parts(Inputs["reference"].text, ',', 2); return new GenevaRequirement(OptionalTurns(p[0]), p[1] == "none" ? (Rational?)null : R(p[1]), OptionalTurns(reference[0]), Turns(reference[1])); }
        private GenevaIdealLockSpecification Lock(string[] ids)
        {
            var v = Parts(Inputs["lock"].text, ',', 6); var window = Parts(Inputs["window"].text, ',', 2); var flags = Parts(Inputs["presence"].text, ',', 2);
            return new GenevaIdealLockSpecification("geneva/driver-lock", ids, Integer(v[5]), Mm(v[2]), Mm(v[0]), Mm(v[1]), Turns(v[3]), Turns(v[4]), new ExactQuantityInterval(Turns(window[0]), Turns(window[1])), Boolean(flags[1]));
        }
        private void Compose()
        {
            if (Source == null) throw new InvalidOperationException("Load or generate a whole source first.");
            var count = Integer(Inputs["count"].text); if (count < 0 || count > 32) throw new FormatException("Bounded material inventory required.");
            var slots = Enumerable.Range(0, count).Select(i => "geneva/slot-" + i.ToString("D2")).ToArray(); var recesses = Enumerable.Range(0, count).Select(i => "geneva/recess-" + i.ToString("D2")).ToArray();
            var mount = Parts(Inputs["mounting"].text, ',', 2); var registration = Parts(Inputs["registration"].text, ',', 2); var terminal = Parts(Inputs["terminal"].text, ',', 2); var flags = Parts(Inputs["presence"].text, ',', 2);
            var g = new GenevaDeviceDefinition("geneva", Inputs["shaft"].text, "geneva/driver-body", "geneva/point-pin", "geneva/wheel-body", Vector(Inputs["driver-center"].text), Mm(Inputs["driver-station"].text),
                Vector(Inputs["normal"].text), Vector(Inputs["direction"].text), new OrientedShaft("geneva/output-shaft", Frame(Inputs["wheel-frame"].text)), Vector(Inputs["wheel-center"].text), Mm(Inputs["wheel-station"].text),
                Turns(mount[0]), Turns(mount[1]), Turns(registration[0]), Integer(registration[1]), Length(Inputs["orbit"].text), new GenevaWheelSpecification(count, Length(Inputs["mouth"].text), Mm(Inputs["slot-root"].text), slots), Lock(recesses),
                Boolean(flags[0]), sourcePortId: string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text);
            Adopt(sdk.ComposeGenevaDraft(new GenevaDefinition(Source, Mapping(), g, new GenevaOutputDefinition("geneva-output", g.OutputShaft.Id, g.WheelBodyId, Integer(terminal[0]), Turns(terminal[1])), Requirement(), RequiredDomains())));
        }
        private void Adopt(GenevaDraft value) { Close(); initial = Before = Draft = value; Hydrate(); AllowComparison(); Analyze(); Notice = "Authored fields loaded; Analyze does not submit unsent fields."; }
        private void Hydrate()
        {
            var d = Draft.Definition; var g = d.Device; Source = d.Source;
            Inputs["shaft"].text = g.SourceShaftId; Inputs["port"].text = g.SourcePortId ?? ""; Inputs["scale"].text = d.SourceMapping?.MillimetersPerSourceUnit.ToString() ?? ""; Inputs["pose"].text = d.SourceMapping == null ? "" : FrameText(d.SourceMapping.PoseMm);
            Inputs["count"].text = g.Wheel.SlotCount.ToString(); Inputs["orbit"].text = LengthText(g.OrbitRadius); Inputs["mouth"].text = LengthText(g.Wheel.MouthRadius); Inputs["slot-root"].text = g.Wheel.SlotRoot.Value.ToString();
            Inputs["driver-center"].text = Point(g.DriverCenterMm); Inputs["driver-station"].text = g.DriverStation.Value.ToString(); Inputs["normal"].text = Point(g.PlaneNormal); Inputs["direction"].text = Point(g.CenterDirection);
            Inputs["wheel-center"].text = Point(g.WheelCenterMm); Inputs["wheel-frame"].text = FrameText(g.OutputShaft.Frame); Inputs["wheel-station"].text = g.WheelStation.Value.ToString();
            Inputs["mounting"].text = g.DriverMountingTurns.Value + "," + g.WheelMountingTurns.Value; Inputs["registration"].text = g.OutputReferenceTurns.Value + "," + g.RegistrationSlot;
            Inputs["terminal"].text = d.Output.TerminalSign + "," + d.Output.TerminalDatum.Value; HydrateLock(g.IdealLock);
            Inputs["presence"].text = g.PinPresent.ToString().ToLowerInvariant() + "," + g.IdealLock.Present.ToString().ToLowerInvariant();
            Inputs["targets"].text = (d.Requirement.RequiredIndexStep?.Value.ToString() ?? "none") + "," + (d.Requirement.RequiredDwellFraction?.ToString() ?? "none");
            Inputs["reference"].text = (d.Requirement.RequiredReferenceOutput?.Value.ToString() ?? "none") + "," + d.Requirement.ReferenceRoot.Value; Inputs["required"].text = string.Join(",", d.RequiredValidationDomains);
        }
        private void HydrateLock(GenevaIdealLockSpecification g)
        { Inputs["lock"].text = g.DriverRadius.Value + "," + g.RecessRadius.Value + "," + g.RecessCenterDistance.Value + "," + g.PatchHalfWidth.Value + "," + g.RecessMountingTurns.Value + "," + g.RecessPitchCount; Inputs["window"].text = g.ReleaseWindow.Lower.Value + "," + g.ReleaseWindow.Upper.Value; }
        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Compose or load a Geneva draft first."); }
        private GenevaNumericRequest NumericRequest() => new GenevaNumericRequest(Turns(Inputs["angular-width"].text), Mm(Inputs["linear-width"].text), R(Inputs["direction-width"].text), Integer(Inputs["work"].text), Integer(Inputs["precision"].text), Integer(Inputs["refinements"].text));
        private void Analyze()
        {
            NeedDraft(); Analysis = sdk.AnalyzeGenevaDraft(Draft); Finalization = null; Comparison = null; ConnectionQuery = null; DisplayUnavailableReason = null;
            try { View.Build(Draft, Analysis); } catch (ArgumentException e) { DisplayError(e); }
            Evaluate(); if (DisplayUnavailableReason == null) Fit(); Notice = "Current definition analyzed; full-cycle=" + Analysis.HasFullCycleMotion + ", targets=" + Analysis.Target;
        }
        private void Evaluate()
        {
            NeedDraft(); if (Analysis == null) throw new InvalidOperationException("Analyze first.");
            Evaluation = sdk.EvaluateGenevaAnalysis(Analysis, Turns(Inputs["root"].text), NumericRequest());
            if (DisplayUnavailableReason == null) try { View.Apply(Evaluation); } catch (ArgumentException e) { DisplayError(e); }
            Notice = "Absolute root evaluated: " + Evaluation.Status + "; one work budget " + Evaluation.NumericWork + "/" + Evaluation.Request.MaximumWork;
        }
        private void Propose()
        { NeedDraft(); Proposal = sdk.CreateMatchingGenevaGeometry(Draft); Inputs["orbit"].text = LengthText(Proposal.OrbitRadius); Inputs["mouth"].text = LengthText(Proposal.Wheel.MouthRadius); HydrateLock(Proposal.IdealLock); Notice = "Review matching proposal " + Proposal.ProposalId + ". Not applied or queued; root and independent targets retained."; }
        private void QueueMatching()
        {
            NeedDraft(); if (Proposal == null || Proposal.DraftId != Draft.DraftId) throw new InvalidOperationException("Propose matching against this current draft first.");
            AddPending(new GenevaEditOperation[] { new SetGenevaOrbitEdit(Draft.Definition.Device.Id, Proposal.OrbitRadius), new SetGenevaWheelEdit(Draft.Definition.Device.Id, Proposal.Wheel), new SetGenevaLockEdit(Draft.Definition.Device.Id, Proposal.IdealLock) });
            Notice = "Reviewed matching parts queued. Apply is separate; requirements are not changed.";
        }
        private void AddPending(IEnumerable<GenevaEditOperation> operations)
        { var list = operations.ToArray(); _ = new GenevaEditBatch(Draft.Revision, Draft.DefinitionId, pending.Concat(list)); pending.AddRange(list); }
        private void Queue()
        {
            NeedDraft(); var g = Draft.Definition.Device; var list = new List<GenevaEditOperation>();
            switch (Inputs["operation"].text)
            {
                case "Count": list.Add(new SetGenevaSlotCountEdit(g.Id, Integer(Inputs["count"].text))); break;
                case "Orbit": list.Add(new SetGenevaOrbitEdit(g.Id, Length(Inputs["orbit"].text))); break;
                case "Mouth": list.Add(new SetGenevaSlotMouthEdit(g.Id, Length(Inputs["mouth"].text))); break;
                case "Root": list.Add(new SetGenevaSlotRootEdit(g.Id, Mm(Inputs["slot-root"].text))); break;
                case "DriverCenter": list.Add(new SetGenevaDriverCenterEdit(g.Id, Vector(Inputs["driver-center"].text), Mm(Inputs["driver-station"].text))); break;
                case "OutputPlacement": list.Add(new SetGenevaOutputPlacementEdit(g.Id, Frame(Inputs["wheel-frame"].text), Vector(Inputs["wheel-center"].text), Mm(Inputs["wheel-station"].text))); break;
                case "Mounting": var m = Parts(Inputs["mounting"].text, ',', 2); list.Add(new SetGenevaDriverMountingPhaseEdit(g.Id, Turns(m[0]))); list.Add(new SetGenevaWheelMountingPhaseEdit(g.Id, Turns(m[1]))); break;
                case "Registration": var r = Parts(Inputs["registration"].text, ',', 2); list.Add(new SetGenevaRegistrationEdit(g.Id, Turns(r[0]), Integer(r[1]))); break;
                case "Lock": list.Add(new SetGenevaLockEdit(g.Id, Lock(g.IdealLock.RecessIds.ToArray()))); break;
                case "Window": var w = Parts(Inputs["window"].text, ',', 2); list.Add(new SetGenevaLockWindowEdit(g.Id, new ExactQuantityInterval(Turns(w[0]), Turns(w[1])))); break;
                case "Pin": list.Add(new SetGenevaPinPresenceEdit(g.Id, Boolean(Inputs["value"].text))); break;
                case "LockPresence": list.Add(new SetGenevaLockPresenceEdit(g.Id, Boolean(Inputs["value"].text))); break;
                case "Targets": list.Add(new SetGenevaRequirementsEdit(Draft.Definition.Output.Key, Requirement())); break;
                case "Terminal": var t = Parts(Inputs["terminal"].text, ',', 2); list.Add(new SetGenevaTerminalEdit(Draft.Definition.Output.Key, Integer(t[0]), Turns(t[1]))); break;
                case "ReverseAxis": list.Add(new ReverseGenevaOutputAxisEdit(g.Id)); break;
                case "ReverseTerminal": list.Add(new ReverseGenevaTerminalEdit(Draft.Definition.Output.Key)); break;
                case "Binding": list.Add(new SetGenevaBindingEdit(g.Id, Inputs["shaft"].text, string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text)); break;
                case "Mapping": list.Add(new SetGenevaSourceLengthMappingEdit(Mapping())); break;
                case "Required": list.Add(new SetGenevaRequiredValidationDomainsEdit(RequiredDomains())); break;
                case "SourceRemove": list.Add(SourceEdit(new RemoveContactEdit(Inputs["value"].text))); break;
                case "SourceRestore": list.Add(SourceEdit(new AddContactEdit(initial.Definition.Source.Definition.Contacts.Single(c => c.Id == Inputs["value"].text)))); break;
                default: throw new FormatException("Unknown typed Geneva edit. Unsubmitted fields do not change the model.");
            }
            AddPending(list); Notice = "Queued " + list.Count + " explicit edits (total " + pending.Count + "). Apply is atomic; no automatic part or target repair.";
        }
        private GenevaEditOperation SourceEdit(MechanicalEditOperation operation)
        {
            var source = Draft.Definition.Source;
            foreach (var queued in pending.OfType<ApplyGenevaSourceEditsEdit>())
            { var preview = sdk.ApplyMechanicalEdits(source, queued.Batch); if (preview.Draft == null) throw new InvalidOperationException("Earlier source transaction is rejected."); source = preview.Draft; }
            return new ApplyGenevaSourceEditsEdit(new MechanicalEditBatch(source.Revision, source.DefinitionId, new[] { operation }));
        }
        private void Apply()
        {
            NeedDraft(); var batch = new GenevaEditBatch(Draft.Revision, Draft.DefinitionId, pending); LastEdit = sdk.ApplyGenevaEdits(Draft, batch);
            history.Add(batch); pending.Clear(); Finalization = null; Comparison = null; ConnectionQuery = null; Proposal = null; Evaluation = null; View.HideCurrentMotion();
            if (LastEdit.Draft != null) { Draft = LastEdit.Draft; Hydrate(); Analyze(); }
            Notice = LastEdit.Status + "; revision=" + Draft.Revision + "; selected geometry and targets changed only by submitted operations.";
        }
        private void Query() { NeedDraft(); ConnectionQuery = sdk.QueryGenevaCompatibility(Draft); Notice = "Local " + ConnectionQuery.Verdict + "; finite ideal mate only, not release-solid or dynamics validation."; }
        private void MarkBefore() { NeedDraft(); Before = Draft; Comparison = null; AllowComparison(); Notice = "Marked before; only initial-to-current comparisons persist in this history."; }
        private void AllowComparison()
        { comparisonReadOnly = false; Inputs["comparison"].interactable = Inputs["input-map"].interactable = Buttons["compare"].interactable = true; Inputs["comparison"].text = "UnwrappedAngularOutput;1,0"; Inputs["input-map"].text = "1,0"; }
        private void Compare()
        {
            NeedDraft(); if (comparisonReadOnly) throw new InvalidOperationException("Loaded extended mapping is read-only; Mark before starts a new explicit request.");
            var p = Parts(Inputs["comparison"].text, ';', 2); var t = Parts(p[1], ',', 2); var u = Parts(Inputs["input-map"].text, ',', 2);
            if (!Enum.TryParse(p[0], false, out GenevaComparisonMode mode) || !Enum.IsDefined(typeof(GenevaComparisonMode), mode)) throw new FormatException("Unknown angular comparison scope.");
            var request = new GenevaMotionComparisonRequest(Before.Definition.Output.Key, Draft.Definition.Output.Key, Before.Definition.Source.Definition.RootShaftId, Draft.Definition.Source.Definition.RootShaftId,
                ExactQuantity.TurnsPerTurn(R(u[0])), Turns(u[1]), Integer(t[0]), Turns(t[1]), mode, numericRequest: NumericRequest());
            Comparison = sdk.CompareGenevaOutputMotion(sdk.AnalyzeGenevaDraft(Before), Analysis, request);
            if (Before.DraftId == initial.DraftId && !comparisons.Any(x => x.RequestId == request.RequestId))
            { if (comparisons.Count >= GenevaProfile.MaxComparisons) throw new InvalidOperationException("Comparison history bound exceeded."); comparisons.Add(request); }
            Notice = Comparison.Verdict + " / " + Comparison.ProofRule + "; average ratio and finite anchors are not function proofs.";
        }
        private void HydrateComparison()
        {
            AllowComparison(); if (comparisons.Count == 0) return; var r = comparisons[comparisons.Count - 1]; Before = initial;
            Comparison = sdk.CompareGenevaOutputMotion(sdk.AnalyzeGenevaDraft(initial), Analysis, r); comparisonReadOnly = true;
            Inputs["comparison"].text = r.Mode + ";" + r.Sign + "," + r.Delta.Value; Inputs["input-map"].text = r.Alpha.Value + "," + r.Beta.Value;
            Inputs["comparison"].interactable = Inputs["input-map"].interactable = Buttons["compare"].interactable = false;
        }
        private void FinalizeDraft() { NeedDraft(); Finalization = sdk.TryFinalizeGenevaDraft(Draft); Notice = "Current source-aware finalize: " + Finalization.Status; }
        private void SaveArtifact()
        { NeedDraft(); if (Finalization == null || !Finalization.IsFinalized || Finalization.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Finalize this current exact definition first."); sdk.SaveGenevaFinalization(Finalization, Inputs["save"].text); Notice = "Artifact saved through staged verified readback."; }
        private void SaveDraft() { NeedDraft(); sdk.SaveGenevaDraft(Draft, Inputs["save"].text); Notice = "Current declarations saved; a draft is not an admitted mechanism."; }
        private void SaveSession() { NeedDraft(); sdk.SaveGenevaEditSession(sdk.CreateGenevaEditSession(initial, history, comparisons), Inputs["save"].text); Notice = "Original plus actual ordered edits and comparison requests saved."; }
        private void LoadSession()
        { var session = sdk.LoadGenevaEditSession(Inputs["file"].text); Adopt(session.InitialDraft); history.AddRange(session.Batches); comparisons.AddRange(session.ComparisonRequests); Draft = session.CurrentDraft; Hydrate(); Analyze(); HydrateComparison(); Notice = "Freshly replayed session loaded; all current declaration fields hydrated."; }
        private void Reapply()
        { NeedDraft(); Draft = sdk.ReapplyGenevaEditSession(sdk.CreateGenevaEditSession(initial, history, comparisons)); Hydrate(); Analyze(); HydrateComparison(); Notice = "Ordered Reapply followed by current analysis; no stored step or pose is authoritative."; }
        private void Rebuild()
        { var rebuilt = sdk.RebuildGenevaArtifact(sdk.LoadGenevaArtifact(Inputs["file"].text)); sdk.SaveGenevaArtifact(rebuilt.Artifact, Inputs["save"].text); if (!File.ReadAllBytes(Inputs["save"].text).SequenceEqual(rebuilt.Bytes)) throw new IOException("Fresh saved rebuild differs."); Notice = "Full source, geometry, registration, lock and unwrapped reference rebuilt."; }
        private void Close()
        {
            Draft = Before = initial = null; Source = null; Analysis = null; Evaluation = null; LastEdit = null; Comparison = null; ConnectionQuery = null; Proposal = null; Finalization = null;
            history.Clear(); pending.Clear(); comparisons.Clear(); IsPlaying = false; comparisonReadOnly = false; DisplayUnavailableReason = null; if (View != null) View.Clear(); Notice = "Closed; no cached output frame is current.";
        }
        private void PlayPause() { NeedDraft(); IsPlaying = !IsPlaying; if (IsPlaying) { playRoot = R(Inputs["root"].text); playStarted = Time.realtimeSinceStartupAsDouble; } }
        private void Update()
        {
            if (IsPlaying) try { var micros = (long)((Time.realtimeSinceStartupAsDouble - playStarted) * 1000000); Inputs["root"].text = (playRoot + new Rational(micros, 1000000)).ToString(); Evaluate(); } catch (Exception e) { InputError(e); }
            notice.text = Notice;
            if (Draft == null || Analysis == null) { readback.text = "No current Geneva analysis. Complete source loaded=" + (Source != null); return; }
            var g = Draft.Definition.Device; var m = Analysis.Descriptor;
            var text = "CURRENT " + Draft.DefinitionId + " revision " + Draft.Revision + "\n" +
                "SOURCE shafts=" + Source.Definition.Shafts.Count + "; retained gears=" + Source.Definition.Bodies.Count + "; separate driver/wheel=2; new output shaft=1\n" +
                "N=" + g.Wheel.SlotCount + "; C=" + Analysis.Local.CenterDistanceMm + " mm; root=" + g.Wheel.SlotRoot.Value + " mm\n" +
                "SOURCE q,p=" + Law(Analysis.SourceRelation) + "; physical phi q,p=" + (m == null ? "unavailable" : Law(m.PhysicalPhase)) + "\n" +
                "Od=" + g.DriverCenterMm + "; Ow=" + g.WheelCenterMm + "; signed output station=" + g.WheelStation.Value + " mm\n" +
                "Pin=" + g.PinPresent + "; ideal lock=" + g.IdealLock.Present + "; j0=" + g.RegistrationSlot + "; ref=" + g.OutputReferenceTurns.Value + " turns\n" +
                "Local=" + Analysis.Local.Verdict + "; full-cycle=" + Analysis.HasFullCycleMotion + "; target=" + Analysis.Target + "; export=" + Analysis.ExportAdmission + "\n";
            if (Evaluation != null)
            {
                text += "ROOT " + Evaluation.RootTurns + " -> " + Evaluation.Status + "\nDRIVER source=" + Evaluation.Driver?.SourceTurns + "; physical phi=" + Evaluation.Driver?.PhysicalPhaseTurns + "\n";
                foreach (var s in Evaluation.SourceShafts) text += "SOURCE SHAFT " + s.ShaftId + "=" + s.Turns + " turns\n";
                var r = Evaluation.Recipe;
                if (r != null) text += "CYCLE k=" + r.CycleIndex + "; centered v=" + r.CenteredPhaseTurns + "; " + r.Regime + "\n" +
                    "MATERIAL slot=" + (r.SlotId ?? "none") + "; recess=" + (r.RecessId ?? "none") + "\n" +
                    "EXACT accumulated shaft=" + r.AccumulatedShaftTurns + "; residual=" + (r.ExactResidualTurns?.ToString() ?? "bounded principal atan") + " turns\n";
                if (Evaluation.NormalPose != null) text += "TERMINAL interval=" + ShortInterval(Evaluation.NormalPose.Numeric.TerminalTurns) + " turns\n";
                text += "NUMERIC " + (Evaluation.Numeric?.Status.ToString() ?? Evaluation.DriverNumeric?.Status.ToString() ?? "not available") + "; aggregate work=" + Evaluation.NumericWork + "/" + Evaluation.Request.MaximumWork + "\n";
                text += Evaluation.HasNormalPose ? "NORMAL OUTPUT: yellow=current slot/pin; green=current ideal mate; magenta=material slot zero.\n" : "NO NORMAL OUTPUT POSE. Conditional recipes never drive stale wheel graphics.\n";
            }
            else text += "NO CURRENT EVALUATION.\n";
            if (DisplayUnavailableReason != null) text += "DISPLAY_UNAVAILABLE " + DisplayUnavailableReason + "\n";
            if (LastEdit != null) text += "EDIT " + LastEdit.Status + "; failed operation=" + LastEdit.FailingOperationIndex + "; pending=" + pending.Count + "\n";
            if (Comparison != null) text += "COMPARE " + Comparison.Verdict + " / " + Comparison.ProofRule + "; " + Comparison.Scope + "\n";
            if (Finalization != null) text += "FINALIZE " + Finalization.Status + "\n";
            foreach (var check in Analysis.Checks) text += check.Domain + ": " + check.Verdict + (check.Required ? " [required]" : "") + "\n";
            foreach (var issue in Analysis.Diagnostics.Concat(Evaluation == null ? Enumerable.Empty<MechanicalDiagnostic>() : Evaluation.Diagnostics)) text += issue.Stage + "/" + issue.Code + "\n";
            text += "Point pin and IDEAL phase-released arc lock. No finite slot, relief solid, impact, friction, torque or manufacturing validation.";
            readback.text = text; var height = Mathf.Max(485, readback.preferredHeight + 14); readback.rectTransform.sizeDelta = new Vector2(845, height); ((RectTransform)readback.transform.parent).sizeDelta = new Vector2(855, height + 10);
        }
        private static string ShortInterval(GenevaInterval x) => x.IsExact ? x.Lower.ToString() : "[" + DisplayEndpoint(x.Lower, false) + "," + DisplayEndpoint(x.Upper, true) + "] outward-rounded display; certified width=" + x.Width;
        private static string DisplayEndpoint(Rational value, bool upper)
        {
            // Readback is not a world-coordinate float. Preserve arbitrarily large
            // accumulated turns and round the displayed endpoints outwards only.
            const int places = 8;
            var scale = System.Numerics.BigInteger.Pow(10, places);
            var scaled = value.Numerator * scale;
            var units = System.Numerics.BigInteger.DivRem(scaled, value.Denominator, out var remainder);
            if (!remainder.IsZero && ((upper && scaled.Sign > 0) || (!upper && scaled.Sign < 0))) units += scaled.Sign;
            var magnitude = System.Numerics.BigInteger.Abs(units);
            var whole = System.Numerics.BigInteger.DivRem(magnitude, scale, out var fraction);
            var digits = fraction.ToString("D8", CultureInfo.InvariantCulture).TrimEnd('0');
            return (units.Sign < 0 ? "-" : "") + whole.ToString(CultureInfo.InvariantCulture) + (digits.Length == 0 ? "" : "." + digits);
        }
        private void InputError(Exception e) { IsPlaying = false; Evaluation = null; Comparison = null; Finalization = null; ConnectionQuery = null; if (View != null) View.HideCurrentMotion(); Notice = "INPUT/OPERATION ERROR: " + e.Message + "; no successful stale frame."; }
        private void DisplayError(Exception e) { IsPlaying = false; DisplayUnavailableReason = e.Message; if (View != null) View.Clear(); Notice = "DISPLAY_UNAVAILABLE: " + e.Message + "; exact SDK result remains independent."; }
        private void Fit()
        {
            var center = View.ViewCenter; var radius = View.ViewRadius;
            var n = View.transform.TransformDirection(Draft == null ? Vector3.forward : GearInvestGenevaView.V(Draft.Definition.Device.PlaneNormal));
            var f = View.transform.TransformDirection(Draft == null ? Vector3.up : GearInvestGenevaView.V(Draft.Definition.Device.TransverseDirection));
            var e = View.transform.TransformDirection(Draft == null ? Vector3.right : GearInvestGenevaView.V(Draft.Definition.Device.CenterDirection));
            var forward = (n + .25f * e - .28f * f).normalized; sceneCamera.transform.position = center - forward * (radius * 4 + 80); sceneCamera.transform.rotation = Quaternion.LookRotation(forward, f);
            // Fit the rectangular scene viewport, as in the existing cam inspector.
            // Bounds conservatively contain the actual current mesh/line points;
            // their 3D diagonal is not the required vertical camera half-size.
            var bounds = View.ViewBounds; var halfHeight = 0f; var halfWidth = 0f;
            foreach (var x in new[] { bounds.min.x, bounds.max.x }) foreach (var y in new[] { bounds.min.y, bounds.max.y }) foreach (var z in new[] { bounds.min.z, bounds.max.z })
            {
                var delta = new Vector3(x, y, z) - center;
                halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(delta, sceneCamera.transform.up)));
                halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(delta, sceneCamera.transform.right)));
            }
            sceneCamera.orthographicSize = Mathf.Max(15, Mathf.Max(halfHeight, halfWidth / sceneCamera.aspect) * 1.12f);
            sceneCamera.nearClipPlane = .1f; sceneCamera.farClipPlane = radius * 10 + 500;
        }
        private Text Label(Transform p, string s, float x, float y, float w, float h, int size) => GearInvestAuthoringWidgets.Label(font, p, s, x, y, w, h, size, new Color(.9f, .94f, 1));
        private void Button(Transform p, string key, string title, float x, float y, float w, Action action) => Buttons.Add(key, GearInvestAuthoringWidgets.Button(font, p, key, title, x, y, w, () => { try { action(); } catch (Exception e) { InputError(e); } }));
        private static Rational R(string s) => Rational.Parse(s);
        private static ExactQuantity Mm(string s) => ExactQuantity.Millimeters(R(s));
        private static ExactQuantity Turns(string s) => ExactQuantity.Turns(R(s));
        private static ExactQuantity? OptionalTurns(string s) => s == "none" ? (ExactQuantity?)null : Turns(s);
        private static int Integer(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static bool Boolean(string s) => s == "true" ? true : s == "false" ? false : throw new FormatException("Use true or false.");
        private static string[] Parts(string s, char separator, int count) { var p = s.Split(separator).Select(x => x.Trim()).ToArray(); if (p.Length != count) throw new FormatException("Expected " + count + " separated fields."); return p; }
        private static ExactVector3 Vector(string s) { var p = Parts(s, ',', 3); return new ExactVector3(R(p[0]), R(p[1]), R(p[2])); }
        private static OrientedFrame Frame(string s) { var p = Parts(s, ';', 4); return new OrientedFrame(Vector(p[0]), Vector(p[1]), Vector(p[2]), Vector(p[3])); }
        private static string Point(ExactVector3 p) => p.X + "," + p.Y + "," + p.Z;
        private static string FrameText(OrientedFrame f) => Point(f.Origin) + ";" + Point(f.X) + ";" + Point(f.Y) + ";" + Point(f.Z);
        private static GenevaLength Length(string s) { var p = s.Split(','); return p.Length == 2 && p[0] == "mm" ? GenevaLength.Millimeters(R(p[1])) : p.Length == 3 && p[0] == "sin" ? GenevaLength.SinTurns(R(p[1]), R(p[2])) : throw new FormatException("Length is mm,c or sin,c,turns."); }
        private static string LengthText(GenevaLength l) => l.Kind == GenevaLengthKind.RationalMm ? "mm," + l.CoefficientMm : "sin," + l.CoefficientMm + "," + l.AngleTurns;
        private string[] RequiredDomains() => Inputs["required"].text.Split(',').Select(x => x.Trim()).Where(x => x.Length != 0).ToArray();
        private static string Law(ExactAffineRelation? r) => r.HasValue ? r.Value.Coefficient + "," + r.Value.Phase : "undetermined";
        private void OnDestroy() { if (!ownsCamera && sceneCamera != null) previousCamera.Restore(sceneCamera); }
        private sealed class CameraState
        {
            private readonly Vector3 position; private readonly Quaternion rotation; private readonly Rect rect; private readonly bool orthographic; private readonly float size, near, far; private readonly Color background; private readonly CameraClearFlags clear; private readonly int mask;
            public CameraState(Camera c) { position = c.transform.position; rotation = c.transform.rotation; rect = c.rect; orthographic = c.orthographic; size = c.orthographicSize; near = c.nearClipPlane; far = c.farClipPlane; background = c.backgroundColor; clear = c.clearFlags; mask = c.cullingMask; }
            public void Restore(Camera c) { c.transform.SetPositionAndRotation(position, rotation); c.rect = rect; c.orthographic = orthographic; c.orthographicSize = size; c.nearClipPlane = near; c.farClipPlane = far; c.backgroundColor = background; c.clearFlags = clear; c.cullingMask = mask; }
        }
    }
}
