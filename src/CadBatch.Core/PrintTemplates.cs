using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace CadBatch.Core
{
    [DataContract] public sealed class FormatPrintSettings
    {
        [DataMember] public string Device {get;set;}="";
        [DataMember] public string Media {get;set;}="";
        [DataMember] public string StyleSheet {get;set;}="";
        [DataMember(EmitDefaultValue=false)] public PrintColorMode? Color {get;set;}
        [DataMember(EmitDefaultValue=false)] public bool? Fit {get;set;}
        [DataMember] public bool UpsideDown {get;set;}
        [DataMember] public double OffsetX {get;set;}
        [DataMember] public double OffsetY {get;set;}
        public FormatPrintSettings Copy()=>(FormatPrintSettings)MemberwiseClone();
        public void Validate(){if(!Numeric.NonNegative(Math.Abs(OffsetX))||!Numeric.NonNegative(Math.Abs(OffsetY))||Color.HasValue&&!Enum.IsDefined(typeof(PrintColorMode),Color.Value))throw new ArgumentException("Некорректные параметры печати формата.");}
    }
    public static class PrintTemplates
    {
        public static ExportRequest Resolve(ExportRequest request,DetectedFrame frame)
        {
            var format=frame.Format.Print??new FormatPrintSettings();format.Validate();
            return new ExportRequest{Device=First(frame.Device,format.Device,request.Device),StyleSheet=First(frame.StyleSheet,format.StyleSheet,request.StyleSheet),
                ColorMode=!string.IsNullOrWhiteSpace(frame.StyleSheet)?PrintColorMode.CustomStyle:format.Color??(!string.IsNullOrWhiteSpace(format.StyleSheet)?PrintColorMode.CustomStyle:request.ColorMode),
                FitToPaper=format.Fit??request.FitToPaper,PhysicalPrint=request.PhysicalPrint,Media=format.Media??"",UpsideDown=format.UpsideDown,OffsetX=format.OffsetX,OffsetY=format.OffsetY};
        }
        private static string First(params string[] values)=>values.FirstOrDefault(x=>!string.IsNullOrWhiteSpace(x))??"";
        public static string PathFor(string directory,string name)
        {
            if(string.IsNullOrWhiteSpace(name)||name.Trim()!=OutputPlanner.Sanitize(name.Trim())||name.Contains(".."))throw new ArgumentException("Укажите имя шаблона без символов пути.");
            return Path.Combine(Path.GetFullPath(directory),name.Trim()+".json");
        }
    }
}
