using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CadBatch.Core;

internal static partial class Program
{
    private static void RunStampTests()
    {
        SearchOptions Options(bool only=true)=>new SearchOptions{Stamp=new StampScaleSettings{Enabled=true,OnlyStamp=only}};
        List<StampSegment> Stamp(double width,double scale,string prefix="S",bool split=false)
        {
            double left=width-190*scale,right=width-5*scale,bottom=5*scale,top=60*scale;
            var segments=new List<StampSegment>();int n=0;
            void Add(double x1,double y1,double x2,double y2)=>segments.Add(new StampSegment{DocumentKey="test",Path=prefix+(n++),Start=new Point2(x1,y1),End=new Point2(x2,y2)});
            if(split)
            {
                double x=left;foreach(double part in new[]{10d,10d,10d,10d,15d,10d,120d}){Add(x,top,x+part*scale,top);x+=part*scale;}
            }
            else Add(left,top,right,top);
            Add(left,bottom,right,bottom);Add(left,bottom,left,top);Add(right,bottom,right,top);return segments;
        }
        ScanResult Find(FrameCandidate candidate,IReadOnlyList<StampSegment> segments,SearchOptions? settings=null)=>FrameDetection.Detect(new[]{candidate},settings??Options(),FormatDefinition.Defaults(),CancellationToken.None,segments);
        Run("Stamp line length computes geometry scale rather than text scale",()=>
        {
            Equal(StampScaleIndex.Measure(18500,185),100);Equal(StampScaleIndex.Measure(185,185),1);Equal(StampScaleIndex.Measure(.185,185),.001);
            Throws<ArgumentException>(()=>StampScaleIndex.Measure(0,185));Throws<ArgumentException>(()=>StampScaleIndex.Measure(185,0));
        });
        Run("Stamp detects unknown scale 100 with only manual scale 1 configured",()=>
        {
            var frame=Candidate("F",0,0,42000,29700);frame.Attributes["SCALE"]="1:50";
            var found=Find(frame,Stamp(42000,100)).Frames.Single();Equal(found.Scale,100);Assert(found.FormatName=="А3"&&found.StampEvidence!=null&&found.ScaleSource.Contains("185"));
        });
        Run("Stamp sizes typed in drawing units do not hide the frame",()=>
        {
            // 18500 × 5500 is a 185 × 55 mm stamp at 1:100: it would mean 1:1, where 42000 × 29700 is no format, so the list decides.
            var wrong=new SearchOptions{Scales=new List<double>{100},Stamp=new StampScaleSettings{Enabled=true,ReferenceWidthMm=18500,HeightMm=5500,RightMarginMm=500,BottomMarginMm=500}};
            var result=Find(Candidate("F",0,0,42000,29700),Stamp(42000,100),wrong);var found=result.Frames.Single();
            Assert(found.FormatName=="А3"&&found.Scale==100&&found.StampEvidence==null);
            wrong.Stamp.OnlyStamp=true;Assert(Find(Candidate("F",0,0,42000,29700),Stamp(42000,100),wrong).Frames.Count==0);
            Assert(StampScaleSettings.UnitsFactor(18500)==100&&StampScaleSettings.UnitsFactor(185)==1&&StampScaleSettings.UnitsFactor(1850)==10&&StampScaleSettings.UnitsFactor(double.NaN)==1);
        });
        Run("Stamp supports arbitrary nonstandard scale and a split upper line",()=>
        {
            var found=Find(Candidate("F",0,0,420*37.5,297*37.5),Stamp(420*37.5,37.5,split:true)).Frames.Single();Equal(found.Scale,37.5,1e-9);Assert(found.StampEvidence!.Members.Count==10);
        });
        Run("Stamp supports rotations including 180 degrees and mirrored blocks",()=>
        {
            foreach(double angle in new[]{0d,.2617993878,.5235987756,Math.PI/2,Math.PI,Math.PI*1.5})foreach(bool mirror in new[]{false,true})
            {
                Point2 Transform(Point2 p)=>Rotate(new Point2(mirror?-p.X:p.X,p.Y),angle)+new Point2(1e6,-1e6);
                var frame=Candidate("F",0,0,42000,29700);frame.Vertices=frame.Vertices.Select(Transform).ToList();
                var segments=Stamp(42000,100).Select(s=>new StampSegment{DocumentKey="test",Path=s.Path,Start=Transform(s.Start),End=Transform(s.End)}).ToArray();
                var found=Find(frame,segments).Frames.Single();Equal(found.Scale,100,1e-7);
            }
        });
        Run("Stamp detects portrait paper and millimeter-independent model units",()=>
        {
            var portrait=Find(Candidate("F",0,0,29700,42000),Stamp(29700,100)).Frames.Single();Equal(portrait.PaperWidthMm,297);Equal(portrait.PaperHeightMm,420);
            var metres=Find(Candidate("M",0,0,.420,.297),Stamp(.420,.001)).Frames.Single();Equal(metres.Scale,.001,1e-10);
        });
        Run("Isolated 185-unit line is not sufficient stamp evidence",()=>
        {
            var frame=Candidate("F",0,0,420,297);Assert(Find(frame,new[]{Stamp(420,1)[0]}).Frames.Count==0);
            var shifted=Stamp(420,1).Select(s=>new StampSegment{DocumentKey="test",Path=s.Path,Start=s.Start+new Point2(-20,30),End=s.End+new Point2(-20,30)}).ToArray();Assert(Find(frame,shifted).Frames.Count==0);
        });
        Run("Wrong-width stamp and another drawing cannot determine scale",()=>
        {
            var wrong=Stamp(420,1);wrong[0].End=new Point2(wrong[0].Start.X+120,wrong[0].Start.Y);Assert(Find(Candidate("F",0,0,420,297),wrong).Frames.Count==0);
            var other=Stamp(420,1);foreach(var s in other)s.DocumentKey="other";Assert(Find(Candidate("F",0,0,420,297),other).Frames.Count==0);
        });
        Run("Stamp does not mix geometry from another layout or elevation",()=>
        {
            var other=Stamp(420,1);foreach(var s in other)s.Layout="Layout1";Assert(Find(Candidate("F",0,0,420,297),other).Frames.Count==0);
            foreach(var s in other){s.Layout="Model";s.Elevation=10;}Assert(Find(Candidate("F",0,0,420,297),other).Frames.Count==0);
        });
        Run("Stamp fallback and strict mode are explicit",()=>
        {
            var frame=Candidate("F",0,0,420,297);var fallback=Find(frame,Array.Empty<StampSegment>(),Options(false));Assert(fallback.Frames.Count==1&&fallback.Frames[0].StampEvidence==null&&fallback.Warnings.Count>0);
            Assert(Find(frame,Array.Empty<StampSegment>()).Frames.Count==0);
            var settings=Options();settings.Scales.Clear();Assert(Find(frame,Stamp(420,1),settings).Frames.Count==1);
        });
        Run("Ambiguous stamp scales are blocked even when manual fallback matches",()=>
        {
            var segments=Stamp(420,1).Concat(Stamp(420,Math.Sqrt(2),"B")).ToArray();var result=Find(Candidate("F",0,0,420,297),segments,Options(false));
            Assert(result.Frames.Count==0&&result.Warnings.Any(w=>w.Contains("разные масштабы")));
        });
        Run("Stamp proof rejects moved or removed reference geometry",()=>
        {
            var segments=Stamp(42000,100);var proof=Find(Candidate("F",0,0,42000,29700),segments).Frames.Single().StampEvidence!;
            Assert(proof.IsCurrent(segments,.02));Assert(!proof.IsCurrent(segments.Skip(1),.02));
            var changed=segments.Select(s=>new StampSegment{Path=s.Path,Index=s.Index,Start=s.Start,End=s.End,Elevation=s.Elevation}).ToArray();changed[0].End+=new Point2(10,0);Assert(!proof.IsCurrent(changed,.02));
        });
        Run("Unknown-scale LINE outline can be assembled for stamp validation",()=>
        {
            double s=37.5;var outer=Box(0,0,420*s,297*s);var lines=Enumerable.Range(0,4).Select(i=>new FrameSegment{Start=outer[i],End=outer[(i+1)%4],Path="O"+i}).ToList();
            lines.AddRange(Stamp(420*s,s).Select(x=>new FrameSegment{Start=x.Start,End=x.End,Path=x.Path}));
            var result=LineFrames.Find(lines,FormatDefinition.Defaults(),Options(),CancellationToken.None);Assert(result.Any(r=>Math.Abs(r.Rectangle.Width*r.Rectangle.Height-420*297*s*s)<.01));
        });
        Run("Stamp settings persist in JSON profile and job",()=>
        {
            string path=Path.Combine(TestRoot,Path.Combine(TestRoot,"artifacts/verification/features/stamp-profile.json"));var profile=new ProjectProfile{Search=Options()};profile.Search.Stamp!.ReferenceWidthMm=180;ProfileStore.Save(path,profile);
            var loaded=ProfileStore.Load(path);Assert(loaded.Search.Stamp!.Enabled&&loaded.Search.Stamp.OnlyStamp&&loaded.Search.Stamp.ReferenceWidthMm==180);
            var frame=Find(Candidate("F",0,0,42000,29700),Stamp(42000,100)).Frames.Single();string jobPath=path+".job.json";
            JobStore.Save(jobPath,JobStore.Capture(new ProjectProfile{Search=Options()},new[]{frame}));var job=JobStore.Load(jobPath);
            Assert(job.Rows[0].Scale==100&&job.Rows[0].StampParts!.Count==4&&job.Profile.Search.Stamp!.ReferenceWidthMm==185);
        });
        Run("Reopened job blocks changed stamp even when outer frame and scale match",()=>
        {
            var before=Find(Candidate("F",0,0,42000,29700),Stamp(42000,100)).Frames.Single();var job=JobStore.Capture(new ProjectProfile{Search=Options()},new[]{before});
            var moved=Stamp(42000,100).Select(s=>new StampSegment{DocumentKey="test",Path=s.Path,Start=s.Start+new Point2(10,0),End=s.End+new Point2(10,0)}).ToArray();
            var after=Find(Candidate("F",0,0,42000,29700),moved).Frames.Single();Equal(before.Scale,after.Scale);
            var restored=JobStore.Restore(job,new[]{after},out var warnings);Assert(warnings.Count==1&&!restored[0].Enabled&&!restored[0].Available);
        });
        Run("Legacy job without scale snapshot remains readable",()=>
        {
            var candidate=Candidate("F",0,0,420,297);var frame=Detect(candidate).Frames.Single();string path=Path.Combine(TestRoot,"artifacts/verification/features/legacy-stamp-job.json");
            JobStore.Save(path,JobStore.Capture(new ProjectProfile(),new[]{frame}));var node=System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
            foreach(var row in node["Rows"]!.AsArray()){row!.AsObject().Remove("Scale");row.AsObject().Remove("StampParts");}File.WriteAllText(path,node.ToJsonString());
            var restored=JobStore.Restore(JobStore.Load(path),new[]{Detect(Candidate("F",0,0,420,297)).Frames.Single()},out var warnings);Assert(warnings.Count==0&&restored[0].Available);
        });
        Run("Stamp inference observes cancellation",()=>
        {
            using(var cancellation=new CancellationTokenSource()){cancellation.Cancel();Throws<OperationCanceledException>(()=>FrameDetection.Detect(new[]{Candidate("F",0,0,420,297)},Options(),FormatDefinition.Defaults(),cancellation.Token,Stamp(420,1)));}
        });
        Run("Stamp spatial index handles mixed scales across 200 frames",()=>
        {
            var candidates=new List<FrameCandidate>();var segments=new List<StampSegment>();
            for(int i=0;i<200;i++)
            {
                double scale=i%2==0?1:3.7;var offset=new Point2(i%20*2000,i/20*1500);var frame=Candidate("F"+i,offset.X,offset.Y,420*scale,297*scale);candidates.Add(frame);
                segments.AddRange(Stamp(420*scale,scale,"F"+i+"S").Select(s=>new StampSegment{DocumentKey="test",Path=s.Path,Start=s.Start+offset,End=s.End+offset}));
            }
            var clock=Stopwatch.StartNew();var result=FrameDetection.Detect(candidates,Options(),FormatDefinition.Defaults(),CancellationToken.None,segments);Assert(result.Frames.Count==200&&result.Frames.All(f=>f.StampEvidence!=null));
            File.WriteAllText(Path.Combine(TestRoot,"artifacts/verification/stamp-performance.txt"),"200 mixed-scale frames: "+clock.Elapsed.TotalMilliseconds.ToString("F0",System.Globalization.CultureInfo.InvariantCulture)+" ms");
        });
    }
}
