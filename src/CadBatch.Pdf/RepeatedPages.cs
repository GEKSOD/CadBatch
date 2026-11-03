using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using CadBatch.Core;

namespace CadBatch.Pdf
{
    public static partial class TargetSheetPdf
    {
        // "Копии" in a PDF: each page is repeated as many times as its row asks, so a printed set needs no manual duplication.
        public static void RepeatPages(string path,IReadOnlyList<int> copies,IReadOnlyList<string>? titles=null)
        {
            if(copies.Count==0||copies.Any(c=>c<1||c>999))throw new ArgumentException("Копий должно быть от 1 до 999.");
            if(copies.All(c=>c==1))return;
            using(var dependencies=new FrameworkPdfDependencies())RepeatCore(path,copies,titles);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void RepeatCore(string path,IReadOnlyList<int> copies,IReadOnlyList<string>? titles)
        {
            string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var input=PdfReader.Open(path,PdfDocumentOpenMode.Import))using(var output=new PdfDocument())
                {
                    if(input.PageCount!=copies.Count)throw new InvalidOperationException("Число страниц PDF ("+input.PageCount+") не совпадает с числом листов ("+copies.Count+").");
                    output.Info.Creator="CAD Batch "+ProductInfo.Version;output.Info.Title=string.IsNullOrWhiteSpace(input.Info.Title)?Path.GetFileNameWithoutExtension(path):input.Info.Title;
                    for(int i=0;i<copies.Count;i++)for(int copy=0;copy<copies[i];copy++)
                    {
                        var page=output.AddPage(input.Pages[i]);
                        if(copy==0&&titles!=null&&i<titles.Count)output.Outlines.Add(string.IsNullOrWhiteSpace(titles[i])?"Страница "+(i+1):titles[i],page,true);
                    }
                    output.Save(temp);
                }
                File.Copy(temp,path,true);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
