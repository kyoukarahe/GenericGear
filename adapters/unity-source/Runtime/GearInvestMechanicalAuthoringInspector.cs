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
    /// <summary>Optional bounded edit form. Transactions, graph analysis, equivalence and finalization are SDK operations.</summary>
    public sealed class GearInvestMechanicalAuthoringInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public MechanicalDraft Draft { get; private set; }
        public MechanicalDraft Before { get; private set; }
        public MechanicalAnalysis Analysis { get; private set; }
        public MechanicalEditResult LastEdit { get; private set; }
        public OutputEquivalenceResult Comparison { get; private set; }
        public ConnectionCompatibilityResult ConnectionQuery { get; private set; }
        public MechanicalFinalizationResult Finalization { get; private set; }
        public GearInvestOrientedMechanismView View { get; private set; }
        public GearInvestPitchPairView PairView { get; private set; }
        public string Notice { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public int PendingOperationCount => pending.Count;
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<MechanicalEditOperation> pending = new List<MechanicalEditOperation>();
        private readonly List<MechanicalEditBatch> history = new List<MechanicalEditBatch>();
        private MechanicalDraft initial;
        private Font font; private Text readback, notice, queueText; private Camera sceneCamera;
        private int selectionIndex = -1, diagnosticIndex = -1, pairIndex = -1;
        private const string IdentityFrame = "0,0,0;1,0,0;0,1,0;0,0,1";

        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("Authoring EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            sceneCamera = Camera.main;
            if (sceneCamera == null) { var go = new GameObject("Mechanical draft camera"); go.tag = "MainCamera"; sceneCamera = go.AddComponent<Camera>(); }
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor;
            sceneCamera.backgroundColor = new Color(.045f, .065f, .1f); sceneCamera.orthographic = true;
            sceneCamera.rect = new Rect(640f / 1600, 470f / 1050, 960f / 1600, 580f / 1050);
            var ui = new GameObject("Mechanical authoring UI camera").AddComponent<Camera>(); ui.transform.SetParent(transform, false);
            ui.cullingMask = 1 << 5; ui.clearFlags = CameraClearFlags.Depth; ui.depth = sceneCamera.depth + 10;
            ui.transform.position = new Vector3(0, 0, -1000); ui.nearClipPlane = .1f; ui.farClipPlane = 30;
            var canvasObject = new GameObject("Mechanical authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = ui; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Authoring panel", 0, 0, 640, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "MECHANICAL AUTHORING / EXACT ANALYSIS", 12, 8, 610, 28, 20);
            Label(panel, "Edit applied != valid mechanism. Drafts may remain disconnected or invalid.", 12, 38, 610, 30, 13);
            Field(panel, "file", "Input artifact / draft / session path", "", 78);
            Button(panel, "load-artifact", "Load artifact", 12, 112, 145, LoadArtifact);
            Button(panel, "load-draft", "Load draft", 167, 112, 145, LoadDraft);
            Button(panel, "load-session", "Load session", 322, 112, 145, LoadSession);
            Button(panel, "mark-before", "Mark before", 477, 112, 145, () => { NeedDraft(); Before = Draft; Comparison = null; Notice = "Comparison reference is current immutable draft."; });
            Field(panel, "kind", "Selection: Body / Contact / Shaft / Output", "Contact", 158);
            Field(panel, "object", "Selected stable ID", "", 194);
            Button(panel, "next-object", "Select next", 12, 230, 145, NextObject);
            Button(panel, "query", "Local query", 167, 230, 145, Query);
            Button(panel, "restore-contact", "Restore contact", 322, 230, 145, RestoreContact);
            Button(panel, "highlight", "Highlight IDs", 477, 230, 145, HighlightSelection);
            Field(panel, "operation", "Operation (listed below)", "RemoveContact", 275);
            Field(panel, "arg1", "Argument 1 / teeth / target / endpoint A", "", 311);
            Field(panel, "arg2", "Argument 2 / endpoint B", "", 347);
            Field(panel, "frame", "Exact frame origin;X;Y;Z", IdentityFrame, 383);
            Label(panel, "RemoveContact, RemoveBody, SetTeeth, RebindContact, BodyFrame, MoveShaft,\nTerminalFrame, Target, AddKeepOut, RemoveKeepOut, Clearance.\nRemoveBody: arg1=cascade is explicit; SetTeeth preserves radius per tooth.\nAddKeepOut: arg1=min x,y,z; arg2=max. Clearance: legacy/refined/planar, 0/1.", 12, 422, 608, 83, 12);
            Button(panel, "queue", "Queue edit", 12, 513, 145, QueueOperation);
            Button(panel, "clear-queue", "Clear queue", 167, 513, 145, () => { pending.Clear(); Notice = "Pending commands cleared; current draft unchanged."; });
            Button(panel, "apply", "Apply batch", 322, 513, 145, ApplyBatch);
            Button(panel, "reanalyze", "Reanalyze", 477, 513, 145, Analyze);
            queueText = Label(panel, "0 pending operations", 12, 555, 608, 43, 13);
            Field(panel, "before-output", "Before output key", "A", 608);
            Field(panel, "after-output", "After output key", "A", 644);
            Field(panel, "mapping", "Caller mapping alpha,beta,s,delta", "1,0,1,0", 680);
            Button(panel, "compare", "Compare outputs", 12, 716, 195, Compare);
            Button(panel, "next-cause", "Next cause / path", 217, 716, 195, NextCause);
            Button(panel, "next-pair", "Next pitch proof", 422, 716, 200, NextPair);
            Field(panel, "root", "Exact input turns (known channels only)", "1/4", 760);
            Field(panel, "profile", "Explicit export profile", OrientedTwoOutputProfile.RefinedId, 796);
            Button(panel, "evaluate", "Evaluate", 12, 832, 145, () => { NeedDraft(); View.gameObject.SetActive(true); PairView.Clear(); View.Apply(Rational.Parse(Inputs["root"].text)); Fit(); });
            Button(panel, "finalize", "Finalize", 167, 832, 145, FinalizeDraft);
            Button(panel, "reapply", "Reapply history", 322, 832, 145, Reapply);
            Button(panel, "close", "Close", 477, 832, 145, Close);
            Field(panel, "save", "New output file (never overwrite)", "", 876);
            Button(panel, "save-draft", "Save draft", 12, 912, 195, () => { NeedDraft(); sdk.SaveMechanicalDraft(Draft, Inputs["save"].text); Notice = "Saved current draft; exact byte readback verified by SDK."; });
            Button(panel, "save-session", "Save session", 217, 912, 195, SaveSession);
            Button(panel, "save-artifact", "Save artifact", 422, 912, 200, SaveArtifact);
            notice = Label(panel, "Load a supported original artifact to start.", 12, 954, 610, 85, 14);
            var viewport = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Analysis readback", 650, 590, 938, 449, new Color(.06f, .08f, .11f));
            viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>();
            scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Rect(viewport, "Exact results", 0, 0, 915, 440); scroll.content = content;
            readback = Label(content, "No draft loaded. No motion inferred.", 8, 6, 899, 428, 14);
            View = new GameObject("Current draft geometry").AddComponent<GearInvestOrientedMechanismView>(); View.transform.SetParent(transform, false);
            PairView = new GameObject("Current exact pitch pair").AddComponent<GearInvestPitchPairView>(); PairView.transform.SetParent(transform, false);
            Notice = "Load a supported original artifact. All computations run in the shared SDK.";
        }

        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Load a draft first."); }
        private byte[] ReadInput()
        {
            var path = Inputs["file"].text; if (new FileInfo(path).Length > MechanicalAuthoringProfile.MaxDocumentBytes) throw new FormatException("Input byte bound exceeded.");
            return File.ReadAllBytes(path);
        }
        private void Adopt(MechanicalDraft draft)
        {
            Close(); Draft = Before = initial = draft; Inputs["profile"].text = draft.ImportedProfile ?? MechanicalAuthoringProfile.PlanarExport;
            Analyze(); Notice = "Loaded immutable draft; original certificates are historical only.";
        }
        private void LoadArtifact() => Adopt(sdk.ImportMechanicalArtifact(ReadInput()));
        private void LoadDraft() => Adopt(sdk.ReadMechanicalDraft(ReadInput()));
        private void LoadSession()
        {
            var session = sdk.ReadMechanicalEditSession(ReadInput());
            Adopt(session.InitialDraft); history.AddRange(session.Batches); Draft = session.CurrentDraft; Analyze(); Notice = "Loaded session; actual ordered reapply and analysis verified by SDK.";
        }
        private void Close()
        {
            Draft = Before = initial = null; Analysis = null; LastEdit = null; Comparison = null; ConnectionQuery = null; Finalization = null;
            pending.Clear(); history.Clear(); View.Clear(); PairView.Clear(); selectionIndex = diagnosticIndex = pairIndex = -1;
            Notice = "Closed. No previous solution or geometry remains current.";
        }
        private void Analyze()
        {
            NeedDraft(); Finalization = null; Comparison = null; ConnectionQuery = null; PairView.Clear();
            Analysis = sdk.AnalyzeMechanicalDraft(Draft); View.gameObject.SetActive(true);
            var current = Analysis; View.BuildDraft(Draft.Definition, u => sdk.EvaluateMechanicalAnalysis(current, u));
            View.Apply(Rational.Parse(Inputs["root"].text)); Fit(); Notice = "Current definition reanalyzed; no cached solution reused.";
        }
        private void NextObject()
        {
            NeedDraft(); var d = Draft.Definition; IEnumerable<string> ids;
            switch (Inputs["kind"].text) { case "Body": ids = d.Bodies.Select(b => b.Id); break; case "Contact": ids = d.Contacts.Select(c => c.Id); break; case "Shaft": ids = d.Shafts.Select(s => s.Id); break; case "Output": ids = d.Outputs.Select(o => o.Key); break; default: throw new FormatException("Choose Body, Contact, Shaft or Output."); }
            var values = ids.ToArray(); if (values.Length == 0) throw new FormatException("No objects of this kind."); selectionIndex = (selectionIndex + 1) % values.Length; Inputs["object"].text = values[selectionIndex]; HighlightSelection();
        }
        private void HighlightSelection()
        {
            NeedDraft(); var kind = Inputs["kind"].text; var id = Inputs["object"].text;
            var exists = kind == "Body" ? Draft.Definition.Bodies.Any(b => b.Id == id) : kind == "Shaft" ? Draft.Definition.Shafts.Any(s => s.Id == id) :
                kind == "Contact" ? Draft.Definition.Contacts.Any(c => c.Id == id) : kind == "Output" && Draft.Definition.Outputs.Any(o => o.Key == id);
            if (!exists) throw new FormatException("Select a current typed object ID.");
            PairView.Clear(); View.gameObject.SetActive(true); View.Highlight(new[] { new MechanicalReference(kind, id) }); Fit();
            Notice = "Selected " + kind + "/" + id + "; related current geometry highlighted. Selection is not validation.";
        }
        private void QueueOperation()
        {
            NeedDraft(); if (pending.Count == MechanicalAuthoringProfile.MaxOperations) throw new FormatException("Operation bound reached.");
            var id = Inputs["object"].text; var a = Inputs["arg1"].text; var b = Inputs["arg2"].text; MechanicalEditOperation operation;
            switch (Inputs["operation"].text)
            {
                case "RemoveContact": operation = new RemoveContactEdit(id); break;
                case "RemoveBody": if (a != "" && a != "cascade") throw new FormatException("Removal arg1 is blank (reject dependencies) or cascade."); operation = new RemoveBodyEdit(id, a == "cascade" ? MechanicalRemovalPolicy.RemoveIncidentContactsAndUnresolveOutputs : MechanicalRemovalPolicy.RejectDependencies); break;
                case "SetTeeth": operation = new SetToothCountEdit(id, int.Parse(a, CultureInfo.InvariantCulture), ToothDimensionPolicy.PreservePitchRadiusPerTooth); break;
                case "RebindContact": operation = new RebindContactEdit(id, a, b); break;
                case "BodyFrame": operation = new SetBodyMountingEdit(id, Frame(Inputs["frame"].text)); break;
                case "MoveShaft": operation = new MoveShaftGroupEdit(id, Frame(Inputs["frame"].text)); break;
                case "TerminalFrame": operation = new SetOutputTerminalEdit(id, Frame(Inputs["frame"].text)); break;
                case "Target": operation = new SetRequestedTransferEdit(id, a == "none" ? (Rational?)null : Rational.Parse(a)); break;
                case "AddKeepOut": operation = new AddKeepOutEdit(new OrientedKeepOut(id, new ExactEnvelope3(Point(a), Point(b)))); break;
                case "RemoveKeepOut": operation = new RemoveKeepOutEdit(id); break;
                case "Clearance": if (b != "0" && b != "1") throw new FormatException("Clearance required flag is explicit 0/1."); operation = new SetClearancePolicyEdit(a == "refined" ? PitchClearancePolicy.Refined : a == "legacy" ? PitchClearancePolicy.Legacy : a == "planar" ? MechanicalAuthoringProfile.PlanarClearance : throw new FormatException("Unknown policy."), b == "1"); break;
                default: throw new FormatException("Unsupported form operation. General typed SDK operations are available through the CLI.");
            }
            pending.Add(operation); Notice = "Queued " + operation.Kind + "; not applied yet.";
        }
        private void RestoreContact()
        {
            NeedDraft(); if (Before == null) throw new InvalidOperationException("No comparison reference.");
            var contact = Before.Definition.Contacts.SingleOrDefault(c => c.Id == Inputs["object"].text);
            if (contact == null) throw new FormatException("Reference draft has no selected contact.");
            pending.Add(new AddContactEdit(contact)); Notice = "Queued original selected contact definition; Apply remains explicit.";
        }
        private void ApplyBatch()
        {
            NeedDraft(); var batch = new MechanicalEditBatch(Draft.Revision, Draft.DefinitionId, pending);
            Finalization = null; Comparison = null; ConnectionQuery = null; LastEdit = sdk.ApplyMechanicalEdits(Draft, batch); history.Add(batch); pending.Clear();
            if (LastEdit.Status == MechanicalEditStatus.Applied) { Draft = LastEdit.Draft; Analyze(); }
            Notice = LastEdit.Status + "; revision=" + Draft.Revision + "; mechanical export=" + Analysis.ExportAdmission + ". Edit success is separate.";
        }
        private void Query()
        {
            NeedDraft(); var id = Inputs["object"].text;
            var contact = Draft.Definition.Contacts.SingleOrDefault(c => c.Id == id);
            if (contact == null) throw new FormatException("Select a current contact for local geometry query.");
            ConnectionQuery = sdk.QueryConnectionCompatibility(Draft, contact); Notice = ConnectionQuery.Verdict + " / LocalConnectionOnly; not whole validity.";
        }
        private void Compare()
        {
            NeedDraft(); if (Before == null) throw new FormatException("Mark a before reference first.");
            var mapping = Parts(Inputs["mapping"].text, ',', 4).Select(Rational.Parse).ToArray();
            var request = new OutputComparisonRequest(Inputs["before-output"].text, Inputs["after-output"].text,
                Before.Definition.RootShaftId ?? "unresolved-input", Draft.Definition.RootShaftId ?? "unresolved-input", mapping[0], mapping[1], mapping[2], mapping[3]);
            Comparison = sdk.CompareOutputMotion(sdk.AnalyzeMechanicalDraft(Before), Analysis, request);
            Notice = Comparison.Verdict + " / " + Comparison.Scope + "; current geometry=" + Analysis.Geometry + ". Not whole mechanism approval.";
        }
        private void FinalizeDraft()
        {
            NeedDraft(); Finalization = sdk.TryFinalizeMechanicalDraft(Draft, Inputs["profile"].text);
            Notice = "Finalize: " + Finalization.Status + (Finalization.IsFinalized ? "; validated current artifact ready to Save." : "; draft preserved, no successful artifact.");
        }
        private void Reapply()
        {
            NeedDraft(); var session = sdk.CreateMechanicalEditSession(initial, history);
            Draft = sdk.ReapplyMechanicalEditSession(session); Analyze(); Notice = "Fresh Apply chain reproduced current definition; reanalyzed.";
        }
        private void SaveSession()
        {
            NeedDraft(); var comparisons = Comparison != null && Before.DraftId == initial.DraftId ? new[] { Comparison.Request } : Array.Empty<OutputComparisonRequest>();
            sdk.SaveMechanicalEditSession(sdk.CreateMechanicalEditSession(initial, history, comparisons), Inputs["save"].text);
            Notice = "Saved session with ordered history and " + comparisons.Length + " initial-to-current comparisons; exact readback verified.";
        }
        private void SaveArtifact()
        {
            NeedDraft(); if (Finalization == null || !Finalization.IsFinalized || Finalization.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Finalize current definition first.");
            sdk.SaveMechanicalFinalization(Finalization, Inputs["save"].text); Notice = "Saved finalized artifact through SDK; exact readback verified.";
        }
        private void NextCause()
        {
            NeedDraft(); var causes = DisplayDiagnostics().ToArray();
            if (causes.Length == 0) { Notice = "No current diagnostic cause."; return; }
            diagnosticIndex = (diagnosticIndex + 1) % causes.Length; var d = causes[diagnosticIndex];
            PairView.Clear(); View.gameObject.SetActive(true); View.Highlight(d.Related); Fit();
            Notice = d.Code + ": " + string.Join(", ", d.Related.Select(r => r.Key)) + "; paths " + string.Join(" | ", d.Paths.Select(p => string.Join(" > ", p.ConstraintIds)));
        }
        private IEnumerable<MechanicalDiagnostic> DisplayDiagnostics()
        {
            // Presentation of SDK evidence only. Never reconstruct graph paths or infer a verdict here.
            var values = Analysis.Diagnostics.AsEnumerable();
            if (LastEdit != null) values = LastEdit.Diagnostics.Concat(values);
            if (ConnectionQuery != null) values = values.Concat(ConnectionQuery.Diagnostics);
            if (Comparison != null) values = values.Concat(Comparison.Diagnostics);
            if (Finalization != null) values = values.Concat(Finalization.Diagnostics);
            return values.Distinct().OrderBy(d => d.Severity == DiagnosticSeverity.Error ? 0 : 1);
        }
        private static string DiagnosticText(MechanicalDiagnostic d)
        {
            var text = d.Stage + "/" + d.Code + " [" + string.Join(",", d.Related.Select(r => r.Key)) + "] " + d.Severity + "; scope=" + d.Scope;
            if (d.Facts.Count != 0) text += "\n  " + string.Join("; ", d.Facts.Select(f => f.Key + " expected=" + (f.Expected?.ToString() ?? "n/a") + " actual=" + (f.Actual?.ToString() ?? "n/a")));
            foreach (var p in d.Paths) text += "\n  path " + p.RootId + " -> " + p.TargetId + ": q=" + p.Coefficient + " p=" + p.Phase + " via " + string.Join(" > ", p.ConstraintIds);
            if (d.AffectedOutputs.Count != 0) text += "\n  affected outputs: " + string.Join(",", d.AffectedOutputs);
            if (d.BlockedPrerequisites.Count != 0) text += "\n  blocked: " + string.Join(",", d.BlockedPrerequisites);
            if (d.ProofReference != null) text += "\n  proof: " + d.ProofReference;
            return text;
        }
        private void NextPair()
        {
            NeedDraft(); var pairs = Analysis.PitchProofs.OrderBy(p => p.Verdict == OrientedCheckVerdict.Pass ? 1 : 0).ThenBy(p => p.PairId, StringComparer.Ordinal).ToArray();
            if (pairs.Length == 0) throw new FormatException("No pitch proof was performed for this definition.");
            pairIndex = (pairIndex + 1) % pairs.Length; var p = pairs[pairIndex];
            View.gameObject.SetActive(false); PairView.Build(p); Fit(); Notice = p.Verdict + "; policy=" + Draft.Definition.ClearancePolicy + "; proof=" + p.Digest;
        }
        private void Update()
        {
            notice.text = Notice; queueText.text = pending.Count + " pending: " + string.Join(", ", pending.Select(o => o.Kind));
            if (Draft == null) { readback.text = "No current draft. No motion inferred."; return; }
            var a = Analysis; var text = "CURRENT DRAFT " + Draft.DefinitionId + "  revision " + Draft.Revision + "\n" +
                "Reference " + a.ReferenceIntegrity + " | declared " + a.DeclaredConnectivity + " | admitted " + a.ConstraintAdmission + " | geometry " + a.Geometry + " | targets " + a.Targets + "\n" +
                "Bodies " + Draft.Definition.Bodies.Count + " / shafts " + Draft.Definition.Shafts.Count + " / contacts " + Draft.Definition.Contacts.Count + ". Reference-pose bodies with unknown motion are NOT zero-speed solutions.\n";
            foreach (var o in a.Outputs) text += o.OutputKey + ": " + o.Determinacy + (o.HasDeterminedMotion ? " shaft q=" + o.ShaftRelation.Value.Coefficient + " port q=" + o.PortRelation.Value.Coefficient + " p=" + o.PortRelation.Value.Phase : " [UNKNOWN motion / reference pose only]") + "; requested=" + o.Binding.RequiredTransfer + "\n";
            if (LastEdit != null) text += "EDIT " + LastEdit.Status + (LastEdit.FailingOperationIndex.HasValue ? " | failing operation index=" + LastEdit.FailingOperationIndex.Value + " (zero-based)" : "") + " | net +" + LastEdit.NetChanges.Added.Count + " -" + LastEdit.NetChanges.Removed.Count + " ~" + LastEdit.NetChanges.Modified.Count + "\n" + string.Join("\n", LastEdit.Changes.Select(c => c.OperationIndex + ": " + c.Action + " " + c.Subject.Key + (c.Dependent ? " [dependent]" : ""))) + "\n";
            if (Comparison != null) text += "COMPARE " + Comparison.Request.BeforeOutputKey + " -> " + Comparison.Request.AfterOutputKey + ": " + Comparison.Verdict + " / " + Comparison.Scope + "\n";
            if (Finalization != null) text += "FINALIZE " + Finalization.Status + " " + Finalization.ArtifactIdentity + "\n";
            if (ConnectionQuery != null) text += "LOCAL " + ConnectionQuery.Verdict + "; q=" + ConnectionQuery.SignedTransfer + "; not global validity\n" +
                string.Join("; ", ConnectionQuery.Checks.Select(c => c.Domain + "/" + c.Subject + "=" + c.Verdict)) + "\n" +
                string.Join("; ", ConnectionQuery.Facts.Select(f => f.Key + " expected=" + (f.Expected?.ToString() ?? "n/a") + " actual=" + (f.Actual?.ToString() ?? "n/a"))) + "\n";
            text += string.Join("\n", DisplayDiagnostics().Select(DiagnosticText));
            text += "\nDisplay only: approximate pitch surfaces. Tooth solids, bearings, dynamics: NotPerformed.";
            readback.text = text; var h = Mathf.Max(435, readback.preferredHeight + 18); readback.rectTransform.sizeDelta = new Vector2(899, h); ((RectTransform)readback.transform.parent).sizeDelta = new Vector2(915, h + 12);
        }
        private void Fit()
        {
            var center = PairView.HasPair ? PairView.ViewCenter : View.ViewCenter; var radius = PairView.HasPair ? PairView.ViewRadius : View.ViewRadius;
            sceneCamera.transform.position = center + new Vector3(1.6f, 1.3f, 1.8f).normalized * radius * 3; sceneCamera.transform.LookAt(center);
            sceneCamera.orthographicSize = Mathf.Max(10, radius); sceneCamera.nearClipPlane = .1f; sceneCamera.farClipPlane = radius * 8 + 100;
        }
        private static string[] Parts(string s, char separator, int count) { var v = s.Split(separator); if (v.Length != count) throw new FormatException("Expected " + count + " exact fields."); return v; }
        private static ExactVector3 Point(string s) { var v = Parts(s, ',', 3).Select(Rational.Parse).ToArray(); return new ExactVector3(v[0], v[1], v[2]); }
        private static OrientedFrame Frame(string s) { var v = Parts(s, ';', 4).Select(Point).ToArray(); return new OrientedFrame(v[0], v[1], v[2], v[3]); }
        private Text Label(Transform parent, string text, float x, float y, float w, float h, int size) => GearInvestAuthoringWidgets.Label(font, parent, text, x, y, w, h, size, new Color(.9f, .94f, 1));
        private void Field(Transform parent, string key, string title, string value, float y)
        {
            Label(parent, title, 12, y, 280, 28, 12); var box = GearInvestAuthoringWidgets.Box(parent, "input-" + key, 297, y, 325, 30, new Color(.14f, .17f, .22f));
            var field = box.gameObject.AddComponent<InputField>(); field.textComponent = Label(box, "", 5, 1, 315, 28, 13); field.characterLimit = 8192; field.text = value; Inputs.Add(key, field);
        }
        private void Button(Transform parent, string key, string title, float x, float y, float width, Action action)
        {
            var box = GearInvestAuthoringWidgets.Box(parent, "button-" + key, x, y, width, 32, new Color(.14f, .28f, .4f));
            var button = box.gameObject.AddComponent<Button>(); button.targetGraphic = box.GetComponent<Image>(); Label(box, title, 4, 4, width - 8, 26, 13);
            button.onClick.AddListener(() => { try { action(); } catch (Exception e) { Finalization = null; Notice = "INPUT/OPERATION ERROR: " + e.Message; if (!(e is ArgumentException || e is FormatException || e is InvalidOperationException || e is IOException || e is ArithmeticException)) Debug.LogException(e); } });
            Buttons.Add(key, button);
        }
    }
}
