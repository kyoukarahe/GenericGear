using System;
using System.Collections.Generic;
using System.Linq;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public WindingDifferentialAnalysis PrepareWindingConnection(WindingDifferentialDefinition source)
    { VerifyDifferentialSource(source.Suffix.Parent.Definition); return WindingDifferentialEngine.Prepare(source); }
    public DifferentialSuffixAnalysis PrepareDifferentialSuffix(DifferentialSuffixDefinition source)
    { VerifyDifferentialSource(source.Parent.Definition); return DifferentialSuffixAnalyzer.Prepare(source); }
    public Rational EvaluateDifferentialSuffix(DifferentialSuffixAnalysis analysis,DifferentialInputSnapshot input)
    { if(!analysis.IsValid)throw new ArgumentException("Admitted suffix required.");var parent=EvaluateDifferential(analysis.Parent,input);return parent.Coordinates[analysis.Source.SourceShaftId]*analysis.Transfer+analysis.Offset; }
    public CarrierDisplayPose DisplayDifferentialSuffix(DifferentialSuffixAnalysis analysis,DifferentialInputSnapshot input)=>DifferentialSuffixAnalyzer.Display(analysis,input);
    public DifferentialSuffixArtifact FinalizeDifferentialSuffix(DifferentialSuffixDefinition source)
    {var parent=TryFinalizeDifferential(source.Parent);if(!parent.IsFinalized)throw new ArgumentException("Parent source not finalizable.");return DifferentialSuffixJson.Write(source,parent.Artifact!);}
    public DifferentialSuffixArtifact ReadDifferentialSuffix(byte[] bytes)
    {var stored=DifferentialSuffixJson.Read(bytes);var fresh=FinalizeDifferentialSuffix(stored.Source);if(!bytes.SequenceEqual(fresh.Bytes))throw new ArtifactFormatException("Suffix rebuild differs.");return fresh;}
    public byte[] WriteWindingConnectionDraft(WindingDifferentialDefinition source)=>WindingConnectionJson.WriteDraft(source);
    public WindingDifferentialDefinition ReadWindingConnectionDraft(byte[] bytes)=>WindingConnectionJson.ReadDraft(bytes);
    public WindingConnectionArtifact FinalizeWindingConnection(WindingDifferentialDefinition source)
    {
        var a=PrepareWindingConnection(source);if(!a.IsValid)throw new ArgumentException("Connected admission: "+string.Join(",",a.Diagnostics));
        var parent=TryFinalizeDifferential(source.Suffix.Parent);if(!parent.IsFinalized)throw new ArgumentException("Parent source not finalizable.");
        return WindingConnectionJson.WriteArtifact(source,parent.Artifact!);
    }
    public WindingConnectionArtifact ReadWindingConnectionArtifact(byte[] bytes)
    {var stored=WindingConnectionJson.ReadArtifact(bytes);var fresh=FinalizeWindingConnection(stored.Source);if(!fresh.Bytes.SequenceEqual(bytes))throw new ArtifactFormatException("Current connected source differs.");return fresh;}
    public ConnectedEvaluation EvaluateWindingConnection(WindingDifferentialAnalysis analysis,WindingDriveInput input)=>WindingDifferentialEngine.Evaluate(analysis,input);
    public ConnectedAdvance AdvanceWindingConnection(WindingDifferentialAnalysis analysis,ConnectedFrame state,IEnumerable<WindingDriveInput> segments)=>WindingDifferentialEngine.Advance(analysis,state,segments);
    public WindingRecordingArtifact RecordWindingConnection(WindingConnectionArtifact source,Rational initialPlanetPortTurns,IEnumerable<IEnumerable<WindingDriveInput>> requests)
    {var admitted=ReadWindingConnectionArtifact(source.Bytes);return WindingConnectionJson.WriteRecording(admitted,WindingDifferentialEngine.Record(admitted.Analysis,initialPlanetPortTurns,requests));}
    public WindingRecordingArtifact ReadWindingRecording(byte[] bytes)
    {var stored=WindingConnectionJson.ReadRecording(bytes);_ = ReadWindingConnectionArtifact(stored.Source.Bytes);return stored;}
    public byte[] ExportWindingReplay(WindingRecordingArtifact recording)=>WindingConnectionJson.WriteReplay(ReadWindingRecording(recording.Bytes));
    public WindingRecordingArtifact RebuildWindingReplay(byte[] bytes)
    {var stored=WindingConnectionJson.ReadReplay(bytes);_ = ReadWindingConnectionArtifact(stored.Source.Bytes);return stored;}
    public void SaveWindingConnection(WindingConnectionArtifact artifact,string path)=>SaveMechanicalSidecar(ReadWindingConnectionArtifact(artifact.Bytes).Bytes,path);
    public WindingConnectionArtifact LoadWindingConnection(string path)=>ReadWindingConnectionArtifact(PortableProjectStorage.ReadBounded(path,WindingConnectionJson.MaxBytes));
    public void SaveWindingRecording(WindingRecordingArtifact recording,string path)=>SaveMechanicalSidecar(ReadWindingRecording(recording.Bytes).Bytes,path);
    public WindingRecordingArtifact LoadWindingRecording(string path)=>ReadWindingRecording(PortableProjectStorage.ReadBounded(path,WindingConnectionJson.MaxBytes));
}
