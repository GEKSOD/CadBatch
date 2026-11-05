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
        // Main title block of ГОСТ Р 21.101 (form 3), millimetres from its bottom-left corner: the sheet title is the bottom
        // cell (the cell above names the building and is the same on every sheet); "Стадия / Лист / Листов" values sit
        // 15–25 mm high under their captions.
        public static List<StampFieldZone> SpdsZones()=>new List<StampFieldZone>{new StampFieldZone("Title",65,0,70,15),new StampFieldZone("Object",65,15,70,15),new StampFieldZone("Code",65,45,120,10),
            new StampFieldZone("Stage",135,15,15,10),new StampFieldZone("SheetNumber",150,15,15,10),new StampFieldZone("SheetCount",165,15,20,10),new StampFieldZone("Revision",0,35,10,10)};
        // Zones of versions up to 0.8.4: the title was read from the building cell and the sheet number at 20–30 mm.
        private static List<StampFieldZone> OldSpdsZones()=>new List<StampFieldZone>{new StampFieldZone("Title",65,15,70,15),new StampFieldZone("Code",65,45,120,10),new StampFieldZone("SheetNumber",150,20,15,10),new StampFieldZone("SheetCount",165,20,20,10),new StampFieldZone("Revision",0,35,10,10)};
        // Title block of the following sheets (ГОСТ Р 21.101 form 6, 185 × 15): the code in the middle, the sheet number
        // in the right cell under its caption. Used automatically with the SPDS zones when the block is recognised.
        public static List<StampFieldZone> SpdsFollowingZones()=>new List<StampFieldZone>{new StampFieldZone("Code",65,0,110,15),new StampFieldZone("SheetNumber",175,0,10,8)};
        public static bool SameZones(IReadOnlyList<StampFieldZone>? a,IReadOnlyList<StampFieldZone> b)=>a!=null&&a.Count==b.Count&&a.Zip(b,(x,y)=>x.Field==y.Field&&x.X==y.X&&x.Y==y.Y&&x.Width==y.Width&&x.Height==y.Height).All(same=>same);
        public static List<StampFieldZone> EskdZones()=>new List<StampFieldZone>{new StampFieldZone("Title",65,15,70,25),new StampFieldZone("Code",65,40,120,15),new StampFieldZone("SheetNumber",145,5,20,10),new StampFieldZone("SheetCount",165,5,20,10),new StampFieldZone("Revision",0,35,10,10)};
        public void Validate(){if(Zones==null||Zones.Any(z=>z==null||string.IsNullOrWhiteSpace(z.Field)||!Numeric.NonNegative(z.X)||!Numeric.NonNegative(z.Y)||!Numeric.Positive(z.Width)||!Numeric.Positive(z.Height)))throw new ArgumentException("Проверьте зоны чтения штампа.");}
        // Profiles saved with the old default SPDS zones get the corrected ones; zones the user edited are kept.
        public bool Upgrade()
        {
            if(!SameZones(Zones,OldSpdsZones()))return false;
            Zones=SpdsZones();return true;
        }
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
        private static readonly HashSet<string> captions=new HashSet<string>(new[]{"Лист","Листов","Стадия","Масштаб","Изм.","Изм","№док.","№ док.","Подп.","Дата","Наименование","Обозначение","Кол. уч.","Кол.уч.","Разработал","Разраб.","Проверил","Пров.","ГИП","Н. контр.","Н.контр.","Утв.","Т. контр.","Подпись","Nдок.","N док.","Кол.уч","Формат"},StringComparer.OrdinalIgnoreCase);
        // Fields that hold one short value: the text nearest to the cell centre wins, a little outside the cell allowed.
        private static readonly HashSet<string> single=new HashSet<string>(new[]{"SheetNumber","SheetCount","Stage","Revision"},StringComparer.Ordinal);
        private const double Slack=1.5;
        public static string Clean(string text)
        {
            text=(text??"").Replace("\\P","\n").Replace("\\~"," ").Replace("%%d","°").Replace("%%p","±").Replace("%%c","⌀").Replace("%%u","").Replace("%%o","");
            text=Regex.Replace(text,@"\\[ACFfHQTWp][^;]*;","");text=Regex.Replace(text,@"\\[LlOoKk]","");
            text=Regex.Replace(text,@"\\S([^;]*);",m=>m.Groups[1].Value.Replace('^','/').Replace('#','/'));
            text=text.Replace("{","").Replace("}","");
            return Regex.Replace(text,@"[ \t]{2,}"," ").Trim();
        }
        // Pieces of one cell in reading order; no space before closing punctuation or after a hyphen ("ВК" + ".СО" = "ВК.СО").
        public static string Join(IEnumerable<string> pieces)
        {
            var result="";
            foreach(var piece in pieces)
            {
                if(result.Length==0){result=piece;continue;}
                bool tight=".,;:)»".IndexOf(piece[0])>=0||"-–(«/".IndexOf(result[result.Length-1])>=0||(piece.Length>1&&"-–".IndexOf(piece[0])>=0&&!char.IsWhiteSpace(piece[1]));
                result+=(tight?"":" ")+piece;
            }
            return result.Trim();
        }
        private static bool LooksLikeValue(string field,string text)=>field=="SheetNumber"||field=="SheetCount"?Regex.IsMatch(text,@"^\s*[\dIVXLC]+[\w.\-/]*\s*$"):text.Length<=12;
        public static void Apply(IEnumerable<DetectedFrame> frames,IEnumerable<DrawingText> source,StampMetadataSettings settings)
        {
            if(!settings.Enabled)return;settings.Validate();bool spds=StampMetadataSettings.SameZones(settings.Zones,StampMetadataSettings.SpdsZones());
            var following=new List<DetectedFrame>();var mainTitles=new Dictionary<string,string>(StringComparer.Ordinal);
            var spaces=source.Where(t=>t.Center.IsFinite&&!string.IsNullOrWhiteSpace(t.Text)).ToLookup(t=>t.DocumentKey+"|"+t.Layout,StringComparer.Ordinal);
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
                var all=spaces[frame.Source.DocumentKey+"|"+frame.Layout].Where(t=>Math.Abs(t.Elevation-frame.Source.Elevation)<.02)
                    .Select(t=>new{Item=t,Text=Clean(t.Text),X=Point2.Dot(t.Center-origin,u)/frame.Scale,Y=Point2.Dot(t.Center-origin,v)/frame.Scale})
                    .Where(t=>t.X>=-2&&t.X<=187&&t.Y>=-2&&t.Y<=57&&t.Text.Length>0).ToArray();
                // Form 6 has the captions "Изм., Лист, Дата…" in its bottom row; the main form has them 30–35 mm high.
                bool isFollowing=spds&&all.Count(t=>captions.Contains(t.Text)&&t.X<65&&t.Y<6)>=2&&!all.Any(t=>captions.Contains(t.Text)&&t.X<65&&t.Y>28&&t.Y<37);
                var zones=isFollowing?StampMetadataSettings.SpdsFollowingZones():settings.Zones;
                var texts=all.Where(t=>!captions.Contains(t.Text)&&(!isFollowing||t.Y<15.5)).ToArray();
                var read=new Dictionary<string,string>(StringComparer.Ordinal);
                foreach(var zone in zones)
                {
                    string value;
                    if(single.Contains(zone.Field))
                    {
                        double cx=zone.X+zone.Width/2,cy=zone.Y+zone.Height/2;
                        value=texts.Where(t=>t.X>=zone.X-Slack&&t.X<zone.X+zone.Width+Slack&&t.Y>=zone.Y-Slack&&t.Y<zone.Y+zone.Height+Slack&&LooksLikeValue(zone.Field,t.Text))
                            .OrderBy(t=>Math.Abs(t.X-cx)/zone.Width+Math.Abs(t.Y-cy)/zone.Height).Select(t=>t.Text.Trim()).FirstOrDefault()??"";
                    }
                    else
                    {
                        var matching=texts.Where(t=>t.X>=zone.X&&t.X<zone.X+zone.Width&&t.Y>=zone.Y&&t.Y<zone.Y+zone.Height).OrderByDescending(t=>Math.Round(t.Y/1.5)).ThenBy(t=>t.X).ToArray();
                        value=Join(matching.Select(t=>t.Text.Replace('\n',' ')).Distinct());
                    }
                    if(value.Length>0&&!read.ContainsKey(zone.Field))read[zone.Field]=value;
                }
                // A title block without a sheet title cell filled: the cell above (building) is better than nothing.
                if(!read.ContainsKey("Title")&&read.TryGetValue("Object",out var building))read["Title"]=building;
                foreach(var pair in read)
                {
                    string field=pair.Key,value=pair.Value;
                    string priority=field=="Title"?frame.Title:field=="Code"?frame.Code:field=="SheetNumber"?frame.SheetNumber:field=="Revision"?frame.Revision:"";
                    if(priority.Length>0)frame.Metadata[field]=priority;
                    else if(!frame.Metadata.TryGetValue(field,out var existing)||string.IsNullOrWhiteSpace(existing))frame.Metadata[field]=value;
                    if(field=="Title"&&string.IsNullOrWhiteSpace(frame.Title))frame.Title=value;
                    if(field=="Code"&&string.IsNullOrWhiteSpace(frame.Code))frame.Code=value;
                    if(field=="SheetNumber"&&string.IsNullOrWhiteSpace(frame.SheetNumber))frame.SheetNumber=value;
                    if(field=="Revision"&&string.IsNullOrWhiteSpace(frame.Revision))frame.Revision=value;
                }
                if(isFollowing)following.Add(frame);
                else if(frame.Code.Length>0&&frame.Title.Length>0&&!mainTitles.ContainsKey(frame.Source.DocumentKey+"|"+frame.Code))mainTitles[frame.Source.DocumentKey+"|"+frame.Code]=frame.Title;
                frame.OriginalRevision=frame.Revision;frame.OriginalTitle=frame.Title;frame.OriginalCode=frame.Code;frame.OriginalSheetNumber=frame.SheetNumber;
            }
            // Following sheets carry no title: they continue the document whose first sheet has the same code.
            foreach(var frame in following)
                if(frame.Title.Length==0&&frame.Code.Length>0&&mainTitles.TryGetValue(frame.Source.DocumentKey+"|"+frame.Code,out var title)){frame.Title=frame.OriginalTitle=title;if(!frame.Metadata.ContainsKey("Title"))frame.Metadata["Title"]=title;}
        }
    }
}
