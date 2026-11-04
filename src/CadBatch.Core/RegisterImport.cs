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
            if(extension==".csv")return ReadCsv(ReadText(File.ReadAllBytes(path)));
            if(extension==".xlsx")return ReadXlsx(path);
            throw new ArgumentException("Выберите ведомость в формате CSV или XLSX.");
        }
        // Excel in Russian Windows saves "CSV" in Windows-1251. Valid UTF-8 (with or without BOM) is read as UTF-8, anything else as 1251.
        public static string ReadText(byte[] bytes)
        {
            if(bytes.Length>=3&&bytes[0]==0xEF&&bytes[1]==0xBB&&bytes[2]==0xBF)return Encoding.UTF8.GetString(bytes,3,bytes.Length-3);
            try{return new UTF8Encoding(false,true).GetString(bytes);}
            catch(DecoderFallbackException)
            {
                var text=new StringBuilder(bytes.Length);foreach(byte b in bytes)text.Append(b<0x80?(char)b:Cp1251[b-0x80]);return text.ToString();
            }
        }
        // Upper half of Windows-1251, generated from the reference code page (0x98 is unassigned).
        private const string Cp1251="\u0402\u0403\u201A\u0453\u201E\u2026\u2020\u2021\u20AC\u2030\u0409\u2039\u040A\u040C\u040B\u040F\u0452\u2018\u2019\u201C\u201D\u2022\u2013\u2014\uFFFD\u2122\u0459\u203A\u045A\u045C\u045B\u045F\u00A0\u040E\u045E\u0408\u00A4\u0490\u00A6\u00A7\u0401\u00A9\u0404\u00AB\u00AC\u00AD\u00AE\u0407\u00B0\u00B1\u0406\u0456\u0491\u00B5\u00B6\u00B7\u0451\u2116\u0454\u00BB\u0458\u0405\u0455\u0457\u0410\u0411\u0412\u0413\u0414\u0415\u0416\u0417\u0418\u0419\u041A\u041B\u041C\u041D\u041E\u041F\u0420\u0421\u0422\u0423\u0424\u0425\u0426\u0427\u0428\u0429\u042A\u042B\u042C\u042D\u042E\u042F\u0430\u0431\u0432\u0433\u0434\u0435\u0436\u0437\u0438\u0439\u043A\u043B\u043C\u043D\u043E\u043F\u0440\u0441\u0442\u0443\u0444\u0445\u0446\u0447\u0448\u0449\u044A\u044B\u044C\u044D\u044E\u044F";
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
