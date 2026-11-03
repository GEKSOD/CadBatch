using System;
using System.Runtime.CompilerServices;
using System.Collections.Generic;
using System.Linq;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;
using CadBatch.Core;

namespace CadBatch.Pdf
{
    public static partial class TargetSheetPdf
    {
        public static void Merge(IReadOnlyList<string> sources,string destination,IReadOnlyList<string> titles,string? documentTitle=null)
        {
            if(sources.Count==0||sources.Count!=titles.Count)throw new ArgumentException("Некорректный список страниц PDF.");
            using(var dependencies=new FrameworkPdfDependencies())MergeCore(sources,destination,titles,documentTitle);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void MergeCore(IReadOnlyList<string> sources,string destination,IReadOnlyList<string> titles,string? documentTitle)
        {
            using(var output=new PdfDocument())
            {
                output.Info.Creator="CAD Batch "+ProductInfo.Version;output.Info.Title=documentTitle??System.IO.Path.GetFileNameWithoutExtension(destination);
                for(int i=0;i<sources.Count;i++)using(var input=PdfReader.Open(sources[i],PdfDocumentOpenMode.Import))
                {
                    if(input.PageCount!=1)throw new InvalidOperationException("Ожидался одностраничный PDF: "+sources[i]);
                    var page=output.AddPage(input.Pages[0]);output.Outlines.Add(string.IsNullOrWhiteSpace(titles[i])?"Страница "+(i+1):titles[i],page,true);
                }
                output.Save(destination);
            }
        }
        public static void CropCentered(string source,string destination,double widthMm,double heightMm)
        {
            using(var dependencies=new FrameworkPdfDependencies())CropCore(source,destination,widthMm,heightMm);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void CropCore(string source,string destination,double widthMm,double heightMm)
        {
            using(var document=PdfReader.Open(source,PdfDocumentOpenMode.Modify))
            {
                if(document.PageCount!=1)throw new InvalidOperationException("Ожидалась одна промежуточная страница PDF.");
                var page=document.Pages[0];var media=page.MediaBox;
                var bounds=PdfCropBounds.Centered(media.X1,media.Y1,media.X2,media.Y2,page.Rotate,widthMm,heightMm);
                var crop=new PdfRectangle(new XRect(bounds.Left,bounds.Bottom,bounds.Width,bounds.Height));
                page.MediaBox=crop;page.CropBox=crop;
                page.Elements.SetRectangle("/TrimBox",crop);page.Elements.SetRectangle("/BleedBox",crop);page.Elements.SetRectangle("/ArtBox",crop);
                document.Save(destination);
            }
        }
    }
}
