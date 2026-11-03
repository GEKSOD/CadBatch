using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml;

namespace CadBatch.Core
{
    public static class RegisterExport
    {
        private static readonly string[] Header={"Лист","Наименование","Шифр","Формат","Чертёж","Вкладка"};
        private static List<string[]> Rows(IEnumerable<DetectedFrame> frames)=>new[]{Header}.Concat(frames.Where(f=>f.Enabled).Select(f=>new[]{f.SheetNumber,f.Title,f.Code,f.FormatName,f.File,f.Layout})).ToList();
        public static string CsvCell(string value)
        {
            if(value.Length>0&&"=+-@\t\r".IndexOf(value[0])>=0)value="'"+value;
            return "\""+value.Replace("\"","\"\"")+"\"";
        }
        public static void Csv(string path,IEnumerable<DetectedFrame> frames,RegisterOptions? options=null)
        {
            Atomic(path,temp=>File.WriteAllLines(temp,(options??new RegisterOptions()).Rows(frames).Select(row=>string.Join(";",row.Select(CsvCell))),new UTF8Encoding(true)));
        }
        public static void Xlsx(string path,IEnumerable<DetectedFrame> frames,RegisterOptions? options=null)
        {
            var rows=(options??new RegisterOptions()).Rows(frames);Atomic(path,temp=>
            {
                using(var file=File.Create(temp))using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
                {
                    Raw(zip,"[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/></Types>");
                    Raw(zip,"_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                    Raw(zip,"xl/workbook.xml","<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Ведомость\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
                    Raw(zip,"xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
                    Raw(zip,"xl/styles.xml","<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf><xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment vertical=\"top\" wrapText=\"1\"/></xf></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
                    const string ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                    using(var stream=zip.CreateEntry("xl/worksheets/sheet1.xml").Open())using(var w=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false}))
                    {
                        w.WriteStartDocument();w.WriteStartElement("worksheet",ns);w.WriteStartElement("sheetViews",ns);w.WriteStartElement("sheetView",ns);w.WriteAttributeString("workbookViewId","0");w.WriteStartElement("pane",ns);w.WriteAttributeString("ySplit","1");w.WriteAttributeString("topLeftCell","A2");w.WriteAttributeString("activePane","bottomLeft");w.WriteAttributeString("state","frozen");w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();
                        w.WriteStartElement("cols",ns);for(int i=0;i<rows[0].Length;i++){w.WriteStartElement("col",ns);w.WriteAttributeString("min",(i+1).ToString());w.WriteAttributeString("max",(i+1).ToString());w.WriteAttributeString("width",i==1?"65":i==4?"40":"20");w.WriteAttributeString("customWidth","1");w.WriteEndElement();}w.WriteEndElement();
                        w.WriteStartElement("sheetData",ns);
                        for(int r=0;r<rows.Count;r++)
                        {
                            w.WriteStartElement("row",ns);w.WriteAttributeString("r",(r+1).ToString());
                            for(int c=0;c<rows[r].Length;c++)
                            {
                                w.WriteStartElement("c",ns);w.WriteAttributeString("r",((char)('A'+c)).ToString()+(r+1));w.WriteAttributeString("t","inlineStr");w.WriteAttributeString("s",r==0?"1":"0");
                                w.WriteStartElement("is",ns);w.WriteStartElement("t",ns);w.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");w.WriteString(rows[r][c]);w.WriteEndElement();w.WriteEndElement();w.WriteEndElement();
                            }
                            w.WriteEndElement();
                        }
                        w.WriteEndElement();w.WriteStartElement("autoFilter",ns);w.WriteAttributeString("ref","A1:"+((char)('A'+rows[0].Length-1))+rows.Count);w.WriteEndElement();w.WriteEndElement();w.WriteEndDocument();
                    }
                }
            });
        }
        private static void Raw(ZipArchive zip,string name,string xml){using(var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(false)))writer.Write("<?xml version=\"1.0\" encoding=\"utf-8\"?>"+xml);}
        private static void Atomic(string path,Action<string> write)
        {
            string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{write(temp);if(File.Exists(full))File.Replace(temp,full,full+".bak");else File.Move(temp,full);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
