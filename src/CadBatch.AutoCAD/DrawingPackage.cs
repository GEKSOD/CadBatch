using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using Autodesk.AutoCAD.DatabaseServices;
using CadBatch.Core;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private static void PackageDrawing(CadOperationRequest request,CadOperationResult result,CancellationToken cancellation)
        {
            var doc=ActiveDocument();if(!Path.IsPathRooted(doc.Name)||!File.Exists(doc.Name))throw new InvalidOperationException("Перед упаковкой сохраните DWG в AutoCAD.");string output=UniqueFile(request.Folder,Path.GetFileNameWithoutExtension(doc.Name)+"_комплект",".zip");
            string scratch=Path.Combine(request.Folder,".cadbatch-package-"+Guid.NewGuid().ToString("N")),zipTemp=output+"."+Guid.NewGuid().ToString("N")+".tmp";
            var files=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);var occupied=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var log=new List<string>();int missing=0;
            string Reserve(string full){full=Path.GetFullPath(full);if(files.TryGetValue(full,out var existing))return existing;string name=OutputPlanner.Sanitize(Path.GetFileName(full)),stem=Path.GetFileNameWithoutExtension(name),ext=Path.GetExtension(name);int suffix=2;while(!occupied.Add(name))name=stem+"_"+(suffix++)+ext;files[full]=name;return name;}
            string? ResolveDependency(string reference,string source,Database db,bool font=false)
            {
                if(string.IsNullOrWhiteSpace(reference))return null;
                string candidate=Path.IsPathRooted(reference)?reference:Path.Combine(Path.GetDirectoryName(source)!,reference);
                if(!File.Exists(candidate))try{candidate=HostApplicationServices.Current.FindFile(reference,db,font?FindFileHint.FontFile:FindFileHint.Default);}catch{ }
                if(!File.Exists(candidate)){missing++;log.Add("НЕ НАЙДЕН: "+reference+" (в "+source+")");return null;}return Path.GetFullPath(candidate);
            }
            string CopyDependency(string full,bool drawing)
            {
                cancellation.ThrowIfCancellationRequested();full=Path.GetFullPath(full);if(files.TryGetValue(full,out var already))return already;
                string name=Reserve(full);log.Add(name+" ← "+full);
                if(drawing)
                {
                    using(var database=new Database(false,true)){database.ReadDwgFile(full,FileOpenMode.OpenForReadAndAllShare,true,"");database.CloseInput(true);Rewrite(database,full);database.SaveAs(Path.Combine(scratch,name),DwgVersion.AC1032);}
                }
                else File.Copy(full,Path.Combine(scratch,name),false);
                return name;
            }
            void Rewrite(Database database,string source)
            {
                using(var tr=database.TransactionManager.StartTransaction())
                {
                    var blocks=(BlockTable)tr.GetObject(database.BlockTableId,OpenMode.ForRead);
                    foreach(ObjectId id in blocks)
                    {
                        var block=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);if(!block.IsFromExternalReference&&!block.IsFromOverlayReference)continue;
                        string? full=ResolveDependency(block.PathName,source,database);if(full==null)continue;string relative=CopyDependency(full,full.EndsWith(".dwg",StringComparison.OrdinalIgnoreCase));block.UpgradeOpen();block.PathName=relative;
                    }
                    var named=(DBDictionary)tr.GetObject(database.NamedObjectsDictionaryId,OpenMode.ForRead);
                    foreach(string key in new[]{"ACAD_IMAGE_DICT","ACAD_PDFDEFINITIONS","ACAD_DWFDEFINITIONS","ACAD_DGNDEFINITIONS"})
                    {
                        if(!named.Contains(key))continue;var definitions=(DBDictionary)tr.GetObject(named.GetAt(key),OpenMode.ForRead);
                        foreach(DBDictionaryEntry entry in definitions)
                        {
                            var definition=tr.GetObject(entry.Value,OpenMode.ForRead);string reference=definition is RasterImageDef image?image.SourceFileName:definition is UnderlayDefinition underlay?underlay.SourceFileName:"";
                            string? full=ResolveDependency(reference,source,database);if(full==null)continue;string relative=CopyDependency(full,false);definition.UpgradeOpen();if(definition is RasterImageDef raster)raster.SourceFileName=relative;else if(definition is UnderlayDefinition link)link.SourceFileName=relative;
                        }
                    }
                    if(request.Crossing)
                    {
                        var styles=(TextStyleTable)tr.GetObject(database.TextStyleTableId,OpenMode.ForRead);foreach(ObjectId id in styles)
                        {
                            var style=(TextStyleTableRecord)tr.GetObject(id,OpenMode.ForRead);
                            foreach(bool big in new[]{false,true}){string reference=big?style.BigFontFileName:style.FileName;if(string.IsNullOrWhiteSpace(reference))continue;string? full=ResolveDependency(reference,source,database,true);if(full==null)continue;string relative=CopyDependency(full,false);style.UpgradeOpen();if(big)style.BigFontFileName=relative;else style.FileName=relative;}
                        }
                    }
                    tr.Commit();
                }
            }
            Directory.CreateDirectory(scratch);
            try
            {
                string main=Reserve(doc.Name);log.Add(main+" ← текущая база "+doc.Name);
                using(doc.LockDocument()){doc.Database.ForceWblockDatabaseCopy();using(var copy=doc.Database.Wblock()){Rewrite(copy,doc.Name);copy.SaveAs(Path.Combine(scratch,main),DwgVersion.AC1032);}}
                cancellation.ThrowIfCancellationRequested();log.Insert(0,"CAD Batch "+ProductInfo.Version+". Главный DWG: "+main+". Не найдено зависимостей: "+missing+". Включены Xref, растры, PDF/DWF/DGN; шрифты: "+request.Crossing+". Исходные файлы не изменены.");
                File.WriteAllLines(Path.Combine(scratch,"Состав комплекта.txt"),log,Encoding.UTF8);ZipFile.CreateFromDirectory(scratch,zipTemp,CompressionLevel.Optimal,false);cancellation.ThrowIfCancellationRequested();File.Move(zipTemp,output);result.Files.Add(output);result.Changed=files.Count;result.Details.AddRange(log);
                if(missing>0)result.Errors.Add("Комплект создан с замечаниями: не найдено зависимостей "+missing+". См. «Состав комплекта.txt» внутри ZIP.");
            }
            finally{if(File.Exists(zipTemp))File.Delete(zipTemp);if(Directory.Exists(scratch))Directory.Delete(scratch,true);}
        }
    }
}
