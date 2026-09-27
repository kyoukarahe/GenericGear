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
    /// <summary>Optional ordinary inspector. Files, input controls and its presentation clock belong to this adapter, never the mechanical core.</summary>
    public sealed class GearInvestOrientedInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public GearInvestOrientedMechanismView View { get; private set; }
        public OrientedArtifact Artifact { get; private set; }
        public OrientedTransmissionResult LastGeneration { get; private set; }
        public string Notice { get; private set; } = "Enter fixed pair data or Load a saved oriented assembly.";
        public bool Playing { get; private set; }
        public bool Rebuilt { get; private set; }
        public bool CachedLoad { get; private set; }
        public Rational RootTurns { get; private set; }
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private Text detail, channels, status;
        private Camera cameraView; private float viewSide = 1; private Rational playBase; private float playStart;
        private OrientedAssemblyRequest loadedRequest;
        private Font font;

        private void Awake()
        {
            Application.runInBackground = true;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("Oriented EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            cameraView = Camera.main; if (cameraView == null) { var go = new GameObject("Oriented camera"); go.tag = "MainCamera"; cameraView = go.AddComponent<Camera>(); }
            cameraView.cullingMask &= ~(1 << 5);
            cameraView.clearFlags = CameraClearFlags.SolidColor; cameraView.backgroundColor = new Color(.075f, .085f, .12f); cameraView.orthographic = true;
            cameraView.rect = new Rect(470f / 1600, 290f / 1050, 1130f / 1600, 760f / 1050);
            // Match the existing authoring consumers: a depth-only UI camera and opaque panels outside the scene viewport.
            var uiObject = new GameObject("Oriented UI camera"); uiObject.transform.SetParent(transform, false);
            var uiCamera = uiObject.AddComponent<Camera>(); uiCamera.cullingMask = 1 << 5; uiCamera.clearFlags = CameraClearFlags.Depth;
            uiCamera.depth = cameraView.depth + 10; uiCamera.transform.position = new Vector3(0, 0, -1000); uiCamera.nearClipPlane = .1f; uiCamera.farClipPlane = 30;
            var canvasGo = new GameObject("Oriented Inspector Controls", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)); canvasGo.transform.SetParent(transform, false);
            canvasGo.layer = 5;
            var canvas = canvasGo.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = uiCamera; canvas.planeDistance = 10;
            var scaler = canvasGo.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1600, 1050); scaler.matchWidthOrHeight = .5f;
            var panel = GearInvestAuthoringWidgets.Box(canvasGo.transform, "Mechanical inputs", 0, 0, 470, 1050, new Color(.055f, .07f, .1f, 1));
            GearInvestAuthoringWidgets.Box(canvasGo.transform, "Mechanical readback background", 470, 760, 1130, 290, cameraView.backgroundColor);
            Label(panel, "ORIENTED TRANSMISSION", 18, 14, 432, 38, 21);
            Field(panel, "z1", "Input teeth", "10", 62, 136); Field(panel, "z2", "Output teeth", "20", 62, 360);
            Field(panel, "reverse", "Output coordinate (+1 / -1)", "1", 106, 340);
            Field(panel, "center1", "Fixed C1 (x,y,z)", "0,0,20", 150, 280);
            Field(panel, "center2", "Fixed C2 (x,y,z)", "10,0,0", 194, 280);
            Button(panel, "pair", "Create fixed pair", 18, 240, 210, CreatePair);
            Button(panel, "view", "View opposite side", 238, 240, 214, () => { viewSide = -viewSide; FitCamera(); });
            Field(panel, "root", "Exact root turns", "0", 284, 280);
            Button(panel, "apply", "Apply root", 18, 328, 100, () => Apply(Rational.Parse(Inputs["root"].text)));
            Button(panel, "zero", "0", 128, 328, 55, () => SetRoot(0)); Button(panel, "quarter", "1/4", 193, 328, 75, () => SetRoot(new Rational(1, 4)));
            Button(panel, "negative", "-1/4", 278, 328, 75, () => SetRoot(new Rational(-1, 4))); Button(panel, "play", "Play / Pause", 18, 371, 210, TogglePlayback);
            Button(panel, "rebuild", "Rebuild saved request", 238, 371, 214, Rebuild);
            Label(panel, "External oriented artifact: Save As never overwrites", 18, 417, 434, 24, 14);
            Field(panel, "file", "", "", 443, 18, 434);
            Button(panel, "load", "Load + validate", 18, 486, 210, Load); Button(panel, "save", "Save As", 238, 486, 214, Save);
            Field(panel, "port-x", "Upstream mating station delta X", "0", 532, 360, 90);
            Button(panel, "port", "Apply explicit port edit / compose", 18, 575, 434, EditPort);
            status = Label(panel, Notice, 18, 620, 434, 78, 16);
            detail = Label(panel, "Validation is per domain.\nTooth/shaft/bearing solids, dynamics and manufacturing: NotPerformed.", 18, 708, 434, 312, 14);
            channels = Label(canvasGo.transform, "No mechanical result adopted.", 500, 768, 1080, 270, 16);
            var viewObject = new GameObject("Oriented mechanical view"); viewObject.transform.SetParent(transform, false); View = viewObject.AddComponent<GearInvestOrientedMechanismView>();
        }
        private void Update()
        {
            if (!Playing || Artifact == null) return;
            // Presentation clock samples an absolute root; no integration or local kinematic solver.
            var milliseconds = (long)((Time.unscaledTime - playStart) * 1000);
            RootTurns = playBase + new Rational(milliseconds, 8000); View.Apply(RootTurns); Readback();
        }
        private void CreatePair()
        {
            var sign = Rational.Parse(Inputs["reverse"].text); if (sign != 1 && sign != -1) throw new FormatException("Output coordinate sign must be +1 or -1.");
            var inputFrame = OrientedFrame.Identity; var outputFrame = new OrientedFrame(default, ExactVector3.UnitZ, -ExactVector3.UnitY * sign, ExactVector3.UnitX * sign);
            var input = new OrientedShaft("bevel/input", inputFrame, true); var output = new OrientedShaft("bevel/output", outputFrame);
            var pair = new RightAngleBevelRequest(default,
                new BevelGearMount(input, ExactVector3.UnitZ, int.Parse(Inputs["z1"].text, CultureInfo.InvariantCulture), 1, Point(Inputs["center1"].text), new ShaftPort("bevel/input-port", input.Id, inputFrame.At(new ExactVector3(0, 0, 60)))),
                new BevelGearMount(output, ExactVector3.UnitX, int.Parse(Inputs["z2"].text, CultureInfo.InvariantCulture), 1, Point(Inputs["center2"].text), new ShaftPort("bevel/output-port", output.Id, outputFrame.At(new ExactVector3(60, 0, 0)))), new Rational(1, 2));
            loadedRequest = new OrientedAssemblyRequest(pair); Generate(loadedRequest);
        }
        private void Generate(OrientedAssemblyRequest request)
        {
            Playing = false; CachedLoad = false; Rebuilt = false; Artifact = null; View.Clear();
            LastGeneration = sdk.ComposeOrientedTransmission(request); detail.text = Domains(LastGeneration.Validation);
            if (!LastGeneration.IsSuccess) { Notice = LastGeneration.Status + ": " + string.Join("; ", LastGeneration.Validation.Diagnostics.Select(d => d.Message)); status.text = Notice; channels.text = "Rejected: no partial mechanism or stale frame."; return; }
            Adopt(sdk.WriteOrientedArtifact(LastGeneration.Mechanism, request).Artifact); Notice = "Complete fixed realization. Physical engineering not performed."; status.text = Notice;
        }
        private void Adopt(OrientedArtifact artifact)
        {
            if (!sdk.ValidateOrientedArtifact(artifact).IsValid) throw new FormatException("Identity/geometry/source/context validation failed.");
            Artifact = artifact; loadedRequest = artifact.Request; Playing = false; View.Build(artifact.Mechanism); FitCamera(); SetRoot(0); detail.text = Domains(sdk.ValidateOrientedArtifact(artifact).Validation);
            var pair = artifact.Request.Bevel;
            Inputs["z1"].text = pair.Input.Teeth.ToString(CultureInfo.InvariantCulture); Inputs["z2"].text = pair.Output.Teeth.ToString(CultureInfo.InvariantCulture);
            Inputs["reverse"].text = pair.Output.Shaft.Frame.Z.Dot(pair.Output.ConeDirection).ToString();
            Inputs["center1"].text = pair.Input.FixedCenter.ToString().Trim('(', ')'); Inputs["center2"].text = pair.Output.FixedCenter.ToString().Trim('(', ')'); Inputs["port-x"].text = "0";
        }
        private void Load() { var loaded = sdk.LoadOrientedArtifact(Inputs["file"].text); Adopt(loaded); CachedLoad = true; Rebuilt = false; Notice = "Cached Load revalidated. Request rebuild has not run."; status.text = Notice; Readback(); }
        private void Save() { NeedArtifact(); sdk.SaveOrientedArtifact(Artifact, Inputs["file"].text); Notice = "Saved new file; authoritative readback matched."; status.text = Notice; }
        private void Rebuild() { NeedArtifact(); var fresh = sdk.RebuildOrientedAssembly(Artifact); Adopt(fresh.Artifact); Rebuilt = true; CachedLoad = false; Notice = "Fresh request-only rebuild: canonical bytes and identity match."; status.text = Notice; Readback(); Debug.Log("GEARINVEST_ORIENTED_ASSEMBLY_REBUILD_PASS"); }
        private void EditPort()
        {
            if (loadedRequest == null || loadedRequest.Upstream == null) throw new FormatException("Load an assembly with an upstream source before editing its mating port.");
            var original = loadedRequest; var pre = original.Upstream; var port = pre.ConnectionPort;
            var changed = new ShaftPort(port.Id, port.ShaftId, port.Frame.At(port.Frame.Origin + ExactVector3.UnitX * Rational.Parse(Inputs["port-x"].text)), port.PhaseOffset, port.Kind);
            Generate(new OrientedAssemblyRequest(original.Bevel, new PlanarArtifactPlacement(pre.SourceBytes, pre.Pose, pre.InputDofId, pre.OutputDofId, changed), original.Downstream,
                original.AssemblyPose, original.RequestedTransfer, original.RequireCrossComponentClearance, original.KeepOuts));
        }
        private void SetRoot(Rational turns) { Inputs["root"].text = turns.ToString(); Apply(turns); }
        private void Apply(Rational turns) { NeedArtifact(); Playing = false; RootTurns = turns; View.Apply(turns); Readback(); }
        private void TogglePlayback() { NeedArtifact(); Playing = !Playing; if (Playing) { playBase = RootTurns; playStart = Time.unscaledTime; } }
        private void FitCamera()
        { if (View.Mechanism == null) return; var direction = new Vector3(1.4f, 1.05f, 1.8f) * viewSide; cameraView.transform.position = View.ViewCenter + direction.normalized * View.ViewRadius * 3; cameraView.transform.LookAt(View.ViewCenter); cameraView.orthographicSize = View.ViewRadius * .92f; cameraView.nearClipPlane = .1f; cameraView.farClipPlane = View.ViewRadius * 8 + 100; }
        private void Readback()
        {
            if (Artifact == null) return; var m = Artifact.Mechanism; var text = new StringBuilder();
            text.AppendLine("ORIENTED MECHANICAL TRUTH   root=" + RootTurns + "   shafts=" + m.Shafts.Count + " bodies=" + m.Bodies.Count + " contacts=" + m.Contacts.Count + " driver=" + m.RootShaftId);
            text.AppendLine("candidate " + Artifact.CandidateId.Substring(0, 20) + "   cached=" + CachedLoad + " rebuilt=" + Rebuilt);
            foreach (var frame in sdk.EvaluateOrientedMechanism(m, RootTurns)) text.AppendLine(frame.ShaftId + "  turns=" + frame.Turns + "  +axis=" + frame.PositiveAxis + "  world omega/root=" + frame.WorldAngularVelocityPerRoot);
            foreach (var contact in m.Contacts.Where(c => c.Cone != null)) text.AppendLine("apex=" + contact.Cone.Apex + "  Q=" + contact.Cone.OuterContact + "  R^2=" + contact.Cone.ConeDistanceSquared + "  q=" + contact.StoredTransfer);
            foreach (var link in m.Connections) text.AppendLine(link.PortAId + " =shaft= " + link.PortBId + "  signed coordinate=" + link.CoordinateTransfer);
            text.AppendLine("White arrows: positive shaft directions. Cyan: explicit port stations. Yellow: intended generator. Rims/ribs are schematic."); channels.text = text.ToString();
        }
        private static string Domains(GearInvest.Layout.OrientedValidation validation) => string.Join("\n", validation.Checks.GroupBy(c => c.Domain).Select(g => g.Key + ": " +
            (g.Any(c => c.Verdict == GearInvest.Layout.OrientedCheckVerdict.Fail) ? "Fail" : g.Any(c => c.Verdict == GearInvest.Layout.OrientedCheckVerdict.Inconclusive) ? "Inconclusive" : g.First().Verdict.ToString()) + " [" + (g.Any(c => c.Required) ? "required" : "not required") + "]"));
        private void NeedArtifact() { if (Artifact == null) throw new FormatException("Generate or Load a valid oriented artifact first."); }
        private static ExactVector3 Point(string text) { var parts = text.Split(','); if (parts.Length != 3) throw new FormatException("Expected x,y,z exact components."); return new ExactVector3(Rational.Parse(parts[0]), Rational.Parse(parts[1]), Rational.Parse(parts[2])); }
        private Text Label(Transform parent, string text, float x, float y, float w, float h, int size) => GearInvestAuthoringWidgets.Label(font, parent, text, x, y, w, h, size, new Color(.9f, .94f, 1));
        private void Field(Transform parent, string key, string label, string value, float y, float x, float width = 90)
        {
            if (label.Length != 0) Label(parent, label, key == "z2" ? 250 : 18, y, 300, 30, 14);
            var rect = GearInvestAuthoringWidgets.Box(parent, "input-" + key, x, y, width, 31, new Color(.14f, .17f, .22f));
            var input = rect.gameObject.AddComponent<InputField>(); var text = Label(rect, "", 7, 2, width - 14, 27, 15); input.textComponent = text; input.text = value; Inputs.Add(key, input);
        }
        private void Button(Transform parent, string key, string label, float x, float y, float width, Action action)
        {
            var rect = GearInvestAuthoringWidgets.Box(parent, "button-" + key, x, y, width, 34, new Color(.14f, .28f, .4f)); var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = rect.GetComponent<Image>();
            Label(rect, label, 7, 5, width - 14, 27, 15); button.onClick.AddListener(() =>
            {
                try { action(); }
                catch (Exception e)
                {
                    Playing = false; Rebuilt = false; CachedLoad = false; Artifact = null; View.Clear();
                    var expected = e is FormatException || e is ArgumentException || e is ArithmeticException || e is System.IO.IOException || e is UnauthorizedAccessException;
                    Notice = (expected ? "INPUT/FILE ERROR: " : "OPERATION FAILED: ") + e.Message;
                    status.text = Notice; channels.text = "No stale result is displayed.";
                    if (!expected) Debug.LogException(e); // Unexpected SDK failures remain observable, never converted to a successful result.
                }
            }); Buttons.Add(key, button);
        }
    }
}
