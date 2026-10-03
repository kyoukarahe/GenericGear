using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;
using GearInvest.WindingExample;
using Xunit;

namespace GearInvest.Tests;

public sealed class MechanicalRuntimeTests
{
    private static readonly GearInvestSdk Sdk = GearInvestSdk.CreateDefault();
    private static readonly WindingConnectionArtifact Source = Sdk.FinalizeWindingConnection(Example.Create());
    private static RuntimeRequest Request(MechanicalRuntimeSession s, string id, Rational q, params MechanicalConnectionEventKind[] events)
    {
        var state = s.Current; var needed = MechanicalModeEngine.RequiredInputPorts(state.Definition, state.Mechanical.Mode);
        return new(id, state.SessionId, state.Definition.DefinitionId, state.StateId, state.Epoch, state.Revision, new[] {
            new RuntimeSegment(new(q, needed.Select(p => new KeyValuePair<string,Rational>(p, p == Source.Source.SunPortId ? new Rational(2,25) : new Rational(3,10)))),
                events.Select((e,i) => new RuntimeEvent(id+"/e"+i,state.EventCursor+i+1,i,e))) });
    }
    [Fact]
    public void Checkpoint_current_source_restore_continues_modes_without_old_requests()
    {
        using var s = Sdk.PrepareMechanicalRuntime(Source.Bytes,"restore-test",new Rational(3,10));
        var kinds = new[] { MechanicalConnectionEventKind.Release, MechanicalConnectionEventKind.Capture, MechanicalConnectionEventKind.LockWorldCarrier, MechanicalConnectionEventKind.LockPlanetRelative, MechanicalConnectionEventKind.CaptureNegative, MechanicalConnectionEventKind.Release };
        var step = 0;
        foreach (var kind in kinds)
        {
            var request = Request(s,kind.ToString()+step++,new Rational(13,200),kind); var result = s.Advance(request); Assert.True(result.IsAccepted,result.Status);
            var checkpoint = s.Checkpoint(); using var restored = Sdk.RestoreMechanicalRuntime(checkpoint.Bytes);
            Assert.Equal(s.Current.StateId,restored.Current.StateId); Assert.Equal(checkpoint.Bytes,restored.Checkpoint().Bytes);
            Assert.Equal("AlreadyApplied",restored.Advance(request).Status);
            var next = Request(s,"next-"+kind,new Rational(3,50)); var a = s.Prepare(next); var b = restored.Prepare(next);
            Assert.Equal(a.Status,b.Status); Assert.Equal(a.Candidate?.StateId,b.Candidate?.StateId);
        }
    }
    [Fact]
    public void Long_run_discards_dead_provenance_not_physical_turns_or_retry_floor()
    {
        using var s = Sdk.PrepareMechanicalRuntime(Source.Bytes,"long-run",0);
        RuntimeRequest? old = null; var sizes = new List<int>(); var maxLive = 0;
        for (var i=0;i<4352;i++)
        {
            // Unique source q values exceed 64 lifetime causes; released capture replaces obsolete H.
            var q = new Rational(400000+i,10000000);
            var r = Request(s,"r-"+i,q,MechanicalConnectionEventKind.Release,MechanicalConnectionEventKind.Capture);
            // Release then capture at the same instant does not replace H. Every second request starts Released instead.
            if(i%2==0) r=Request(s,"r-"+i,q,MechanicalConnectionEventKind.Release);
            else r=Request(s,"r-"+i,q,MechanicalConnectionEventKind.Capture);
            old ??= r; var outcome=s.Advance(r); Assert.True(outcome.IsAccepted,outcome.Status); maxLive=Math.Max(maxLive,s.Current.Witnesses.Count);
            if(i%256==255)
            {
                var bytes=s.Checkpoint().Bytes;sizes.Add(bytes.Length);using var restore=Sdk.RestoreMechanicalRuntime(bytes);Assert.Equal(s.Current.StateId,restore.Current.StateId);
                Assert.Equal("AlreadyApplied",restore.Advance(r).Status);
            }
        }
        Assert.Equal(new BigInteger(4352),s.Current.Revision);Assert.Equal(new BigInteger(17),s.Current.Epoch);Assert.Equal(16,s.Current.Ledger.Count);
        Assert.InRange(maxLive,1,3);Assert.True(sizes.Max()-sizes.Min()<3000);Assert.Equal("StaleSnapshot",s.Advance(old!).Status);
        Assert.Equal(Source.Source.DefinitionId,s.Current.Definition.Connection.DefinitionId);
    }
    [Fact]
    public void Prepared_cancel_and_duplicate_commit_never_publish_partial_or_false_rollback()
    {
        using var s=Sdk.PrepareMechanicalRuntime(Source.Bytes,"atomic",0);var initial=s.Current;
        var request=Request(s,"new",new Rational(1,20),MechanicalConnectionEventKind.Release);
        var prepared=s.Prepare(request);Assert.Same(initial,s.Current);Assert.True(prepared.IsAccepted);
        Assert.Equal("Accepted",s.Commit(prepared));Assert.Equal("StaleSnapshot",s.Commit(prepared));
        var checkpoint=s.Checkpoint().Bytes;var bad=Request(s,"boundary",new Rational(1,2));Assert.False(s.Advance(bad).IsAccepted);Assert.Equal(checkpoint,s.Checkpoint().Bytes);
        var conflict=Request(s,"new",new Rational(1,25));Assert.Equal("IdempotencyConflict",s.Advance(conflict).Status);
        s.Dispose();Assert.Throws<ObjectDisposedException>(()=>s.Prepare(request));
    }
    [Theory]
    [InlineData("profile")][InlineData("owner")][InlineData("branch")][InlineData("h")][InlineData("latent")][InlineData("cursor")][InlineData("epoch")][InlineData("lock")][InlineData("source")][InlineData("mode")]
    public void Rehashed_checkpoint_tampering_is_rebuilt_not_trusted(string field)
    {
        using var s=Sdk.PrepareMechanicalRuntime(Source.Bytes,"tamper",0);
        Assert.True(s.Advance(Request(s,"release",new Rational(1,20),MechanicalConnectionEventKind.Release)).IsAccepted);
        Assert.True(s.Advance(Request(s,"capture",new Rational(3,50),MechanicalConnectionEventKind.Capture,MechanicalConnectionEventKind.LockWorldCarrier)).IsAccepted);
        var env=JsonNode.Parse(s.Checkpoint().Bytes)!;var p=JsonNode.Parse(Convert.FromBase64String(env["payloadUtf8"]!.GetValue<string>()))!;var state=p["state"]!;
        switch(field)
        {
            case "profile":p["runtimeProfile"]="wrong";break;
            case "source":p["sourceArtifactId"]=new string('0',64);break;
            case "mode":state["mode"]="not-a-mode";break;
            case "owner":state["frame"]!["coordinates"]![0]!["shaftId"]="fake-owner";break;
            case "branch":state["frame"]!["winding"]!["driverContact"]=99;break;
            case "h":state["couplingOffset"]!["constant"]!["numerator"]="77";break;
            case "latent":state["witnesses"]![0]!["driverTurns"]!["numerator"]="2";break;
            case "cursor":state["eventCursor"]="99";break;
            case "epoch":state["epoch"]="7";break;
            case "lock":state["lockReference"]!["constant"]!["numerator"]="88";break;
        }
        var raw=Encoding.UTF8.GetBytes(p.ToJsonString());env["payloadUtf8"]=Convert.ToBase64String(raw);env["payloadId"]=Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        Assert.ThrowsAny<Exception>(()=>Sdk.RestoreMechanicalRuntime(Encoding.UTF8.GetBytes(env.ToJsonString())));
    }
    [Fact]
    public void Legacy_import_replays_then_uses_new_explicit_identity_without_rewriting_old_artifact()
    {
        var old=ModeExample.Create(Sdk,Source);var bytes=old.Bytes;using var s=Sdk.ImportMechanicalRecording(bytes,"imported");
        Assert.Equal(old.Recording.Final.Frame.SnapshotId,s.Current.Mechanical.Frame.SnapshotId);
        Assert.Equal(bytes,old.Bytes);using var restored=Sdk.RestoreMechanicalRuntime(s.Checkpoint().Bytes);Assert.Equal(s.Current.StateId,restored.Current.StateId);
    }
    [Fact]
    public void Checkpoint_store_CAS_and_interrupted_pending_preserve_last_valid_state()
    {
        var directory=Path.Combine(Path.GetTempPath(),"GearInvest-runtime-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);var path=Path.Combine(directory,"checkpoint.json");
        using var s=Sdk.PrepareMechanicalRuntime(Source.Bytes,"store",0);var first=s.Checkpoint();var id=Sdk.SaveMechanicalCheckpoint(first,path,null);
        File.WriteAllText(path+".pending","interrupted partial write");using(var loaded=Sdk.LoadMechanicalCheckpoint(path))Assert.Equal(s.Current.StateId,loaded.Current.StateId);
        Assert.True(s.Advance(Request(s,"next",new Rational(1,20))).IsAccepted);var next=s.Checkpoint();
        Assert.Throws<InvalidOperationException>(()=>Sdk.SaveMechanicalCheckpoint(next,path,null));Assert.Equal(first.Bytes,File.ReadAllBytes(path));
        Assert.Equal(next.ArtifactId,Sdk.SaveMechanicalCheckpoint(next,path,id));using(var loaded=Sdk.LoadMechanicalCheckpoint(path))Assert.Equal(s.Current.StateId,loaded.Current.StateId);
        // Small test-owned files only; unrelated user/package directories are never targeted.
        Directory.Delete(directory,true);
    }
    [Fact]
    public void Wire_counters_are_exact_strings_above_JS_safe_integer_and_rejected_atomically_as_stale()
    {
        using var s=Sdk.PrepareMechanicalRuntime(Source.Bytes,"large-counters",0);var old=s.Current;
        var revision=BigInteger.Pow(10,40)+1;var good=Request(s,"stale-huge",0);
        var request=new RuntimeRequest(good.Id,good.SessionId,good.DefinitionId,good.ExpectedStateId,revision/256,revision,good.Segments);
        var decoded=MechanicalRuntimeJson.ReadRequest(MechanicalRuntimeJson.WriteRequest(request));Assert.Equal(revision,decoded.Revision);Assert.Equal("StaleSnapshot",s.Advance(decoded).Status);Assert.Same(old,s.Current);
        Assert.Throws<ArgumentException>(()=>MechanicalRuntime.Counter(BigInteger.Pow(10,128)));
    }
    [Fact]
    public void Changing_locks_cannot_silently_discard_independent_live_causes_at_capacity()
    {
        using var s=Sdk.PrepareMechanicalRuntime(Source.Bytes,"active-capacity",0);var refused=false;var maximum=0;
        for(var i=0;i<150;i++)
        {
            var before=s.Current;var q=new Rational(4000+i,100000);var request=Request(s,"lock-"+i,q,i%2==0?MechanicalConnectionEventKind.LockWorldCarrier:MechanicalConnectionEventKind.LockPlanetRelative);
            var result=s.Advance(request);maximum=Math.Max(maximum,s.Current.Witnesses.Count);
            if(!result.IsAccepted){Assert.Equal("ProvenanceResourceLimit",result.Status);Assert.Same(before,s.Current);refused=true;break;}
        }
        Assert.True(refused);Assert.InRange(maximum,50,64);
        using var restored=Sdk.RestoreMechanicalRuntime(s.Checkpoint().Bytes);Assert.Equal(s.Current.StateId,restored.Current.StateId);
    }
    [Fact]
    public void Null_transaction_token_is_not_a_successful_commit_or_late_cancel()
    {
        using var host=new MechanicalRuntimeHost(Sdk);
        var loaded=JsonNode.Parse(host.Dispatch(System.Text.Json.JsonSerializer.Serialize(new{op="load",sourceUtf8=Encoding.UTF8.GetString(Source.Bytes),sessionId="tokens",initialPlanet="0"})))!;
        var state=loaded["snapshot"]!["state"]!["stateId"]!.GetValue<string>();
        foreach(var op in new[]{"commit","cancel"})
        {
            var result=JsonNode.Parse(host.Dispatch("{\"op\":\""+op+"\",\"token\":null}"))!;
            Assert.Equal("InvalidInput",result["status"]!.GetValue<string>());Assert.False(result["committed"]!.GetValue<bool>());
            Assert.Equal(state,JsonNode.Parse(host.Dispatch("{\"op\":\"snapshot\"}"))!["snapshot"]!["state"]!["stateId"]!.GetValue<string>());
        }
        var tooMany=new string('x',WindingConnectionJson.MaxBytes+1);
        Assert.Equal("ResourceLimit",JsonNode.Parse(host.Dispatch(tooMany))!["status"]!.GetValue<string>());
    }
}
