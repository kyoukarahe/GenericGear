using System.Linq;
using GearInvest.Engine;
using static GearInvest.Serialization.Json.CanonicalOrientedJson;

namespace GearInvest.Serialization.Json;

public sealed class DifferentialSuffixArtifact
{
    private readonly byte[] bytes;
    internal DifferentialSuffixArtifact(DifferentialSuffixDefinition s,byte[] bytes) { Source=s;this.bytes=(byte[])bytes.Clone();ArtifactId=Hash(bytes); }
    public DifferentialSuffixDefinition Source { get; }
    public string ArtifactId { get; }
    public byte[] Bytes => (byte[])bytes.Clone();
}

public static class DifferentialSuffixJson
{
    public const string Format="gear-invest.differential-spur-suffix";
    public static DifferentialSuffixArtifact Write(DifferentialSuffixDefinition s,DifferentialArtifact parent)
    {
        var a=DifferentialSuffixAnalyzer.Prepare(s);Require(a.IsValid && parent.Request.RequestId==s.Parent.RequestId,"Suffix or parent not admitted.");
        var bytes=WindingConnectionJson.Encode(w=>
        {
            WindingConnectionJson.Start(w,Format);w.WriteString("profile",DifferentialSuffixDefinition.Profile);w.WritePropertyName("source");WindingConnectionJson.Suffix(w,s);
            w.WriteBase64String("parentArtifactUtf8",parent.Bytes);w.WritePropertyName("outputLaw");WindingConnectionJson.Law(w,a.OutputLaw!);w.WriteEndObject();
        });return new(s,bytes);
    }
    public static DifferentialSuffixArtifact Read(byte[] bytes)=>MechanicalAuthoringJson.Guard(()=>
    {
        using var doc=WindingConnectionJson.Parse(bytes);var p=doc.RootElement;Header(p,Format);
        var s=WindingConnectionJson.Suffix(p.GetProperty("source"));var parent=DifferentialJson.ReadArtifact(WindingConnectionJson.Raw(p,"parentArtifactUtf8"));var fresh=Write(s,parent);
        Require(bytes.SequenceEqual(fresh.Bytes),"Suffix current source/owner/registration/law differs.");return fresh;
    });
}
