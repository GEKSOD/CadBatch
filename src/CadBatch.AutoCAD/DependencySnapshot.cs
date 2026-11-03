using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.AutoCAD.DatabaseServices;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private static List<string> DependencySnapshot(Database database,string source)
        {
            var files=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var seen=new HashSet<Database>();
            void Walk(Database db,string path)
            {
                if(!seen.Add(db))return;
                string? Resolve(string reference)
                {
                    if(string.IsNullOrWhiteSpace(reference))return null;
                    string full=Path.IsPathRooted(reference)?reference:Path.Combine(Path.GetDirectoryName(path)??"",reference);
                    if(!File.Exists(full))try{full=HostApplicationServices.Current.FindFile(reference,db,FindFileHint.Default);}catch{ }
                    full=Path.GetFullPath(full);files.Add(full);return full;
                }
                using(var tr=db.TransactionManager.StartOpenCloseTransaction())
                {
                    foreach(ObjectId id in (BlockTable)tr.GetObject(db.BlockTableId,OpenMode.ForRead))
                    {
                        var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);if(!block.IsFromExternalReference&&!block.IsFromOverlayReference)continue;
                        string? full=Resolve(block.PathName);var nested=block.GetXrefDatabase(false);if(nested!=null&&full!=null)Walk(nested,full);
                    }
                    var named=(DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId,OpenMode.ForRead);
                    foreach(string key in new[]{"ACAD_IMAGE_DICT","ACAD_PDFDEFINITIONS","ACAD_DWFDEFINITIONS","ACAD_DGNDEFINITIONS"})
                    {
                        if(!named.Contains(key))continue;foreach(DBDictionaryEntry entry in (DBDictionary)tr.GetObject(named.GetAt(key),OpenMode.ForRead))
                        {
                            var definition=tr.GetObject(entry.Value,OpenMode.ForRead);if(definition is RasterImageDef raster)Resolve(raster.SourceFileName);else if(definition is UnderlayDefinition underlay)Resolve(underlay.SourceFileName);
                        }
                    }
                }
            }
            Walk(database,source);return new List<string>(files);
        }
    }
}
