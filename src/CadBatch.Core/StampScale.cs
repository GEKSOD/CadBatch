using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;

namespace CadBatch.Core
{
    [DataContract]
    public sealed class StampScaleSettings
    {
        [DataMember] public bool Enabled {get;set;}
        [DataMember] public bool OnlyStamp {get;set;}
        [DataMember] public double ReferenceWidthMm {get;set;}=185;
        [DataMember] public double HeightMm {get;set;}=55;
        [DataMember] public double RightMarginMm {get;set;}=5;
        [DataMember] public double BottomMarginMm {get;set;}=5;
        [DataMember] public double ToleranceMm {get;set;}=.5;
        public void Validate()
        {
            if(!Numeric.Positive(ReferenceWidthMm)||!Numeric.Positive(HeightMm)||!Numeric.NonNegative(RightMarginMm)||!Numeric.NonNegative(BottomMarginMm)||!Numeric.Positive(ToleranceMm)||ToleranceMm>=Math.Min(ReferenceWidthMm,HeightMm)/4)
                throw new ArgumentException("Проверьте ширину/высоту штампа, поля и допуск. Допуск должен быть меньше четверти размеров штампа.");
        }
        public StampScaleSettings Copy()=>new StampScaleSettings{Enabled=Enabled,OnlyStamp=OnlyStamp,ReferenceWidthMm=ReferenceWidthMm,HeightMm=HeightMm,RightMarginMm=RightMarginMm,BottomMarginMm=BottomMarginMm,ToleranceMm=ToleranceMm};
    }
    public sealed class StampSegment
    {
        public string DocumentKey {get;set;}="";
        public string Layout {get;set;}="Model";
        public string Path {get;set;}="";
        public int Index {get;set;}
        public Point2 Start {get;set;}
        public Point2 End {get;set;}
        public double Elevation {get;set;}
        public string Key=>Path+":"+Index;
    }
    public sealed class StampScaleEvidence
    {
        public double Scale {get;set;}
        public Point2 HeaderStart {get;set;}
        public Point2 HeaderEnd {get;set;}
        public StampScaleSettings Settings {get;set;}=new StampScaleSettings();
        public List<StampSegment> Members {get;set;}=new List<StampSegment>();
        public bool IsCurrent(IEnumerable<StampSegment> current,double tolerance)
        {
            if(!Numeric.Positive(tolerance))throw new ArgumentException("Некорректный допуск проверки штампа.");
            var segments=current.ToLookup(s=>s.Key,StringComparer.Ordinal);
            return Members.Count>0&&Members.All(before=>segments[before.Key].Any(after=>Math.Abs(after.Elevation-before.Elevation)<=tolerance&&
                (before.Start.Distance(after.Start)<=tolerance&&before.End.Distance(after.End)<=tolerance||before.Start.Distance(after.End)<=tolerance&&before.End.Distance(after.Start)<=tolerance)));
        }
    }
    public sealed class StampScaleResult
    {
        public StampScaleEvidence? Evidence {get;set;}
        public bool Ambiguous {get;set;}
    }
    // BVH limits each title-block query to a small region; stamp lines need not use the frame layer.
    public sealed class StampScaleIndex
    {
        private sealed class Node
        {
            public double X1,Y1,X2,Y2;public Node? Left,Right;public StampSegment[]? Items;
        }
        private readonly Dictionary<string,Node> spaces=new Dictionary<string,Node>(StringComparer.Ordinal);
        public StampScaleIndex(IEnumerable<StampSegment> segments)
        {
            foreach(var group in segments.Where(s=>s.Start.IsFinite&&s.End.IsFinite&&s.Start.Distance(s.End)>0).GroupBy(s=>Space(s.DocumentKey,s.Layout)))
                spaces[group.Key]=Build(group.ToArray());
        }
        private static string Space(string document,string layout)=>document+"\u001f"+layout;
        private static Node Build(StampSegment[] items)
        {
            var node=new Node{X1=items.Min(s=>Math.Min(s.Start.X,s.End.X)),Y1=items.Min(s=>Math.Min(s.Start.Y,s.End.Y)),X2=items.Max(s=>Math.Max(s.Start.X,s.End.X)),Y2=items.Max(s=>Math.Max(s.Start.Y,s.End.Y))};
            if(items.Length<=24)node.Items=items;
            else
            {
                bool x=node.X2-node.X1>=node.Y2-node.Y1;var sorted=items.OrderBy(s=>x?s.Start.X+s.End.X:s.Start.Y+s.End.Y).ToArray();int middle=items.Length/2;
                node.Left=Build(sorted.Take(middle).ToArray());node.Right=Build(sorted.Skip(middle).ToArray());
            }
            return node;
        }
        private static void Query(Node node,double x1,double y1,double x2,double y2,List<StampSegment> output)
        {
            if(node.X1>x2||node.X2<x1||node.Y1>y2||node.Y2<y1)return;
            if(node.Items!=null){foreach(var s in node.Items)if(Math.Min(s.Start.X,s.End.X)<=x2&&Math.Max(s.Start.X,s.End.X)>=x1&&Math.Min(s.Start.Y,s.End.Y)<=y2&&Math.Max(s.Start.Y,s.End.Y)>=y1)output.Add(s);}
            else{Query(node.Left!,x1,y1,x2,y2,output);Query(node.Right!,x1,y1,x2,y2,output);}
        }
        private sealed class Span
        {
            public double From,To,Level;public List<StampSegment> Members=new List<StampSegment>();
        }
        private static List<Span> Spans(IEnumerable<Tuple<double,double,double,StampSegment>> input,double tolerance)
        {
            var levels=new List<List<Tuple<double,double,double,StampSegment>>>();
            foreach(var item in input.OrderBy(i=>i.Item1))
            {
                if(levels.Count==0||item.Item1-levels[levels.Count-1][0].Item1>tolerance)levels.Add(new List<Tuple<double,double,double,StampSegment>>());
                levels[levels.Count-1].Add(item);
            }
            var spans=new List<Span>();
            foreach(var level in levels)
            {
                Span? span=null;
                foreach(var item in level.OrderBy(i=>i.Item2))
                {
                    if(span==null||item.Item2>span.To+tolerance){span=new Span{From=item.Item2,To=item.Item3,Level=level[0].Item1};spans.Add(span);}
                    else span.To=Math.Max(span.To,item.Item3);
                    span.Members.Add(item.Item4);
                }
            }
            return spans;
        }
        private static Span? Cover(IEnumerable<Span> spans,double level,double from,double to,double tolerance)=>spans.FirstOrDefault(s=>Math.Abs(s.Level-level)<=tolerance&&s.From<=from+tolerance&&s.To>=to-tolerance);
        public StampScaleResult Infer(FrameCandidate source,Rectangle2 frame,IReadOnlyList<FormatDefinition> formats,SearchOptions options,CancellationToken cancellation)
        {
            var result=new StampScaleResult();var settings=options.Stamp;if(settings?.Enabled!=true||!spaces.TryGetValue(Space(source.DocumentKey,source.Layout),out var root))return result;
            var found=new List<StampScaleEvidence>();
            for(int corner=0;corner<4;corner++)foreach(int winding in new[]{1,-1})
            {
                cancellation.ThrowIfCancellationRequested();var origin=frame.Corners[corner];var dx=frame.Corners[(corner+winding+4)%4]-origin;var dy=frame.Corners[(corner-winding+4)%4]-origin;
                double width=dx.Length,height=dy.Length;var u=dx*(1/width);var v=dy*(1/height);
                var proposals=new List<double>();
                foreach(var format in formats)foreach(bool swap in new[]{false,true})
                {
                    double fw=swap?format.HeightMm:format.WidthMm,fh=swap?format.WidthMm:format.HeightMm;
                    double scale=(width/fw+height/fh)/2;
                    if(!Numeric.Positive(scale)||Math.Abs(width/scale-fw)>options.SizeToleranceMm||Math.Abs(height/scale-fh)>options.SizeToleranceMm)continue;
                    if(!proposals.Any(s=>Math.Abs(s-scale)<=scale*1e-7))proposals.Add(scale);
                }
                foreach(double proposed in proposals)
                {
                    double xRight=width-settings.RightMarginMm*proposed,xLeft=xRight-settings.ReferenceWidthMm*proposed;
                    double yBottom=settings.BottomMarginMm*proposed,yTop=yBottom+settings.HeightMm*proposed;
                    double halo=(settings.ToleranceMm+options.SizeToleranceMm*2+1)*proposed;
                    if(xLeft<-halo||yTop>height+halo)continue;
                    var corners=new[]{origin+u*xLeft+v*yBottom,origin+u*xRight+v*yBottom,origin+u*xRight+v*yTop,origin+u*xLeft+v*yTop};
                    var nearby=new List<StampSegment>();Query(root,corners.Min(p=>p.X)-halo,corners.Min(p=>p.Y)-halo,corners.Max(p=>p.X)+halo,corners.Max(p=>p.Y)+halo,nearby);
                    var horizontal=new List<Tuple<double,double,double,StampSegment>>();var vertical=new List<Tuple<double,double,double,StampSegment>>();
                    double tolerance=settings.ToleranceMm*proposed,angular=Math.Max(1e-9,Math.Min(options.GeometryTolerance,tolerance*.05));
                    foreach(var segment in nearby)
                    {
                        if(Math.Abs(segment.Elevation-source.Elevation)>Math.Max(1e-9,Math.Min(options.GeometryTolerance,tolerance)))continue;
                        var a=segment.Start-origin;var b=segment.End-origin;double ax=Point2.Dot(a,u),ay=Point2.Dot(a,v),bx=Point2.Dot(b,u),by=Point2.Dot(b,v);
                        if(Math.Abs(ay-by)<=angular)horizontal.Add(Tuple.Create((ay+by)/2,Math.Min(ax,bx),Math.Max(ax,bx),segment));
                        else if(Math.Abs(ax-bx)<=angular)vertical.Add(Tuple.Create((ax+bx)/2,Math.Min(ay,by),Math.Max(ay,by),segment));
                    }
                    var hs=Spans(horizontal,tolerance);var vs=Spans(vertical,tolerance);
                    foreach(var header in hs.Where(h=>Math.Abs(h.Level-yTop)<=halo))
                    {
                        double scale=Measure(header.To-header.From,settings.ReferenceWidthMm);double tol=settings.ToleranceMm*scale;
                        double bottom=settings.BottomMarginMm*scale,top=(settings.BottomMarginMm+settings.HeightMm)*scale;
                        if(Math.Abs(header.To-(width-settings.RightMarginMm*scale))>tol||Math.Abs(header.Level-top)>tol||header.From<-tol||top>height+tol)continue;
                        bool fits=formats.Any(f=>Fits(width/scale,height/scale,f,options.SizeToleranceMm));if(!fits)continue;
                        var lower=Cover(hs,bottom,header.From,header.To,tol);var left=Cover(vs,header.From,bottom,top,tol);var right=Cover(vs,header.To,bottom,top,tol);
                        if(lower==null||left==null||right==null)continue;
                        if(found.Any(e=>Math.Abs(e.Scale-scale)<=scale*1e-6))continue;
                        found.Add(new StampScaleEvidence{Scale=scale,HeaderStart=origin+u*header.From+v*header.Level,HeaderEnd=origin+u*header.To+v*header.Level,Settings=settings.Copy(),
                            Members=header.Members.Concat(lower.Members).Concat(left.Members).Concat(right.Members).GroupBy(s=>s.Key,StringComparer.Ordinal).Select(g=>g.First()).ToList()});
                    }
                }
            }
            if(found.Count==1)result.Evidence=found[0];else if(found.Count>1)result.Ambiguous=true;
            return result;
        }
        private static bool Fits(double width,double height,FormatDefinition format,double tolerance)=>
            Math.Abs(width-format.WidthMm)<=tolerance&&Math.Abs(height-format.HeightMm)<=tolerance||Math.Abs(width-format.HeightMm)<=tolerance&&Math.Abs(height-format.WidthMm)<=tolerance;
        public static double Measure(double measuredLength,double referenceWidthMm)
        {
            if(!Numeric.Positive(measuredLength)||!Numeric.Positive(referenceWidthMm)||!Numeric.Positive(measuredLength/referenceWidthMm))throw new ArgumentException("Длина линии штампа и её бумажная ширина должны быть положительными.");
            return measuredLength/referenceWidthMm;
        }
    }
    public sealed class StampScaleMeasurement
    {
        public double Length {get;set;}
        public double Scale {get;set;}
        public string Source {get;set;}="";
    }
}
