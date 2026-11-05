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
    // Frames drawn as separate lines. A plan with thousands of lines at many angles must never fail the search:
    // only the main directions are checked, sides are matched by size through sorted levels, and the work is capped.
    public static class LineFrames
    {
        private sealed class Side { public double Level;public List<Tuple<double,double,string>> Intervals=new List<Tuple<double,double,string>>(); }
        public const int MaxDirections=16;
        // Without a known scale (title-block mode) every pair of sides is a candidate; beyond this many sides that is skipped.
        public const int MaxSidesForAnyScale=60;
        public const long WorkLimit=4_000_000;
        public static List<LineFrame> Find(IReadOnlyList<FrameSegment> source,IReadOnlyList<FormatDefinition> formats,SearchOptions options,CancellationToken cancellation,List<string>? warnings=null)
        {
            cancellation.ThrowIfCancellationRequested();
            double tolerance=options.GeometryTolerance;
            var segments=source.Where(s=>s.Start.IsFinite&&s.End.IsFinite&&(s.End-s.Start).Length>tolerance).ToArray();
            var output=new List<LineFrame>();
            // Directions modulo 90°: a frame needs at least four lines of one family (two sides each way).
            var families=new Dictionary<long,(double Angle,int Count)>();
            foreach(var segment in segments)
            {
                double angle=Math.Atan2(segment.End.Y-segment.Start.Y,segment.End.X-segment.Start.X)%(Math.PI/2);if(angle<0)angle+=Math.PI/2;
                if(Math.PI/2-angle<1e-7)angle=0;
                long key=(long)Math.Round(angle*1e7);
                long found=families.ContainsKey(key)?key:families.ContainsKey(key-1)?key-1:families.ContainsKey(key+1)?key+1:key;
                families[found]=families.TryGetValue(found,out var family)?(family.Angle,family.Count+1):(angle,1);
            }
            var usable=families.Values.Where(f=>f.Count>=4).OrderByDescending(f=>f.Count).ToList();
            if(usable.Count>MaxDirections)warnings?.Add("Рамки из отрезков: проверены "+MaxDirections+" основных направлений из "+usable.Count+"; рамки под редкими углами могут быть пропущены.");
            long work=0;bool capped=false,anyScaleSkipped=false;
            foreach(var family in usable.Take(MaxDirections))
            {
                cancellation.ThrowIfCancellationRequested();
                FindInDirection(family.Angle,segments,formats,options,tolerance,output,cancellation,ref work,ref capped,ref anyScaleSkipped);
                if(capped)break;
            }
            if(anyScaleSkipped)warnings?.Add("Рамки из отрезков: слишком много отрезков, чтобы искать их без списка масштабов; использован только список масштабов.");
            if(capped)warnings?.Add("Рамки из отрезков: слишком много отрезков на слое; проверка остановлена, часть рамок могла быть пропущена. Ограничьте поиск слоем рамок.");
            return output;
        }
        private static void FindInDirection(double angle,FrameSegment[] segments,IReadOnlyList<FormatDefinition> formats,SearchOptions options,double tolerance,List<LineFrame> output,CancellationToken cancellation,ref long work,ref bool capped,ref bool anyScaleSkipped)
        {
            var ux=new Point2(Math.Cos(angle),Math.Sin(angle));var uy=new Point2(-ux.Y,ux.X);
            var horizontal=new List<Tuple<double,double,double,string>>();var vertical=new List<Tuple<double,double,double,string>>();
            foreach(var s in segments)
            {
                var a=new Point2(Point2.Dot(s.Start,ux),Point2.Dot(s.Start,uy));var b=new Point2(Point2.Dot(s.End,ux),Point2.Dot(s.End,uy));
                if(Math.Abs(a.Y-b.Y)<=tolerance)horizontal.Add(Tuple.Create((a.Y+b.Y)/2,Math.Min(a.X,b.X),Math.Max(a.X,b.X),s.Path));
                else if(Math.Abs(a.X-b.X)<=tolerance)vertical.Add(Tuple.Create((a.X+b.X)/2,Math.Min(a.Y,b.Y),Math.Max(a.Y,b.Y),s.Path));
            }
            if(horizontal.Count<2||vertical.Count<2)return;
            var hs=Group(horizontal,tolerance);var vs=Group(vertical,tolerance);
            var hLevels=hs.Select(h=>h.Level).ToArray();var vLevels=vs.Select(v=>v.Level).ToArray();
            bool anyScale=options.Stamp?.Enabled==true;
            if(anyScale&&(hs.Count>MaxSidesForAnyScale||vs.Count>MaxSidesForAnyScale)){anyScale=false;anyScaleSkipped=true;}
            // Paper sizes in DWG units for the listed scales: (side along the pair, the other side, tolerance).
            var sizes=new List<(double Across,double Along,double Tol)>();
            foreach(var scale in options.Scales)foreach(var format in formats)
            {
                double tol=options.SizeToleranceMm*scale;
                sizes.Add((format.HeightMm*scale,format.WidthMm*scale,tol));sizes.Add((format.WidthMm*scale,format.HeightMm*scale,tol));
            }
            var tops=new SortedSet<int>();var rights=new SortedSet<int>();var widths=new List<Tuple<double,double>>();
            for(int i=0;i<hs.Count;i++)
            {
                var bottom=hs[i];tops.Clear();
                if(anyScale)for(int j=i+1;j<hs.Count;j++)tops.Add(j);
                else foreach(var size in sizes)AddRange(hLevels,bottom.Level+size.Across-size.Tol,bottom.Level+size.Across+size.Tol,i+1,tops);
                foreach(int j in tops)
                {
                    var top=hs[j];double height=top.Level-bottom.Level;if(height<=tolerance)continue;
                    if(++work>WorkLimit){capped=true;return;}
                    if((work&1023)==0)cancellation.ThrowIfCancellationRequested();
                    widths.Clear();
                    foreach(var size in sizes)if(Math.Abs(height-size.Across)<=size.Tol)widths.Add(Tuple.Create(size.Along,size.Tol));
                    if(anyScale)foreach(var format in formats)
                    {
                        double s=height/format.HeightMm;widths.Add(Tuple.Create(format.WidthMm*s,options.SizeToleranceMm*s*2));
                        s=height/format.WidthMm;widths.Add(Tuple.Create(format.HeightMm*s,options.SizeToleranceMm*s*2));
                    }
                    if(widths.Count==0)continue;
                    for(int k=0;k<vs.Count;k++)
                    {
                        var left=vs[k];rights.Clear();
                        foreach(var w in widths)AddRange(vLevels,left.Level+w.Item1-w.Item2,left.Level+w.Item1+w.Item2,k+1,rights);
                        foreach(int r in rights)
                        {
                            var right=vs[r];if(right.Level<=left.Level+tolerance)continue;
                            if(++work>WorkLimit){capped=true;return;}
                            double x1=left.Level,x2=right.Level,y1=bottom.Level,y2=top.Level;
                            var members=new HashSet<string>(StringComparer.Ordinal);
                            if(!Covers(bottom,x1,x2,tolerance,members)||!Covers(top,x1,x2,tolerance,members)||!Covers(left,y1,y2,tolerance,members)||!Covers(right,y1,y2,tolerance,members))continue;
                            // Prefer elementary cells: adjacent sheets must not turn into an extended sheet.
                            if(vs.Any(v=>v.Level>x1+tolerance&&v.Level<x2-tolerance&&Covers(v,y1,y2,tolerance,new HashSet<string>()))||
                                hs.Any(h=>h.Level>y1+tolerance&&h.Level<y2-tolerance&&Covers(h,x1,x2,tolerance,new HashSet<string>())))continue;
                            var points=new[]{ux*x1+uy*y1,ux*x2+uy*y1,ux*x2+uy*y2,ux*x1+uy*y2};
                            if(Rectangle2.TryCreate(points,tolerance,out var rectangle)&&!output.Any(o=>o.Rectangle.SameBoundary(rectangle!,tolerance)))
                                output.Add(new LineFrame{Rectangle=rectangle!,Members=members.OrderBy(m=>m,StringComparer.Ordinal).ToList()});
                        }
                    }
                }
            }
        }
        // Indices (from "first" on) of sorted levels within [low, high].
        private static void AddRange(double[] levels,double low,double high,int first,SortedSet<int> result)
        {
            int index=Array.BinarySearch(levels,first,levels.Length-first,low);if(index<0)index=~index;
            for(;index<levels.Length&&levels[index]<=high;index++)result.Add(index);
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
