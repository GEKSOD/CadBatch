using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CadBatch.Core;
using CadBatch.Transport;

internal static partial class Program
{
    private static void RunSelectionOrderTests()
    {
        List<MediaEntry> Pc3()=>new List<MediaEntry>
        {
            new MediaEntry{Name="ISO_full_bleed_A4_(297.00_x_210.00_MM)",Width=297,Height=210},
            new MediaEntry{Name="ISO_full_bleed_A3_(420.00_x_297.00_MM)",Width=420,Height=297},
            new MediaEntry{Name="ISO_A3_(420.00_x_297.00_MM)",Width=420,Height=297,Margins=22},
            new MediaEntry{Name="ISO_full_bleed_A1_(841.00_x_594.00_MM)",Width=841,Height=594},
            new MediaEntry{Name="ISO_full_bleed_2A0_(1189.00_x_1682.00_MM)",Width=1189,Height=1682},
        };
        Run("Paper: exact full-bleed size, turned when the sheet is portrait",()=>
        {
            var landscape=MediaPicker.Pick(Pc3(),420,297,true);Assert(landscape.Name.Contains("full_bleed_A3")&&!landscape.Rotated&&!landscape.IsCarrier);
            var portrait=MediaPicker.Pick(Pc3(),297,420,true);Assert(portrait.Name.Contains("full_bleed_A3")&&portrait.Rotated);
        });
        Run("Paper: a multiple format uses the smallest larger carrier",()=>
        {
            var pick=MediaPicker.Pick(Pc3(),630,297,true);Assert(pick.IsCarrier&&pick.Name.Contains("A1")&&pick.ScaleFactor==1);
            var huge=MediaPicker.Pick(Pc3(),2523,1189,true);Assert(huge.IsCarrier&&huge.Name.Contains("2A0")&&huge.ScaleFactor<1);
        });
        Run("Paper: a template paper is honoured and margins need fitting",()=>
        {
            Assert(MediaPicker.Pick(Pc3(),420,297,true,"ISO_A3_(420.00_x_297.00_MM)").Name=="ISO_A3_(420.00_x_297.00_MM)");
            Throws<InvalidOperationException>(()=>MediaPicker.Pick(Pc3(),420,297,false,"ISO_A3_(420.00_x_297.00_MM)"));
            Throws<InvalidOperationException>(()=>MediaPicker.Pick(new List<MediaEntry>(),420,297,true));
        });
        Run("Paper catalogue is read once per device and read again after a failure",()=>
        {
            int reads=0;string device="Тест-"+Guid.NewGuid().ToString("N");
            List<MediaEntry> Read(){reads++;return Pc3();}
            MediaCatalog.Get(device,Read,out bool first);MediaCatalog.Get(device.ToUpperInvariant(),Read,out bool second);
            Assert(!first&&second&&reads==1);
            MediaCatalog.Forget(device);MediaCatalog.Get(device,Read,out bool third);Assert(!third&&reads==2);
            MediaCatalog.Get("Пустой-"+device,()=>new List<MediaEntry>(),out _);MediaCatalog.Get("Пустой-"+device,()=>{reads++;return new List<MediaEntry>();},out bool empty);Assert(!empty&&reads==3);
            MediaCatalog.Clear();MediaCatalog.Get(device,Read,out bool fourth);Assert(!fourth&&reads==4);
        });
        Run("Sheets follow their numbers; sheets without a number keep their place at the end",()=>
        {
            var frames=Enumerable.Range(0,6).Select(i=>Detect(Candidate("O"+i,i*500,0,420,297)).Frames.Single()).ToList();
            string[] numbers={"10","2","","1","2","АР-3"};for(int i=0;i<6;i++)frames[i].SheetNumber=numbers[i];
            var ordered=FrameOrder.Ordered(frames,FrameOrder.ByNumber);
            Assert(ordered.Select(f=>f.SheetNumber).SequenceEqual(new[]{"1","2","2","10","АР-3",""}));
            Assert(ordered[1].Source.HandlePath=="O1"&&ordered[2].Source.HandlePath=="O4"&&ordered.Select(f=>f.Page).SequenceEqual(Enumerable.Range(1,6)));
            var kept=FrameOrder.Ordered(frames,null);Assert(kept.Select(f=>f.Source.HandlePath).SequenceEqual(frames.Select(f=>f.Source.HandlePath)));
            var drawing=FrameOrder.ByDrawing(ordered);Assert(drawing.Select(f=>f.Source.HandlePath).SequenceEqual(new[]{"O0","O1","O2","O3","O4","O5"}));
        });
        Run("The sheet order is kept in the profile",()=>
        {
            var p=new ProjectProfile{SheetOrder=FrameOrder.ByNumber};var copy=WireJson.Read<ProjectProfile>(WireJson.Write(p));
            Assert(copy.SheetOrder==FrameOrder.ByNumber&&WireJson.Read<ProjectProfile>(WireJson.Write(new ProjectProfile())).SheetOrder==null);
        });
        Run("A selection reaches AutoCAD with the search settings and stays through the wider search",()=>
        {
            var selection=new SearchSelection{DocumentPath=@"C:\Проект\План.dwg",Layout="Лист1",Handles=new List<string>{"2F1","2F2","A0B"}};
            var p=new ProjectProfile();p.Search.Selection=selection;p.Validate();
            var copy=WireJson.Read<ProjectProfile>(WireJson.Write(p)).Search.Selection!;
            Assert(copy.Layout=="Лист1"&&copy.HandleSet().Contains("a0b")&&copy.Handles.Count==3);
            Assert(SearchFallback.Wider(p.Search,1).Selection==selection&&SearchFallback.Adopt(p.Search,new DetectedFrame[0],FormatDefinition.Defaults()).Selection==selection);
            Assert(SearchFallback.Signature(p.Search)!=SearchFallback.Signature(new SearchOptions()));
            Assert(!WireJson.Write(new ProjectProfile()).Contains("Selection"));
        });
        Run("The AutoCAD component announces the current protocol version",()=>
        {
            Assert(new BridgeEndpoint().Protocol==BridgeProtocol.Version&&new BridgeInfo().Protocol==BridgeProtocol.Version&&BridgeProtocol.Version>=2);
        });
        Run("An empty or broken selection is refused",()=>
        {
            Throws<ArgumentException>(()=>new SearchSelection{DocumentPath="a.dwg",Layout="Model"}.Validate());
            Throws<ArgumentException>(()=>new SearchSelection{DocumentPath="a.dwg",Layout="Model",Handles=new List<string>{"XYZ"}}.Validate());
            Throws<ArgumentException>(()=>new SearchSelection{DocumentPath="",Layout="Model",Handles=new List<string>{"1F"}}.Validate());
        });
    }
}
