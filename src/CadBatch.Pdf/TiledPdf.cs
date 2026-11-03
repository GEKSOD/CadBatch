using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using CadBatch.Core;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace CadBatch.Pdf
{
    public static partial class TargetSheetPdf
    {
        public static TilePlan PlanTiles(string source,double sheetWidth,double sheetHeight,double margin,double overlap,double scale)
        {
            using(var dependencies=new FrameworkPdfDependencies())return PlanTilesCore(source,sheetWidth,sheetHeight,margin,overlap,scale);
        }
        private sealed class SourceSheet
        {
            public double WidthMm,HeightMm;public int Rotation;public PdfRectangle Box=null!;
            public double DisplayWidthMm=>Rotation==90||Rotation==270?HeightMm:WidthMm;
            public double DisplayHeightMm=>Rotation==90||Rotation==270?WidthMm:HeightMm;
        }
        private static SourceSheet ReadSheet(string source)
        {
            using(var pdf=PdfReader.Open(source,PdfDocumentOpenMode.Import))
            {
                if(pdf.PageCount!=1)throw new ArgumentException("Для разбивки выберите одностраничный PDF большой рамки.");
                var page=pdf.Pages[0];var box=page.CropBox;if(box.Width<=0||box.Height<=0)box=page.MediaBox;
                int rotation=((page.Rotate%360)+360)%360;if(rotation%90!=0)throw new ArgumentException("Неподдерживаемый поворот страницы PDF.");
                double unit=page.Elements.GetReal("/UserUnit");if(unit!=0&&Math.Abs(unit-1)>.000001)throw new ArgumentException("PDF с нестандартным UserUnit требует предварительного приведения к обычным единицам.");
                return new SourceSheet{WidthMm=box.Width*25.4/72,HeightMm=box.Height*25.4/72,Rotation=rotation,Box=box};
            }
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static TilePlan PlanTilesCore(string source,double width,double height,double margin,double overlap,double scale)
        {
            var sheet=ReadSheet(source);return TilePlan.Create(sheet.DisplayWidthMm,sheet.DisplayHeightMm,width,height,margin,overlap,scale);
        }
        public static void Tile(string source,string destination,TilePlan plan,CancellationToken cancellation=default)
        {
            using(var dependencies=new FrameworkPdfDependencies())TileCore(source,destination,plan,cancellation);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void TileCore(string source,string destination,TilePlan plan,CancellationToken cancellation)
        {
            string temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";string normalized=temporary+".source.pdf";
            try
            {
                var sheet=ReadSheet(source);
                var actual=TilePlan.Create(sheet.DisplayWidthMm,sheet.DisplayHeightMm,plan.SheetWidthMm,plan.SheetHeightMm,plan.MarginMm,plan.OverlapMm,plan.Scale);
                if(Math.Abs(actual.ContentWidthMm-plan.ContentWidthMm)>.01||Math.Abs(actual.ContentHeightMm-plan.ContentHeightMm)>.01)throw new InvalidOperationException("Исходный PDF изменился после планирования.");
                using(var input=PdfReader.Open(source,PdfDocumentOpenMode.Import))using(var flat=new PdfDocument())
                {
                    var page=flat.AddPage(input.Pages[0]);page.Rotate=0;page.MediaBox=sheet.Box;page.CropBox=sheet.Box;flat.Save(normalized);
                }
                using(var form=XPdfForm.FromFile(normalized))using(var document=new PdfDocument())
                {
                    form.PageNumber=1;
                    const double points=72/25.4;double margin=plan.MarginMm*points;int index=0;
                    document.Info.Creator="CAD Batch "+ProductInfo.Version;document.Info.Title=Path.GetFileNameWithoutExtension(destination);
                    for(int row=0;row<plan.Rows;row++)for(int col=0;col<plan.Columns;col++)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var page=document.AddPage();page.Width=XUnit.FromMillimeter(plan.SheetWidthMm);page.Height=XUnit.FromMillimeter(plan.SheetHeightMm);
                        using(var g=XGraphics.FromPdfPage(page))
                        {
                            var state=g.Save();g.IntersectClip(new XRect(margin,margin,plan.UsableWidthMm*points,plan.UsableHeightMm*points));
                            double left=margin-plan.X(col)*points,top=margin-plan.Y(row)*points;
                            double rawWidth=sheet.WidthMm*plan.Scale*points,rawHeight=sheet.HeightMm*plan.Scale*points;
                            if(sheet.Rotation==90){g.TranslateTransform(left+rawHeight,top);g.RotateTransform(90);}
                            else if(sheet.Rotation==180){g.TranslateTransform(left+rawWidth,top+rawHeight);g.RotateTransform(180);}
                            else if(sheet.Rotation==270){g.TranslateTransform(left,top+rawWidth);g.RotateTransform(270);}
                            else g.TranslateTransform(left,top);
                            g.DrawImage(form,0,0,rawWidth,rawHeight);g.Restore(state);
                            var pen=new XPen(XColors.Gray,.3);double right=margin+plan.UsableWidthMm*points,bottom=margin+plan.UsableHeightMm*points;double mark=Math.Min(4,plan.MarginMm)*points;
                            foreach(double x in new[]{margin,right})foreach(double y in new[]{margin,bottom})
                            {g.DrawLine(pen,x-mark,y,x+mark,y);g.DrawLine(pen,x,y-mark,x,y+mark);}
                            if(plan.MarginMm>=5)DrawNumber(g,(++index).ToString("000"),margin,2*points,points);
                            else index++;
                        }
                        document.Outlines.Add("Ряд "+(row+1)+", столбец "+(col+1),page,false);
                    }
                    document.Save(temporary);
                }
                if(File.Exists(destination))File.Replace(temporary,destination,destination+".bak");else File.Move(temporary,destination);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);if(File.Exists(normalized))File.Delete(normalized);}
        }
        private static void DrawNumber(XGraphics g,string value,double x,double y,double unit)
        {
            // Vector seven-segment digits avoid changing the host's global font resolver.
            int[] masks={0x3f,0x06,0x5b,0x4f,0x66,0x6d,0x7d,0x07,0x7f,0x6f};
            double[,] lines={{0,0,1,0},{1,0,1,1},{1,1,1,2},{0,2,1,2},{0,1,0,2},{0,0,0,1},{0,1,1,1}};
            var pen=new XPen(XColors.Gray,.4);
            foreach(char digit in value)
            {
                for(int s=0;s<7;s++)if((masks[digit-'0']&(1<<s))!=0)g.DrawLine(pen,x+lines[s,0]*unit,y+lines[s,1]*unit,x+lines[s,2]*unit,y+lines[s,3]*unit);
                x+=unit*1.6;
            }
        }
    }
}
