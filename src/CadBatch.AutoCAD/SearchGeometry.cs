using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadBatch.Core;
using AcException=Autodesk.AutoCAD.Runtime.Exception;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private sealed class LineScanGroup
        {
            public FrameCandidate Template=null!;
            public List<ClipBoundary> Clips=new List<ClipBoundary>();
            public List<FrameSegment> Segments=new List<FrameSegment>();
        }
        private static bool HasClip(BlockReference block,Transaction tr)=>!block.ExtensionDictionary.IsNull&&((DBDictionary)tr.GetObject(block.ExtensionDictionary,OpenMode.ForRead)).Contains("ACAD_FILTER");
        private static void Walk(Document doc,string layout,BlockTableRecord record,Transaction tr,Matrix3d transform,string path,string blockName,
            Dictionary<string,string> inherited,HashSet<ObjectId> ancestors,int depth,SearchOptions options,IReadOnlyList<FormatDefinition> formats,List<FrameCandidate> output,List<LineScanGroup> lineGroups,List<StampSegment> stampGeometry,List<DrawingText> texts,
            List<string> warnings,CancellationToken cancellation,bool external,string parentLayer,ref int visited,List<ClipBoundary>? clips=null,HashSet<string>? only=null)
        {
            if(depth>24||!ancestors.Add(record.ObjectId)){warnings.Add(doc.Name+": циклическая или слишком глубокая вложенность.");return;}
            var lines=new Dictionary<string,LineScanGroup>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach(ObjectId id in record)
                {
                    cancellation.ThrowIfCancellationRequested();visited++;if(id.IsErased)continue;
                    // Selection search: only the selected objects of the tab; the contents of selected blocks are read in full.
                    if(only!=null&&!only.Contains(id.Handle.ToString()))continue;
                    try
                    {
                        if(!(tr.GetObject(id,OpenMode.ForRead) is Entity entity)||!entity.Visible)continue;
                        ReadDrawingTexts(doc,layout,entity,transform,path+entity.Handle,texts);
                        if(options.Stamp?.Enabled==true&&(entity is Line||entity is Polyline||entity is Polyline2d))stampGeometry.AddRange(ReadStampSegments(entity,tr,transform,DocumentKey(doc),layout,path+entity.Handle));
                        string layer=entity.Layer=="0"&&!string.IsNullOrEmpty(parentLayer)?parentLayer:entity.Layer;
                        if(options.SpecialEntities&&IsSpecial(entity)){ReadSpecial(doc,layout,entity,tr,transform,path+entity.Handle,layer,blockName,inherited,external,options,formats,output,texts,stampGeometry,warnings,clips);continue;}
                        if(entity is BlockReference br)
                        {
                            var definition=(BlockTableRecord)tr.GetObject(br.BlockTableRecord,OpenMode.ForRead);
                            bool xref=definition.IsFromExternalReference||definition.IsFromOverlayReference;
                            if(xref&&!options.ExternalReferences)continue;
                            var nestedClips=new List<ClipBoundary>(clips??new List<ClipBoundary>());
                            try{var clip=ReadClip(br,tr,transform);if(clip!=null)nestedClips.Add(clip);}catch(NotSupportedException ex){warnings.Add(doc.Name+": "+ex.Message);continue;}
                            string name=br.IsDynamicBlock?((BlockTableRecord)tr.GetObject(br.DynamicBlockTableRecord,OpenMode.ForRead)).Name:definition.Name;
                            var attributes=new Dictionary<string,string>(inherited,StringComparer.OrdinalIgnoreCase);
                            foreach(ObjectId aid in br.AttributeCollection)if(tr.GetObject(aid,OpenMode.ForRead) is AttributeReference a){attributes[a.Tag]=a.TextString;ReadDrawingTexts(doc,layout,a,transform,path+br.Handle+"/"+a.Handle,texts);}
                            if(xref)
                            {
                                var database=definition.GetXrefDatabase(false);
                                if(database==null){warnings.Add(doc.Name+": Xref не загружена: "+name);continue;}
                                using(var xt=database.TransactionManager.StartOpenCloseTransaction())
                                {
                                    var bt=(BlockTable)xt.GetObject(database.BlockTableId,OpenMode.ForRead);var model=(BlockTableRecord)xt.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);
                                    Walk(doc,layout,model,xt,transform*br.BlockTransform,path+br.Handle+"/",name,attributes,ancestors,depth+1,options,formats,output,lineGroups,stampGeometry,texts,warnings,cancellation,true,layer,ref visited,nestedClips);
                                }
                            }
                            else Walk(doc,layout,definition,tr,transform*br.BlockTransform,path+br.Handle+"/",name,attributes,ancestors,depth+1,options,formats,output,lineGroups,stampGeometry,texts,warnings,cancellation,external,layer,ref visited,nestedClips);
                        }
                        else if(entity is Line line&&options.Lines&&(depth==0||options.Blocks||external)&&FrameDetection.Wildcard(layer,options.LayerPattern))
                        {
                            var a=line.StartPoint.TransformBy(transform);var b=line.EndPoint.TransformBy(transform);
                            if(Math.Abs(a.Z-b.Z)>options.GeometryTolerance)continue;
                            string groupKey=layer+"|"+Math.Round(a.Z/options.GeometryTolerance).ToString(System.Globalization.CultureInfo.InvariantCulture);
                            if(!lines.TryGetValue(groupKey,out var group))
                            {
                                group=new LineScanGroup{Clips=clips??new List<ClipBoundary>(),Template=Candidate(doc,layout,layer,blockName,path+line.Handle,inherited,external)};group.Template.Elevation=a.Z;lines.Add(groupKey,group);
                            }
                            group.Segments.Add(new FrameSegment{Start=new Point2(a.X,a.Y),End=new Point2(b.X,b.Y),Path=path+line.Handle});
                        }
                        else if((entity is Polyline||entity is Polyline2d)&&(depth>0?(options.Blocks||external):options.Polylines)||entity is Viewport&&options.Viewports&&depth==0)
                        {
                            var points=Boundary(entity,tr,transform,out double elevation);if(points==null)continue;
                            var c=Candidate(doc,layout,layer,blockName,path+entity.Handle,inherited,external);c.Vertices=points;c.Elevation=elevation;
                            c.GeometryKind=entity is Polyline2d?"Polyline2d":entity is Viewport?"Viewport":"Polyline";
                            if(clips!=null&&clips.Any(clip=>!clip.Allows(points)))continue;
                            c.Kind=(entity is Polyline2d?"2D-полилиния":entity is Viewport?"Видовой экран":depth>0?"Контур в блоке":"Полилиния")+(external?" в Xref":"");output.Add(c);
                        }
                    }
                    catch(AcException ex){warnings.Add(doc.Name+": объект "+id.Handle+": "+ex.Message);}
                }
                lineGroups.AddRange(lines.Values);
            }
            finally{ancestors.Remove(record.ObjectId);}
        }
        private static FrameCandidate Candidate(Document doc,string layout,string layer,string block,string path,Dictionary<string,string> attributes,bool external)=>new FrameCandidate
        {
            DrawingModified=(Convert.ToInt32(Autodesk.AutoCAD.ApplicationServices.Core.Application.GetSystemVariable("DBMOD"))&1)!=0,DocumentKey=DocumentKey(doc),DocumentName=Path.GetFileName(doc.Name),DocumentPath=doc.Name,Layout=layout,Layer=layer,BlockName=block,HandlePath=path,IsExternal=external,
            Attributes=new Dictionary<string,string>(attributes,StringComparer.OrdinalIgnoreCase)
        };
        private static List<Point2>? Boundary(Entity entity,Transaction tr,Matrix3d transform)=>Boundary(entity,tr,transform,out _);
        private static List<Point2>? Boundary(Entity entity,Transaction tr,Matrix3d transform,out double elevation)
        {
            elevation=0;var points=new List<Point3d>();
            if(entity is Polyline p)
            {
                // Frames are often drawn without "Close": the last vertex returns to the first one.
                int count=p.NumberOfVertices;bool closedByPoint=!p.Closed&&count>=5&&OutlineGeometry.EndsAtStart(new Point2(p.GetPoint2dAt(0).X,p.GetPoint2dAt(0).Y),new Point2(p.GetPoint2dAt(count-1).X,p.GetPoint2dAt(count-1).Y),new Point2(p.GetPoint2dAt(1).X,p.GetPoint2dAt(1).Y));
                if(!(p.Closed||closedByPoint)||count<4)return null;if(closedByPoint)count--;
                // Arc segments disqualify; the dropped closing vertex of an open outline has no segment of its own.
                for(int i=0;i<count;i++){if(Math.Abs(p.GetBulgeAt(i))>1e-10)return null;points.Add(p.GetPoint3dAt(i).TransformBy(transform));}
            }
            else if(entity is Polyline2d old)
            {
                if(old.PolyType!=Poly2dType.SimplePoly)return null;
                var vertices=new List<Vertex2d>();foreach(ObjectId id in old)vertices.Add((Vertex2d)tr.GetObject(id,OpenMode.ForRead));
                bool closedByPoint=!old.Closed&&vertices.Count>=5&&OutlineGeometry.EndsAtStart(new Point2(vertices[0].Position.X,vertices[0].Position.Y),new Point2(vertices[vertices.Count-1].Position.X,vertices[vertices.Count-1].Position.Y),new Point2(vertices[1].Position.X,vertices[1].Position.Y));
                if(!old.Closed&&!closedByPoint)return null;if(closedByPoint)vertices.RemoveAt(vertices.Count-1);
                foreach(var vertex in vertices)
                {
                    if(Math.Abs(vertex.Bulge)>1e-10)return null;
                    points.Add(new Point3d(vertex.Position.X,vertex.Position.Y,old.Elevation).TransformBy(Matrix3d.PlaneToWorld(old.Normal)).TransformBy(transform));
                }
            }
            else if(entity is Viewport vp)
            {
                if(vp.Number==1||vp.NonRectClipOn||!Numeric.Positive(vp.Width)||!Numeric.Positive(vp.Height))return null;
                var owner=(BlockTableRecord)tr.GetObject(vp.OwnerId,OpenMode.ForRead);
                if(owner.IsLayout)
                {
                    var layout=(Layout)tr.GetObject(owner.LayoutId,OpenMode.ForRead);var viewports=layout.GetViewports();
                    if(viewports.Count>0&&viewports[0]==vp.ObjectId)return null;
                }
                foreach(var offset in new[]{new Point2(-1,-1),new Point2(1,-1),new Point2(1,1),new Point2(-1,1)})
                    points.Add(new Point3d(vp.CenterPoint.X+offset.X*vp.Width/2,vp.CenterPoint.Y+offset.Y*vp.Height/2,vp.CenterPoint.Z).TransformBy(transform));
            }
            else return null;
            if(points.Count<4||points.Max(p=>p.Z)-points.Min(p=>p.Z)>.01)return null;
            elevation=points[0].Z;return points.Select(p=>new Point2(p.X,p.Y)).ToList();
        }
        private static void WithPathEntity(Document doc,string layoutName,string path,Transaction outer,Action<Entity,Transaction,Matrix3d> action,DetectedFrame? frame=null)
        {
            var dictionary=(DBDictionary)outer.GetObject(doc.Database.LayoutDictionaryId,OpenMode.ForRead);
            if(!dictionary.Contains(layoutName))throw new InvalidOperationException("Вкладка удалена. Повторите поиск.");
            var layout=(Layout)outer.GetObject(dictionary.GetAt(layoutName),OpenMode.ForRead);var owner=layout.BlockTableRecordId;
            Database database=doc.Database;Transaction current=outer;var transactions=new List<Transaction>();Matrix3d transform=Matrix3d.Identity;
            try
            {
                var handles=path.Split('/');
                for(int i=0;i<handles.Length;i++)
                {
                    var entity=(Entity)current.GetObject(Resolve(database,handles[i].Split('!')[0]),OpenMode.ForRead);
                    if(entity.OwnerId!=owner||!entity.Visible)throw new InvalidOperationException("Вложенность или видимость изменилась. Повторите поиск.");
                    if(i==handles.Length-1)
                    {
                        var suffix=handles[i].Split('!');
                        if(suffix.Length>1&&suffix[1].StartsWith("p",StringComparison.Ordinal))WithSpecialParts(entity,parts=>{int index=int.Parse(suffix[1].Substring(1));if(index<0||index>=parts.Count)throw new InvalidOperationException("Геометрия СПДС/proxy изменилась. Повторите поиск.");action(parts[index],current,transform);});
                        else action(entity,current,transform);return;
                    }
                    if(!(entity is BlockReference br))throw new InvalidOperationException("Блок изменился. Повторите поиск.");
                    var clip=ReadClip(br,current,transform);if(frame!=null&&clip!=null&&!clip.Allows(frame.Geometry.Corners))throw new InvalidOperationException("XCLIP обрезает рамку. Повторите поиск.");
                    transform=transform*br.BlockTransform;var definition=(BlockTableRecord)current.GetObject(br.BlockTableRecord,OpenMode.ForRead);
                    if(definition.IsFromExternalReference||definition.IsFromOverlayReference)
                    {
                        database=definition.GetXrefDatabase(false)??throw new InvalidOperationException("Xref недоступна. Повторите поиск.");
                        current=database.TransactionManager.StartOpenCloseTransaction();transactions.Add(current);
                        owner=((BlockTable)current.GetObject(database.BlockTableId,OpenMode.ForRead))[BlockTableRecord.ModelSpace];
                    }
                    else owner=br.BlockTableRecord;
                }
                throw new InvalidOperationException("Недоступен контур рамки.");
            }
            finally{for(int i=transactions.Count-1;i>=0;i--)transactions[i].Dispose();}
        }
        private static Rectangle2 ValidateFrame(Document doc,DetectedFrame frame,Transaction tr)
        {
            if(!frame.Available)throw new InvalidOperationException("Рамка недоступна. Повторите поиск.");
            Rectangle2? rectangle=null;
            if(frame.Source.GeometryKind=="Lines")
            {
                var segments=new List<FrameSegment>();
                foreach(string path in frame.Source.MemberPaths)WithPathEntity(doc,frame.Layout,path,tr,(entity,transaction,matrix)=>
                {
                    if(!(entity is Line line))throw new InvalidOperationException("Отрезки рамки изменились. Повторите поиск.");
                    var a=line.StartPoint.TransformBy(matrix);var b=line.EndPoint.TransformBy(matrix);
                    if(Math.Abs(a.Z-b.Z)>.01)throw new InvalidOperationException("Плоскость рамки изменилась.");
                    segments.Add(new FrameSegment{Start=new Point2(a.X,a.Y),End=new Point2(b.X,b.Y),Path=path});
                },frame);
                var matches=LineFrames.Find(segments,new[]{frame.Format},new SearchOptions{Scales=new List<double>{frame.Scale}},CancellationToken.None);
                rectangle=matches.FirstOrDefault(m=>m.Rectangle.SameBoundary(frame.Geometry,.02))?.Rectangle;
            }
            else if(frame.Source.GeometryKind=="Exploded")WithPathEntity(doc,frame.Layout,frame.Source.HandlePath,tr,(entity,transaction,matrix)=>WithSpecialParts(entity,parts=>
            {
                rectangle=SpecialRectangles(parts,transaction,matrix,new[]{frame.Format},new SearchOptions{Scales=new List<double>{frame.Scale}}).FirstOrDefault(r=>r.SameBoundary(frame.Geometry,.02));
            }),frame);
            else WithPathEntity(doc,frame.Layout,frame.Source.HandlePath,tr,(entity,transaction,matrix)=>
            {
                var points=Boundary(entity,transaction,matrix);if(points!=null)Rectangle2.TryCreate(points,.01,out rectangle);
            },frame);
            // A sheet found by its inner ГОСТ frame: the outline is the inner frame, the sheet is that frame plus the margins.
            if(rectangle!=null&&!frame.Geometry.SameBoundary(rectangle,.02)&&frame.Source.GeometryKind!="Lines"&&frame.Source.GeometryKind!="Exploded")
            {var sheet=InnerFrames.Expand(rectangle,frame.Scale);if(frame.Geometry.SameBoundary(sheet,.02))rectangle=sheet;}
            if(rectangle==null||!frame.Geometry.SameBoundary(rectangle,.02))throw new InvalidOperationException("Геометрия рамки изменилась. Повторите поиск.");
            ValidateStampEvidence(doc,frame,tr);return rectangle;
        }
    }
}
