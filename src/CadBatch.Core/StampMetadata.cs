using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;

namespace CadBatch.Core
{
    [DataContract] public sealed class StampFieldZone
    {
        [DataMember] public string Field {get;set;}="Title";
        [DataMember] public double X {get;set;}
        [DataMember] public double Y {get;set;}
        [DataMember] public double Width {get;set;}
        [DataMember] public double Height {get;set;}
        public StampFieldZone(){}
        public StampFieldZone(string field,double x,double y,double width,double height){Field=field;X=x;Y=y;Width=width;Height=height;}
    }
    [DataContract] public sealed class StampMetadataSettings
    {
        [DataMember] public bool Enabled {get;set;}=true;
        [DataMember] public string Form {get;set;}="СПДС 185×55";
        [DataMember] public List<StampFieldZone> Zones {get;set;}=SpdsZones();
        public static List<StampFieldZone> SpdsZones()=>new List<StampFieldZone>{new StampFieldZone("Title",65,15,70,15),new StampFieldZone("Code",65,45,120,10),new StampFieldZone("SheetNumber",150,20,15,10),new StampFieldZone("SheetCount",165,20,20,10),new StampFieldZone("Revision",0,35,10,10)};
        public static List<StampFieldZone> EskdZones()=>new List<StampFieldZone>{new StampFieldZone("Title",65,15,70,25),new StampFieldZone("Code",65,40,120,15),new StampFieldZone("SheetNumber",145,5,20,10),new StampFieldZone("SheetCount",165,5,20,10),new StampFieldZone("Revision",0,35,10,10)};
        public void Validate(){if(Zones==null||Zones.Any(z=>z==null||string.IsNullOrWhiteSpace(z.Field)||!Numeric.NonNegative(z.X)||!Numeric.NonNegative(z.Y)||!Numeric.Positive(z.Width)||!Numeric.Positive(z.Height)))throw new ArgumentException("Проверьте зоны чтения штампа.");}
    }
    public sealed class DrawingText
    {
        public string DocumentKey {get;set;}="";
        public string Layout {get;set;}="";
        public string Path {get;set;}="";
        public Point2 Center {get;set;}
        public double Elevation {get;set;}
        public string Text {get;set;}="";
    }
    public static class StampMetadata
    {
        private static readonly HashSet<string> captions=new HashSet<string>(new[]{"Лист","Листов","Стадия","Масштаб","Изм.","Изм","№док.","Подп.","Дата","Наименование","Обозначение"},StringComparer.OrdinalIgnoreCase);
        public static string Clean(string text)
        {
            text=(text??"").Replace("\\P","\n").Replace("\\~"," ").Replace("%%d","°").Replace("%%p","±");
            text=Regex.Replace(text,@"\\[ACFfHQTW][^;]*;","");text=Regex.Replace(text,@"\\[LlOoKk]","");
            text=Regex.Replace(text,@"\\S([^;]*);",m=>m.Groups[1].Value.Replace('^','/').Replace('#','/'));return text.Replace("{","").Replace("}","").Trim();
        }
        public static void Apply(IEnumerable<DetectedFrame> frames,IEnumerable<DrawingText> source,StampMetadataSettings settings)
        {
            if(!settings.Enabled)return;settings.Validate();var spaces=source.Where(t=>t.Center.IsFinite&&!string.IsNullOrWhiteSpace(t.Text)).ToLookup(t=>t.DocumentKey+"|"+t.Layout,StringComparer.Ordinal);
            foreach(var frame in frames)
            {
                // The header's directed endpoints provide rotation/mirroring when scale evidence exists.
                Point2 u,v,origin;var evidence=frame.StampEvidence;
                if(evidence!=null)
                {
                    u=(evidence.HeaderEnd-evidence.HeaderStart)*(1/(evidence.Settings.ReferenceWidthMm*frame.Scale));
                    v=new Point2(-u.Y,u.X);var mid=(evidence.HeaderStart+evidence.HeaderEnd)*.5;
                    if(Point2.Dot(frame.Geometry.Center-mid,v)>0)v=v*-1;
                    origin=evidence.HeaderStart+v*(evidence.Settings.HeightMm*frame.Scale);v=v*-1;
                }
                else
                {
                    u=(frame.Geometry.Corners[1]-frame.Geometry.Corners[0])*(1/frame.Geometry.Width);v=new Point2(-u.Y,u.X);
                    origin=frame.Geometry.Corners[0]+u*(frame.Geometry.Width-190*frame.Scale)+v*(5*frame.Scale);
                }
                var texts=spaces[frame.Source.DocumentKey+"|"+frame.Layout].Where(t=>Math.Abs(t.Elevation-frame.Source.Elevation)<.02).Select(t=>new{Item=t,X=Point2.Dot(t.Center-origin,u)/frame.Scale,Y=Point2.Dot(t.Center-origin,v)/frame.Scale}).Where(t=>t.X>=-.2&&t.X<=185.2&&t.Y>=-.2&&t.Y<=55.2).ToArray();
                foreach(var zone in settings.Zones)
                {
                    var matching=texts.Where(t=>t.X>=zone.X&&t.X<zone.X+zone.Width&&t.Y>=zone.Y&&t.Y<zone.Y+zone.Height).OrderByDescending(t=>Math.Round(t.Y/1.5)).ThenBy(t=>t.X).ToArray();
                    string value=string.Join(" ",matching.Select(t=>Clean(t.Item.Text)).Where(t=>t.Length>0&&!captions.Contains(t)).Distinct()).Trim();
                    if(value.Length==0)continue;
                    string priority=zone.Field=="Title"?frame.Title:zone.Field=="Code"?frame.Code:zone.Field=="SheetNumber"?frame.SheetNumber:zone.Field=="Revision"?frame.Revision:"";
                    if(priority.Length>0)frame.Metadata[zone.Field]=priority;
                    else if(!frame.Metadata.TryGetValue(zone.Field,out var existing)||string.IsNullOrWhiteSpace(existing))frame.Metadata[zone.Field]=value;
                    if(zone.Field=="Title"&&string.IsNullOrWhiteSpace(frame.Title))frame.Title=value;
                    if(zone.Field=="Code"&&string.IsNullOrWhiteSpace(frame.Code))frame.Code=value;
                    if(zone.Field=="SheetNumber"&&string.IsNullOrWhiteSpace(frame.SheetNumber))frame.SheetNumber=value;
                    if(zone.Field=="Revision"&&string.IsNullOrWhiteSpace(frame.Revision))frame.Revision=value;
                }
                frame.OriginalRevision=frame.Revision;frame.OriginalTitle=frame.Title;frame.OriginalCode=frame.Code;frame.OriginalSheetNumber=frame.SheetNumber;
            }
        }
    }
}
