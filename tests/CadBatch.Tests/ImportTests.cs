using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CadBatch.Core;
using CadBatch.Pdf;

internal static partial class Program
{
    private static void RunImportTests()
    {
        List<DetectedFrame> Pair()
        {
            var frames=Detect(Candidate("I1",0,0,420,297),Candidate("I2",500,0,420,297)).Frames.ToList();
            frames[0].SheetNumber="1";frames[0].Title="План";frames[0].Code="АР";frames[1].SheetNumber="2";frames[1].Title="Разрез";frames[1].Code="КР";return frames;
        }
        Run("Register groups by several fields with nested headings",()=>
        {
            var frames=Pair();frames[1].Code="АР";
            var rows=new RegisterOptions{Columns=new[]{"Лист","Формат"},GroupBy="Шифр; Формат"}.Rows(frames);
            Assert(rows.Count==5&&rows[1][0]=="Шифр: АР"&&rows[2][0]=="  Формат: "+frames[0].FormatName&&rows[3][0]=="1"&&rows[4][0]=="2");
            Throws<ArgumentException>(()=>new RegisterOptions{GroupBy="А;Б;В;Г;Д"}.Rows(frames));
        });
        string folder=Path.Combine(TestRoot,"artifacts","verification","ImportTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            Run("Register XLSX round trip brings edited titles back by row number",()=>
            {
                var frames=Pair();string path=Path.Combine(folder,"register.xlsx");
                RegisterExport.Xlsx(path,frames,new RegisterOptions{Columns=new[]{"№","Лист","Наименование","Шифр"},GroupBy="Шифр"});
                frames[0].Title="Изменено в CAD Batch";frames[1].SheetNumber="99";
                var changes=RegisterImport.Plan(frames,RegisterImport.Read(path),out int skipped);
                Assert(skipped==0&&changes.Count==2&&changes.Any(c=>c.Field=="Наименование"&&c.New=="План")&&changes.Any(c=>c.Field=="Лист"&&c.New=="2"));
                RegisterImport.Apply(changes);Assert(frames[0].Title=="План"&&frames[1].SheetNumber=="2");
            });
            Run("Register CSV import keeps quotes, separators and formula guards",()=>
            {
                var frames=Pair();
                var rows=RegisterImport.ReadCsv("﻿\"Лист\";\"Наименование\"\r\n\"'=1\";\"План; узлы \"\"А\"\"\"\r\n\"2\";\"Разрез\r\nпродолжение\"\r\n");
                var changes=RegisterImport.Plan(frames,rows,out _);
                Assert(changes.Count==3&&changes.Any(c=>c.Field=="Лист"&&c.New=="=1")&&changes.Any(c=>c.New=="План; узлы \"А\"")&&changes.Any(c=>c.New=="Разрез\r\nпродолжение"||c.New=="Разрез\nпродолжение"));
            });
            Run("Register CSV in Windows-1251 keeps Cyrillic",()=>
            {
                var bytes=new List<byte>();foreach(char ch in "Лист;Наименование\r\n1;План первого этажа ёЁ №\r\n2;Разрез\r\n"){int code=ch;bytes.Add(code<0x80?(byte)code:code==0x401?(byte)0xA8:code==0x451?(byte)0xB8:code==0x2116?(byte)0xB9:(byte)(code-0x410+0xC0));}
                string text=RegisterImport.ReadText(bytes.ToArray());Assert(text.Contains("План первого этажа ёЁ №")&&text.Contains("Разрез"));
                Assert(RegisterImport.ReadText(Encoding.UTF8.GetBytes("Лист;План")).Contains("План")&&RegisterImport.ReadText(new byte[]{0xEF,0xBB,0xBF}.Concat(Encoding.UTF8.GetBytes("Ёлка")).ToArray())=="Ёлка");
                var changes=RegisterImport.Plan(Pair(),RegisterImport.ReadCsv(text),out _);Assert(changes.Any(c=>c.New=="План первого этажа ёЁ №"));
            });
            Run("Register import by order refuses a different number of rows",()=>
            {
                var frames=Pair();Throws<ArgumentException>(()=>RegisterImport.Plan(frames,RegisterImport.ReadCsv("Лист;Наименование\n1;План\n"),out _));
                Throws<ArgumentException>(()=>RegisterImport.Plan(frames,RegisterImport.ReadCsv("Колонка;Другая\n1;2\n"),out _));
            });
            Run("Register CSV export and import skip group headings",()=>
            {
                var frames=Pair();string path=Path.Combine(folder,"register.csv");RegisterExport.Csv(path,frames,new RegisterOptions{Columns=new[]{"Лист","Наименование"},GroupBy="Шифр"});
                frames[1].Title="Другое";var changes=RegisterImport.Plan(frames,RegisterImport.Read(path),out _);Assert(changes.Count==1&&changes[0].New=="Разрез");
            });
            Run("Register XLSX saved by Excel with shared strings is read",()=>
            {
                string path=Path.Combine(folder,"excel.xlsx");
                using(var zip=ZipFile.Open(path,ZipArchiveMode.Create))
                {
                    void Entry(string name,string xml){using(var w=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false)))w.Write(xml);}
                    Entry("xl/sharedStrings.xml","<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>№</t></si><si><t>Наименование</t></si><si><r><t>Новый </t></r><r><t>план</t></r></si></sst>");
                    Entry("xl/worksheets/sheet1.xml","<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"C1\" t=\"s\"><v>1</v></c></row><row r=\"2\"><c r=\"A2\"><v>2</v></c><c r=\"C2\" t=\"s\"><v>2</v></c></row></sheetData></worksheet>");
                }
                var rows=RegisterImport.Read(path);Assert(rows.Count==2&&rows[0].Length==3&&rows[0][2]=="Наименование"&&rows[1][2]=="Новый план");
                var frames=Pair();var changes=RegisterImport.Plan(frames,rows,out _);Assert(changes.Count==1&&changes[0].Frame==frames[1]&&changes[0].New=="Новый план");
            });
            Run("PDF for gluing goes through the PDF runner (size and tiles)",()=>
            {
                string source=Path.Combine(folder,"tile-source.pdf"),size=Path.Combine(folder,"tile-size.txt"),output=Path.Combine(folder,"tile-out.pdf");
                using(var document=new PdfSharp.Pdf.PdfDocument()){var page=document.AddPage();page.Width=PdfSharp.Drawing.XUnit.FromMillimeter(841);page.Height=PdfSharp.Drawing.XUnit.FromMillimeter(594);document.Save(source);}
                PdfRunner.Execute(PdfJob.Size(source,size));var wh=File.ReadAllText(size).Split(' ').Select(v=>double.Parse(v,System.Globalization.CultureInfo.InvariantCulture)).ToArray();
                Assert(Math.Abs(wh[0]-841)<0.5&&Math.Abs(wh[1]-594)<0.5);
                var plan=TilePlan.Create(wh[0],wh[1],210,297,10,5,1);
                PdfRunner.Execute(PdfJob.Tile(source,output,210,297,10,5,1));
                using(var tiles=PdfSharp.Pdf.IO.PdfReader.Open(output,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))Assert(tiles.PageCount==plan.Count&&plan.Count>1);
            });
            Run("PDF pages repeat by copies and keep page order",()=>
            {
                string path=Path.Combine(folder,"copies.pdf");
                using(var document=new PdfSharp.Pdf.PdfDocument())
                {
                    foreach(double width in new[]{100.0,200.0}){var page=document.AddPage();page.Width=PdfSharp.Drawing.XUnit.FromMillimeter(width);page.Height=PdfSharp.Drawing.XUnit.FromMillimeter(50);}
                    document.Save(path);
                }
                TargetSheetPdf.RepeatPages(path,new[]{1,1});using(var same=PdfSharp.Pdf.IO.PdfReader.Open(path,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))Assert(same.PageCount==2);
                TargetSheetPdf.RepeatPages(path,new[]{2,3},new[]{"План","Разрез"});
                using(var result=PdfSharp.Pdf.IO.PdfReader.Open(path,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                {
                    var widths=Enumerable.Range(0,result.PageCount).Select(i=>Math.Round(result.Pages[i].Width.Millimeter)).ToArray();
                    Assert(widths.SequenceEqual(new double[]{100,100,200,200,200}));
                }
                Throws<InvalidOperationException>(()=>TargetSheetPdf.RepeatPages(path,new[]{2,2}));Throws<ArgumentException>(()=>TargetSheetPdf.RepeatPages(path,new[]{0}));
            });
        }
        finally{try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
