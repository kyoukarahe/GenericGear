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
    /// <summary>Optional bounded form. SDK owns quantities, bindings, exact law/domain, edits, comparison and persistence. This component owns only the caller clock and presentation.</summary>
    public sealed class GearInvestLinearLeadScrewInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public RotaryLinearDraft Draft { get; private set; }
        public RotaryLinearDraft Before { get; private set; }
        public RotaryLinearAnalysis Analysis { get; private set; }
        public RotaryLinearEvaluation Evaluation { get; private set; }
        public RotaryLinearEditResult LastEdit { get; private set; }
        public LinearOutputEquivalenceResult Comparison { get; private set; }
        public LeadScrewCompatibilityResult ConnectionQuery { get; private set; }
        public RotaryLinearFinalizationResult Finalization { get; private set; }
        public GearInvestLinearLeadScrewView View { get; private set; }
        public MechanicalDraft Source { get; private set; }
        public string Notice { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public bool IsPlaying { get; private set; }
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<RotaryLinearEditBatch> history = new List<RotaryLinearEditBatch>();
        private readonly List<RotaryLinearEditOperation> pending = new List<RotaryLinearEditOperation>();
        private readonly List<LinearOutputComparisonRequest> sessionComparisons = new List<LinearOutputComparisonRequest>();
        private bool comparisonEditingBlocked;
        private RotaryLinearDraft initial; private Font font; private Text readback, notice; private Camera sceneCamera;
        private double playStarted; private Rational playRoot; private int shaftIndex;
        public int PendingOperationCount => pending.Count;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";

        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("Linear EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            sceneCamera = Camera.main; if (sceneCamera == null) { var go = new GameObject("Linear camera"); go.tag = "MainCamera"; sceneCamera = go.AddComponent<Camera>(); }
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.035f, .055f, .085f); sceneCamera.orthographic = true;
            sceneCamera.rect = new Rect(700f / 1600, 520f / 1050, 900f / 1600, 530f / 1050);
            var ui = new GameObject("Linear UI camera").AddComponent<Camera>(); ui.transform.SetParent(transform, false); ui.cullingMask = 1 << 5; ui.clearFlags = CameraClearFlags.Depth; ui.depth = sceneCamera.depth + 10;
            ui.transform.position = new Vector3(0, 0, -1000); ui.nearClipPlane = .1f; ui.farClipPlane = 30;
            var canvasObject = new GameObject("Linear authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = ui; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Linear control panel", 0, 0, 700, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "DIMENSIONED LINEAR MOTION / GROUNDED SCREW", 12, 9, 675, 28, 19);
            Label(panel, "Exact turns + mm. Scoped ideal motion and closed axial intervals; NOT solid/dynamics approval.", 12, 40, 675, 27, 12);
            Field(panel, "file", "Source / draft / session file", "", 73);
            Button(panel, "example", "Public 20T -> 40T", 12, 110, 162, PublicSource);
            Button(panel, "load-source", "Load source", 182, 110, 162, LoadSource);
            Button(panel, "next-shaft", "Select next shaft", 352, 110, 162, NextShaft);
            Button(panel, "standalone", "Standalone screw", 522, 110, 164, StandaloneSource);
            Field(panel, "shaft", "Actual retained source shaft", "output", 151);
            Field(panel, "scale", "Explicit mm / source unit", "1", 183);
            Field(panel, "pose", "Source pose origin(mm);X;Y;Z", Identity, 215);
            Field(panel, "lead", "Positive lead (mm / screw turn)", "4", 247);
            Field(panel, "hand", "Material helix H (+1 / -1)", "1", 279);
            Field(panel, "reference", "Reference: screw turns,nut mm", "0,20", 311);
            Field(panel, "datum", "Fixed screw axial datum (mm)", "60,0,0", 343);
            Field(panel, "physical", "Physical helix axis n", "0,0,1", 375);
            Field(panel, "guide", "Guide origin(mm);X;Y;g", "60,0,0;1,0,0;0,1,0;0,0,1", 407);
            Field(panel, "stroke", "Guide low,high;engagement low,high mm", "18,26;18,26", 439);
            Field(panel, "requirements", "Target gain,root0 terminal;root low,high", "2,20;-1,3", 471);
            Field(panel, "terminal", "Terminal sign,datum(mm)", "1,0", 503);
            Field(panel, "operation", "Queue: Lead/Hand/Reference/Guide/Terminal/Requirements/Transmission/SourceRemove/SourceRestore/Binding/Mapping", "Lead", 535);
            Field(panel, "value", "Operation ID / transmission true,false", "", 567);
            Field(panel, "comparison", "Mode; mapped-after sign,datum(mm)", "FullLinearAffineOutput;1,0", 599);
            Field(panel, "root", "Absolute unwrapped root turns", "1/4", 631);
            Field(panel, "save", "New file path (no overwrite)", "", 663);
            Button(panel, "compose", "Compose / Analyze", 12, 704, 162, Compose);
            Button(panel, "queue", "Queue edit", 182, 704, 162, Queue);
            Button(panel, "apply", "Apply batch", 352, 704, 162, Apply);
            Button(panel, "reanalyze", "Reanalyze", 522, 704, 164, Analyze);
            Button(panel, "query", "Local query", 12, 744, 162, Query);
            Button(panel, "compare", "Compare", 182, 744, 162, Compare);
            Button(panel, "mark-before", "Mark before", 352, 744, 162, () => { NeedDraft(); Before = Draft; Comparison = null; AllowFormComparison(); Notice = "Marked immutable comparison reference. Intermediate-before comparisons are visible but are not persisted as initial-to-current history."; });
            Button(panel, "finalize", "Finalize", 522, 744, 164, FinalizeDraft);
            Button(panel, "evaluate", "Evaluate root", 12, 784, 162, Evaluate);
            Button(panel, "play", "Play / Pause", 182, 784, 162, PlayPause);
            Button(panel, "load-draft", "Load draft", 352, 784, 162, () => Adopt(sdk.ReadRotaryLinearDraft(ReadFile())));
            Button(panel, "load-session", "Load session", 522, 784, 164, LoadSession);
            Button(panel, "save-draft", "Save draft", 12, 824, 162, () => { NeedDraft(); sdk.SaveRotaryLinearDraft(Draft, Inputs["save"].text); Notice = "Saved current definition, including invalid/undriven drafts."; });
            Button(panel, "save-session", "Save history", 182, 824, 162, SaveSession);
            Button(panel, "reapply", "Reapply history", 352, 824, 162, Reapply);
            Button(panel, "rebuild", "Rebuild artifact", 522, 824, 164, Rebuild);
            Button(panel, "save-artifact", "Save final artifact", 12, 864, 205, SaveArtifact);
            Button(panel, "clear-queue", "Clear queued edits", 230, 864, 220, () => { pending.Clear(); Notice = "Pending edits cleared; current draft unchanged."; });
            Button(panel, "close", "Close current", 462, 864, 224, Close);
            notice = Label(panel, "Select a real source, declare its mm mapping and Compose.", 12, 907, 674, 131, 14);
            var viewport = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Exact readback", 710, 540, 878, 499, new Color(.065f, .085f, .115f));
            viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Rect(viewport, "Exact quantity results", 0, 0, 855, 485); scroll.content = content;
            readback = Label(content, "No current motion.", 7, 4, 845, 480, 14);
            View = new GameObject("Linear schematic view").AddComponent<GearInvestLinearLeadScrewView>(); View.transform.SetParent(transform, false);
            Notice = "Public input example or source file -> actual shaft -> explicit mapping -> Compose. No hidden linear driver.";
        }
        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Compose/load a mixed draft first."); }
        private byte[] ReadFile() { var p = Inputs["file"].text; if (new FileInfo(p).Length > RotaryLinearProfile.MaxDocumentBytes) throw new FormatException("Input byte bound exceeded."); return File.ReadAllBytes(p); }
        private void PublicSource()
        {
            ExampleFields(false);
            var kinematic = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") }, new[] { new ExternalGearCoupling("mesh", "input", "output", 20, 40) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", 0, 0), new SpatialAxis("output-axis", 60, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, 20, 20), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, 40, 40) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var generation = sdk.Generate(new LowLevelMechanicalSpecification("oriented-example-spur-20-40-0", kinematic, spatial));
            if (!generation.IsSuccess) throw new InvalidOperationException("Public source generation failed.");
            SetSource(sdk.ImportMechanicalArtifact(sdk.WriteArtifact(generation.Candidates.Single()).Bytes), "output");
        }
        private void StandaloneSource()
        {
            ExampleFields(true);
            SetSource(new MechanicalDraft(new MechanicalDefinition("screw", new[] { new OrientedShaft("screw", OrientedFrame.Identity, true) },
                Array.Empty<OrientedGearBody>(), Array.Empty<MechanicalContact>(), Array.Empty<ShaftPort>(), clearancePolicy: MechanicalAuthoringProfile.PlanarClearance)), "screw");
        }
        private void ExampleFields(bool standalone)
        {
            // Choosing an explicit authored example fills its visible input specification; edits never call this method.
            Inputs["scale"].text = "1"; Inputs["pose"].text = Identity; Inputs["lead"].text = "4"; Inputs["hand"].text = "1";
            Inputs["reference"].text = "0,20"; Inputs["terminal"].text = "1,0"; Inputs["root"].text = "1/4";
            Inputs["stroke"].text = standalone ? "0,40;0,40" : "18,26;18,26";
            Inputs["requirements"].text = standalone ? "-4,20;-5,5" : "2,20;-1,3";
        }
        private void LoadSource() { var source = sdk.ImportMechanicalArtifact(ReadFile()); SetSource(source, source.Definition.Outputs.First().ShaftId); }
        private void SetSource(MechanicalDraft source, string shaft)
        { Close(); Source = source; Inputs["shaft"].text = shaft; SetReferenceFromSelectedShaft(); Notice = "Source imported with all declared shafts/bodies/outputs. Parameter fields are explicit inputs; Compose remains required."; }
        private void NextShaft() { if (Source == null) throw new InvalidOperationException("Load a source first."); var shafts = Source.Definition.Shafts.ToArray(); shaftIndex = (shaftIndex + 1) % shafts.Length; Inputs["shaft"].text = shafts[shaftIndex].Id; SetReferenceFromSelectedShaft(); Notice = "Selected actual retained shaft and displayed its mapped mounting frame. Review references before Compose."; }
        private void SetReferenceFromSelectedShaft()
        {
            var mapping = new SourceLengthMapping(Rational.Parse(Inputs["scale"].text), Frame(Inputs["pose"].text));
            var f = mapping.FrameMm(Source.Definition.Shafts.Single(s => s.Id == Inputs["shaft"].text).Frame);
            Inputs["datum"].text = Point(f.Origin); Inputs["physical"].text = Point(f.Z); Inputs["guide"].text = FrameText(f);
        }
        private LinearOutputRequirement Requirement()
        { var parts = Parts(Inputs["requirements"].text, ';', 2); var q = Parts(parts[0], ',', 2); return new LinearOutputRequirement(q[0] == "none" ? (ExactQuantity?)null : ExactQuantity.MillimetersPerTurn(Rational.Parse(q[0])), q[1] == "none" ? (ExactQuantity?)null : ExactQuantity.Millimeters(Rational.Parse(q[1])), Interval(parts[1], true)); }
        private void Compose()
        {
            if (Source == null) throw new InvalidOperationException("Load/generate a real source first.");
            var refs = Parts(Inputs["reference"].text, ',', 2); var stroke = Parts(Inputs["stroke"].text, ';', 2); var terminal = Parts(Inputs["terminal"].text, ',', 2);
            var device = new GroundedLeadScrewDefinition("lead-screw", Inputs["shaft"].text, "lead-screw/body", "lead-screw/nut", "lead-screw/guide", "lead-screw/translation", Vector(Inputs["physical"].text), Vector(Inputs["datum"].text), Frame(Inputs["guide"].text),
                ExactQuantity.MillimetersPerTurn(Rational.Parse(Inputs["lead"].text)), ExactQuantity.Turns(Rational.Parse(refs[0])), ExactQuantity.Millimeters(Rational.Parse(refs[1])), Interval(stroke[0], false), Interval(stroke[1], false), int.Parse(Inputs["hand"].text, CultureInfo.InvariantCulture));
            Adopt(new RotaryLinearDraft(new RotaryLinearDefinition(Source, new SourceLengthMapping(Rational.Parse(Inputs["scale"].text), Frame(Inputs["pose"].text)), device,
                new LinearOutputDefinition("linear", device.LinearDofId, device.NutBodyId, "nut-reference", int.Parse(terminal[0], CultureInfo.InvariantCulture), ExactQuantity.Millimeters(Rational.Parse(terminal[1]))), Requirement())));
        }
        private void Adopt(RotaryLinearDraft draft) { Close(); initial = Before = Draft = draft; HydrateCurrentFields(); AllowFormComparison(); Analyze(); Notice = "Current mixed draft analyzed from declarations; editable fields show the adopted definition."; }
        private void HydrateCurrentFields()
        {
            // Explicit state adoption only. Ordinary Evaluate/Reanalyze never overwrite unsubmitted form edits.
            var definition = Draft.Definition; var d = definition.Device; var mapping = definition.SourceMapping;
            Source = definition.Source; Inputs["shaft"].text = d.ScrewShaftId;
            Inputs["scale"].text = mapping == null ? "" : mapping.MillimetersPerSourceUnit.ToString();
            Inputs["pose"].text = mapping == null ? "" : FrameText(mapping.PoseMm);
            Inputs["lead"].text = d.Lead.Value.ToString(); Inputs["hand"].text = d.Handedness.ToString(CultureInfo.InvariantCulture);
            Inputs["reference"].text = d.ScrewReferenceTurns.Value + "," + d.NutReferencePosition.Value;
            Inputs["datum"].text = Point(d.ScrewAxialDatumMm); Inputs["physical"].text = Point(d.PhysicalAxis);
            Inputs["guide"].text = FrameText(d.GuideFrameMm);
            Inputs["stroke"].text = IntervalText(d.GuideInterval) + ";" + IntervalText(d.EngagementInterval);
            var requirement = definition.Requirement;
            Inputs["requirements"].text = (requirement.RequiredGain.HasValue ? requirement.RequiredGain.Value.Value.ToString() : "none") + "," +
                (requirement.RequiredReferencePosition.HasValue ? requirement.RequiredReferencePosition.Value.Value.ToString() : "none") + ";" + IntervalText(requirement.RequiredRootInterval);
            Inputs["terminal"].text = definition.Output.TerminalSign.ToString(CultureInfo.InvariantCulture) + "," + definition.Output.TerminalDatum.Value;
            Inputs["value"].text = "";
        }
        private void Analyze() { NeedDraft(); Finalization = null; Comparison = null; ConnectionQuery = null; Analysis = sdk.AnalyzeRotaryLinearDraft(Draft); View.Build(Draft, Analysis); Evaluate(); Fit(); }
        private void Evaluate() { NeedDraft(); Evaluation = sdk.EvaluateRotaryLinearAnalysis(Analysis, ExactQuantity.Turns(Rational.Parse(Inputs["root"].text))); View.Apply(Evaluation); if (Evaluation.Status != RotaryLinearEvaluationStatus.Success) IsPlaying = false; Notice = "Evaluation: " + Evaluation.Status + "; absolute input preserved. Unknown/out-of-range hides successful nut pose, never clamps."; }
        private void PlayPause() { NeedDraft(); IsPlaying = !IsPlaying; if (IsPlaying) { playStarted = Time.realtimeSinceStartupAsDouble; playRoot = Rational.Parse(Inputs["root"].text); } Notice = IsPlaying ? "Caller clock running at +1 root turn / second; SDK receives absolute typed turns." : "Caller clock paused."; }
        private void Queue()
        {
            NeedDraft(); var d = Draft.Definition.Device; var id = string.IsNullOrEmpty(Inputs["value"].text) ? d.Id : Inputs["value"].text; RotaryLinearEditOperation edit;
            switch (Inputs["operation"].text)
            {
                case "Lead": edit = new SetLeadScrewLeadEdit(id, ExactQuantity.MillimetersPerTurn(Rational.Parse(Inputs["lead"].text))); break;
                case "Hand": edit = new SetLeadScrewHandednessEdit(id, int.Parse(Inputs["hand"].text, CultureInfo.InvariantCulture)); break;
                case "Reference": var refs = Parts(Inputs["reference"].text, ',', 2); edit = new SetLeadScrewReferenceEdit(id, ExactQuantity.Turns(Rational.Parse(refs[0])), ExactQuantity.Millimeters(Rational.Parse(refs[1])), Vector(Inputs["datum"].text)); break;
                case "Guide": var stroke = Parts(Inputs["stroke"].text, ';', 2); edit = new SetLeadScrewGuideEdit(id, Frame(Inputs["guide"].text), Interval(stroke[0], false), Interval(stroke[1], false)); break;
                case "Terminal": var t = Parts(Inputs["terminal"].text, ',', 2); edit = new SetLinearTerminalEdit(Draft.Definition.Output.Key, int.Parse(t[0], CultureInfo.InvariantCulture), ExactQuantity.Millimeters(Rational.Parse(t[1]))); break;
                case "Requirements": edit = new SetLinearRequirementsEdit(Draft.Definition.Output.Key, Requirement()); break;
                case "Transmission": if (Inputs["value"].text != "true" && Inputs["value"].text != "false") throw new FormatException("Explicit transmission true/false required."); edit = new SetLeadScrewTransmissionEdit(d.Id, Inputs["value"].text == "true"); break;
                case "Binding": edit = new SetLeadScrewBindingEdit(d.Id, Inputs["shaft"].text, null, Vector(Inputs["physical"].text)); break;
                case "Mapping": edit = new SetSourceLengthMappingEdit(new SourceLengthMapping(Rational.Parse(Inputs["scale"].text), Frame(Inputs["pose"].text))); break;
                case "SourceRemove": edit = SourceEdit(new RemoveContactEdit(Inputs["value"].text)); break;
                case "SourceRestore": edit = SourceEdit(new AddContactEdit(initial.Definition.Source.Definition.Contacts.Single(c => c.Id == Inputs["value"].text))); break;
                default: throw new FormatException("Choose a listed typed operation; arbitrary scripts are not accepted.");
            }
            if (pending.Count == 128) throw new FormatException("Pending operation bound exceeded."); pending.Add(edit); Notice = "Queued " + edit.Kind + "; " + pending.Count + " pending, no change yet.";
        }
        private RotaryLinearEditOperation SourceEdit(MechanicalEditOperation op)
        {
            // Only the SDK derives each nested expected source identity. This preview is immutable and is never adopted until Apply.
            var s = Draft.Definition.Source;
            foreach (var queued in pending.OfType<ApplyRotarySourceEditsEdit>())
            { var preview = sdk.ApplyMechanicalEdits(s, queued.Batch); if (preview.Draft == null) throw new InvalidOperationException("An earlier pending source request is rejected; clear/apply that batch first."); s = preview.Draft; }
            return new ApplyRotarySourceEditsEdit(new MechanicalEditBatch(s.Revision, s.DefinitionId, new[] { op }));
        }
        private void Apply()
        {
            NeedDraft(); var batch = new RotaryLinearEditBatch(Draft.Revision, Draft.DefinitionId, pending); LastEdit = sdk.ApplyRotaryLinearEdits(Draft, batch); history.Add(batch); pending.Clear(); Finalization = null;
            if (LastEdit.Draft != null) { Draft = LastEdit.Draft; Source = Draft.Definition.Source; Analyze(); }
            Notice = LastEdit.Status + "; revision=" + Draft.Revision + "; targets/range retained unless explicitly edited.";
        }
        private void Query() { NeedDraft(); ConnectionQuery = sdk.QueryLeadScrewCompatibility(Draft); Notice = "Local compatibility " + ConnectionQuery.Verdict + "; does not prove whole range/targets/source clearance."; }
        private void Compare()
        {
            NeedDraft(); if (comparisonEditingBlocked) throw new InvalidOperationException("The loaded request has an input/domain mapping this bounded form cannot edit. Use SDK/CLI or Mark before for a new form comparison.");
            var parts = Parts(Inputs["comparison"].text, ';', 2); var map = Parts(parts[1], ',', 2);
            if (!Enum.TryParse(parts[0], out LinearOutputComparisonMode mode)) throw new FormatException("Unknown linear comparison mode.");
            var request = new LinearOutputComparisonRequest(Before.Definition.Output.Key, Draft.Definition.Output.Key, Before.Definition.Source.Definition.RootShaftId, Draft.Definition.Source.Definition.RootShaftId,
                sign: int.Parse(map[0], CultureInfo.InvariantCulture), delta: ExactQuantity.Millimeters(Rational.Parse(map[1])), mode: mode,
                beforeReferencePointId: mode == LinearOutputComparisonMode.WorldLinearPath ? Before.Definition.Output.ReferencePointId : null,
                afterReferencePointId: mode == LinearOutputComparisonMode.WorldLinearPath ? Draft.Definition.Output.ReferencePointId : null);
            Comparison = sdk.CompareLinearOutputMotion(sdk.AnalyzeRotaryLinearDraft(Before), Analysis, request);
            var persistable = Before.DraftId == initial.DraftId;
            if (persistable && !sessionComparisons.Any(r => r.RequestId == request.RequestId))
            { if (sessionComparisons.Count == 64) throw new InvalidOperationException("Session comparison bound reached; the new comparison was not added to saved history."); sessionComparisons.Add(request); }
            Notice = "Scoped comparison " + Comparison.Verdict + "; operating domains equal=" + Comparison.OperatingDomainsEqual +
                (persistable ? "; initial-to-current request retained for Save history." : "; intermediate-before request NOT persisted as initial-to-current history.");
        }
        private void FinalizeDraft() { NeedDraft(); Finalization = sdk.TryFinalizeRotaryLinearDraft(Draft); Notice = "Finalize " + Finalization.Status + "; unsupported solid/dynamics domains remain NotPerformed."; }
        private void SaveArtifact() { NeedDraft(); if (Finalization == null || !Finalization.IsFinalized || Finalization.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Finalize this current draft first."); sdk.SaveRotaryLinearFinalization(Finalization, Inputs["save"].text); Notice = "Saved finalized mixed artifact with exact SDK readback."; }
        private void SaveSession() { NeedDraft(); sdk.SaveRotaryLinearEditSession(sdk.CreateRotaryLinearEditSession(initial, history, sessionComparisons), Inputs["save"].text); Notice = "Saved ordered portable history and " + sessionComparisons.Count + " explicit initial-to-current comparisons; fresh reader actually reapplies/reanalyzes."; }
        private void LoadSession()
        {
            var session = sdk.ReadRotaryLinearEditSession(ReadFile()); Adopt(session.InitialDraft); history.AddRange(session.Batches); sessionComparisons.AddRange(session.ComparisonRequests);
            Draft = session.CurrentDraft; HydrateCurrentFields(); Analyze(); HydrateComparisonFields();
            Notice = "Loaded current session; editable fields show current values, not initial defaults." + (comparisonEditingBlocked ? " Saved comparison input/domain mapping is shown read-only; use SDK/CLI or Mark before for a new request." : " Explicit saved comparison mapping retained.");
        }
        private void Reapply()
        {
            NeedDraft(); Draft = sdk.ReapplyRotaryLinearEditSession(sdk.CreateRotaryLinearEditSession(initial, history, sessionComparisons)); HydrateCurrentFields(); Analyze(); HydrateComparisonFields();
            Notice = "Fresh Reapply -> Reanalyze completed; editable fields show the reapplied current definition." + (comparisonEditingBlocked ? " Saved extended comparison mapping remains read-only." : "");
        }
        private void AllowFormComparison()
        { comparisonEditingBlocked = false; Inputs["comparison"].interactable = true; Buttons["compare"].interactable = true; Inputs["comparison"].text = "FullLinearAffineOutput;1,0"; }
        private void HydrateComparisonFields()
        {
            AllowFormComparison(); if (sessionComparisons.Count == 0) return;
            var request = sessionComparisons[sessionComparisons.Count - 1];
            // Stored comparisons explicitly mean initial -> current, never an intermediate Mark-before snapshot.
            Before = initial; Comparison = sdk.CompareLinearOutputMotion(sdk.AnalyzeRotaryLinearDraft(initial), Analysis, request);
            var worldPath = request.Mode == LinearOutputComparisonMode.WorldLinearPath;
            var representable = request.BeforeOutputKey == initial.Definition.Output.Key && request.AfterOutputKey == Draft.Definition.Output.Key &&
                request.BeforeInputId == initial.Definition.Source.Definition.RootShaftId && request.AfterInputId == Draft.Definition.Source.Definition.RootShaftId &&
                request.Alpha == ExactQuantity.TurnsPerTurn(1) && request.Beta == ExactQuantity.Turns(0) && request.Delta.Kind == QuantityKind.LinearPosition && request.ComparisonInterval == null &&
                (worldPath ? request.BeforeReferencePointId == initial.Definition.Output.ReferencePointId && request.AfterReferencePointId == Draft.Definition.Output.ReferencePointId : request.BeforeReferencePointId == null && request.AfterReferencePointId == null);
            Inputs["comparison"].text = representable ? request.Mode + ";" + request.Sign.ToString(CultureInfo.InvariantCulture) + "," + request.Delta.Value : "Saved extended mapping: read-only (see exact readback)";
            comparisonEditingBlocked = !representable; Inputs["comparison"].interactable = representable; Buttons["compare"].interactable = representable;
        }
        private void Rebuild()
        {
            var artifact = sdk.ReadRotaryLinearArtifact(ReadFile()); var rebuilt = sdk.RebuildRotaryLinearArtifact(artifact);
            var bytes = rebuilt.Bytes; var path = Inputs["save"].text;
            sdk.SaveRotaryLinearArtifact(rebuilt.Artifact, path);
            if (!File.ReadAllBytes(path).SequenceEqual(bytes)) throw new IOException("Rebuild save readback differs."); Notice = "Request-only fresh Rebuild saved; no cached motion result trusted.";
        }
        private void Close() { Draft = Before = initial = null; Source = null; Analysis = null; Evaluation = null; LastEdit = null; Comparison = null; ConnectionQuery = null; Finalization = null; history.Clear(); pending.Clear(); sessionComparisons.Clear(); comparisonEditingBlocked = false; IsPlaying = false; if (View != null) View.Clear(); Notice = "Closed. No prior pose is current."; }
        private void Update()
        {
            if (IsPlaying)
            {
                // Clock precision is a caller presentation choice. The SDK gets an exact absolute rational and does not integrate or wrap it.
                try { var micros = (long)((Time.realtimeSinceStartupAsDouble - playStarted) * 1000000); Inputs["root"].text = (playRoot + new Rational(micros, 1000000)).ToString(); Evaluate(); }
                catch (Exception e) { IsPlaying = false; Evaluation = null; View.HideCurrentMotion(); Notice = "CALLER INPUT ERROR: " + e.Message + "; no current successful pose."; }
            }
            notice.text = Notice;
            if (Draft == null) { readback.text = "No current mixed draft or motion. Source loaded=" + (Source != null); return; }
            var d = Draft.Definition.Device; var a = Analysis.LinearOutput;
            var text = "CURRENT " + Draft.DefinitionId + "  revision " + Draft.Revision + "\n" +
                "Source bodies=" + Draft.Definition.Source.Definition.Bodies.Count + "; separate screw + nut bodies=2. Quantity DOFs=" + string.Join(", ", Analysis.Nodes.Select(n => n.Id + ":" + n.Kind)) + "\n" +
                "Source bodies without a rotary channel are reference-pose projections, NOT zero-speed solutions.\n" +
                "Actual shaft=" + d.ScrewShaftId + " a=" + a.ScrewPositiveAxis + " | physical helix n=" + d.PhysicalAxis + " | guide g=" + d.GuideFrameMm.Z + "\n" +
                "Lead=" + d.Lead + "; H=" + d.Handedness + "; screw reference=" + d.ScrewReferenceTurns + "; nut reference=" + d.NutReferencePosition + "\n" +
                "Linear=" + a.Determinacy + "; gain=" + a.GuideRelation?.Gain + "; offset=" + a.GuideRelation?.Offset + "\n" +
                "Actual valid root closed interval=" + a.ValidRootInterval + "; required=" + Draft.Definition.Requirement.RequiredRootInterval + " -> " + a.RequiredRange + "; target=" + a.Target + "\n";
            if (Evaluation == null) text += "NO CURRENT EVALUATION. No previous successful frame represents the requested input.\n";
            if (Evaluation != null)
            {
                text += "REQUESTED ABSOLUTE ROOT " + Evaluation.Input + " -> " + Evaluation.Status + "\n";
                foreach (var r in Evaluation.Rotary) text += "ROTARY " + r.ShaftId + "=" + r.Turns + " turn; +axis=" + r.PositiveAxis + "\n";
                text += Evaluation.Linear == null ? "LINEAR CURRENT: UNKNOWN / NO SUCCESSFUL POSE. Previous pose is not displayed.\n" :
                    "SCREW=" + Evaluation.Linear.ScrewTurns + "; GUIDE x=" + Evaluation.Linear.GuidePosition + "; TERMINAL y=" + Evaluation.Linear.TerminalPosition + "\nWORLD nut mm=" + Evaluation.Linear.WorldPositionMm + "; guide gain=" + Evaluation.Linear.GuideGain + "; terminal gain=" + Evaluation.Linear.TerminalGain + "\n";
                if (Evaluation.DiagnosticLinear != null) text += "DIAGNOSTIC ONLY, UNCLAMPED position=" + Evaluation.DiagnosticLinear.GuidePosition + " (not successful motion)\n";
            }
            if (LastEdit != null) text += "EDIT " + LastEdit.Status + "; failed index=" + LastEdit.FailingOperationIndex + "; pending=" + pending.Count + "\n";
            if (Comparison != null) text += "COMPARE " + Comparison.Verdict + " / " + Comparison.Request.Mode + "; same operating domains=" + Comparison.OperatingDomainsEqual + "\n" +
                "Request alpha=" + Comparison.Request.Alpha + "; beta=" + Comparison.Request.Beta + "; sign=" + Comparison.Request.Sign + "; datum=" + Comparison.Request.Delta + "; comparison interval=" + Comparison.Request.ComparisonInterval +
                (comparisonEditingBlocked ? " [read-only: not representable by this form]" : "") + "\n";
            text += "Persisted comparison requests=" + sessionComparisons.Count + " (explicit initial-to-current only).\n";
            if (ConnectionQuery != null) text += "LOCAL " + ConnectionQuery.Verdict + "; source-port sign is evidence only=" + ConnectionQuery.SourcePortCoordinateSign + "\n";
            if (Finalization != null) text += "FINALIZE " + Finalization.Status + "\n";
            foreach (var c in Analysis.Checks) text += c.Domain + ": " + c.Verdict + (c.Required ? " [required]" : "") + "\n";
            text += string.Join("\n", Analysis.Diagnostics.Concat(LastEdit == null ? Enumerable.Empty<MechanicalDiagnostic>() : LastEdit.Diagnostics).Select(x => x.Stage + "/" + x.Code + ": " + string.Join(",", x.Related.Select(r => r.Key))));
            text += "\nScrew ribs, nut size and rails are schematic. Added-body/swept/thread/force/friction/dynamics: NotPerformed.";
            readback.text = text; var height = Mathf.Max(485, readback.preferredHeight + 14); readback.rectTransform.sizeDelta = new Vector2(845, height); ((RectTransform)readback.transform.parent).sizeDelta = new Vector2(855, height + 10);
        }
        private void Fit() { var center = View.ViewCenter; var radius = View.ViewRadius; sceneCamera.transform.position = center + new Vector3(1.1f, .85f, 1.5f).normalized * radius * 3; sceneCamera.transform.LookAt(center); sceneCamera.orthographicSize = radius; sceneCamera.nearClipPlane = .1f; sceneCamera.farClipPlane = radius * 8 + 100; }
        private static string[] Parts(string s, char separator, int n) { var p = s.Split(separator); if (p.Length != n) throw new FormatException("Expected " + n + " exact fields."); return p; }
        private static ExactVector3 Vector(string s) { var p = Parts(s, ',', 3).Select(Rational.Parse).ToArray(); return new ExactVector3(p[0], p[1], p[2]); }
        private static OrientedFrame Frame(string s) { var p = Parts(s, ';', 4).Select(Vector).ToArray(); return new OrientedFrame(p[0], p[1], p[2], p[3]); }
        private static ExactQuantityInterval Interval(string s, bool turns) { var p = Parts(s, ',', 2).Select(Rational.Parse).ToArray(); return turns ? ExactQuantityInterval.Turns(p[0], p[1]) : ExactQuantityInterval.Millimeters(p[0], p[1]); }
        private static string Point(ExactVector3 v) => v.X + "," + v.Y + "," + v.Z;
        private static string FrameText(OrientedFrame f) => Point(f.Origin) + ";" + Point(f.X) + ";" + Point(f.Y) + ";" + Point(f.Z);
        private static string IntervalText(ExactQuantityInterval interval) => interval.Lower.Value + "," + interval.Upper.Value;
        private Text Label(Transform p, string t, float x, float y, float w, float h, int size) => GearInvestAuthoringWidgets.Label(font, p, t, x, y, w, h, size, new Color(.9f, .94f, 1));
        private void Field(Transform p, string key, string title, string value, float y) => Inputs.Add(key, GearInvestAuthoringWidgets.Field(font, p, key, title, value, y));
        private void Button(Transform p, string key, string title, float x, float y, float width, Action action)
        { Buttons.Add(key, GearInvestAuthoringWidgets.Button(font, p, key, title, x, y, width, () => { try { action(); } catch (Exception e) { IsPlaying = false; Finalization = null; if (key == "evaluate" || key == "play") { Evaluation = null; View.HideCurrentMotion(); } Notice = "INPUT/OPERATION ERROR: " + e.Message; } })); }
    }
}
