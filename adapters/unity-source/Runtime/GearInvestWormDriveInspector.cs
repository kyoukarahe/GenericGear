using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using BigInteger = System.Numerics.BigInteger;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GearInvest.Unity
{
    /// <summary>Optional exact worm authoring form. No helical relation, geometry admission or motion solver lives in Unity.</summary>
    public sealed class GearInvestWormDriveInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public WormDriveDraft Draft { get; private set; }
        public WormDriveDraft Before { get; private set; }
        public WormDriveAnalysis Analysis { get; private set; }
        public WormDriveEvaluation Evaluation { get; private set; }
        public WormDriveEditResult LastEdit { get; private set; }
        public WormDriveOutputEquivalenceResult Comparison { get; private set; }
        public WormDriveCompatibilityResult ConnectionQuery { get; private set; }
        public WormDriveFinalizationResult Finalization { get; private set; }
        public IdealWormWheelSpecification ProposedWheel { get; private set; }
        public GearInvestWormDriveView View { get; private set; }
        public MechanicalDraft Source { get; private set; }
        public string Notice { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public bool IsPlaying { get; private set; }
        public string DisplayUnavailableReason { get; private set; }
        public int PendingOperationCount => pending.Count;
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<WormDriveEditBatch> history = new List<WormDriveEditBatch>();
        private readonly List<WormDriveEditOperation> pending = new List<WormDriveEditOperation>();
        private readonly List<OutputComparisonRequest> comparisons = new List<OutputComparisonRequest>();
        private WormDriveDraft initial; private bool comparisonReadOnly; private Font font; private Text notice, readback;
        private Camera sceneCamera; private CameraState previousCamera; private bool ownsSceneCamera;
        private double playStarted; private Rational playRoot;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";

        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
            { var events = new GameObject("Worm drive EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(transform, false); }
            sceneCamera = Camera.main; ownsSceneCamera = sceneCamera == null;
            if (ownsSceneCamera) { var go = new GameObject("Worm drive scene camera"); go.tag = "MainCamera"; go.transform.SetParent(transform, false); sceneCamera = go.AddComponent<Camera>(); }
            else previousCamera = new CameraState(sceneCamera);
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.035f, .055f, .085f);
            sceneCamera.orthographic = true; sceneCamera.rect = new Rect(700f / 1600, 520f / 1050, 900f / 1600, 530f / 1050);
            var uiCamera = new GameObject("Worm drive UI camera").AddComponent<Camera>(); uiCamera.transform.SetParent(transform, false);
            uiCamera.cullingMask = 1 << 5; uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.depth = sceneCamera.depth + 10;
            uiCamera.transform.position = new Vector3(0, 0, -1000); uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 30;
            var canvasObject = new GameObject("Worm drive authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Worm drive control panel", 0, 0, 700, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "IDEAL CYLINDRICAL WORM / INDEPENDENT WHEEL", 12, 9, 675, 28, 20);
            Label(panel, "Schematic pitch helices, NOT manufactured flanks. Propose wheel -> Queue Wheel -> Apply.", 12, 40, 675, 30, 12);
            var viewport = GearInvestAuthoringWidgets.Box(panel, "Scrollable worm drive parameters", 0, 74, 700, 610, new Color(.055f, .07f, .1f));
            viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var form = GearInvestAuthoringWidgets.Rect(viewport, "Explicit worm drive inputs", 0, 0, 700, 900); scroll.content = form;
            var y = 0f;
            void Field(string key, string title, string value) { Inputs.Add(key, GearInvestAuthoringWidgets.Field(font, form, key, title, value, y)); y += 32; }
            Field("file", "Source / draft / session / artifact file", ""); Field("shaft", "Actual retained source shaft", "output"); Field("port", "Optional original input port", "");
            Field("scale", "Explicit mm / source unit", "1"); Field("pose", "Source pose origin(mm);X;Y;Z", Identity);
            Field("starts", "Worm starts S (1..8)", "2"); Field("module", "Worm axial module mW (mm)", "2");
            Field("radius", "Independent worm pitch radius Rw (mm)", "10"); Field("hand", "Physical worm handedness H (+1/-1)", "1");
            Field("teeth", "Independent wheel teeth Z", "40"); Field("wheel-module", "Wheel transverse module mG (mm)", "2");
            Field("wheel-hand", "Independent wheel trace hand HG", "1"); Field("slope", "Independent wheel exact trace slope", "1/5");
            Field("input-station", "Signed input pitch station along actual shaft (mm)", "40");
            Field("output-frame", "Output shaft origin(mm);X;Y;Z", "60,50,40;0,1,0;0,0,1;1,0,0");
            Field("station", "Signed output pitch station (mm)", "0");
            Field("terminal-frame", "World terminal origin(mm);X;Y;Z", "60,50,40;0,1,0;0,0,1;1,0,0");
            Field("terminal-phase", "Independent terminal angular datum (turn)", "0");
            Field("axis", "Physical helix axis n", "0,0,1"); Field("side", "Declared contact-side common normal b", "0,1,0");
            Field("reference", "Input,output assembly references (turn)", "0,0");
            Field("mounting", "Signed input,output material mounting phase (turn)", "0,0");
            Field("targets", "Requested terminal ratio,phase (or none)", "-1/40,0"); Field("required", "Extra required validation domains (comma)", "");
            Field("wheel", "Current selected independent wheel [read-only]", "none"); Inputs["wheel"].interactable = false;
            Field("proposal", "Explicit matching wheel proposal [read-only]", "none"); Inputs["proposal"].interactable = false;
            Field("placement", "Compatible pitch-center proposal [read-only]", "none"); Inputs["placement"].interactable = false;
            Field("move", "Output-group WORLD transform origin;X;Y;Z", "0,20,0;1,0,0;0,1,0;0,0,1");
            Field("operation", "Typed edit: Starts/Wheel/Teeth/Move/Targets...", "Starts"); Field("value", "Edit ID / source contact / true,false", "");
            Field("comparison", "Mode; explicit mapped-output sign,delta", "FullAffineOutput;1,0");
            Field("root", "Absolute unwrapped root turns", "1/4"); Field("save", "New file path (never overwrite)", "");
            form.sizeDelta = new Vector2(700, y + 8);
            var row = 698f;
            void Four(string a, string at, Action aa, string b, string bt, Action ba, string c, string ct, Action ca, string d, string dt, Action da)
            { Button(panel, a, at, 12, row, 162, aa); Button(panel, b, bt, 182, row, 162, ba); Button(panel, c, ct, 352, row, 162, ca); Button(panel, d, dt, 522, row, 164, da); row += 39; }
            Four("example", "Public 20T -> 40T", PublicSource, "load-source", "Load whole source", LoadSource, "next-shaft", "Next actual shaft", NextShaft, "standalone", "Standalone shaft", StandaloneSource);
            Four("compose", "Compose / Analyze", Compose, "propose-wheel", "Propose wheel", ProposeWheel, "queue", "Queue explicit edit", Queue, "apply", "Apply whole batch", Apply);
            Four("reanalyze", "Reanalyze", Analyze, "query", "Local query", Query, "compare", "Compare", Compare, "mark-before", "Mark before", MarkBefore);
            Four("evaluate", "Evaluate root", Evaluate, "play", "Play / Pause", PlayPause, "finalize", "Finalize", FinalizeDraft, "save-artifact", "Save artifact", SaveArtifact);
            Four("load-draft", "Load draft", () => Adopt(sdk.ReadWormDriveDraft(ReadFile())), "load-session", "Load session", LoadSession, "save-draft", "Save draft", SaveDraft, "save-session", "Save history", SaveSession);
            Four("reapply", "Reapply history", Reapply, "rebuild", "Rebuild artifact", Rebuild, "load-artifact", "Load artifact", () => Adopt(sdk.ReadWormDriveArtifact(ReadFile()).Request), "close", "Close current", Close);
            Four("load-wheel", "Load wheel proposal", LoadWheel, "save-wheel", "Save proposal", SaveWheel, "clear-queue", "Clear queued edits", () => { pending.Clear(); Notice = "Queued edits cleared; current definition unchanged."; },
                "propose-placement", "Propose placement", ProposePlacement);
            notice = Label(panel, "Choose source; Compose; Propose wheel; Queue Wheel and Apply.", 12, 975, 674, 68, 13);
            var results = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Exact worm drive readback", 710, 540, 878, 499, new Color(.065f, .085f, .115f));
            results.gameObject.AddComponent<RectMask2D>(); var resultScroll = results.gameObject.AddComponent<ScrollRect>(); resultScroll.viewport = results; resultScroll.horizontal = false; resultScroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Rect(results, "Exact worm drive results", 0, 0, 855, 485); resultScroll.content = content;
            readback = Label(content, "No current mechanism.", 7, 4, 845, 480, 14);
            View = new GameObject("Worm drive pitch-line presentation").AddComponent<GearInvestWormDriveView>(); View.transform.SetParent(transform, false);
            Notice = "A proposal is not installation. All current mechanical results come from the public SDK.";
        }
        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Compose/load an worm drive draft first."); }
        private byte[] ReadFile()
        { var path = Inputs["file"].text; if (new FileInfo(path).Length > WormDriveProfile.MaxDocumentBytes) throw new FormatException("Input byte bound exceeded."); var bytes = File.ReadAllBytes(path); if (bytes.Length > WormDriveProfile.MaxDocumentBytes) throw new FormatException("Input byte bound exceeded."); return bytes; }
        private void PublicSource()
        {
            var kinematic = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") }, new[] { new ExternalGearCoupling("mesh", "input", "output", 20, 40) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", 0, 0), new SpatialAxis("output-axis", 60, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, 20, 20), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, 40, 40) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("worm-drive-public-spur-20-40", kinematic, spatial));
            if (!generated.IsSuccess) throw new InvalidOperationException("Public source generation failed.");
            ExampleFields(false); SetSource(sdk.ImportMechanicalArtifact(sdk.WriteArtifact(generated.Candidates.Single()).Bytes), "output", 40);
        }
        private void StandaloneSource()
        {
            ExampleFields(true); SetSource(new MechanicalDraft(new MechanicalDefinition("driver", new[] { new OrientedShaft("driver", OrientedFrame.Identity, true) },
                Array.Empty<OrientedGearBody>(), Array.Empty<MechanicalContact>(), Array.Empty<ShaftPort>(), clearancePolicy: MechanicalAuthoringProfile.PlanarClearance)), "driver", 0);
        }
        private void ExampleFields(bool standalone)
        {
            Inputs["scale"].text = "1"; Inputs["pose"].text = Identity;
            Inputs["starts"].text = "2"; Inputs["module"].text = Inputs["wheel-module"].text = "2"; Inputs["radius"].text = "10";
            Inputs["hand"].text = Inputs["wheel-hand"].text = "1"; Inputs["teeth"].text = "40"; Inputs["slope"].text = "1/5";
            Inputs["reference"].text = Inputs["mounting"].text = "0,0"; Inputs["terminal-phase"].text = "0";
            Inputs["station"].text = "0"; Inputs["required"].text = ""; Inputs["root"].text = "1/4";
            Inputs["targets"].text = standalone ? "1/20,0" : "-1/40,0";
        }
        private void SetSource(MechanicalDraft source, string shaft, Rational station)
        { Close(); Source = source; Inputs["shaft"].text = shaft; MountFields(station); Notice = "Whole source retained. Mount fields are visible proposals; Compose does not install a wheel."; }
        private void LoadSource() { var source = sdk.ImportMechanicalArtifact(ReadFile()); SetSource(source, source.Definition.Outputs.FirstOrDefault()?.ShaftId ?? source.Definition.RootShaftId, 40); }
        private void NextShaft()
        { if (Source == null) throw new InvalidOperationException("Load a source first."); var shafts = Source.Definition.Shafts.ToArray(); var i = Array.FindIndex(shafts, s => s.Id == Inputs["shaft"].text); Inputs["shaft"].text = shafts[(i + 1) % shafts.Length].Id; MountFields(40); }
        private void MountFields(Rational station)
        {
            var mapped = Mapping().FrameMm(Source.Definition.Shafts.Single(s => s.Id == Inputs["shaft"].text).Frame);
            var center = mapped.Origin + mapped.Z * station;
            // Visible initial placement proposal only; later edits never relocate or repair the selected wheel.
            var ag = mapped.X; var b = mapped.Y; var cg = center + b * (Rational.Parse(Inputs["radius"].text) + Rational.Parse(Inputs["wheel-module"].text) * Integer(Inputs["teeth"].text) / 2);
            var output = new OrientedFrame(cg, b, ag.Cross(b), ag);
            Inputs["port"].text = Source.Definition.Outputs.FirstOrDefault(o => o.ShaftId == Inputs["shaft"].text)?.PortId ?? "";
            Inputs["input-station"].text = station.ToString(); Inputs["axis"].text = Point(mapped.Z); Inputs["side"].text = Point(b);
            Inputs["output-frame"].text = Inputs["terminal-frame"].text = FrameText(output); Inputs["station"].text = "0";
        }
        private SourceLengthMapping Mapping() => new SourceLengthMapping(Rational.Parse(Inputs["scale"].text), Frame(Inputs["pose"].text));
        private void Compose()
        {
            if (Source == null) throw new InvalidOperationException("Load or generate an actual source first.");
            var references = Parts(Inputs["reference"].text, ',', 2); var mounting = Parts(Inputs["mounting"].text, ',', 2); var requirements = Parts(Inputs["targets"].text, ',', 2);
            var device = new WormDriveTransmissionDefinition("worm-drive", Inputs["shaft"].text, "worm-drive/input-worm",
                WormFields(), Vector(Inputs["axis"].text), Mm(Inputs["input-station"].text),
                new OrientedShaft("worm-drive/output-shaft", Frame(Inputs["output-frame"].text)), "worm-drive/output-wheel", null,
                Mm(Inputs["station"].text), Vector(Inputs["side"].text),
                ExactQuantity.Turns(Rational.Parse(mounting[0])), ExactQuantity.Turns(Rational.Parse(mounting[1])),
                ExactQuantity.Turns(Rational.Parse(references[0])), ExactQuantity.Turns(Rational.Parse(references[1])),
                inputPortId: string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text);
            var terminal = new ShaftPort("worm-drive/terminal", device.OutputShaft.Id, Frame(Inputs["terminal-frame"].text), Rational.Parse(Inputs["terminal-phase"].text));
            Adopt(sdk.ComposeWormDriveDraft(new WormDriveDefinition(Source, Mapping(), device, terminal,
                new MechanicalOutput("worm-output", device.OutputShaft.Id, device.OutputWheelBodyId, terminal.Id, Optional(requirements[0])), Optional(requirements[1]), RequiredDomains())));
        }
        private CylindricalWormSpecification WormFields() => new CylindricalWormSpecification(Integer(Inputs["starts"].text), Mm(Inputs["module"].text), Mm(Inputs["radius"].text), Integer(Inputs["hand"].text));
        private IdealWormWheelSpecification WheelFields() => new IdealWormWheelSpecification(Integer(Inputs["teeth"].text), Mm(Inputs["wheel-module"].text), Integer(Inputs["wheel-hand"].text), Rational.Parse(Inputs["slope"].text));
        private void Adopt(WormDriveDraft draft) { Close(); initial = Before = Draft = draft; Hydrate(); AllowComparison(); Analyze(); Notice = "Current exact declarations adopted and freshly analyzed; visible fields are current, selected wheel remains explicit."; }
        private void Hydrate()
        {
            var d = Draft.Definition; var c = d.Device; var w = c.Worm; var g = c.SelectedWheel; Source = d.Source;
            Inputs["shaft"].text = c.InputShaftId; Inputs["port"].text = c.InputPortId ?? ""; Inputs["scale"].text = d.SourceMapping?.MillimetersPerSourceUnit.ToString() ?? "";
            Inputs["pose"].text = d.SourceMapping == null ? "" : FrameText(d.SourceMapping.PoseMm);
            Inputs["starts"].text = w.Starts.ToString(CultureInfo.InvariantCulture); Inputs["module"].text = w.AxialModule.Value.ToString();
            Inputs["radius"].text = w.PitchRadius.Value.ToString(); Inputs["hand"].text = w.Handedness.ToString(CultureInfo.InvariantCulture);
            if (g != null) { Inputs["teeth"].text = g.ToothCount.ToString(CultureInfo.InvariantCulture); Inputs["wheel-module"].text = g.TransverseModule.Value.ToString();
                Inputs["wheel-hand"].text = g.Handedness.ToString(CultureInfo.InvariantCulture); Inputs["slope"].text = g.TraceSlope.ToString(); }
            Inputs["input-station"].text = c.InputPitchStation.Value.ToString(); Inputs["station"].text = c.OutputPitchStation.Value.ToString();
            Inputs["output-frame"].text = FrameText(c.OutputShaft.Frame); Inputs["terminal-frame"].text = FrameText(d.OutputTerminal.Frame);
            Inputs["terminal-phase"].text = d.OutputTerminal.PhaseOffset.ToString(); Inputs["axis"].text = Point(c.PhysicalHelixAxis); Inputs["side"].text = Point(c.ContactSide);
            Inputs["reference"].text = c.InputReferenceTurns.Value + "," + c.OutputReferenceTurns.Value;
            Inputs["mounting"].text = c.InputMountingPhase.Value + "," + c.OutputMountingPhase.Value;
            Inputs["targets"].text = (d.Output.RequiredTransfer?.ToString() ?? "none") + "," + (d.RequiredOutputPhase?.ToString() ?? "none");
            Inputs["required"].text = string.Join(",", d.RequiredValidationDomains); Inputs["wheel"].text = WheelText(g);
        }
        private void Analyze()
        {
            NeedDraft(); Finalization = null; Comparison = null; ConnectionQuery = null; Evaluation = null; Analysis = null; DisplayUnavailableReason = null; View.Clear();
            Analysis = sdk.AnalyzeWormDriveDraft(Draft);
            try { View.Build(Draft, Analysis); Fit(); }
            catch (WormDriveDisplayUnavailableException error) { DisplayError(error); }
            // Loading/reapplying the current mechanical definition is independent from an unsubmitted/malformed caller root field.
            try { Evaluate(); } catch (Exception error) { InputError(error); }
        }
        private void Evaluate()
        {
            NeedDraft(); Evaluation = null; var current = sdk.EvaluateWormDriveAnalysis(Analysis, ExactQuantity.Turns(Rational.Parse(Inputs["root"].text))); Evaluation = current;
            if (DisplayUnavailableReason != null)
            {
                DisplayUnavailableReason = null;
                try { View.Build(Draft, Analysis); Fit(); } catch (WormDriveDisplayUnavailableException error) { DisplayError(error); }
            }
            if (DisplayUnavailableReason == null)
            { try { View.Apply(current); } catch (WormDriveDisplayUnavailableException error) { DisplayError(error); } }
            if (current.Status != WormDriveEvaluationStatus.Success) IsPlaying = false;
            Notice = DisplayUnavailableReason == null ? "Evaluation " + current.Status + "; exact current source channels retained; no false stopped/cached wheel output." :
                "DISPLAY_UNAVAILABLE: " + DisplayUnavailableReason + "; exact SDK evaluation=" + current.Status + ". Definition/analysis/finalization semantics unchanged.";
        }
        private void PlayPause()
        { NeedDraft(); IsPlaying = !IsPlaying; if (IsPlaying) { playStarted = Time.realtimeSinceStartupAsDouble; playRoot = Rational.Parse(Inputs["root"].text); } Notice = IsPlaying ? "Caller clock: +1 absolute root turn/second." : "Caller clock paused."; }
        private void ProposeWheel()
        { NeedDraft(); ProposedWheel = sdk.CreateMatchingIdealWormWheel(Draft.Definition.Device.Worm, Integer(Inputs["teeth"].text)); Inputs["proposal"].text = WheelText(ProposedWheel); Notice = "Independent wheel PROPOSAL only. Queue Wheel and Apply to install; target and placement remain unchanged."; }
        private void ProposePlacement()
        { NeedDraft(); Inputs["placement"].text = Point(sdk.CreateCompatibleWormPlacement(Draft)); Notice = "Compatible output PITCH CENTER proposal only. The shaft frame, station and terminal are not moved."; }
        private void LoadWheel() { ProposedWheel = sdk.ReadWormDriveSpecification(ReadFile()); Inputs["proposal"].text = WheelText(ProposedWheel); Notice = "Loaded independent wheel proposal only; Queue Wheel and Apply explicitly."; }
        private void SaveWheel() { if (ProposedWheel == null) throw new InvalidOperationException("Create/load a wheel proposal first."); sdk.SaveWormDriveSpecification(ProposedWheel, Inputs["save"].text); Notice = "Saved independent wheel declaration; not installed."; }
        private void Queue()
        {
            NeedDraft(); var c = Draft.Definition.Device; var id = string.IsNullOrEmpty(Inputs["value"].text) ? c.Id : Inputs["value"].text; WormDriveEditOperation operation;
            // Every constructor argument is fully parsed before the pending queue is mutated.
            switch (Inputs["operation"].text)
            {
                case "Worm": operation = new SetWormDriveWormSpecificationEdit(id, WormFields()); break;
                case "Starts": operation = new SetWormDriveStartsEdit(id, Integer(Inputs["starts"].text)); break;
                case "Module": operation = new SetWormDriveAxialModuleEdit(id, Mm(Inputs["module"].text)); break;
                case "Radius": operation = new SetWormDrivePitchRadiusEdit(id, Mm(Inputs["radius"].text)); break;
                case "Hand": operation = new SetWormDriveHandednessEdit(id, Integer(Inputs["hand"].text)); break;
                case "WheelSpec": operation = new SetWormDriveWheelSpecificationEdit(id, WheelFields()); break;
                case "Teeth": operation = new SetWormDriveWheelToothCountEdit(id, Integer(Inputs["teeth"].text)); break;
                case "WheelModule": operation = new SetWormDriveWheelModuleEdit(id, Mm(Inputs["wheel-module"].text)); break;
                case "WheelHand": operation = new SetWormDriveWheelHandednessEdit(id, Integer(Inputs["wheel-hand"].text)); break;
                case "Slope": operation = new SetWormDriveWheelTraceSlopeEdit(id, Rational.Parse(Inputs["slope"].text)); break;
                case "InputStation": operation = new SetWormDriveInputPitchStationEdit(id, Mm(Inputs["input-station"].text)); break;
                case "Station": operation = new SetWormDriveOutputPitchStationEdit(id, Mm(Inputs["station"].text)); break;
                case "Mounting": var mounts = Parts(Inputs["mounting"].text, ',', 2); operation = new SetWormDriveMountingPhaseEdit(id, ExactQuantity.Turns(Rational.Parse(mounts[0])), ExactQuantity.Turns(Rational.Parse(mounts[1]))); break;
                case "OutputFrame": operation = new SetWormDriveOutputShaftFrameEdit(c.OutputShaft.Id, Frame(Inputs["output-frame"].text)); break;
                case "Move": operation = new MoveWormDriveOutputShaftGroupEdit(c.OutputShaft.Id, Frame(Inputs["move"].text)); break;
                case "Terminal": operation = new SetWormDriveOutputTerminalEdit(Draft.Definition.Output.Key, Frame(Inputs["terminal-frame"].text), Rational.Parse(Inputs["terminal-phase"].text)); break;
                case "Axis": operation = new SetWormDrivePhysicalHelixAxisEdit(id, Vector(Inputs["axis"].text)); break;
                case "Side": operation = new SetWormDriveContactSideEdit(id, Vector(Inputs["side"].text)); break;
                case "Reference": var reference = Parts(Inputs["reference"].text, ',', 2); operation = new SetWormDriveReferenceEdit(id, ExactQuantity.Turns(Rational.Parse(reference[0])), ExactQuantity.Turns(Rational.Parse(reference[1]))); break;
                case "Wheel": if (ProposedWheel == null) throw new InvalidOperationException("Propose/load a wheel first; worm changes never prepare it implicitly."); operation = new SetWormDriveWheelSpecificationEdit(id, ProposedWheel); break;
                case "ClearWheel": operation = new SetWormDriveWheelSpecificationEdit(id, null); break;
                case "Targets": var targets = Parts(Inputs["targets"].text, ',', 2); operation = new SetWormDriveRequirementsEdit(Draft.Definition.Output.Key, Optional(targets[0]), Optional(targets[1])); break;
                case "Transmission": if (Inputs["value"].text != "true" && Inputs["value"].text != "false") throw new FormatException("Explicit true/false required."); operation = new SetWormDriveTransmissionEdit(c.Id, Inputs["value"].text == "true"); break;
                case "Binding": operation = new SetWormDriveInputBindingEdit(c.Id, Inputs["shaft"].text, string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text); break;
                case "Mapping": operation = new SetWormDriveSourceLengthMappingEdit(Mapping()); break;
                case "Required": operation = new SetWormDriveRequiredValidationEdit(RequiredDomains()); break;
                case "SourceRemove": operation = SourceEdit(new RemoveContactEdit(Inputs["value"].text)); break;
                case "SourceRestore": operation = SourceEdit(new AddContactEdit(initial.Definition.Source.Definition.Contacts.Single(x => x.Id == Inputs["value"].text))); break;
                default: throw new FormatException("Typed edits: Worm,Starts,Module,Radius,Hand,WheelSpec,Teeth,WheelModule,WheelHand,Slope,InputStation,Station,Mounting,OutputFrame,Move,Terminal,Axis,Side,Reference,Wheel,ClearWheel,Targets,Transmission,Binding,Mapping,Required,SourceRemove,SourceRestore.");
            }
            if (pending.Count >= 128) throw new FormatException("Pending operation bound exceeded."); pending.Add(operation);
            Notice = "Queued " + operation.Kind + "; " + pending.Count + " pending. Current declarations unchanged.";
        }
        private WormDriveEditOperation SourceEdit(MechanicalEditOperation operation)
        {
            var source = Draft.Definition.Source;
            foreach (var queued in pending.OfType<ApplyWormDriveSourceEditsEdit>())
            { var preview = sdk.ApplyMechanicalEdits(source, queued.Batch); if (preview.Draft == null) throw new InvalidOperationException("Earlier queued source transaction is rejected."); source = preview.Draft; }
            return new ApplyWormDriveSourceEditsEdit(new MechanicalEditBatch(source.Revision, source.DefinitionId, new[] { operation }));
        }
        private void Apply()
        {
            NeedDraft(); var batch = new WormDriveEditBatch(Draft.Revision, Draft.DefinitionId, pending); LastEdit = sdk.ApplyWormDriveEdits(Draft, batch);
            history.Add(batch); pending.Clear(); Finalization = null;
            if (LastEdit.Draft != null) { Draft = LastEdit.Draft; Source = Draft.Definition.Source; Hydrate(); Analyze(); }
            Notice = LastEdit.Status + "; revision=" + Draft.Revision + ". Worm, wheel, placement and targets change only through their submitted typed edits.";
        }
        private void Query() { NeedDraft(); ConnectionQuery = sdk.QueryWormDriveCompatibility(Draft); Notice = "Local " + ConnectionQuery.Verdict + "; exact module/trace/station proof, not physical flank contact or self-locking."; }
        private void MarkBefore() { NeedDraft(); Before = Draft; Comparison = null; AllowComparison(); Notice = "Marked immutable before; comparisons from this intermediate state are NOT persisted as initial-to-current history."; }
        private void Compare()
        {
            NeedDraft(); if (comparisonReadOnly) throw new InvalidOperationException("Loaded input/domain mapping cannot be edited in this form; SDK/CLI or Mark before starts a new request.");
            var parts = Parts(Inputs["comparison"].text, ';', 2); var map = Parts(parts[1], ',', 2);
            if (!Enum.TryParse(parts[0], false, out OutputComparisonMode mode) || !Enum.IsDefined(typeof(OutputComparisonMode), mode)) throw new FormatException("Unknown comparison mode.");
            var request = new OutputComparisonRequest(Before.Definition.Output.Key, Draft.Definition.Output.Key, Before.Definition.Source.Definition.RootShaftId, Draft.Definition.Source.Definition.RootShaftId,
                sign: Rational.Parse(map[0]), delta: Rational.Parse(map[1]), mode: mode);
            Comparison = sdk.CompareWormDriveOutputMotion(sdk.AnalyzeWormDriveDraft(Before), Analysis, request); var persist = Before.DraftId == initial.DraftId;
            if (persist && !comparisons.Any(r => r.RequestId == request.RequestId))
            { if (comparisons.Count >= WormDriveProfile.MaxComparisons) throw new InvalidOperationException("Comparison history bound exceeded."); comparisons.Add(request); }
            Notice = "Scoped " + Comparison.Verdict + "; same geometry=" + Comparison.SameGeometry + "; same wheel=" + Comparison.SameWheelSpecification +
                (persist ? "; initial-to-current request recorded." : "; intermediate-before request NOT persisted.");
        }
        private void AllowComparison() { comparisonReadOnly = false; Inputs["comparison"].interactable = true; Buttons["compare"].interactable = true; Inputs["comparison"].text = "FullAffineOutput;1,0"; }
        private void HydrateComparison()
        {
            AllowComparison(); if (comparisons.Count == 0) return; var request = comparisons[comparisons.Count - 1]; Before = initial;
            Comparison = sdk.CompareWormDriveOutputMotion(sdk.AnalyzeWormDriveDraft(initial), Analysis, request);
            var fits = request.BeforeOutputKey == initial.Definition.Output.Key && request.AfterOutputKey == Draft.Definition.Output.Key &&
                request.BeforeInputId == initial.Definition.Source.Definition.RootShaftId && request.AfterInputId == Draft.Definition.Source.Definition.RootShaftId &&
                request.Alpha == 1 && request.Beta == 0 && request.MotionDomain == WormDriveProfile.MotionDomain;
            Inputs["comparison"].text = fits ? request.Mode + ";" + request.Sign + "," + request.Delta : "Saved extended input/domain mapping: read-only";
            comparisonReadOnly = !fits; Inputs["comparison"].interactable = fits; Buttons["compare"].interactable = fits;
        }
        private void FinalizeDraft() { NeedDraft(); Finalization = sdk.TryFinalizeWormDriveDraft(Draft); Notice = "Finalize " + Finalization.Status + "; flanks/self-locking/friction/efficiency/dynamics remain NotPerformed."; }
        private void SaveArtifact()
        { NeedDraft(); if (Finalization == null || !Finalization.IsFinalized || Finalization.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Finalize this exact current definition first."); sdk.SaveWormDriveFinalization(Finalization, Inputs["save"].text); Notice = "Current source-aware worm drive artifact saved with exact staged readback."; }
        private void SaveDraft() { NeedDraft(); sdk.SaveWormDriveDraft(Draft, Inputs["save"].text); Notice = "Declarations saved; an invalid/undriven draft is not an admitted artifact."; }
        private void SaveSession() { NeedDraft(); sdk.SaveWormDriveEditSession(sdk.CreateWormDriveEditSession(initial, history, comparisons), Inputs["save"].text); Notice = "Ordered actual operations and initial-to-current comparisons saved; readers execute them again."; }
        private void LoadSession()
        {
            var session = sdk.ReadWormDriveEditSession(ReadFile()); Adopt(session.InitialDraft); history.AddRange(session.Batches); comparisons.AddRange(session.ComparisonRequests);
            Draft = session.CurrentDraft; Hydrate(); Analyze(); HydrateComparison(); Notice = "Loaded actual current session; exact fields and explicit saved comparison mapping retained.";
        }
        private void Reapply() { NeedDraft(); Draft = sdk.ReapplyWormDriveEditSession(sdk.CreateWormDriveEditSession(initial, history, comparisons)); Hydrate(); Analyze(); HydrateComparison(); Notice = "Ordered Reapply -> fresh Analyze; fields hydrated from reconstructed current declarations."; }
        private void Rebuild()
        { var rebuilt = sdk.RebuildWormDriveArtifact(sdk.ReadWormDriveArtifact(ReadFile())); sdk.SaveWormDriveArtifact(rebuilt.Artifact, Inputs["save"].text); if (!File.ReadAllBytes(Inputs["save"].text).SequenceEqual(rebuilt.Bytes)) throw new IOException("Fresh saved rebuild bytes differ."); Notice = "Request-only source/module/trace/geometry/graph Rebuild saved; stored coefficients and pitch points were not treated as authority."; }
        private void Close()
        {
            Draft = Before = initial = null; Source = null; Analysis = null; Evaluation = null; LastEdit = null; Comparison = null; ConnectionQuery = null; Finalization = null; ProposedWheel = null;
            history.Clear(); pending.Clear(); comparisons.Clear(); comparisonReadOnly = false; IsPlaying = false; DisplayUnavailableReason = null; if (View != null) View.Clear();
            if (Inputs.ContainsKey("proposal")) Inputs["proposal"].text = "none"; if (Inputs.ContainsKey("wheel")) Inputs["wheel"].text = "none"; if (Inputs.ContainsKey("placement")) Inputs["placement"].text = "none"; Notice = "Closed. No old worm or wheel material pose is current.";
        }
        private void Update()
        {
            if (IsPlaying)
            {
                try { var micros = (long)((Time.realtimeSinceStartupAsDouble - playStarted) * 1000000); Inputs["root"].text = (playRoot + new Rational(micros, 1000000)).ToString(); Evaluate(); }
                catch (Exception error) { InputError(error); }
            }
            notice.text = Notice;
            if (Draft == null || Analysis == null) { readback.text = "No current worm-drive analysis or motion. Whole source loaded=" + (Source != null); return; }
            var c = Draft.Definition.Device; var local = Analysis.LocalCompatibility; var output = Analysis.Output; var g = local.Geometry;
            var text = "CURRENT " + Draft.DefinitionId + "  revision " + Draft.Revision + "\n" +
                "Bodies=" + (Draft.Definition.Source.Definition.Bodies.Count + 2) + "; shafts=" + Analysis.Nodes.Count +
                "; drivers=" + Analysis.Nodes.Count(n => n.IsPrescribed) + "; source contacts=" + Draft.Definition.Source.Definition.Contacts.Count + "; worm relation=" + (local.IsAdmitted ? 1 : 0) + "\n" +
                "Worm S=" + c.Worm.Starts + "; mW=" + c.Worm.AxialModule + "; Rw=" + c.Worm.PitchRadius + "; H=" + c.Worm.Handedness + "\n" +
                "Wheel " + WheelText(c.SelectedWheel) + "\n" +
                "px=pi*" + g?.AxialPitchPiCoefficientMm + " mm; L=pi*" + g?.LeadPiCoefficientMm + " mm; tau=" + g?.LeadSlope + "\n" +
                "Rw/Rg=" + g?.WormPitchRadiusMm + "/" + g?.WheelPitchRadiusMm + " mm; D=" + g?.CenterDistanceMm + " mm\n" +
                "Cw=" + g?.InputPitchCenterMm + "; Cg=" + g?.OutputPitchCenterMm + "; Q=" + g?.PitchPointMm + "\n" +
                "n=" + c.PhysicalHelixAxis + "; b=" + c.ContactSide + "; Tw=" + g?.WormTrace + "; Tg=" + g?.WheelTrace + "\n" +
                "eps=" + local.Epsilon + "; sigma=" + local.Sigma + "; terminal sign=" + local.TerminalCoordinateSign + "; datum=" + Draft.Definition.OutputTerminal.PhaseOffset + "\n" +
                "Reference=" + c.InputReferenceTurns + "/" + c.OutputReferenceTurns + "; mount=" + c.InputMountingPhase + "/" + c.OutputMountingPhase + "\n" +
                "Admitted q=" + local.AdmittedTransfer + "; prospective NOT admitted=" + local.ProspectiveTransfer + "\n" +
                "Shaft law=" + Law(output.ShaftRelation) + "; terminal law=" + Law(output.PortRelation) + "\n" +
                "Output=" + output.Determinacy + "; target=" + output.Target + "; whole valid=" + Analysis.IsMechanicallyValid + "\n";
            if (g != null && DisplayUnavailableReason == null)
            {
                try { text += "Display lead angle=" + (Math.Atan(WormDriveDisplay.ApproximateScalar(g.LeadSlope)) * 180 / Math.PI).ToString("F3", CultureInfo.InvariantCulture) + " deg [approximate only]\n"; }
                catch (WormDriveDisplayUnavailableException error) { DisplayError(error); }
            }
            if (DisplayUnavailableReason != null) text += "DISPLAY_UNAVAILABLE: " + DisplayUnavailableReason + ". Exact mechanics unchanged.\n";
            if (Evaluation == null) text += "NO CURRENT EVALUATION. No cached successful frame.\n";
            else
            {
                text += "ABSOLUTE ROOT " + Evaluation.Input + " -> " + Evaluation.Status + "\n";
                foreach (var r in Evaluation.Rotary) text += "ROTARY " + r.ShaftId + "=" + r.Turns + " turn; +axis=" + r.PositiveAxis + "\n";
                text += Evaluation.Output == null ? "WHEEL OUTPUT UNKNOWN / NO CURRENT POSE.\n" :
                    "SHAFT=" + Evaluation.Output.ShaftTurns + "; TERMINAL=" + Evaluation.Output.TerminalTurns +
                    "; JW/JG=" + Evaluation.Output.WormPhaseIndex + "/" + Evaluation.Output.WheelPhaseIndex + "\n";
            }
            if (LastEdit != null) text += "EDIT " + LastEdit.Status + "; failed index=" + LastEdit.FailingOperationIndex + "; queued=" + pending.Count + "\n";
            if (Comparison != null) text += "COMPARE " + Comparison.Verdict + " / " + Comparison.Scope + "; same geometry=" + Comparison.SameGeometry +
                "; same worm=" + Comparison.SameWormSpecification + "; same wheel=" + Comparison.SameWheelSpecification + "\n";
            if (Finalization != null) text += "FINALIZE " + Finalization.Status + "\n";
            text += "Persisted comparisons=" + comparisons.Count + "; proposal=" + (ProposedWheel?.SpecificationId ?? "none") + "\n";
            foreach (var check in Analysis.Checks) text += check.Domain + ": " + check.Verdict + (check.Required ? " [required]" : "") + "\n";
            foreach (var issue in Analysis.Diagnostics.Concat(LastEdit == null ? Enumerable.Empty<MechanicalDiagnostic>() : LastEdit.Diagnostics)) text += issue.Stage + "/" + issue.Code + "\n";
            text += "Fixed centers, rotating material helices. Schematic pitch traces only. No conjugate flanks, self-locking, physical backdrive, friction, efficiency or force proof.";
            readback.text = text; var height = Mathf.Max(485, readback.preferredHeight + 14); readback.rectTransform.sizeDelta = new Vector2(845, height); ((RectTransform)readback.transform.parent).sizeDelta = new Vector2(855, height + 10);
        }
        private void InputError(Exception error)
        {
            if (error is WormDriveDisplayUnavailableException display) { DisplayError(display); return; }
            IsPlaying = false; Finalization = null; Evaluation = null; Comparison = null; ConnectionQuery = null; if (View != null) View.HideCurrentMotion();
            Notice = "INPUT/OPERATION ERROR: " + error.Message + "; no cached successful frame is current.";
        }
        private void DisplayError(WormDriveDisplayUnavailableException error)
        {
            IsPlaying = false; DisplayUnavailableReason = error.Message; if (View != null) View.Clear();
            Notice = "DISPLAY_UNAVAILABLE: " + error.Message + "; exact draft/analysis/evaluation and source-aware finalization remain independent.";
        }
        private void Fit()
        {
            var center = View.ViewCenter; var radius = View.ViewRadius;
            sceneCamera.transform.position = center + new Vector3(1.2f, -1f, 1.5f).normalized * radius * 3;
            sceneCamera.transform.LookAt(center);
            // A padded bounding sphere fits every camera direction, including a tall wheel and
            // a narrow viewport. This changes presentation only, never mechanical placement.
            sceneCamera.orthographicSize = radius * 1.05f / Mathf.Min(1, sceneCamera.aspect);
            sceneCamera.nearClipPlane = .1f; sceneCamera.farClipPlane = radius * 8 + 100;
        }
        private void OnDestroy() { if (!ownsSceneCamera && sceneCamera != null && previousCamera != null) previousCamera.Restore(sceneCamera); }
        private string[] RequiredDomains() => Inputs["required"].text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        private static string WheelText(IdealWormWheelSpecification wheel) => wheel == null ? "none" : "Z=" + wheel.ToothCount + "; mG=" + wheel.TransverseModule + "; HG=" + wheel.Handedness + "; tauG=" + wheel.TraceSlope + "; " + wheel.SpecificationId;
        private static int Integer(string text) => int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException("Bounded signed integer required.");
        private static BigInteger Big(string text) => text.Length <= 129 && BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var value) ? value : throw new FormatException("Bounded exact integer required.");
        private static string Law(ExactAffineRelation? r) => r.HasValue ? r.Value.Coefficient + "*root+" + r.Value.Phase : "undetermined";
        private static Rational? Optional(string value) => value == "none" ? (Rational?)null : Rational.Parse(value);
        private static ExactQuantity Mm(string value) => ExactQuantity.Millimeters(Rational.Parse(value));
        private static string[] Parts(string value, char separator, int count) { var parts = value.Split(separator); if (parts.Length != count) throw new FormatException("Expected " + count + " exact fields."); return parts; }
        private static ExactVector3 Vector(string value) { var p = Parts(value, ',', 3).Select(Rational.Parse).ToArray(); return new ExactVector3(p[0], p[1], p[2]); }
        private static OrientedFrame Frame(string value) { var p = Parts(value, ';', 4).Select(Vector).ToArray(); return new OrientedFrame(p[0], p[1], p[2], p[3]); }
        private static string Point(ExactVector3 p) => p.X + "," + p.Y + "," + p.Z;
        private static string FrameText(OrientedFrame f) => Point(f.Origin) + ";" + Point(f.X) + ";" + Point(f.Y) + ";" + Point(f.Z);
        private Text Label(Transform parent, string value, float x, float y, float width, float height, int size) => GearInvestAuthoringWidgets.Label(font, parent, value, x, y, width, height, size, new Color(.9f, .94f, 1));
        private void Button(Transform parent, string key, string title, float x, float y, float width, Action action)
        { Buttons.Add(key, GearInvestAuthoringWidgets.Button(font, parent, key, title, x, y, width, () => { try { action(); } catch (Exception e) { InputError(e); } })); }
        private sealed class CameraState
        {
            private readonly Rect rect; private readonly Color background; private readonly CameraClearFlags clear; private readonly int mask;
            private readonly bool orthographic; private readonly float size, near, far; private readonly Vector3 position; private readonly Quaternion rotation;
            internal CameraState(Camera camera) { rect = camera.rect; background = camera.backgroundColor; clear = camera.clearFlags; mask = camera.cullingMask; orthographic = camera.orthographic; size = camera.orthographicSize; near = camera.nearClipPlane; far = camera.farClipPlane; position = camera.transform.position; rotation = camera.transform.rotation; }
            internal void Restore(Camera camera) { camera.rect = rect; camera.backgroundColor = background; camera.clearFlags = clear; camera.cullingMask = mask; camera.orthographic = orthographic; camera.orthographicSize = size; camera.nearClipPlane = near; camera.farClipPlane = far; camera.transform.SetPositionAndRotation(position, rotation); }
        }
    }
}
