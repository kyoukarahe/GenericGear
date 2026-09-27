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
    /// <summary>Optional bounded public-SDK authoring form. No belt routing, length proof or motion solver lives in Unity.</summary>
    public sealed class GearInvestOpenBeltInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public OpenBeltDraft Draft { get; private set; }
        public OpenBeltDraft Before { get; private set; }
        public OpenBeltAnalysis Analysis { get; private set; }
        public OpenBeltEvaluation Evaluation { get; private set; }
        public OpenBeltEditResult LastEdit { get; private set; }
        public OpenBeltOutputEquivalenceResult Comparison { get; private set; }
        public OpenBeltCompatibilityResult ConnectionQuery { get; private set; }
        public OpenBeltFinalizationResult Finalization { get; private set; }
        public OpenBeltLengthSpecification ProposedBelt { get; private set; }
        public GearInvestOpenBeltView View { get; private set; }
        public MechanicalDraft Source { get; private set; }
        public string Notice { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public bool IsPlaying { get; private set; }
        public string DisplayUnavailableReason { get; private set; }
        public int PendingOperationCount => pending.Count;
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<OpenBeltEditBatch> history = new List<OpenBeltEditBatch>();
        private readonly List<OpenBeltEditOperation> pending = new List<OpenBeltEditOperation>();
        private readonly List<OutputComparisonRequest> comparisons = new List<OutputComparisonRequest>();
        private OpenBeltDraft initial; private bool comparisonReadOnly; private Font font; private Text notice, readback;
        private Camera sceneCamera; private CameraState previousCamera; private bool ownsSceneCamera;
        private double playStarted; private Rational playRoot;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";

        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
            { var events = new GameObject("Open belt EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); events.transform.SetParent(transform, false); }
            sceneCamera = Camera.main; ownsSceneCamera = sceneCamera == null;
            if (ownsSceneCamera) { var go = new GameObject("Open belt scene camera"); go.tag = "MainCamera"; go.transform.SetParent(transform, false); sceneCamera = go.AddComponent<Camera>(); }
            else previousCamera = new CameraState(sceneCamera);
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.035f, .055f, .085f);
            sceneCamera.orthographic = true; sceneCamera.rect = new Rect(700f / 1600, 520f / 1050, 900f / 1600, 530f / 1050);
            var uiCamera = new GameObject("Open belt UI camera").AddComponent<Camera>(); uiCamera.transform.SetParent(transform, false);
            uiCamera.cullingMask = 1 << 5; uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.depth = sceneCamera.depth + 10;
            uiCamera.transform.position = new Vector3(0, 0, -1000); uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 30;
            var canvasObject = new GameObject("Open belt authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Open belt control panel", 0, 0, 700, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "IDEAL OPEN BELT / EXPLICIT LENGTH", 12, 9, 675, 28, 20);
            Label(panel, "Orange: unadmitted required-route preview; selected belt is NOT stretched. Propose -> Queue Belt -> Apply.", 12, 40, 675, 30, 12);
            var viewport = GearInvestAuthoringWidgets.Box(panel, "Scrollable open belt parameters", 0, 74, 700, 610, new Color(.055f, .07f, .1f));
            viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var form = GearInvestAuthoringWidgets.Rect(viewport, "Explicit open belt inputs", 0, 0, 700, 900); scroll.content = form;
            var y = 0f;
            void Field(string key, string title, string value) { Inputs.Add(key, GearInvestAuthoringWidgets.Field(font, form, key, title, value, y)); y += 32; }
            Field("file", "Source / draft / session / artifact file", ""); Field("shaft", "Actual retained source shaft", "output"); Field("port", "Optional original input port", "");
            Field("scale", "Explicit mm / source unit", "1"); Field("pose", "Source pose origin(mm);X;Y;Z", Identity);
            Field("r1", "Input pitch radius R1 (mm)", "20"); Field("r2", "Output pitch radius R2 (mm)", "40"); Field("c1", "Input pulley fixed center C1 (mm)", "60,0,40");
            Field("output-frame", "Output shaft origin(mm);X;Y;Z", "160,0,40;1,0,0;0,1,0;0,0,1"); Field("station", "Output pulley station along +shaft Z (mm)", "0");
            Field("terminal-frame", "Independent terminal origin(mm);X;Y;Z", "160,0,40;1,0,0;0,1,0;0,0,1"); Field("normal", "Explicit route plane normal n", "0,0,1");
            Field("routing", "Routing kind (only open admitted)", "open"); Field("reference", "Input reference,output reference (turn)", "0,0");
            Field("targets", "Requested terminal ratio,phase (or none)", "-1/4,0"); Field("required", "Extra required validation domains (comma)", "");
            Field("belt", "Current selected belt [read-only]", "none"); Inputs["belt"].interactable = false;
            Field("proposal", "Explicit belt proposal [read-only]", "none"); Inputs["proposal"].interactable = false;
            Field("move", "Output-group WORLD transform origin;X;Y;Z", "40,0,0;1,0,0;0,1,0;0,0,1");
            Field("operation", "Typed edit (R2 / Belt / Targets / Move...)", "R2"); Field("value", "Edit ID / source contact / true,false", "");
            Field("comparison", "Mode; explicit mapped-output sign,delta", "FullAffineOutput;1,0"); Field("root", "Absolute unwrapped root turns", "1/4"); Field("save", "New file path (never overwrite)", "");
            form.sizeDelta = new Vector2(700, y + 8);
            var row = 698f;
            void Four(string a, string at, Action aa, string b, string bt, Action ba, string c, string ct, Action ca, string d, string dt, Action da)
            { Button(panel, a, at, 12, row, 162, aa); Button(panel, b, bt, 182, row, 162, ba); Button(panel, c, ct, 352, row, 162, ca); Button(panel, d, dt, 522, row, 164, da); row += 39; }
            Four("example", "Public 20T -> 40T", PublicSource, "load-source", "Load whole source", LoadSource, "next-shaft", "Next actual shaft", NextShaft, "standalone", "Standalone shaft", StandaloneSource);
            Four("compose", "Compose / Analyze", Compose, "propose-belt", "Propose current belt", ProposeBelt, "queue", "Queue explicit edit", Queue, "apply", "Apply whole batch", Apply);
            Four("reanalyze", "Reanalyze", Analyze, "query", "Local query", Query, "compare", "Compare", Compare, "mark-before", "Mark before", MarkBefore);
            Four("evaluate", "Evaluate root", Evaluate, "play", "Play / Pause", PlayPause, "finalize", "Finalize", FinalizeDraft, "save-artifact", "Save artifact", SaveArtifact);
            Four("load-draft", "Load draft", () => Adopt(sdk.ReadOpenBeltDraft(ReadFile())), "load-session", "Load session", LoadSession, "save-draft", "Save draft", SaveDraft, "save-session", "Save history", SaveSession);
            Four("reapply", "Reapply history", Reapply, "rebuild", "Rebuild artifact", Rebuild, "load-artifact", "Load artifact", () => Adopt(sdk.ReadOpenBeltArtifact(ReadFile()).Request), "close", "Close current", Close);
            Four("load-belt", "Load belt proposal", LoadBelt, "save-belt", "Save proposal", SaveBelt, "clear-queue", "Clear queued edits", () => { pending.Clear(); Notice = "Queued edits cleared; current definition unchanged."; },
                "clear-proposal", "Clear proposal", () => { ProposedBelt = null; Inputs["proposal"].text = "none"; Notice = "Proposal cleared; installed belt is unchanged."; });
            notice = Label(panel, "Choose source; compose; propose belt; queue Belt and Apply.", 12, 975, 674, 68, 13);
            var results = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Exact open belt readback", 710, 540, 878, 499, new Color(.065f, .085f, .115f));
            results.gameObject.AddComponent<RectMask2D>(); var resultScroll = results.gameObject.AddComponent<ScrollRect>(); resultScroll.viewport = results; resultScroll.horizontal = false; resultScroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Rect(results, "Exact open belt results", 0, 0, 855, 485); resultScroll.content = content;
            readback = Label(content, "No current mechanism.", 7, 4, 845, 480, 14);
            View = new GameObject("Open belt pitch-line presentation").AddComponent<GearInvestOpenBeltView>(); View.transform.SetParent(transform, false);
            Notice = "A proposal is not installation. All current mechanical results come from the public SDK.";
        }
        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Compose/load an open belt draft first."); }
        private byte[] ReadFile()
        { var path = Inputs["file"].text; if (new FileInfo(path).Length > OpenBeltProfile.MaxDocumentBytes) throw new FormatException("Input byte bound exceeded."); var bytes = File.ReadAllBytes(path); if (bytes.Length > OpenBeltProfile.MaxDocumentBytes) throw new FormatException("Input byte bound exceeded."); return bytes; }
        private void PublicSource()
        {
            var kinematic = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") }, new[] { new ExternalGearCoupling("mesh", "input", "output", 20, 40) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", 0, 0), new SpatialAxis("output-axis", 60, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, 20, 20), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, 40, 40) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("open-belt-public-spur-20-40", kinematic, spatial));
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
            Inputs["scale"].text = "1"; Inputs["pose"].text = Identity; Inputs["r1"].text = "20"; Inputs["r2"].text = standalone ? "20" : "40";
            Inputs["reference"].text = "0,0"; Inputs["station"].text = "0"; Inputs["routing"].text = "open"; Inputs["required"].text = "";
            Inputs["root"].text = "1/4"; Inputs["targets"].text = standalone ? "1,0" : "-1/4,0";
        }
        private void SetSource(MechanicalDraft source, string shaft, Rational station)
        { Close(); Source = source; Inputs["shaft"].text = shaft; MountFields(station); Notice = "Whole source retained. Mount fields are visible proposals; Compose does not create or install a belt."; }
        private void LoadSource() { var source = sdk.ImportMechanicalArtifact(ReadFile()); SetSource(source, source.Definition.Outputs.FirstOrDefault()?.ShaftId ?? source.Definition.RootShaftId, 40); }
        private void NextShaft()
        { if (Source == null) throw new InvalidOperationException("Load a source first."); var shafts = Source.Definition.Shafts.ToArray(); var i = Array.FindIndex(shafts, s => s.Id == Inputs["shaft"].text); Inputs["shaft"].text = shafts[(i + 1) % shafts.Length].Id; MountFields(40); }
        private void MountFields(Rational station)
        {
            var mapped = Mapping().FrameMm(Source.Definition.Shafts.Single(s => s.Id == Inputs["shaft"].text).Frame);
            var center = mapped.Origin + mapped.Z * station; var output = mapped.At(center + mapped.X * 100);
            Inputs["port"].text = Source.Definition.Outputs.FirstOrDefault(o => o.ShaftId == Inputs["shaft"].text)?.PortId ?? "";
            Inputs["c1"].text = Point(center); Inputs["normal"].text = Point(mapped.Z); Inputs["output-frame"].text = Inputs["terminal-frame"].text = FrameText(output); Inputs["station"].text = "0";
        }
        private SourceLengthMapping Mapping() => new SourceLengthMapping(Rational.Parse(Inputs["scale"].text), Frame(Inputs["pose"].text));
        private void Compose()
        {
            if (Source == null) throw new InvalidOperationException("Load or generate an actual source first.");
            var references = Parts(Inputs["reference"].text, ',', 2); var requirements = Parts(Inputs["targets"].text, ',', 2);
            var device = new OpenBeltTransmissionDefinition("open-belt", Inputs["shaft"].text, "open-belt/input-pulley", Vector(Inputs["c1"].text), Mm(Inputs["r1"].text),
                new OrientedShaft("open-belt/output-shaft", Frame(Inputs["output-frame"].text)), "open-belt/output-pulley", Mm(Inputs["station"].text), Mm(Inputs["r2"].text),
                Vector(Inputs["normal"].text), ExactQuantity.Turns(Rational.Parse(references[0])), ExactQuantity.Turns(Rational.Parse(references[1])), null,
                inputPortId: string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text, routingKind: Inputs["routing"].text);
            var terminal = new ShaftPort("open-belt/terminal", device.OutputShaft.Id, Frame(Inputs["terminal-frame"].text));
            Adopt(sdk.ComposeOpenBeltDraft(new OpenBeltDefinition(Source, Mapping(), device, terminal,
                new MechanicalOutput("belt-output", device.OutputShaft.Id, device.OutputPulleyBodyId, terminal.Id, Optional(requirements[0])), Optional(requirements[1]), RequiredDomains())));
        }
        private void Adopt(OpenBeltDraft draft) { Close(); initial = Before = Draft = draft; Hydrate(); AllowComparison(); Analyze(); Notice = "Current exact declarations adopted and freshly analyzed; visible fields are current, selected belt remains explicit."; }
        private void Hydrate()
        {
            var d = Draft.Definition; var c = d.Device; Source = d.Source;
            Inputs["shaft"].text = c.InputShaftId; Inputs["port"].text = c.InputPortId ?? ""; Inputs["scale"].text = d.SourceMapping?.MillimetersPerSourceUnit.ToString() ?? "";
            Inputs["pose"].text = d.SourceMapping == null ? "" : FrameText(d.SourceMapping.PoseMm); Inputs["r1"].text = c.InputPitchRadius.Value.ToString(); Inputs["r2"].text = c.OutputPitchRadius.Value.ToString();
            Inputs["c1"].text = Point(c.InputPulleyCenterMm); Inputs["output-frame"].text = FrameText(c.OutputShaft.Frame); Inputs["station"].text = c.OutputPulleyStation.Value.ToString();
            Inputs["terminal-frame"].text = FrameText(d.OutputTerminal.Frame); Inputs["normal"].text = Point(c.RouteNormal); Inputs["routing"].text = c.RoutingKind;
            Inputs["reference"].text = c.InputReferenceTurns.Value + "," + c.OutputReferenceTurns.Value;
            Inputs["targets"].text = (d.Output.RequiredTransfer?.ToString() ?? "none") + "," + (d.RequiredOutputPhase?.ToString() ?? "none");
            Inputs["required"].text = string.Join(",", d.RequiredValidationDomains); Inputs["belt"].text = BeltText(c.SelectedBelt);
        }
        private void Analyze()
        {
            NeedDraft(); Finalization = null; Comparison = null; ConnectionQuery = null; Evaluation = null; Analysis = null; DisplayUnavailableReason = null; View.Clear();
            Analysis = sdk.AnalyzeOpenBeltDraft(Draft);
            try { View.Build(Draft, Analysis); Fit(); }
            catch (OpenBeltDisplayUnavailableException error) { DisplayError(error); }
            // Loading/reapplying the current mechanical definition is independent from an unsubmitted/malformed caller root field.
            try { Evaluate(); } catch (Exception error) { InputError(error); }
        }
        private void Evaluate()
        {
            NeedDraft(); Evaluation = null; var current = sdk.EvaluateOpenBeltAnalysis(Analysis, ExactQuantity.Turns(Rational.Parse(Inputs["root"].text))); Evaluation = current;
            if (DisplayUnavailableReason != null)
            {
                DisplayUnavailableReason = null;
                try { View.Build(Draft, Analysis); Fit(); } catch (OpenBeltDisplayUnavailableException error) { DisplayError(error); }
            }
            if (DisplayUnavailableReason == null)
            { try { View.Apply(current); } catch (OpenBeltDisplayUnavailableException error) { DisplayError(error); } }
            if (current.Status != OpenBeltEvaluationStatus.Success) IsPlaying = false;
            Notice = DisplayUnavailableReason == null ? "Evaluation " + current.Status + "; exact current source channels retained; no false stopped/cached belt output." :
                "DISPLAY_UNAVAILABLE: " + DisplayUnavailableReason + "; exact SDK evaluation=" + current.Status + ". Definition/analysis/finalization semantics unchanged.";
        }
        private void PlayPause()
        { NeedDraft(); IsPlaying = !IsPlaying; if (IsPlaying) { playStarted = Time.realtimeSinceStartupAsDouble; playRoot = Rational.Parse(Inputs["root"].text); } Notice = IsPlaying ? "Caller clock: +1 absolute root turn/second." : "Caller clock paused."; }
        private void ProposeBelt()
        { NeedDraft(); ProposedBelt = sdk.CreateOpenBeltForCurrentRoute(Draft); Inputs["proposal"].text = BeltText(ProposedBelt); Notice = "New length PROPOSAL only. Select Belt, Queue and Apply to replace the installed belt explicitly."; }
        private void LoadBelt() { ProposedBelt = sdk.ReadOpenBeltLengthSpecification(ReadFile()); Inputs["proposal"].text = BeltText(ProposedBelt); Notice = "Loaded proposal only; current selection is unchanged until explicit Belt edit is applied."; }
        private void SaveBelt() { if (ProposedBelt == null) throw new InvalidOperationException("Create/load a belt proposal first."); sdk.SaveOpenBeltLengthSpecification(ProposedBelt, Inputs["save"].text); Notice = "Saved exact proposed length tuple, not a rounded/catalogue length."; }
        private void Queue()
        {
            NeedDraft(); var c = Draft.Definition.Device; var id = string.IsNullOrEmpty(Inputs["value"].text) ? c.Id : Inputs["value"].text; OpenBeltEditOperation operation;
            switch (Inputs["operation"].text)
            {
                case "R1": operation = new SetOpenBeltInputPitchRadiusEdit(id, Mm(Inputs["r1"].text)); break;
                case "R2": operation = new SetOpenBeltOutputPitchRadiusEdit(id, Mm(Inputs["r2"].text)); break;
                case "Center": operation = new SetOpenBeltInputPulleyCenterEdit(id, Vector(Inputs["c1"].text)); break;
                case "Station": operation = new SetOpenBeltOutputPulleyStationEdit(id, Mm(Inputs["station"].text)); break;
                case "OutputFrame": operation = new SetOpenBeltOutputShaftFrameEdit(c.OutputShaft.Id, Frame(Inputs["output-frame"].text)); break;
                case "Move": operation = new MoveOpenBeltOutputShaftGroupEdit(c.OutputShaft.Id, Frame(Inputs["move"].text)); break;
                case "Terminal": operation = new SetOpenBeltOutputTerminalEdit(Draft.Definition.Output.Key, Frame(Inputs["terminal-frame"].text)); break;
                case "Route": operation = new SetOpenBeltRouteEdit(id, Vector(Inputs["normal"].text), Inputs["routing"].text); break;
                case "Reference": var reference = Parts(Inputs["reference"].text, ',', 2); operation = new SetOpenBeltReferenceEdit(id, ExactQuantity.Turns(Rational.Parse(reference[0])), ExactQuantity.Turns(Rational.Parse(reference[1]))); break;
                case "Belt": if (ProposedBelt == null) throw new InvalidOperationException("Propose or load a belt first; radius and placement edits do not prepare one implicitly."); operation = new SetOpenBeltLengthSpecificationEdit(id, ProposedBelt); break;
                case "ClearBelt": operation = new SetOpenBeltLengthSpecificationEdit(id, null); break;
                case "Targets": var targets = Parts(Inputs["targets"].text, ',', 2); operation = new SetOpenBeltRequirementsEdit(Draft.Definition.Output.Key, Optional(targets[0]), Optional(targets[1])); break;
                case "Transmission": if (Inputs["value"].text != "true" && Inputs["value"].text != "false") throw new FormatException("Explicit true/false required."); operation = new SetOpenBeltTransmissionEdit(c.Id, Inputs["value"].text == "true"); break;
                case "Binding": operation = new SetOpenBeltInputBindingEdit(c.Id, Inputs["shaft"].text, string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text); break;
                case "Mapping": operation = new SetOpenBeltSourceLengthMappingEdit(Mapping()); break;
                case "Required": operation = new SetOpenBeltRequiredValidationEdit(RequiredDomains()); break;
                case "SourceRemove": operation = SourceEdit(new RemoveContactEdit(Inputs["value"].text)); break;
                case "SourceRestore": operation = SourceEdit(new AddContactEdit(initial.Definition.Source.Definition.Contacts.Single(x => x.Id == Inputs["value"].text))); break;
                default: throw new FormatException("Typed edits: R1,R2,Center,Station,OutputFrame,Move,Terminal,Route,Reference,Belt,ClearBelt,Targets,Transmission,Binding,Mapping,Required,SourceRemove,SourceRestore.");
            }
            if (pending.Count >= 128) throw new FormatException("Pending operation bound exceeded."); pending.Add(operation); Notice = "Queued " + operation.Kind + "; " + pending.Count + " pending. Current mechanism and installed belt unchanged.";
        }
        private OpenBeltEditOperation SourceEdit(MechanicalEditOperation operation)
        {
            var source = Draft.Definition.Source;
            foreach (var queued in pending.OfType<ApplyOpenBeltSourceEditsEdit>())
            { var preview = sdk.ApplyMechanicalEdits(source, queued.Batch); if (preview.Draft == null) throw new InvalidOperationException("Earlier queued source transaction is rejected."); source = preview.Draft; }
            return new ApplyOpenBeltSourceEditsEdit(new MechanicalEditBatch(source.Revision, source.DefinitionId, new[] { operation }));
        }
        private void Apply()
        {
            NeedDraft(); var batch = new OpenBeltEditBatch(Draft.Revision, Draft.DefinitionId, pending); LastEdit = sdk.ApplyOpenBeltEdits(Draft, batch);
            history.Add(batch); pending.Clear(); Finalization = null;
            if (LastEdit.Draft != null) { Draft = LastEdit.Draft; Source = Draft.Definition.Source; Hydrate(); Analyze(); }
            Notice = LastEdit.Status + "; revision=" + Draft.Revision + ". Radii, length selection and targets changed only through their submitted typed edits.";
        }
        private void Query() { NeedDraft(); ConnectionQuery = sdk.QueryOpenBeltCompatibility(Draft); Notice = "Local " + ConnectionQuery.Verdict + "; selected belt " + ConnectionQuery.LengthCompatibility.Verdict + "; no whole-solid/traction claim."; }
        private void MarkBefore() { NeedDraft(); Before = Draft; Comparison = null; AllowComparison(); Notice = "Marked immutable before; comparisons from this intermediate state are NOT persisted as initial-to-current history."; }
        private void Compare()
        {
            NeedDraft(); if (comparisonReadOnly) throw new InvalidOperationException("Loaded input/domain mapping cannot be edited in this form; SDK/CLI or Mark before starts a new request.");
            var parts = Parts(Inputs["comparison"].text, ';', 2); var map = Parts(parts[1], ',', 2);
            if (!Enum.TryParse(parts[0], false, out OutputComparisonMode mode) || !Enum.IsDefined(typeof(OutputComparisonMode), mode)) throw new FormatException("Unknown comparison mode.");
            var request = new OutputComparisonRequest(Before.Definition.Output.Key, Draft.Definition.Output.Key, Before.Definition.Source.Definition.RootShaftId, Draft.Definition.Source.Definition.RootShaftId,
                sign: Rational.Parse(map[0]), delta: Rational.Parse(map[1]), mode: mode);
            Comparison = sdk.CompareOpenBeltOutputMotion(sdk.AnalyzeOpenBeltDraft(Before), Analysis, request); var persist = Before.DraftId == initial.DraftId;
            if (persist && !comparisons.Any(r => r.RequestId == request.RequestId))
            { if (comparisons.Count >= OpenBeltProfile.MaxComparisons) throw new InvalidOperationException("Comparison history bound exceeded."); comparisons.Add(request); }
            Notice = "Scoped " + Comparison.Verdict + "; same route=" + Comparison.SameRoute + "; lengths=" + Comparison.BeforeLength + "/" + Comparison.AfterLength +
                (persist ? "; initial-to-current request recorded." : "; intermediate-before request NOT persisted.");
        }
        private void AllowComparison() { comparisonReadOnly = false; Inputs["comparison"].interactable = true; Buttons["compare"].interactable = true; Inputs["comparison"].text = "FullAffineOutput;1,0"; }
        private void HydrateComparison()
        {
            AllowComparison(); if (comparisons.Count == 0) return; var request = comparisons[comparisons.Count - 1]; Before = initial;
            Comparison = sdk.CompareOpenBeltOutputMotion(sdk.AnalyzeOpenBeltDraft(initial), Analysis, request);
            var fits = request.BeforeOutputKey == initial.Definition.Output.Key && request.AfterOutputKey == Draft.Definition.Output.Key &&
                request.BeforeInputId == initial.Definition.Source.Definition.RootShaftId && request.AfterInputId == Draft.Definition.Source.Definition.RootShaftId &&
                request.Alpha == 1 && request.Beta == 0 && request.MotionDomain == OpenBeltProfile.MotionDomain;
            Inputs["comparison"].text = fits ? request.Mode + ";" + request.Sign + "," + request.Delta : "Saved extended input/domain mapping: read-only";
            comparisonReadOnly = !fits; Inputs["comparison"].interactable = fits; Buttons["compare"].interactable = fits;
        }
        private void FinalizeDraft() { NeedDraft(); Finalization = sdk.TryFinalizeOpenBeltDraft(Draft); Notice = "Finalize " + Finalization.Status + "; traction/tension/slip/solids/dynamics remain NotPerformed."; }
        private void SaveArtifact()
        { NeedDraft(); if (Finalization == null || !Finalization.IsFinalized || Finalization.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Finalize this exact current definition first."); sdk.SaveOpenBeltFinalization(Finalization, Inputs["save"].text); Notice = "Current source-aware open belt artifact saved with exact staged readback."; }
        private void SaveDraft() { NeedDraft(); sdk.SaveOpenBeltDraft(Draft, Inputs["save"].text); Notice = "Declarations saved; an invalid/undriven draft is not an admitted artifact."; }
        private void SaveSession() { NeedDraft(); sdk.SaveOpenBeltEditSession(sdk.CreateOpenBeltEditSession(initial, history, comparisons), Inputs["save"].text); Notice = "Ordered actual operations and initial-to-current comparisons saved; readers execute them again."; }
        private void LoadSession()
        {
            var session = sdk.ReadOpenBeltEditSession(ReadFile()); Adopt(session.InitialDraft); history.AddRange(session.Batches); comparisons.AddRange(session.ComparisonRequests);
            Draft = session.CurrentDraft; Hydrate(); Analyze(); HydrateComparison(); Notice = "Loaded actual current session; exact fields and explicit saved comparison mapping retained.";
        }
        private void Reapply() { NeedDraft(); Draft = sdk.ReapplyOpenBeltEditSession(sdk.CreateOpenBeltEditSession(initial, history, comparisons)); Hydrate(); Analyze(); HydrateComparison(); Notice = "Ordered Reapply -> fresh Analyze; fields hydrated from reconstructed current declarations."; }
        private void Rebuild()
        { var rebuilt = sdk.RebuildOpenBeltArtifact(sdk.ReadOpenBeltArtifact(ReadFile())); sdk.SaveOpenBeltArtifact(rebuilt.Artifact, Inputs["save"].text); if (!File.ReadAllBytes(Inputs["save"].text).SequenceEqual(rebuilt.Bytes)) throw new IOException("Fresh saved rebuild bytes differ."); Notice = "Request-only source/route/length/graph Rebuild saved; stored coefficients and tangent points were not treated as authority."; }
        private void Close()
        {
            Draft = Before = initial = null; Source = null; Analysis = null; Evaluation = null; LastEdit = null; Comparison = null; ConnectionQuery = null; Finalization = null; ProposedBelt = null;
            history.Clear(); pending.Clear(); comparisons.Clear(); comparisonReadOnly = false; IsPlaying = false; DisplayUnavailableReason = null; if (View != null) View.Clear();
            if (Inputs.ContainsKey("proposal")) Inputs["proposal"].text = "none"; if (Inputs.ContainsKey("belt")) Inputs["belt"].text = "none"; Notice = "Closed. No old pulley or belt material pose is current.";
        }
        private void Update()
        {
            if (IsPlaying)
            {
                try { var micros = (long)((Time.realtimeSinceStartupAsDouble - playStarted) * 1000000); Inputs["root"].text = (playRoot + new Rational(micros, 1000000)).ToString(); Evaluate(); }
                catch (Exception error) { InputError(error); }
            }
            notice.text = Notice;
            if (Draft == null || Analysis == null) { readback.text = "No current open-belt analysis or motion. Whole source loaded=" + (Source != null); return; }
            var c = Draft.Definition.Device; var local = Analysis.LocalCompatibility; var output = Analysis.Output;
            var text = "CURRENT " + Draft.DefinitionId + "  revision " + Draft.Revision + "\n" +
                "Source bodies=" + Draft.Definition.Source.Definition.Bodies.Count + "; TWO distinct added pulleys; shafts=" + Analysis.Nodes.Count + "; drivers=" + Analysis.Nodes.Count(n => n.IsPrescribed) + "\n" +
                "R1=" + c.InputPitchRadius + "; R2=" + c.OutputPitchRadius + "; center1=" + c.InputPulleyCenterMm + "; center2=" + c.OutputPulleyCenterMm + "\n" +
                "Selected belt=" + BeltText(c.SelectedBelt) + "\nLENGTH " + local.LengthCompatibility.Verdict + ": " + local.LengthCompatibility.ProofKind + "\n" +
                "Route=" + (local.Route?.RouteId ?? "INVALID / UNAVAILABLE") + "; prospective q (NOT admitted)=" + local.ProspectiveTransfer + "; admitted q=" + local.AdmittedTransfer + "\n" +
                "Axes input/output/terminal signs=" + local.InputAxisRouteSign + "/" + local.OutputAxisRouteSign + "/" + local.TerminalCoordinateSign + "; route normal=" + c.RouteNormal + "\n" +
                "Reference turns=" + c.InputReferenceTurns + "/" + c.OutputReferenceTurns + "; shaft law=" + Law(output.ShaftRelation) + "; terminal law=" + Law(output.PortRelation) + "\n" +
                "Output=" + output.Determinacy + "; target=" + output.Target + "; whole valid=" + Analysis.IsMechanicallyValid + "\n";
            if (DisplayUnavailableReason != null) text += "DISPLAY_UNAVAILABLE: " + DisplayUnavailableReason + ". Exact analysis/identity are preserved; no visual clamp or false mechanical failure.\n";
            text += !c.TransmissionPresent ? "BELT REMOVED. Fixed pitch outlines/tangencies are reference geometry, not stopped material.\n" :
                local.IsAdmitted ? "GREEN: locally admitted ideal route and material motion; targets/whole export remain independent.\n" :
                "ORANGE: UNADMITTED required-route preview; selected belt NOT stretched. No admitted belt material motion.\n";
            if (View.DisplayRoute != null) text += "DISPLAY ONLY length~=" + View.DisplayRoute.LoopLengthMm.ToString("G12", CultureInfo.InvariantCulture) + " mm; wraps=" +
                View.DisplayRoute.InputWrapRadians.ToString("G6", CultureInfo.InvariantCulture) + "/" + View.DisplayRoute.OutputWrapRadians.ToString("G6", CultureInfo.InvariantCulture) + " rad\n";
            if (Evaluation == null) text += "NO CURRENT EVALUATION. Failed input cannot show a cached successful frame.\n";
            else
            {
                text += "ABSOLUTE ROOT " + Evaluation.Input + " -> " + Evaluation.Status + "\n";
                foreach (var r in Evaluation.Rotary) text += "ROTARY " + r.ShaftId + "=" + r.Turns + " turn; +axis=" + r.PositiveAxis + "\n";
                text += Evaluation.Output == null ? "BELT OUTPUT UNKNOWN / NO SUCCESSFUL POSE. Fixed outlines are reference geometry only.\n" :
                    "OUTPUT SHAFT=" + Evaluation.Output.ShaftTurns + "; TERMINAL=" + Evaluation.Output.TerminalTurns + "; clockwise material travel=" + Evaluation.Output.BeltMaterialTravelPiCoefficientMm + " * pi mm\n";
            }
            if (LastEdit != null) text += "EDIT " + LastEdit.Status + "; failed index=" + LastEdit.FailingOperationIndex + "; queued=" + pending.Count + "\n";
            if (Comparison != null) text += "COMPARE " + Comparison.Verdict + " / " + Comparison.Scope + "; same route=" + Comparison.SameRoute + "; same specification=" + Comparison.SameBeltSpecification + "\n" +
                "Input alpha=" + Comparison.Request.Alpha + "; beta=" + Comparison.Request.Beta + "; output sign=" + Comparison.Request.Sign + "; delta=" + Comparison.Request.Delta + (comparisonReadOnly ? " [read-only extended mapping]" : "") + "\n";
            if (Finalization != null) text += "FINALIZE " + Finalization.Status + "\n";
            text += "Persisted initial-to-current comparisons=" + comparisons.Count + "; proposal=" + (ProposedBelt?.SpecificationId ?? "none") + "\n";
            foreach (var check in Analysis.Checks) text += check.Domain + ": " + check.Verdict + (check.Required ? " [required]" : "") + "\n";
            foreach (var issue in Analysis.Diagnostics.Concat(LastEdit == null ? Enumerable.Empty<MechanicalDiagnostic>() : LastEdit.Diagnostics)) text += issue.Stage + "/" + issue.Code + "\n";
            text += "Tangencies/spans stay FIXED; pulley and belt MATERIAL marks move. Source bodies without channels are reference projections, not solved zero speed.\nNo tooth indexing, traction/tension/slip/solids/dynamics/manufacturing proof.";
            readback.text = text; var height = Mathf.Max(485, readback.preferredHeight + 14); readback.rectTransform.sizeDelta = new Vector2(845, height); ((RectTransform)readback.transform.parent).sizeDelta = new Vector2(855, height + 10);
        }
        private void InputError(Exception error)
        {
            if (error is OpenBeltDisplayUnavailableException display) { DisplayError(display); return; }
            IsPlaying = false; Finalization = null; Evaluation = null; Comparison = null; ConnectionQuery = null; if (View != null) View.HideCurrentMotion();
            Notice = "INPUT/OPERATION ERROR: " + error.Message + "; no cached successful frame is current.";
        }
        private void DisplayError(OpenBeltDisplayUnavailableException error)
        {
            IsPlaying = false; DisplayUnavailableReason = error.Message; if (View != null) View.Clear();
            Notice = "DISPLAY_UNAVAILABLE: " + error.Message + "; exact draft/analysis/evaluation and source-aware finalization remain independent.";
        }
        private void Fit() { var center = View.ViewCenter; var radius = View.ViewRadius; sceneCamera.transform.position = center + new Vector3(.2f, -.45f, 1.5f).normalized * radius * 3; sceneCamera.transform.LookAt(center); sceneCamera.orthographicSize = radius * .7f; sceneCamera.nearClipPlane = .1f; sceneCamera.farClipPlane = radius * 8 + 100; }
        private void OnDestroy() { if (!ownsSceneCamera && sceneCamera != null && previousCamera != null) previousCamera.Restore(sceneCamera); }
        private string[] RequiredDomains() => Inputs["required"].text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToArray();
        private static string BeltText(OpenBeltLengthSpecification b) => b == null ? "none" : "D=" + b.CenterDistance.Value + "; radii=" + b.InputPitchRadius.Value + "," + b.OutputPitchRadius.Value + "; " + b.SpecificationId;
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
