using System;
using System.Collections.Generic;
using System.Linq;

namespace CadBatch.Core
{
    // One paper of a PC3 device: its canonical name, size without rotation and the sum of its margins, in millimetres.
    public sealed class MediaEntry
    {
        public string Name {get;set;}="";
        public double Width {get;set;}
        public double Height {get;set;}
        public double Margins {get;set;}
    }
    public sealed class MediaPick
    {
        public string Name="";public bool Rotated;public double Score;public double Margins;
        public double Width;public double Height;public bool IsCarrier;public double ScaleFactor=1;
    }
    // Chooses the paper for a sheet. Reading the sizes of every paper of a PC3 takes about a second, the choice itself
    // nothing; so the AutoCAD component reads the catalogue once per device and session and decides here.
    public static class MediaPicker
    {
        public static MediaPick Pick(IReadOnlyList<MediaEntry> entries,double width,double height,bool fit,string? preferred=null)
        {
            MediaPick? best=null,carrier=null,smallCarrier=null;
            foreach(var entry in entries.Where(e=>string.IsNullOrWhiteSpace(preferred)||string.Equals(e.Name,preferred,StringComparison.OrdinalIgnoreCase)))
            {
                foreach(bool rotated in new[]{false,true})
                {
                    double w=rotated?entry.Height:entry.Width,h=rotated?entry.Width:entry.Height;
                    double error=Math.Abs(w-width)+Math.Abs(h-height);
                    double carrierScale=Math.Min(1,Math.Min(w/width,h/height));
                    if(entry.Margins<=.05&&carrierScale>0&&(smallCarrier==null||carrierScale>smallCarrier.ScaleFactor))smallCarrier=new MediaPick{Name=entry.Name,Rotated=rotated,Margins=entry.Margins,Width=w,Height=h,IsCarrier=true,ScaleFactor=carrierScale};
                    if(Math.Abs(w-width)<=1&&Math.Abs(h-height)<=1)
                    {
                        double score=error+entry.Margins;
                        if(best==null||score<best.Score)best=new MediaPick{Name=entry.Name,Rotated=rotated,Score=score,Margins=entry.Margins,Width=w,Height=h};
                    }
                    else if(w>=width-0.01&&h>=height-0.01&&entry.Margins<=0.05)
                    {
                        // Full-bleed carrier preserves a centered physical-size window; the PDF is trimmed afterwards.
                        double score=w*h;
                        if(carrier==null||score<carrier.Score)carrier=new MediaPick{Name=entry.Name,Rotated=rotated,Score=score,Margins=entry.Margins,Width=w,Height=h,IsCarrier=true};
                    }
                }
            }
            if(best==null)
            {
                best=carrier??smallCarrier??throw new InvalidOperationException("В PC3 нет бумаги "+width+" × "+height+" мм и полного носителя достаточного размера. Проверено: "+entries.Count+".");
            }
            if(!fit&&best.Margins>1)throw new InvalidOperationException("Носитель имеет поля. Включите «Вписать в бумагу» для первого теста.");
            return best;
        }
    }
    // Paper catalogues read in this session, by device. Forgotten when a plot with the device fails, so an edited PC3
    // is read again.
    public static class MediaCatalog
    {
        private static readonly Dictionary<string,List<MediaEntry>> cache=new Dictionary<string,List<MediaEntry>>(StringComparer.OrdinalIgnoreCase);
        private static readonly object sync=new object();
        public static List<MediaEntry> Get(string device,Func<List<MediaEntry>> read,out bool cached)
        {
            lock(sync){if(cache.TryGetValue(device,out var known)){cached=true;return known;}}
            var fresh=read();cached=false;
            lock(sync){if(fresh.Count>0)cache[device]=fresh;}
            return fresh;
        }
        public static void Forget(string device){lock(sync)cache.Remove(device);}
        public static void Clear(){lock(sync)cache.Clear();}
    }
}
