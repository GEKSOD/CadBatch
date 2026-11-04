using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CadBatch.Core;

internal static partial class Program
{
    private static void RunModernFeatureTests()
    {
        DrawingText Text(DetectedFrame f,double x,double y,string text)=>new DrawingText{DocumentKey=f.Source.DocumentKey,Layout=f.Layout,Center=new Point2(x,y),Text=text};
        Run("Stamp text zones read title, code and sheet without captions",()=>
        {
            var f=Detect(Candidate("M",0,0,420,297)).Frames.Single();
            StampMetadata.Apply(new[]{f},new[]{Text(f,310,25,"План этажа"),Text(f,320,55,"АР-01"),Text(f,388,30,"007"),Text(f,388,33,"Лист"),Text(f,20,200,"Чужой текст")},new StampMetadataSettings());
            Assert(f.Title=="План этажа"&&f.Code=="АР-01"&&f.SheetNumber=="007"&&f.OriginalTitle==f.Title);
        });
        Run("Stamp attributes retain priority over ordinary text",()=>
        {
            var c=Candidate("M",0,0,420,297);c.Attributes["TITLE"]="Атрибут";var f=Detect(c).Frames.Single();StampMetadata.Apply(new[]{f},new[]{Text(f,310,25,"Текст")},new StampMetadataSettings());Assert(f.Title=="Атрибут"&&f.Metadata["Title"]=="Атрибут");
        });
        Run("Stamp metadata ignores another layout or elevation",()=>
        {
            var f=Detect(Candidate("M",0,0,420,297)).Frames.Single();var a=Text(f,310,25,"Чужой лист");a.Layout="Другой";var b=Text(f,310,25,"Этаж выше");b.Elevation=3;StampMetadata.Apply(new[]{f},new[]{a,b},new StampMetadataSettings());Assert(f.Title=="");
        });
        Run("Stamp evidence supports rotated text coordinates at scale 100",()=>
        {
            var source=Candidate("M",0,0,420,297);source.Vertices=source.Vertices.Select(p=>Rotate(p*100,.47)+new Point2(800,-300)).ToList();
            var f=FrameDetection.Detect(new[]{source},new SearchOptions{Scales=new List<double>{100}},FormatDefinition.Defaults(),System.Threading.CancellationToken.None).Frames.Single();
            Point2 Transform(double x,double y)=>Rotate(new Point2(x,y)*100,.47)+new Point2(800,-300);
            f.StampEvidence=new StampScaleEvidence{HeaderStart=Transform(230,60),HeaderEnd=Transform(415,60)};
            var text=new DrawingText{DocumentKey=source.DocumentKey,Layout=f.Layout,Center=Transform(310,25),Text="Разрез А–А"};StampMetadata.Apply(new[]{f},new[]{text},new StampMetadataSettings());Assert(f.Title=="Разрез А–А");
        });
        Run("Text metadata survives rescan while manual edits are preserved",()=>
        {
            var old=Detect(Candidate("M",0,0,420,297)).Frames.Single();old.Title=old.OriginalTitle="Было";
            var fresh=Detect(Candidate("M",0,0,420,297)).Frames.Single();fresh.Title="Стало";FrameOrder.PreserveEdits(new[]{old},new[]{fresh});Assert(fresh.Title=="Стало");
            old.Title="Исправлено вручную";FrameOrder.PreserveEdits(new[]{old},new[]{fresh});Assert(fresh.Title==old.Title);
        });
        Run("MText cleanup retains meaningful text and stacked fractions",()=>Assert(StampMetadata.Clean(@"{\fArial|b0;План\P1\S1^2;} %%d")=="План\n11/2 °"));
        Run("XCLIP accepts a fully visible frame and rejects partial clipping",()=>{var area=Box(-1,-1,500,400);Assert(PolygonVisibility.FullyVisible(Box(0,0,420,297),area));Assert(!PolygonVisibility.FullyVisible(Box(-5,0,420,297),area));});
        Run("Concave XCLIP rejects a frame crossing a notch",()=>
        {
            var clip=new[]{new Point2(0,0),new Point2(10,0),new Point2(10,10),new Point2(6,10),new Point2(6,4),new Point2(4,4),new Point2(4,10),new Point2(0,10)};
            Assert(!PolygonVisibility.FullyVisible(Box(2,2,6,6),clip));Assert(PolygonVisibility.FullyVisible(Box(1,1,2,2),clip));
        });
        Run("Inverted XCLIP rejects a clip hole inside the frame",()=>{Assert(!PolygonVisibility.FullyVisible(Box(0,0,10,10),Box(3,3,2,2),true));Assert(PolygonVisibility.FullyVisible(Box(0,0,2,2),Box(3,3,2,2),true));});
        Run("Format print settings honor row then format then general precedence",()=>
        {
            var f=Detect(Candidate("M",0,0,420,297)).Frames.Single();f.Format.Print=new FormatPrintSettings{Device="format.pc3",StyleSheet="format.ctb",Fit=false,UpsideDown=true,OffsetX=3};
            var request=new ExportRequest{Device="general.pc3",FitToPaper=true};var effective=PrintTemplates.Resolve(request,f);Assert(effective.Device=="format.pc3"&&!effective.FitToPaper&&effective.UpsideDown&&effective.OffsetX==3&&effective.ColorMode==PrintColorMode.CustomStyle);
            f.Device="row.pc3";f.StyleSheet="row.ctb";effective=PrintTemplates.Resolve(request,f);Assert(effective.Device=="row.pc3"&&effective.StyleSheet=="row.ctb");
        });
        Run("Print template names cannot escape their directory",()=>{Throws<ArgumentException>(()=>PrintTemplates.PathFor("C:\\Templates","../escape"));Throws<ArgumentException>(()=>PrintTemplates.PathFor("C:\\Templates","bad:name"));});
        Run("Profiles clone metadata and format settings without sharing edits",()=>
        {
            var profile=new ProjectProfile();profile.Formats[0].Print=new FormatPrintSettings{Device="X",Fit=false};var copy=ProfileStore.Clone(profile);copy.Formats[0].Print!.Device="Y";copy.Metadata!.Zones[0].X=99;
            Assert(profile.Formats[0].Print!.Device=="X"&&profile.Metadata!.Zones[0].X==65);Assert(copy.Formats[0].Print!.Fit==false);
        });
        Run("Jobs preserve revision and additional stamp fields",()=>
        {
            var f=Detect(Candidate("M",0,0,420,297)).Frames.Single();f.Revision="02";f.Metadata["Organization"]="Проект";var job=JobStore.Capture(new ProjectProfile(),new[]{f});var restored=JobStore.Restore(job,new[]{f},out _).Single();Assert(restored.Revision=="02"&&restored.Metadata["Organization"]=="Проект");
        });
        Run("Catalog includes every one of the 24 formats in the supplied reference",()=>
        {
            var formats=FormatDefinition.Defaults();Assert(formats.Count==24);var f=formats.Single(x=>x.Name=="А4×9");Equal(f.WidthMm,1892);Equal(f.HeightMm,297);
            foreach(var format in formats)foreach(bool swap in new[]{false,true}){var found=Detect(Candidate(format.Name,0,0,swap?format.HeightMm:format.WidthMm,swap?format.WidthMm:format.HeightMm));Assert(found.Frames.Count==1&&found.Frames[0].FormatName==format.Name);}
        });
        Run("Catalog v2 adds only A4x9 to configured v1 profiles and keeps per-format settings",()=>
        {
            var profile=new ProjectProfile{CatalogVersion=1,Formats=new List<FormatDefinition>{new FormatDefinition("А3",421,298){Print=new FormatPrintSettings{Device="Свой.pc3"}}}};profile.UpgradeCatalog();Assert(profile.CatalogVersion==2&&profile.Formats.Count==2&&profile.Formats[0].WidthMm==421&&profile.Formats[0].Print!.Device=="Свой.pc3");profile.UpgradeCatalog();Assert(profile.Formats.Count==2);
        });
        Run("Concave XCLIP rejects a notch ending exactly at a frame edge",()=>
        {
            var clip=new[]{new Point2(0,0),new Point2(10,0),new Point2(10,10),new Point2(6,10),new Point2(6,4),new Point2(4,4),new Point2(4,10),new Point2(0,10)};Assert(!PolygonVisibility.FullyVisible(Box(0,0,10,10),clip));
        });
        Run("Advanced register uses selected columns and explicit group headings",()=>
        {
            var frames=new[]{Detect(Candidate("1",0,0,420,297)).Frames.Single(),Detect(Candidate("2",500,0,420,297)).Frames.Single()};frames[0].Code="АР";frames[1].Code="КР";frames[0].Revision="02";
            var rows=new RegisterOptions{Columns=new[]{"Лист","Ревизия","Размер, мм"},GroupBy="Шифр"}.Rows(frames);Assert(rows.Count==5&&rows[1][0]=="Шифр: АР"&&rows[2][1]=="02"&&rows[3][0]=="Шифр: КР");
        });
        string folder=Path.Combine(TestRoot,"artifacts","verification","ModernTests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            List<DetectedFrame> Frames()=>new[]{Candidate("M1",0,0,420,297),Candidate("M2",500,0,420,297)}.Select(c=>Detect(c).Frames.Single()).ToList();
            Run("Oversized PDF restores vector page dimensions through a smaller carrier",()=>
            {
                const double unit=72/25.4;string source=Path.Combine(folder,"small-carrier.pdf");
                using(var document=new PdfSharp.Pdf.PdfDocument())
                {
                    var page=document.AddPage();page.Width=PdfSharp.Drawing.XUnit.FromMillimeter(800);page.Height=PdfSharp.Drawing.XUnit.FromMillimeter(600);
                    using(var g=PdfSharp.Drawing.XGraphics.FromPdfPage(page))g.DrawLine(PdfSharp.Drawing.XPens.Red,300*unit,300*unit,320*unit,300*unit);document.Save(source);
                }
                string qa=Path.Combine(TestRoot,"artifacts","verification","features");Directory.CreateDirectory(qa);
                foreach(int rotation in new[]{0,90,180,270})
                {
                    string rotated=Path.Combine(folder,"carrier-"+rotation+".pdf");using(var doc=PdfSharp.Pdf.IO.PdfReader.Open(source,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Modify)){doc.Pages[0].Rotate=rotation;doc.Save(rotated);}
                    string output=Path.Combine(qa,"restored-"+rotation+".pdf");bool quarter=rotation==90||rotation==270;
                    CadBatch.Pdf.TargetSheetPdf.RestoreCentered(rotated,output,quarter?1000:4000,quarter?4000:1000,.2);
                    using(var document=PdfSharp.Pdf.IO.PdfReader.Open(output,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import)){Equal(document.Pages[0].Width.Millimeter,quarter?1000:4000,.001);Equal(document.Pages[0].Height.Millimeter,quarter?4000:1000,.001);Assert(document.Pages[0].Rotate==0);}
                }
                Throws<ArgumentException>(()=>CadBatch.Pdf.TargetSheetPdf.RestoreCentered(source,Path.Combine(folder,"invalid.pdf"),4000,1000,1.1));
            });
            Run("Checkpoint resume skips only verified completed outputs",()=>
            {
                var frames=Frames();var items=OutputPlanner.Build(frames,folder,"{Page}_{SheetNumber}_{Format}");var groups=PdfGroupPlanner.Build(items,PdfOutputMode.Separate,folder,"X");var c=CheckpointStore.Create(new ProjectProfile(),frames,groups,PdfOutputMode.Separate);
                File.WriteAllText(groups[0].Path,"PDF fake fixture");CheckpointStore.Complete(c,groups[0].Path);string path=Path.Combine(folder,"resume.json");CheckpointStore.Save(path,c);c=CheckpointStore.Load(path);Assert(CheckpointStore.Pending(c,frames).Count==1);
                File.AppendAllText(groups[0].Path,"CHANGED");Throws<ArgumentException>(()=>CheckpointStore.Pending(c,frames));File.Delete(groups[0].Path);
            });
            Run("Checkpoint rejects changed or missing source DWGs",()=>
            {
                var frames=Frames();string source=Path.Combine(folder,"source.dwg");File.WriteAllText(source,"original");foreach(var f in frames)f.Source.DocumentPath=source;
                var groups=PdfGroupPlanner.Build(OutputPlanner.Build(frames,folder,"{Page}"),PdfOutputMode.OneFile,folder,"set");var c=CheckpointStore.Create(new ProjectProfile(),frames,groups,PdfOutputMode.OneFile);
                File.WriteAllText(source,"changed");Throws<ArgumentException>(()=>CheckpointStore.Pending(c,frames));File.Delete(source);Throws<ArgumentException>(()=>CheckpointStore.Pending(c,frames));
            });
            Run("Checkpoint refuses to mix unsaved object changes",()=>
            {
                var frames=Frames();frames[0].Source.DrawingModified=true;var groups=PdfGroupPlanner.Build(OutputPlanner.Build(frames,folder,"{Page}"),PdfOutputMode.OneFile,folder,"set");var c=CheckpointStore.Create(new ProjectProfile(),frames,groups,PdfOutputMode.OneFile);Throws<ArgumentException>(()=>CheckpointStore.Pending(c,frames));
            });
        }
        finally{foreach(string path in Directory.GetFiles(folder))File.Delete(path);Directory.Delete(folder);}
    }
}
