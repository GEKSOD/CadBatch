using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CadBatch.Core
{
    // When a search finds nothing, the settings are usually the reason, not the drawing: a new computer starts with
    // "model only, scale 1", while the frames sit on layout tabs or are drawn at 1:100. The window then searches again
    // with wider settings and offers to keep only what was needed to find the frames.
    public static class SearchFallback
    {
        // Common drawing scales in DWG units per paper millimetre, most usual first: millimetre drawings, then drawings in metres.
        // The order breaks ties: A-series sheets are similar, so A1 at 1:100 is also A3 at 1:200 and A0×2 at 1:50.
        public static readonly double[] CommonScales={100,1,50,200,10,20,25,500,1000,5,2,2.5,4,75,250,400,2000,5000,0.1,0.001,0.05,0.2,0.01,0.02,0.025,0.5,0.005,0.002,0.25};
        public static int Stages=>2;
        // Stage 1: everywhere and every kind of frame, with the user's scales and the scale from the title block.
        // Stage 2: the same with the common scales.
        public static SearchOptions Wider(SearchOptions original,int stage)
        {
            if(stage<1||stage>Stages)throw new ArgumentOutOfRangeException(nameof(stage));
            var stamp=original.Stamp?.Copy()??new StampScaleSettings();stamp.Enabled=true;stamp.OnlyStamp=false;
            var scales=original.Scales.ToList();if(stage==2)scales.AddRange(CommonScales);
            return new SearchOptions
            {
                Files=original.Files,Region=original.Region,Selection=original.Selection,AllOpenDocuments=original.AllOpenDocuments,
                Model=true,Layouts=true,ActiveLayoutOnly=false,Polylines=true,Blocks=true,Lines=true,ExternalReferences=true,SpecialEntities=true,Viewports=original.Viewports,
                LayerPattern="*",BlockPattern="*",Scales=Distinct(scales),SizeToleranceMm=original.SizeToleranceMm,GeometryTolerance=original.GeometryTolerance,Stamp=stamp
            };
        }
        // The original settings plus only what the found frames need. alternatives: other scales that explain the frames
        // just as well (the drawing has no title block to tell them apart); the user is asked to check the formats.
        public static SearchOptions Adopt(SearchOptions original,IReadOnlyCollection<DetectedFrame> found,IReadOnlyList<FormatDefinition> formats)=>Adopt(original,found,formats,out _);
        public static SearchOptions Adopt(SearchOptions original,IReadOnlyCollection<DetectedFrame> found,IReadOnlyList<FormatDefinition> formats,out List<double> alternatives)
        {
            var s=new SearchOptions
            {
                Files=original.Files,Region=original.Region,Selection=original.Selection,AllOpenDocuments=original.AllOpenDocuments,Model=original.Model,Layouts=original.Layouts,ActiveLayoutOnly=original.ActiveLayoutOnly,
                Polylines=original.Polylines,Blocks=original.Blocks,Lines=original.Lines,ExternalReferences=original.ExternalReferences,SpecialEntities=original.SpecialEntities,Viewports=original.Viewports,
                LayerPattern=original.LayerPattern,BlockPattern=original.BlockPattern,Scales=original.Scales.ToList(),SizeToleranceMm=original.SizeToleranceMm,GeometryTolerance=original.GeometryTolerance,Stamp=original.Stamp?.Copy()
            };
            // Scales first: frames that fit none of the chosen scales (small rectangles at odd scales) need no settings.
            var loose=found.Where(f=>f.StampEvidence==null&&!original.Scales.Any(x=>Fits(f,x,formats,original.SizeToleranceMm,false))).ToList();
            // Frames the user's scales already explain set the size a real sheet has in this drawing.
            double reference=found.Where(f=>f.StampEvidence!=null||!loose.Contains(f)).Select(Area).DefaultIfEmpty(0).Max();
            foreach(double scale in ChooseScales(loose,formats,original.SizeToleranceMm,out alternatives,reference))if(!s.Scales.Any(x=>Same(x,scale)))s.Scales.Add(scale);
            var kept=Explained(s,found,formats);
            foreach(var f in kept)
            {
                var c=f.Source;
                if(IsModel(c.Layout))s.Model=true;else s.Layouts=true;
                if(c.GeometryKind=="Lines")s.Lines=true;
                if(c.GeometryKind=="Exploded")s.SpecialEntities=true;
                if(c.IsExternal)s.ExternalReferences=true;
                if(!string.IsNullOrEmpty(c.BlockName)||c.IsExternal)s.Blocks=true;
                else if(c.GeometryKind!="Lines"&&c.GeometryKind!="Exploded"&&c.GeometryKind!="Viewport")s.Polylines=true;
                if(!FrameDetection.Wildcard(c.Layer,s.LayerPattern))s.LayerPattern="*";
                if(!string.IsNullOrEmpty(c.BlockName)&&!FrameDetection.Wildcard(c.BlockName,s.BlockPattern))s.BlockPattern="*";
                if(f.StampEvidence!=null){s.Stamp=f.StampEvidence.Settings.Copy();s.Stamp.Enabled=true;s.Stamp.OnlyStamp=false;}
            }
            if(s.ActiveLayoutOnly&&kept.Select(f=>f.Layout).Distinct(StringComparer.Ordinal).Skip(1).Any())s.ActiveLayoutOnly=false;
            s.Scales=Distinct(s.Scales);
            return s;
        }
        // Greedy. Real frames are the largest rectangles of a drawing, while small ones (windows, tables) fit some format at
        // odd scales; so a scale is ranked by the area of the frames it explains, then by how many become plain A0–A4
        // sheets, then by how many it explains at all, then by how usual it is. Further scales must explain sheets of a
        // comparable size; at most three scales.
        public static List<double> ChooseScales(IReadOnlyCollection<DetectedFrame> frames,IReadOnlyList<FormatDefinition> formats,double toleranceMm,out List<double> alternatives,double referenceArea=0)
        {
            var chosen=new List<double>();alternatives=new List<double>();var remaining=frames.ToList();double largest=referenceArea;
            var scales=Distinct(CommonScales.Concat(frames.Select(f=>f.Scale)));
            while(remaining.Count>0&&chosen.Count<3)
            {
                var ranked=scales.Where(x=>!chosen.Any(y=>Same(x,y))).Select((x,rank)=>
                {
                    var fit=remaining.Where(f=>Fits(f,x,formats,toleranceMm,false)).ToList();double area=fit.Sum(Area);
                    return new{Scale=x,Rank=rank,Area=area,AreaKey=area>0?Math.Round(Math.Log(area)*1000):double.MinValue,Biggest=fit.Count==0?0:fit.Max(Area),Plain=fit.Count(f=>Fits(f,x,formats,toleranceMm,true)),Any=fit.Count};
                }).Where(r=>r.Any>0).OrderByDescending(r=>r.AreaKey).ThenByDescending(r=>r.Plain).ThenByDescending(r=>r.Any).ThenBy(r=>r.Rank).ToList();
                if(ranked.Count==0)break;
                var best=ranked[0];
                if(best.Biggest<largest*.05)break;
                if(chosen.Count==0)alternatives=ranked.Skip(1).Where(r=>r.AreaKey==best.AreaKey&&r.Plain==best.Plain&&r.Any==best.Any).Select(r=>r.Scale).ToList();
                chosen.Add(best.Scale);largest=Math.Max(largest,best.Biggest);remaining.RemoveAll(f=>Fits(f,best.Scale,formats,toleranceMm,false));
            }
            return chosen;
        }
        private static double Area(DetectedFrame f)=>f.Geometry.Width*f.Geometry.Height;
        // The frames that the adopted settings will show: confirmed by a title block or fitting one of its scales.
        public static List<DetectedFrame> Explained(SearchOptions adopted,IEnumerable<DetectedFrame> frames,IReadOnlyList<FormatDefinition> formats)=>
            frames.Where(f=>f.StampEvidence!=null||adopted.Scales.Any(x=>Fits(f,x,formats,adopted.SizeToleranceMm,false))).ToList();
        private static bool Fits(DetectedFrame f,double scale,IReadOnlyList<FormatDefinition> formats,double toleranceMm,bool plainOnly)
        {
            double a=f.Geometry.Width/scale,b=f.Geometry.Height/scale;
            foreach(var format in formats)
            {
                if(plainOnly&&format.Name.IndexOf('×')>=0)continue;
                if((Math.Abs(a-format.WidthMm)<=toleranceMm&&Math.Abs(b-format.HeightMm)<=toleranceMm)||(Math.Abs(a-format.HeightMm)<=toleranceMm&&Math.Abs(b-format.WidthMm)<=toleranceMm))return true;
            }
            return false;
        }
        // What decides which frames a search finds; equal signatures find the same frames.
        public static string Signature(SearchOptions o)=>string.Join("|",new object[]{o.Model,o.Layouts,o.ActiveLayoutOnly,o.AllOpenDocuments,o.Polylines,o.Blocks,o.Lines,o.ExternalReferences,o.SpecialEntities,o.Viewports,o.LayerPattern,o.BlockPattern,
            string.Join(";",o.Scales.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))),o.SizeToleranceMm.ToString("R",CultureInfo.InvariantCulture),o.Stamp?.Enabled==true,o.Stamp?.OnlyStamp==true,string.Join(";",o.Files??new List<string>()),o.Region==null?"":o.Region.DocumentPath+"/"+o.Region.Layout+"/"+o.Region.Points.Count,o.Selection==null?"":o.Selection.DocumentPath+"/"+o.Selection.Layout+"/"+string.Join(",",o.Selection.Handles)});
        // Plain words for what changed, for the question to the user.
        public static List<string> Changes(SearchOptions before,SearchOptions after)
        {
            var list=new List<string>();
            if(after.Layouts&&!before.Layouts)list.Add("искать и на листах (вкладки внизу AutoCAD)");
            if(after.Model&&!before.Model)list.Add("искать и в модели");
            if(!after.ActiveLayoutOnly&&before.ActiveLayoutOnly)list.Add("искать на всех листах, а не только на открытом");
            var added=after.Scales.Where(x=>!before.Scales.Any(y=>Same(x,y))).ToList();
            if(added.Count>0)list.Add((added.Count==1?"добавить масштаб ":"добавить масштабы ")+string.Join(", ",added.Select(Scale)));
            if(after.Stamp?.Enabled==true&&before.Stamp?.Enabled!=true)list.Add("определять масштаб по штампу");
            if(after.Polylines&&!before.Polylines)list.Add("искать рамки-полилинии");
            if(after.Blocks&&!before.Blocks)list.Add("искать рамки в блоках");
            if(after.Lines&&!before.Lines)list.Add("искать рамки из отдельных отрезков");
            if(after.ExternalReferences&&!before.ExternalReferences)list.Add("смотреть внешние ссылки (Xref)");
            if(after.SpecialEntities&&!before.SpecialEntities)list.Add("смотреть объекты СПДС и proxy");
            if(after.LayerPattern!=before.LayerPattern)list.Add("любой слой вместо «"+before.LayerPattern+"»");
            if(after.BlockPattern!=before.BlockPattern)list.Add("любое имя блока вместо «"+before.BlockPattern+"»");
            return list;
        }
        // "1:100" for a millimetre drawing; drawings in metres read as "1:100 (чертёж в метрах)".
        public static string Scale(double unitsPerMm)
        {
            if(unitsPerMm>=1)return "1:"+unitsPerMm.ToString("0.##",CultureInfo.CurrentCulture);
            double metres=unitsPerMm*1000;
            return "1:"+metres.ToString("0.##",CultureInfo.CurrentCulture)+" (чертёж в метрах)";
        }
        private static bool IsModel(string layout)=>string.Equals(layout,"Model",StringComparison.OrdinalIgnoreCase);
        private static bool Same(double a,double b)=>Math.Abs(a-b)<=Math.Max(Math.Abs(a),Math.Abs(b))*1e-9;
        private static List<double> Distinct(IEnumerable<double> scales)
        {
            var list=new List<double>();foreach(double x in scales)if(Numeric.Positive(x)&&!list.Any(y=>Same(x,y)))list.Add(x);return list;
        }
    }
}
