using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CadBatch.Core
{
    public sealed class FrameSegment
    {
        public Point2 Start {get;set;}
        public Point2 End {get;set;}
        public string Path {get;set;}="";
    }
    public sealed class LineFrame
    {
        public Rectangle2 Rectangle {get;set;}=null!;
        public List<string> Members {get;set;}=new List<string>();
    }
    public static class LineFrames
    {
        private sealed class Side { public double Level;public List<Tuple<double,double,string>> Intervals=new List<Tuple<double,double,string>>(); }
        public static List<LineFrame> Find(IReadOnlyList<FrameSegment> source,IReadOnlyList<FormatDefinition> formats,SearchOptions options,CancellationToken cancellation)
        {
            if(source.Count>5000)throw new ArgumentException("Более 5000 отрезков в одном вхождении/слое. Задайте маску слоя рамок или ограничьте набор.");
            var segments=source.Where(s=>s.Start.IsFinite&&s.End.IsFinite&&(s.End-s.Start).Length>options.GeometryTolerance).ToArray();
            var directions=new List<double>();var output=new List<LineFrame>();double tolerance=options.GeometryTolerance;
            foreach(var segment in segments)
            {
                double angle=Math.Atan2(segment.End.Y-segment.Start.Y,segment.End.X-segment.Start.X)%(Math.PI/2);if(angle<0)angle+=Math.PI/2;
                if(directions.Any(a=>Math.Min(Math.Abs(a-angle),Math.PI/2-Math.Abs(a-angle))<1e-7))continue;
                if(directions.Count==32)throw new ArgumentException("На выбранных слоях слишком много направлений отрезков. Ограничьте поиск слоем рамок.");
                directions.Add(angle);cancellation.ThrowIfCancellationRequested();
                var ux=new Point2(Math.Cos(angle),Math.Sin(angle));var uy=new Point2(-ux.Y,ux.X);
                var horizontal=new List<Tuple<double,double,double,string>>();var vertical=new List<Tuple<double,double,double,string>>();
                foreach(var s in segments)
                {
                    var a=new Point2(Point2.Dot(s.Start,ux),Point2.Dot(s.Start,uy));var b=new Point2(Point2.Dot(s.End,ux),Point2.Dot(s.End,uy));
                    if(Math.Abs(a.Y-b.Y)<=tolerance)horizontal.Add(Tuple.Create((a.Y+b.Y)/2,Math.Min(a.X,b.X),Math.Max(a.X,b.X),s.Path));
                    else if(Math.Abs(a.X-b.X)<=tolerance)vertical.Add(Tuple.Create((a.X+b.X)/2,Math.Min(a.Y,b.Y),Math.Max(a.Y,b.Y),s.Path));
                }
                var hs=Group(horizontal,tolerance);var vs=Group(vertical,tolerance);
                foreach(var bottom in hs)
                foreach(var top in hs.Where(h=>h.Level>bottom.Level+tolerance))
                {
                    cancellation.ThrowIfCancellationRequested();double height=top.Level-bottom.Level;
                    var sizes=new List<Tuple<double,double>>();
                    foreach(var scale in options.Scales)foreach(var format in formats)
                    {
                        double tol=options.SizeToleranceMm*scale;
                        if(Math.Abs(height-format.HeightMm*scale)<=tol)sizes.Add(Tuple.Create(format.WidthMm*scale,tol));
                        if(Math.Abs(height-format.WidthMm*scale)<=tol)sizes.Add(Tuple.Create(format.HeightMm*scale,tol));
                    }
                    if(options.Stamp?.Enabled==true)foreach(var format in formats)
                    {
                        double s=height/format.HeightMm;sizes.Add(Tuple.Create(format.WidthMm*s,options.SizeToleranceMm*s*2));
                        s=height/format.WidthMm;sizes.Add(Tuple.Create(format.HeightMm*s,options.SizeToleranceMm*s*2));
                    }
                    if(sizes.Count==0)continue;
                    foreach(var left in vs)
                    foreach(var right in vs.Where(v=>v.Level>left.Level+tolerance&&sizes.Any(s=>Math.Abs(v.Level-left.Level-s.Item1)<=s.Item2)))
                    {
                        double x1=left.Level,x2=right.Level,y1=bottom.Level,y2=top.Level;
                        var members=new HashSet<string>(StringComparer.Ordinal);
                        if(!Covers(bottom,x1,x2,tolerance,members)||!Covers(top,x1,x2,tolerance,members)||!Covers(left,y1,y2,tolerance,members)||!Covers(right,y1,y2,tolerance,members))continue;
                        // Prefer elementary cells: adjacent sheets must not turn into an extended sheet.
                        if(vs.Any(v=>v.Level>x1+tolerance&&v.Level<x2-tolerance&&Covers(v,y1,y2,tolerance,new HashSet<string>()))||
                            hs.Any(h=>h.Level>y1+tolerance&&h.Level<y2-tolerance&&Covers(h,x1,x2,tolerance,new HashSet<string>())))continue;
                        var points=new[]{ux*x1+uy*y1,ux*x2+uy*y1,ux*x2+uy*y2,ux*x1+uy*y2};
                        if(Rectangle2.TryCreate(points,tolerance,out var rectangle)&&!output.Any(r=>r.Rectangle.SameBoundary(rectangle!,tolerance)))
                            output.Add(new LineFrame{Rectangle=rectangle!,Members=members.OrderBy(m=>m,StringComparer.Ordinal).ToList()});
                    }
                }
            }
            return output;
        }
        private static List<Side> Group(IEnumerable<Tuple<double,double,double,string>> intervals,double tolerance)
        {
            var result=new List<Side>();Side? current=null;
            foreach(var item in intervals.OrderBy(i=>i.Item1))
            {
                if(current==null||item.Item1-current.Level>tolerance){current=new Side{Level=item.Item1};result.Add(current);}
                current.Intervals.Add(Tuple.Create(item.Item2,item.Item3,item.Item4));
            }
            foreach(var side in result)side.Intervals=side.Intervals.OrderBy(i=>i.Item1).ToList();return result;
        }
        private static bool Covers(Side side,double start,double end,double tolerance,HashSet<string> members)
        {
            double covered=start;var used=new List<string>();
            foreach(var interval in side.Intervals)
            {
                if(interval.Item2<covered-tolerance)continue;if(interval.Item1>covered+tolerance)break;
                if(interval.Item1>end+tolerance)break;
                if(interval.Item2>covered){covered=interval.Item2;used.Add(interval.Item3);}
                if(covered>=end-tolerance){foreach(var path in used)members.Add(path);return true;}
            }
            return false;
        }
    }
}
