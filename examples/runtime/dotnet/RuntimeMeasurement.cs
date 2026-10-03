using System.Diagnostics;
using System.Text.Json;
using GearInvest;
using GearInvest.Core;
using GearInvest.Engine;

internal static class RuntimeMeasurement
{
    public static void Run(string sourcePath)
    {
        var sdk=GearInvestSdk.CreateDefault();var bytes=File.ReadAllBytes(sourcePath);var timer=Stopwatch.StartNew();
        using var session=sdk.PrepareMechanicalRuntime(bytes,"measurement",new Rational(3,10));var prepareMs=timer.Elapsed.TotalMilliseconds;
        var liveBefore=GC.GetTotalMemory(true);var times=new List<double>();long totalAllocated=0;var maxLive=0;
        for(var i=0;i<4352;i++)
        {
            var state=session.Current;var id="m-"+i;var input=new MechanicalModeInput(new Rational(400000+i,10000000),MechanicalModeEngine.RequiredInputPorts(state.Definition,state.Mechanical.Mode).Select(p=>new KeyValuePair<string,Rational>(p,p==state.Definition.Connection.SunPortId?new Rational(2,25):new Rational(3,10))));
            var request=new RuntimeRequest(id,state.SessionId,state.Definition.DefinitionId,state.StateId,state.Epoch,state.Revision,new[]{new RuntimeSegment(input,new[]{new RuntimeEvent(id+"/e",state.EventCursor+1,0,i%2==0?MechanicalConnectionEventKind.Release:MechanicalConnectionEventKind.Capture)})});
            var allocated=GC.GetAllocatedBytesForCurrentThread();timer.Restart();var result=session.Advance(request);times.Add(timer.Elapsed.TotalMilliseconds);totalAllocated+=GC.GetAllocatedBytesForCurrentThread()-allocated;
            if(!result.IsAccepted)throw new InvalidOperationException(result.Status);maxLive=Math.Max(maxLive,session.Current.Witnesses.Count);
        }
        var liveAfter=GC.GetTotalMemory(true);timer.Restart();var checkpoint=session.Checkpoint();var createMs=timer.Elapsed.TotalMilliseconds;timer.Restart();using var restored=sdk.RestoreMechanicalRuntime(checkpoint.Bytes);var restoreMs=timer.Elapsed.TotalMilliseconds;
        if(restored.Current.StateId!=session.Current.StateId)throw new InvalidOperationException("Restore identity");times.Sort();
        Console.WriteLine(JsonSerializer.Serialize(new{host=".NET8 Win-x64",runtime=Environment.Version.ToString(),count=times.Count,prepareMs,advanceP50Ms=times[times.Count/2],advanceP95Ms=times[(int)(times.Count*.95)],advanceCurrentThreadAllocatedBytes=totalAllocated,managedLiveBefore=liveBefore,managedLiveAfter=liveAfter,managedLiveScope="GC.GetTotalMemory(true), process-managed estimate includes observer/JIT/cache; not native or WASM",maxLiveProvenance=maxLive,checkpointBytes=checkpoint.Bytes.Length,createMs,restoreMs,unmeasured=new[]{"GPU","WASM memory","native allocations"}}));
    }
}
