using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

namespace CadBatch.Core
{
    [DataContract]
    public sealed class FormatDefinition
    {
        [DataMember] public string Name { get; set; } = "";
        [DataMember] public double WidthMm { get; set; }
        [DataMember] public double HeightMm { get; set; }
        [DataMember(EmitDefaultValue=false)] public FormatPrintSettings? Print {get;set;}
        public FormatDefinition() { }
        public FormatDefinition(string name, double width, double height) { Name = name; WidthMm = width; HeightMm = height; }
        public static List<FormatDefinition> Defaults() => new List<FormatDefinition>
        {
            new FormatDefinition("А4",297,210), new FormatDefinition("А3",420,297),
            new FormatDefinition("А2",594,420), new FormatDefinition("А1",841,594), new FormatDefinition("А0",1189,841),
            new FormatDefinition("А4×3",630,297),new FormatDefinition("А4×4",841,297),new FormatDefinition("А3×3",891,420),
            new FormatDefinition("А3×4",1189,420),new FormatDefinition("А3×5",1486,420),new FormatDefinition("А3×6",1783,420),new FormatDefinition("А3×7",2080,420),
            new FormatDefinition("А4×5",1051,297),new FormatDefinition("А4×6",1261,297),new FormatDefinition("А4×7",1471,297),new FormatDefinition("А4×8",1682,297),new FormatDefinition("А4×9",1892,297),
            new FormatDefinition("А2×3",1261,594),new FormatDefinition("А2×4",1682,594),new FormatDefinition("А2×5",2102,594),
            new FormatDefinition("А1×3",1783,841),new FormatDefinition("А1×4",2378,841),new FormatDefinition("А0×2",1682,1189),new FormatDefinition("А0×3",2523,1189)
        };
    }

    [DataContract]
    public sealed class SearchOptions
    {
        [DataMember(EmitDefaultValue=false)] public List<string>? Files {get;set;}
        [DataMember(EmitDefaultValue=false)] public SearchRegion? Region {get;set;}
        [DataMember] public bool Model { get; set; } = true;
        [DataMember] public bool Layouts { get; set; }
        [DataMember] public bool AllOpenDocuments { get; set; }
        [DataMember] public bool Polylines { get; set; } = true;
        [DataMember] public bool Blocks { get; set; } = true;
        [DataMember] public bool Lines { get; set; }
        [DataMember] public bool ExternalReferences { get; set; }
        [DataMember] public bool SpecialEntities {get;set;}
        [DataMember] public bool Viewports { get; set; }
        [DataMember] public bool ActiveLayoutOnly { get; set; }
        [DataMember] public string LayerPattern { get; set; } = "*";
        [DataMember] public string BlockPattern { get; set; } = "*";
        [DataMember] public List<double> Scales { get; set; } = new List<double> { 1 };
        [DataMember] public double SizeToleranceMm { get; set; } = 2;
        [DataMember] public double GeometryTolerance { get; set; } = 0.01;
        [DataMember(EmitDefaultValue=false)] public StampScaleSettings? Stamp {get;set;}
        public void Validate()
        {
            if (!Model && !Layouts) throw new ArgumentException("Выберите модель или листы.");
            if (!Polylines && !Blocks && !Lines && !Viewports) throw new ArgumentException("Выберите хотя бы один тип источника.");
            if (Scales == null || (Scales.Count == 0 && Stamp?.Enabled!=true) || Scales.Any(x => !Numeric.Positive(x))) throw new ArgumentException("Масштабы должны быть положительными числами.");
            if (!Numeric.NonNegative(SizeToleranceMm) || !Numeric.Positive(GeometryTolerance)) throw new ArgumentException("Некорректный допуск.");
            Stamp?.Validate();Region?.Validate();
        }
    }

    public static class Numeric
    {
        public static bool Positive(double n) => n > 0 && !double.IsNaN(n) && !double.IsInfinity(n);
        public static bool NonNegative(double n) => n >= 0 && !double.IsNaN(n) && !double.IsInfinity(n);
    }

    public sealed class FrameCandidate
    {
        public List<string> Dependencies {get;set;}=new List<string>();
        public bool DrawingModified {get;set;}
        public string DocumentKey { get; set; } = "";
        public string DocumentName { get; set; } = "";
        public string DocumentPath { get; set; } = "";
        public string Layout { get; set; } = "Model";
        public string HandlePath { get; set; } = "";
        public string Layer { get; set; } = "";
        public string BlockName { get; set; } = "";
        public string Kind { get; set; } = "Полилиния";
        public string GeometryKind { get; set; } = "Polyline";
        public List<string> MemberPaths { get; set; } = new List<string>();
        public bool IsExternal { get; set; }
        public double Elevation {get;set;}
        public List<Point2> Vertices { get; set; } = new List<Point2>();
        public Dictionary<string,string> Attributes { get; set; } = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public string Key => DocumentKey + "|" + Layout + "|" + HandlePath;
    }

    public sealed class DetectedFrame
    {
        public FrameCandidate Source { get; set; } = new FrameCandidate();
        public Rectangle2 Geometry { get; set; } = null!;
        public FormatDefinition Format { get; set; } = new FormatDefinition();
        public bool Enabled { get; set; } = true;
        public int Copies { get; set; } = 1;
        public string Device { get; set; } = "";
        public string StyleSheet { get; set; } = "";
        public bool Available { get; set; } = true;
        public double Scale { get; set; }
        public StampScaleEvidence? StampEvidence {get;set;}
        public string ScaleSource=>StampEvidence==null?"Список масштабов":"Штамп: "+StampEvidence.Settings.ReferenceWidthMm.ToString(System.Globalization.CultureInfo.CurrentCulture)+" мм";
        public int Page { get; set; }
        public string SheetNumber { get; set; } = "";
        public string Title { get; set; } = "";
        public string Code { get; set; } = "";
        public Dictionary<string,string> Metadata {get;set;}=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        public string Revision {get;set;}="";
        public string OriginalTitle {get;set;}="";
        public string OriginalRevision {get;set;}="";
        public string OriginalCode {get;set;}="";
        public string OriginalSheetNumber {get;set;}="";
        public string Status { get; set; } = "Готово к проверке";
        public string File => Source.DocumentName;
        public string Layout => Source.Layout;
        public string FormatName => Format.Name;
        public string Layer => Source.Layer;
        public string Block => Source.BlockName;
        public string Kind => Source.Kind;
        public double WidthMm => Math.Round(Geometry.Width / Scale, 2);
        public double HeightMm => Math.Round(Geometry.Height / Scale, 2);
        public double PaperWidthMm => PaperSize().X;
        public double PaperHeightMm => PaperSize().Y;
        private Point2 PaperSize()
        {
            double width=Geometry.Width/Scale,height=Geometry.Height/Scale;
            bool swap=Math.Abs(width-Format.WidthMm)+Math.Abs(height-Format.HeightMm)>
                Math.Abs(width-Format.HeightMm)+Math.Abs(height-Format.WidthMm);
            return swap?new Point2(Format.HeightMm,Format.WidthMm):new Point2(Format.WidthMm,Format.HeightMm);
        }
        public string SizeText => PaperWidthMm.ToString("0.##")+" × "+PaperHeightMm.ToString("0.##");
        public double AngleDegrees => Math.Round(Geometry.Angle * 180 / Math.PI, 2);
    }

    public sealed class ScanResult
    {
        public List<DetectedFrame> Frames { get; } = new List<DetectedFrame>();
        public List<string> Warnings { get; } = new List<string>();
        public int ObjectsVisited { get; set; }
        public int Duplicates { get; set; }
    }

    [DataContract]
    public sealed class ProjectProfile
    {
        [DataMember] public int SchemaVersion { get; set; } = 1;
        [DataMember] public SearchOptions Search { get; set; } = new SearchOptions();
        [DataMember] public List<FormatDefinition> Formats { get; set; } = FormatDefinition.Defaults();
        [DataMember] public string Device { get; set; } = "DWG To PDF.pc3";
        [DataMember] public string StyleSheet { get; set; } = "";
        [DataMember] public string OutputFolder { get; set; } = "";
        [DataMember] public string FilePattern { get; set; } = "{Document}_{Page}_{Format}";
        [DataMember] public PrintColorMode ColorMode { get; set; } = PrintColorMode.DrawingColors;
        [DataMember] public int CatalogVersion { get; set; } = 2;
        [DataMember] public PdfOutputMode PdfMode { get; set; }
        [DataMember] public string CombinedPdfName { get; set; } = "Комплект";
        [DataMember(EmitDefaultValue=false)] public bool? FitToPaper {get;set;}
        [DataMember(EmitDefaultValue=false)] public WindowSettings? Window {get;set;}
        [DataMember(EmitDefaultValue=false)] public StampMetadataSettings? Metadata {get;set;}=new StampMetadataSettings();
        public void UpgradeCatalog()
        {
            if(CatalogVersion>=2)return;
            if(Formats==null)Formats=new List<FormatDefinition>();
            var additions=CatalogVersion<1?FormatDefinition.Defaults():new List<FormatDefinition>{new FormatDefinition("А4×9",1892,297)};
            foreach(var format in additions)
                if(!Formats.Any(f=>string.Equals(f.Name,format.Name,StringComparison.OrdinalIgnoreCase)))Formats.Add(format);
            CatalogVersion=2;
        }
        public void Validate()
        {
            if (SchemaVersion != 1) throw new ArgumentException("Неподдерживаемая версия профиля.");
            if (Search == null || Formats == null || Formats.Count == 0) throw new ArgumentException("Профиль не содержит настроек поиска/форматов.");
            Search.Validate();Metadata?.Validate();
            if(!Enum.IsDefined(typeof(PrintColorMode),ColorMode))throw new ArgumentException("Некорректный режим цвета.");
            if(!Enum.IsDefined(typeof(PdfOutputMode),PdfMode))throw new ArgumentException("Некорректный режим PDF.");
            foreach(var f in Formats)f?.Print?.Validate();
            if (Formats.Any(f => f == null || string.IsNullOrWhiteSpace(f.Name) || !Numeric.Positive(f.WidthMm) || !Numeric.Positive(f.HeightMm)))
                throw new ArgumentException("Форматы должны иметь имя и положительные размеры.");
        }
    }
}
