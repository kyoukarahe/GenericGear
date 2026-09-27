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
    /// <summary>Optional bounded CP form. The public SDK owns source truth, edits, tangency, law, domain, comparison and persistence.</summary>
    public sealed class GearInvestRackPinionInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public RackPinionDraft Draft { get; private set; }
        public RackPinionDraft Before { get; private set; }
        public RackPinionAnalysis Analysis { get; private set; }
        public RackPinionEvaluation Evaluation { get; private set; }
        public RackPinionEditResult LastEdit { get; private set; }
        public PrismaticOutputEquivalenceResult Comparison { get; private set; }
        public RackPinionCompatibilityResult ConnectionQuery { get; private set; }
        public RackPinionFinalizationResult Finalization { get; private set; }
        public GearInvestRackPinionView View { get; private set; }
        public MechanicalDraft Source { get; private set; }
        public string Notice { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public bool IsPlaying { get; private set; }
        public int PendingOperationCount => pending.Count;
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<RackPinionEditBatch> history = new List<RackPinionEditBatch>();
        private readonly List<RackPinionEditOperation> pending = new List<RackPinionEditOperation>();
        private readonly List<LinearOutputComparisonRequest> comparisons = new List<LinearOutputComparisonRequest>();
        private RackPinionDraft initial; private bool comparisonReadOnly; private Font font; private Text notice, readback; private Camera sceneCamera;
        private double playStarted; private Rational playRoot;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";

        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("Rack EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            sceneCamera = Camera.main; if (sceneCamera == null) { var go = new GameObject("Rack camera"); go.tag = "MainCamera"; sceneCamera = go.AddComponent<Camera>(); }
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.035f, .055f, .085f);
            sceneCamera.orthographic = true; sceneCamera.rect = new Rect(700f / 1600, 520f / 1050, 900f / 1600, 530f / 1050);
            var uiCamera = new GameObject("Rack UI camera").AddComponent<Camera>(); uiCamera.transform.SetParent(transform, false);
            uiCamera.cullingMask = 1 << 5; uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.depth = sceneCamera.depth + 10;
            uiCamera.transform.position = new Vector3(0, 0, -1000); uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 30;
            var canvasObject = new GameObject("Rack authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Rack control panel", 0, 0, 700, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "CIRCULAR-PITCH RACK / FIXED PINION", 12, 9, 675, 28, 20);
            Label(panel, "Exact CP + r+c/pi mm. Scroll parameters. Prepare guide is explicit, never an automatic repair.", 12, 40, 675, 30, 12);
            var viewport = GearInvestAuthoringWidgets.Box(panel, "Scrollable CP parameters", 0, 74, 700, 610, new Color(.055f, .07f, .1f));
            viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var form = GearInvestAuthoringWidgets.Rect(viewport, "Explicit CP inputs", 0, 0, 700, 775); scroll.content = form;
            var y = 0f;
            void Field(string key, string title, string value) { Inputs.Add(key, GearInvestAuthoringWidgets.Field(font, form, key, title, value, y)); y += 32; }
            Field("file", "Source / draft / session / artifact file", "");
            Field("shaft", "Actual retained shaft", "output"); Field("port", "Optional existing mounting port", "");
            Field("scale", "Explicit mm / source unit", "1"); Field("pose", "Source pose origin(mm);X;Y;Z", Identity);
            Field("teeth", "CP pinion integer teeth Z", "20"); Field("pinion-pitch", "Pinion circular pitch pP (mm)", "5"); Field("rack-pitch", "Rack pitch pR (mm)", "5");
            Field("center", "Pinion center C0 on retained shaft (mm)", "60,0,40"); Field("normal", "Center-to-rack cardinal normal b", "0,-1,0"); Field("direction", "Increasing guide direction g", "1,0,0");
            Field("guide", "Guide rationalXYZ|inversePiXYZ;X;Y;g", "60,0,40|0,-50,0;0,0,1;0,-1,0;1,0,0");
            Field("longitudinal", "Guide longitudinal offset d (mm)", "0"); Field("reference", "Pinion reference turns,rack reference mm", "0,20");
            Field("material", "Moving material xi closed low,high (mm)", "-120,80"); Field("travel", "Guide reference x closed low,high (mm)", "-90,130");
            Field("requirements", "Target gain,root0 terminal;root low,high", "-50,20;-2,2"); Field("terminal", "Terminal sign,datum(mm) [coordinate only]", "1,0");
            Field("operation", "Typed edit (Teeth / Guide / Requirements...)", "Teeth"); Field("value", "Edit ID / source contact / true,false", "");
            Field("comparison", "Mode; mapped-after sign,datum(mm)", "FullLinearAffineOutput;1,0"); Field("root", "Absolute unwrapped input turns", "1/4"); Field("save", "New file path (never overwrite)", "");
            form.sizeDelta = new Vector2(700, y + 8);
            var row = 698f;
            void Four(string a, string at, Action aa, string b, string bt, Action ba, string c, string ct, Action ca, string d, string dt, Action da)
            { Button(panel, a, at, 12, row, 162, aa); Button(panel, b, bt, 182, row, 162, ba); Button(panel, c, ct, 352, row, 162, ca); Button(panel, d, dt, 522, row, 164, da); row += 39; }
            Four("example", "Public 20T -> 40T", PublicSource, "load-source", "Load source", LoadSource, "next-shaft", "Next actual shaft", NextShaft, "standalone", "Standalone pinion", StandaloneSource);
            Four("compose", "Compose / Analyze", Compose, "prepare-guide", "Prepare guide fields", PrepareGuide, "queue", "Queue edit", Queue, "apply", "Apply batch", Apply);
            Four("reanalyze", "Reanalyze", Analyze, "query", "Local query", Query, "compare", "Compare", Compare, "mark-before", "Mark before", MarkBefore);
            Four("evaluate", "Evaluate root", Evaluate, "play", "Play / Pause", PlayPause, "finalize", "Finalize", FinalizeDraft, "save-artifact", "Save artifact", SaveArtifact);
            Four("load-draft", "Load draft", () => Adopt(sdk.ReadRackPinionDraft(ReadFile())), "load-session", "Load session", LoadSession, "save-draft", "Save draft", SaveDraft, "save-session", "Save history", SaveSession);
            Four("reapply", "Reapply history", Reapply, "rebuild", "Rebuild artifact", Rebuild, "clear-queue", "Clear queued edits", () => { pending.Clear(); Notice = "Queued edits cleared; current draft unchanged."; }, "close", "Close current", Close);
            notice = Label(panel, "Choose a real source and compose explicit CP inputs.", 12, 937, 674, 102, 14);
            var readViewport = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Exact rack readback", 710, 540, 878, 499, new Color(.065f, .085f, .115f));
            readViewport.gameObject.AddComponent<RectMask2D>(); var readScroll = readViewport.gameObject.AddComponent<ScrollRect>(); readScroll.viewport = readViewport; readScroll.horizontal = false; readScroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Rect(readViewport, "Exact rack results", 0, 0, 855, 485); readScroll.content = content;
            readback = Label(content, "No current motion.", 7, 4, 845, 480, 14);
            View = new GameObject("Rack schematic view").AddComponent<GearInvestRackPinionView>(); View.transform.SetParent(transform, false);
            Notice = "Public source / original artifact -> explicit shaft, CP and guide -> Compose. Tooth edits never relocate a stored guide.";
        }
        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Compose/load a rack draft first."); }
        private byte[] ReadFile()
        { var path = Inputs["file"].text; if (new FileInfo(path).Length > RackPinionProfile.MaxDocumentBytes) throw new FormatException("Input byte bound exceeded."); var bytes = File.ReadAllBytes(path); if (bytes.Length > RackPinionProfile.MaxDocumentBytes) throw new FormatException("Input byte bound exceeded."); return bytes; }
        private void PublicSource()
        {
            var kinematic = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") }, new[] { new ExternalGearCoupling("mesh", "input", "output", 20, 40) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", 0, 0), new SpatialAxis("output-axis", 60, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, 20, 20), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, 40, 40) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("oriented-example-spur-20-40-0", kinematic, spatial));
            if (!generated.IsSuccess) throw new InvalidOperationException("Public source generation failed.");
            ExampleFields(false); SetSource(sdk.ImportMechanicalArtifact(sdk.WriteArtifact(generated.Candidates.Single()).Bytes), "output", 40);
        }
        private void StandaloneSource()
        {
            ExampleFields(true); SetSource(new MechanicalDraft(new MechanicalDefinition("pinion", new[] { new OrientedShaft("pinion", OrientedFrame.Identity, true) },
                Array.Empty<OrientedGearBody>(), Array.Empty<MechanicalContact>(), Array.Empty<ShaftPort>(), clearancePolicy: MechanicalAuthoringProfile.PlanarClearance)), "pinion", 0);
        }
        private void ExampleFields(bool standalone)
        {
            Inputs["scale"].text = "1"; Inputs["pose"].text = Identity; Inputs["teeth"].text = "20"; Inputs["pinion-pitch"].text = Inputs["rack-pitch"].text = "5";
            Inputs["reference"].text = "0,20"; Inputs["longitudinal"].text = "0"; Inputs["material"].text = "-120,80"; Inputs["travel"].text = "-90,130";
            Inputs["terminal"].text = "1,0"; Inputs["root"].text = "1/4"; Inputs["requirements"].text = standalone ? "100,20;-1,1" : "-50,20;-2,2";
        }
        private void LoadSource()
        { var source = sdk.ImportMechanicalArtifact(ReadFile()); SetSource(source, source.Definition.Outputs.FirstOrDefault()?.ShaftId ?? source.Definition.RootShaftId, 40); }
        private void SetSource(MechanicalDraft source, string shaft, Rational station)
        { Close(); Source = source; Inputs["shaft"].text = shaft; MountFields(station); Notice = "Retained the whole source. Visible mounting/CP inputs are proposals; Compose remains a separate action."; }
        private void NextShaft()
        {
            if (Source == null) throw new InvalidOperationException("Load a source first."); var shafts = Source.Definition.Shafts.ToArray();
            var index = Array.FindIndex(shafts, s => s.Id == Inputs["shaft"].text); Inputs["shaft"].text = shafts[(index + 1) % shafts.Length].Id; MountFields(40);
            Notice = "Selected an actual source shaft; review proposed mounting fields before Compose or Queue Binding/Guide.";
        }
        private void MountFields(Rational station)
        {
            var mapped = Mapping().FrameMm(Source.Definition.Shafts.Single(s => s.Id == Inputs["shaft"].text).Frame);
            Inputs["port"].text = Source.Definition.Outputs.FirstOrDefault(o => o.ShaftId == Inputs["shaft"].text)?.PortId ?? "";
            Inputs["center"].text = Point(mapped.Origin + mapped.Z * station); Inputs["normal"].text = Point(-mapped.Y); Inputs["direction"].text = Point(mapped.X); PrepareGuide();
        }
        private SourceLengthMapping Mapping() => new SourceLengthMapping(Rational.Parse(Inputs["scale"].text), Frame(Inputs["pose"].text));
        private void PrepareGuide()
        {
            if (Source == null) throw new InvalidOperationException("Load/select a real source first.");
            var axis = Mapping().Direction(Source.Definition.Shafts.Single(s => s.Id == Inputs["shaft"].text).Frame.Z);
            var proposal = RackPinionGeometry.CreateTangentRackGuide(Vector(Inputs["center"].text), axis, Vector(Inputs["normal"].text), Vector(Inputs["direction"].text),
                Integer(Inputs["teeth"].text), Mm(Inputs["pinion-pitch"].text), Mm(Inputs["longitudinal"].text));
            Inputs["guide"].text = PiFrameText(proposal);
            Notice = "Prepared exact guide fields only. Current draft is unchanged; select Guide, Queue and Apply to accept this placement.";
        }
        private LinearOutputRequirement Requirement()
        { var parts = Parts(Inputs["requirements"].text, ';', 2); var q = Parts(parts[0], ',', 2); return new LinearOutputRequirement(q[0] == "none" ? (ExactQuantity?)null : ExactQuantity.MillimetersPerTurn(Rational.Parse(q[0])), q[1] == "none" ? (ExactQuantity?)null : Mm(q[1]), Interval(parts[1], true)); }
        private void Compose()
        {
            if (Source == null) throw new InvalidOperationException("Load/generate a real source first."); var refs = Parts(Inputs["reference"].text, ',', 2); var terminal = Parts(Inputs["terminal"].text, ',', 2);
            var device = new CircularPitchRackDefinition("rack-pinion", Inputs["shaft"].text, "rack-pinion/pinion", "rack-pinion/rack", "rack-pinion/guide", "rack-pinion/translation",
                Integer(Inputs["teeth"].text), Mm(Inputs["pinion-pitch"].text), Mm(Inputs["rack-pitch"].text), Vector(Inputs["center"].text), Vector(Inputs["normal"].text), PiFrame(Inputs["guide"].text),
                Mm(Inputs["longitudinal"].text), ExactQuantity.Turns(Rational.Parse(refs[0])), Mm(refs[1]), Interval(Inputs["material"].text), Interval(Inputs["travel"].text), pinionPortId: Port());
            Adopt(sdk.ComposeRackPinionDraft(new RackPinionDefinition(Source, Mapping(), device,
                new PrismaticOutputDefinition("linear", device.LinearDofId, device.RackBodyId, "rack-reference", Integer(terminal[0]), Mm(terminal[1])), Requirement())));
        }
        private void Adopt(RackPinionDraft draft) { Close(); initial = Before = Draft = draft; Hydrate(); AllowComparison(); Analyze(); Notice = "Adopted declarations and recomputed exact current analysis; fields show the current definition."; }
        private void Hydrate()
        {
            var definition = Draft.Definition; var d = definition.Device; var mapping = definition.SourceMapping; Source = definition.Source;
            Inputs["shaft"].text = d.PinionShaftId; Inputs["port"].text = d.PinionPortId ?? ""; Inputs["scale"].text = mapping?.MillimetersPerSourceUnit.ToString() ?? ""; Inputs["pose"].text = mapping == null ? "" : FrameText(mapping.PoseMm);
            Inputs["teeth"].text = d.PinionToothCount.ToString(CultureInfo.InvariantCulture); Inputs["pinion-pitch"].text = d.PinionCircularPitch.Value.ToString(); Inputs["rack-pitch"].text = d.RackPitch.Value.ToString();
            Inputs["center"].text = Point(d.PinionCenterMm); Inputs["normal"].text = Point(d.ContactNormal); Inputs["direction"].text = Point(d.GuideFrameMm.Z); Inputs["guide"].text = PiFrameText(d.GuideFrameMm);
            Inputs["longitudinal"].text = d.LongitudinalOffset.Value.ToString(); Inputs["reference"].text = d.PinionReferenceTurns.Value + "," + d.RackReferencePosition.Value;
            Inputs["material"].text = IntervalText(d.ActiveMaterialInterval); Inputs["travel"].text = IntervalText(d.GuideInterval);
            Inputs["terminal"].text = definition.Output.TerminalSign.ToString(CultureInfo.InvariantCulture) + "," + definition.Output.TerminalDatum.Value;
            Inputs["requirements"].text = (definition.Requirement.RequiredGain?.Value.ToString() ?? "none") + "," + (definition.Requirement.RequiredReferencePosition?.Value.ToString() ?? "none") + ";" + IntervalText(definition.Requirement.RequiredRootInterval);
        }
        private void Analyze()
        { NeedDraft(); Finalization = null; Comparison = null; ConnectionQuery = null; Analysis = sdk.AnalyzeRackPinionDraft(Draft); View.Build(Draft, Analysis); Evaluate(); Fit(); }
        private void Evaluate()
        { NeedDraft(); Evaluation = sdk.EvaluateRackPinionAnalysis(Analysis, ExactQuantity.Turns(Rational.Parse(Inputs["root"].text))); View.Apply(Evaluation); if (Evaluation.Status != RackPinionEvaluationStatus.Success) IsPlaying = false; Notice = "Evaluation " + Evaluation.Status + ". Absolute input retained; no successful rack pose outside guide/material domain."; }
        private void PlayPause()
        { NeedDraft(); IsPlaying = !IsPlaying; if (IsPlaying) { playStarted = Time.realtimeSinceStartupAsDouble; playRoot = Rational.Parse(Inputs["root"].text); } Notice = IsPlaying ? "Caller clock: +1 absolute root turn/second." : "Caller clock paused."; }
        private void Queue()
        {
            NeedDraft(); var device = Draft.Definition.Device; var id = string.IsNullOrEmpty(Inputs["value"].text) ? device.Id : Inputs["value"].text; RackPinionEditOperation edit;
            switch (Inputs["operation"].text)
            {
                case "Teeth": edit = new SetRackPinionTeethEdit(id, Integer(Inputs["teeth"].text)); break;
                case "PinionPitch": edit = new SetRackPinionCircularPitchEdit(id, Mm(Inputs["pinion-pitch"].text)); break;
                case "RackPitch": edit = new SetRackPitchEdit(id, Mm(Inputs["rack-pitch"].text)); break;
                case "Center": edit = new SetRackPinionCenterEdit(id, Vector(Inputs["center"].text)); break;
                case "Normal": edit = new SetRackContactNormalEdit(id, Vector(Inputs["normal"].text)); break;
                case "Guide": edit = new SetRackGuidePlacementEdit(id, PiFrame(Inputs["guide"].text), Mm(Inputs["longitudinal"].text)); break;
                case "Reference": var refs = Parts(Inputs["reference"].text, ',', 2); edit = new SetRackReferenceEdit(id, ExactQuantity.Turns(Rational.Parse(refs[0])), Mm(refs[1])); break;
                case "Material": edit = new SetRackActiveMaterialIntervalEdit(id, Interval(Inputs["material"].text)); break;
                case "Travel": edit = new SetRackGuideIntervalEdit(id, Interval(Inputs["travel"].text)); break;
                case "Terminal": var terminal = Parts(Inputs["terminal"].text, ',', 2); edit = new SetRackTerminalEdit(Draft.Definition.Output.Key, Integer(terminal[0]), Mm(terminal[1])); break;
                case "Requirements": edit = new SetRackRequirementsEdit(Draft.Definition.Output.Key, Requirement()); break;
                case "Transmission":
                    if (Inputs["value"].text != "true" && Inputs["value"].text != "false") throw new FormatException("Explicit true/false required.");
                    edit = new SetRackTransmissionEdit(device.Id, Inputs["value"].text == "true"); break;
                case "Binding": edit = new SetRackPinionBindingEdit(device.Id, Inputs["shaft"].text, Port()); break;
                case "Mapping": edit = new SetRackSourceLengthMappingEdit(Mapping()); break;
                case "SourceRemove": edit = SourceEdit(new RemoveContactEdit(Inputs["value"].text)); break;
                case "SourceRestore": edit = SourceEdit(new AddContactEdit(initial.Definition.Source.Definition.Contacts.Single(c => c.Id == Inputs["value"].text))); break;
                default: throw new FormatException("Typed edits: Teeth, PinionPitch, RackPitch, Center, Normal, Guide, Reference, Material, Travel, Terminal, Requirements, Transmission, Binding, Mapping, SourceRemove, SourceRestore.");
            }
            if (pending.Count == 128) throw new FormatException("Pending operation bound exceeded."); pending.Add(edit); Notice = "Queued " + edit.Kind + "; " + pending.Count + " pending. No current state changed.";
        }
        private RackPinionEditOperation SourceEdit(MechanicalEditOperation operation)
        {
            var source = Draft.Definition.Source;
            foreach (var request in pending.OfType<ApplyRackSourceEditsEdit>())
            { var preview = sdk.ApplyMechanicalEdits(source, request.Batch); if (preview.Draft == null) throw new InvalidOperationException("An earlier queued source edit is rejected; clear/apply first."); source = preview.Draft; }
            return new ApplyRackSourceEditsEdit(new MechanicalEditBatch(source.Revision, source.DefinitionId, new[] { operation }));
        }
        private void Apply()
        {
            NeedDraft(); var batch = new RackPinionEditBatch(Draft.Revision, Draft.DefinitionId, pending); LastEdit = sdk.ApplyRackPinionEdits(Draft, batch);
            history.Add(batch); pending.Clear(); Finalization = null;
            if (LastEdit.Draft != null) { Draft = LastEdit.Draft; Source = Draft.Definition.Source; Analyze(); }
            Notice = LastEdit.Status + "; revision=" + Draft.Revision + ". Guide, target and range change only through their explicit edits.";
        }
        private void Query() { NeedDraft(); ConnectionQuery = sdk.QueryRackPinionCompatibility(Draft); Notice = "Local " + ConnectionQuery.Verdict + "; does not prove full source/range/target/solid support."; }
        private void MarkBefore()
        { NeedDraft(); Before = Draft; Comparison = null; AllowComparison(); Notice = "Marked immutable before snapshot. Intermediate-before comparisons are visible but NOT persisted as initial-to-current history."; }
        private void Compare()
        {
            NeedDraft(); if (comparisonReadOnly) throw new InvalidOperationException("Loaded extended input/domain mapping is read-only in this bounded form; use SDK/CLI or Mark before for a new request.");
            var parts = Parts(Inputs["comparison"].text, ';', 2); var mapping = Parts(parts[1], ',', 2);
            if (!Enum.TryParse(parts[0], false, out LinearOutputComparisonMode mode) || !Enum.IsDefined(typeof(LinearOutputComparisonMode), mode)) throw new FormatException("Unknown comparison mode.");
            var request = new LinearOutputComparisonRequest(Before.Definition.Output.Key, Draft.Definition.Output.Key, Before.Definition.Source.Definition.RootShaftId, Draft.Definition.Source.Definition.RootShaftId,
                sign: Integer(mapping[0]), delta: Mm(mapping[1]), mode: mode,
                beforeReferencePointId: mode == LinearOutputComparisonMode.WorldLinearPath ? Before.Definition.Output.ReferencePointId : null,
                afterReferencePointId: mode == LinearOutputComparisonMode.WorldLinearPath ? Draft.Definition.Output.ReferencePointId : null);
            Comparison = sdk.CompareLinearOutputMotion(sdk.AnalyzeRackPinionDraft(Before), Analysis, request); var persistable = Before.DraftId == initial.DraftId;
            if (persistable && !comparisons.Any(r => r.RequestId == request.RequestId))
            { if (comparisons.Count == RackPinionProfile.MaxComparisons) throw new InvalidOperationException("Session comparison bound reached."); comparisons.Add(request); }
            Notice = "Scoped " + Comparison.Verdict + "; same actual domains=" + Comparison.OperatingDomainsEqual +
                (persistable ? "; explicit initial-to-current request saved in history." : "; intermediate-before request NOT persisted as initial-to-current history.");
        }
        private void AllowComparison()
        { comparisonReadOnly = false; Inputs["comparison"].interactable = true; Buttons["compare"].interactable = true; Inputs["comparison"].text = "FullLinearAffineOutput;1,0"; }
        private void HydrateComparison()
        {
            AllowComparison(); if (comparisons.Count == 0) return; var request = comparisons[comparisons.Count - 1]; Before = initial;
            Comparison = sdk.CompareLinearOutputMotion(sdk.AnalyzeRackPinionDraft(initial), Analysis, request);
            var path = request.Mode == LinearOutputComparisonMode.WorldLinearPath;
            var representable = request.BeforeOutputKey == initial.Definition.Output.Key && request.AfterOutputKey == Draft.Definition.Output.Key &&
                request.BeforeInputId == initial.Definition.Source.Definition.RootShaftId && request.AfterInputId == Draft.Definition.Source.Definition.RootShaftId &&
                request.Alpha == ExactQuantity.TurnsPerTurn(1) && request.Beta == ExactQuantity.Turns(0) && request.Delta.Kind == QuantityKind.LinearPosition && request.ComparisonInterval == null &&
                (path ? request.BeforeReferencePointId == initial.Definition.Output.ReferencePointId && request.AfterReferencePointId == Draft.Definition.Output.ReferencePointId : request.BeforeReferencePointId == null && request.AfterReferencePointId == null);
            Inputs["comparison"].text = representable ? request.Mode + ";" + request.Sign.ToString(CultureInfo.InvariantCulture) + "," + request.Delta.Value : "Saved extended mapping: read-only (exact readback below)";
            comparisonReadOnly = !representable; Inputs["comparison"].interactable = representable; Buttons["compare"].interactable = representable;
        }
        private void FinalizeDraft() { NeedDraft(); Finalization = sdk.TryFinalizeRackPinionDraft(Draft); Notice = "Finalize " + Finalization.Status + "; actual tooth flank, solids, forces and dynamics remain NotPerformed."; }
        private void SaveArtifact()
        { NeedDraft(); if (Finalization == null || !Finalization.IsFinalized || Finalization.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Finalize this exact current definition first."); sdk.SaveRackPinionFinalization(Finalization, Inputs["save"].text); Notice = "Saved current source-aware rack artifact with exact readback."; }
        private void SaveDraft() { NeedDraft(); sdk.SaveRackPinionDraft(Draft, Inputs["save"].text); Notice = "Saved current declarations, including invalid/undriven drafts. Saved is not mechanical admission."; }
        private void SaveSession()
        { NeedDraft(); sdk.SaveRackPinionEditSession(sdk.CreateRackPinionEditSession(initial, history, comparisons), Inputs["save"].text); Notice = "Saved actual ordered operations and explicit initial-to-current comparisons; reading reapplies and reanalyzes."; }
        private void LoadSession()
        {
            var session = sdk.ReadRackPinionEditSession(ReadFile()); Adopt(session.InitialDraft); history.AddRange(session.Batches); comparisons.AddRange(session.ComparisonRequests);
            Draft = session.CurrentDraft; Hydrate(); Analyze(); HydrateComparison(); Notice = "Loaded current session into editable fields. " + (comparisonReadOnly ? "Extended comparison mapping is read-only." : "Explicit comparison mapping retained.");
        }
        private void Reapply()
        {
            NeedDraft(); Draft = sdk.ReapplyRackPinionEditSession(sdk.CreateRackPinionEditSession(initial, history, comparisons)); Hydrate(); Analyze(); HydrateComparison();
            Notice = "Actual ordered Reapply -> fresh Analyze completed; visible inputs now show the current definition.";
        }
        private void Rebuild()
        {
            var rebuilt = sdk.RebuildRackPinionArtifact(sdk.ReadRackPinionArtifact(ReadFile())); sdk.SaveRackPinionArtifact(rebuilt.Artifact, Inputs["save"].text);
            if (!File.ReadAllBytes(Inputs["save"].text).SequenceEqual(rebuilt.Bytes)) throw new IOException("Fresh rebuilt save readback differs."); Notice = "Request-only fresh Rebuild saved. Cached law, radius, domain and source result were not trusted.";
        }
        private void Close()
        {
            Draft = Before = initial = null; Source = null; Analysis = null; Evaluation = null; LastEdit = null; Comparison = null; ConnectionQuery = null; Finalization = null;
            history.Clear(); pending.Clear(); comparisons.Clear(); comparisonReadOnly = false; IsPlaying = false; if (View != null) View.Clear(); Notice = "Closed. No old rack pose is current.";
        }
        private void Update()
        {
            if (IsPlaying)
            {
                try { var micros = (long)((Time.realtimeSinceStartupAsDouble - playStarted) * 1000000); Inputs["root"].text = (playRoot + new Rational(micros, 1000000)).ToString(); Evaluate(); }
                catch (Exception e) { InputError(e); }
            }
            notice.text = Notice;
            if (Draft == null || Analysis == null) { readback.text = "No current rack analysis or motion. Whole source loaded=" + (Source != null); return; }
            var device = Draft.Definition.Device; var linear = Analysis.LinearOutput;
            var text = "CURRENT " + Draft.DefinitionId + "  revision " + Draft.Revision + "\n" +
                "Whole source bodies=" + Draft.Definition.Source.Definition.Bodies.Count + "; added CP pinion + rack bodies=2; actual shaft=" + device.PinionShaftId + "\n" +
                "CP Z=" + device.PinionToothCount + "; pP=" + device.PinionCircularPitch + "; pR=" + device.RackPitch + "; exact R=" + device.PitchRadius + "\n";
            try { text += "DISPLAY ONLY radius ~= " + device.PitchRadius.ApproximateMillimeters().ToString("G9", CultureInfo.InvariantCulture) + " mm; never mechanical truth\n"; }
            catch (ArgumentException) { text += "DISPLAY ONLY radius unavailable: finite rendering bound exceeded\n"; }
            text += "a=" + linear.SourcePositiveAxis + "; b=" + device.ContactNormal + "; g=" + device.GuideFrameMm.Z + "; contact sign=" + Analysis.LocalCompatibility.ContactSign + "\n" +
                "Linear=" + linear.Determinacy + "; GUIDE gain=" + linear.GuideRelation?.Gain + "; offset=" + linear.GuideRelation?.Offset + "\n" +
                "Actual root closed interval=" + linear.ValidRootInterval + "; required=" + Draft.Definition.Requirement.RequiredRootInterval + " -> " + linear.RequiredRange + "; target=" + linear.Target + "\n" +
                "Guide x=" + device.GuideInterval + "; moving material xi=" + device.ActiveMaterialInterval + "; d=" + device.LongitudinalOffset + "\n";
            if (Evaluation == null) text += "NO CURRENT EVALUATION. Previous successful pose does not represent a failed input.\n";
            else
            {
                text += "REQUESTED ABSOLUTE ROOT " + Evaluation.Input + " -> " + Evaluation.Status + "\n";
                foreach (var rotary in Evaluation.Rotary) text += "ROTARY " + rotary.ShaftId + "=" + rotary.Turns + " turn; +axis=" + rotary.PositiveAxis + "\n";
                text += Evaluation.Linear == null ? "RACK CURRENT: UNKNOWN / NO SUCCESSFUL POSE. Previous pose is hidden.\n" :
                    "PINION=" + Evaluation.Linear.PinionTurns + "; GUIDE x=" + Evaluation.Linear.GuidePosition + "; TERMINAL y=" + Evaluation.Linear.TerminalPosition + "\n" +
                    "WORLD rack reference mm=" + Evaluation.Linear.WorldPositionMm + "; material contact xi=" + Evaluation.Linear.MaterialContactCoordinate + "\n" +
                    "GROUND FIXED Q=" + Evaluation.Linear.FixedContactPointMm + "; terminal gain=" + Evaluation.Linear.TerminalGain + "\n";
                if (Evaluation.DiagnosticLinear != null) text += "DIAGNOSTIC ONLY, UNCLAMPED x=" + Evaluation.DiagnosticLinear.GuidePosition + "; xi=" + Evaluation.DiagnosticLinear.MaterialContactCoordinate + " (not an admitted pose)\n";
                foreach (var diagnostic in Evaluation.Diagnostics) text += diagnostic.Stage + "/" + diagnostic.Code + "\n";
            }
            if (LastEdit != null) text += "EDIT " + LastEdit.Status + "; failed index=" + LastEdit.FailingOperationIndex + "; pending=" + pending.Count + "\n";
            if (Comparison != null) text += "COMPARE " + Comparison.Verdict + " / " + Comparison.Request.Mode + "; same actual domains=" + Comparison.OperatingDomainsEqual + "; both feasible=" + Comparison.BothFeasibleOnComparisonInterval + "\n" +
                "Request alpha=" + Comparison.Request.Alpha + "; beta=" + Comparison.Request.Beta + "; sign=" + Comparison.Request.Sign + "; delta=" + Comparison.Request.Delta + "; comparison interval=" + Comparison.Request.ComparisonInterval + (comparisonReadOnly ? " [read-only extended mapping]" : "") + "\n";
            text += "Persisted comparisons=" + comparisons.Count + " (initial-to-current only).\n";
            if (ConnectionQuery != null) text += "LOCAL " + ConnectionQuery.Verdict + "; port sign evidence only=" + ConnectionQuery.SourcePortCoordinateSign + "\n";
            if (Finalization != null) text += "FINALIZE " + Finalization.Status + "\n";
            foreach (var check in Analysis.Checks) text += check.Domain + ": " + check.Verdict + (check.Required ? " [required]" : "") + "\n";
            foreach (var diagnostic in Analysis.Diagnostics.Concat(LastEdit == null ? Enumerable.Empty<MechanicalDiagnostic>() : LastEdit.Diagnostics)) text += diagnostic.Stage + "/" + diagnostic.Code + "\n";
            text += "Source bodies without a rotary channel are reference projections, NOT solved zero-speed motion.\nPitch circle/line schematic only: tooth flank, solids, swept volume, strength, backlash, forces and dynamics NotPerformed.";
            readback.text = text; var height = Mathf.Max(485, readback.preferredHeight + 14); readback.rectTransform.sizeDelta = new Vector2(845, height); ((RectTransform)readback.transform.parent).sizeDelta = new Vector2(855, height + 10);
        }
        private void InputError(Exception exception)
        { IsPlaying = false; Finalization = null; Evaluation = null; Comparison = null; ConnectionQuery = null; if (View != null) View.HideCurrentMotion(); Notice = "INPUT/OPERATION ERROR: " + exception.Message + "; no previous successful frame shown as current."; }
        private void Fit()
        { var center = View.ViewCenter; var radius = View.ViewRadius; sceneCamera.transform.position = center + new Vector3(.45f, -.7f, 1.4f).normalized * radius * 3; sceneCamera.transform.LookAt(center); sceneCamera.orthographicSize = radius * .72f; sceneCamera.nearClipPlane = .1f; sceneCamera.farClipPlane = radius * 8 + 100; }
        private string Port() => string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text;
        private static int Integer(string text) => int.Parse(text, NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static ExactQuantity Mm(string text) => ExactQuantity.Millimeters(Rational.Parse(text));
        private static string[] Parts(string text, char separator, int count) { var parts = text.Split(separator); if (parts.Length != count) throw new FormatException("Expected " + count + " exact fields."); return parts; }
        private static ExactVector3 Vector(string text) { var p = Parts(text, ',', 3).Select(Rational.Parse).ToArray(); return new ExactVector3(p[0], p[1], p[2]); }
        private static OrientedFrame Frame(string text) { var p = Parts(text, ';', 4).Select(Vector).ToArray(); return new OrientedFrame(p[0], p[1], p[2], p[3]); }
        private static ExactPiFrame PiFrame(string text)
        { var p = Parts(text, ';', 4); var origin = Parts(p[0], '|', 2); return new ExactPiFrame(new ExactPiVector3(Vector(origin[0]), Vector(origin[1])), Vector(p[1]), Vector(p[2]), Vector(p[3])); }
        private static ExactQuantityInterval Interval(string text, bool turns = false)
        { var p = Parts(text, ',', 2).Select(Rational.Parse).ToArray(); return turns ? ExactQuantityInterval.Turns(p[0], p[1]) : ExactQuantityInterval.Millimeters(p[0], p[1]); }
        private static string Point(ExactVector3 point) => point.X + "," + point.Y + "," + point.Z;
        private static string FrameText(OrientedFrame frame) => Point(frame.Origin) + ";" + Point(frame.X) + ";" + Point(frame.Y) + ";" + Point(frame.Z);
        private static string PiFrameText(ExactPiFrame frame) => Point(frame.Origin.RationalPartMm) + "|" + Point(frame.Origin.InversePiCoefficientMm) + ";" + Point(frame.X) + ";" + Point(frame.Y) + ";" + Point(frame.Z);
        private static string IntervalText(ExactQuantityInterval interval) => interval.Lower.Value + "," + interval.Upper.Value;
        private Text Label(Transform parent, string text, float x, float y, float width, float height, int size) => GearInvestAuthoringWidgets.Label(font, parent, text, x, y, width, height, size, new Color(.9f, .94f, 1));
        private void Button(Transform parent, string key, string title, float x, float y, float width, Action action)
        { Buttons.Add(key, GearInvestAuthoringWidgets.Button(font, parent, key, title, x, y, width, () => { try { action(); } catch (Exception e) { InputError(e); } })); }
    }
}
