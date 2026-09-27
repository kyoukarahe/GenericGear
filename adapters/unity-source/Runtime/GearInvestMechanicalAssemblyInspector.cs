using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GearInvest.Unity
{
    /// <summary>Generic, editable single-root assembly consumer. All mechanics and transactions are SDK operations.</summary>
    public sealed class GearInvestMechanicalAssemblyInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> MemberButtons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public readonly Dictionary<AssemblyComponentReference, Button> ReferenceButtons = new Dictionary<AssemblyComponentReference, Button>();
        public readonly Dictionary<string, int> ActionCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        public event Action<string> ActionCompleted;
        public MechanicalAssemblyDraft Draft { get; private set; }
        public MechanicalAssemblyDraft Before { get; private set; }
        public MechanicalAssemblyAnalysis BeforeAnalysis { get; private set; }
        public MechanicalAssemblyAnalysis Analysis { get; private set; }
        public MechanicalAssemblyEvaluation Evaluation { get; private set; }
        public MechanicalAssemblyEditResult LastEdit { get; private set; }
        public AssemblyBindingAnalysis ConnectionQuery { get; private set; }
        public MechanicalAssemblyOutputComparisonResult Comparison { get; private set; }
        public MechanicalAssemblyFinalizationResult Finalization { get; private set; }
        public MechanicalAssemblyEditSession LastSession { get; private set; }
        public MechanicalAssemblyArtifactWriteResult Rebuilt { get; private set; }
        public GearInvestMechanicalAssemblyView View { get; private set; }
        public Button RootButton { get; private set; }
        public Camera SceneCamera { get; private set; }
        public string SelectedMemberId { get; private set; }
        public AssemblyComponentReference SelectedReference { get; private set; }
        public string Notice
        {
            get => noticeValue;
            private set { noticeValue = value; if (notice != null) notice.text = value; }
        }
        public string NoticeReadback => notice == null ? "" : notice.text;
        public string DisplayUnavailableReason { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public string ExactEvaluationJson => Evaluation == null ? "" : Encoding.UTF8.GetString(sdk.WriteMechanicalAssemblyEvaluation(Evaluation));
        public ExactQuantity? CurrentRoot => Evaluation == null ? (ExactQuantity?)null : Evaluation.RootInput;
        public bool IsPlaying { get; private set; }
        public int PendingOperationCount => pending.Count;
        public int HistoryCount => history.Count;
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<MechanicalAssemblyEditOperation> pending = new List<MechanicalAssemblyEditOperation>();
        private readonly List<MechanicalAssemblyEditBatch> history = new List<MechanicalAssemblyEditBatch>();
        private readonly List<MechanicalAssemblyOutputComparisonRequest> comparisons = new List<MechanicalAssemblyOutputComparisonRequest>();
        private MechanicalAssemblyDraft initial;
        private PreparedMechanicalAssemblyEvaluation preparedEvaluation;
        private Font font;
        private string noticeValue;
        private Text notice, readback;
        private RectTransform tree, results;
        private double playStarted, lastPlayTick;
        private Rational playRoot, playSpeed;
        private bool ownsCamera;
        private Rect oldCameraRect;
        private bool oldOrthographic;
        private float oldCameraSize, oldNear, oldFar;
        private Vector3 oldCameraPosition;
        private Quaternion oldCameraRotation;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";

        private void Awake()
        {
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
            { var e = new GameObject("Assembly EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); e.transform.SetParent(transform, false); }
            SceneCamera = Camera.main; ownsCamera = SceneCamera == null;
            if (ownsCamera) { var go = new GameObject("Assembly scene camera"); go.tag = "MainCamera"; go.transform.SetParent(transform, false); SceneCamera = go.AddComponent<Camera>(); }
            oldCameraRect = SceneCamera.rect; oldOrthographic = SceneCamera.orthographic; oldCameraSize = SceneCamera.orthographicSize;
            oldCameraPosition = SceneCamera.transform.position; oldCameraRotation = SceneCamera.transform.rotation; oldNear = SceneCamera.nearClipPlane; oldFar = SceneCamera.farClipPlane;
            SceneCamera.orthographic = true; SceneCamera.rect = new Rect(700f / 1600, 520f / 1050, 900f / 1600, 530f / 1050);
            var goCanvas = new GameObject("Assembly authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); goCanvas.transform.SetParent(transform, false);
            goCanvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = goCanvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(goCanvas.transform, "Assembly controls", 0, 0, 700, 1050, new Color(.045f, .06f, .085f));
            Label(panel, "SINGLE ROOT / OWNED ASSEMBLY AUTHORING", 12, 7, 678, 27, 19);
            Label(panel, "Select an owner or actual reference. Corrections and targets are explicit; no automatic repair.", 12, 37, 675, 33, 12);
            tree = Scroll(panel, "Root/member and owned-reference tree", 0, 73, 700, 170);
            var form = Scroll(panel, "Explicit assembly fields", 0, 250, 700, 414); var y = 0f;
            void Field(string key, string title, string value) { Inputs.Add(key, GearInvestAuthoringWidgets.Field(font, form, key, title, value, y)); y += 32; }
            Field("file", "Input draft/session/artifact/declaration/batch path", "");
            Field("save", "New output path (create only)", "");
            Field("member", "Selected / new instance ID", "");
            Field("binding-owner", "Actual upstream owner: root OR member:ID", "root");
            Field("binding-shaft", "Actual upstream local shaft ID", "");
            Field("binding-port", "Optional actual mounting port ID", "");
            Field("binding-station", "Signed binding station (mm)", "0");
            Field("operation", "Operation (see buttons and readback help)", "WormStarts");
            Field("value", "Explicit operation value", "4");
            Field("worm-starts", "Worm starts (does NOT replace wheel)", "2");
            Field("wheel-slope", "Independent wheel trace slope", "1/5");
            Field("terminal-datum", "Terminal datum: turns or mm by family", "0");
            Field("lock-present", "Geneva ideal lock present true/false", "true");
            Field("transmission-present", "Belt transmission present true/false", "true");
            Field("cam-face", "Cam face lower,upper (mm)", "-20,20");
            Field("target-basis", "Member targets: GlobalRoot / MemberInput", "GlobalRoot");
            Field("target-a", "Transfer / Geneva step / linear stroke; none", "none");
            Field("target-b", "Affine phase / Geneva dwell fraction; none", "none");
            Field("target-reference", "Nonlinear reference output; none", "none");
            Field("target-root", "Explicit reference root (turns)", "0");
            Field("positive-stroke", "Cam positive-stroke requirement true/false", "false");
            Field("required", "Member required domains, comma-separated", "");
            Field("assembly-required", "Assembly required domains, comma-separated", "");
            Field("output-key", "Assembly observation key", "output");
            Field("output-ref", "Observation ref: root/memberId;Kind;localId", "root;Shaft;input");
            Field("output-transfer", "Observation required global transfer; none", "none");
            Field("output-reference", "Observation reference value; none", "none");
            Field("output-unit", "Observation reference unit: turns / mm", "turns");
            Field("output-root", "Observation reference global root", "0");
            Field("before-ref", "Before owned output: owner;Kind;localId", "root;Shaft;input");
            Field("after-ref", "After owned output: owner;Kind;localId", "root;Shaft;input");
            Field("comparison-mode", "UnwrappedAngular/WrappedAngular/PrismaticScalar/WorldPrismaticPath", "UnwrappedAngular");
            Field("input-map", "Explicit uAfter=alpha*uBefore+beta", "1,0");
            Field("output-map", "Explicit output sign,datum", "1,0");
            Field("world-map", "Optional after-world to before-world frame", "none");
            Field("root", "Absolute unwrapped root turns (exact rational)", "0");
            Field("speed", "Play speed: global root turns / second", "1");
            Field("work", "One aggregate numeric work ceiling", "262144");
            Field("angular-width", "Angular enclosure width (turns)", "1/1000000000000");
            Field("linear-width", "Linear enclosure width (mm)", "1/1000000000");
            Field("direction-width", "Unit-direction enclosure width", "1/1000000000000");
            Field("precision", "Maximum precision bits", "512");
            Field("refinements", "Maximum refinements", "4");
            Field("display", "Optional display true/false", "true");
            Field("contour-samples", "Requested cam contour samples (8..128)", "32");
            Field("root-scale", "Root mapping mm per source unit", "1");
            Field("root-frame", "Root mapping origin(mm);X;Y;Z", Identity);
            AddButton(form, "load-before", "Load before draft", 12, y, 215, LoadBeforeDraft);
            AddButton(form, "save-comparison", "Save comparison request", 237, y, 215, SaveComparisonRequest);
            AddButton(form, "save-declaration", "Save selected declaration", 462, y, 224, SaveSelectedDeclaration); y += 37;
            AddButton(form, "save-evaluation", "Save current exact evaluation", 12, y, 330, SaveEvaluation);
            AddButton(form, "queue-batch", "Load and queue assembly batch", 352, y, 334, QueueBatch); y += 37;
            AddButton(form, "upgrade-geneva-suffix", "Explicitly import 30A file into Geneva-suffix profile", 12, y, 674, UpgradeGenevaSuffixProfile); y += 37;
            form.sizeDelta = new Vector2(700, y + 8);
            var row = 674f;
            void Four(string a, string at, Action aa, string b, string bt, Action ba, string c, string ct, Action ca, string d, string dt, Action da)
            { AddButton(panel, a, at, 12, row, 162, aa); AddButton(panel, b, bt, 182, row, 162, ba); AddButton(panel, c, ct, 352, row, 162, ca); AddButton(panel, d, dt, 522, row, 164, da); row += 37; }
            Four("queue", "Queue named edit", Queue, "apply", "Apply atomic batch", Apply, "clear-queue", "Clear queue", ClearQueue, "analyze", "Analyze current", Analyze);
            Four("query", "Query binding", Query, "mark-before", "Mark before", MarkBefore, "compare", "Compare outputs", Compare, "finalize", "Finalize", FinalizeDraft);
            Four("evaluate", "Seek absolute root", Seek, "play", "Play / Pause", PlayPause, "fit", "Fit whole assembly", Fit, "focus", "Focus selected", FocusSelected);
            Four("load-draft", "Load draft", LoadDraft, "save-draft", "Save draft", SaveDraft, "load-session", "Load history", LoadSession, "save-session", "Save history", SaveSession);
            Four("reapply", "Reapply history", Reapply, "rebuild", "Rebuild artifact", Rebuild, "load-artifact", "Load artifact", LoadArtifact, "save-artifact", "Save artifact", SaveArtifact);
            Four("worm-starts", "Queue starts", () => QueueNamed("WormStarts", TextOf("worm-starts")), "wheel-slope", "Queue wheel slope", () => QueueNamed("WheelSlope", TextOf("wheel-slope")), "terminal-datum", "Queue datum", () => QueueNamed("TerminalDatum", TextOf("terminal-datum")), "reverse-shaft", "Queue shaft reverse", () => QueueNamed("ReverseWormShaft", ""));
            Four("geneva-lock", "Queue ideal lock", () => QueueNamed("GenevaLock", TextOf("lock-present")), "belt-transmission", "Queue belt present", () => QueueNamed("BeltTransmission", TextOf("transmission-present")), "cam-face", "Queue cam face", () => QueueNamed("CamFace", TextOf("cam-face")), "targets", "Queue targets/scopes", QueueTargets);
            Four("binding", "Queue binding", () => QueueOperation(new SetMechanicalAssemblyBindingEdit(TextOf("member"), BindingFields())), "disconnect", "Queue disconnect", () => QueueOperation(new SetMechanicalAssemblyBindingEdit(TextOf("member"), null)), "restore-binding", "Restore initial bind", RestoreBinding, "close", "Close current", Close);
            notice = Label(panel, "Load a whole assembly draft or session, or call SetDraft with a public SDK composition.", 12, 977, 674, 66, 12);
            results = Scroll(goCanvas.transform, "Exact current SDK readback", 710, 540, 878, 500);
            readback = Label(results, "No current assembly.", 7, 4, 845, 480, 13);
            View = new GameObject("SDK owned assembly presentation").AddComponent<GearInvestMechanicalAssemblyView>(); View.transform.SetParent(transform, false);
            Notice = "No surrogate source; no consumer-side mechanism solver. Optional drawing never replaces numeric results.";
        }

        public void SetDraft(MechanicalAssemblyDraft draft)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            ValidateLoadInputs();
            Close(); Draft = initial = Before = draft; PutDefinitionFields();
            Analyze(); BeforeAnalysis = Analysis; if (draft.Definition.Members.Count != 0) SelectMember(draft.Definition.Members[0].InstanceId); Fit();
            Notice = "Loaded whole single-root assembly; no member parameters, bindings or targets inferred."; RefreshReadback();
        }
        public void SelectMember(string instanceId)
        {
            NeedDraft(); var member = Draft.Definition.Members.Single(x => x.InstanceId == instanceId);
            SelectedMemberId = instanceId; Inputs["member"].text = instanceId; var d = member.Declaration;
            Inputs["target-basis"].text = d.TargetBasis.ToString(); Inputs["required"].text = string.Join(",", d.RequiredValidationDomains);
            if (member.InputBinding != null) PutBinding(member.InputBinding);
            else { Inputs["binding-owner"].text = "root"; Inputs["binding-shaft"].text = Inputs["binding-port"].text = Inputs["binding-station"].text = ""; }
            Inputs["target-a"].text = Inputs["target-b"].text = Inputs["target-reference"].text = "none"; Inputs["target-root"].text = "0";
            switch (d)
            {
                case AssemblyWormDeclaration w:
                    Inputs["worm-starts"].text = w.Device.Worm.Starts.ToString(CultureInfo.InvariantCulture); Inputs["wheel-slope"].text = w.Device.SelectedWheel == null ? "none" : w.Device.SelectedWheel.TraceSlope.ToString();
                    Inputs["terminal-datum"].text = w.OutputTerminal.PhaseOffset.ToString(); SetAffineTargets(w.Output, w.RequiredOutputPhase); break;
                case AssemblyOpenBeltDeclaration b:
                    Inputs["transmission-present"].text = BoolText(b.Device.TransmissionPresent); Inputs["terminal-datum"].text = b.OutputTerminal.PhaseOffset.ToString(); SetAffineTargets(b.Output, b.RequiredOutputPhase); break;
                case AssemblyPitchChainDeclaration c: Inputs["terminal-datum"].text = c.OutputTerminal.PhaseOffset.ToString(); SetAffineTargets(c.Output, c.RequiredOutputPhase); break;
                case AssemblyGenevaDeclaration g:
                    Inputs["lock-present"].text = BoolText(g.Device.IdealLock.Present); Inputs["terminal-datum"].text = g.Output.TerminalDatum.Value.ToString();
                    Inputs["target-a"].text = OptionalText(g.Requirement.RequiredIndexStep); Inputs["target-b"].text = OptionalText(g.Requirement.RequiredDwellFraction);
                    Inputs["target-reference"].text = OptionalText(g.Requirement.RequiredReferenceOutput); Inputs["target-root"].text = g.Requirement.ReferenceRoot.Value.ToString(); break;
                case AssemblyCrankSliderDeclaration c:
                    Inputs["terminal-datum"].text = c.Output.TerminalDatum.Value.ToString(); Inputs["target-a"].text = OptionalText(c.Requirement.RequiredStroke);
                    Inputs["target-reference"].text = OptionalText(c.Requirement.RequiredReferencePosition); Inputs["target-root"].text = c.Requirement.ReferenceRoot.Value.ToString(); break;
                case AssemblyCamFollowerDeclaration c:
                    Inputs["terminal-datum"].text = c.Output.TerminalDatum.Value.ToString(); Inputs["cam-face"].text = c.Device.FollowerFace.Lower.Value + "," + c.Device.FollowerFace.Upper.Value;
                    Inputs["target-a"].text = OptionalText(c.Requirement.RequiredStroke); Inputs["target-reference"].text = OptionalText(c.Requirement.RequiredReferencePosition);
                    Inputs["target-root"].text = c.Requirement.ReferenceRoot.Value.ToString(); Inputs["positive-stroke"].text = BoolText(c.Requirement.PositiveStrokeRequired); break;
            }
            Inputs["after-ref"].text = RefText(AssemblyComponentReference.Member(instanceId, AssemblyComponentKind.Output, d.OutputKey));
            Notice = "Selected " + instanceId + "; fields show committed values. Queued edits are not silently applied."; RefreshReadback();
        }
        public void SelectReference(AssemblyComponentReference reference)
        {
            SelectedReference = reference; Inputs["output-ref"].text = RefText(reference); Inputs["after-ref"].text = RefText(reference);
            if (reference.Kind == AssemblyComponentKind.Shaft) { Inputs["binding-owner"].text = OwnerText(reference); Inputs["binding-shaft"].text = reference.LocalId; }
            Notice = "Selected actual owned reference " + RefText(reference) + ". Selecting alone does not rebind a member."; RefreshReadback();
        }
        public void QueueOperation(MechanicalAssemblyEditOperation operation)
        { NeedDraft(); if (operation == null) throw new ArgumentNullException(nameof(operation)); pending.Add(operation); Notice = "Queued " + operation.Kind + "; " + pending.Count + " operations; Apply submits one SDK transaction."; RefreshReadback(); }
        public void ClearQueue() { pending.Clear(); Notice = "Queue cleared; authored definition and history unchanged."; RefreshReadback(); }
        private MechanicalAssemblyDraft StagedDraft()
        {
            NeedDraft(); if (pending.Count == 0) return Draft;
            var result = sdk.ApplyMechanicalAssemblyEdits(Draft, new MechanicalAssemblyEditBatch(Draft.Revision, Draft.DefinitionId, pending));
            if (result.Draft == null) throw new InvalidOperationException("Existing queue is rejected. Apply to record rejection, or clear it before authoring additional replacements.");
            return result.Draft;
        }
        public void Queue() => QueueNamed(TextOf("operation"), TextOf("value"));
        public void QueueNamed(string operation, string value)
        {
            NeedDraft(); var id = TextOf("member");
            switch (operation)
            {
                case "Binding": QueueOperation(new SetMechanicalAssemblyBindingEdit(id, BindingFields())); return;
                case "Disconnect": QueueOperation(new SetMechanicalAssemblyBindingEdit(id, null)); return;
                case "RestoreBinding": RestoreBinding(); return;
                case "RemoveMember": QueueOperation(new RemoveMechanicalAssemblyMemberEdit(id)); return;
                case "AddDeclaration": QueueOperation(new AddMechanicalAssemblyMemberEdit(new MechanicalAssemblyMember(id, sdk.LoadMechanicalAssemblyMemberDeclaration(TextOf("file")), BindingFields()))); return;
                case "ReplaceDeclaration": var old = StagedDraft().Definition.Members.Single(x => x.InstanceId == id); QueueOperation(new ReplaceMechanicalAssemblyMemberEdit(new MechanicalAssemblyMember(id, sdk.LoadMechanicalAssemblyMemberDeclaration(TextOf("file")), old.InputBinding))); return;
                case "RootBatch": QueueOperation(new ApplyMechanicalAssemblyRootEditsEdit(sdk.LoadMechanicalEditBatch(TextOf("file")))); return;
                case "RootMapping": QueueOperation(new SetMechanicalAssemblyRootMappingEdit(new SourceLengthMapping(R(TextOf("root-scale")), Frame(TextOf("root-frame"))))); return;
                case "AssemblyRequired": QueueOperation(new SetMechanicalAssemblyRequiredValidationEdit(Domains("assembly-required"))); return;
                case "Output": QueueOutput(); return;
                case "RemoveOutput": QueueOperation(new RemoveMechanicalAssemblyOutputEdit(TextOf("output-key"))); return;
                case "Targets": QueueTargets(); return;
            }
            var member = StagedDraft().Definition.Members.Single(x => x.InstanceId == id); var declaration = member.Declaration;
            if (operation == "RestoreMember") { QueueOperation(new ReplaceMechanicalAssemblyMemberEdit(initial.Definition.Members.Single(x => x.InstanceId == id))); return; }
            if (declaration is AssemblyWormDeclaration w)
            {
                var d = w.Device; var worm = d.Worm; var wheel = d.SelectedWheel; var shaft = d.OutputShaft; var station = d.OutputPitchStation; var mounting = d.OutputMountingPhase; var reference = d.OutputReferenceTurns; var terminal = w.OutputTerminal;
                if (operation == "WormStarts") worm = new CylindricalWormSpecification(Integer(value), worm.AxialModule, worm.PitchRadius, worm.Handedness, worm.Parameterization, worm.GeometryKind);
                else if (operation == "WheelSlope") { if (wheel == null) throw new InvalidOperationException("No selected wheel; use an explicit complete declaration."); wheel = new IdealWormWheelSpecification(wheel.ToothCount, wheel.TransverseModule, wheel.Handedness, R(value), wheel.Parameterization, wheel.TraceSemantics); }
                else if (operation == "TerminalDatum") terminal = PortDatum(terminal, R(value));
                else if (operation == "ReverseWormShaft") { var f = shaft.Frame; shaft = new OrientedShaft(shaft.Id, new OrientedFrame(f.Origin, f.X, -f.Y, -f.Z), shaft.IsPrescribed); station = Mm(-station.Value); mounting = Turns(-mounting.Value); reference = Turns(-reference.Value); }
                else throw new ArgumentException("Unsupported worm edit: " + operation);
                d = new WormDriveTransmissionDefinition(d.Id, d.InputShaftId, d.InputWormBodyId, worm, d.PhysicalHelixAxis, d.InputPitchStation, shaft, d.OutputWheelBodyId, wheel, station, d.ContactSide, d.InputMountingPhase, mounting, d.InputReferenceTurns, reference, d.TransmissionPresent, d.InputCenterFixed, d.InputAxisFixed, d.OutputCenterFixed, d.OutputAxisFixed, d.InputPortId, d.Profile);
                declaration = new AssemblyWormDeclaration(d, terminal, w.Output, w.RequiredOutputPhase, w.RequiredValidationDomains, w.TargetBasis);
            }
            else if (declaration is AssemblyGenevaDeclaration g)
            {
                var d = g.Device; var l = d.IdealLock; var output = g.Output; var station = d.DriverStation; var mounting = d.DriverMountingTurns;
                if (operation == "GenevaLock") l = new GenevaIdealLockSpecification(l.DriverFeatureId, l.RecessIds, l.RecessPitchCount, l.RecessCenterDistance, l.DriverRadius, l.RecessRadius, l.PatchHalfWidth, l.RecessMountingTurns, l.ReleaseWindow, Boolean(value), l.Policy, l.DriverSurfaceKind);
                else if (operation == "TerminalDatum") output = new GenevaOutputDefinition(output.Key, output.ShaftId, output.BodyId, output.TerminalSign, Turns(value));
                else if (operation == "GenevaDriverStation") station = Mm(value);
                else if (operation == "GenevaDriverMounting") mounting = Turns(value);
                else throw new ArgumentException("Unsupported Geneva edit: " + operation);
                d = new GenevaDeviceDefinition(d.Id, d.SourceShaftId, d.DriverBodyId, d.PinId, d.WheelBodyId, d.DriverCenterMm, station, d.PlaneNormal, d.CenterDirection, d.OutputShaft, d.WheelCenterMm, d.WheelStation, mounting, d.WheelMountingTurns, d.OutputReferenceTurns, d.RegistrationSlot, d.OrbitRadius, d.Wheel, l, d.PinPresent, d.AxesFixed, d.SourcePortId, d.MechanismKind, d.PinKind, d.PinCount);
                declaration = new AssemblyGenevaDeclaration(d, output, g.Requirement, g.RequiredValidationDomains, g.TargetBasis);
            }
            else if (declaration is AssemblyOpenBeltDeclaration b)
            {
                var d = b.Device; var terminal = b.OutputTerminal; var present = d.TransmissionPresent;
                if (operation == "BeltTransmission") present = Boolean(value); else if (operation == "TerminalDatum") terminal = PortDatum(terminal, R(value)); else throw new ArgumentException("Unsupported belt edit: " + operation);
                d = new OpenBeltTransmissionDefinition(d.Id, d.InputShaftId, d.InputPulleyBodyId, d.InputPulleyCenterMm, d.InputPitchRadius, d.OutputShaft, d.OutputPulleyBodyId, d.OutputPulleyStation, d.OutputPitchRadius, d.RouteNormal, d.InputReferenceTurns, d.OutputReferenceTurns, d.SelectedBelt, present, d.InputCenterFixed, d.InputAxisFixed, d.OutputCenterFixed, d.OutputAxisFixed, d.InputPortId, d.RoutingKind);
                declaration = new AssemblyOpenBeltDeclaration(d, terminal, b.Output, b.RequiredOutputPhase, b.RequiredValidationDomains, b.TargetBasis);
            }
            else if (declaration is AssemblyCamFollowerDeclaration c)
            {
                var d = c.Device; var face = d.FollowerFace; var output = c.Output;
                if (operation == "CamFace") { var p = Parts(value, ',', 2); face = new ExactQuantityInterval(Mm(p[0]), Mm(p[1])); }
                else if (operation == "TerminalDatum") output = LinearDatum(output, Mm(value)); else throw new ArgumentException("Unsupported cam edit: " + operation);
                d = new FlatCamFollowerDefinition(d.Id, d.SourceShaftId, d.CamBodyId, d.FollowerBodyId, d.GuideId, d.LinearDofId, d.FollowerReferenceId, d.SupportProfile, d.CenterMm, d.CamStation, d.PlaneNormal, d.GuideFrameMm, d.MountingTurns, d.GuideTravel, face, d.ContactPresent, d.ContactPolicy, d.GuidePresent, d.FollowerRotationFixed, d.TransverseMotionFixed, d.CamAxisFixed, d.SourcePortId, d.FollowerIsPrescribed, d.FollowerKind);
                declaration = new AssemblyCamFollowerDeclaration(d, output, c.Requirement, c.RequiredValidationDomains, c.TargetBasis);
            }
            else if (declaration is AssemblyCrankSliderDeclaration crank && operation == "TerminalDatum") declaration = new AssemblyCrankSliderDeclaration(crank.Device, LinearDatum(crank.Output, Mm(value)), crank.Requirement, crank.RequiredValidationDomains, crank.TargetBasis);
            else if (declaration is AssemblyPitchChainDeclaration chain && operation == "TerminalDatum") declaration = new AssemblyPitchChainDeclaration(chain.Device, PortDatum(chain.OutputTerminal, R(value)), chain.Output, chain.RequiredOutputPhase, chain.RequiredValidationDomains, chain.TargetBasis);
            else throw new ArgumentException("This family does not support the named edit. Load an explicit complete member declaration for other parameters.");
            QueueOperation(new ReplaceMechanicalAssemblyMemberEdit(new MechanicalAssemblyMember(id, declaration, member.InputBinding)));
        }

        public void QueueTargets()
        {
            var member = StagedDraft().Definition.Members.Single(x => x.InstanceId == TextOf("member")); var d = member.Declaration;
            var basis = (AssemblyTargetBasis)Enum.Parse(typeof(AssemblyTargetBasis), TextOf("target-basis"), false); var domains = Domains("required");
            var a = OptionalR(TextOf("target-a")); var b = OptionalR(TextOf("target-b")); var reference = OptionalR(TextOf("target-reference")); var root = Turns(TextOf("target-root"));
            switch (d)
            {
                case AssemblyWormDeclaration w: d = new AssemblyWormDeclaration(w.Device, w.OutputTerminal, Target(w.Output, a), b, domains, basis); break;
                case AssemblyOpenBeltDeclaration w: d = new AssemblyOpenBeltDeclaration(w.Device, w.OutputTerminal, Target(w.Output, a), b, domains, basis); break;
                case AssemblyPitchChainDeclaration w: d = new AssemblyPitchChainDeclaration(w.Device, w.OutputTerminal, Target(w.Output, a), b, domains, basis); break;
                case AssemblyGenevaDeclaration g: d = new AssemblyGenevaDeclaration(g.Device, g.Output, new GenevaRequirement(OptionalQuantity(a, true), b, OptionalQuantity(reference, true), root), domains, basis); break;
                case AssemblyCrankSliderDeclaration c: d = new AssemblyCrankSliderDeclaration(c.Device, c.Output, new CrankSliderRequirement(OptionalQuantity(a, false), OptionalQuantity(reference, false), root), domains, basis); break;
                case AssemblyCamFollowerDeclaration c: d = new AssemblyCamFollowerDeclaration(c.Device, c.Output, new CamFollowerRequirement(OptionalQuantity(a, false), OptionalQuantity(reference, false), root, Boolean(TextOf("positive-stroke"))), domains, basis); break;
            }
            QueueOperation(new ReplaceMechanicalAssemblyMemberEdit(new MechanicalAssemblyMember(member.InstanceId, d, member.InputBinding)));
        }
        public void QueueOutput()
        {
            var angular = TextOf("output-unit") == "turns"; if (!angular && TextOf("output-unit") != "mm") throw new FormatException("Observation unit must be turns or mm.");
            QueueOperation(new SetMechanicalAssemblyOutputEdit(new AssemblyOutputBinding(TextOf("output-key"), ParseReference(TextOf("output-ref")), OptionalR(TextOf("output-transfer")), OptionalQuantity(OptionalR(TextOf("output-reference")), angular), Turns(TextOf("output-root")))));
        }
        public void RestoreBinding() => QueueOperation(new SetMechanicalAssemblyBindingEdit(TextOf("member"), initial.Definition.Members.Single(x => x.InstanceId == TextOf("member")).InputBinding));
        public void Apply()
        {
            LastEdit = null; NeedDraft(); if (pending.Count == 0) throw new InvalidOperationException("Queue explicit operations first.");
            var batch = new MechanicalAssemblyEditBatch(Draft.Revision, Draft.DefinitionId, pending);
            if (history.Count >= MechanicalAssemblyProfile.MaxBatches || history.Sum(x => x.OperationCount) > MechanicalAssemblyProfile.MaxTotalOperations - batch.OperationCount)
                throw new InvalidOperationException("Session history limit reached; no edit applied or history dropped. Save the current draft, then explicitly load it to begin a new session.");
            LastEdit = sdk.ApplyMechanicalAssemblyEdits(Draft, batch); history.Add(batch); pending.Clear(); LastSession = null;
            if (LastEdit.Draft != null) { Draft = LastEdit.Draft; InvalidateCurrent(); Analyze(); }
            Notice = "Atomic edit " + LastEdit.Status + "; failed operation=" + LastEdit.FailingOperationIndex + "; rejected transactions remain in ordered history."; RefreshReadback();
        }
        public void Analyze()
        {
            NeedDraft(); IsPlaying = false; Evaluation = null; Analysis = null; preparedEvaluation = null; ConnectionQuery = null; Finalization = null; Rebuilt = null; View.HideCurrentMotion();
            PrepareCurrentAnalysis(NumericRequest()); RebuildTree();
            Display(() => View.Build(Draft, Analysis)); Evaluate(); Notice = "Current definition analyzed; topology=" + Analysis.TopologyValid + "; export=" + Analysis.ExportAdmission; RefreshReadback();
        }
        public void Query() { ConnectionQuery = null; NeedDraft(); ConnectionQuery = sdk.QueryMechanicalAssemblyBinding(Draft, TextOf("member"), NumericRequest()); Notice = "Binding query: structure=" + ConnectionQuery.StructuralValidity + "; mounting=" + ConnectionQuery.MountingValidity; RefreshReadback(); }
        public void Evaluate()
        {
            NeedDraft(); Evaluation = null; View.HideCurrentMotion(); var root = Turns(TextOf("root")); var request = NumericRequest();
            if (Analysis == null || Analysis.Draft.DraftId != Draft.DraftId || Analysis.NumericRequest.CanonicalRepresentation != request.CanonicalRepresentation)
            { PrepareCurrentAnalysis(request); Display(() => View.Build(Draft, Analysis)); }
            var current = preparedEvaluation == null ? sdk.EvaluateMechanicalAssembly(Analysis, root, request) : preparedEvaluation.Evaluate(root);
            if (current.Analysis.Draft.DraftId != Draft.DraftId || current.Analysis.NumericRequest.CanonicalRepresentation != request.CanonicalRepresentation)
                throw new InvalidOperationException("An evaluation for another revision or policy cannot be displayed as current.");
            Evaluation = current; Analysis = current.Analysis;
            Display(() => View.Apply(Evaluation)); Notice = "Current absolute root=" + root.Value + "; actual aggregate numeric work=" + Evaluation.NumericWork + "/" + request.MaximumWork; RefreshReadback();
        }
        private void PrepareCurrentAnalysis(AssemblyNumericRequest request)
        {
            preparedEvaluation = null;
            // Invalid-policy diagnosis is the existing public path, not a prepared-state admission.
            // Ordinary valid play/seek owns one explicit snapshot until revision/policy changes.
            if (!request.IsValid) { Analysis = sdk.AnalyzeMechanicalAssembly(Draft, request); return; }
            preparedEvaluation = sdk.PrepareMechanicalAssemblyEvaluation(Draft, request);
            Analysis = preparedEvaluation.Analysis;
        }
        public void PlayPause()
        {
            Inputs["root"].DeactivateInputField();
            if (IsPlaying) { IsPlaying = false; Notice = "Paused at current exact root."; return; }
            Evaluate(); playRoot = R(TextOf("root")); playSpeed = R(TextOf("speed")); playStarted = Time.realtimeSinceStartupAsDouble; lastPlayTick = -1; IsPlaying = true;
        }
        /// <summary>Explicit user seek rebases the single play clock. Frame evaluation never rebases itself.</summary>
        public void Seek()
        {
            Inputs["root"].DeactivateInputField(); var resume = IsPlaying; IsPlaying = false; Evaluate();
            if (resume) { playRoot = Evaluation.RootInput.Value; playStarted = Time.realtimeSinceStartupAsDouble; lastPlayTick = 0; IsPlaying = true; Notice += "; playback rebased to the exact requested root."; }
        }
        public void MarkBefore() { Comparison = null; NeedDraft(); if (Analysis == null) Analyze(); Before = Draft; BeforeAnalysis = Analysis; Inputs["before-ref"].text = TextOf("after-ref"); Notice = "Before snapshot retained explicitly: " + Before.DraftId; RefreshReadback(); }
        public void Compare()
        {
            Comparison = null; NeedDraft(); if (Before == null) throw new InvalidOperationException("Mark a before snapshot first."); var map = Parts(TextOf("input-map"), ',', 2); var output = Parts(TextOf("output-map"), ',', 2);
            var mode = (AssemblyOutputComparisonMode)Enum.Parse(typeof(AssemblyOutputComparisonMode), TextOf("comparison-mode"), false); var angular = mode == AssemblyOutputComparisonMode.UnwrappedAngular || mode == AssemblyOutputComparisonMode.WrappedAngular;
            var request = new MechanicalAssemblyOutputComparisonRequest(ParseReference(TextOf("before-ref")), ParseReference(TextOf("after-ref")), mode, ExactQuantity.TurnsPerTurn(R(map[0])), Turns(map[1]), Integer(output[0]), angular ? Turns(output[1]) : Mm(output[1]), NumericRequest(), TextOf("world-map") == "none" ? null : Frame(TextOf("world-map")));
            if (BeforeAnalysis == null || Analysis == null) throw new InvalidOperationException("Analyze current and load/mark the before snapshot first.");
            if (Before.DraftId == initial.DraftId && comparisons.Count >= MechanicalAssemblyProfile.MaxComparisons)
                throw new InvalidOperationException("Session comparison limit reached; no request dropped. Save the current draft, then explicitly load it to begin a new session.");
            Comparison = sdk.CompareMechanicalAssemblyOutputs(BeforeAnalysis, Analysis, request);
            // Session comparisons are defined by the SDK against its initial snapshot, not an arbitrary UI checkpoint.
            if (Before.DraftId == initial.DraftId) comparisons.Add(request);
            Notice = "Comparison " + Comparison.Verdict + "; " + Comparison.Scope + (Before.DraftId == initial.DraftId ? "; request included in session." : "; marked-before comparison is not an initial-session comparison. SaveComparisonRequest exports it explicitly."); RefreshReadback();
        }
        public void FinalizeDraft() { Finalization = null; NeedDraft(); Finalization = sdk.TryFinalizeMechanicalAssembly(Draft, NumericRequest()); Notice = "Fresh finalization " + Finalization.Status + "; all required scopes remain authored."; RefreshReadback(); }
        public void SaveDraft() { NeedDraft(); sdk.SaveMechanicalAssemblyDraft(Draft, TextOf("save")); var loaded = sdk.LoadMechanicalAssemblyDraft(TextOf("save")); if (loaded.DraftId != Draft.DraftId) throw new InvalidOperationException("Draft readback mismatch."); Notice = "Create-only draft saved and re-read: " + loaded.DraftId; }
        public void LoadDraft() => SetDraft(sdk.LoadMechanicalAssemblyDraft(TextOf("file")));
        public void UpgradeGenevaSuffixProfile() => SetDraft(sdk.UpgradeMechanicalAssemblyToGenevaSuffixProfile(TextOf("file")));
        public void LoadBeforeDraft() { Comparison = null; var draft = sdk.LoadMechanicalAssemblyDraft(TextOf("file")); var analysis = sdk.AnalyzeMechanicalAssembly(draft, NumericRequest()); Before = draft; BeforeAnalysis = analysis; Notice = "Loaded explicit before assembly " + Before.DraftId + "; current assembly unchanged."; RefreshReadback(); }
        public void QueueBatch()
        {
            NeedDraft(); var batch = sdk.LoadMechanicalAssemblyEditBatch(TextOf("file"));
            if (batch.ExpectedRevision != Draft.Revision || batch.ExpectedDefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Loaded batch does not name the current revision and definition.");
            foreach (var operation in batch.Operations) QueueOperation(operation);
        }
        public void SaveSession()
        {
            NeedDraft(); LastSession = sdk.CreateMechanicalAssemblyEditSession(initial, history, NumericRequest(), comparisons); sdk.SaveMechanicalAssemblyEditSession(LastSession, TextOf("save"));
            var loaded = sdk.LoadMechanicalAssemblyEditSession(TextOf("save")); if (loaded.CurrentDraft.DraftId != Draft.DraftId || loaded.SessionId != LastSession.SessionId) throw new InvalidOperationException("Session readback mismatch."); Notice = "Create-only session saved, re-read and replay-verified: " + loaded.SessionId;
        }
        public void LoadSession()
        {
            var loaded = sdk.LoadMechanicalAssemblyEditSession(TextOf("file")); ValidateLoadInputs(); Close(); LastSession = loaded; initial = Before = loaded.InitialDraft; Draft = sdk.ReapplyMechanicalAssemblyEditSession(loaded);
            history.AddRange(loaded.Batches); comparisons.AddRange(loaded.ComparisonRequests); BeforeAnalysis = loaded.InitialAnalysis; PutNumeric(loaded.NumericRequest); PutDefinitionFields(); Analyze(); if (Draft.Definition.Members.Count > 0) SelectMember(Draft.Definition.Members[0].InstanceId); Fit(); Notice = "Loaded/reapplied ordered history " + loaded.SessionId;
        }
        public void Reapply()
        { NeedDraft(); LastSession = sdk.CreateMechanicalAssemblyEditSession(initial, history, NumericRequest(), comparisons); Draft = sdk.ReapplyMechanicalAssemblyEditSession(LastSession); InvalidateCurrent(); Analyze(); Notice = "Reapplied initial assembly and " + history.Count + " ordered batches."; }
        public void LoadArtifact() { var artifact = sdk.LoadMechanicalAssemblyArtifact(TextOf("file")); ValidateLoadInputs(); PutNumeric(artifact.NumericRequest); SetDraft(artifact.Request); }
        public void SaveArtifact() { NeedDraft(); FinalizeDraft(); if (!Finalization.IsFinalized) throw new InvalidOperationException("Finalization blocked; no artifact written."); sdk.SaveMechanicalAssemblyFinalization(Finalization, TextOf("save")); var read = sdk.LoadMechanicalAssemblyArtifact(TextOf("save")); if (read.Request.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Artifact readback mismatch."); Notice = "Fresh artifact saved and reconstructed from authoritative readback."; }
        public void Rebuild()
        {
            var artifact = sdk.LoadMechanicalAssemblyArtifact(TextOf("file")); Rebuilt = sdk.RebuildMechanicalAssemblyArtifact(artifact);
            sdk.SaveMechanicalAssemblyArtifact(Rebuilt.Artifact, TextOf("save")); var loaded = sdk.LoadMechanicalAssemblyArtifact(TextOf("save"));
            if (loaded.ArtifactHash != Rebuilt.Artifact.ArtifactHash || loaded.Request.DefinitionId != Rebuilt.Artifact.Request.DefinitionId) throw new InvalidOperationException("Rebuilt artifact readback mismatch.");
            Notice = "Complete artifact rebuilt, create-only saved and freshly re-read: " + loaded.ArtifactHash; RefreshReadback();
        }
        public void SaveComparisonRequest() { if (Comparison == null) throw new InvalidOperationException("Compare first."); sdk.SaveMechanicalAssemblyOutputComparisonRequest(Comparison.Request, TextOf("save")); }
        public void SaveSelectedDeclaration() { NeedDraft(); sdk.SaveMechanicalAssemblyMemberDeclaration(Draft.Definition.Members.Single(x => x.InstanceId == TextOf("member")).Declaration, TextOf("save")); }
        public void SaveEvaluation() { if (Evaluation == null) throw new InvalidOperationException("Evaluate the current root first."); sdk.SaveMechanicalAssemblyEvaluation(Evaluation, TextOf("save")); Notice = "Saved current complete exact SDK evaluation, independent of approximate graphics."; }
        public void Close()
        {
            IsPlaying = false; Draft = Before = initial = null; Analysis = BeforeAnalysis = null; preparedEvaluation = null; Evaluation = null; LastEdit = null; ConnectionQuery = null; Comparison = null; Finalization = null; LastSession = null; Rebuilt = null;
            SelectedMemberId = null; SelectedReference = null; pending.Clear(); history.Clear(); comparisons.Clear(); DisplayUnavailableReason = null;
            if (View != null) View.Clear(); if (tree != null) ClearTree(); Notice = "Closed current assembly; no stale current motion."; RefreshReadback();
        }
        private void InvalidateCurrent() { IsPlaying = false; Analysis = null; preparedEvaluation = null; Evaluation = null; ConnectionQuery = null; Comparison = null; Finalization = null; Rebuilt = null; if (View != null) View.HideCurrentMotion(); }
        private void Display(Action render)
        { DisplayUnavailableReason = null; try { render(); } catch (Exception e) { DisplayUnavailableReason = e.Message; View.Clear(); } }
        private void Update()
        {
            if (IsPlaying && !Inputs["root"].isFocused)
            {
                // One user-controlled clock. Only elapsed time is quantized; the arbitrary-size root is exact throughout.
                var ticks = Math.Floor((Time.realtimeSinceStartupAsDouble - playStarted) * 10);
                if (ticks != lastPlayTick) { lastPlayTick = ticks; Inputs["root"].text = (playRoot + new Rational((long)ticks, 10) * playSpeed).ToString(); InvokeUi("play-tick", Evaluate); }
            }
        }
        private void RebuildTree()
        {
            ClearTree(); var y = 0f;
            RootButton = GearInvestAuthoringWidgets.Button(font, tree, "select-root", "ROOT  " + Draft.Definition.Root.Definition.RootShaftId, 8, y, 675, () => InvokeUi("select-root", () => { SelectedMemberId = null; Notice = "One actual live root " + Draft.Definition.Root.DraftId; RefreshReadback(); })); y += 35;
            foreach (var id in Analysis.TopologicalOrder.Concat(Draft.Definition.Members.Select(m => m.InstanceId)).Distinct())
            {
                var member = Draft.Definition.Members.Single(m => m.InstanceId == id); var node = Analysis.Members.FirstOrDefault(m => m.InstanceId == id);
                var depth = node == null ? 1 : Math.Min(4, node.Binding.DependencyPath.Count);
                MemberButtons[id] = GearInvestAuthoringWidgets.Button(font, tree, "select-member-" + id, new string(' ', depth * 2) + id + "  [" + member.Declaration.Kind + "]  " + (node == null ? "unresolved" : node.HasDeterminedMotion ? "determined" : "blocked"), 8, y, 675, () => InvokeUi("select-member", () => SelectMember(id))); y += 35;
                foreach (var entry in Analysis.Inventory.Where(e => e.Reference.Owner == AssemblyOwnerKind.Member && e.Reference.MemberId == id && (e.Reference.Kind == AssemblyComponentKind.Shaft || e.Reference.Kind == AssemblyComponentKind.Port || e.Reference.Kind == AssemblyComponentKind.Output)))
                { var reference = entry.Reference; ReferenceButtons[reference] = GearInvestAuthoringWidgets.Button(font, tree, "owned-reference", "    " + RefText(reference) + "  " + entry.Role, 22, y, 661, () => InvokeUi("select-reference", () => SelectReference(reference))); y += 35; }
            }
            foreach (var entry in Analysis.Inventory.Where(e => e.Reference.Owner == AssemblyOwnerKind.Root && (e.Reference.Kind == AssemblyComponentKind.Shaft || e.Reference.Kind == AssemblyComponentKind.Port || e.Reference.Kind == AssemblyComponentKind.Output)))
            { var reference = entry.Reference; ReferenceButtons[reference] = GearInvestAuthoringWidgets.Button(font, tree, "root-reference", RefText(reference), 22, y, 661, () => InvokeUi("select-reference", () => SelectReference(reference))); y += 35; }
            tree.sizeDelta = new Vector2(700, Math.Max(170, y));
        }
        private void ClearTree() { RootButton = null; MemberButtons.Clear(); ReferenceButtons.Clear(); foreach (Transform child in tree) { child.gameObject.SetActive(false); Destroy(child.gameObject); } }
        public void RefreshReadback()
        {
            if (readback == null) return; var text = new StringBuilder();
            if (Draft == null) text.AppendLine("No current assembly. Load a draft/session/artifact or supply a public composition with SetDraft.");
            else
            {
                text.AppendLine("CURRENT " + Draft.DefinitionId + "\nrevision=" + Draft.Revision + "; history=" + history.Count + "; pending=" + pending.Count);
                text.AppendLine("ONE LIVE ROOT " + Draft.Definition.Root.DraftId + "; shafts=" + Draft.Definition.Root.Definition.Shafts.Count + "; source bodies=" + Draft.Definition.Root.Definition.Bodies.Count + "; member instances=" + Draft.Definition.Members.Count);
                text.AppendLine("ROOT field=" + TextOf("root") + "; evaluated=" + (CurrentRoot.HasValue ? CurrentRoot.Value.Value.ToString() : "unavailable"));
                if (Analysis != null)
                {
                    text.AppendLine("Topology=" + Analysis.TopologyValid + "; admission=" + Analysis.ExportAdmission + "; root analyses=" + Analysis.RootAnalysisCount + "; local=" + Analysis.LocalAnalysisCount);
                    var visibleMembers = Draft.Definition.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId ?
                        Analysis.Members.OrderBy(x => x.InstanceId == SelectedMemberId ? 0 : 1).ThenBy(x => x.InstanceId, StringComparer.Ordinal) : Analysis.Members.AsEnumerable();
                    foreach (var node in visibleMembers)
                    {
                        var e = Evaluation == null ? null : Evaluation.Members.FirstOrDefault(x => x.InstanceId == node.InstanceId);
                        text.AppendLine(node.InstanceId + " [" + node.Member.Declaration.Kind + "] motion=" + node.HasDeterminedMotion + "; local=" + node.LocalAdmitted + "; target=" + node.Target + "; scopes=" + node.RequiredScopesSatisfied);
                        text.AppendLine("  " + DeclarationSummary(node.Member.Declaration) + "; target basis=" + node.Member.Declaration.TargetBasis);
                        if (node.GenevaShaftMotion != null)
                            text.AppendLine("  input=" + (node.Binding.GenevaInputMotion == null ? Law(node.InputRelation) : "A*G+B " + Law(node.Binding.GenevaInputMotion.Transform)) +
                                "; actual shaft=A*G+B " + Law(node.GenevaShaftMotion.Transform) + "; terminal=A*G+B " + Law(node.GenevaTerminalMotion.Transform));
                        else text.AppendLine("  actual input q,p=" + Law(node.InputRelation) + "; shaft=" + Law(node.ShaftRelation) + "; terminal=" + Law(node.TerminalRelation));
                        if (Draft.Definition.Profile == MechanicalAssemblyProfile.GenevaAffineSuffixId)
                            text.AppendLine("  local transfer=" + node.LocalTransfer + "; GLOBAL LAW=" + node.GlobalMotionKind +
                                (node.GenevaShaftMotion == null ? "" : "; A,B=" + Law(node.GenevaShaftMotion.Transform)));
                        text.AppendLine("  binding=" + (node.Binding.Binding == null ? "disconnected" : RefText(node.Binding.Binding.UpstreamShaft)) + "; path=" + string.Join(" -> ", node.Binding.DependencyPath.Select(RefText)));
                        if (node.Binding.FixedInputFrameMm != null) text.AppendLine("  fixed input frame(mm)=" + FrameText(node.Binding.FixedInputFrameMm));
                        if (e != null)
                        {
                            text.AppendLine("  " + e.Status + "; input=" + (e.InputTurns.HasValue ? e.InputTurns.Value.Value.ToString() : e.GenevaSuffix != null ? "exact accumulated + certified residual" : "unavailable") + "; numeric=" + e.NumericAvailable + "; display=" + e.DisplayAvailable + "; work=" + e.NumericWork);
                            if (e.Recipe is GenevaPoseRecipe g) text.AppendLine("  Geneva cycle k=" + g.CycleIndex + "; centered=" + g.CenteredPhaseTurns + "; regime=" + g.Regime + "; accumulated=" + g.AccumulatedShaftTurns + "; slot=" + g.SlotId + "; recess=" + g.RecessId);
                            if (e.GenevaSuffix != null)
                            {
                                var suffix = e.GenevaSuffix;
                                text.AppendLine("  suffix " + suffix.Input.Geneva.Regime + "; accumulated=" + suffix.Shaft.AccumulatedTurns + "; residual factor=" + suffix.Shaft.ResidualCoefficient);
                                text.AppendLine("  shaft=" + (suffix.ShaftTurns == null ? "numeric unavailable" : Interval(suffix.ShaftTurns.Lower, suffix.ShaftTurns.Upper)) +
                                    "; upstream reuse/refine=" + suffix.UpstreamReuses + "/" + suffix.UpstreamRefinements);
                            }
                            if (e.InputObservation is GenevaDriverObservation driver) text.AppendLine("  actual Geneva driver phase=" + driver.PhysicalPhaseTurns + "; actual input shaft=" + driver.SourceTurns);
                            if (e.Numeric is GenevaNumericComputation gn && gn.Pose != null) text.AppendLine("  terminal interval=" + Interval(gn.Pose.TerminalTurns.Lower, gn.Pose.TerminalTurns.Upper) + "; certified width=" + gn.Pose.TerminalTurns.Width);
                            if (e.Numeric is CrankSliderNumericComputation cn && cn.Pose != null) text.AppendLine("  terminal mm=" + Interval(cn.Pose.TerminalPositionMm.Lower, cn.Pose.TerminalPositionMm.Upper));
                            if (e.Recipe is CamFollowerPoseRecipe cp) text.AppendLine("  guide mm=" + cp.GuidePosition.Value + "; terminal mm=" + cp.TerminalPosition.Value);
                        }
                        foreach (var check in node.Checks) text.AppendLine("  " + check.Domain + "=" + check.Verdict + (check.Required ? " REQUIRED" : ""));
                    }
                    foreach (var check in Analysis.Checks) text.AppendLine(check.Domain + "=" + check.Verdict + (check.Required ? " REQUIRED" : ""));
                    foreach (var output in Analysis.Outputs) text.AppendLine("OBSERVATION " + output.Key + " -> " + RefText(output.Binding.Target) + "; resolved=" + output.IsResolved + "; target=" + output.Target + "; required transfer=" + OptionalText(output.Binding.RequiredGlobalTransfer) + "; reference=" + OptionalText(output.Binding.RequiredReferenceValue) + "@" + output.Binding.ReferenceRoot.Value);
                    foreach (var issue in Analysis.Diagnostics.Concat(Evaluation == null ? Enumerable.Empty<AssemblyDiagnostic>() : Evaluation.Diagnostics)) text.AppendLine(issue.Stage + "/" + issue.Code + ": " + issue.Detail + " refs=" + string.Join(",", issue.Related.Select(RefText)));
                }
                if (Evaluation != null) { text.AppendLine("WORK " + Evaluation.NumericWork + "/" + Evaluation.Analysis.NumericRequest.MaximumWork + "; one root evaluation=" + Evaluation.RootEvaluationCount); foreach (var shaft in Evaluation.Shafts) text.AppendLine(RefText(shaft.Reference) + "=" + shaft.Turns.Value + " turns"); }
            }
            if (ConnectionQuery != null) text.AppendLine("QUERY " + ConnectionQuery.InstanceId + " structure=" + ConnectionQuery.StructuralValidity + "; mounting=" + ConnectionQuery.MountingValidity + "\n" + string.Join("\n", ConnectionQuery.Diagnostics.Select(d => d.Code + ": " + d.Detail)));
            if (LastEdit != null) text.AppendLine("EDIT " + LastEdit.Status + "; failed index=" + LastEdit.FailingOperationIndex + "\n" + string.Join("\n", LastEdit.Diagnostics.Select(d => d.Code + ": " + d.Detail)));
            if (Comparison != null) text.AppendLine("COMPARE " + Comparison.Verdict + "; " + Comparison.ProofRule + "; scope=" + Comparison.Scope + "; work=" + Comparison.NumericWork);
            if (Finalization != null) text.AppendLine("FINALIZE " + Finalization.Status + "\n" + string.Join("\n", Finalization.Diagnostics.Select(d => d.Code + ": " + d.Detail)));
            if (DisplayUnavailableReason != null) text.AppendLine("VIEW DISPLAY UNAVAILABLE: " + DisplayUnavailableReason + "; current SDK numeric result preserved.");
            text.AppendLine("Named edits: WormStarts, WheelSlope, ReverseWormShaft, TerminalDatum, GenevaLock, GenevaDriverStation, GenevaDriverMounting, BeltTransmission, CamFace, Binding, Disconnect, RestoreBinding, RestoreMember, Targets, Output, RemoveOutput, AssemblyRequired, RootBatch, RootMapping, AddDeclaration, ReplaceDeclaration, RemoveMember.");
            text.AppendLine("Reversal never edits consumers. Queue the chosen consumer's Binding, GenevaDriverStation and GenevaDriverMounting separately. Complete declaration files expose all remaining authored parameters.");
            text.AppendLine("Geometry rendering is not clearance, force, torque, dynamics, retention or manufacturing certification.");
            readback.text = text.ToString(); var height = Mathf.Max(480, readback.preferredHeight + 20); readback.rectTransform.sizeDelta = new Vector2(845, height); results.sizeDelta = new Vector2(878, height + 12);
        }
        public void Fit() { if (View != null) FitBounds(View.ViewBounds); }
        public void FocusSelected() { Bounds bounds; if (SelectedMemberId == null || !View.TryGetMemberBounds(SelectedMemberId, out bounds)) throw new InvalidOperationException("Select an existing member with visible geometry."); FitBounds(bounds); }
        private void FitBounds(Bounds bounds)
        {
            var center = bounds.center; var radius = Math.Max(1, bounds.extents.magnitude); var forward = new Vector3(.6f, -.45f, 1).normalized;
            SceneCamera.transform.position = center - forward * (radius * 4 + 80); SceneCamera.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            var h = 0f; var w = 0f;
            foreach (var x in new[] { bounds.min.x, bounds.max.x }) foreach (var y in new[] { bounds.min.y, bounds.max.y }) foreach (var z in new[] { bounds.min.z, bounds.max.z })
            { var d = new Vector3(x, y, z) - center; h = Mathf.Max(h, Mathf.Abs(Vector3.Dot(d, SceneCamera.transform.up))); w = Mathf.Max(w, Mathf.Abs(Vector3.Dot(d, SceneCamera.transform.right))); }
            SceneCamera.orthographicSize = Mathf.Max(15, Mathf.Max(h, w / SceneCamera.aspect) * 1.12f); SceneCamera.nearClipPlane = .1f; SceneCamera.farClipPlane = radius * 10 + 500;
        }
        private void OnDestroy()
        { preparedEvaluation = null; Evaluation = null; Analysis = BeforeAnalysis = null; if (!ownsCamera && SceneCamera != null) { SceneCamera.rect = oldCameraRect; SceneCamera.orthographic = oldOrthographic; SceneCamera.orthographicSize = oldCameraSize; SceneCamera.transform.SetPositionAndRotation(oldCameraPosition, oldCameraRotation); SceneCamera.nearClipPlane = oldNear; SceneCamera.farClipPlane = oldFar; } }
        private void InvokeUi(string key, Action action)
        {
            try { action(); ActionCounts[key] = ActionCounts.TryGetValue(key, out var count) ? count + 1 : 1; ActionCompleted?.Invoke(key); }
            catch (Exception e) { IsPlaying = false; Notice = "OPERATION ERROR " + key + ": " + e.Message; if (key == "evaluate" || key == "play-tick" || key == "analyze") { Evaluation = null; View.HideCurrentMotion(); } }
            RefreshReadback();
        }
        private void AddButton(Transform p, string key, string label, float x, float y, float w, Action action) => Buttons.Add(key, GearInvestAuthoringWidgets.Button(font, p, key, label, x, y, w, () => InvokeUi(key, action)));
        private Text Label(Transform p, string text, float x, float y, float w, float h, int size) => GearInvestAuthoringWidgets.Label(font, p, text, x, y, w, h, size, new Color(.9f, .94f, 1));
        private RectTransform Scroll(Transform p, string name, float x, float y, float w, float h)
        { var box = GearInvestAuthoringWidgets.Box(p, name, x, y, w, h, new Color(.055f, .07f, .1f)); box.gameObject.AddComponent<RectMask2D>(); var scroll = box.gameObject.AddComponent<ScrollRect>(); scroll.viewport = box; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped; var content = GearInvestAuthoringWidgets.Rect(box, name + " content", 0, 0, w, h); scroll.content = content; return content; }
        public AssemblyNumericRequest NumericRequest() => new AssemblyNumericRequest(Integer(TextOf("work")), Turns(TextOf("angular-width")), Mm(TextOf("linear-width")), R(TextOf("direction-width")), Integer(TextOf("precision")), Integer(TextOf("refinements")), Boolean(TextOf("display")), AssemblyNumericRequest.CurrentPolicy, Integer(TextOf("contour-samples")));
        private void PutNumeric(AssemblyNumericRequest r) { Inputs["work"].text = r.MaximumWork.ToString(CultureInfo.InvariantCulture); Inputs["angular-width"].text = r.AngularWidth.Value.ToString(); Inputs["linear-width"].text = r.LinearWidth.Value.ToString(); Inputs["direction-width"].text = r.DirectionWidth.ToString(); Inputs["precision"].text = r.MaximumPrecisionBits.ToString(CultureInfo.InvariantCulture); Inputs["refinements"].text = r.MaximumRefinements.ToString(CultureInfo.InvariantCulture); Inputs["display"].text = BoolText(r.IncludeDisplay); Inputs["contour-samples"].text = r.CamContourSamples.ToString(CultureInfo.InvariantCulture); }
        private void PutDefinitionFields()
        {
            Inputs["assembly-required"].text = string.Join(",", Draft.Definition.RequiredValidationDomains);
            var mapping = Draft.Definition.RootMapping;
            Inputs["root-scale"].text = mapping == null ? "" : mapping.MillimetersPerSourceUnit.ToString();
            Inputs["root-frame"].text = mapping == null ? "" : FrameText(mapping.PoseMm);
        }
        private void ValidateLoadInputs() { _ = Turns(TextOf("root")); _ = NumericRequest(); }
        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Load or compose a whole assembly first."); }
        private string TextOf(string key) => Inputs[key].text.Trim();
        private string[] Domains(string key) => TextOf(key).Split(',').Select(x => x.Trim()).Where(x => x.Length != 0).ToArray();
        private UpstreamShaftBinding BindingFields() => new UpstreamShaftBinding(TextOf("binding-owner") == "root" ? AssemblyComponentReference.Root(AssemblyComponentKind.Shaft, TextOf("binding-shaft")) : AssemblyComponentReference.Member(MemberOwner(TextOf("binding-owner")), AssemblyComponentKind.Shaft, TextOf("binding-shaft")), Mm(TextOf("binding-station")), TextOf("binding-port").Length == 0 ? null : TextOf("binding-port"));
        private void PutBinding(UpstreamShaftBinding b) { Inputs["binding-owner"].text = OwnerText(b.UpstreamShaft); Inputs["binding-shaft"].text = b.UpstreamShaft.LocalId; Inputs["binding-port"].text = b.MountingPortId ?? ""; Inputs["binding-station"].text = b.MountingStation.Value.ToString(); }
        private void SetAffineTargets(MechanicalOutput output, Rational? phase) { Inputs["target-a"].text = OptionalText(output.RequiredTransfer); Inputs["target-b"].text = OptionalText(phase); }
        private static MechanicalOutput Target(MechanicalOutput o, Rational? transfer) => new MechanicalOutput(o.Key, o.ShaftId, o.BodyId, o.PortId, transfer, o.Role, o.UnresolvedReason, o.FormerEndpoint);
        private static ShaftPort PortDatum(ShaftPort p, Rational datum) => new ShaftPort(p.Id, p.ShaftId, p.Frame, datum, p.Kind);
        private static PrismaticOutputDefinition LinearDatum(PrismaticOutputDefinition p, ExactQuantity datum) => new PrismaticOutputDefinition(p.Key, p.LinearDofId, p.BodyId, p.ReferencePointId, p.TerminalSign, datum);
        private static string BoolText(bool value) => value ? "true" : "false";
        private static bool Boolean(string value) => value == "true" ? true : value == "false" ? false : throw new FormatException("Use explicit true or false.");
        private static int Integer(string value) => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture);
        private static Rational R(string value) => Rational.Parse(value);
        private static ExactQuantity Turns(string value) => Turns(R(value));
        private static ExactQuantity Turns(Rational value) => ExactQuantity.Turns(value);
        private static ExactQuantity Mm(string value) => Mm(R(value));
        private static ExactQuantity Mm(Rational value) => ExactQuantity.Millimeters(value);
        private static Rational? OptionalR(string value) => value == "none" ? (Rational?)null : R(value);
        private static string OptionalText(Rational? value) => value.HasValue ? value.Value.ToString() : "none";
        private static string OptionalText(ExactQuantity? value) => value.HasValue ? value.Value.Value.ToString() : "none";
        private static ExactQuantity? OptionalQuantity(Rational? value, bool angular) => value.HasValue ? angular ? Turns(value.Value) : Mm(value.Value) : (ExactQuantity?)null;
        private static string[] Parts(string text, char delimiter, int count) { var parts = text.Split(delimiter).Select(x => x.Trim()).ToArray(); if (parts.Length != count) throw new FormatException("Expected " + count + " explicit fields separated by " + delimiter); return parts; }
        private static ExactVector3 Vector(string text) { var p = Parts(text, ',', 3); return new ExactVector3(R(p[0]), R(p[1]), R(p[2])); }
        private static OrientedFrame Frame(string text) { var p = Parts(text, ';', 4); return new OrientedFrame(Vector(p[0]), Vector(p[1]), Vector(p[2]), Vector(p[3])); }
        private static string VectorText(ExactVector3 v) => v.X + "," + v.Y + "," + v.Z;
        private static string FrameText(OrientedFrame f) => VectorText(f.Origin) + ";" + VectorText(f.X) + ";" + VectorText(f.Y) + ";" + VectorText(f.Z);
        private static string OwnerText(AssemblyComponentReference r) => r.Owner == AssemblyOwnerKind.Root ? "root" : "member:" + r.MemberId;
        private static string MemberOwner(string text) => text.StartsWith("member:", StringComparison.Ordinal) ? text.Substring(7) : text;
        public static string RefText(AssemblyComponentReference r) => OwnerText(r) + ";" + r.Kind + ";" + r.LocalId;
        public static AssemblyComponentReference ParseReference(string text) { var p = Parts(text, ';', 3); var kind = (AssemblyComponentKind)Enum.Parse(typeof(AssemblyComponentKind), p[1], false); return p[0] == "root" ? AssemblyComponentReference.Root(kind, p[2]) : AssemblyComponentReference.Member(MemberOwner(p[0]), kind, p[2]); }
        private static string Law(ExactAffineRelation? law) => law.HasValue ? law.Value.Coefficient + "," + law.Value.Phase : "undetermined";
        private static string DeclarationSummary(AssemblyMemberDeclaration declaration)
        {
            switch (declaration)
            {
                case AssemblyWormDeclaration w: return "starts=" + w.Device.Worm.Starts + "; module=" + w.Device.Worm.AxialModule.Value + "; wheel trace=" + (w.Device.SelectedWheel == null ? "none" : w.Device.SelectedWheel.TraceSlope.ToString()) + "; terminal datum=" + w.OutputTerminal.PhaseOffset + "; required transfer,phase=" + OptionalText(w.Output.RequiredTransfer) + "," + OptionalText(w.RequiredOutputPhase);
                case AssemblyOpenBeltDeclaration b: return "belt present=" + b.Device.TransmissionPresent + "; pitch radii=" + b.Device.InputPitchRadius.Value + "," + b.Device.OutputPitchRadius.Value + "; required transfer,phase=" + OptionalText(b.Output.RequiredTransfer) + "," + OptionalText(b.RequiredOutputPhase);
                case AssemblyPitchChainDeclaration c: return "teeth=" + c.Device.InputToothCount + "," + c.Device.OutputToothCount + "; pitches(mm)=" + c.Device.InputPitch.Value + "," + c.Device.OutputPitch.Value + "; links=" + (c.Device.SelectedChain == null ? "none" : c.Device.SelectedChain.LinkCount.ToString(CultureInfo.InvariantCulture)) + "; required transfer,phase=" + OptionalText(c.Output.RequiredTransfer) + "," + OptionalText(c.RequiredOutputPhase);
                case AssemblyGenevaDeclaration g: return "N=" + g.Device.Wheel.SlotCount + "; pin=" + g.Device.PinPresent + "; ideal lock=" + g.Device.IdealLock.Present + "; driver station=" + g.Device.DriverStation.Value + "; mounting=" + g.Device.DriverMountingTurns.Value + "; targets step,dwell,reference@root=" + OptionalText(g.Requirement.RequiredIndexStep) + "," + OptionalText(g.Requirement.RequiredDwellFraction) + "," + OptionalText(g.Requirement.RequiredReferenceOutput) + "@" + g.Requirement.ReferenceRoot.Value;
                case AssemblyCrankSliderDeclaration c: return "radius,rod(mm)=" + c.Device.CrankRadius.Value + "," + c.Device.RodLength.Value + "; branch=" + c.Device.AssemblyBranch + "; travel(mm)=" + c.Device.GuideTravel.Lower.Value + "," + c.Device.GuideTravel.Upper.Value + "; required stroke,reference@root=" + OptionalText(c.Requirement.RequiredStroke) + "," + OptionalText(c.Requirement.RequiredReferencePosition) + "@" + c.Requirement.ReferenceRoot.Value;
                case AssemblyCamFollowerDeclaration c: return "face(mm)=" + c.Device.FollowerFace.Lower.Value + "," + c.Device.FollowerFace.Upper.Value + "; required stroke,reference@root=" + OptionalText(c.Requirement.RequiredStroke) + "," + OptionalText(c.Requirement.RequiredReferencePosition) + "@" + c.Requirement.ReferenceRoot.Value;
                default: return declaration.Kind.ToString();
            }
        }
        private static string Interval(Rational lower, Rational upper) => lower == upper ? lower.ToString() : "[" + Endpoint(lower, false) + ", " + Endpoint(upper, true) + "] outward-rounded readback";
        private static string Endpoint(Rational value, bool upper)
        {
            var scale = System.Numerics.BigInteger.Pow(10, 8); var scaled = value.Numerator * scale;
            var units = System.Numerics.BigInteger.DivRem(scaled, value.Denominator, out var remainder);
            if (!remainder.IsZero && (upper && scaled.Sign > 0 || !upper && scaled.Sign < 0)) units += scaled.Sign;
            var whole = System.Numerics.BigInteger.DivRem(System.Numerics.BigInteger.Abs(units), scale, out var fraction); var digits = fraction.ToString("D8", CultureInfo.InvariantCulture).TrimEnd('0');
            return (units.Sign < 0 ? "-" : "") + whole.ToString(CultureInfo.InvariantCulture) + (digits.Length == 0 ? "" : "." + digits);
        }
    }
}
