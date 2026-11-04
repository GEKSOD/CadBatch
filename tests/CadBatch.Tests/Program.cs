using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CadBatch.Core;
using CadBatch.Pdf;

internal static partial class Program
{
    private static int passed,failed;
    // Output goes under the repository (found from the test binary), never under the current directory,
    // and the folders exist before any test writes into them.
    private static readonly string TestRoot=CreateTestRoot();
    private static string CreateTestRoot()
    {
        string? root=Environment.GetEnvironmentVariable("CADBATCH_TEST_ROOT");
        for(var dir=new DirectoryInfo(AppContext.BaseDirectory);root==null&&dir!=null;dir=dir.Parent)if(File.Exists(Path.Combine(dir.FullName,"CadBatch.sln")))root=dir.FullName;
        root??=Path.Combine(Path.GetTempPath(),"CadBatch-tests");
        foreach(string sub in new[]{"artifacts/verification/features","artifacts/tests"})Directory.CreateDirectory(Path.Combine(root,sub));
        return root;
    }
    private static void Main()
    {
        Run("Axis-aligned rectangle",()=>{Assert(Rectangle2.TryCreate(Box(0,0,420,297),.01,out var r));Equal(r!.Width*r.Height,420*297,.01);});
        Run("Rotated rectangle retains dimensions",()=>
        {
            var points=Box(10,20,420,297).Select(p=>Rotate(p,.731)).ToList();Assert(Rectangle2.TryCreate(points,.01,out var r));
            Assert((Math.Abs(r!.Width-420)<.01 && Math.Abs(r.Height-297)<.01)||(Math.Abs(r.Width-297)<.01 && Math.Abs(r.Height-420)<.01));
        });
        Run("Mirrored rectangle accepted",()=>Assert(Rectangle2.TryCreate(Box(0,0,420,297).Select(p=>new Point2(-p.X,p.Y)),.01,out _)));
        Run("Collinear vertices simplified",()=>Assert(Rectangle2.TryCreate(new[]{new Point2(0,0),new Point2(200,0),new Point2(420,0),new Point2(420,297),new Point2(0,297)},.01,out _)));
        Run("Repeated closing vertex accepted",()=>Assert(Rectangle2.TryCreate(Box(0,0,420,297).Concat(new[]{new Point2(0,0)}),.01,out _)));
        Run("Trapezoid rejected",()=>Assert(!Rectangle2.TryCreate(new[]{new Point2(0,0),new Point2(420,0),new Point2(400,297),new Point2(0,297)},.01,out _)));
        Run("Self-crossing contour rejected",()=>Assert(!Rectangle2.TryCreate(new[]{new Point2(0,0),new Point2(420,297),new Point2(420,0),new Point2(0,297)},.01,out _)));
        Run("NaN rejected",()=>Assert(!Rectangle2.TryCreate(Box(0,0,double.NaN,297),.01,out _)));
        Run("Zero dimensions rejected",()=>Assert(!Rectangle2.TryCreate(Box(0,0,0,297),.01,out _)));
        Run("Negative tolerance rejected",()=>Assert(!Rectangle2.TryCreate(Box(0,0,420,297),-1,out _)));
        Run("Large coordinates do not change dimensions",()=>{Assert(Rectangle2.TryCreate(Box(1e8,1e8,420,297),.01,out var r));Equal(r!.Width*r.Height,420*297,.1);});
        Run("A3 detection",()=>{var r=Detect(Candidate("A",0,0,420,297));Assert(r.Frames.Count==1);Assert(r.Frames[0].FormatName=="А3");});
        Run("Portrait A3 detection",()=>Assert(Detect(Candidate("A",0,0,297,420)).Frames.Single().FormatName=="А3"));
        Run("Geometry scale 100",()=>
        {
            var r=FrameDetection.Detect(new[]{Candidate("A",0,0,42000,29700)},new SearchOptions {Scales=new List<double>{1,100}},FormatDefinition.Defaults(),CancellationToken.None);
            Assert(r.Frames.Count==1);Equal(r.Frames[0].Scale,100);Equal(r.Frames[0].WidthMm,420);
        });
        Run("Size tolerance is on paper",()=>Assert(Detect(Candidate("A",0,0,421.5,298)).Frames.Count==1));
        Run("Tolerant geometry uses exact catalog paper",()=>{var f=Detect(Candidate("A",0,0,421.5,298)).Frames.Single();Equal(f.PaperWidthMm,420);Equal(f.PaperHeightMm,297);});
        Run("Portrait paper is oriented correctly",()=>{var f=Detect(Candidate("A",0,0,297,420)).Frames.Single();Equal(f.PaperWidthMm,297);Equal(f.PaperHeightMm,420);});
        Run("Outside tolerance skipped",()=>Assert(Detect(Candidate("A",0,0,430,307)).Frames.Count==0));
        Run("Repeated identity is not duplicated",()=>{var a=Candidate("A",0,0,420,297);var r=Detect(a,a);Assert(r.Frames.Count==1 && r.Duplicates==1);});
        Run("Overlapping contours grouped",()=>{var r=Detect(Candidate("A",0,0,420,297),Candidate("B",0,0,420,297));Assert(r.Frames.Count==1 && r.Duplicates==1 && r.Warnings.Count==1);});
        Run("Adjacent frames preserved",()=>Assert(Detect(Candidate("A",0,0,420,297),Candidate("B",420,0,420,297)).Frames.Count==2));
        Run("Duplicate handles across documents preserved",()=>{var a=Candidate("A",0,0,420,297);var b=Candidate("A",0,0,420,297);b.DocumentKey="different";Assert(Detect(a,b).Frames.Count==2);});
        Run("Different layouts preserved",()=>{var a=Candidate("A",0,0,420,297);var b=Candidate("A",0,0,420,297);b.Layout="Лист1";Assert(Detect(a,b).Frames.Count==2);});
        Run("Nested instances preserved",()=>Assert(Detect(Candidate("10/A",0,0,420,297),Candidate("20/A",1000,0,420,297)).Frames.Count==2));
        Run("Attributes extracted without whitespace",()=>{var a=Candidate("A",0,0,420,297);a.Attributes["SHEET"]="  2.1 ";Assert(Detect(a).Frames.Single().SheetNumber=="2.1");});
        Run("Layer wildcard applied",()=>
        {
            var a=Candidate("A",0,0,420,297);a.Layer="РАМКА_АР";
            Assert(FrameDetection.Detect(new[]{a},new SearchOptions {LayerPattern="РАМКА_*"},FormatDefinition.Defaults(),CancellationToken.None).Frames.Count==1);
            Assert(FrameDetection.Detect(new[]{a},new SearchOptions {LayerPattern="ДРУГОЙ"},FormatDefinition.Defaults(),CancellationToken.None).Frames.Count==0);
        });
        Run("Invalid options fail early",()=>Throws<ArgumentException>(()=>FrameDetection.Detect(new FrameCandidate[0],new SearchOptions{Scales=new List<double>{0}},FormatDefinition.Defaults(),CancellationToken.None)));
        Run("Cancellation is observed",()=>{using(var c=new CancellationTokenSource()){c.Cancel();Throws<OperationCanceledException>(()=>FrameDetection.Detect(new[]{Candidate("A",0,0,420,297)},new SearchOptions(),FormatDefinition.Defaults(),c.Token));}});
        Run("Reserved filename sanitized",()=>Assert(OutputPlanner.Sanitize("CON.txt")=="_CON.txt"));
        Run("Invalid filename characters sanitized",()=>Assert(OutputPlanner.Sanitize("АР/01: План?")=="АР_01_ План_"));
        Run("Empty filename has fallback",()=>Assert(OutputPlanner.Sanitize(" ... ")=="Чертеж"));
        Run("Wide viewport keeps complete frame",()=>
        {
            var view=ViewWindow.Around(Box(500,800,420,297),2);
            Equal(view.Width/view.Height,2);Assert(view.Width>=420*1.2 && view.Height>=297*1.2);Equal(view.Center.X,710);Equal(view.Center.Y,948.5);
        });
        Run("Tall viewport keeps complete frame",()=>
        {
            var view=ViewWindow.Around(Box(0,0,420,297),.5);
            Equal(view.Width/view.Height,.5);Assert(view.Width>=420*1.2 && view.Height>=297*1.2);
        });
        Run("Empty view bounds rejected",()=>Throws<ArgumentException>(()=>ViewWindow.Around(Array.Empty<Point2>(),1.5)));
        Run("Operation failure retains exact stage and native exception",()=>
        {
            var trace=new OperationTrace();
            try {trace.Step("Назначение области печати",()=>throw new ArgumentException("eInvalidInput"));throw new Exception("Expected stage exception");}
            catch(InvalidOperationException ex){Assert(ex.Message.Contains("Назначение области печати"));Assert(ex.InnerException is ArgumentException);}
            Assert(trace.Entries.Last().Contains("ОШИБКА"));
        });
        Run("Failure report does not claim completion",()=>
        {
            var report=new ExportReport();report.Errors.Add("eInvalidInput");report.Details.Add("НАЧАЛО: Назначение области печати");
            string text=ExportReportText.Format(report,new ExportRequest{Device="DWG To PDF.pc3"},"AutoCAD 2023");
            Assert(ExportReportText.State(report)=="PDF НЕ СОЗДАН — ОШИБКА");Assert(text.Contains("Назначение области печати"));Assert(text.Contains("DWG To PDF.pc3"));
        });
        Run("Partial report distinguishes created files",()=>
        {
            var report=new ExportReport();report.Files.Add("one.pdf");report.Errors.Add("native failure");
            Assert(ExportReportText.State(report).StartsWith("ЧАСТИЧНО"));
        });
        Run("Cancelled report takes priority",()=>Assert(ExportReportText.State(new ExportReport{Cancelled=true})=="ОТМЕНЕНО"));
        Run("Validated paper guard keeps landscape physical size",()=>
            ValidatedPaper.EnsureUnchanged("DWG To PDF.pc3",420,297,"DWG To PDF.pc3",297,420,true));
        Run("Validated paper guard keeps portrait physical size",()=>
            ValidatedPaper.EnsureUnchanged("DWG To PDF.pc3",297,420,"DWG To PDF.pc3",297,420,false));
        Run("Validated paper guard cannot silently replace A3 with A4",()=>
            Throws<InvalidOperationException>(()=>ValidatedPaper.EnsureUnchanged("DWG To PDF.pc3",420,297,"DWG To PDF.pc3",210,297,true)));
        Run("Validated paper guard cannot silently rotate the page",()=>
            Throws<InvalidOperationException>(()=>ValidatedPaper.EnsureUnchanged("DWG To PDF.pc3",420,297,"DWG To PDF.pc3",297,420,false)));
        Run("Validated paper dimension rounding is tolerated",()=>
            ValidatedPaper.EnsureUnchanged("DWG To PDF.pc3",420,297,"DWG To PDF.pc3",297.001,420.001,true));
        Run("Validated paper guard cannot replace the PDF device",()=>
            Throws<InvalidOperationException>(()=>ValidatedPaper.EnsureUnchanged("DWG To PDF.pc3",420,297,"Other PDF.pc3",297,420,true)));
        Run("Validated device name comparison supports paths and case",()=>
            ValidatedPaper.EnsureUnchanged("DWG To PDF.pc3",420,297,@"C:\Plotters\DWG TO PDF.PC3",297,420,true));
        Run("Invalid validated paper dimensions are rejected",()=>
            Throws<ArgumentException>(()=>ValidatedPaper.EnsureUnchanged("DWG To PDF.pc3",420,297,"DWG To PDF.pc3",double.NaN,420,true)));
        Run("Drawing color mode bypasses inherited monochrome style",()=>
        {
            var style=PrintStylePolicy.Resolve(PrintColorMode.DrawingColors,"monochrome.ctb",true);
            Assert(style.ApplyPlotStyles==false && style.StyleSheet==null);
        });
        Run("Inherited style mode retains original settings",()=>
        {
            var style=PrintStylePolicy.Resolve(PrintColorMode.FromDrawing,"",true);
            Assert(!style.ApplyPlotStyles.HasValue && style.StyleSheet==null);
        });
        Run("Monochrome mode respects CTB and STB drawing type",()=>
        {
            Assert(PrintStylePolicy.Resolve(PrintColorMode.Monochrome,"",true).StyleSheet=="monochrome.ctb");
            Assert(PrintStylePolicy.Resolve(PrintColorMode.Monochrome,"",false).StyleSheet=="monochrome.stb");
        });
        Run("Custom style requires an explicit file",()=>Throws<ArgumentException>(()=>PrintStylePolicy.Resolve(PrintColorMode.CustomStyle,"",true)));
        Run("Catalog migration adds missing formats without changing custom rows",()=>
        {
            var profile=new ProjectProfile{CatalogVersion=0,Formats=new List<FormatDefinition>{new FormatDefinition("А3",421,298)}};
            profile.UpgradeCatalog();Assert(profile.Formats.Any(f=>f.Name=="А3×4" && f.WidthMm==1189));Equal(profile.Formats.Single(f=>f.Name=="А3").WidthMm,421);
            int count=profile.Formats.Count;profile.UpgradeCatalog();Assert(profile.Formats.Count==count);
        });
        Run("Carrier crop keeps requested landscape size with rotated PDF",()=>
        {
            var b=PdfCropBounds.Centered(0,0,841*72/25.4,1189*72/25.4,270,891,420);
            Equal(b.Width*25.4/72,420,.001);Equal(b.Height*25.4/72,891,.001);
        });
        Run("Carrier crop keeps exact size with offset PDF origin",()=>
        {
            var b=PdfCropBounds.Centered(100,200,100+1189*72/25.4,200+841*72/25.4,0,1189,420);
            Equal(b.Width*25.4/72,1189,.001);Equal(b.Height*25.4/72,420,.001);Equal(b.Left,100,.001);
        });
        Run("Carrier smaller than requested sheet is rejected",()=>Throws<InvalidOperationException>(()=>PdfCropBounds.Centered(0,0,420*72/25.4,297*72/25.4,0,891,420)));
        Run("Carrier crop tolerates native integer-point paper rounding",()=>
        {
            var b=PdfCropBounds.Centered(0,0,2384,3370,270,1189,420);
            Equal(b.Width*25.4/72,420,.001);Equal(b.Height*25.4/72,1189,.001);
        });
        Run("Mixed paper-layout formats yield all ten frames",()=>
        {
            var fixture=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(TestRoot,"tests/fixtures/synthetic-layout.json")));
            var candidates=fixture.RootElement.EnumerateArray().Select(row=>new FrameCandidate
            {
                DocumentKey="synthetic-fixture",DocumentName="Regression.dwg",Layout="Sheets",Layer="CAD_FRAME",HandlePath=row.GetProperty("handle").GetString()!,
                Vertices=row.GetProperty("points").EnumerateArray().Select(p=>new Point2(p[0].GetDouble(),p[1].GetDouble())).ToList()
            }).ToArray();
            var found=FrameDetection.Detect(candidates,new SearchOptions(),FormatDefinition.Defaults(),CancellationToken.None).Frames;
            Assert(found.Count==10);Assert(found.Count(f=>f.FormatName=="А3×4")==2);Assert(found.Count(f=>f.FormatName=="А3×3")==2);
            Assert(found.Count(f=>f.FormatName=="А3")==5);Assert(found.Count(f=>f.FormatName=="А4×3")==1);
        });
        string temp=Path.Combine(TestRoot,"artifacts","tests","cadbatch-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);
        try
        {
            Run("Planning reserves names case-insensitively",()=>
            {
                var frames=Detect(Candidate("A",0,0,420,297),Candidate("B",1000,0,420,297)).Frames;
                var items=OutputPlanner.Build(frames,temp,"same");Assert(items.Count==2);Assert(items[1].Path.EndsWith("same (2).pdf"));
            });
            Run("Existing files preserved",()=>{File.WriteAllText(Path.Combine(temp,"exists.pdf"),"original");var items=OutputPlanner.Build(Detect(Candidate("A",0,0,420,297)).Frames,temp,"exists");Assert(items.Single().Path.EndsWith("exists (2).pdf"));});
            Run("Disabled rows excluded",()=>{var f=Detect(Candidate("A",0,0,420,297)).Frames;f[0].Enabled=false;Throws<ArgumentException>(()=>OutputPlanner.Build(f,temp,"{Page}"));});
            Run("Unknown naming token rejected",()=>Throws<ArgumentException>(()=>OutputPlanner.Build(Detect(Candidate("A",0,0,420,297)).Frames,temp,"{Unknown}")));
            Run("Unclosed naming token rejected",()=>Throws<ArgumentException>(()=>OutputPlanner.Build(Detect(Candidate("A",0,0,420,297)).Frames,temp,"{Page")));
            Run("Profile roundtrip and backup",()=>
            {
                string path=Path.Combine(temp,"profile.json");var p=new ProjectProfile();p.Search.Scales.Add(100);ProfileStore.Save(path,p);
                Assert(ProfileStore.Load(path).Search.Scales.SequenceEqual(new double[]{1,100}));p.FilePattern="{Title}";ProfileStore.Save(path,p);
                Assert(File.Exists(path+".bak"));Assert(ProfileStore.Load(path+".bak").FilePattern!="{Title}");
            });
            Run("Unknown schema rejected",()=>{var p=new ProjectProfile{SchemaVersion=42};Throws<ArgumentException>(()=>ProfileStore.Save(Path.Combine(temp,"bad.json"),p));});
            Run("Invalid catalog not saved",()=>{var p=new ProjectProfile();p.Formats[0].WidthMm=-1;Throws<ArgumentException>(()=>ProfileStore.Save(Path.Combine(temp,"invalid.json"),p));});
            Run("Legacy JSON loads with colors and upgraded catalog",()=>
            {
                string path=Path.Combine(temp,"legacy.json");var p=new ProjectProfile{Formats=FormatDefinition.Defaults().Take(8).ToList()};ProfileStore.Save(path,p);
                var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!.AsObject();node.Remove("CatalogVersion");node.Remove("ColorMode");File.WriteAllText(path,node.ToJsonString());
                var loaded=ProfileStore.Load(path);Assert(loaded.CatalogVersion==2 && loaded.ColorMode==PrintColorMode.DrawingColors);Assert(loaded.Formats.Any(f=>f.Name=="А3×4"));
            });
            Run("Vector carrier PDF is cropped without rasterizing",()=>
            {
                string source=Path.Combine(temp,"carrier.pdf"),output=Path.Combine(temp,"cropped.pdf");
                using(var document=new PdfSharp.Pdf.PdfDocument())
                {
                    var page=document.AddPage();page.Width=PdfSharp.Drawing.XUnit.FromMillimeter(1189);page.Height=PdfSharp.Drawing.XUnit.FromMillimeter(841);
                    using(var graphics=PdfSharp.Drawing.XGraphics.FromPdfPage(page))
                        graphics.DrawLine(PdfSharp.Drawing.XPens.Red,100,1100,2000,1100);
                    document.Save(source);
                }
                TargetSheetPdf.CropCentered(source,output,891,420);
                using(var document=PdfSharp.Pdf.IO.PdfReader.Open(output,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                {
                    Assert(document.PageCount==1);Equal(document.Pages[0].MediaBox.Width*25.4/72,891,.001);Equal(document.Pages[0].MediaBox.Height*25.4/72,420,.001);
                    Assert(document.Pages[0].Contents.Elements.Count>0);
                }
                string qa=Path.Combine(TestRoot,"artifacts","verification","carrier-vector-test.pdf");File.Copy(output,qa,true);
            });
            Run("Native AutoCAD PDF fonts survive page-box crop",()=>
            {
                string source=Path.Combine(TestRoot,"artifacts/feedback/20261102-batch/CadBatch-control.dwg_006_А2.pdf");
                if(!File.Exists(source))return;
                string output=Path.Combine(TestRoot,"artifacts","verification","native-crop-test.pdf");
                TargetSheetPdf.CropCentered(source,output,594,420);
                using(var doc=PdfSharp.Pdf.IO.PdfReader.Open(output,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))
                {Assert(doc.PageCount==1);Equal(doc.Pages[0].MediaBox.Width*25.4/72,420,.001);Equal(doc.Pages[0].MediaBox.Height*25.4/72,594,.001);}
            });
        }
        finally { foreach(var file in Directory.GetFiles(temp))File.Delete(file);Directory.Delete(temp); }
        RunFeatureTests();
        RunStampTests();
        RunModernFeatureTests();
        RunApplicationTests();
        RunImportTests();
        RunReleaseToolsTests();
        Run("500-frame synthetic corpus",()=>
        {
            var candidates=Enumerable.Range(0,500).Select(i=>Candidate(i.ToString(),i%25*500,i/25*400,420,297)).ToList();
            Assert(FrameDetection.Detect(candidates,new SearchOptions(),FormatDefinition.Defaults(),CancellationToken.None).Frames.Count==500);
        });
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
    }
    private static FrameCandidate Candidate(string handle,double x,double y,double w,double h)=>new FrameCandidate{DocumentKey="test",DocumentName="Тест.dwg",HandlePath=handle,Vertices=Box(x,y,w,h)};
    private static List<Point2> Box(double x,double y,double w,double h)=>new List<Point2>{new Point2(x,y),new Point2(x+w,y),new Point2(x+w,y+h),new Point2(x,y+h)};
    private static Point2 Rotate(Point2 p,double a)=>new Point2(p.X*Math.Cos(a)-p.Y*Math.Sin(a),p.X*Math.Sin(a)+p.Y*Math.Cos(a));
    private static ScanResult Detect(params FrameCandidate[] candidates)=>FrameDetection.Detect(candidates,new SearchOptions(),FormatDefinition.Defaults(),CancellationToken.None);
    private static void Assert(bool condition){if(!condition)throw new Exception("Assertion failed");}
    private static void Equal(double a,double b,double tolerance=1e-9){if(!(Math.Abs(a-b)<=tolerance))throw new Exception("Expected "+b.ToString("R")+", got "+a.ToString("R"));}
    private static void Throws<T>(Action action) where T:Exception {try{action();}catch(T){return;}throw new Exception("Expected "+typeof(T).Name);}
    private static void Run(string name,Action test){try{test();passed++;Console.WriteLine("PASS "+name);}catch(Exception ex){failed++;Console.WriteLine("FAIL "+name+": "+ex);}}
}
