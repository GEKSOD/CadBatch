using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CadBatch.Core;

internal static partial class Program
{
    private static void RunSearchFallbackTests()
    {
        // A computer with the old defaults: model only, scale 1.
        SearchOptions OldDefaults()=>new SearchOptions{Layouts=false,Scales=new List<double>{1}};
        Run("New profiles search model and layouts at 1 and 1:100",()=>
        {
            var s=new SearchOptions();Assert(s.Model&&s.Layouts&&s.Scales.SequenceEqual(new double[]{1,100}));
        });
        Run("Wider search looks everywhere, then adds common scales",()=>
        {
            var original=OldDefaults();original.LayerPattern="РАМКА";original.ActiveLayoutOnly=true;
            var first=SearchFallback.Wider(original,1);
            Assert(first.Model&&first.Layouts&&!first.ActiveLayoutOnly&&first.Lines&&first.ExternalReferences&&first.SpecialEntities&&first.LayerPattern=="*"&&first.Stamp!.Enabled&&!first.Stamp.OnlyStamp);
            Assert(first.Scales.SequenceEqual(new double[]{1}));
            var second=SearchFallback.Wider(original,2);Assert(second.Scales.Contains(100)&&second.Scales.Contains(0.1)&&second.Scales.Count==second.Scales.Distinct().Count());
            Assert(original.LayerPattern=="РАМКА"&&!original.Layouts&&original.Stamp==null);
            Throws<ArgumentOutOfRangeException>(()=>SearchFallback.Wider(original,3));
        });
        Run("Frames on a layout at 1:100 are found by the wider search and only the needed settings are kept",()=>
        {
            var original=OldDefaults();
            var onLayout=Candidate("L1",0,0,42000,29700);onLayout.Layout="3.Вертикалка";
            var formats=FormatDefinition.Defaults();
            // The old settings skip layout tabs; detection itself is fed what the host would read.
            Assert(FrameDetection.Detect(new[]{onLayout},original,formats,CancellationToken.None).Frames.Count==0);
            var found=FrameDetection.Detect(new[]{onLayout},SearchFallback.Wider(original,2),formats,CancellationToken.None).Frames;
            Assert(found.Count==1&&found[0].Scale==100);
            var adopted=SearchFallback.Adopt(original,found,formats);
            Assert(adopted.Layouts&&adopted.Model&&adopted.Scales.SequenceEqual(new double[]{1,100})&&!adopted.Lines&&!adopted.ExternalReferences&&!adopted.SpecialEntities&&adopted.Stamp==null);
            var changes=SearchFallback.Changes(original,adopted);
            Assert(changes.Count==2&&changes[0].Contains("листах")&&changes[1]=="добавить масштаб 1:100");
        });
        Run("Adopting keeps kinds of frames the search needed",()=>
        {
            var original=new SearchOptions{Model=true,Layouts=false,Polylines=true,Blocks=false,LayerPattern="РАМКА",Scales=new List<double>{1}};
            var lines=Candidate("LINE:1+2+3+4",0,0,420,297);lines.GeometryKind="Lines";lines.Layer="0";
            var xref=Candidate("X1",1000,0,420,297);xref.IsExternal=true;xref.BlockName="Подложка";xref.Layer="РАМКА";
            var found=FrameDetection.Detect(new[]{lines,xref},SearchFallback.Wider(original,1),FormatDefinition.Defaults(),CancellationToken.None).Frames;
            Assert(found.Count==2);
            var adopted=SearchFallback.Adopt(original,found,FormatDefinition.Defaults());
            Assert(adopted.Lines&&adopted.ExternalReferences&&adopted.Blocks&&adopted.LayerPattern=="*"&&!adopted.Layouts&&adopted.Scales.SequenceEqual(new double[]{1}));
            var changes=SearchFallback.Changes(original,adopted);
            Assert(changes.Any(c=>c.Contains("отрезков"))&&changes.Any(c=>c.Contains("Xref"))&&changes.Any(c=>c.Contains("любой слой")));
        });
        Run("A set of A-series sheets gets one scale for the whole drawing",()=>
        {
            // A1, A2, A3 and A4 at 1:100. At 1:50 the A1 would read as A0×2, at 1:200 the A3 and A4 fit nothing.
            var formats=FormatDefinition.Defaults();var original=new SearchOptions{Scales=new List<double>{1}};
            var sheets=new[]{("А1",841.0,594.0),("А2",594.0,420.0),("А3",420.0,297.0),("А4",297.0,210.0)}.Select((s,i)=>Candidate("S"+i,i*200000,0,s.Item2*100,s.Item3*100)).ToArray();
            var found=FrameDetection.Detect(sheets,SearchFallback.Wider(original,2),formats,CancellationToken.None).Frames;
            Assert(found.Count==4);
            var adopted=SearchFallback.Adopt(original,found,formats,out var alternatives);
            Assert(adopted.Scales.SequenceEqual(new double[]{1,100})&&alternatives.Count==0);
            var again=FrameDetection.Detect(sheets,adopted,formats,CancellationToken.None).Frames;
            Assert(again.Select(f=>f.FormatName).OrderBy(n=>n).SequenceEqual(new[]{"А1","А2","А3","А4"}));
            // Only A2–A4: 1:50 reads them as A0–A2 just as well, so the usual 1:100 is chosen and the doubt is reported.
            var small=sheets.Skip(1).ToArray();var smallFound=FrameDetection.Detect(small,SearchFallback.Wider(original,2),formats,CancellationToken.None).Frames;
            var smallAdopted=SearchFallback.Adopt(original,smallFound,formats,out var smallAlternatives);
            Assert(smallAdopted.Scales.SequenceEqual(new double[]{1,100})&&smallAlternatives.Contains(50));
        });
        Run("An open polyline drawn back to its start is a closed outline",()=>
        {
            Assert(OutlineGeometry.EndsAtStart(new Point2(0,0),new Point2(0,0),new Point2(42000,0)));
            Assert(OutlineGeometry.EndsAtStart(new Point2(0,0),new Point2(0.1,0),new Point2(42000,0)));
            Assert(!OutlineGeometry.EndsAtStart(new Point2(0,0),new Point2(50,0),new Point2(42000,0)));
            Assert(!OutlineGeometry.EndsAtStart(new Point2(0,0),new Point2(0,0),new Point2(0,0)));
        });
        Run("Line frames survive a busy plan: many angles and thousands of lines",()=>
        {
            var random=new Random(7);var segments=new List<FrameSegment>();
            // A1 frame at 1:100 drawn with four lines, among 6000 short lines at 300 different angles.
            foreach(var (a,b) in new[]{((0.0,0.0),(84100.0,0.0)),((84100.0,0.0),(84100.0,59400.0)),((84100.0,59400.0),(0.0,59400.0)),((0.0,59400.0),(0.0,0.0))})
                segments.Add(new FrameSegment{Start=new Point2(a.Item1,a.Item2),End=new Point2(b.Item1,b.Item2),Path="F"+segments.Count});
            for(int i=0;i<6000;i++)
            {
                double angle=(i%300)*Math.PI/600,x=random.NextDouble()*80000,y=random.NextDouble()*55000,length=200+random.NextDouble()*3000;
                segments.Add(new FrameSegment{Start=new Point2(x,y),End=new Point2(x+Math.Cos(angle)*length,y+Math.Sin(angle)*length),Path="N"+i});
            }
            var options=SearchFallback.Wider(new SearchOptions{Scales=new List<double>{100}},1);var warnings=new List<string>();
            var watch=System.Diagnostics.Stopwatch.StartNew();
            var found=LineFrames.Find(segments,FormatDefinition.Defaults(),options,CancellationToken.None,warnings);
            Assert(found.Any(f=>Math.Abs(f.Rectangle.Width*f.Rectangle.Height-84100.0*59400)<1)&&warnings.Any(w=>w.Contains("основных направлений"))&&watch.Elapsed.TotalSeconds<30);
        });
        Run("Small rectangles at odd scales do not add a scale next to a real sheet",()=>
        {
            var formats=FormatDefinition.Defaults();var original=new SearchOptions{Scales=new List<double>{100}};
            // A real A3×3 at 1:100 and two tables that read as A4×4 at 1:0.25 (drawing in metres).
            var sheet=Candidate("S",0,0,89100,42000);var t1=Candidate("T1",100000,0,210.25,74.25);var t2=Candidate("T2",101000,0,210.25,74.25);
            var found=FrameDetection.Detect(new[]{sheet,t1,t2},SearchFallback.Wider(original,2),formats,CancellationToken.None).Frames;
            Assert(found.Count==3);
            var adopted=SearchFallback.Adopt(original,found,formats);
            Assert(adopted.Scales.SequenceEqual(new double[]{100})&&SearchFallback.Explained(adopted,found,formats).Count==1);
        });
        Run("Drawings in metres are named so",()=>
        {
            Assert(SearchFallback.Scale(100)=="1:100"&&SearchFallback.Scale(0.1)=="1:100 (чертёж в метрах)"&&SearchFallback.Scale(1)=="1:1");
        });
        Run("Nothing found reports the largest rectangles",()=>
        {
            var r=FrameDetection.Detect(new[]{Candidate("R1",0,0,3950,2870),Candidate("R2",0,0,50,30),Candidate("R3",9000,0,50,30)},new SearchOptions{Scales=new List<double>{1}},FormatDefinition.Defaults(),CancellationToken.None);
            var note=r.Warnings.Single(w=>w.StartsWith(FrameDetection.NothingFoundPrefix,StringComparison.Ordinal));
            Assert(r.Frames.Count==0&&note.Contains("— 3")&&note.Contains("3950 × 2870")&&note.Contains("50 × 30")&&note.IndexOf("50 × 30",StringComparison.Ordinal)==note.LastIndexOf("50 × 30",StringComparison.Ordinal));
            Assert(!Detect(Candidate("OK",0,0,420,297)).Warnings.Any(w=>w.StartsWith(FrameDetection.NothingFoundPrefix,StringComparison.Ordinal)));
        });
    }
}
