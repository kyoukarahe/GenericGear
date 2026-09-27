using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Layout;
using GearInvest.Serialization.Json;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GearInvest.Unity
{
    /// <summary>Finite goal authoring and presentation only. All search, ranking, validation and project operations use the SDK.</summary>
    public sealed class GearInvestOrientedGoalInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public GearInvestOrientedMechanismView View { get; private set; }
        public GearInvestPitchPairView PairView { get; private set; }
        public PitchPairProof SelectedPitchPair { get; private set; }
        public string PitchComparison { get; private set; }
        private PitchPairProof[] pitchPairs = Array.Empty<PitchPairProof>();
        private int pitchIndex = -1;
        public OrientedGoalAuthoringController Controller { get; private set; }
        public OrientedTwoOutputArtifact Artifact { get; private set; }
        public bool SelectedRebuilt { get; private set; }
        public string Notice { get; private set; }
        public Rational RootTurns { get; private set; }
        public bool Playing { get; private set; }
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private readonly Dictionary<string, string> loadedFields = new Dictionary<string, string>();
        private OrientedTwoOutputGoal loadedGoal;
        private OrientedGoalGeneration observed;
        private Font font; private Camera sceneCamera; private Text status, readback; private bool populating; private int selectedIndex = -1;
        private float side = 1, playStart; private Rational playBase;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";
        private const string BFrame = "60,0,0;0,0,1;0,-1,0;1,0,0";

        private void Awake()
        {
            Application.runInBackground = true; Controller = new OrientedGoalAuthoringController(sdk); font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("Goal EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            sceneCamera = Camera.main; if (sceneCamera == null) { var cameraObject = new GameObject("Goal camera"); cameraObject.tag = "MainCamera"; sceneCamera = cameraObject.AddComponent<Camera>(); }
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.075f, .085f, .12f); sceneCamera.orthographic = true;
            sceneCamera.rect = new Rect(620f / 1600, 340f / 1050, 980f / 1600, 710f / 1050);
            var cameraUi = new GameObject("Goal UI camera").AddComponent<Camera>(); cameraUi.transform.SetParent(transform, false); cameraUi.cullingMask = 1 << 5; cameraUi.clearFlags = CameraClearFlags.Depth;
            cameraUi.depth = sceneCamera.depth + 10; cameraUi.transform.position = new Vector3(0, 0, -1000); cameraUi.nearClipPlane = .1f; cameraUi.farClipPlane = 30;
            var canvasObject = new GameObject("Goal controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasObject.transform.SetParent(transform, false); canvasObject.layer = 5;
            var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = cameraUi; canvas.planeDistance = 10;
            var scaler = canvasObject.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Goal panel", 0, 0, 620, 1050, new Color(.055f, .07f, .1f));
            Label(panel, "GOAL / FINITE ORIENTED SEARCH", 14, 8, 590, 28, 20);
            Label(panel, "Fixed terminals. Finite domains. Scroll for sources, frames and bounds.", 14, 38, 590, 24, 13);
            var viewport = GearInvestAuthoringWidgets.Box(panel, "Scrollable goal fields", 0, 68, 620, 636, new Color(.055f, .07f, .1f)); viewport.gameObject.AddComponent<RectMask2D>();
            var scroll = viewport.gameObject.AddComponent<ScrollRect>(); scroll.viewport = viewport; scroll.horizontal = false; scroll.movementType = ScrollRect.MovementType.Clamped;
            var content = GearInvestAuthoringWidgets.Box(viewport, "Finite input content", 0, 0, 605, 1083, new Color(.055f, .07f, .1f)); scroll.content = content;
            var rows = new[] {
                new[]{"a-z","A z poses (blank = exact records)","60,100,140"}, new[]{"input-teeth","Bevel input tooth choices","10"}, new[]{"output-teeth","Bevel output tooth choices","20"},
                new[]{"scales","Common pitch scale choices","1"}, new[]{"target-a","A terminal signed q","-1/2"}, new[]{"target-b","B terminal signed q","1/6"},
                new[]{"budget","Tuple work budget","10000"}, new[]{"cap","Returned candidate cap","16"}, new[]{"b-policy","B: NoneOnly / RequiredSource / OptionalSource","RequiredSource"},
                new[]{"source-a","A files: path;input;output | ...",""}, new[]{"source-b","B files: path;input;output | ...",""},
                new[]{"a-teeth","Build A sources: in,out | ...","20,40"}, new[]{"b-teeth","Build B sources: in,out | ...","10,30"},
                new[]{"a-ports","A source semantic input,output","input,output"}, new[]{"b-ports","B source semantic input,output","input,output"},
                new[]{"a-records","A: pose @ mating-frame @ penalty | ...","0,0,100;1,0,0;0,1,0;0,0,1@0,0,100;1,0,0;0,1,0;0,0,1@0"},
                new[]{"b-records","B: pose @ mating-frame @ penalty | ...",BFrame+"@"+BFrame+"@0"},
                new[]{"root-frame","Fixed root origin;X;Y;Z",Identity}, new[]{"output-frame","Fixed bevel output origin;X;Y;Z","0,0,0;0,0,1;0,-1,0;1,0,0"},
                new[]{"mount","Apex;coneA;coneB;inner;None B station","0,0,0;0,0,1;1,0,0;1/2;60,0,0"},
                new[]{"a-terminal","Fixed A terminal frame","60,0,100;1,0,0;0,1,0;0,0,1"}, new[]{"b-terminal","Fixed B terminal frame","60,0,40;0,0,1;0,-1,0;1,0,0"},
                new[]{"a-body","Optional A body center;teeth","none;none"}, new[]{"b-body","Optional B body center;teeth","none;none"},
                new[]{"keepouts","World boxes: id;min;max | ...",""}, new[]{"limits","Whole max bodies,shafts,total teeth","34,32,139264"}, new[]{"bounds","Optional world bounds min;max",""},
                new[]{"clearance","Require cross clearance: 0 / 1","1"}, new[]{"resources","Retain max unique,bytes,origins,details","64,8388608,512,256"},
                new[]{"validation-policy","Pitch validation: legacy / refined","legacy"}, new[]{"inspect-tuple","Tuple ordinal for fresh comparison","0"},
                new[]{"pitch-query","Optional original pair query JSON file",""}
            };
            for (var i = 0; i < rows.Length; i++) Field(content, rows[i][0], rows[i][1], rows[i][2], 8 + i * 33, true);
            Field(panel, "file", "Project directory (Save/Load)", "", 714, false); Field(panel, "export", "Export original mechanism file", "", 747, false); Field(panel, "root", "Exact unwrapped root turns", "0", 780, false);
            Button(panel, "generate", "Generate", 14, 819, 112, Generate); Button(panel, "cancel", "Cancel", 134, 819, 90, () => { Controller.Cancel(); Clear(); });
            Button(panel, "select-next", "Select next", 232, 819, 112, SelectNext); Button(panel, "apply", "Apply root", 352, 819, 112, () => Apply(Rational.Parse(Inputs["root"].text)));
            Button(panel, "play", "Play/Pause", 472, 819, 130, TogglePlay);
            Button(panel, "save", "Save As", 14, 860, 103, () => Save(false)); Button(panel, "save-update", "Save", 125, 860, 77, () => Save(true)); Button(panel, "load", "Load", 210, 860, 77, Load);
            Button(panel, "rebuild", "Selected Rebuild", 295, 860, 149, Rebuild); Button(panel, "regenerate", "Goal Regenerate", 452, 860, 150, () => { Controller.BeginRegeneration(); Clear(); });
            Button(panel, "export", "Export mechanism", 14, 901, 145, Export); Button(panel, "view", "Other side", 165, 901, 105, () => { side = -side; Fit(); });
            Button(panel, "compare-pitch", "Compare tuple", 278, 901, 124, ComparePitch); Button(panel, "next-pitch", "Next pair", 410, 901, 95, NextPitch); Button(panel, "pitch-query", "Pair query", 513, 901, 89, LoadPitchQuery);
            status = Label(panel, "Ready. No artifact is selected implicitly.", 14, 943, 590, 98, 13);
            // The scene camera clears only its viewport. This opaque panel must repaint
            // the other viewport; otherwise old glyphs accumulate in the backbuffer.
            var readbackPanel = GearInvestAuthoringWidgets.Box(canvasObject.transform, "Goal readback viewport", 620, 710, 980, 340, new Color(.055f, .07f, .1f));
            readbackPanel.gameObject.AddComponent<RectMask2D>();
            var readbackScroll = readbackPanel.gameObject.AddComponent<ScrollRect>(); readbackScroll.viewport = readbackPanel; readbackScroll.horizontal = false; readbackScroll.movementType = ScrollRect.MovementType.Clamped;
            readback = Label(readbackPanel, "No validated selection. Generate, then select a stable candidate + origin.", 20, 12, 935, 315, 14); readbackScroll.content = readback.rectTransform;
            var viewObject = new GameObject("Goal mechanical view"); viewObject.transform.SetParent(transform, false); View = viewObject.AddComponent<GearInvestOrientedMechanismView>();
            PairView = new GameObject("Goal pitch proof view").AddComponent<GearInvestPitchPairView>(); PairView.transform.SetParent(transform, false);
        }

        private void Update()
        {
            Controller.Pump();
            if (observed != Controller.Generation) { observed = Controller.Generation; selectedIndex = -1; if (Controller.Selected != null) AdoptSelection(); else ShowReadback(); }
            if (Playing && Artifact != null) { RootTurns = playBase + new Rational((long)((Time.unscaledTime - playStart) * 1000), 8000); View.Apply(RootTurns); ShowReadback(); }
        }
        private void LateUpdate()
        {
            // EventSystem buttons may run after Update. Render flags from this frame's
            // final controller state, alongside the selection/readback they describe.
            Notice = Controller.Notice; status.text = Notice + (Controller.State == LayoutAuthoringState.Generating ? "\nprocessed=" + Controller.ProcessedProgress : "") +
                "\nCached Load=" + Controller.IsCachedLoad + " | selected rebuild=" + SelectedRebuilt + " | goal regenerated=" + Controller.Reproduced;
        }
        private void OnDestroy() { Controller?.Dispose(); }
        private void Edited() { if (populating) return; Controller.InvalidRawInput("Input edited; old result and selection invalidated. Generate to validate the new domain."); Clear(); observed = null; }
        private void ClearPitch() { pitchPairs = Array.Empty<PitchPairProof>(); pitchIndex = -1; SelectedPitchPair = null; PitchComparison = null; PairView?.Clear(); if (View != null) View.gameObject.SetActive(true); }
        private void Clear() { ClearPitch(); Playing = false; Artifact = null; SelectedRebuilt = false; selectedIndex = -1; View.Clear(); if (readback != null) readback.text = "No validated selection. Prior geometry was cleared."; }
        private void Generate() { Clear(); Controller.Edit(BuildGoal()); Controller.BeginGeneration(); }

        public OrientedTwoOutputGoal BuildGoal()
        {
            var mount = Parts(Inputs["mount"].text, ';', 5); var count = Integers(Inputs["limits"].text); if (count.Length != 3) throw new FormatException("Whole limits require three integers.");
            var resource = Integers(Inputs["resources"].text); if (resource.Length != 4) throw new FormatException("Four retention limits required.");
            var policy = (OrientedTurnedPolicy)Enum.Parse(typeof(OrientedTurnedPolicy), Inputs["b-policy"].text, false);
            var clearance = Inputs["clearance"].text; if (clearance != "0" && clearance != "1") throw new FormatException("Clearance policy is 0 or 1.");
            var keepouts = string.IsNullOrWhiteSpace(Inputs["keepouts"].text) ? Array.Empty<OrientedKeepOut>() : Inputs["keepouts"].text.Split('|').Select(s => { var v = Parts(s, ';', 3); return new OrientedKeepOut(v[0], new ExactEnvelope3(Point(v[1]), Point(v[2]))); }).ToArray();
            ExactEnvelope3? bounds = null; if (!string.IsNullOrWhiteSpace(Inputs["bounds"].text)) { var v = Parts(Inputs["bounds"].text, ';', 2); bounds = new ExactEnvelope3(Point(v[0]), Point(v[1])); }
            OrientedGoalOutput Target(string key, OrientedOutputRole role)
            { var fixedBody = Parts(Inputs[key + "-body"].text, ';', 2); return new OrientedGoalOutput(loadedGoal?.Outputs.Single(o => o.Role == role).Key ?? key.ToUpperInvariant(), role,
                Frame(Inputs[key + "-terminal"].text), Rational.Parse(Inputs["target-" + key].text), fixedBody[0] == "none" ? (ExactVector3?)null : Point(fixedBody[0]), fixedBody[1] == "none" ? (int?)null : Integer(fixedBody[1])); }
            return new OrientedTwoOutputGoal(Frame(Inputs["root-frame"].text), Frame(Inputs["output-frame"].text), Point(mount[0]), Point(mount[1]), Point(mount[2]), Rational.Parse(mount[3]),
                new[] { Target("a", OrientedOutputRole.ParallelBranch), Target("b", OrientedOutputRole.TurnedBranch) }, Integers(Inputs["input-teeth"].text), Integers(Inputs["output-teeth"].text),
                Inputs["scales"].text.Split(',').Select(Rational.Parse), Sources("a"), policy, policy == OrientedTurnedPolicy.NoneOnly ? null : Sources("b"), clearance == "1", keepouts,
                new OrientedGoalLimits(count[0], count[1], count[2], bounds), new OrientedGoalSearchOptions(Integer(Inputs["budget"].text), Integer(Inputs["cap"].text), resource[0], resource[1], resource[2], resource[3]),
                profile: Inputs["validation-policy"].text == "refined" ? OrientedGoalProfile.RefinedId : Inputs["validation-policy"].text == "legacy" ? OrientedGoalProfile.Id : throw new FormatException("Pitch validation policy must be legacy or refined."), unattachedTurnedMatingStation: Point(mount[4]));
        }
        private OrientedGoalSource[] Sources(string key)
        {
            var prior = key == "a" ? loadedGoal?.ParallelSources : loadedGoal?.TurnedSources;
            var controls = new[] { "source-" + key, key + "-teeth", key + "-ports", key + "-records" }.Concat(key == "a" ? new[] { "a-z" } : Array.Empty<string>()).ToArray();
            if (prior != null && prior.Count > 0 && controls.All(k => loadedFields.TryGetValue(k, out var value) && value == Inputs[k].text)) return prior.ToArray();
            var poses = Placements(key); var ports = Parts(Inputs[key + "-ports"].text, ',', 2); var result = new List<OrientedGoalSource>();
            void Add(byte[] bytes, string input, string output) { var a = sdk.ReadArtifact(bytes); result.Add(new OrientedGoalSource(bytes, a.CandidateId, a.ArtifactHash, input, output, poses)); }
            var paths = Inputs["source-" + key].text;
            if (!string.IsNullOrWhiteSpace(paths)) foreach (var record in BoundedParts(paths, '|', OrientedGoalProfile.MaxSourcesPerBranch))
            { var f = Parts(record, ';', 3); if (new FileInfo(f[0]).Length > OrientedGoalProfile.MaxSourceBytes) throw new FormatException("Source byte limit."); Add(File.ReadAllBytes(f[0]), f[1], f[2]); }
            else if (prior != null && prior.Count > 0 && loadedFields.TryGetValue(key + "-teeth", out var oldTeeth) && oldTeeth == Inputs[key + "-teeth"].text)
                foreach (var s in prior) Add(s.SourceBytes, ports[0], ports[1]);
            else foreach (var entry in BoundedParts(Inputs[key + "-teeth"].text, '|', OrientedGoalProfile.MaxSourcesPerBranch))
            { var teeth = Integers(entry); if (teeth.Length != 2) throw new FormatException("Source teeth = input,output pairs."); Add(BuildSpur(teeth[0], teeth[1]), ports[0], ports[1]); }
            return result.ToArray();
        }
        private OrientedGoalPlacement[] Placements(string key)
        {
            if (key == "a" && !string.IsNullOrWhiteSpace(Inputs["a-z"].text)) return BoundedParts(Inputs["a-z"].text, ',', OrientedGoalProfile.MaxPlacements).Select(s => { var f = OrientedFrame.Identity.At(new ExactVector3(0, 0, Rational.Parse(s))); return new OrientedGoalPlacement(f, f); }).ToArray();
            return BoundedParts(Inputs[key + "-records"].text, '|', OrientedGoalProfile.MaxPlacements).Select(s => { var parts = Parts(s, '@', 3); return new OrientedGoalPlacement(Frame(parts[0]), Frame(parts[1]), Rational.Parse(parts[2])); }).ToArray();
        }
        private byte[] BuildSpur(int input, int output)
        {
            if (input <= 0 || output <= 0 || input > OrientedTransmissionProfile.MaxTeeth || output > OrientedTransmissionProfile.MaxTeeth) throw new FormatException("Source teeth exceed the declared bounded profile.");
            var k = new KinematicSpecification("input", new[] { new RotationalDof("input", true), new RotationalDof("output") }, new[] { new ExternalGearCoupling("mesh", "input", "output", input, output) });
            var spatial = new SpatialMechanism(new[] { new SpatialAxis("input-axis", 0, 0), new SpatialAxis("output-axis", checked(input + output), 0) },
                new[] { new SpatialBody("input-gear", SpatialBodyKind.Gear, "input-axis", "input", 0, input, input), new SpatialBody("output-gear", SpatialBodyKind.Gear, "output-axis", "output", 0, output, output) },
                new[] { new SpatialContact("contact", SpatialContactKind.ExternalGearMesh, "mesh", "input-gear", "output-gear") });
            var generated = sdk.Generate(new LowLevelMechanicalSpecification("oriented-example-spur-" + input + "-" + output + "-0", k, spatial));
            if (!generated.IsSuccess) throw new FormatException("Public source generation rejected the input."); return sdk.WriteArtifact(generated.Candidates.Single()).Bytes;
        }
        private void SelectNext()
        {
            var candidates = Controller.Generation?.Candidates; if (candidates == null || candidates.Count == 0) throw new FormatException("No validated returned candidates.");
            var choices = candidates.SelectMany(c => c.Origins.Select(o => new { Candidate = c.CandidateId, Origin = o.Id })).ToArray();
            selectedIndex = (selectedIndex + 1) % choices.Length; Controller.Select(choices[selectedIndex].Candidate, choices[selectedIndex].Origin); AdoptSelection();
        }
        private void AdoptSelection()
        {
            ClearPitch();
            if (Controller.Selected == null) { Clear(); return; } Artifact = sdk.ExportOrientedGoalSelection(Controller.Snapshot()).Artifact; SelectedRebuilt = false;
            View.Build(Artifact.Mechanism); Fit(); RootTurns = Controller.Snapshot().RootTurns; Inputs["root"].text = RootTurns.ToString(); View.Apply(RootTurns); ShowReadback();
        }
        private void Apply(Rational root) { if (Artifact == null) throw new FormatException("Select a validated candidate first."); Playing = false; RootTurns = root; Inputs["root"].text = root.ToString(); Controller.SetRootTurns(root); View.Apply(root); ShowReadback(); }
        private void TogglePlay() { if (Artifact == null) throw new FormatException("Select a validated candidate first."); Playing = !Playing; if (Playing) { playBase = RootTurns; playStart = Time.unscaledTime; } else Controller.SetRootTurns(RootTurns); }
        private void Save(bool overwrite) { if (Playing) TogglePlay(); sdk.SaveOrientedGoalProject(Controller.Snapshot(), Inputs["file"].text, overwrite); }
        private void Load() { Clear(); Controller.Load(Inputs["file"].text); Populate(Controller.Snapshot().Goal); observed = Controller.Generation; if (Controller.Selected != null) AdoptSelection(); else ShowReadback(); }
        private void Rebuild() { var rebuilt = sdk.RebuildOrientedGoalSelection(Controller.Snapshot()); Artifact = rebuilt.Artifact; SelectedRebuilt = true; View.Build(Artifact.Mechanism); Fit(); View.Apply(RootTurns); ShowReadback(); }
        private void Export() { var selected = sdk.ExportOrientedGoalSelection(Controller.Snapshot()); sdk.SaveOrientedTwoOutputArtifact(selected.Artifact, Inputs["export"].text); }
        private void Populate(OrientedTwoOutputGoal g)
        {
            populating = true; loadedGoal = g;
            try
            {
                void Set(string key, string value) { Inputs[key].text = value; }
                Set("root-frame", FrameText(g.RootShaftFrame)); Set("output-frame", FrameText(g.BevelOutputShaftFrame)); Set("mount", PointText(g.Apex) + ";" + PointText(g.InputConeDirection) + ";" + PointText(g.OutputConeDirection) + ";" + g.InnerParameter + ";" + PointText(g.UnattachedTurnedMatingStation));
                Set("input-teeth", string.Join(",", g.InputTeeth)); Set("output-teeth", string.Join(",", g.OutputTeeth)); Set("scales", string.Join(",", g.PitchScales)); Set("b-policy", g.TurnedPolicy.ToString());
                Set("budget", g.Options.WorkBudget.ToString(CultureInfo.InvariantCulture)); Set("cap", g.Options.ReturnedCandidateCap.ToString(CultureInfo.InvariantCulture));
                Set("resources", g.Options.MaxUniqueCandidates + "," + g.Options.MaxRetainedBytes + "," + g.Options.MaxOrigins + "," + g.Options.MaxDetails);
                Set("limits", g.Limits.MaxBodies + "," + g.Limits.MaxShafts + "," + g.Limits.MaxTotalTeeth); Set("bounds", g.Limits.WholeBounds.HasValue ? PointText(g.Limits.WholeBounds.Value.Min) + ";" + PointText(g.Limits.WholeBounds.Value.Max) : "");
                Set("keepouts", string.Join("|", g.KeepOuts.Select(k => k.Id + ";" + PointText(k.Envelope.Min) + ";" + PointText(k.Envelope.Max)))); Set("clearance", g.RequireCrossComponentClearance ? "1" : "0");
                Set("validation-policy", g.Profile == OrientedGoalProfile.RefinedId ? "refined" : "legacy");
                foreach (var key in new[] { "a", "b" })
                {
                    var target = g.Outputs.Single(o => o.Role == (key == "a" ? OrientedOutputRole.ParallelBranch : OrientedOutputRole.TurnedBranch));
                    Set(key + "-terminal", FrameText(target.TerminalFrame)); Set("target-" + key, target.RequiredTransfer.ToString()); Set(key + "-body", (target.FixedBodyCenter.HasValue ? PointText(target.FixedBodyCenter.Value) : "none") + ";" + (target.FixedBodyTeeth?.ToString(CultureInfo.InvariantCulture) ?? "none"));
                    var sources = key == "a" ? g.ParallelSources : g.TurnedSources; Set("source-" + key, "");
                    if (sources.Count > 0)
                    {
                        Set(key + "-teeth", string.Join("|", sources.Select(s => { var m = sdk.ReadArtifact(s.SourceBytes).Candidate; return m.Spatial.Bodies.Single(b => b.DofId == s.InputDofId).ToothCount + "," + m.Spatial.Bodies.Single(b => b.DofId == s.OutputDofId).ToothCount; })));
                        Set(key + "-ports", sources[0].InputDofId + "," + sources[0].OutputDofId);
                        Set(key + "-records", string.Join("|", sources[0].Placements.Select(p => FrameText(p.Pose) + "@" + FrameText(p.InputMatingFrame) + "@" + p.PreferencePenalty)));
                    }
                }
                Set("a-z", g.ParallelSources.All(s => s.Placements.All(p => p.Pose.Origin.X == 0 && p.Pose.Origin.Y == 0 && p.Pose.X == ExactVector3.UnitX && p.Pose.Y == ExactVector3.UnitY && p.Pose.Z == ExactVector3.UnitZ && FrameText(p.Pose) == FrameText(p.InputMatingFrame) && p.PreferencePenalty == 0)) ? string.Join(",", g.ParallelSources[0].Placements.Select(p => p.Pose.Origin.Z)) : "");
                loadedFields.Clear(); foreach (var item in Inputs) loadedFields[item.Key] = item.Value.text;
            }
            finally { populating = false; }
        }
        private void ShowReadback()
        {
            var result = Controller.Generation; var text = result == null ? "No current generated result." :
                "STATUS " + result.Status + "  processed=" + result.ProcessedTuples + "/" + result.TheoreticalTuples + "  remaining=" + result.RemainingTuples + "  accepted=" + result.AcceptedTuples + "  rejected=" + result.RejectedTuples + "  unresolved=" + result.InconclusiveTuples +
                "\n" + result.RankingGuarantee + "  return cap truncated=" + result.ResultTruncated + "  (scroll for full readback)";
            if (result != null) text += "\nPOLICY " + (result.Profile == OrientedGoalProfile.RefinedId ? PitchClearancePolicy.Refined : PitchClearancePolicy.Legacy) + " | primary pair predicates=" + result.PairPredicateCount;
            if (result != null) text += "\n" + string.Join(" | ", result.Details.Where(d => d.Verdict == OrientedTupleVerdict.Rejected || d.Verdict == OrientedTupleVerdict.Inconclusive).Select(d => d.Reason).Distinct().Take(2));
            if (Artifact != null)
            {
                var m = Artifact.Mechanism; var frame = sdk.EvaluateOrientedTwoOutput(m, RootTurns);
                text += "\nSELECTED " + Artifact.CandidateId.Substring(0, 16) + " root=" + RootTurns + " shafts/bodies/contacts=" + m.Shafts.Count + "/" + m.Bodies.Count + "/" + m.Contacts.Count;
                var origin = Controller.SelectedOrigin; var tuple = origin.Tuple;
                text += "\norigin=" + origin.Id.Substring(0, 16) + " bevel=" + tuple.InputTeeth + ":" + tuple.OutputTeeth + " scale=" + tuple.Scale + " A=" + tuple.ParallelSourceId.Substring(0, 12) + "@" + tuple.ParallelPlacement + " B=" + (tuple.TurnedSourceId == null ? "None" : tuple.TurnedSourceId.Substring(0, 12) + "@" + tuple.TurnedPlacement);
                foreach (var output in frame.Outputs) text += "\n" + output.Binding.Key + " requested=" + Controller.Snapshot().Goal.Outputs.Single(o => o.Key == output.Binding.Key).RequiredTransfer + " actual port=" + output.PortCoefficient + " shaft=" + output.ShaftCoefficient + " turns=" + output.PortTurns +
                    "\n  fixed terminal=" + m.Ports.Single(p => p.Id == output.Binding.PortId).Frame.Origin + "  actual body=" + m.Bodies.Single(b => b.Id == output.Binding.BodyId).MountingFrame.Origin;
                var score = Controller.Selected.Metrics;
                text += "\nScore: bodies=" + score.Bodies + " volume=" + score.Volume + " max extent=" + score.MaximumExtent + " preference=" + score.PreferencePenalty + " teeth=" + score.TotalTeeth;
                text += "\nGeometric ideal mechanism. Physical engineering NotPerformed.";
            }
            else text += "\nNo implicit rank-0 adoption. Use Select next.";
            if (result != null) text += "\n" + string.Join("\n", result.Candidates.Select((c, i) => (i + 1) + ": " + c.CandidateId.Substring(0, 12) + " bodies=" + c.Metrics.Bodies + " AABB=" + c.Metrics.Volume + " origins=" + c.Origins.Count));
            if (SelectedPitchPair != null)
            {
                var p = SelectedPitchPair;
                text = "FRESH PAIR INSPECTION — refined proof preview, not implicit mechanism acceptance\n" + PitchComparison +
                    "\n" + p.A.Id + " / " + p.B.Id + " | " + p.Broad + " -> " + p.Verdict + " | required=" + p.Required +
                    "\n" + p.Method + " | boundary equality=" + p.BoundaryEquality + " | witness=" + (p.Witness?.ToString() ?? "not emitted; exact decision only") +
                    "\n" + string.Join("; ", p.Terms.Select(t => t.Key + "=" + t.Value)) +
                    "\nBlue=A, amber=B, gray=AABBs, pink=exact section ring, green=witness. Display markers/lines have thickness.\nShapes: filled closed disk, zero thickness; finite cone LATERAL surface, NO caps. Physical solids/dynamics: NotPerformed.\n\n" + text;
            }
            readback.text = text;
            readback.rectTransform.sizeDelta = new Vector2(935, Mathf.Max(315, readback.preferredHeight + 16));
        }
        private void ComparePitch()
        {
            Playing = false; var g = BuildGoal(); var ordinal = Integer(Inputs["inspect-tuple"].text);
            OrientedGoalTupleInspection Inspect(string policy) { var compiled = sdk.CompileOrientedTwoOutputSearchPlan(g.WithClearancePolicy(policy)); if (!compiled.IsSuccess) throw new FormatException(compiled.Detail); return sdk.InspectOrientedGoalTuple(compiled.Plan, OrientedGoalCompiler.Decode(compiled.Plan, ordinal)); }
            var old = Inspect(PitchClearancePolicy.Legacy); var refined = Inspect(PitchClearancePolicy.Refined);
            PitchComparison = "Tuple " + ordinal + ": same domain, separate actual composers. Legacy required non-Pass=" + old.Validation.Checks.Count(c => c.Required && c.Verdict != OrientedCheckVerdict.Pass) +
                "; refined required non-Pass=" + refined.Validation.Checks.Count(c => c.Required && c.Verdict != OrientedCheckVerdict.Pass) + ". Cached search is not relabeled.";
            pitchPairs = refined.Validation.PitchProofs.OrderBy(p => p.Broad == PitchBroadRelation.StrictlySeparated ? 1 : 0).ThenBy(p => p.Verdict == OrientedCheckVerdict.Pass ? 1 : 0).ThenBy(p => p.PairId, StringComparer.Ordinal).ToArray();
            if (pitchPairs.Length == 0) throw new FormatException("No pair refinement performed: inspect earlier required geometry/target failure."); pitchIndex = -1; NextPitch();
        }
        private void NextPitch()
        {
            if (pitchPairs.Length == 0) throw new FormatException("Compare a tuple first."); pitchIndex = (pitchIndex + 1) % pitchPairs.Length; SelectedPitchPair = pitchPairs[pitchIndex];
            View.gameObject.SetActive(false); PairView.Build(SelectedPitchPair); Fit(); ShowReadback();
        }
        private void LoadPitchQuery()
        {
            var path = Inputs["pitch-query"].text; if (new FileInfo(path).Length > CanonicalOrientedTwoOutputJson.MaxDocumentBytes) throw new FormatException("Pair query byte limit.");
            var proof = sdk.ClassifyPitchClearanceQuery(File.ReadAllBytes(path)); Playing = false; PitchComparison = "Original standalone pair query, freshly classified by the SDK; not a whole mechanism result.";
            pitchPairs = new[] { proof }; pitchIndex = -1; NextPitch();
        }
        private void Fit() { var pair = PairView != null && PairView.HasPair; if (!pair && !View.HasMechanism) return; var center = pair ? PairView.ViewCenter : View.ViewCenter; var radius = pair ? PairView.ViewRadius : View.ViewRadius;
            var direction = new Vector3(1.6f, 1.3f, 1.8f) * side; sceneCamera.transform.position = center + direction.normalized * radius * 3;
            sceneCamera.transform.LookAt(center); sceneCamera.orthographicSize = radius; sceneCamera.nearClipPlane = .1f; sceneCamera.farClipPlane = radius * 8 + 100; }
        private static int Integer(string s) => int.Parse(s, CultureInfo.InvariantCulture);
        private static int[] Integers(string s) => s.Split(',').Select(Integer).ToArray();
        private static string[] BoundedParts(string s, char separator, int maximum) { var parts = s.Split(separator); if (parts.Length > maximum) throw new FormatException("Input domain exceeds " + maximum + " entries."); return parts; }
        private static string[] Parts(string s, char separator, int count) { var result = s.Split(separator); if (result.Length != count) throw new FormatException("Expected " + count + " parts separated by " + separator); return result; }
        private static ExactVector3 Point(string text) { var v = Parts(text, ',', 3); return new ExactVector3(Rational.Parse(v[0]), Rational.Parse(v[1]), Rational.Parse(v[2])); }
        private static OrientedFrame Frame(string text) { var f = Parts(text, ';', 4); return new OrientedFrame(Point(f[0]), Point(f[1]), Point(f[2]), Point(f[3])); }
        private static string PointText(ExactVector3 p) => p.X + "," + p.Y + "," + p.Z;
        private static string FrameText(OrientedFrame f) => PointText(f.Origin) + ";" + PointText(f.X) + ";" + PointText(f.Y) + ";" + PointText(f.Z);
        private Text Label(Transform parent, string text, float x, float y, float width, float height, int size) => GearInvestAuthoringWidgets.Label(font, parent, text, x, y, width, height, size, new Color(.9f, .94f, 1));
        private void Field(Transform parent, string key, string title, string value, float y, bool invalidates)
        {
            Label(parent, title, 14, y + 2, 256, 27, 12); var box = GearInvestAuthoringWidgets.Box(parent, "input-" + key, 274, y, 322, 29, new Color(.14f, .17f, .22f));
            var field = box.gameObject.AddComponent<InputField>(); field.textComponent = Label(box, "", 5, 1, 312, 27, 13); field.characterLimit = 32768; field.text = value;
            if (invalidates) field.onValueChanged.AddListener(_ => Edited()); Inputs.Add(key, field);
        }
        private void Button(Transform parent, string key, string title, float x, float y, float width, Action action)
        {
            var box = GearInvestAuthoringWidgets.Box(parent, "button-" + key, x, y, width, 32, new Color(.14f, .28f, .4f)); var button = box.gameObject.AddComponent<Button>(); button.targetGraphic = box.GetComponent<Image>();
            Label(box, title, 4, 4, width - 8, 26, 13); button.onClick.AddListener(() => { try { action(); } catch (Exception e) { Controller.InvalidRawInput("INPUT/OPERATION ERROR: " + e.Message); Clear(); if (!(e is FormatException || e is ArgumentException || e is IOException || e is ArithmeticException || e is UnauthorizedAccessException || e is ArtifactFormatException)) Debug.LogException(e); } }); Buttons.Add(key, button);
        }
    }
}
