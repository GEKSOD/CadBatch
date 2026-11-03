using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CadBatch.Core;

namespace CadBatch.Transport
{
    [DataContract] public sealed class WireItem
    {
        [DataMember] public WireFrame Frame {get;set;}=new WireFrame();
        [DataMember] public string Path {get;set;}="";
        public static WireItem From(OutputItem i)=>new WireItem{Frame=WireFrame.From(i.Frame),Path=i.Path};
        public OutputItem ToItem()=>new OutputItem{Frame=Frame.ToFrame(),Path=Path};
    }
    [DataContract] public sealed class WireGroup
    {
        [DataMember] public string Path {get;set;}="";
        [DataMember] public int[] Items {get;set;}=Array.Empty<int>();
    }
    [DataContract] public sealed class WireExport
    {
        [DataMember] public List<WireItem> Items {get;set;}=new List<WireItem>();
        [DataMember] public List<WireGroup> Groups {get;set;}=new List<WireGroup>();
        [DataMember] public string Device {get;set;}="";
        [DataMember] public string Style {get;set;}="";
        [DataMember] public bool Fit {get;set;}
        [DataMember] public PrintColorMode Color {get;set;}
        [DataMember] public PdfOutputMode Mode {get;set;}
        [DataMember] public bool Physical {get;set;}
        public static WireExport From(ExportRequest r)=>new WireExport{Items=r.Items.Select(WireItem.From).ToList(),Groups=r.Groups.Select(g=>new WireGroup{Path=g.Path,Items=g.Items.Select(i=>r.Items.IndexOf(i)).ToArray()}).ToList(),Device=r.Device,Style=r.StyleSheet,Fit=r.FitToPaper,Color=r.ColorMode,Mode=r.PdfMode,Physical=r.PhysicalPrint};
        public ExportRequest ToRequest()
        {
            var r=new ExportRequest{Items=Items.Select(i=>i.ToItem()).ToList(),Device=Device,StyleSheet=Style,FitToPaper=Fit,ColorMode=Color,PdfMode=Mode,PhysicalPrint=Physical};
            foreach(var group in Groups){if(group.Items.Any(i=>i<0||i>=r.Items.Count))throw new ArgumentException("Повреждены группы PDF.");r.Groups.Add(new PdfGroup{Path=group.Path,Items=group.Items.Select(i=>r.Items[i]).ToList()});}return r;
        }
    }
    [DataContract] public sealed class WireReport
    {
        [DataMember] public List<string> Files {get;set;}=new List<string>();
        [DataMember] public List<string> Errors {get;set;}=new List<string>();
        [DataMember] public List<string> Details {get;set;}=new List<string>();
        [DataMember] public bool Cancelled {get;set;}
        [DataMember] public bool Physical {get;set;}
        [DataMember] public int Queued {get;set;}
        [DataMember] public string[] Statuses {get;set;}=Array.Empty<string>();
        public static WireReport From(ExportReport r,ExportRequest request)=>new WireReport{Files=r.Files,Errors=r.Errors,Details=r.Details,Cancelled=r.Cancelled,Physical=r.PhysicalPrint,Queued=r.Queued,Statuses=request.Items.Select(i=>i.Frame.Status).ToArray()};
        public ExportReport ToReport(){var r=new ExportReport{Cancelled=Cancelled,PhysicalPrint=Physical,Queued=Queued};r.Files.AddRange(Files);r.Errors.AddRange(Errors);r.Details.AddRange(Details);return r;}
    }
    [DataContract] public sealed class WireOperation
    {
        [DataMember] public CadOperation Operation {get;set;}
        [DataMember] public List<WireFrame> Frames {get;set;}=new List<WireFrame>();
        [DataMember] public List<WireItem> Items {get;set;}=new List<WireItem>();
        [DataMember] public string Folder {get;set;}="";
        [DataMember] public string Device {get;set;}="";
        [DataMember] public string NamePattern {get;set;}="";
        [DataMember] public bool Crossing {get;set;}
        [DataMember] public string NumberTag {get;set;}="";
        [DataMember] public string TotalTag {get;set;}="";
        [DataMember] public string TitleTag {get;set;}="";
        [DataMember] public string CodeTag {get;set;}="";
        [DataMember] public bool WriteTitles {get;set;}
        [DataMember] public bool WriteTotals {get;set;}
        [DataMember] public bool ApplyChanges {get;set;}
        [DataMember] public FormatDefinition Format {get;set;}=new FormatDefinition();
        [DataMember] public double Scale {get;set;}
        [DataMember] public string[] Files {get;set;}=Array.Empty<string>();
        [DataMember] public int Columns {get;set;}
        [DataMember] public double Spacing {get;set;}
        public static WireOperation From(CadOperationRequest r)=>new WireOperation{Operation=r.Operation,Frames=r.Frames.Select(WireFrame.From).ToList(),Items=r.Items.Select(WireItem.From).ToList(),Folder=r.Folder,Device=r.Device,NamePattern=r.NamePattern,Crossing=r.Crossing,NumberTag=r.NumberTag,TotalTag=r.TotalTag,TitleTag=r.TitleTag,CodeTag=r.CodeTag,WriteTitles=r.WriteTitles,WriteTotals=r.WriteTotals,ApplyChanges=r.ApplyChanges,Format=r.Format,Scale=r.Scale,Files=r.Files,Columns=r.Columns,Spacing=r.Spacing};
        public CadOperationRequest ToRequest()
        {
            var frames=Frames.Select(f=>f.ToFrame()).ToList();
            var byKey=frames.ToLookup(f=>f.Source.Key);var items=Items.Select(i=>{var f=i.Frame.ToFrame();return new OutputItem{Path=i.Path,Frame=byKey[f.Source.Key].FirstOrDefault()??f};}).ToList();
            return new CadOperationRequest{Operation=Operation,Frames=frames,Items=items,Folder=Folder,Device=Device,NamePattern=NamePattern,Crossing=Crossing,NumberTag=NumberTag,TotalTag=TotalTag,TitleTag=TitleTag,CodeTag=CodeTag,WriteTitles=WriteTitles,WriteTotals=WriteTotals,ApplyChanges=ApplyChanges,Format=Format,Scale=Scale,Files=Files,Columns=Columns,Spacing=Spacing};
        }
    }
    [DataContract] public sealed class WireOperationResult
    {
        [DataMember] public int Changed {get;set;}
        [DataMember] public List<string> Files {get;set;}=new List<string>();
        [DataMember] public List<string> Errors {get;set;}=new List<string>();
        [DataMember] public List<string> Details {get;set;}=new List<string>();
        [DataMember] public bool Cancelled {get;set;}
        [DataMember] public List<WireFrame> Frames {get;set;}=new List<WireFrame>();
        public static WireOperationResult From(CadOperationResult r,CadOperationRequest request)=>new WireOperationResult{Changed=r.Changed,Files=r.Files,Errors=r.Errors,Details=r.Details,Cancelled=r.Cancelled,Frames=request.Frames.Select(WireFrame.From).ToList()};
        public CadOperationResult ToResult(){var r=new CadOperationResult{Changed=Changed,Cancelled=Cancelled};r.Files.AddRange(Files);r.Errors.AddRange(Errors);r.Details.AddRange(Details);return r;}
    }
}
