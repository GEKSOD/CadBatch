using System;
using System.IO;
using System.Runtime.CompilerServices;
using PdfSharp.Drawing;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using CadBatch.Core;

namespace CadBatch.Pdf
{
    public static partial class TargetSheetPdf
    {
        public static void RestoreCentered(string source,string destination,double widthMm,double heightMm,double nativeScale)
        {
            if(!Numeric.Positive(widthMm)||!Numeric.Positive(heightMm)||!Numeric.Positive(nativeScale)||nativeScale>1)throw new ArgumentException("Некорректный размер PDF или масштаб промежуточного носителя.");
            if(nativeScale==1){CropCentered(source,destination,widthMm,heightMm);return;}
            using(var dependencies=new FrameworkPdfDependencies())RestoreCore(source,destination,widthMm,heightMm,nativeScale);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RestoreCore(string source,string destination,double widthMm,double heightMm,double nativeScale)
        {
            string normalized=destination+"."+Guid.NewGuid().ToString("N")+".source.pdf",temporary=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                var sheet=ReadSheet(source);
                if(widthMm*nativeScale>sheet.DisplayWidthMm+.5||heightMm*nativeScale>sheet.DisplayHeightMm+.5)throw new ArgumentException("Промежуточный PDF меньше согласованного окна.");
                using(var input=PdfReader.Open(source,PdfDocumentOpenMode.Import))using(var flat=new PdfDocument())
                {var page=flat.AddPage(input.Pages[0]);page.Rotate=0;page.MediaBox=sheet.Box;page.CropBox=sheet.Box;flat.Save(normalized);}
                using(var form=XPdfForm.FromFile(normalized))using(var document=new PdfDocument())
                {
                    const double points=72/25.4;var page=document.AddPage();page.Width=XUnit.FromMillimeter(widthMm);page.Height=XUnit.FromMillimeter(heightMm);form.PageNumber=1;
                    document.Info.Creator="CAD Batch "+ProductInfo.Version;document.Info.Title=Path.GetFileNameWithoutExtension(destination);
                    using(var graphics=XGraphics.FromPdfPage(page))
                    {
                        double left=(widthMm-sheet.DisplayWidthMm/nativeScale)*points/2,top=(heightMm-sheet.DisplayHeightMm/nativeScale)*points/2;
                        double rawWidth=sheet.WidthMm/nativeScale*points,rawHeight=sheet.HeightMm/nativeScale*points;
                        if(sheet.Rotation==90){graphics.TranslateTransform(left+rawHeight,top);graphics.RotateTransform(90);}
                        else if(sheet.Rotation==180){graphics.TranslateTransform(left+rawWidth,top+rawHeight);graphics.RotateTransform(180);}
                        else if(sheet.Rotation==270){graphics.TranslateTransform(left,top+rawWidth);graphics.RotateTransform(270);}
                        else graphics.TranslateTransform(left,top);
                        graphics.DrawImage(form,0,0,rawWidth,rawHeight);
                    }
                    document.Save(temporary);
                }
                if(File.Exists(destination))File.Replace(temporary,destination,destination+".bak");else File.Move(temporary,destination);
            }
            finally{if(File.Exists(normalized))File.Delete(normalized);if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
