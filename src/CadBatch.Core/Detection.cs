using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace CadBatch.Core
{
    public static class FrameDetection
    {
        public static ScanResult Detect(IEnumerable<FrameCandidate> candidates, SearchOptions options,
            IReadOnlyList<FormatDefinition> formats, CancellationToken cancellation,IReadOnlyList<StampSegment>? stampGeometry=null)
        {
            options.Validate();
            if (formats.Count == 0 || formats.Any(f => !Numeric.Positive(f.WidthMm) || !Numeric.Positive(f.HeightMm))) throw new ArgumentException("Некорректный каталог форматов.");
            var result = new ScanResult();
            var stampIndex=options.Stamp?.Enabled==true?new StampScaleIndex(stampGeometry??Array.Empty<StampSegment>()):null;
            int stampsFound=0,fallbacks=0,rectangles=0,filtered=0;
            // The largest rectangles that matched no format: they tell what is wrong when nothing is found.
            var misses=new List<Rectangle2>();
            // Inner ГОСТ frames whose sheet edge is missing; added after the outlines, unless a sheet edge was found too.
            var inner=new List<(FrameCandidate Source,Rectangle2 Sheet,FormatDefinition Format,double Scale)>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            // Spatial buckets avoid a quadratic search through all detected frames.
            var buckets = new Dictionary<string,List<DetectedFrame>>(StringComparer.Ordinal);
            double bucketSize = Math.Max(options.GeometryTolerance * 2, 0.001);
            foreach (var c in candidates)
            {
                cancellation.ThrowIfCancellationRequested();
                result.ObjectsVisited++;
                if (!Wildcard(c.Layer,options.LayerPattern) || (!string.IsNullOrEmpty(c.BlockName) && !Wildcard(c.BlockName,options.BlockPattern))) { filtered++; continue; }
                if (!Rectangle2.TryCreate(c.Vertices, options.GeometryTolerance, out var geometry)) continue;
                rectangles++;
                StampScaleEvidence? evidence=null;IEnumerable<double> scales=options.Scales;
                if(stampIndex!=null)
                {
                    var stamp=stampIndex.Infer(c,geometry!,formats,options,cancellation);
                    if(stamp.Ambiguous){result.Warnings.Add(c.DocumentName+" / "+c.HandlePath+": несколько штампов дают разные масштабы. Укажите масштаб вручную и повторите поиск.");continue;}
                    evidence=stamp.Evidence;
                    if(evidence!=null)scales=new[]{evidence.Scale};
                    else if(options.Stamp!.OnlyStamp)continue;
                }
                var (match,scale)=BestFormat(geometry!,scales,formats,options.SizeToleranceMm);
                if (match == null)
                {
                    if(c.GeometryKind!="Lines"&&c.GeometryKind!="Exploded")
                    {
                        var (innerFormat,innerScale)=InnerFrames.Match(geometry!,options.Scales,formats,options.SizeToleranceMm);
                        var sheet=innerFormat!=null?InnerFrames.Expand(geometry!,innerScale):null;
                        if(sheet!=null){inner.Add((c,sheet,innerFormat!,innerScale));continue;}
                    }
                    Remember(misses,geometry!);continue;
                }
                if (!identities.Add(c.Key)) { result.Duplicates++; continue; }
                long bx = (long)Math.Floor(geometry!.Center.X / bucketSize), by = (long)Math.Floor(geometry.Center.Y / bucketSize);
                DetectedFrame? overlap = null;
                for (int x = -1; x <= 1 && overlap == null; x++)
                    for (int y = -1; y <= 1 && overlap == null; y++)
                        if (buckets.TryGetValue(Bucket(c, bx+x, by+y), out var nearby))
                            overlap = nearby.FirstOrDefault(f => f.Geometry.SameBoundary(geometry, options.GeometryTolerance));
                if (overlap != null)
                {
                    result.Duplicates++;
                    overlap.Status = "Совпадающие контуры: проверьте";
                    result.Warnings.Add(c.DocumentName + ": совпадающая рамка " + c.HandlePath + " / " + overlap.Source.HandlePath);
                    continue;
                }
                var frame = new DetectedFrame { Source = c, Geometry = geometry, Format = match, Scale = scale,StampEvidence=evidence };
                if(evidence!=null)stampsFound++;else if(stampIndex!=null)fallbacks++;
                frame.SheetNumber = ReadAttribute(c, "SHEET", "SHEETNUMBER", "ЛИСТ", "НОМЕР_ЛИСТА");
                frame.Title = ReadAttribute(c, "TITLE", "НАИМЕНОВАНИЕ", "NAME");
                frame.Code = ReadAttribute(c, "CODE", "ШИФР", "ОБОЗНАЧЕНИЕ");
                frame.Revision=ReadAttribute(c,"REVISION","REV","РЕВИЗИЯ","ИЗМ");
                foreach(var attribute in c.Attributes)frame.Metadata[attribute.Key]=attribute.Value;
                result.Frames.Add(frame);
                string key = Bucket(c, bx, by);
                if (!buckets.TryGetValue(key, out var list)) buckets[key] = list = new List<DetectedFrame>();
                list.Add(frame);
            }
            foreach(var (c,sheet,format,s) in inner)
            {
                double tolerance=Math.Max(options.GeometryTolerance,options.SizeToleranceMm*s);
                if(result.Frames.Any(f=>f.Source.DocumentKey==c.DocumentKey&&f.Layout==c.Layout&&f.Geometry.SameBoundary(sheet,tolerance))){result.Duplicates++;continue;}
                if(!identities.Add(c.Key)){result.Duplicates++;continue;}
                c.Kind=(string.IsNullOrEmpty(c.Kind)?"Контур":c.Kind)+" · внутренняя рамка";
                var frame=new DetectedFrame{Source=c,Geometry=sheet,Format=format,Scale=s};if(stampIndex!=null)fallbacks++;
                frame.SheetNumber=ReadAttribute(c,"SHEET","SHEETNUMBER","ЛИСТ","НОМЕР_ЛИСТА");frame.Title=ReadAttribute(c,"TITLE","НАИМЕНОВАНИЕ","NAME");
                frame.Code=ReadAttribute(c,"CODE","ШИФР","ОБОЗНАЧЕНИЕ");frame.Revision=ReadAttribute(c,"REVISION","REV","РЕВИЗИЯ","ИЗМ");
                foreach(var attribute in c.Attributes)frame.Metadata[attribute.Key]=attribute.Value;
                result.Frames.Add(frame);
            }
            var ordered=FrameOrder.ByDrawing(result.Frames);result.Frames.Clear();result.Frames.AddRange(ordered);
            if(result.Frames.Count==0)result.Warnings.Add(NothingFound(rectangles,filtered,misses,options));
            if(stampIndex!=null&&(fallbacks>0||stampsFound==0))result.Warnings.Add("Масштаб по штампу подтверждён у рамок: "+stampsFound+"; использован список масштабов: "+fallbacks+". Проверьте параметры штампа и колонку «Источник масштаба».");
            return result;
        }
        private static (FormatDefinition? Match,double Scale) BestFormat(Rectangle2 geometry,IEnumerable<double> scales,IReadOnlyList<FormatDefinition> formats,double toleranceMm)
        {
            double best = double.MaxValue; FormatDefinition? match = null; double scale = 0;
            foreach (var s in scales)
                foreach (var f in formats)
                {
                    double a = geometry.Width / s, b = geometry.Height / s;
                    foreach (bool swap in new[] { false, true })
                    {
                        double dw = Math.Abs(a - (swap ? f.HeightMm : f.WidthMm));
                        double dh = Math.Abs(b - (swap ? f.WidthMm : f.HeightMm));
                        double error = dw / f.WidthMm + dh / f.HeightMm;
                        if (dw <= toleranceMm && dh <= toleranceMm && error < best)
                        { best = error; match = f; scale = s; }
                    }
                }
            return (match,scale);
        }
        private static void Remember(List<Rectangle2> misses,Rectangle2 r)
        {
            if(misses.Any(m=>Math.Abs(m.Width-r.Width)<1e-6&&Math.Abs(m.Height-r.Height)<1e-6))return;
            misses.Add(r);misses.Sort((a,b)=>(b.Width*b.Height).CompareTo(a.Width*a.Height));if(misses.Count>5)misses.RemoveAt(5);
        }
        public const string NothingFoundPrefix="Рамки не найдены: ";
        private static string NothingFound(int rectangles,int filtered,List<Rectangle2> misses,SearchOptions options)
        {
            var culture=System.Globalization.CultureInfo.CurrentCulture;
            string text=NothingFoundPrefix+"замкнутых прямоугольников — "+rectangles+(filtered>0?", отброшено по маске слоя или блока — "+filtered:"")+"; "+(options.Scales.Count>6?"проверено масштабов — "+options.Scales.Count:"масштабы: "+string.Join("; ",options.Scales.Select(s=>s.ToString("0.###",culture))))+".";
            if(misses.Count>0)text+=" Самые крупные прямоугольники (единиц DWG): "+string.Join(", ",misses.Select(m=>Math.Max(m.Width,m.Height).ToString("0.##",culture)+" × "+Math.Min(m.Width,m.Height).ToString("0.##",culture)))+".";
            return text;
        }
        private static string Bucket(FrameCandidate c,long x,long y) => c.DocumentKey + "|" + c.Layout + "|" + x + ":" + y;
        public static string ReadAttribute(FrameCandidate c,params string[] names)
        {
            foreach (var name in names) if (c.Attributes.TryGetValue(name,out var value) && !string.IsNullOrWhiteSpace(value)) return value.Trim();
            return "";
        }
        public static bool Wildcard(string value,string pattern)
        {
            if (string.IsNullOrWhiteSpace(pattern) || pattern == "*") return true;
            return Regex.IsMatch(value,"^" + Regex.Escape(pattern).Replace("\\*", ".*").Replace("\\?", ".") + "$",RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,TimeSpan.FromSeconds(1));
        }
    }
}
