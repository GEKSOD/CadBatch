using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using CadBatch.Core;

namespace CadBatch.Transport
{
    [DataContract] public sealed class WireFrame
    {
        [DataMember] public JobRow Row {get;set;}=new JobRow();
        [DataMember] public string DocumentKey {get;set;}="";
        [DataMember] public string DocumentName {get;set;}="";
        [DataMember] public string Layer {get;set;}="";
        [DataMember] public string Block {get;set;}="";
        [DataMember] public string Kind {get;set;}="";
        [DataMember] public string GeometryKind {get;set;}="";
        [DataMember] public List<string> Members {get;set;}=new List<string>();
        [DataMember] public Dictionary<string,string> Attributes {get;set;}=new Dictionary<string,string>();
        [DataMember] public List<string> Dependencies {get;set;}=new List<string>();
        [DataMember] public double Elevation {get;set;}
        [DataMember] public bool External {get;set;}
        [DataMember] public bool Modified {get;set;}
        [DataMember] public bool Available {get;set;}
        [DataMember] public string Status {get;set;}="";
        [DataMember] public int Page {get;set;}
        [DataMember] public FormatDefinition Format {get;set;}=new FormatDefinition();
        [DataMember] public string[] Originals {get;set;}=Array.Empty<string>();
        [DataMember] public StampScaleSettings? StampSettings {get;set;}
        [DataMember] public double[]? Header {get;set;}
        [DataMember(EmitDefaultValue=false)] public int LayoutOrder {get;set;}
        public static WireFrame From(DetectedFrame f)=>new WireFrame
        {
            Row=JobStore.Capture(new ProjectProfile(),new[]{f}).Rows[0],DocumentKey=f.Source.DocumentKey,DocumentName=f.File,Layer=f.Layer,Block=f.Block,Kind=f.Kind,GeometryKind=f.Source.GeometryKind,
            Members=f.Source.MemberPaths,Attributes=f.Source.Attributes,Dependencies=f.Source.Dependencies,Elevation=f.Source.Elevation,External=f.Source.IsExternal,Modified=f.Source.DrawingModified,Available=f.Available,Status=f.Status,Page=f.Page,Format=f.Format,
            Originals=new[]{f.OriginalTitle,f.OriginalCode,f.OriginalSheetNumber,f.OriginalRevision},StampSettings=f.StampEvidence?.Settings,Header=f.StampEvidence==null?null:new[]{f.StampEvidence.HeaderStart.X,f.StampEvidence.HeaderStart.Y,f.StampEvidence.HeaderEnd.X,f.StampEvidence.HeaderEnd.Y},LayoutOrder=f.Source.LayoutOrder
        };
        public DetectedFrame ToFrame()
        {
            if(Row.Corners==null||Row.Corners.Any(p=>p==null||p.Length!=2)||!Rectangle2.TryCreate(Row.Corners.Select(p=>new Point2(p[0],p[1])),.01,out var rectangle)||!Row.Scale.HasValue||!Numeric.Positive(Row.Scale.Value))throw new ArgumentException("Повреждён контур переданной рамки.");
            var f=new DetectedFrame{Source=new FrameCandidate{DocumentKey=DocumentKey,DocumentName=DocumentName,DocumentPath=Row.DocumentPath,Layout=Row.Layout,HandlePath=Row.HandlePath,Layer=Layer,BlockName=Block,Kind=Kind,GeometryKind=GeometryKind,MemberPaths=Members,Attributes=new Dictionary<string,string>(Attributes,StringComparer.OrdinalIgnoreCase),Dependencies=Dependencies,Elevation=Elevation,IsExternal=External,DrawingModified=Modified,Vertices=rectangle!.Corners.ToList()},
                Geometry=rectangle!,Format=Format,Scale=Row.Scale.Value,Page=Page,Enabled=Row.Enabled,Available=Available,Copies=Row.Copies,Device=Row.Device,StyleSheet=Row.StyleSheet,SheetNumber=Row.Number,Title=Row.Title,Code=Row.Code,Revision=Row.Revision??"",Metadata=Row.Metadata??new Dictionary<string,string>(),Status=Status};
            f.Source.LayoutOrder=LayoutOrder;
            if(Originals.Length==4){f.OriginalTitle=Originals[0];f.OriginalCode=Originals[1];f.OriginalSheetNumber=Originals[2];f.OriginalRevision=Originals[3];}
            if(StampSettings!=null&&Header?.Length==4&&Row.StampParts!=null)f.StampEvidence=new StampScaleEvidence{Scale=f.Scale,Settings=StampSettings,HeaderStart=new Point2(Header[0],Header[1]),HeaderEnd=new Point2(Header[2],Header[3]),Members=Row.StampParts.Select(p=>new StampSegment{DocumentKey=DocumentKey,Layout=Row.Layout,Path=p.Path,Index=p.Index,Start=new Point2(p.Coordinates[0],p.Coordinates[1]),End=new Point2(p.Coordinates[2],p.Coordinates[3]),Elevation=p.Coordinates[4]}).ToList()};
            return f;
        }
    }
    [DataContract] public sealed class WireScan
    {
        [DataMember] public List<WireFrame> Frames {get;set;}=new List<WireFrame>();
        [DataMember] public List<string> Warnings {get;set;}=new List<string>();
        [DataMember] public int Visited {get;set;}
        [DataMember] public int Duplicates {get;set;}
        public static WireScan From(ScanResult s)=>new WireScan{Frames=s.Frames.Select(WireFrame.From).ToList(),Warnings=s.Warnings,Visited=s.ObjectsVisited,Duplicates=s.Duplicates};
        public ScanResult ToScan(){var s=new ScanResult{ObjectsVisited=Visited,Duplicates=Duplicates};s.Frames.AddRange(Frames.Select(f=>f.ToFrame()));s.Warnings.AddRange(Warnings);return s;}
    }
    [DataContract] public sealed class WireMeasure
    {
        [DataMember] public double Reference {get;set;}
        [DataMember] public bool TwoPoints {get;set;}
    }
    [DataContract] public sealed class WireProgress
    {
        [DataMember] public int Done {get;set;}
        [DataMember] public int Total {get;set;}
        [DataMember] public string Text {get;set;}="";
        [DataMember] public string[] Statuses {get;set;}=Array.Empty<string>();
        // Only the statuses that changed since the previous message: sending all of them every time grew with the square
        // of the number of sheets (about 250 000 statuses for 500 sheets).
        [DataMember(EmitDefaultValue=false)] public int[]? Changed {get;set;}
        public static WireProgress Delta(BatchProgress p,IReadOnlyList<string> current,string?[] sent)
        {
            var changed=new List<int>();var values=new List<string>();
            for(int i=0;i<current.Count&&i<sent.Length;i++)if(!string.Equals(current[i],sent[i],StringComparison.Ordinal)){changed.Add(i);values.Add(current[i]);sent[i]=current[i];}
            return new WireProgress{Done=p.Done,Total=p.Total,Text=p.Text,Changed=changed.ToArray(),Statuses=values.ToArray()};
        }
        public void ApplyTo(IReadOnlyList<DetectedFrame> frames)
        {
            if(Changed==null){for(int i=0;i<Math.Min(Statuses.Length,frames.Count);i++)frames[i].Status=Statuses[i];return;}
            for(int i=0;i<Math.Min(Changed.Length,Statuses.Length);i++)if(Changed[i]>=0&&Changed[i]<frames.Count)frames[Changed[i]].Status=Statuses[i];
        }
    }
}
