using System.Collections.Generic;
using GearInvest.Core;
using GearInvest.Engine;
using GearInvest.Serialization.Json;

namespace GearInvest;

public sealed partial class GearInvestSdk
{
    public MechanicalModeState StartMechanicalModes(MechanicalModeDefinition definition,Rational initialPlanetPortTurns)
    {_ = FinalizeWindingConnection(definition.Connection);return MechanicalModeEngine.Start(definition,initialPlanetPortTurns);}
    public MechanicalModeAdvance AdvanceMechanicalModes(MechanicalModeDefinition definition,MechanicalModeState state,MechanicalModeRequest request)=>MechanicalModeEngine.Advance(definition,state,request);
    public MechanicalModeRecordingArtifact RecordMechanicalModes(WindingConnectionArtifact source,MechanicalModeDefinition definition,Rational initialPlanetPortTurns,IEnumerable<MechanicalModeRequest> requests)
    {var admitted=ReadWindingConnectionArtifact(source.Bytes);return MechanicalModesJson.Write(admitted,MechanicalModeRecording.Rebuild(definition,initialPlanetPortTurns,requests));}
    public MechanicalModeRecordingArtifact ReadMechanicalModeRecording(byte[] bytes)
    {var artifact=MechanicalModesJson.Read(bytes);_ = ReadWindingConnectionArtifact(artifact.Source.Bytes);return artifact;}
    public byte[] ExportMechanicalModeReplay(MechanicalModeRecordingArtifact artifact)=>MechanicalModesJson.WriteReplay(ReadMechanicalModeRecording(artifact.Bytes));
    public MechanicalModeRecordingArtifact RebuildMechanicalModeReplay(byte[] bytes)
    {var artifact=MechanicalModesJson.ReadReplay(bytes);_ = ReadWindingConnectionArtifact(artifact.Source.Bytes);return artifact;}
    public void SaveMechanicalModeRecording(MechanicalModeRecordingArtifact artifact,string path)=>SaveMechanicalSidecar(ReadMechanicalModeRecording(artifact.Bytes).Bytes,path);
    public MechanicalModeRecordingArtifact LoadMechanicalModeRecording(string path)=>ReadMechanicalModeRecording(PortableProjectStorage.ReadBounded(path,WindingConnectionJson.MaxBytes));
}
