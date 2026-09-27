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
    /// <summary>Optional authoring form. SDK owns support geometry, contact, proof, exact motion and numerical admission.</summary>
    public sealed class GearInvestCamFollowerInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public CamFollowerDraft Draft { get; private set; }
        public CamFollowerDraft Before { get; private set; }
        public CamFollowerAnalysis Analysis { get; private set; }
        public CamFollowerEvaluation Evaluation { get; private set; }
        public CamFollowerEditResult LastEdit { get; private set; }
        public CamFollowerOutputEquivalenceResult Comparison { get; private set; }
        public CamFollowerCompatibilityResult ConnectionQuery { get; private set; }
        public CamFollowerFinalizationResult Finalization { get; private set; }
        public GearInvestCamFollowerView View { get; private set; }
        public MechanicalDraft Source { get; private set; }
        public string Notice { get; private set; }
        public string Readback => readback == null ? "" : readback.text;
        public string DisplayUnavailableReason { get; private set; }
        public int PendingOperationCount => pending.Count;
        public bool IsPlaying { get; private set; }
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly List<CamFollowerEditBatch> history = new List<CamFollowerEditBatch>();
        private readonly List<CamFollowerEditOperation> pending = new List<CamFollowerEditOperation>();
        private readonly List<CamFollowerOutputComparisonRequest> comparisons = new List<CamFollowerOutputComparisonRequest>();
        private CamFollowerDraft initial;
        private Font font; private Text notice, readback; private Camera sceneCamera;
        private CameraState previousCamera; private bool ownsSceneCamera, comparisonReadOnly;
        private double playStarted; private Rational playRoot;
        private const string DefaultProfileText = "rise,Quintic,0,1/4,30,40;high-dwell,Dwell,1/4,1/2,40,40;return,Quintic,1/2,3/4,40,30;low-dwell,Dwell,3/4,1,30,30";
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";

        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null)
            { var e = new GameObject("Cam / follower EventSystem", typeof(EventSystem), typeof(StandaloneInputModule)); e.transform.SetParent(transform, false); }
            sceneCamera = Camera.main; ownsSceneCamera = sceneCamera == null;
            if (ownsSceneCamera) { var go = new GameObject("Cam / follower scene camera"); go.tag = "MainCamera"; go.transform.SetParent(transform, false); sceneCamera = go.AddComponent<Camera>(); }
            else previousCamera = new CameraState(sceneCamera);
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.035f, .055f, .085f);
            sceneCamera.orthographic = true; sceneCamera.rect = new Rect(700f / 1600, 520f / 1050, 900f / 1600, 530f / 1050);
            var uiCamera = new GameObject("Cam / follower UI camera").AddComponent<Camera>(); uiCamera.transform.SetParent(transform, false);
            uiCamera.cullingMask = 1 << 5; uiCamera.clearFlags = CameraClearFlags.Depth; uiCamera.depth = sceneCamera.depth + 10;
            uiCamera.transform.position = new Vector3(0, 0, -1000); uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 30;
            var canvasObject = new GameObject("Cam / follower authoring controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Cam / follower controls", 0, 0, 700, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "CONVEX CAM / FINITE FLAT FOLLOWER", 12, 9, 675, 28, 20);
            Label(panel, "SDK support contour and global contact proof. Maintained contact is ideal, not a force calculation.", 12, 40, 675, 30, 12);
            var viewport = GearInvestAuthoringWidgets.Box(panel, "Scrollable cam-follower parameters", 0, 74, 700, 610, new Color(.055f, .07f, .1f));
            viewport.gameObject.AddComponent<RectMask2D>(); var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var form = GearInvestAuthoringWidgets.Rect(viewport, "Explicit cam-follower inputs", 0, 0, 700, 1100); scroll.content = form; var y = 0f;
            void Field(string key, string title, string value) { Inputs.Add(key, GearInvestAuthoringWidgets.Field(font, form, key, title, value, y)); y += 32; }
            Field("file", "Source / draft / session / artifact file", ""); Field("shaft", "Actual retained source shaft", "output"); Field("port", "Optional retained port (no second sign)", "");
            Field("scale", "Explicit mm per source unit", "1"); Field("pose", "Source pose origin(mm);X;Y;Z", Identity);
            Field("profile", "Support records: id,kind,start,end,H0,H1;...", DefaultProfileText);
            Field("station", "Signed cam station on actual shaft (mm)", "40"); Field("center", "Explicit cam center O (mm)", "60,0,40");
            Field("normal", "Mechanism normal n", "0,0,1"); Field("guide-frame", "Guide origin(mm);X;Y;Z [Z=g]", "90,0,40;0,1,0;0,0,1;1,0,0");
            Field("offset", "Lateral datum e (mm)", "0"); Field("datum", "Longitudinal datum d (mm)", "30");
            Field("travel", "Finite guide lower,upper (mm)", "0,10"); Field("face", "Finite face lambda lower,upper (mm)", "-13,13");
            Field("contact", "Explicit contact policy / none", CamFollowerProfile.MaintainedContactIdeal);
            Field("stroke", "Independent required stroke mm / none", "none"); Field("positive-stroke", "Require positive stroke true/false", "false");
            Field("reference", "Required terminal mm / none, reference root", "none,0"); Field("terminal", "Readout sign,datum (body unchanged)", "1,0");
            Field("mounting", "Signed cam mounting phase (turns)", "0");
            Field("proof-depth", "Proof maximum subdivision depth", "16"); Field("proof-nodes", "Proof maximum visited nodes", "4096");
            Field("proof-work", "Proof arithmetic work budget", "262144"); Field("proof-precision", "Proof precision bits", "512");
            Field("required", "Additional required validation domains (comma)", "");
            Field("operation", "Typed edit: Profile/Face/GuideOffset/Targets/Contact", "Profile"); Field("value", "Edit value / source contact / boolean flags", "");
            Field("comparison", "Scope; mapped-output sign,datum (mm)", "FollowerScalar;1,0"); Field("input-map", "Explicit uAfter=alpha*uBefore+beta", "1,0");
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
            Four("load-draft", "Load draft", () => Adopt(sdk.LoadCamFollowerDraft(Inputs["file"].text)), "load-session", "Load session", LoadSession, "save-draft", "Save draft", SaveDraft, "save-session", "Save history", SaveSession);
            Four("reapply", "Reapply history", Reapply, "rebuild", "Rebuild artifact", Rebuild, "load-artifact", "Load artifact", () => Adopt(sdk.LoadCamFollowerArtifact(Inputs["file"].text).Request), "close", "Close current", Close);
            notice = Label(panel, "Choose source and Compose. Every later dimension, guide and target change is explicit.", 12, 950, 674, 85, 13);
            var results = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Exact cam-follower readback", 710, 540, 878, 499, new Color(.065f, .085f, .115f));
            results.gameObject.AddComponent<RectMask2D>(); var resultScroll = results.gameObject.AddComponent<ScrollRect>(); resultScroll.viewport = results; resultScroll.horizontal = false; resultScroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Rect(results, "Exact cam-follower results", 0, 0, 855, 485); resultScroll.content = content;
            readback = Label(content, "No current mechanism.", 7, 4, 845, 480, 14);
            View = new GameObject("Cam / follower position presentation").AddComponent<GearInvestCamFollowerView>(); View.transform.SetParent(transform, false);
            Notice = "Exact SDK position kinematics. Display midpoints never certify the mechanical result.";
        }
        private void PublicSource()
        {
            var kinematic = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") }, new[] { new ExternalGearCoupling("mesh", "input", "output", 20, 40) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", 0, 0), new SpatialAxis("output-axis", 60, 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, 20, 20), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, 40, 40) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("cam-follower-public-spur-20-40", kinematic, spatial));
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
            Inputs["scale"].text = "1"; Inputs["pose"].text = Identity; Inputs["profile"].text = DefaultProfileText;
            Inputs["offset"].text = Inputs["mounting"].text = "0"; Inputs["datum"].text = "30"; Inputs["travel"].text = "0,10"; Inputs["face"].text = "-13,13";
            Inputs["contact"].text = CamFollowerProfile.MaintainedContactIdeal; Inputs["positive-stroke"].text = "false";
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
            var center = frame.Origin + frame.Z * station;
            Inputs["station"].text = station.ToString(); Inputs["center"].text = Point(center); Inputs["normal"].text = Point(frame.Z);
            Inputs["guide-frame"].text = FrameText(new OrientedFrame(center + frame.X * 30, frame.Y, frame.Z, frame.X));
            Inputs["offset"].text = "0"; Inputs["datum"].text = "30"; Inputs["port"].text = Source.Definition.Outputs.FirstOrDefault(o => o.ShaftId == Inputs["shaft"].text)?.PortId ?? "";
        }
        private SourceLengthMapping Mapping() => new SourceLengthMapping(Rational.Parse(Inputs["scale"].text), Frame(Inputs["pose"].text));
        private CamFollowerRequirement Requirement()
        { var p = Parts(Inputs["reference"].text, ',', 2); return new CamFollowerRequirement(OptionalMm(Inputs["stroke"].text), OptionalMm(p[0]), ExactQuantity.Turns(Rational.Parse(p[1])), Boolean(Inputs["positive-stroke"].text)); }
        private ExactQuantityInterval Interval(string key) { var p = Parts(Inputs[key].text, ',', 2); return new ExactQuantityInterval(Mm(p[0]), Mm(p[1])); }
        private CamSupportProfile Profile()
        {
            var rows = Inputs["profile"].text.Split(';'); if (rows.Length > 32) throw new FormatException("At most32 support records.");
            return new CamSupportProfile(rows.Select(row =>
            {
                var p = Parts(row.Trim(), ',', 6);
                if (!Enum.TryParse(p[1], false, out CamSupportSegmentKind kind) || !Enum.IsDefined(typeof(CamSupportSegmentKind), kind)) throw new FormatException("Dwell or Quintic required.");
                return new CamSupportSegment(p[0], Rational.Parse(p[2]), Rational.Parse(p[3]), Mm(p[4]), Mm(p[5]), kind);
            }));
        }
        private CamGeometryProofRequest ProofRequest() => new CamGeometryProofRequest(Integer(Inputs["proof-precision"].text), Integer(Inputs["proof-depth"].text), Integer(Inputs["proof-nodes"].text), Integer(Inputs["proof-work"].text));
        private string ContactPolicy() => Inputs["contact"].text == "none" ? null : Inputs["contact"].text;
        private void Compose()
        {
            if (Source == null) throw new InvalidOperationException("Load or generate an actual source first.");
            var terminal = Parts(Inputs["terminal"].text, ',', 2);
            var d = new FlatCamFollowerDefinition("cam-follower", Inputs["shaft"].text, "cam-follower/cam", "cam-follower/follower",
                "cam-follower/guide", "cam-follower/linear", "cam-follower/face-reference", Profile(), Vector(Inputs["center"].text),
                Mm(Inputs["station"].text), Vector(Inputs["normal"].text), Frame(Inputs["guide-frame"].text),
                ExactQuantity.Turns(Rational.Parse(Inputs["mounting"].text)), Interval("travel"), Interval("face"), contactPolicy: ContactPolicy(),
                sourcePortId: string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text);
            Adopt(sdk.ComposeCamFollowerDraft(new CamFollowerDefinition(Source, Mapping(), d,
                new PrismaticOutputDefinition("follower-output", d.LinearDofId, d.FollowerBodyId, d.FollowerReferenceId, Integer(terminal[0]), Mm(terminal[1])), Requirement(), RequiredDomains())));
        }
        private void Adopt(CamFollowerDraft draft) { Close(); initial = Before = Draft = draft; Hydrate(); AllowComparison(); Analyze(); Notice = "Current authored declarations adopted. Analyze never submits unqueued form fields."; }
        private void Hydrate()
        {
            var d = Draft.Definition; var c = d.Device; Source = d.Source;
            Inputs["shaft"].text = c.SourceShaftId; Inputs["port"].text = c.SourcePortId ?? ""; Inputs["scale"].text = d.SourceMapping?.MillimetersPerSourceUnit.ToString() ?? "";
            Inputs["pose"].text = d.SourceMapping == null ? "" : FrameText(d.SourceMapping.PoseMm);
            Inputs["profile"].text = string.Join(";", c.SupportProfile.Segments.Select(p => p.Id + "," + p.Kind + "," + p.StartTurns + "," + p.EndTurns + "," + p.StartHeight.Value + "," + p.EndHeight.Value));
            Inputs["station"].text = c.CamStation.Value.ToString(); Inputs["center"].text = Point(c.CenterMm); Inputs["normal"].text = Point(c.PlaneNormal);
            Inputs["guide-frame"].text = FrameText(c.GuideFrameMm); var displacement = c.GuideFrameMm.Origin - c.CenterMm;
            Inputs["datum"].text = displacement.Dot(c.GuideFrameMm.Z).ToString(); Inputs["offset"].text = displacement.Dot(c.PlaneNormal.Cross(c.GuideFrameMm.Z)).ToString();
            Inputs["travel"].text = c.GuideTravel.Lower.Value + "," + c.GuideTravel.Upper.Value; Inputs["face"].text = c.FollowerFace.Lower.Value + "," + c.FollowerFace.Upper.Value;
            Inputs["contact"].text = c.ContactPolicy ?? "none"; Inputs["mounting"].text = c.MountingTurns.Value.ToString();
            Inputs["stroke"].text = d.Requirement.RequiredStroke?.Value.ToString() ?? "none"; Inputs["positive-stroke"].text = d.Requirement.PositiveStrokeRequired ? "true" : "false";
            Inputs["reference"].text = (d.Requirement.RequiredReferencePosition?.Value.ToString() ?? "none") + "," + d.Requirement.ReferenceRoot.Value;
            Inputs["terminal"].text = d.Output.TerminalSign + "," + d.Output.TerminalDatum.Value; Inputs["required"].text = string.Join(",", d.RequiredValidationDomains);
        }
        private void NeedDraft() { if (Draft == null) throw new InvalidOperationException("Compose or load a cam-follower draft first."); }
        private void Analyze()
        {
            NeedDraft(); Analysis = sdk.AnalyzeCamFollowerDraft(Draft, ProofRequest()); Finalization = null; Comparison = null; ConnectionQuery = null; DisplayUnavailableReason = null;
            try { View.Build(Draft, Analysis); Fit(); } catch (ArgumentException e) { DisplayError(e); }
            Evaluate(); Notice = "Analyzed current submitted definition; full-cycle travel=" + Analysis.GuideTravelCoverage + ", target=" + Analysis.Target;
        }
        private CamNumericRequest NumericRequest() => new CamNumericRequest(Mm(Inputs["width"].text), Integer(Inputs["work"].text), Integer(Inputs["precision"].text), Integer(Inputs["refinements"].text));
        private void Evaluate()
        {
            NeedDraft(); if (Analysis == null) throw new InvalidOperationException("Analyze first.");
            Evaluation = sdk.EvaluateCamFollowerAnalysis(Analysis, ExactQuantity.Turns(Rational.Parse(Inputs["root"].text)), NumericRequest());
            if (DisplayUnavailableReason == null) try { View.Apply(Evaluation); } catch (ArgumentException e) { DisplayError(e); }
            Notice = "Absolute Evaluate " + Evaluation.Status + "; travel=" + Evaluation.TravelDecision + "; numeric=" + Evaluation.ContactNumeric?.Status;
        }
        private void PlayPause()
        { NeedDraft(); IsPlaying = !IsPlaying; if (IsPlaying) { playRoot = Rational.Parse(Inputs["root"].text); playStarted = Time.realtimeSinceStartupAsDouble; } }
        private void Queue()
        {
            NeedDraft(); var c = Draft.Definition.Device; var list = new List<CamFollowerEditOperation>();
            // All fields, including every member of a compound input, parse before this queue is mutated.
            switch (Inputs["operation"].text)
            {
                case "Profile": list.Add(new SetCamSupportProfileEdit(c.Id, Profile())); break;
                case "Segment":
                    var id = Inputs["value"].text; list.Add(new SetCamSupportSegmentEdit(c.Id, id, Profile().Segments.Single(p => p.Id == id))); break;
                case "Guide": list.Add(new SetCamFollowerGuidePlacementEdit(c.Id, Frame(Inputs["guide-frame"].text), Vector(Inputs["normal"].text))); break;
                case "GuideOffset":
                    var frame = Frame(Inputs["guide-frame"].text); var normal = Vector(Inputs["normal"].text);
                    var origin = Vector(Inputs["center"].text) + frame.Z * Rational.Parse(Inputs["datum"].text) + normal.Cross(frame.Z) * Rational.Parse(Inputs["offset"].text);
                    list.Add(new SetCamFollowerGuidePlacementEdit(c.Id, new OrientedFrame(origin, frame.X, frame.Y, frame.Z), normal)); break;
                case "Center": list.Add(new SetCamFollowerCenterEdit(c.Id, Vector(Inputs["center"].text), Mm(Inputs["station"].text))); break;
                case "Mounting": list.Add(new SetCamFollowerMountingPhaseEdit(c.Id, ExactQuantity.Turns(Rational.Parse(Inputs["mounting"].text)))); break;
                case "Travel": list.Add(new SetCamFollowerGuideTravelEdit(c.Id, Interval("travel"))); break;
                case "Face": list.Add(new SetCamFollowerFaceEdit(c.Id, Interval("face"))); break;
                case "Terminal": var terminal = Parts(Inputs["terminal"].text, ',', 2); list.Add(new SetCamFollowerTerminalEdit(Draft.Definition.Output.Key, Integer(terminal[0]), Mm(terminal[1]))); break;
                case "Targets": list.Add(new SetCamFollowerRequirementsEdit(Draft.Definition.Output.Key, Requirement())); break;
                case "Contact": list.Add(new SetCamFollowerContactEdit(c.Id, Boolean(Inputs["value"].text), ContactPolicy())); break;
                case "Grounding": var flags = Parts(Inputs["value"].text, ',', 4).Select(Boolean).ToArray(); list.Add(new SetCamFollowerGroundingEdit(c.Id, flags[0], flags[1], flags[2], flags[3])); break;
                case "InputTopology": list.Add(new SetCamFollowerInputTopologyEdit(c.Id, Boolean(Inputs["value"].text))); break;
                case "Binding": list.Add(new SetCamFollowerBindingEdit(c.Id, Inputs["shaft"].text, string.IsNullOrEmpty(Inputs["port"].text) ? null : Inputs["port"].text)); break;
                case "Mapping": list.Add(new SetCamFollowerSourceLengthMappingEdit(Mapping())); break;
                case "Required": list.Add(new SetCamFollowerRequiredValidationDomainsEdit(RequiredDomains())); break;
                case "SourceRemove": list.Add(SourceEdit(new RemoveContactEdit(Inputs["value"].text))); break;
                case "SourceRestore": list.Add(SourceEdit(new AddContactEdit(initial.Definition.Source.Definition.Contacts.Single(k => k.Id == Inputs["value"].text)))); break;
                default: throw new FormatException("Unknown typed cam-follower operation.");
            }
            if (pending.Count + list.Count > 128) throw new FormatException("Pending operation bound exceeded.");
            // Validate the whole prospective typed batch before publishing queued operations.
            var checkedBatch = new CamFollowerEditBatch(Draft.Revision, Draft.DefinitionId, pending.Concat(list));
            pending.AddRange(list); Notice = "Queued " + list.Count + " explicit operations; " + checkedBatch.Operations.Count + " pending. No automatic travel or target repair.";
        }
        private CamFollowerEditOperation SourceEdit(MechanicalEditOperation operation)
        {
            var source = Draft.Definition.Source;
            foreach (var queued in pending.OfType<ApplyCamFollowerSourceEditsEdit>())
            { var preview = sdk.ApplyMechanicalEdits(source, queued.Batch); if (preview.Draft == null) throw new InvalidOperationException("Earlier source transaction is rejected."); source = preview.Draft; }
            return new ApplyCamFollowerSourceEditsEdit(new MechanicalEditBatch(source.Revision, source.DefinitionId, new[] { operation }));
        }
        private void Apply()
        {
            NeedDraft(); var batch = new CamFollowerEditBatch(Draft.Revision, Draft.DefinitionId, pending); LastEdit = sdk.ApplyCamFollowerEdits(Draft, batch);
            history.Add(batch); pending.Clear(); Finalization = null; Comparison = null; ConnectionQuery = null; Evaluation = null; View.HideCurrentMotion();
            if (LastEdit.Draft != null) { Draft = LastEdit.Draft; Hydrate(); Analyze(); }
            Notice = LastEdit.Status + "; revision=" + Draft.Revision + "; current guide and target changed only when explicitly submitted.";
        }
        private void Query() { NeedDraft(); ConnectionQuery = sdk.QueryCamFollowerCompatibility(Draft, ProofRequest()); Notice = "Local " + ConnectionQuery.Verdict + "; support geometry and global ideal contact, not contact retention dynamics."; }
        private void MarkBefore() { NeedDraft(); Before = Draft; Comparison = null; AllowComparison(); Notice = "Marked before; only initial-to-current comparisons are persisted in this history."; }
        private void AllowComparison()
        { comparisonReadOnly = false; Inputs["comparison"].interactable = Inputs["input-map"].interactable = Inputs["world-map"].interactable = true; Buttons["compare"].interactable = true; Inputs["comparison"].text = "FollowerScalar;1,0"; Inputs["input-map"].text = "1,0"; Inputs["world-map"].text = Identity; }
        private void Compare()
        {
            NeedDraft(); if (comparisonReadOnly) throw new InvalidOperationException("Saved extended comparison mapping is read-only; Mark before begins a new explicit request.");
            var parts = Parts(Inputs["comparison"].text, ';', 2); var output = Parts(parts[1], ',', 2); var input = Parts(Inputs["input-map"].text, ',', 2);
            if (!Enum.TryParse(parts[0], false, out CamFollowerOutputComparisonMode mode) || !Enum.IsDefined(typeof(CamFollowerOutputComparisonMode), mode)) throw new FormatException("Unknown output comparison mode.");
            var request = new CamFollowerOutputComparisonRequest(Before.Definition.Output.Key, Draft.Definition.Output.Key,
                Before.Definition.Source.Definition.RootShaftId, Draft.Definition.Source.Definition.RootShaftId,
                ExactQuantity.TurnsPerTurn(Rational.Parse(input[0])), ExactQuantity.Turns(Rational.Parse(input[1])), Integer(output[0]), Mm(output[1]), mode,
                Before.Definition.Output.ReferencePointId, Draft.Definition.Output.ReferencePointId,
                mode == CamFollowerOutputComparisonMode.WorldFollowerPath ? Frame(Inputs["world-map"].text) : null, numericRequest: NumericRequest());
            Comparison = sdk.CompareCamFollowerOutputMotion(sdk.AnalyzeCamFollowerDraft(Before), Analysis, request);
            if (Before.DraftId == initial.DraftId && !comparisons.Any(r => r.RequestId == request.RequestId))
            { if (comparisons.Count >= CamFollowerProfile.MaxComparisons) throw new InvalidOperationException("Comparison history bound exceeded."); comparisons.Add(request); }
            Notice = "Follower-only " + Comparison.Verdict + " / " + Comparison.ProofRule + ". Sample agreement is not a function proof; material/contact-footprint equality is not claimed.";
        }
        private void HydrateComparison()
        {
            AllowComparison(); if (comparisons.Count == 0) return; var r = comparisons[comparisons.Count - 1]; Before = initial;
            Comparison = sdk.CompareCamFollowerOutputMotion(sdk.AnalyzeCamFollowerDraft(initial), Analysis, r);
            // Preserve extended witness/domain/numeric mappings exactly; never replace them by a default request.
            comparisonReadOnly = true; Inputs["comparison"].text = r.Mode + ";" + r.Sign + "," + r.Delta.Value; Inputs["input-map"].text = r.Alpha.Value + "," + r.Beta.Value;
            Inputs["world-map"].text = r.AfterWorldToBeforeWorldMm == null ? Identity : FrameText(r.AfterWorldToBeforeWorldMm);
            Inputs["comparison"].interactable = Inputs["input-map"].interactable = Inputs["world-map"].interactable = false; Buttons["compare"].interactable = false;
        }
        private void FinalizeDraft() { NeedDraft(); Finalization = sdk.TryFinalizeCamFollowerDraft(Draft); Notice = "Finalize " + Finalization.Status + "; exact reference recipe only, numerical precision is a separate request."; }
        private void SaveArtifact()
        { NeedDraft(); if (Finalization == null || !Finalization.IsFinalized || Finalization.DefinitionId != Draft.DefinitionId) throw new InvalidOperationException("Finalize this exact current definition first."); sdk.SaveCamFollowerFinalization(Finalization, Inputs["save"].text); Notice = "Source-aware artifact saved with staged exact readback."; }
        private void SaveDraft() { NeedDraft(); sdk.SaveCamFollowerDraft(Draft, Inputs["save"].text); Notice = "Current declarations saved; saved draft is not automatically an admitted artifact."; }
        private void SaveSession() { NeedDraft(); sdk.SaveCamFollowerEditSession(sdk.CreateCamFollowerEditSession(initial, history, comparisons), Inputs["save"].text); Notice = "Original plus ordered actual edits and explicit comparisons saved."; }
        private void LoadSession()
        { var session = sdk.LoadCamFollowerEditSession(Inputs["file"].text); Adopt(session.InitialDraft); history.AddRange(session.Batches); comparisons.AddRange(session.ComparisonRequests); Draft = session.CurrentDraft; Hydrate(); Analyze(); HydrateComparison(); Notice = "Loaded and freshly replayed session. Current exact fields hydrated."; }
        private void Reapply()
        { NeedDraft(); Draft = sdk.ReapplyCamFollowerEditSession(sdk.CreateCamFollowerEditSession(initial, history, comparisons)); Hydrate(); Analyze(); HydrateComparison(); Notice = "Ordered Reapply and fresh Analyze; current fields hydrated from actual resulting declarations."; }
        private void Rebuild()
        { var rebuilt = sdk.RebuildCamFollowerArtifact(sdk.LoadCamFollowerArtifact(Inputs["file"].text)); sdk.SaveCamFollowerArtifact(rebuilt.Artifact, Inputs["save"].text); if (!File.ReadAllBytes(Inputs["save"].text).SequenceEqual(rebuilt.Bytes)) throw new IOException("Saved rebuild differs."); Notice = "Current source, support geometry, contact, full-cycle proof and root-zero exact recipe rebuilt from request."; }
        private void Close()
        {
            Draft = Before = initial = null; Source = null; Analysis = null; Evaluation = null; LastEdit = null; Comparison = null; ConnectionQuery = null; Finalization = null;
            history.Clear(); pending.Clear(); comparisons.Clear(); comparisonReadOnly = false; IsPlaying = false; DisplayUnavailableReason = null; if (View != null) View.Clear();
            Notice = "Closed. No cached cam or follower frame is current.";
        }
        private void Update()
        {
            if (IsPlaying) try { var micros = (long)((Time.realtimeSinceStartupAsDouble - playStarted) * 1000000); Inputs["root"].text = (playRoot + new Rational(micros, 1000000)).ToString(); Evaluate(); } catch (Exception e) { InputError(e); }
            notice.text = Notice;
            if (Draft == null || Analysis == null) { readback.text = "No current cam-follower analysis. Complete source loaded=" + (Source != null); return; }
            var c = Draft.Definition.Device; var m = Analysis.Descriptor; var envelope = Analysis.Envelope; var proof = Analysis.GeometryProof;
            var text = "CURRENT " + Draft.DefinitionId + " revision " + Draft.Revision + "\n" +
                "SOURCE shafts=" + Draft.Definition.Source.Definition.Shafts.Count + "; gears=" + Draft.Definition.Source.Definition.Bodies.Count +
                "; separate cam/follower=2; prescribed follower=" + c.FollowerIsPrescribed + "\n" +
                "Support records=" + c.SupportProfile.Segments.Count + "; contact=" + c.ContactPresent + " / " + c.ContactPolicy + "\n" +
                "Source law=" + Law(Analysis.SourceRelation) + "; follower-contact phi law=" + (m == null ? "unavailable" : Law(m.PhysicalPhaseRelation)) + "\n" +
                "O=" + c.CenterMm + "; G=" + c.GuideFrameMm.Origin + "; n=" + c.PlaneNormal + "; g=" + c.GuideFrameMm.Z + "\n" +
                "h is support height, NOT polar radius. C=h*N+h_t*T/(2*pi); rho=h+h_tt/(4*pi^2)\n" +
                "GEOMETRY PROOF " + proof.Status + "; nodes=" + proof.Nodes + "; depth=" + proof.MaximumDepthReached + "; work=" + proof.Work + "\n" +
                "d=" + envelope?.GuideDatumMm + "; e=" + envelope?.GuideOffsetMm + "; range=" + envelope?.LowerMm + ".." + envelope?.UpperMm + "; stroke=" + envelope?.StrokeMm + " mm\n" +
                "CONTACT FOOTPRINT " + envelope?.MinimumFaceCoordinateMm + " .. " + envelope?.MaximumFaceCoordinateMm + "\n" +
                "FACE " + c.FollowerFace + ": " + Analysis.FaceCoverage + "; GUIDE " + c.GuideTravel + ": " + Analysis.GuideTravelCoverage + "\n" +
                "Target=" + Analysis.Target + "; follower determinacy=" + Analysis.Determinacy + "; export=" + Analysis.ExportAdmission + "\n";
            if (proof.Witness != null) text += "REFUTATION t=" + proof.Witness.MaterialNormalTurns + "; v=" + proof.Witness.LocalParameter + "; certified rho.upper=" + proof.Witness.RadiusInterval.Upper + "\n";
            if (Evaluation != null)
            {
                var cam = Evaluation.Cam;
                text += "CAM source=" + (cam == null ? "unknown" : cam.SourceTurns + " turns") +
                    "; physical phi=" + (cam?.PhysicalPhaseTurns == null ? "unknown" : cam.PhysicalPhaseTurns.Value + " turns") + "\n";
                text += "ROOT " + Evaluation.RootTurns + " -> " + Evaluation.Status + "; face=" + Evaluation.FaceDecision + "; travel=" + Evaluation.TravelDecision + "\n";
                foreach (var r in Evaluation.Rotary) text += "ROTARY " + r.ShaftId + "=" + r.Turns + " turns\n";
                var recipe = Evaluation.Recipe;
                if (recipe != null)
                {
                    text += "phi=" + recipe.PhysicalPhaseTurns + "; contact t=" + recipe.ContactParameterTurns + "; segment=" + recipe.Support.SegmentId + "; local v=" + recipe.Support.LocalParameter + "\n";
                    text += "EXACT h=" + recipe.Support.HeightMm + "; h_t=" + recipe.Support.FirstDerivativeMmPerTurn + "; h_tt=" + recipe.Support.SecondDerivativeMmPerTurnSquared + "\n";
                    text += "EXACT x=" + recipe.GuidePosition + "; terminal=" + recipe.TerminalPosition + "; lambda=" + recipe.FaceCoordinateMm + "\n";
                    text += "REFERENCE P=" + recipe.FollowerReferencePointMm + "; CONTACT Q=" + recipe.ContactPointMm + "\n";
                }
                text += "NUMERIC contact=" + Evaluation.ContactNumeric?.Status + "; material=" + Evaluation.MaterialNumeric?.Status + "; total work=" + Evaluation.NumericWork + "/" + Evaluation.Request.MaximumWork + "\n";
                text += Evaluation.Pose == null ? "NO NORMAL FOLLOWER POSE. Diagnostic results do not authorize rendering.\n" : "NORMAL POSE: blue=P; yellow=Q; magenta=persistent material t=0.\n";
            }
            else text += "NO CURRENT EVALUATION; no previous success frame.\n";
            if (View.IsRejectedGeometryPreview) text += "RED OUTLINE = REJECTED / UNPROVED GEOMETRY PREVIEW, not an admitted cam.\n";
            if (DisplayUnavailableReason != null) text += "DISPLAY_UNAVAILABLE: " + DisplayUnavailableReason + "\n";
            if (LastEdit != null) text += "EDIT " + LastEdit.Status + "; failed index=" + LastEdit.FailingOperationIndex + "; queued=" + pending.Count + "\n";
            if (Comparison != null) text += "COMPARE " + Comparison.Verdict + " / " + Comparison.ProofRule + "; " + Comparison.Scope + "; operating domains=" + Comparison.OperatingDomainsEqual + "\n";
            if (Finalization != null) text += "FINALIZE " + Finalization.Status + "\n";
            foreach (var check in Analysis.Checks) text += check.Domain + ": " + check.Verdict + (check.Required ? " [required]" : "") + "\n";
            foreach (var d in Analysis.Diagnostics.Concat(Evaluation == null ? Enumerable.Empty<MechanicalDiagnostic>() : Evaluation.Diagnostics)) text += d.Stage + "/" + d.Code + "\n";
            text += "MaintainedContactIdeal is not force, spring, gravity, wear, solid clearance or manufacturing validation.";
            readback.text = text; var height = Mathf.Max(485, readback.preferredHeight + 14); readback.rectTransform.sizeDelta = new Vector2(845, height); ((RectTransform)readback.transform.parent).sizeDelta = new Vector2(855, height + 10);
        }
        private void InputError(Exception error)
        {
            IsPlaying = false; Evaluation = null; Comparison = null; Finalization = null; ConnectionQuery = null; if (View != null) View.HideCurrentMotion();
            Notice = "INPUT/OPERATION ERROR: " + error.Message + "; no cached successful frame is current.";
        }
        private void DisplayError(Exception error)
        { IsPlaying = false; DisplayUnavailableReason = error.Message; if (View != null) View.Clear(); Notice = "DISPLAY_UNAVAILABLE: " + error.Message + "; SDK exact result remains independent."; }
        private void Fit()
        {
            var center = View.ViewCenter; var radius = View.ViewRadius;
            // View from the source side: the retained wire gears stay visible in
            // front of an enlarged opaque cam without moving either mechanism.
            sceneCamera.transform.position = center + new Vector3(.35f, -.65f, -1.5f).normalized * radius * 3; sceneCamera.transform.LookAt(center);
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
