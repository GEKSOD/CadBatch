using System;
using System.Runtime.Serialization;

namespace CadBatch.Core
{
    [DataContract]
    public enum PrintColorMode
    {
        [EnumMember] DrawingColors=0,
        [EnumMember] FromDrawing=1,
        [EnumMember] Monochrome=2,
        [EnumMember] CustomStyle=3
    }
    public sealed class PrintStyleChoice
    {
        public bool? ApplyPlotStyles {get;set;}
        public string? StyleSheet {get;set;}
    }
    public static class PrintStylePolicy
    {
        public static PrintStyleChoice Resolve(PrintColorMode mode,string custom,bool colorDependent)
        {
            switch(mode)
            {
                case PrintColorMode.DrawingColors:return new PrintStyleChoice{ApplyPlotStyles=false};
                case PrintColorMode.FromDrawing:return new PrintStyleChoice();
                case PrintColorMode.Monochrome:return new PrintStyleChoice{ApplyPlotStyles=true,StyleSheet=colorDependent?"monochrome.ctb":"monochrome.stb"};
                case PrintColorMode.CustomStyle:
                    if(string.IsNullOrWhiteSpace(custom))throw new ArgumentException("Выберите CTB/STB для режима «Указанный стиль».");
                    return new PrintStyleChoice{ApplyPlotStyles=true,StyleSheet=custom.Trim()};
                default:throw new ArgumentException("Неизвестный режим цвета.");
            }
        }
        public static string Describe(PrintColorMode mode)=>mode==PrintColorMode.DrawingColors?"Цвета чертежа":
            mode==PrintColorMode.FromDrawing?"Стиль из DWG":mode==PrintColorMode.Monochrome?"Монохромный":"Указанный стиль";
    }
    public readonly struct PdfCropBounds
    {
        public PdfCropBounds(double left,double bottom,double right,double top){Left=left;Bottom=bottom;Right=right;Top=top;}
        public double Left{get;}public double Bottom{get;}public double Right{get;}public double Top{get;}
        public double Width=>Right-Left;public double Height=>Top-Bottom;
        public static PdfCropBounds Centered(double left,double bottom,double right,double top,int rotation,double targetWidthMm,double targetHeightMm)
        {
            if(!Numeric.Positive(targetWidthMm)||!Numeric.Positive(targetHeightMm)||!Numeric.Positive(right-left)||!Numeric.Positive(top-bottom))throw new ArgumentException("Некорректная область PDF.");
            if(rotation%90!=0)throw new ArgumentException("Некорректный поворот страницы PDF.");
            bool quarter=Math.Abs(rotation/90)%2==1;
            double w=(quarter?targetHeightMm:targetWidthMm)*72/25.4,h=(quarter?targetWidthMm:targetHeightMm)*72/25.4;
            // AutoCAD writes integer-point MediaBox dimensions. Permit only its sub-mm rounding,
            // never a genuinely smaller carrier (for example A3 instead of an extended sheet).
            const double roundingPoints=0.5*72/25.4;
            if(w>right-left+roundingPoints || h>top-bottom+roundingPoints)throw new InvalidOperationException("Промежуточная страница меньше целевого формата.");
            double x=(left+right-w)/2,y=(bottom+top-h)/2;
            return new PdfCropBounds(x,y,x+w,y+h);
        }
    }
}
