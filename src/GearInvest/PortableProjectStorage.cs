using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GearInvest.Core;
using GearInvest.Serialization;

namespace GearInvest;

/// <summary>Shared filesystem mechanics only; each project kind owns its typed manifest, content and validation.</summary>
internal static class PortableProjectStorage
{
    private static void Need(bool value, string message) { if (!value) throw new FormatException(message); }
    internal static string Revision(SortedDictionary<string, byte[]> files) => GearRoutingContract.Sha(System.Text.Encoding.UTF8.GetBytes(DiscreteGeometryContract.List(files.Select(f => DiscreteGeometryContract.Pack(f.Key, GearRoutingContract.Sha(f.Value))))));
    internal static void Commit(string root, string revision, SortedDictionary<string, byte[]> files, byte[] manifest)
    {
        var revisionRoot = Resolve(root, "revisions/" + revision); var parent = Path.GetDirectoryName(revisionRoot)!; RejectLinks(parent); Directory.CreateDirectory(parent);
        if (Directory.Exists(revisionRoot))
        { foreach (var f in files) Need(ReadBounded(Resolve(root, "revisions/" + revision + "/" + f.Key + ".json"), f.Value.Length).SequenceEqual(f.Value), "ExistingRevisionMismatch"); }
        else
        {
            var stage = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage); RejectLinks(stage);
            foreach (var f in files) File.WriteAllBytes(Path.Combine(stage, f.Key + ".json"), f.Value);
            foreach (var f in files) Need(ReadBounded(Path.Combine(stage, f.Key + ".json"), f.Value.Length).SequenceEqual(f.Value), "StagedWriteIntegrityFailure");
            Directory.Move(stage, revisionRoot);
        }
        var manifestPath = Resolve(root, "project.json"); var pending = Path.Combine(root, ".manifest-" + Guid.NewGuid().ToString("N") + ".tmp"); File.WriteAllBytes(pending, manifest);
        if (File.Exists(manifestPath)) File.Replace(pending, manifestPath, null); else File.Move(pending, manifestPath);
        Need(ReadBounded(manifestPath, manifest.Length).SequenceEqual(manifest), "ManifestCommitReadbackMismatch");
    }
    internal static byte[] ReadBounded(string path, int max)
    {
        RejectLinks(path); var info = new FileInfo(path); Need(info.Exists && info.Length > 0 && info.Length <= max, "MissingOrOversizedProjectFile:" + path);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read); Need(stream.Length > 0 && stream.Length <= max, "ProjectFileChangedSize"); var bytes = new byte[checked((int)stream.Length)]; int offset = 0;
        while (offset < bytes.Length) { int count = stream.Read(bytes, offset, bytes.Length - offset); Need(count > 0, "TruncatedProjectFile"); offset += count; } return bytes;
    }
    internal static string Root(string directory)
    {
        var root = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Need(root.Length > (Path.GetPathRoot(root)?.Length ?? 0), "ProjectRootCannotBeFilesystemRoot"); RejectLinks(root); return root;
    }
    internal static string Resolve(string root, string relative)
    {
        Need(DiscreteLayoutProjectJson.SafeRelativePath(relative), "UnsafeBundleRelativePath");
        var path = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        Need(path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "ProjectPathEscapesRoot"); RejectLinks(path); return path;
    }
    internal static void RejectLinks(string path)
    {
        for (string? current = Path.GetFullPath(path); current != null; current = Path.GetDirectoryName(current))
            if (File.Exists(current) || Directory.Exists(current)) Need((File.GetAttributes(current) & FileAttributes.ReparsePoint) == 0, "SymlinkOrJunctionNotSupported");
    }
}
