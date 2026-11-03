using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace CadBatch.Core
{
    public enum PdfOutputMode { Separate,OneFile,ByDocument,ByFormat,ByCode }
    public sealed class PdfGroup
    {
        public string Path {get;set;}="";
        public List<OutputItem> Items {get;set;}=new List<OutputItem>();
    }
    public static class PdfGroupPlanner
    {
        public static List<PdfGroup> Build(IReadOnlyList<OutputItem> items,PdfOutputMode mode,string folder,string name)
        {
            if(items.Count==0)throw new ArgumentException("Нет страниц для объединения.");
            if(!Enum.IsDefined(typeof(PdfOutputMode),mode))throw new ArgumentException("Неизвестный режим PDF.");
            if(mode==PdfOutputMode.Separate)return items.Select(i=>new PdfGroup{Path=i.Path,Items=new List<OutputItem>{i}}).ToList();
            if(!Path.IsPathRooted(folder))throw new ArgumentException("Выберите папку PDF.");
            var groups=items.GroupBy(i=>mode==PdfOutputMode.ByDocument?i.Frame.Source.DocumentPath+"|"+i.Frame.Source.DocumentKey:
                mode==PdfOutputMode.ByFormat?i.Frame.FormatName:mode==PdfOutputMode.ByCode?i.Frame.Code:"",StringComparer.OrdinalIgnoreCase);
            var occupied=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var output=new List<PdfGroup>();
            foreach(var group in groups)
            {
                string key=mode==PdfOutputMode.ByDocument?Path.GetFileNameWithoutExtension(group.First().Frame.File):
                    mode==PdfOutputMode.OneFile?name:group.Key;
                key=(key??"").Trim();if(mode==PdfOutputMode.OneFile&&key.EndsWith(".pdf",StringComparison.OrdinalIgnoreCase))key=key.Substring(0,key.Length-4);
                if(string.IsNullOrWhiteSpace(key))key=mode==PdfOutputMode.ByCode?"Без шифра":"Комплект";
                key=OutputPlanner.Sanitize(key);if(key.Length>120)key=key.Substring(0,120);
                string path=Path.Combine(folder,key+".pdf");int suffix=2;
                while(occupied.Contains(path)||File.Exists(path)||Directory.Exists(path))path=Path.Combine(folder,key+" ("+(suffix++)+").pdf");
                if(path.Length>240)throw new ArgumentException("Слишком длинный путь PDF.");
                occupied.Add(path);output.Add(new PdfGroup{Path=path,Items=group.ToList()});
            }
            return output;
        }
    }
    public sealed class NaturalTextComparer : IComparer<string>
    {
        public static readonly NaturalTextComparer Instance=new NaturalTextComparer();
        public int Compare(string? a,string? b)
        {
            a=a??"";b=b??"";if(a.Length==0||b.Length==0)return a.Length==b.Length?0:a.Length==0?1:-1;
            var aa=Regex.Matches(a,@"\d+|\D+");var bb=Regex.Matches(b,@"\d+|\D+");
            for(int i=0;i<Math.Min(aa.Count,bb.Count);i++)
            {
                string x=aa[i].Value,y=bb[i].Value;int compare;
                if(char.IsDigit(x[0])&&char.IsDigit(y[0]))
                {
                    x=x.TrimStart('0');y=y.TrimStart('0');compare=x.Length.CompareTo(y.Length);
                    if(compare==0)compare=StringComparer.Ordinal.Compare(x,y);
                }
                else compare=StringComparer.OrdinalIgnoreCase.Compare(x,y);
                if(compare!=0)return compare;
            }
            return aa.Count.CompareTo(bb.Count);
        }
    }
    public enum FrameSort { RowsLeftToRight,RowsRightToLeft,RowsBottomUp,ColumnsTopDown,ColumnsBottomUp,SheetNumber,Format,Document,Code }
    public static class FrameOrder
    {
        public static void PreserveEdits(IEnumerable<DetectedFrame> previous,IEnumerable<DetectedFrame> fresh)
        {
            var old=previous.Where(f=>f.Available).ToLookup(f=>f.Source.Key,StringComparer.Ordinal);
            foreach(var frame in fresh)
            {
                var before=old[frame.Source.Key].FirstOrDefault(f=>f.Geometry.SameBoundary(frame.Geometry,.02));if(before==null)continue;
                if(before.Revision!=(before.OriginalRevision.Length>0?before.OriginalRevision:FrameDetection.ReadAttribute(before.Source,"REVISION","REV","РЕВИЗИЯ","ИЗМ")))frame.Revision=before.Revision;
                frame.Enabled=before.Enabled;frame.Copies=before.Copies;frame.Device=before.Device;frame.StyleSheet=before.StyleSheet;
                if(before.SheetNumber!=(before.OriginalSheetNumber.Length>0?before.OriginalSheetNumber:FrameDetection.ReadAttribute(before.Source,"SHEET","SHEETNUMBER","ЛИСТ","НОМЕР_ЛИСТА")))frame.SheetNumber=before.SheetNumber;
                if(before.Title!=(before.OriginalTitle.Length>0?before.OriginalTitle:FrameDetection.ReadAttribute(before.Source,"TITLE","НАИМЕНОВАНИЕ","NAME")))frame.Title=before.Title;
                if(before.Code!=(before.OriginalCode.Length>0?before.OriginalCode:FrameDetection.ReadAttribute(before.Source,"CODE","ШИФР","ОБОЗНАЧЕНИЕ")))frame.Code=before.Code;
            }
        }
        public static List<DetectedFrame> Sort(IEnumerable<DetectedFrame> source,FrameSort mode,double tolerance)
        {
            if(!Numeric.NonNegative(tolerance))throw new ArgumentException("Допуск сортировки должен быть неотрицательным.");
            var rows=source.ToList();IEnumerable<DetectedFrame> sorted;
            if(mode==FrameSort.SheetNumber)sorted=rows.OrderBy(f=>f.SheetNumber,NaturalTextComparer.Instance);
            else if(mode==FrameSort.Format)sorted=rows.OrderBy(f=>f.FormatName,NaturalTextComparer.Instance);
            else if(mode==FrameSort.Document)sorted=rows.OrderBy(f=>f.Source.DocumentPath,NaturalTextComparer.Instance).ThenBy(f=>f.Layout,NaturalTextComparer.Instance);
            else if(mode==FrameSort.Code)sorted=rows.OrderBy(f=>f.Code,NaturalTextComparer.Instance);
            else
            {
                var result=new List<DetectedFrame>();bool columns=mode==FrameSort.ColumnsTopDown||mode==FrameSort.ColumnsBottomUp;
                foreach(var space in rows.GroupBy(f=>f.Source.DocumentKey+"|"+f.Layout))
                {
                    var remaining=(columns?space.OrderBy(f=>f.Geometry.Center.X):space.OrderByDescending(f=>f.Geometry.Center.Y)).ToList();
                    var groups=new List<List<DetectedFrame>>();
                    while(remaining.Count>0)
                    {
                        // Fixed anchor makes grouping transitive and independent of sort comparator calls.
                        double anchor=columns?remaining[0].Geometry.Center.X:remaining[0].Geometry.Center.Y;
                        var line=remaining.Where(f=>Math.Abs((columns?f.Geometry.Center.X:f.Geometry.Center.Y)-anchor)<=tolerance).ToList();
                        var members=new HashSet<DetectedFrame>(line);remaining.RemoveAll(members.Contains);
                        groups.Add((columns?(mode==FrameSort.ColumnsBottomUp?line.OrderBy(f=>f.Geometry.Center.Y):line.OrderByDescending(f=>f.Geometry.Center.Y)):
                            mode==FrameSort.RowsRightToLeft?line.OrderByDescending(f=>f.Geometry.Center.X):line.OrderBy(f=>f.Geometry.Center.X)).ToList());
                    }
                    if(mode==FrameSort.RowsBottomUp)groups.Reverse();foreach(var group in groups)result.AddRange(group);
                }
                sorted=result;
            }
            var output=sorted.ToList();Repage(output);return output;
        }
        public static void Repage(IList<DetectedFrame> frames) {for(int i=0;i<frames.Count;i++)frames[i].Page=i+1;}
        public static void Move(IList<DetectedFrame> rows,ISet<DetectedFrame> selected,int direction)
        {
            if(direction==-1)for(int i=1;i<rows.Count;i++){if(selected.Contains(rows[i])&&!selected.Contains(rows[i-1])){var f=rows[i];rows[i]=rows[i-1];rows[i-1]=f;}}
            else if(direction==1)for(int i=rows.Count-2;i>=0;i--){if(selected.Contains(rows[i])&&!selected.Contains(rows[i+1])){var f=rows[i];rows[i]=rows[i+1];rows[i+1]=f;}}
            else throw new ArgumentException("Направление должно быть -1 или 1.");
            Repage(rows);
        }
    }
    public sealed class NumberChange
    {
        public DetectedFrame Frame {get;set;}=null!;
        public string OldNumber {get;set;}="";
        public string NewNumber {get;set;}="";
    }
    public static class Numbering
    {
        public static List<NumberChange> Plan(IEnumerable<DetectedFrame> source,int start,int step,string prefix,string suffix,int digits)
        {
            if(start<0||step<1||digits<0||digits>12)throw new ArgumentException("Проверьте начало, шаг и число цифр.");
            var result=new List<NumberChange>();long value=start;
            foreach(var frame in source.Where(f=>f.Enabled))
            {
                if(value>int.MaxValue)throw new ArgumentException("Нумерация превышает допустимый диапазон.");
                result.Add(new NumberChange{Frame=frame,OldNumber=frame.SheetNumber,NewNumber=prefix+value.ToString(digits==0?"0":new string('0',digits),CultureInfo.InvariantCulture)+suffix});value+=step;
            }
            return result;
        }
        public static string[] Warnings(IEnumerable<DetectedFrame> source)
        {
            var rows=source.Where(f=>f.Enabled).ToArray();var warnings=new List<string>();
            if(rows.Any(f=>string.IsNullOrWhiteSpace(f.SheetNumber)))warnings.Add("Есть пустые номера листов.");
            foreach(var g in rows.Where(f=>!string.IsNullOrWhiteSpace(f.SheetNumber)).GroupBy(f=>f.Source.DocumentKey+"|"+f.SheetNumber.Trim(),StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1))
                warnings.Add("Повтор номера: "+g.First().SheetNumber+" ("+g.Count()+")");
            return warnings.ToArray();
        }
    }
    public enum CadOperation { CreateLayouts,ExportDwg,InsertRegister,WriteAttributes,CreateFrame,MergeDrawings,InsertRasters,RenameLayouts,ReorderLayouts,ExportLayouts,PackageDrawing,WriteDsd,PublishDsd,InsertFrameTemplate }
    public sealed class CadOperationRequest
    {
        public CadOperation Operation {get;set;}
        public List<DetectedFrame> Frames {get;set;}=new List<DetectedFrame>();
        public List<OutputItem> Items {get;set;}=new List<OutputItem>();
        public string Folder {get;set;}="";
        public string Device {get;set;}="DWG To PDF.pc3";
        public string NamePattern {get;set;}="CB_{Page}_{SheetNumber}";
        public bool Crossing {get;set;}
        public string NumberTag {get;set;}="SHEET";
        public string TotalTag {get;set;}="SHEETS";
        public string TitleTag {get;set;}="TITLE";
        public string CodeTag {get;set;}="CODE";
        public bool WriteTitles {get;set;}
        public bool WriteTotals {get;set;}
        public bool ApplyChanges {get;set;}
        public FormatDefinition Format {get;set;}=new FormatDefinition("А3",420,297);
        public double Scale {get;set;}=1;
        public string[] Files {get;set;}=Array.Empty<string>();
        public int Columns {get;set;}=1;
        public double Spacing {get;set;}=100;
    }
    public sealed class CadOperationResult
    {
        public int Changed {get;set;}
        public List<string> Details {get;}=new List<string>();
        public List<string> Errors {get;}=new List<string>();
        public List<string> Files {get;}=new List<string>();
        public bool Cancelled {get;set;}
    }
    [DataContract] public sealed class BatchJob
    {
        [DataMember] public int Version {get;set;}=1;
        [DataMember] public ProjectProfile Profile {get;set;}=new ProjectProfile();
        [DataMember] public List<JobRow> Rows {get;set;}=new List<JobRow>();
    }
    [DataContract] public sealed class JobRow
    {
        [DataMember] public string DocumentPath {get;set;}="";
        [DataMember] public string Layout {get;set;}="";
        [DataMember] public string HandlePath {get;set;}="";
        [DataMember] public double[][] Corners {get;set;}=Array.Empty<double[]>();
        [DataMember] public bool Enabled {get;set;}=true;
        [DataMember(EmitDefaultValue=false)] public string? Revision {get;set;}
        [DataMember(EmitDefaultValue=false)] public Dictionary<string,string>? Metadata {get;set;}
        [DataMember] public string Number {get;set;}="";
        [DataMember] public string Title {get;set;}="";
        [DataMember] public string Code {get;set;}="";
        [DataMember] public int Copies {get;set;}=1;
        [DataMember] public string Device {get;set;}="";
        [DataMember] public string StyleSheet {get;set;}="";
        [DataMember(EmitDefaultValue=false)] public double? Scale {get;set;}
        [DataMember(EmitDefaultValue=false)] public List<JobStampPart>? StampParts {get;set;}
    }
    [DataContract] public sealed class JobStampPart
    {
        [DataMember] public string Path {get;set;}="";
        [DataMember] public int Index {get;set;}
        [DataMember] public double[] Coordinates {get;set;}=Array.Empty<double>();
    }
    public static class JobStore
    {
        public static BatchJob Capture(ProjectProfile profile,IEnumerable<DetectedFrame> frames)=>new BatchJob{Profile=profile,Rows=frames.Select(f=>new JobRow{
            DocumentPath=f.Source.DocumentPath,Layout=f.Layout,HandlePath=f.Source.HandlePath,Corners=f.Geometry.Corners.Select(p=>new[]{p.X,p.Y}).ToArray(),Enabled=f.Enabled,
            Number=f.SheetNumber,Title=f.Title,Code=f.Code,Revision=f.Revision,Metadata=new Dictionary<string,string>(f.Metadata),Copies=f.Copies,Device=f.Device,StyleSheet=f.StyleSheet,Scale=f.Scale,
            StampParts=f.StampEvidence?.Members.Select(s=>new JobStampPart{Path=s.Path,Index=s.Index,Coordinates=new[]{s.Start.X,s.Start.Y,s.End.X,s.End.Y,s.Elevation}}).ToList()}).ToList()};
        public static void Save(string path,BatchJob job)
        {
            Validate(job);string full=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(full)!);string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var file=File.Create(temp)){new DataContractJsonSerializer(typeof(BatchJob)).WriteObject(file,job);file.Flush(true);}
                if(File.Exists(full))File.Replace(temp,full,full+".bak");else File.Move(temp,full);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public static BatchJob Load(string path)
        {
            using(var file=File.OpenRead(path)){var job=(BatchJob)new DataContractJsonSerializer(typeof(BatchJob)).ReadObject(file)!;Validate(job);job.Profile.UpgradeCatalog();return job;}
        }
        private static void Validate(BatchJob job)
        {
            if(job==null||job.Version!=1||job.Profile==null||job.Rows==null)throw new ArgumentException("Неподдерживаемое или повреждённое задание.");job.Profile.Validate();
            if(job.Rows.Any(r=>r==null||r.Corners==null||r.Corners.Length!=4||r.Corners.Any(p=>p==null||p.Length!=2)||
                !Rectangle2.TryCreate(r.Corners.Select(p=>new Point2(p[0],p[1])),.01,out _)||r.Copies<1||r.Copies>999||
                r.Scale.HasValue&&!Numeric.Positive(r.Scale.Value)||r.StampParts!=null&&r.StampParts.Any(p=>p==null||p.Coordinates==null||p.Coordinates.Length!=5||p.Coordinates.Any(n=>double.IsNaN(n)||double.IsInfinity(n)))))throw new ArgumentException("Повреждены строки задания.");
        }
        public static List<DetectedFrame> Restore(BatchJob job,IEnumerable<DetectedFrame> live,out List<string> warnings)
        {
            Validate(job);warnings=new List<string>();var available=live.ToLookup(f=>Key(f.Source.DocumentPath,f.Layout,f.Source.HandlePath),StringComparer.OrdinalIgnoreCase);var result=new List<DetectedFrame>();
            foreach(var row in job.Rows)
            {
                Rectangle2.TryCreate(row.Corners.Select(p=>new Point2(p[0],p[1])),.01,out var old);
                var frame=available[Key(row.DocumentPath,row.Layout,row.HandlePath)].FirstOrDefault(f=>f.Geometry.SameBoundary(old!,.02)&&
                    (!row.Scale.HasValue||Math.Abs(f.Scale-row.Scale.Value)<=Math.Max(1e-9,row.Scale.Value*1e-6))&&StampMatches(row,f));
                if(frame==null)
                {
                    frame=new DetectedFrame{Source=new FrameCandidate{DocumentPath=row.DocumentPath,DocumentName=Path.GetFileName(row.DocumentPath),Layout=row.Layout,HandlePath=row.HandlePath},
                        Geometry=old!,Scale=1,Format=new FormatDefinition("?",old!.Width,old.Height),Available=false,Enabled=false,Status="Источник не найден или изменён — повторите поиск"};
                    warnings.Add(row.DocumentPath+" / "+row.Layout+" / "+row.HandlePath+": не найден или изменён");
                }
                else frame.Enabled=row.Enabled;
                frame.Revision=row.Revision??"";frame.Metadata=row.Metadata??new Dictionary<string,string>();frame.SheetNumber=row.Number;frame.Title=row.Title;frame.Code=row.Code;frame.Copies=row.Copies;frame.Device=row.Device;frame.StyleSheet=row.StyleSheet;result.Add(frame);
            }
            FrameOrder.Repage(result);return result;
        }
        private static string Key(string document,string layout,string handles)=>document+"|"+layout+"|"+handles;
        private static bool StampMatches(JobRow row,DetectedFrame frame)
        {
            if(row.StampParts==null)return true;if(frame.StampEvidence==null)return false;
            var proof=new StampScaleEvidence{Members=row.StampParts.Select(p=>new StampSegment{Path=p.Path,Index=p.Index,Start=new Point2(p.Coordinates[0],p.Coordinates[1]),End=new Point2(p.Coordinates[2],p.Coordinates[3]),Elevation=p.Coordinates[4]}).ToList()};
            return proof.IsCurrent(frame.StampEvidence.Members,Math.Max(1e-8,frame.Scale*.001));
        }
    }
}
