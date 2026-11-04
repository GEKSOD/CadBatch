using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using CadBatch.Core;

namespace CadBatch.Pdf
{
    public static partial class TargetSheetPdf
    {
        // ИУЛ on A4 portrait: document, file with checksum, signatures, and the "-УЛ" designation in the corner.
        public static void WriteIul(IulSheet sheet,string destination)
        {
            using(var dependencies=new FrameworkPdfDependencies())IulCore(sheet,destination);
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void IulCore(IulSheet sheet,string destination)
        {
            WindowsFonts.Install();
            string temp=destination+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var document=new PdfDocument())
                {
                    document.Info.Title="ИУЛ "+sheet.Designation;document.Info.Creator="CAD Batch "+ProductInfo.Version;
                    var page=document.AddPage();page.Width=XUnit.FromMillimeter(210);page.Height=XUnit.FromMillimeter(297);
                    using(var g=XGraphics.FromPdfPage(page))new IulLayout(g,sheet).Draw();
                    document.Save(temp);
                }
                OutputFiles.MoveReplacing(temp,destination);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        private sealed class IulLayout
        {
            private readonly XGraphics g;private readonly IulSheet s;
            private readonly XFont regular=new XFont("Arial",9),bold=new XFont("Arial",9,XFontStyleEx.Bold),title=new XFont("Arial",13,XFontStyleEx.Bold),big=new XFont("Arial",11,XFontStyleEx.Bold);
            private readonly XPen thick=new XPen(XColors.Black,Mm(0.5)),thin=new XPen(XColors.Black,Mm(0.25));
            public IulLayout(XGraphics graphics,IulSheet sheet){g=graphics;s=sheet;}
            private static double Mm(double value)=>value*72/25.4;
            public void Draw()
            {
                // Frame: 20 mm on the left for binding, 5 mm elsewhere (ГОСТ Р 21.101).
                double left=20,top=5,right=205,bottom=292;g.DrawRectangle(thick,Mm(left),Mm(top),Mm(right-left),Mm(bottom-top));
                Text("Информационно-удостоверяющий лист",title,left,18,right-left,true);
                double y=30;
                y=Table(y,new[]{12.0,48,85,40},new[]{"Номер п/п","Обозначение документа","Наименование документа","Номер последнего изменения (версии)"},new[]{"1",s.Designation,s.Title,s.Revision});
                y+=6;
                y=Table(y,new[]{55.0,35,30,65},new[]{"Наименование файла","Дата и время последнего изменения файла","Размер файла, байт","Значение контрольной суммы ("+s.Algorithm+")"},new[]{s.FileName,s.ModifiedText,s.Size.ToString("N0",System.Globalization.CultureInfo.GetCultureInfo("ru-RU")),s.Checksum});
                y+=6;
                var widths=new[]{45.0,60,40,40};y=Row(y,widths,new[]{"Характер работы","Фамилия","Подпись","Дата"},true);
                foreach(var signer in s.Signers)y=Row(y,widths,new[]{signer.Role,signer.Name,"",""},false,12);
                // Title block corner: designation of the sheet itself.
                double bx=right-130,by=bottom-25;g.DrawRectangle(thick,Mm(bx),Mm(by),Mm(130),Mm(25));
                g.DrawLine(thin,Mm(bx),Mm(by+13),Mm(right),Mm(by+13));g.DrawLine(thin,Mm(right-40),Mm(by+13),Mm(right-40),Mm(bottom));g.DrawLine(thin,Mm(right-20),Mm(by+13),Mm(right-20),Mm(bottom));
                Text(s.Designation+"-УЛ",big,bx,by+4,130,true);
                Text("Информационно-удостоверяющий лист",regular,bx+2,by+17,88,false);
                Text("Лист",regular,right-40,by+14.5,20,true);Text("1",bold,right-40,by+19.5,20,true);
                Text("Листов",regular,right-20,by+14.5,20,true);Text("1",bold,right-20,by+19.5,20,true);
            }
            private double Table(double y,double[] widths,string[] headers,string[] values){y=Row(y,widths,headers,true);return Row(y,widths,values,false);}
            private double Row(double y,double[] widths,string[] cells,bool header,double minHeight=0)
            {
                double x=20,pad=1.5;var font=header?bold:regular;var lines=new List<string>[cells.Length];double height=minHeight;
                for(int i=0;i<cells.Length;i++){lines[i]=Wrap(cells[i],font,widths[i]-2*pad);height=Math.Max(height,lines[i].Count*4.2+2*pad);}
                height=Math.Max(height,8);
                for(int i=0;i<cells.Length;i++)
                {
                    g.DrawRectangle(thin,Mm(x),Mm(y),Mm(widths[i]),Mm(height));
                    double ty=y+(height-lines[i].Count*4.2)/2;
                    foreach(string line in lines[i]){Text(line,font,x+pad,ty,widths[i]-2*pad,header);ty+=4.2;}
                    x+=widths[i];
                }
                return y+height;
            }
            private List<string> Wrap(string text,XFont font,double width)
            {
                var result=new List<string>();
                foreach(string paragraph in (text??"").Replace("\r","").Split('\n'))
                {
                    string line="";
                    foreach(string word in paragraph.Split(' '))
                    {
                        string attempt=line.Length==0?word:line+" "+word;
                        if(g.MeasureString(attempt,font).Width<=Mm(width)||line.Length==0){line=attempt;continue;}
                        result.Add(line);line=word;
                    }
                    // A single very long token (file name, checksum) is cut by characters.
                    while(line.Length>1&&g.MeasureString(line,font).Width>Mm(width)){int cut=line.Length-1;while(cut>1&&g.MeasureString(line.Substring(0,cut),font).Width>Mm(width))cut--;result.Add(line.Substring(0,cut));line=line.Substring(cut);}
                    result.Add(line);
                }
                return result;
            }
            private void Text(string text,XFont font,double x,double y,double width,bool center)
            {
                var size=g.MeasureString(text,font);double left=center?Mm(x)+(Mm(width)-size.Width)/2:Mm(x);
                g.DrawString(text,font,XBrushes.Black,left,Mm(y)+size.Height*0.8);
            }
        }
        // Cyrillic text needs real fonts: Arial from the Windows font folder, resolved once per process.
        private sealed class WindowsFonts : IFontResolver
        {
            private static bool installed;
            public static void Install()
            {
                if(installed)return;installed=true;
                try{if(GlobalFontSettings.FontResolver==null)GlobalFontSettings.FontResolver=new WindowsFonts();}catch(InvalidOperationException){}
            }
            public FontResolverInfo? ResolveTypeface(string familyName,bool bold,bool italic)=>new FontResolverInfo(bold?"cadbatch-arial-bold":"cadbatch-arial");
            public byte[]? GetFont(string faceName)
            {
                string folder=Environment.GetFolderPath(Environment.SpecialFolder.Fonts);if(folder.Length==0)folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Fonts");
                return File.ReadAllBytes(Path.Combine(folder,faceName=="cadbatch-arial-bold"?"arialbd.ttf":"arial.ttf"));
            }
        }
    }
}
