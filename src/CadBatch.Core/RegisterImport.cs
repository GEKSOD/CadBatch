using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace CadBatch.Core
{
    public sealed class RegisterChange
    {
        public DetectedFrame Frame {get;set;}=null!;
        public string Field {get;set;}="";
        public string Old {get;set;}="";
        public string New {get;set;}="";
    }
    // Reads a register (CSV or XLSX) edited by hand and brings titles, sheet numbers, codes and revisions back into the job.
    public static class RegisterImport
    {
        public static readonly string[] Editable={"Лист","Наименование","Шифр","Ревизия"};
        public static List<string[]> Read(string path)
        {
            string extension=Path.GetExtension(path).ToLowerInvariant();
            if(extension==".csv")return ReadCsv(File.ReadAllText(path));
            if(extension==".xlsx")return ReadXlsx(path);
            throw new ArgumentException("Выберите ведомость в формате CSV или XLSX.");
        }
        public static List<string[]> ReadCsv(string text)
        {
            text=text.TrimStart('﻿');int firstLine=text.IndexOf('\n');string header=firstLine<0?text:text.Substring(0,firstLine);
            char separator=header.Contains(";")?';':header.Contains("\t")?'\t':',';
            var rows=new List<string[]>();var row=new List<string>();var cell=new StringBuilder();bool quoted=false;
            for(int i=0;i<text.Length;i++)
            {
                char ch=text[i];
                if(quoted){if(ch=='"'){if(i+1<text.Length&&text[i+1]=='"'){cell.Append('"');i++;}else quoted=false;}else cell.Append(ch);continue;}
                if(ch=='"'&&cell.Length==0)quoted=true;
                else if(ch==separator){row.Add(Unguard(cell.ToString()));cell.Clear();}
                else if(ch=='\n'){row.Add(Unguard(cell.ToString().TrimEnd('\r')));cell.Clear();rows.Add(row.ToArray());row.Clear();}
                else cell.Append(ch);
            }
            if(cell.Length>0||row.Count>0){row.Add(Unguard(cell.ToString().TrimEnd('\r')));rows.Add(row.ToArray());}
            return rows;
        }
        // RegisterExport.CsvCell prefixes formula-like values with an apostrophe; the import removes it again.
        private static string Unguard(string value)=>value.Length>1&&value[0]=='\''&&"=+-@\t\r".IndexOf(value[1])>=0?value.Substring(1):value;
        public static List<string[]> ReadXlsx(string path)
        {
            using(var zip=ZipFile.OpenRead(path))
            {
                XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var shared=new List<string>();var strings=zip.GetEntry("xl/sharedStrings.xml");
                if(strings!=null)using(var stream=strings.Open())shared.AddRange(XDocument.Load(stream).Root!.Elements(ns+"si").Select(si=>string.Concat(si.Descendants(ns+"t").Select(t=>t.Value))));
                var sheet=zip.GetEntry("xl/worksheets/sheet1.xml")??zip.Entries.Where(e=>e.FullName.StartsWith("xl/worksheets/",StringComparison.OrdinalIgnoreCase)&&e.FullName.EndsWith(".xml",StringComparison.OrdinalIgnoreCase)).OrderBy(e=>e.FullName,StringComparer.Ordinal).FirstOrDefault();
                if(sheet==null)throw new ArgumentException("В книге Excel нет листа с данными.");
                var rows=new List<string[]>();
                using(var stream=sheet.Open())foreach(var xmlRow in XDocument.Load(stream).Descendants(ns+"row"))
                {
                    var cells=new SortedDictionary<int,string>();int next=0;
                    foreach(var c in xmlRow.Elements(ns+"c"))
                    {
                        int column=ColumnIndex((string?)c.Attribute("r"))??next;next=column+1;string type=(string?)c.Attribute("t")??"";
                        string value=type=="inlineStr"?string.Concat(c.Descendants(ns+"t").Select(t=>t.Value)):(string?)c.Element(ns+"v")??"";
                        if(type=="s"&&int.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out int index))value=index>=0&&index<shared.Count?shared[index]:"";
                        cells[column]=value;
                    }
                    int width=cells.Count==0?0:cells.Keys.Max()+1;var array=new string[width];for(int i=0;i<width;i++)array[i]=cells.TryGetValue(i,out var v)?v:"";rows.Add(array);
                }
                return rows;
            }
        }
        private static int? ColumnIndex(string? reference)
        {
            if(string.IsNullOrEmpty(reference))return null;int value=0,length=0;
            foreach(char ch in reference!){if(ch<'A'||ch>'Z')break;value=value*26+(ch-'A'+1);length++;}
            return length==0?(int?)null:value-1;
        }
        // Rows are matched by the "№" column (row number in CAD Batch) when present, otherwise by the order of checked sheets.
        public static List<RegisterChange> Plan(IList<DetectedFrame> frames,IList<string[]> rows,out int skipped)
        {
            skipped=0;int headerIndex=-1;
            for(int i=0;i<rows.Count&&headerIndex<0;i++)if(rows[i].Any(c=>Editable.Contains(c.Trim(),StringComparer.OrdinalIgnoreCase)))headerIndex=i;
            if(headerIndex<0)throw new ArgumentException("В файле нет столбцов «Лист», «Наименование», «Шифр» или «Ревизия». Выберите ведомость, сохранённую из CAD Batch.");
            var header=rows[headerIndex].Select(c=>c.Trim()).ToArray();int page=Array.FindIndex(header,h=>h=="№");
            var data=rows.Skip(headerIndex+1).Where(r=>r.Any(c=>c.Trim().Length>0)&&!IsHeading(r)).ToList();
            var pairs=new List<(DetectedFrame Frame,string[] Row)>();
            if(page>=0)
            {
                var byPage=frames.GroupBy(f=>f.Page).ToDictionary(g=>g.Key,g=>g.First());
                foreach(var row in data){if(int.TryParse(Cell(row,page),NumberStyles.Integer,CultureInfo.InvariantCulture,out int number)&&byPage.TryGetValue(number,out var frame))pairs.Add((frame,row));else skipped++;}
            }
            else
            {
                var marked=frames.Where(f=>f.Enabled).ToList();
                if(marked.Count!=data.Count)throw new ArgumentException("В ведомости строк: "+data.Count+", а отмечено листов: "+marked.Count+". Отметьте те же листы, что были при сохранении ведомости, или добавьте в ведомость столбец «№».");
                for(int i=0;i<data.Count;i++)pairs.Add((marked[i],data[i]));
            }
            var changes=new List<RegisterChange>();
            foreach(var pair in pairs)foreach(string field in Editable)
            {
                int column=Array.FindIndex(header,h=>string.Equals(h,field,StringComparison.OrdinalIgnoreCase));if(column<0)continue;
                string value=Cell(pair.Row,column).Trim(),old=Get(pair.Frame,field);if(value!=old)changes.Add(new RegisterChange{Frame=pair.Frame,Field=field,Old=old,New=value});
            }
            return changes;
        }
        public static void Apply(IEnumerable<RegisterChange> changes)
        {
            foreach(var change in changes)
                switch(change.Field){case "Лист":change.Frame.SheetNumber=change.New;break;case "Наименование":change.Frame.Title=change.New;break;case "Шифр":change.Frame.Code=change.New;break;case "Ревизия":change.Frame.Revision=change.New;break;}
        }
        private static string Get(DetectedFrame f,string field)=>field=="Лист"?f.SheetNumber:field=="Наименование"?f.Title:field=="Шифр"?f.Code:f.Revision;
        private static string Cell(string[] row,int index)=>index<row.Length?row[index]??"":"";
        // Group headings written by RegisterOptions: "Шифр: АР" in the first cell, the rest empty.
        private static bool IsHeading(string[] row)=>row.Length>0&&row[0].Contains(": ")&&row.Skip(1).All(c=>c.Trim().Length==0);
    }
}
