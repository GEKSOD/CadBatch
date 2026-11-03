using System;
using System.Collections.Generic;
using System.Linq;
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
        private static List<StampSegment> ReadStampSegments(Entity entity,Transaction tr,Matrix3d transform,string document,string layout,string path)
        {
            var result=new List<StampSegment>();
            void Add(Point3d a,Point3d b,int index)
            {
                a=a.TransformBy(transform);b=b.TransformBy(transform);
                if(Math.Abs(a.Z-b.Z)>.01||a.DistanceTo(b)<1e-10)return;
                result.Add(new StampSegment{DocumentKey=document,Layout=layout,Path=path,Index=index,Start=new Point2(a.X,a.Y),End=new Point2(b.X,b.Y),Elevation=(a.Z+b.Z)/2});
            }
            if(entity is Line line)Add(line.StartPoint,line.EndPoint,0);
            else if(entity is Polyline p)
            {
                int count=p.Closed?p.NumberOfVertices:p.NumberOfVertices-1;
                for(int i=0;i<count;i++)if(Math.Abs(p.GetBulgeAt(i))<=1e-10)Add(p.GetPoint3dAt(i),p.GetPoint3dAt((i+1)%p.NumberOfVertices),i);
            }
            else if(entity is Polyline2d old&&old.PolyType==Poly2dType.SimplePoly)
            {
                var vertices=old.Cast<ObjectId>().Select(id=>(Vertex2d)tr.GetObject(id,OpenMode.ForRead)).ToArray();int count=old.Closed?vertices.Length:vertices.Length-1;
                var ocs=Matrix3d.PlaneToWorld(old.Normal);
                for(int i=0;i<count;i++)if(Math.Abs(vertices[i].Bulge)<=1e-10)
                {
                    var a=vertices[i].Position;var b=vertices[(i+1)%vertices.Length].Position;
                    Add(new Point3d(a.X,a.Y,old.Elevation).TransformBy(ocs),new Point3d(b.X,b.Y,old.Elevation).TransformBy(ocs),i);
                }
            }
            return result;
        }
        private static void ValidateStampEvidence(Document doc,DetectedFrame frame,Transaction tr)
        {
            var proof=frame.StampEvidence;if(proof==null)return;double tolerance=Math.Max(1e-8,frame.Scale*.001);var actual=new List<StampSegment>();
            foreach(var group in proof.Members.GroupBy(s=>s.Path,StringComparer.Ordinal))
            {
                WithPathEntity(doc,frame.Layout,group.Key,tr,(entity,transaction,matrix)=>
                {
                    actual.AddRange(ReadStampSegments(entity,transaction,matrix,frame.Source.DocumentKey,frame.Layout,group.Key));
                });
            }
            if(!proof.IsCurrent(actual,tolerance))throw new InvalidOperationException("Геометрия штампа изменилась после определения масштаба. Повторите поиск рамок.");
        }
        public async Task<StampScaleMeasurement?> MeasureStampScaleAsync(double referenceWidthMm,bool twoPoints)
        {
            StampScaleMeasurement? measurement=null;
            await InContext(()=>
            {
                using var interaction=CadInteraction.Begin();
                var doc=ActiveDocument();doc.Window.Focus();Point3d a,b;string description;
                if(twoPoints)
                {
                    var first=doc.Editor.GetPoint("\nCAD Batch: первый конец ВЕРХНЕЙ линии штампа (вся ширина "+referenceWidthMm+" мм бумаги): ");
                    if(first.Status!=PromptStatus.OK)return;
                    var second=doc.Editor.GetPoint(new PromptPointOptions("\nCAD Batch: второй конец той же верхней линии: "){UseBasePoint=true,BasePoint=first.Value,UseDashedLine=true});
                    if(second.Status!=PromptStatus.OK)return;
                    a=first.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);b=second.Value.TransformBy(doc.Editor.CurrentUserCoordinateSystem);description="Две точки верхней линии штампа";
                }
                else
                {
                    var selected=doc.Editor.GetNestedEntity("\nCAD Batch: выберите ВЕРХНЮЮ линию штампа полной ширины "+referenceWidthMm+" мм бумаги (LINE, можно внутри блока): ");
                    if(selected.Status!=PromptStatus.OK)return;
                    using(doc.LockDocument())using(var tr=selected.ObjectId.Database.TransactionManager.StartOpenCloseTransaction())
                    {
                        if(!(tr.GetObject(selected.ObjectId,OpenMode.ForRead) is Line line))throw new InvalidOperationException("Выбран не отдельный LINE. Для полилинии, таблицы или СПДС выберите «По двум концам верхней линии…» и укажите всю ширину штампа.");
                        a=line.StartPoint.TransformBy(selected.Transform);b=line.EndPoint.TransformBy(selected.Transform);description="LINE "+line.Handle+"; "+doc.Name;
                    }
                }
                if(Math.Abs(a.Z-b.Z)>.01)throw new InvalidOperationException("Линия наклонена к XY. Для этой версии нужна линия в плоскости рамки XY.");
                double length=a.DistanceTo(b);measurement=new StampScaleMeasurement{Length=length,Scale=StampScaleIndex.Measure(length,referenceWidthMm),Source=description};
            });
            return measurement;
        }
    }
}
