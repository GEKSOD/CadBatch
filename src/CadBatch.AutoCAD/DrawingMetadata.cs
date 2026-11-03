using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadBatch.Core;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private static void ReadDrawingTexts(Document doc,string layout,Entity entity,Matrix3d matrix,string path,List<DrawingText> texts)
        {
            void Add(string value,Point3d point){if(string.IsNullOrWhiteSpace(value))return;point=point.TransformBy(matrix);texts.Add(new DrawingText{DocumentKey=DocumentKey(doc),Layout=layout,Path=path,Center=new Point2(point.X,point.Y),Elevation=point.Z,Text=value});}
            if(entity is AttributeDefinition attribute&&!attribute.Constant)return;
            if(entity is Table table)
            {
                for(int row=0;row<table.Rows.Count;row++)for(int col=0;col<table.Columns.Count;col++)
                {
                    using var points=table.Cells[row,col].GetExtents();if(points.Count==0)continue;
                    var center=new Point3d(points.Cast<Point3d>().Average(p=>p.X),points.Cast<Point3d>().Average(p=>p.Y),points.Cast<Point3d>().Average(p=>p.Z));
                    Add(table.Cells[row,col].TextString,center);
                }
            }
            else if(entity is MText mtext)
            {
                var bounds=mtext.GeometricExtents;Add(mtext.Text,new Point3d((bounds.MinPoint.X+bounds.MaxPoint.X)/2,(bounds.MinPoint.Y+bounds.MaxPoint.Y)/2,mtext.Location.Z));
            }
            else if(entity is DBText text)
            {
                var bounds=text.GeometricExtents;Add(text.TextString,new Point3d((bounds.MinPoint.X+bounds.MaxPoint.X)/2,(bounds.MinPoint.Y+bounds.MaxPoint.Y)/2,text.Position.Z));
            }
        }
    }
}
