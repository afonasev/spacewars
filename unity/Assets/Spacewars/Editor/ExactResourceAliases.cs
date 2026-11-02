using System;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Spacewars.Editor
{
    // Authoring aliases only; the Player holds normal Unity object references.
    public static class ExactResourceAliases
    {
        [Serializable] sealed class Entry { public string source, target; }
        [Serializable] sealed class Catalog { public Entry[] aliases; }
        static Catalog catalog;
        public static string Resolve(string path)
        {
            if(File.Exists(path))return path;
            catalog ??= JsonUtility.FromJson<Catalog>(File.ReadAllText("Assets/Spacewars/Editor/ExactResourceAliases.json"));
            var entry=catalog.aliases.FirstOrDefault(a=>a.source==path);
            return entry!=null&&File.Exists(entry.target)?entry.target:path;
        }
        public static string PackedMaskPath(string path,byte[] pixels)
        {
            string shared=Resolve(path);
            // A future rework with different pixels receives its own asset.
            return shared!=path&&File.ReadAllBytes(shared).SequenceEqual(pixels)?shared:path;
        }
    }
}
