using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Xml.Linq;
using CadBatch.Core;
using CadBatch.Pdf;

internal static partial class Program
{
    private static void RunFeatureTests()
    {
        DetectedFrame F(string handle,double x=0,double y=0)
        {
            var f=Detect(Candidate(handle,x,y,420,297)).Frames.Single();f.Source.DocumentPath="C:\\Source\\Test.dwg";return f;
        }
        Run("Natural sheet sorting handles hierarchical and huge numbers",()=>
        {
            var numbers=new[]{"10","2.10","2.2","2.1","2","1","99999999999999999999999999999",""};
            var result=numbers.OrderBy(n=>n,NaturalTextComparer.Instance).ToArray();Assert(string.Join("|",result)=="1|2|2.1|2.2|2.10|10|99999999999999999999999999999|");
        });
        Run("Row grouping uses a fixed anchor instead of non-transitive comparator",()=>
        {
            var a=F("A",100,3);var b=F("B",0,1.5);var c=F("C",-100,0);
            var result=FrameOrder.Sort(new[]{c,a,b},FrameSort.RowsLeftToRight,2);Assert(result.SequenceEqual(new[]{b,a,c}));Assert(result.Select(r=>r.Page).SequenceEqual(new[]{1,2,3}));
        });
        Run("Manual selected row block moves without reversing its order",()=>
        {
            var rows=new List<DetectedFrame>{F("A"),F("B"),F("C"),F("D")};var selected=new HashSet<DetectedFrame>{rows[1],rows[2]};
            FrameOrder.Move(rows,selected,-1);Assert(string.Join("",rows.Select(f=>f.Source.HandlePath))=="BCAD");FrameOrder.Move(rows,selected,1);Assert(string.Join("",rows.Select(f=>f.Source.HandlePath))=="ABCD");
        });
        Run("Rescan preserves manual metadata while reading untouched native fields",()=>
        {
            var before=F("A");before.Source.Attributes["SHEET"]="1";before.SheetNumber="007";before.Title="";before.Copies=3;
            var fresh=F("A");fresh.SheetNumber="2";fresh.Title="Новый текст в DWG";
            FrameOrder.PreserveEdits(new[]{before},new[]{fresh});Assert(fresh.SheetNumber=="007"&&fresh.Title=="Новый текст в DWG"&&fresh.Copies==3);
        });
        Run("Rescan does not carry edits onto a moved frame",()=>
        {
            var before=F("A");before.SheetNumber="007";var fresh=F("A",100);FrameOrder.PreserveEdits(new[]{before},new[]{fresh});Assert(fresh.SheetNumber=="");
        });
        Run("Numbering plan preserves old values until applied and skips disabled",()=>
        {
            var a=F("A");a.SheetNumber="old";var b=F("B");b.Enabled=false;var plan=Numbering.Plan(new[]{a,b},7,2,"АР-","x",3);
            Assert(plan.Count==1&&plan[0].NewNumber=="АР-007x"&&a.SheetNumber=="old");
        });
        Run("Numbering detects integer overflow before applying",()=>Throws<ArgumentException>(()=>Numbering.Plan(new[]{F("A"),F("B")},int.MaxValue,1,"","",0)));
        Run("Number warnings separate documents",()=>
        {
            var a=F("A");var b=F("B");a.SheetNumber=b.SheetNumber="1";Assert(Numbering.Warnings(new[]{a,b}).Length==1);b.Source.DocumentKey="other";Assert(Numbering.Warnings(new[]{a,b}).Length==0);
        });
        List<FrameSegment> Segments(IEnumerable<Point2> points)
        {
            var p=points.ToArray();return Enumerable.Range(0,p.Length).Select(i=>new FrameSegment{Start=p[i],End=p[(i+1)%p.Length],Path=i.ToString()}).ToList();
        }
        Run("Four rotated LINE objects form one A3 frame",()=>
        {
            var points=Box(12,34,420,297).Select(p=>Rotate(p,.63));var found=LineFrames.Find(Segments(points),FormatDefinition.Defaults(),new SearchOptions(),CancellationToken.None);
            Assert(found.Count==1&&found[0].Members.Count==4);Equal(found[0].Rectangle.Width*found[0].Rectangle.Height,420*297,.001);
        });
        Run("Segmented side and sub-tolerance joint gap are connected",()=>
        {
            var segments=Segments(Box(0,0,420,297));segments.RemoveAt(0);segments.Add(new FrameSegment{Start=new Point2(0,0),End=new Point2(210,0),Path="a"});segments.Add(new FrameSegment{Start=new Point2(210.005,0),End=new Point2(420,0),Path="b"});
            Assert(LineFrames.Find(segments,FormatDefinition.Defaults(),new SearchOptions(),CancellationToken.None).Count==1);
        });
        Run("Large side gap cannot produce a phantom frame",()=>
        {
            var segments=Segments(Box(0,0,420,297));segments[0].End=new Point2(410,0);Assert(LineFrames.Find(segments,FormatDefinition.Defaults(),new SearchOptions(),CancellationToken.None).Count==0);
        });
        Run("Adjacent LINE frames do not merge into a false extended sheet",()=>
        {
            var segments=Segments(Box(0,0,420,297));segments.AddRange(Segments(Box(420,0,420,297)).Select(s=>new FrameSegment{Start=s.Start,End=s.End,Path="B"+s.Path}));
            Assert(LineFrames.Find(segments,FormatDefinition.Defaults(),new SearchOptions(),CancellationToken.None).Count==2);
        });
        Run("LINE search observes cancellation",()=>
        {
            using(var cancel=new CancellationTokenSource()){cancel.Cancel();Throws<OperationCanceledException>(()=>LineFrames.Find(Segments(Box(0,0,420,297)),FormatDefinition.Defaults(),new SearchOptions(),cancel.Token));}
        });
        string directory=Path.Combine(TestRoot,"artifacts","verification","features");Directory.CreateDirectory(directory);
        Run("Grouped PDF separates same-named documents by full identity",()=>
        {
            var a=F("A");var b=F("B");b.Source.DocumentPath="C:\\Other\\Test.dwg";b.Source.DocumentKey="other";
            var items=OutputPlanner.Build(new[]{a,b},directory,"{Document}_{Page}");var groups=PdfGroupPlanner.Build(items,PdfOutputMode.ByDocument,directory,"All");
            Assert(groups.Count==2&&groups[0].Path!=groups[1].Path&&groups.All(g=>g.Items.Count==1));
        });
        Run("PDF grouping preserves page order and puts empty codes into one group",()=>
        {
            var a=F("A");var b=F("B");var c=F("C");a.Code=c.Code="АР";var items=OutputPlanner.Build(new[]{c,b,a},directory,"{Page}");
            var groups=PdfGroupPlanner.Build(items,PdfOutputMode.ByCode,directory,"All");Assert(groups.Count==2&&groups[0].Items[0].Frame==c&&groups[0].Items[1].Frame==a&&Path.GetFileName(groups[1].Path).StartsWith("Без шифра"));
        });
        Run("One PDF includes all enabled rows in order and accepts a PDF filename",()=>
        {
            var a=F("A");var b=F("B");var disabled=F("C");disabled.Enabled=false;
            var items=OutputPlanner.Build(new[]{b,disabled,a},directory,"{Page}");var group=PdfGroupPlanner.Build(items,PdfOutputMode.OneFile,directory,"Общий.PDF").Single();
            Assert(group.Items.Count==2&&group.Items[0].Frame==b&&group.Items[1].Frame==a&&Path.GetFileName(group.Path)=="Общий.pdf");
        });
        Run("DWG output planner preserves an existing drawing",()=>
        {
            string folder=Path.Combine(directory,"dwg");Directory.CreateDirectory(folder);File.WriteAllText(Path.Combine(folder,"one.dwg"),"original");
            var item=OutputPlanner.Build(new[]{F("A")},folder,"one",".dwg").Single();Assert(Path.GetFileName(item.Path)=="one (2).dwg");
        });
        Run("Job roundtrip keeps order metadata devices and selection",()=>
        {
            var a=F("A");var b=F("B",1000);a.Copies=3;a.Device="Test.pc3";a.StyleSheet="test.ctb";a.SheetNumber="007";b.Enabled=false;
            string path=Path.Combine(directory,"job.json");JobStore.Save(path,JobStore.Capture(new ProjectProfile{FitToPaper=false},new[]{b,a}));var job=JobStore.Load(path);Assert(job.Profile.FitToPaper==false);
            var restored=JobStore.Restore(job,new[]{F("A"),F("B",1000)},out var warnings);Assert(warnings.Count==0&&restored[0].Source.HandlePath=="B"&&!restored[0].Enabled&&restored[1].Copies==3&&restored[1].Device=="Test.pc3"&&restored[1].SheetNumber=="007");
        });
        Run("Changed job geometry is disabled even when handle matches",()=>
        {
            var job=JobStore.Capture(new ProjectProfile(),new[]{F("A")});var restored=JobStore.Restore(job,new[]{F("A",1)},out var warnings);
            Assert(warnings.Count==1&&!restored[0].Enabled&&!restored[0].Available&&Numeric.Positive(restored[0].WidthMm));
            restored[0].Enabled=true;Throws<ArgumentException>(()=>OutputPlanner.Build(restored,directory,"{Page}"));
        });
        Run("Closed drawing remains a visible unavailable job row",()=>
        {
            var restored=JobStore.Restore(JobStore.Capture(new ProjectProfile(),new[]{F("A")}),Array.Empty<DetectedFrame>(),out var warnings);Assert(restored.Count==1&&!restored[0].Available&&warnings.Count==1);
        });
        Run("Unknown job version cannot be saved",()=>Throws<ArgumentException>(()=>JobStore.Save(Path.Combine(directory,"bad.json"),new BatchJob{Version=99})));
        Run("Register CSV neutralizes formula input and preserves quotes",()=>
        {
            var f=F("A");f.Title="=1+2";f.Code="Шифр \"A\"";string path=Path.Combine(directory,"register.csv");RegisterExport.Csv(path,new[]{f});string text=File.ReadAllText(path);Assert(text.Contains("\"'=1+2\"")&&text.Contains("\"\"A\"\""));
        });
        Run("Register XLSX stores text literally with Cyrillic and line breaks",()=>
        {
            var f=F("A");f.SheetNumber="007";f.Title="=1+2\nУзел <А>&Б";f.Code="АР-01";string path=Path.Combine(directory,"register.xlsx");RegisterExport.Xlsx(path,new[]{f});
            using(var zip=ZipFile.OpenRead(path))using(var stream=zip.GetEntry("xl/worksheets/sheet1.xml")!.Open())
            {
                var xml=XDocument.Load(stream);XNamespace ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                Assert(xml.Descendants(ns+"f").Count()==0);Assert(xml.Descendants(ns+"t").Any(t=>t.Value=="007")&&xml.Descendants(ns+"t").Any(t=>t.Value==f.Title));
            }
        });
        Run("Register replacement retains a backup",()=>
        {
            string path=Path.Combine(directory,"backup.csv");File.WriteAllText(path,"old");RegisterExport.Csv(path,new[]{F("A")});Assert(File.ReadAllText(path+".bak")=="old");
        });
        Run("Mixed-size PDF merge retains rotation page boxes and bookmarks",()=>
        {
            string vector=Path.Combine(TestRoot,"artifacts/verification/carrier-vector-test.pdf"),native=Path.Combine(TestRoot,"artifacts/verification/native-crop-test.pdf");if(!File.Exists(native))native=vector;
            string path=Path.Combine(directory,"combined.pdf");TargetSheetPdf.Merge(new[]{vector,native},path,new[]{"001 — Общие данные","002 — План"});
            using(var pdf=PdfSharp.Pdf.IO.PdfReader.Open(path,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {
                Assert(pdf.PageCount==2&&pdf.Outlines.Count==2);Equal(pdf.Pages[0].MediaBox.Width*25.4/72,891,.001);
                using(var original=PdfSharp.Pdf.IO.PdfReader.Open(native,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))Assert(pdf.Pages[1].Rotate==original.Pages[0].Rotate);
            }
        });
        Run("Physical print report reports queue submission rather than PDF creation",()=>
        {
            var report=new ExportReport{PhysicalPrint=true,Queued=3};Assert(ExportReportText.State(report)=="ПЕРЕДАНО В ОЧЕРЕДЬ ПЕЧАТИ");
            Assert(ExportReportText.Format(report,new ExportRequest{PhysicalPrint=true},"test").Contains("Передано страниц с копиями: 3"));
        });
        Run("Real AutoCAD pages merge across drawings without changing page count",()=>
        {
            string[] folders={Path.Combine(TestRoot,"artifacts/feedback/20261102-batch"),Path.Combine(TestRoot,"artifacts/feedback/real-drawing-20261102")};
            var pages=folders.Where(Directory.Exists).SelectMany(d=>Directory.GetFiles(d,"*.pdf").OrderBy(Path.GetFileName,StringComparer.OrdinalIgnoreCase)).ToArray();
            if(pages.Length==0)return;string path=Path.Combine(directory,"combined-real.pdf");TargetSheetPdf.Merge(pages,path,pages.Select(Path.GetFileNameWithoutExtension).ToArray()!);
            using(var pdf=PdfSharp.Pdf.IO.PdfReader.Open(path,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))Assert(pdf.PageCount==pages.Length&&pdf.Outlines.Count==pages.Length);
        });
        Run("Tile grid covers an exact two-by-two sheet with no extra page",()=>
        {
            var plan=TilePlan.Create(594,420,297,210,0,0);Assert(plan.Columns==2&&plan.Rows==2&&plan.Count==4);Equal(plan.X(1),297);Equal(plan.Y(1),210);
        });
        Run("Tile overlap and printable margins are included in coverage",()=>
        {
            var p=TilePlan.Create(594,420,210,297,10,5);Assert(p.Columns==4&&p.Rows==2);
            Assert(p.X(p.Columns-1)+p.UsableWidthMm>=p.ContentWidthMm&&p.Y(p.Rows-1)+p.UsableHeightMm>=p.ContentHeightMm);Equal(p.X(1),185);
        });
        Run("Invalid tile margins overlap and excessive page counts are rejected",()=>
        {
            Throws<ArgumentException>(()=>TilePlan.Create(594,420,210,297,110,0));Throws<ArgumentException>(()=>TilePlan.Create(594,420,210,297,10,190));Throws<ArgumentException>(()=>TilePlan.Create(1e6,1e6,210,297,10,5));
        });
        Run("Vector PDF tiles retain target paper dimensions and bookmarks",()=>
        {
            string source=Path.Combine(TestRoot,"artifacts/verification/carrier-vector-test.pdf"),output=Path.Combine(directory,"tiles-vector.pdf");
            var plan=TargetSheetPdf.PlanTiles(source,297,210,10,5,1);TargetSheetPdf.Tile(source,output,plan);
            using(var pdf=PdfSharp.Pdf.IO.PdfReader.Open(output,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
            {Assert(pdf.PageCount==plan.Count&&pdf.Outlines.Count==plan.Count);foreach(var page in pdf.Pages){Equal(page.MediaBox.Width*25.4/72,297,.001);Equal(page.MediaBox.Height*25.4/72,210,.001);}}
        });
        Run("Rotated native PDF tile plan uses displayed orientation",()=>
        {
            string source=Path.Combine(TestRoot,"artifacts/verification/native-crop-test.pdf");if(!File.Exists(source))return;
            var plan=TargetSheetPdf.PlanTiles(source,210,297,10,5,1);Equal(plan.ContentWidthMm,594,.001);Equal(plan.ContentHeightMm,420,.001);
            TargetSheetPdf.Tile(source,Path.Combine(directory,"tiles-native.pdf"),plan);
        });
        Run("Cancelled PDF tiling preserves an existing destination",()=>
        {
            string source=Path.Combine(TestRoot,"artifacts/verification/carrier-vector-test.pdf"),output=Path.Combine(directory,"tile-cancel.pdf");File.WriteAllText(output,"original");
            var plan=TargetSheetPdf.PlanTiles(source,210,297,10,5,1);using(var cancel=new CancellationTokenSource())
            {cancel.Cancel();Throws<OperationCanceledException>(()=>TargetSheetPdf.Tile(source,output,plan,cancel.Token));Assert(File.ReadAllText(output)=="original");}
        });
    }
}
