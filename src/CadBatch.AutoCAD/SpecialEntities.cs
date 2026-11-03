using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadBatch.Core;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private static bool IsSpecial(Entity entity)=>entity is ProxyEntity||entity.GetRXClass().DxfName.StartsWith("SPDS",StringComparison.OrdinalIgnoreCase);
        private static void WithSpecialParts(Entity source,Action<List<Entity>> action)
        {
            var parts=new List<Entity>();int count=0;
            void Expand(Entity entity,int depth)
            {
                if(depth>6||++count>10000)throw new InvalidOperationException("Слишком сложная геометрия СПДС/proxy.");
                if(entity is Line||entity is Polyline||entity is DBText||entity is MText||entity is Table){parts.Add((Entity)entity.Clone());return;}
                var objects=new DBObjectCollection();
                try{entity.Explode(objects);foreach(DBObject obj in objects)if(obj is Entity part)Expand(part,depth+1);}
                finally{foreach(DBObject obj in objects)obj.Dispose();}
            }
            try{Expand(source,0);action(parts);}finally{foreach(var entity in parts)entity.Dispose();}
        }
        private static List<Rectangle2> SpecialRectangles(List<Entity> parts,Transaction tr,Matrix3d transform,IReadOnlyList<FormatDefinition> formats,SearchOptions options)
        {
            var rectangles=new List<Rectangle2>();var lines=new List<FrameSegment>();int index=0;
            foreach(var entity in parts)
            {
                if(entity is Polyline){var points=Boundary(entity,tr,transform);if(points!=null&&Rectangle2.TryCreate(points,options.GeometryTolerance,out var r))rectangles.Add(r!);}
                else if(entity is Line line)
                {
                    var a=line.StartPoint.TransformBy(transform);var b=line.EndPoint.TransformBy(transform);if(Math.Abs(a.Z-b.Z)<.01)lines.Add(new FrameSegment{Start=new Point2(a.X,a.Y),End=new Point2(b.X,b.Y),Path=(index++).ToString()});
                }
            }
            rectangles.AddRange(LineFrames.Find(lines,formats,options,CancellationToken.None).Select(f=>f.Rectangle));return rectangles;
        }
        private static void ReadSpecial(Document doc,string layout,Entity entity,Transaction tr,Matrix3d transform,string path,string layer,string block,Dictionary<string,string> attributes,bool external,SearchOptions options,IReadOnlyList<FormatDefinition> formats,List<FrameCandidate> output,List<DrawingText> texts,List<StampSegment> stampGeometry,List<string> warnings,List<ClipBoundary>? clips)
        {
            try
            {
                WithSpecialParts(entity,parts=>
                {
                    for(int n=0;n<parts.Count;n++)
                    {
                        string member=path+"!p"+n;ReadDrawingTexts(doc,layout,parts[n],transform,member,texts);
                        if(options.Stamp?.Enabled==true)stampGeometry.AddRange(ReadStampSegments(parts[n],tr,transform,DocumentKey(doc),layout,member));
                    }
                    int index=0;foreach(var rectangle in SpecialRectangles(parts,tr,transform,formats,options))
                    {
                        if(clips!=null&&clips.Any(c=>!c.Allows(rectangle.Corners)))continue;
                        var frame=Candidate(doc,layout,layer,block,path+"!"+(index++),attributes,external);frame.GeometryKind="Exploded";frame.Kind=entity is ProxyEntity?"Proxy: публичная геометрия":"СПДС: публичная геометрия";frame.Vertices=rectangle.Corners.ToList();frame.Elevation=entity.GeometricExtents.MinPoint.TransformBy(transform).Z;output.Add(frame);
                    }
                });
            }
            catch(Exception ex){warnings.Add(doc.Name+" / "+entity.Handle+": СПДС/proxy не отдаёт геометрию через AutoCAD API. Нужен совместимый Object Enabler. "+ex.Message);}
        }
    }
}
