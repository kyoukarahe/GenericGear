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
    /// <summary>Small exact-form authoring adapter, not a topology editor or a local solver.</summary>
    public sealed class GearInvestOrientedTwoOutputInspector : MonoBehaviour
    {
        public readonly Dictionary<string, InputField> Inputs = new Dictionary<string, InputField>(StringComparer.Ordinal);
        public readonly Dictionary<string, Button> Buttons = new Dictionary<string, Button>(StringComparer.Ordinal);
        public GearInvestOrientedMechanismView View { get; private set; }
        public OrientedTwoOutputArtifact Artifact { get; private set; }
        public OrientedTwoOutputResult LastGeneration { get; private set; }
        public Rational RootTurns { get; private set; }
        public bool Playing { get; private set; }
        public bool CachedLoad { get; private set; }
        public bool Rebuilt { get; private set; }
        public string Notice { get; private set; }
        private readonly GearInvestSdk sdk = GearInvestSdk.CreateDefault();
        private Font font; private Text status, readback; private Camera sceneCamera; private float side = 1, playStart; private Rational playBase;
        private OrientedTwoOutputAssemblyRequest loaded;
        private const string Identity = "0,0,0;1,0,0;0,1,0;0,0,1";
        private void Awake()
        {
            Application.runInBackground = true; font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (FindAnyObjectByType<EventSystem>() == null) new GameObject("Two-output EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            sceneCamera = Camera.main; if (sceneCamera == null) { var go = new GameObject("Two-output camera"); go.tag = "MainCamera"; sceneCamera = go.AddComponent<Camera>(); }
            sceneCamera.cullingMask &= ~(1 << 5); sceneCamera.clearFlags = CameraClearFlags.SolidColor; sceneCamera.backgroundColor = new Color(.075f,.085f,.12f); sceneCamera.orthographic = true;
            sceneCamera.rect = new Rect(600f/1600,340f/1050,1000f/1600,710f/1050);
            var uic = new GameObject("Two-output UI camera").AddComponent<Camera>(); uic.transform.SetParent(transform,false); uic.cullingMask=1<<5; uic.clearFlags=CameraClearFlags.Depth; uic.depth=sceneCamera.depth+10;
            uic.transform.position=new Vector3(0,0,-1000); uic.nearClipPlane=.1f; uic.farClipPlane=30;
            var canvasGo=new GameObject("Two-output controls",typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster)); canvasGo.transform.SetParent(transform,false);canvasGo.layer=5;
            var canvas=canvasGo.GetComponent<Canvas>();canvas.renderMode=RenderMode.ScreenSpaceCamera;canvas.worldCamera=uic;canvas.planeDistance=10;
            var scaler=canvasGo.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;scaler.referenceResolution=new Vector2(1600,1050);scaler.matchWidthOrHeight=.5f;
            var panel=GearInvestAuthoringWidgets.Box(canvasGo.transform,"Exact mechanical inputs",0,0,600,1050,new Color(.055f,.07f,.1f));
            GearInvestAuthoringWidgets.Box(canvasGo.transform,"Readback",600,710,1000,340,sceneCamera.backgroundColor);
            Label(panel,"SHARED SHAFT / TWO OUTPUTS",14,8,570,28,20);
            Label(panel,"Frame = origin ; X ; Y ; Z. Exact fractions; no snap or sign repair.",14,36,574,22,13);
            var rows = new[] {
                new[]{"source-a","Source A file (blank = typed)",""},new[]{"source-b","Source B file (blank = typed)",""},
                new[]{"a-teeth","A spur teeth (in,out)","20,40"},new[]{"b-teeth","B spur teeth (in,out)","10,30"},new[]{"with-b","Attach source B: 0 / 1","0"},
                new[]{"bevel-teeth","Bevel teeth (in,out)","10,20"},new[]{"mount","Apex;coneA;coneB;inner;scale","0,0,0;0,0,1;1,0,0;1/2;1"},
                new[]{"input-frame","Input shaft frame",Identity},new[]{"output-frame","Output shaft frame","0,0,0;0,0,1;0,-1,0;1,0,0"},
                new[]{"center-a","Fixed pinion center","0,0,20"},new[]{"center-b","Fixed bevel wheel center","10,0,0"},
                new[]{"input-port","Bevel input mating port","0,0,100;1,0,0;0,1,0;0,0,1"},new[]{"output-port","Bevel output mating port","60,0,0;0,0,1;0,-1,0;1,0,0"},
                new[]{"a-pose","Source A pose","0,0,100;1,0,0;0,1,0;0,0,1"},new[]{"b-pose","Source B pose","60,0,0;0,0,1;0,-1,0;1,0,0"},
                new[]{"a-port","Source A input port","0,0,100;1,0,0;0,1,0;0,0,1"},new[]{"b-port","Source B input port","60,0,0;0,0,1;0,-1,0;1,0,0"},
                new[]{"a-terminal","A terminal port frame","60,0,100;1,0,0;0,1,0;0,0,1"},new[]{"b-terminal","B terminal port frame","60,0,0;0,0,1;0,-1,0;1,0,0"},
                new[]{"targets","Port targets A,B (blank = none)","-1/2,-1/2"},new[]{"pose","Global assembly frame",Identity},
                new[]{"keepouts","World boxes: id;min;max | ...",""},new[]{"file","Artifact Save As / Load path",""},new[]{"root","Exact unwrapped root turns","0"}
            };
            for(int i=0;i<rows.Length;i++)Field(panel,rows[i][0],rows[i][1],rows[i][2],64+i*29);
            Button(panel,"compose","Compose + validate",14,770,180,Compose);Button(panel,"apply","Apply root",204,770,120,()=>Apply(Rational.Parse(Inputs["root"].text)));
            Button(panel,"play","Play / Pause",334,770,120,TogglePlay);Button(panel,"view","Other side",464,770,122,()=>{side=-side;Fit();});
            Button(panel,"load","Load + validate",14,812,180,Load);Button(panel,"rebuild","Request-only Rebuild",204,812,190,Rebuild);Button(panel,"save","Save As",404,812,182,Save);
            status=Label(panel,"Ready: explicit fixed inputs, one global driver, two named outputs. Physical engineering NotPerformed.",14,863,574,177,14);
            readback=Label(canvasGo.transform,"No mechanism adopted.",620,720,960,320,16);
            var view=new GameObject("Two-output mechanical view");view.transform.SetParent(transform,false);View=view.AddComponent<GearInvestOrientedMechanismView>();
        }
        private void Update(){if(!Playing||Artifact==null)return;RootTurns=playBase+new Rational((long)((Time.unscaledTime-playStart)*1000),8000);View.Apply(RootTurns);Readback();}
        private OrientedTwoOutputAssemblyRequest Request()
        {
            var iFrame=Frame("input-frame");var oFrame=Frame("output-frame");var teeth=Teeth("bevel-teeth");var mount=Inputs["mount"].text.Split(';');if(mount.Length!=5)throw new FormatException("Mount = apex;coneA;coneB;inner;scale.");
            var input=new OrientedShaft(loaded?.Bevel.Input.Shaft.Id??"bevel/input",iFrame,true);var output=new OrientedShaft(loaded?.Bevel.Output.Shaft.Id??"bevel/output",oFrame);
            var pair=new RightAngleBevelRequest(Point(mount[0]),new BevelGearMount(input,Point(mount[1]),teeth[0],Rational.Parse(mount[4]),Point(Inputs["center-a"].text),new ShaftPort(loaded?.Bevel.Input.Port.Id??"bevel/input-port",input.Id,Frame("input-port"))),
                new BevelGearMount(output,Point(mount[2]),teeth[1],Rational.Parse(mount[4]),Point(Inputs["center-b"].text),new ShaftPort(loaded?.Bevel.Output.Port.Id??"bevel/output-port",output.Id,Frame("output-port"))),Rational.Parse(mount[3]),loaded?.Bevel.RequestedTransfer);
            var withB=Inputs["with-b"].text;if(withB!="0"&&withB!="1")throw new FormatException("Attach B is 0 or 1.");
            var a=Source("a",loaded?.ParallelBranch);var b=withB=="1"?Source("b",loaded?.TurnedBranch):null;
            var targets=Inputs["targets"].text.Split(',');if(targets.Length!=2)throw new FormatException("Two port targets separated by comma required.");
            Rational? Target(int i)=>string.IsNullOrWhiteSpace(targets[i])?(Rational?)null:Rational.Parse(targets[i]);
            string TerminalBody(PlanarArtifactPlacement p)=>sdk.ReadArtifact(p.SourceBytes).Candidate.Spatial.Bodies.Single(g=>g.DofId==p.OutputDofId).Id;
            var oldA=loaded?.Outputs.Single(o=>o.Role==OrientedOutputRole.ParallelBranch);var oldB=loaded?.Outputs.Single(o=>o.Role==OrientedOutputRole.TurnedBranch);
            var outs=new[]{new OrientedOutputRequest(oldA?.Key??"A",OrientedOutputRole.ParallelBranch,TerminalBody(a),new ShaftPort(oldA?.TerminalPort.Id??"terminal-port",a.OutputDofId,Frame("a-terminal")),Target(0)),
                new OrientedOutputRequest(oldB?.Key??"B",OrientedOutputRole.TurnedBranch,b==null?"bevel/wheel":TerminalBody(b),
                    new ShaftPort(b==null?(loaded?.TurnedBranch==null?oldB?.TerminalPort.Id??pair.Output.Port.Id:pair.Output.Port.Id):(loaded?.TurnedBranch==null?"terminal-port":oldB.TerminalPort.Id),b==null?output.Id:b.OutputDofId,Frame("b-terminal")),Target(1))};
            var keepouts=string.IsNullOrWhiteSpace(Inputs["keepouts"].text)?Array.Empty<OrientedKeepOut>():Inputs["keepouts"].text.Split('|').Select(s=>{var parts=s.Split(';');if(parts.Length!=3)throw new FormatException("Box = id;min;max.");return new OrientedKeepOut(parts[0],new ExactEnvelope3(Point(parts[1]),Point(parts[2])));}).ToArray();
            return new OrientedTwoOutputAssemblyRequest(pair,a,outs,b,Frame("pose"),loaded?.RequireCrossComponentClearance??true,keepouts);
        }
        private PlanarArtifactPlacement Source(string key,PlanarArtifactPlacement prior)
        {
            var file=Inputs["source-"+key].text;byte[] bytes;
            if(!string.IsNullOrWhiteSpace(file)){if(new FileInfo(file).Length>512*1024)throw new FormatException("Planar source exceeds 512 KiB.");bytes=File.ReadAllBytes(file);}
            else if(prior!=null&&Inputs[key+"-teeth"].text==SourceTeeth(prior))bytes=prior.SourceBytes;
            else {var teeth=Teeth(key+"-teeth");bytes=SpurSource(teeth[0],teeth[1]);}
            var original=sdk.ReadArtifact(bytes);if(!sdk.Validate(original).IsValid)throw new FormatException("Original source is invalid.");var k=original.Candidate.Kinematic;
            var root=k.RootDofId;var terminal=k.Dofs.Single(d=>d.Id!=root&&k.Couplings.Count(c=>c.DriverDofId==d.Id||c.DrivenDofId==d.Id)==1).Id;
            return new PlanarArtifactPlacement(bytes,Frame(key+"-pose"),root,terminal,new ShaftPort(prior?.ConnectionPort.Id??"input-port",root,Frame(key+"-port")));
        }
        private byte[] SpurSource(int input,int output)
        {
            // Ordinary public SDK generation creates new source bytes, never patches an artifact.
            var k=new KinematicSpecification("input",new[]{new RotationalDof("input",true),new RotationalDof("output")},new[]{new ExternalGearCoupling("mesh","input","output",input,output)});
            var spatial=new SpatialMechanism(new[]{new SpatialAxis("input-axis",0,0),new SpatialAxis("output-axis",input+output,0)},
                new[]{new SpatialBody("input-gear",SpatialBodyKind.Gear,"input-axis","input",0,input,input),new SpatialBody("output-gear",SpatialBodyKind.Gear,"output-axis","output",0,output,output)},
                new[]{new SpatialContact("contact",SpatialContactKind.ExternalGearMesh,"mesh","input-gear","output-gear")});
            var result=sdk.Generate(new LowLevelMechanicalSpecification("oriented-example-spur-"+input+"-"+output+"-0",k,spatial));if(!result.IsSuccess)throw new FormatException("Source generation failed.");return sdk.WriteArtifact(result.Candidates.Single()).Bytes;
        }
        private void Compose(){Clear();var request=Request();LastGeneration=sdk.ComposeOrientedTwoOutput(request);if(!LastGeneration.IsSuccess){Notice=LastGeneration.Status+"\n"+string.Join("\n",LastGeneration.Validation.Checks.Where(c=>c.Required&&c.Verdict!=OrientedCheckVerdict.Pass).Select(c=>c.Domain+"/"+c.Subject+": "+c.Verdict));status.text=Notice;return;}Adopt(sdk.WriteOrientedTwoOutputArtifact(LastGeneration.Mechanism,request).Artifact);Notice="Complete: sources preserved, one global solve, two explicit port outputs. Physical engineering NotPerformed.";status.text=Notice;}
        private void Clear(){Playing=false;Artifact=null;Rebuilt=false;CachedLoad=false;View.Clear();readback.text="Rejected/cleared: no partial or stale mechanism.";}
        private void Adopt(OrientedTwoOutputArtifact artifact){if(!sdk.ValidateOrientedTwoOutputArtifact(artifact).IsValid)throw new FormatException("Artifact validation failed.");Artifact=artifact;loaded=artifact.Request;Playing=false;View.Build(artifact.Mechanism);Fit();Apply(0);}
        private void Load(){var artifact=sdk.LoadOrientedTwoOutputArtifact(Inputs["file"].text);Adopt(artifact);Populate();CachedLoad=true;Rebuilt=false;Notice="Cached Load validated; not a request-only Rebuild.";status.text=Notice;Readback();}
        private void Save(){Need();sdk.SaveOrientedTwoOutputArtifact(Artifact,Inputs["file"].text);Notice="Save As and authoritative byte readback passed.";status.text=Notice;}
        private void Rebuild(){Need();var fresh=sdk.RebuildOrientedTwoOutput(Artifact);Adopt(fresh.Artifact);Rebuilt=true;CachedLoad=false;Notice="Fresh request-only Rebuild: full canonical bytes matched.";status.text=Notice;Readback();Debug.Log("GEARINVEST_ORIENTED_TWO_OUTPUT_REBUILD_PASS");}
        private void Populate()
        {
            var r=loaded;var p=r.Bevel;Inputs["source-a"].text="";Inputs["source-b"].text="";Inputs["a-teeth"].text=SourceTeeth(r.ParallelBranch);if(r.TurnedBranch!=null)Inputs["b-teeth"].text=SourceTeeth(r.TurnedBranch);
            Inputs["with-b"].text=r.TurnedBranch==null?"0":"1";Inputs["bevel-teeth"].text=p.Input.Teeth+","+p.Output.Teeth;
            Inputs["mount"].text=PointText(p.Apex)+";"+PointText(p.Input.ConeDirection)+";"+PointText(p.Output.ConeDirection)+";"+p.InnerParameter+";"+p.Input.OuterPitchRadiusPerTooth;
            Inputs["input-frame"].text=FrameText(p.Input.Shaft.Frame);Inputs["output-frame"].text=FrameText(p.Output.Shaft.Frame);Inputs["center-a"].text=PointText(p.Input.FixedCenter);Inputs["center-b"].text=PointText(p.Output.FixedCenter);
            Inputs["input-port"].text=FrameText(p.Input.Port.Frame);Inputs["output-port"].text=FrameText(p.Output.Port.Frame);Inputs["a-pose"].text=FrameText(r.ParallelBranch.Pose);Inputs["a-port"].text=FrameText(r.ParallelBranch.ConnectionPort.Frame);
            if(r.TurnedBranch!=null){Inputs["b-pose"].text=FrameText(r.TurnedBranch.Pose);Inputs["b-port"].text=FrameText(r.TurnedBranch.ConnectionPort.Frame);}
            var a=r.Outputs.Single(o=>o.Role==OrientedOutputRole.ParallelBranch);var b=r.Outputs.Single(o=>o.Role==OrientedOutputRole.TurnedBranch);
            Inputs["a-terminal"].text=FrameText(a.TerminalPort.Frame);Inputs["b-terminal"].text=FrameText(b.TerminalPort.Frame);
            Inputs["targets"].text=(a.RequestedTransfer?.ToString()??"")+","+(b.RequestedTransfer?.ToString()??"");Inputs["pose"].text=FrameText(r.AssemblyPose);
            Inputs["keepouts"].text=string.Join("|",r.KeepOuts.Select(k=>k.Id+";"+PointText(k.Envelope.Min)+";"+PointText(k.Envelope.Max)));
        }
        private string SourceTeeth(PlanarArtifactPlacement source){var m=sdk.ReadArtifact(source.SourceBytes).Candidate;return m.Spatial.Bodies.Single(b=>b.DofId==source.InputDofId).ToothCount+","+m.Spatial.Bodies.Single(b=>b.DofId==source.OutputDofId).ToothCount;}
        private void Apply(Rational root){Need();Playing=false;RootTurns=root;Inputs["root"].text=root.ToString();View.Apply(root);Readback();}
        private void TogglePlay(){Need();Playing=!Playing;if(Playing){playBase=RootTurns;playStart=Time.unscaledTime;}}
        private void Fit(){if(!View.HasMechanism)return;var direction=new Vector3(1.6f,1.3f,1.8f)*side;sceneCamera.transform.position=View.ViewCenter+direction.normalized*View.ViewRadius*3;sceneCamera.transform.LookAt(View.ViewCenter);sceneCamera.orthographicSize=View.ViewRadius;sceneCamera.nearClipPlane=.1f;sceneCamera.farClipPlane=View.ViewRadius*8+100;}
        private void Readback()
        {
            if(Artifact==null)return;var m=Artifact.Mechanism;var frame=sdk.EvaluateOrientedTwoOutput(m,RootTurns);
            readback.text="ONE SHARED INPUT / TWO NAMED OUTPUTS    root="+RootTurns+"\nshafts="+m.Shafts.Count+" bodies="+m.Bodies.Count+" contacts="+m.Contacts.Count+" drivers="+m.Shafts.Count(s=>s.IsPrescribed)+" outputs="+m.Outputs.Count+
                "\ncandidate "+Artifact.CandidateId.Substring(0,24)+"  cached="+CachedLoad+" rebuilt="+Rebuilt+"\n"+
                string.Join("\n",frame.Outputs.Select(o=>o.Binding.Key+" ["+o.Binding.Role+"] "+o.Binding.ShaftId+"\nshaft="+o.ShaftTurns+" (q="+o.ShaftCoefficient+") port="+o.PortTurns+" (q="+o.PortCoefficient+", sign="+o.PortCoordinateSign+")\n+axis="+o.ShaftPositiveAxis+" port +axis="+o.PortPositiveAxis+" world rate/root="+o.WorldAngularVelocityPerRoot))+
                "\nBlue: parallel spur. Green: turned spur. Orange/purple: separate bevel bodies.\nWhite: positive axes / phase. Cyan: explicit ports. Physical solids/dynamics: NotPerformed.";
        }
        private void Need(){if(Artifact==null)throw new FormatException("Compose or Load a valid artifact first.");}
        private int[] Teeth(string key){var p=Inputs[key].text.Split(',');if(p.Length!=2)throw new FormatException("Expected input,output teeth.");return p.Select(s=>int.Parse(s,CultureInfo.InvariantCulture)).ToArray();}
        private static ExactVector3 Point(string text){var p=text.Split(',');if(p.Length!=3)throw new FormatException("Expected exact x,y,z.");return new ExactVector3(Rational.Parse(p[0]),Rational.Parse(p[1]),Rational.Parse(p[2]));}
        private OrientedFrame Frame(string key){var p=Inputs[key].text.Split(';');if(p.Length!=4)throw new FormatException("Frame = origin;X;Y;Z.");return new OrientedFrame(Point(p[0]),Point(p[1]),Point(p[2]),Point(p[3]));}
        private static string PointText(ExactVector3 p)=>p.X+","+p.Y+","+p.Z;
        private static string FrameText(OrientedFrame f)=>PointText(f.Origin)+";"+PointText(f.X)+";"+PointText(f.Y)+";"+PointText(f.Z);
        private Text Label(Transform parent,string text,float x,float y,float w,float h,int size)=>GearInvestAuthoringWidgets.Label(font,parent,text,x,y,w,h,size,new Color(.9f,.94f,1));
        private void Field(Transform parent,string key,string title,string value,float y)
        {Label(parent,title,14,y+2,240,24,13);var box=GearInvestAuthoringWidgets.Box(parent,"input-"+key,254,y,332,26,new Color(.14f,.17f,.22f));var input=box.gameObject.AddComponent<InputField>();input.textComponent=Label(box,"",5,1,322,24,13);input.text=value;Inputs.Add(key,input);}
        private void Button(Transform parent,string key,string title,float x,float y,float width,Action action)
        {var box=GearInvestAuthoringWidgets.Box(parent,"button-"+key,x,y,width,32,new Color(.14f,.28f,.4f));var button=box.gameObject.AddComponent<Button>();button.targetGraphic=box.GetComponent<Image>();Label(box,title,5,4,width-10,26,14);button.onClick.AddListener(()=>{try{action();}catch(Exception e){Clear();Notice="INPUT/OPERATION ERROR: "+e.Message;status.text=Notice;if(!(e is FormatException||e is ArgumentException||e is IOException||e is ArithmeticException||e is UnauthorizedAccessException))Debug.LogException(e);}});Buttons.Add(key,button);}
    }
}
