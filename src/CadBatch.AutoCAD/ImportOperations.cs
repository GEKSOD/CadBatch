using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadBatch.Core;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private static string UniqueFile(string folder,string name,string extension)
        {
            if(!Path.IsPathRooted(folder))throw new ArgumentException("Выберите абсолютную папку результата.");
            Directory.CreateDirectory(folder);string stem=OutputPlanner.Sanitize(name);if(stem.Length>120)stem=stem.Substring(0,120);
            string path=Path.Combine(folder,stem+extension);int n=2;
            while(File.Exists(path)||Directory.Exists(path))path=Path.Combine(folder,stem+" ("+(n++)+")"+extension);
            if(path.Length>240)throw new ArgumentException("Слишком длинный путь результата.");return path;
        }
        private static void MergeDrawings(CadOperationRequest request,CadOperationResult result,CancellationToken cancellation)
        {
            if(request.Files.Length==0||request.Columns<1||!Numeric.NonNegative(request.Spacing)||!Numeric.Positive(request.Scale))throw new ArgumentException("Проверьте файлы, колонки, интервал и масштаб.");
            string path=UniqueFile(request.Folder,request.NamePattern,".dwg");string temporary=Path.Combine(request.Folder,"cadbatch-tmp-"+Guid.NewGuid().ToString("N")+".dwg");
            try
            {
                using(var output=new Database(true,true))
                {
                    double x=0,y=0,rowHeight=0;int index=0;UnitsValue? units=null;
                    foreach(string file in request.Files)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        using(var source=new Database(false,true))
                        {
                            if(!string.Equals(Path.GetExtension(file),".dwg",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Для сборки выберите DWG: "+file);
                            source.ReadDwgFile(file,FileOpenMode.OpenForReadAndAllShare,true,"");source.CloseInput(true);
                            if(units.HasValue&&units.Value!=source.Insunits)throw new InvalidOperationException("Разные INSUNITS: "+file+". Перед сборкой приведите исходники к одинаковым единицам.");
                            units=source.Insunits;output.Insunits=source.Insunits;
                            ObjectIdCollection ids;Extents3d? bounds=null;
                            using(var tr=source.TransactionManager.StartTransaction())
                            {
                                var bt=(BlockTable)tr.GetObject(source.BlockTableId,OpenMode.ForRead);var model=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
                                ids=new ObjectIdCollection(model.Cast<ObjectId>().ToArray());
                                foreach(ObjectId id in model)if(tr.GetObject(id,OpenMode.ForRead) is Entity e)
                                    try{var extent=e.GeometricExtents;if(bounds.HasValue){var b=bounds.Value;b.AddExtents(extent);bounds=b;}else bounds=extent;}catch(Autodesk.AutoCAD.Runtime.Exception){result.Details.Add(Path.GetFileName(file)+": габарит объекта "+id.Handle+" недоступен.");}
                                foreach(ObjectId id in bt)
                                {
                                    var b=(BlockTableRecord)tr.GetObject(id,OpenMode.ForRead);
                                    if(b.IsFromExternalReference&&!string.IsNullOrWhiteSpace(b.PathName))
                                    {
                                        if(!Path.IsPathRooted(b.PathName)){b.UpgradeOpen();b.PathName=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(file)!,b.PathName));}
                                        result.Details.Add("Внешняя ссылка остаётся связанной: "+b.PathName);
                                    }
                                }
                                tr.Commit();
                            }
                            if(ids.Count==0||!bounds.HasValue)throw new InvalidOperationException("Нет геометрии модели: "+file);
                            ObjectId container;
                            using(var tr=output.TransactionManager.StartTransaction())
                            {
                                var bt=(BlockTable)tr.GetObject(output.BlockTableId,OpenMode.ForWrite);var block=new BlockTableRecord{Name="CB_SOURCE_"+(index+1).ToString("000")};container=bt.Add(block);tr.AddNewlyCreatedDBObject(block,true);tr.Commit();
                            }
                            source.WblockCloneObjects(ids,container,new IdMapping(),DuplicateRecordCloning.MangleName,false);
                            var ext=bounds.Value;double width=Math.Max(1,ext.MaxPoint.X-ext.MinPoint.X)*request.Scale,height=Math.Max(1,ext.MaxPoint.Y-ext.MinPoint.Y)*request.Scale;
                            using(var tr=output.TransactionManager.StartTransaction())
                            {
                                var bt=(BlockTable)tr.GetObject(output.BlockTableId,OpenMode.ForRead);var model=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);
                                var reference=new BlockReference(new Point3d(x-ext.MinPoint.X*request.Scale,y-ext.MinPoint.Y*request.Scale,0),container){ScaleFactors=new Scale3d(request.Scale)};
                                model.AppendEntity(reference);tr.AddNewlyCreatedDBObject(reference,true);tr.Commit();
                            }
                            result.Details.Add((index+1)+". "+file+" → отдельный блок; ресурсы изолированы MangleName; INSUNITS="+source.Insunits);
                            rowHeight=Math.Max(rowHeight,height);index++;x+=width+request.Spacing;
                            if(index%request.Columns==0){x=0;y-=rowHeight+request.Spacing;rowHeight=0;}
                        }
                    }
                    cancellation.ThrowIfCancellationRequested();output.SaveAs(temporary,DwgVersion.AC1032);
                }
                File.Move(temporary,path);result.Files.Add(path);result.Changed=request.Files.Length;
                result.Details.Add("Исходные DWG не сохранены и не изменены. Сборка включает модель; исходные Layout не переносятся. Растры/подложки остаются внешними зависимостями.");
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        private static void InsertRasters(CadOperationRequest request,CadOperationResult result,CancellationToken cancellation)
        {
            if(request.Files.Length==0||!Numeric.Positive(request.Scale)||request.Columns<1||!Numeric.NonNegative(request.Spacing))throw new ArgumentException("Проверьте файлы, ширину изображения, колонки и интервал.");
            var doc=ActiveDocument();var point=AskPoint(doc,"Укажите нижний левый угол первой картинки");if(!point.HasValue){result.Cancelled=true;return;}
            using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction())
            {
                ObjectId dictId=RasterImageDef.GetImageDictionary(doc.Database);if(dictId.IsNull)dictId=RasterImageDef.CreateImageDictionary(doc.Database);
                var images=(DBDictionary)tr.GetObject(dictId,OpenMode.ForWrite);var space=(BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId,OpenMode.ForWrite);
                double x=point.Value.X,y=point.Value.Y,rowHeight=0;int index=0;
                foreach(string file in request.Files)
                {
                    cancellation.ThrowIfCancellationRequested();var definition=new RasterImageDef{SourceFileName=Path.GetFullPath(file)};definition.Load();
                    images.SetAt("CB_IMAGE_"+Guid.NewGuid().ToString("N"),definition);tr.AddNewlyCreatedDBObject(definition,true);
                    var image=new RasterImage();image.SetDatabaseDefaults(doc.Database);image.ImageDefId=definition.ObjectId;
                    if(image.ImageWidth<=0||image.ImageHeight<=0)throw new InvalidOperationException("Не удалось определить размер растра: "+file);
                    double width=request.Scale,height=width*image.ImageHeight/image.ImageWidth;
                    image.Orientation=new CoordinateSystem3d(new Point3d(x,y,point.Value.Z),new Vector3d(width,0,0),new Vector3d(0,height,0));
                    space.AppendEntity(image);tr.AddNewlyCreatedDBObject(image,true);RasterImage.EnableReactors(true);image.AssociateRasterDef(definition);
                    result.Details.Add((index+1)+". "+file+" → "+width+" × "+height+" единиц DWG (внешняя ссылка на изображение).");
                    index++;x+=width+request.Spacing;rowHeight=Math.Max(rowHeight,height);if(index%request.Columns==0){x=point.Value.X;y-=rowHeight+request.Spacing;rowHeight=0;}
                }
                tr.Commit();result.Changed=request.Files.Length;doc.Editor.Regen();
            }
        }
    }
}
