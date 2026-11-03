using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CadBatch.Core;
using CadBatch.UI;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        public async Task<CadOperationResult> RunCadOperationAsync(CadOperationRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation)
        {
            var result=new CadOperationResult();
            try
            {
                await InContext(()=>
                {
                    var previous=AcApp.DocumentManager.MdiActiveDocument;
                    try
                    {
                        if(request.Operation==CadOperation.InsertFrameTemplate){WithUndo(()=>InsertFrameTemplate(request,result,cancellation));return;}
                        if(request.Operation==CadOperation.PackageDrawing){PackageDrawing(request,result,cancellation);return;}
                        if(request.Operation>=CadOperation.RenameLayouts&&request.Operation!=CadOperation.PackageDrawing){LayoutOperation(request,result,cancellation);return;}
                        if(request.Operation==CadOperation.CreateFrame){WithUndo(()=>CreateFrame(request,result));return;}
                        if(request.Operation==CadOperation.InsertRegister){WithUndo(()=>InsertRegister(request,result));return;}
                        if(request.Operation==CadOperation.MergeDrawings){MergeDrawings(request,result,cancellation);return;}
                        if(request.Operation==CadOperation.InsertRasters){WithUndo(()=>InsertRasters(request,result,cancellation));return;}
                        int done=0;
                        foreach(var group in request.Frames.GroupBy(f=>f.Source.DocumentKey))
                        {
                            cancellation.ThrowIfCancellationRequested();var doc=FindDocument(group.First());AcApp.DocumentManager.MdiActiveDocument=doc;
                            using(doc.LockDocument())
                            {
                                if(request.Operation==CadOperation.ExportDwg)
                                {
                                    foreach(var frame in group)
                                    {
                                        cancellation.ThrowIfCancellationRequested();ExportFrameDwg(doc,request.Items.Single(i=>ReferenceEquals(i.Frame,frame)),request.Crossing,result);
                                        progress.Report(new BatchProgress(++done,request.Frames.Count,"Экспорт DWG: "+done));
                                    }
                                }
                                else if(request.Operation==CadOperation.CreateLayouts)using(var undo=new UndoGroup(doc))CreateLayouts(doc,group.ToList(),request,result,cancellation);
                                else if(request.Operation==CadOperation.WriteAttributes)using(var undo=new UndoGroup(doc))WriteAttributes(doc,group.ToList(),request,result,cancellation);
                                else throw new NotSupportedException("Неизвестная операция.");
                            }
                        }
                    }
                    finally{if(previous!=null)AcApp.DocumentManager.MdiActiveDocument=previous;}
                });
            }
            catch(OperationCanceledException){result.Cancelled=true;}
            catch(Exception ex){result.Errors.Add(ex.Message);result.Details.Add(ex.ToString());}
            return result;
        }
        private static Document ActiveDocument()=>AcApp.DocumentManager.MdiActiveDocument??throw new InvalidOperationException("Откройте DWG.");
        private static Point3d? AskPoint(Document doc,string message)
        {
            using var interaction=CadInteraction.Begin();
            doc.Window.Focus();var point=doc.Editor.GetPoint("\n"+message+" (Esc — отмена): ");
            return point.Status==PromptStatus.OK?(Point3d?)point.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem):null;
        }
        private static void CreateLayouts(Document doc,List<DetectedFrame> frames,CadOperationRequest request,CadOperationResult result,CancellationToken cancellation)
        {
            if(frames.Any(f=>!string.Equals(f.Layout,"Model",StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Создание листов выполняется для рамок модели. Исключите строки пространства листа.");
            var manager=LayoutManager.Current;string previous=manager.CurrentLayout;var created=new List<Tuple<string,ObjectId>>();
            try
            {
                using(var tr=doc.Database.TransactionManager.StartTransaction())
                {
                    foreach(var f in frames)ValidateFrame(doc,f,tr);
                    string layer=NonPlotLayer(doc.Database,tr);
                    var layoutNames=new HashSet<string>(((DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId,OpenMode.ForRead)).Cast<DBDictionaryEntry>().Select(e=>e.Key),StringComparer.OrdinalIgnoreCase);
                    foreach(var frame in frames)
                    {
                        cancellation.ThrowIfCancellationRequested();string name=LayoutName(request.NamePattern,frame);string stem=name;int suffix=2;
                        while(layoutNames.Contains(name))name=stem+" ("+(suffix++)+")";layoutNames.Add(name);
                        ObjectId id=manager.CreateLayout(name);created.Add(Tuple.Create(name,id));
                        manager.CurrentLayout=name;
                        var layout=(Layout)tr.GetObject(id,OpenMode.ForWrite);var validator=PlotSettingsValidator.Current;
                        validator.SetPlotConfigurationName(layout,request.Device,null);validator.RefreshLists(layout);
                        var trace=new OperationTrace();var media=PickMedia(layout,validator,frame.PaperWidthMm,frame.PaperHeightMm,true,trace);
                        if(media.IsCarrier||media.Margins>.05)throw new InvalidOperationException(frame.FormatName+": для точного Layout нужна бумага этого размера без полей. Вывод кратного формата в PDF доступен через обычную кнопку PDF.");
                        validator.SetCanonicalMediaName(layout,media.Name);validator.SetPlotPaperUnits(layout,PlotPaperUnit.Millimeters);validator.SetPlotRotation(layout,media.Rotation);
                        validator.SetPlotType(layout,Autodesk.AutoCAD.DatabaseServices.PlotType.Layout);validator.SetPlotOrigin(layout,new Point2d(0,0));validator.SetUseStandardScale(layout,true);validator.SetStdScaleType(layout,StdScaleType.StdScale1To1);
                        layout.PlotPlotStyles=false;layout.PrintLineweights=true;
                        var paper=(BlockTableRecord)tr.GetObject(layout.BlockTableRecordId,OpenMode.ForWrite);
                        // CreateLayout can generate a default floating viewport. Keep only the paper-space viewport.
                        var viewportIds=layout.GetViewports();ObjectId paperViewport=viewportIds.Count>0?viewportIds[0]:ObjectId.Null;
                        foreach(ObjectId entityId in paper)if(entityId!=paperViewport&&tr.GetObject(entityId,OpenMode.ForRead) is Viewport existing&&existing.Number!=1){existing.UpgradeOpen();existing.Erase();}
                        var rectangle=frame.Geometry;double angle=rectangle.Angle;
                        var center=new Point3d(rectangle.Center.X,rectangle.Center.Y,0).TransformBy(Matrix3d.Rotation(-angle,Vector3d.ZAxis,Point3d.Origin));
                        var viewport=new Viewport();viewport.SetDatabaseDefaults(doc.Database);
                        viewport.CenterPoint=new Point3d(frame.PaperWidthMm/2,frame.PaperHeightMm/2,0);viewport.Width=frame.PaperWidthMm;viewport.Height=frame.PaperHeightMm;
                        viewport.Layer=layer;paper.AppendEntity(viewport);tr.AddNewlyCreatedDBObject(viewport,true);
                        viewport.ViewDirection=Vector3d.ZAxis;viewport.ViewTarget=Point3d.Origin;viewport.TwistAngle=-angle;viewport.ViewCenter=new Point2d(center.X,center.Y);
                        viewport.ViewHeight=rectangle.Height;viewport.CustomScale=Math.Min(frame.PaperWidthMm/rectangle.Width,frame.PaperHeightMm/rectangle.Height);viewport.On=true;viewport.Locked=true;
                        result.Details.Add(name+": "+frame.FormatName+", viewport 1:"+frame.Scale+", исходная рамка "+frame.Source.HandlePath);
                    }
                    tr.Commit();result.Changed+=created.Count;
                }
            }
            catch
            {
                manager.CurrentLayout=previous;
                foreach(var item in created.AsEnumerable().Reverse())
                    if(manager.LayoutExists(item.Item1)&&manager.GetLayoutId(item.Item1)==item.Item2)manager.DeleteLayout(item.Item1);
                throw;
            }
            finally{manager.CurrentLayout=previous;doc.Editor.Regen();}
        }
        private static string NonPlotLayer(Database database,Transaction tr)
        {
            var layers=(LayerTable)tr.GetObject(database.LayerTableId,OpenMode.ForRead);string name="CADBATCH_VIEWPORTS";int n=2;
            while(layers.Has(name))
            {
                if(!((LayerTableRecord)tr.GetObject(layers[name],OpenMode.ForRead)).IsPlottable)return name;
                name="CADBATCH_VIEWPORTS_"+(n++);
            }
            layers.UpgradeOpen();var layer=new LayerTableRecord{Name=name,IsPlottable=false};layers.Add(layer);tr.AddNewlyCreatedDBObject(layer,true);return name;
        }
        private static string LayoutName(string pattern,DetectedFrame frame)
        {
            string name=Regex.Replace(pattern,@"\{([^{}]+)\}",m=>m.Groups[1].Value=="Page"?frame.Page.ToString("000"):m.Groups[1].Value=="SheetNumber"?frame.SheetNumber:
                m.Groups[1].Value=="Format"?frame.FormatName:m.Groups[1].Value=="Title"?frame.Title:throw new ArgumentException("Неизвестное поле имени листа: "+m.Value));
            name=Regex.Replace(name,@"[<>/\\\"":;?*|=,\x00-\x1f]","_").Trim().TrimEnd('.');
            if(string.IsNullOrWhiteSpace(name))name="CB_"+frame.Page.ToString("000");return name.Length>100?name.Substring(0,100):name;
        }
        private static void ExportFrameDwg(Document doc,OutputItem item,bool crossing,CadOperationResult result)
        {
            var frame=item.Frame;if(!string.Equals(frame.Layout,"Model",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Экспорт рамок в DWG сейчас выполняется из модели; строки Layout исключите.");
            using(var tr=doc.Database.TransactionManager.StartTransaction()){ValidateFrame(doc,frame,tr);tr.Commit();}
            string previous=LayoutManager.Current.CurrentLayout;int previousViewport=Convert.ToInt32(AcApp.GetSystemVariable("CVPORT"),CultureInfo.InvariantCulture);
            using(var view=doc.Editor.GetCurrentView())
            {
                try
                {
                    LayoutManager.Current.CurrentLayout="Model";
                    using(var current=doc.Editor.GetCurrentView())
                    {
                        var window=ViewWindow.Around(frame.Geometry.Corners,ViewAspect(current));current.ViewDirection=Vector3d.ZAxis;current.ViewTwist=0;current.Target=Point3d.Origin;
                        current.CenterPoint=new Point2d(window.Center.X,window.Center.Y);current.Width=window.Width;current.Height=window.Height;doc.Editor.SetCurrentView(current);
                    }
                    doc.Editor.Regen();var inverse=doc.Editor.CurrentUserCoordinateSystem.Inverse();
                    var polygon=new Point3dCollection(frame.Geometry.Corners.Select(p=>new Point3d(p.X,p.Y,0).TransformBy(inverse)).ToArray());
                    var selected=crossing?doc.Editor.SelectCrossingPolygon(polygon):doc.Editor.SelectWindowPolygon(polygon);
                    if(selected.Status!=PromptStatus.OK||selected.Value.Count==0)throw new InvalidOperationException("В области рамки нет выбранных объектов.");
                    ObjectIdCollection ids;using(var selection=selected.Value)ids=new ObjectIdCollection(selection.GetObjectIds());Directory.CreateDirectory(Path.GetDirectoryName(item.Path)!);
                    string temporary=Path.Combine(Path.GetDirectoryName(item.Path)!,"cadbatch-tmp-"+Guid.NewGuid().ToString("N")+".dwg");
                    try
                    {
                        using(var output=doc.Database.Wblock(ids,Point3d.Origin)){output.Insunits=doc.Database.Insunits;output.SaveAs(temporary,DwgVersion.AC1032);}
                        File.Move(temporary,item.Path);result.Files.Add(item.Path);result.Changed++;result.Details.Add(frame.Page+": "+ids.Count+" объектов; "+item.Path+". Блоки/пересечения сохраняются целиком, без обрезки.");
                    }
                    finally{if(File.Exists(temporary))File.Delete(temporary);}
                }
                finally
                {
                    LayoutManager.Current.CurrentLayout=previous;
                    if(previousViewport>1&&!string.Equals(previous,"Model",StringComparison.OrdinalIgnoreCase)){doc.Editor.SwitchToModelSpace();AcApp.SetSystemVariable("CVPORT",previousViewport);}
                    doc.Editor.SetCurrentView(view);doc.Editor.Regen();
                }
            }
        }
        private sealed class AttributeWrite { public ObjectId Id;public string Old="",New="";public string Label=""; }
        private static void WriteAttributes(Document doc,List<DetectedFrame> frames,CadOperationRequest request,CadOperationResult result,CancellationToken cancellation)
        {
            using(var tr=doc.Database.TransactionManager.StartTransaction())
            {
                var writes=new Dictionary<ObjectId,AttributeWrite>();
                foreach(var frame in frames)
                {
                    cancellation.ThrowIfCancellationRequested();ValidateFrame(doc,frame,tr);
                    if(frame.Source.IsExternal)throw new InvalidOperationException("Запись в Xref запрещена: "+frame.Page);
                    string path=frame.Source.MemberPaths.Count>0?frame.Source.MemberPaths[0]:frame.Source.HandlePath;
                    if(!path.Contains("/"))throw new InvalidOperationException("Рамка №"+frame.Page+": нет атрибутов внешнего вхождения блока. Используйте блок с атрибутами или создайте нашу рамку.");
                    var root=(BlockReference)tr.GetObject(Resolve(doc.Database,path.Split('/')[0]),OpenMode.ForRead);
                    var attributes=root.AttributeCollection.Cast<ObjectId>().Select(id=>(AttributeReference)tr.GetObject(id,OpenMode.ForRead)).ToList();
                    Add(request.NumberTag,frame.SheetNumber);
                    if(request.WriteTotals)Add(request.TotalTag,frames.Count.ToString(CultureInfo.InvariantCulture));
                    if(request.WriteTitles){Add(request.TitleTag,frame.Title);Add(request.CodeTag,frame.Code);}
                    void Add(string tag,string value)
                    {
                        var matches=attributes.Where(a=>string.Equals(a.Tag,tag,StringComparison.OrdinalIgnoreCase)).ToArray();
                        if(matches.Any(a=>a.HasFields))throw new InvalidOperationException("Атрибут содержит FIELD: "+tag+". Автоматическая замена запрещена.");
                        if(matches.Length!=1)throw new InvalidOperationException("Рамка №"+frame.Page+": атрибут "+tag+" не найден однозначно.");
                        var a=matches[0];if(frame.Source.Attributes.TryGetValue(tag,out var before)&&before!=a.TextString)throw new InvalidOperationException("Атрибут "+tag+" изменён после поиска. Повторите поиск.");
                        if(writes.TryGetValue(a.ObjectId,out var existing)&&existing.New!=value)throw new InvalidOperationException("Несколько рамок используют один атрибут "+tag+" с разными новыми значениями.");
                        writes[a.ObjectId]=new AttributeWrite{Id=a.ObjectId,Old=a.TextString,New=value,Label="Рамка "+frame.Page+" / "+tag};
                    }
                }
                int changed=0;
                foreach(var write in writes.Values)
                {
                    result.Details.Add(write.Label+": «"+write.Old+"» → «"+write.New+"»");
                    if(request.ApplyChanges&&write.New!=write.Old){((AttributeReference)tr.GetObject(write.Id,OpenMode.ForWrite)).TextString=write.New;changed++;}
                }
                if(request.ApplyChanges)
                {
                    tr.Commit();result.Changed+=changed;foreach(var frame in frames)
                    {
                        frame.Source.Attributes[request.NumberTag]=frame.SheetNumber;
                        if(request.WriteTotals)frame.Source.Attributes[request.TotalTag]=frames.Count.ToString(CultureInfo.InvariantCulture);
                        if(request.WriteTitles){frame.Source.Attributes[request.TitleTag]=frame.Title;frame.Source.Attributes[request.CodeTag]=frame.Code;}
                    }
                    doc.Editor.Regen();
                }
            }
        }
        private static void InsertRegister(CadOperationRequest request,CadOperationResult result)
        {
            var doc=ActiveDocument();var point=AskPoint(doc,"Укажите верхний левый угол ведомости");if(!point.HasValue){result.Cancelled=true;return;}
            if(request.Frames.Count==0)throw new ArgumentException("Нет отмеченных рамок.");
            using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction())
            {
                var table=new Table();table.SetDatabaseDefaults(doc.Database);table.TableStyle=doc.Database.Tablestyle;table.Position=point.Value;
                table.SetSize(request.Frames.Count+2,4);table.SetRowHeight(8);table.SetColumnWidth(35);table.Columns[1].Width=110;table.Columns[2].Width=55;
                table.MergeCells(CellRange.Create(table,0,0,0,3));table.Cells[0,0].TextString="Ведомость чертежей";
                string[] headers={"Лист","Наименование","Шифр","Формат"};for(int c=0;c<4;c++)table.Cells[1,c].TextString=headers[c];
                for(int r=0;r<request.Frames.Count;r++)
                {
                    var f=request.Frames[r];string[] values={string.IsNullOrEmpty(f.SheetNumber)?f.Page.ToString():f.SheetNumber,f.Title,f.Code,f.FormatName};
                    for(int c=0;c<4;c++){table.Cells[r+2,c].TextString=values[c];table.Cells[r+2,c].TextHeight=3.5;}
                }
                table.GenerateLayout();var space=(BlockTableRecord)tr.GetObject(doc.Database.CurrentSpaceId,OpenMode.ForWrite);space.AppendEntity(table);tr.AddNewlyCreatedDBObject(table,true);tr.Commit();
                result.Changed=1;result.Details.Add("Вставлена ведомость: "+request.Frames.Count+" строк. Данные штампов не изменены.");doc.Editor.Regen();
            }
        }
        private static void CreateFrame(CadOperationRequest request,CadOperationResult result)
        {
            var doc=ActiveDocument();if(LayoutManager.Current.CurrentLayout!="Model")throw new InvalidOperationException("Переключитесь на вкладку «Модель» для вставки рамки.");
            double w=request.Format.WidthMm,h=request.Format.HeightMm;if(!Numeric.Positive(w)||!Numeric.Positive(h)||w<210||h<148||!Numeric.Positive(request.Scale))throw new ArgumentException("Размер рамки не менее 210 × 148 мм, масштаб положительный.");
            var point=AskPoint(doc,"Укажите нижний левый угол новой рамки");if(!point.HasValue){result.Cancelled=true;return;}
            using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction())
            {
                var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForWrite);var block=new BlockTableRecord{Name="CB_FRAME_"+Guid.NewGuid().ToString("N")};bt.Add(block);tr.AddNewlyCreatedDBObject(block,true);
                void Add(Entity e){e.SetDatabaseDefaults(doc.Database);block.AppendEntity(e);tr.AddNewlyCreatedDBObject(e,true);}
                void Rect(double x,double y,double width,double height)
                {
                    var poly=new Polyline(4);poly.AddVertexAt(0,new Point2d(x,y),0,0,0);poly.AddVertexAt(1,new Point2d(x+width,y),0,0,0);poly.AddVertexAt(2,new Point2d(x+width,y+height),0,0,0);poly.AddVertexAt(3,new Point2d(x,y+height),0,0,0);poly.Closed=true;Add(poly);
                }
                Rect(0,0,w,h);Rect(20,5,w-25,h-10);double sx=w-190;Rect(sx,5,185,55);
                foreach(double y in new[]{15d,35d,50d})Add(new Line(new Point3d(sx,y,0),new Point3d(w-5,y,0)));
                void Text(string text,double x,double y,double height){var t=new DBText{TextString=text,Position=new Point3d(x,y,0),Height=height};Add(t);}
                Text("Наименование",sx+2,52,2.5);Text("Обозначение",sx+2,37,2.5);Text("Лист / Листов",w-55,7,2.5);Text("CAD Batch — собственный шаблон",sx+2,7,2.5);
                void Attribute(string tag,string value,double x,double y){var a=new AttributeDefinition{Tag=tag,Prompt=tag,TextString=value,Position=new Point3d(x,y,0),Height=3.5};Add(a);}
                Attribute("TITLE","Новый чертёж",sx+2,42);Attribute("CODE","CB-TEST",sx+2,24);Attribute("SHEET","1",w-50,17);Attribute("SHEETS","1",w-25,17);
                var model=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);var reference=new BlockReference(point.Value,block.ObjectId);reference.SetDatabaseDefaults(doc.Database);reference.ScaleFactors=new Scale3d(request.Scale);model.AppendEntity(reference);tr.AddNewlyCreatedDBObject(reference,true);
                foreach(ObjectId id in block)if(tr.GetObject(id,OpenMode.ForRead) is AttributeDefinition definition)
                {
                    var attribute=new AttributeReference();attribute.SetAttributeFromBlock(definition,reference.BlockTransform);attribute.TextString=definition.TextString;reference.AttributeCollection.AppendAttribute(attribute);tr.AddNewlyCreatedDBObject(attribute,true);
                }
                tr.Commit();result.Changed=1;result.Details.Add("Создан блок "+reference.Handle+", "+request.Format.Name+". Это собственный тестовый штамп; нормативное соответствие СПДС/ЕСКД не заявлено.");doc.Editor.Regen();
            }
        }
    }
}
