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
        Run("Drawing order: files by name with numbers, tabs in AutoCAD order, rows with tolerance",()=>
        {
            FrameCandidate C(string handle,string key,string file,string tab,int tabOrder,double x,double y,double w,double h){var c=Candidate(handle,x,y,w,h);c.DocumentKey=key;c.DocumentName=file;c.DocumentPath=@"C:\П\"+file;c.Layout=tab;c.LayoutOrder=tabOrder;return c;}
            // The document key starts with the drawing's GUID: "f…" for 01-АР, "0…" for 02-КР.
            var a=C("A","f1|01-АР","01-АР.dwg","Model",0,0,0,420,297);var b=C("B","01|02-КР","02-КР.dwg","Model",0,0,0,420,297);
            // Tabs Лист1, Лист10, Лист2 created in that order but arranged Лист1, Лист2, Лист10.
            var t1=C("T1","f1|01-АР","01-АР.dwg","Лист1",1,0,0,420,297);var t10=C("T10","f1|01-АР","01-АР.dwg","Лист10",3,0,0,420,297);var t2=C("T2","f1|01-АР","01-АР.dwg","Лист2",2,0,0,420,297);
            // A row А3, А1, А4 aligned at the bottom: the A1 is taller, its centre higher.
            var r3=C("R3","f1|01-АР","01-АР.dwg","Model",0,1000,0,420,297);var r1=C("R1","f1|01-АР","01-АР.dwg","Model",0,1500,0,841,594);var r4=C("R4","f1|01-АР","01-АР.dwg","Model",0,2500,0,297,210);
            var found=FrameDetection.Detect(new[]{b,t10,r4,t2,r1,a,t1,r3},new SearchOptions{Layouts=true},FormatDefinition.Defaults(),CancellationToken.None).Frames;
            Assert(found.Select(f=>f.Source.HandlePath).SequenceEqual(new[]{"A","R3","R1","R4","T1","T2","T10","B"}));
            Assert(found.Select(f=>f.Page).SequenceEqual(Enumerable.Range(1,8))&&FrameOrder.ByDrawing(found.AsEnumerable().Reverse()).Select(f=>f.Source.HandlePath).SequenceEqual(found.Select(f=>f.Source.HandlePath)));
            // Two rows stay two rows.
            var up=C("U","f1|01-АР","01-АР.dwg","Model",0,5000,1000,420,297);var down=C("D","f1|01-АР","01-АР.dwg","Model",0,4000,0,420,297);
            Assert(FrameDetection.Detect(new[]{down,up},new SearchOptions(),FormatDefinition.Defaults(),CancellationToken.None).Frames.Select(f=>f.Source.HandlePath).SequenceEqual(new[]{"U","D"}));
        });
        Run("The tab order crosses the bridge",()=>
        {
            var f=Detect(Candidate("W",0,0,420,297)).Frames.Single();f.Source.Layout="Лист2";f.Source.LayoutOrder=4;
            Assert(WireJson.Read<WireFrame>(WireJson.Write(WireFrame.From(f))).ToFrame().Source.LayoutOrder==4);
        });
        Run("Downloaded installers of this and older versions are removed, newer and foreign folders kept",()=>
        {
            string root=System.IO.Path.Combine(TestRoot,"artifacts","verification","Updates-"+Guid.NewGuid().ToString("N"));
            foreach(var name in new[]{"0.8.7","0.8.8","0.9.0","Мои файлы"}){System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root,name));System.IO.File.WriteAllText(System.IO.Path.Combine(root,name,"CadBatch-Setup.exe"),"x");}
            try
            {
                Assert(CadBatch.Updates.UpdateClient.RemoveOldDownloads(root,"0.8.8")==2);
                Assert(!System.IO.Directory.Exists(System.IO.Path.Combine(root,"0.8.7"))&&!System.IO.Directory.Exists(System.IO.Path.Combine(root,"0.8.8"))&&System.IO.Directory.Exists(System.IO.Path.Combine(root,"0.9.0"))&&System.IO.Directory.Exists(System.IO.Path.Combine(root,"Мои файлы")));
                Assert(CadBatch.Updates.UpdateClient.RemoveOldDownloads(System.IO.Path.Combine(root,"нет"),"0.8.8")==0);
            }
            finally{System.IO.Directory.Delete(root,true);}
        });
        Run("Progress carries only the statuses that changed",()=>
        {
            var frames=Enumerable.Range(0,4).Select(i=>Detect(Candidate("P"+i,i*500,0,420,297)).Frames.Single()).ToList();
            var sent=new string?[4];var statuses=new List<string>{"","","",""};
            var first=WireProgress.Delta(new BatchProgress(0,4,"Старт"),statuses,sent);Assert(first.Changed!.Length==4);
            statuses[2]="PDF создан";var second=WireJson.Read<WireProgress>(WireJson.Write(WireProgress.Delta(new BatchProgress(1,4,"PDF 1"),statuses,sent)));
            Assert(second.Changed!.SequenceEqual(new[]{2})&&second.Statuses.SequenceEqual(new[]{"PDF создан"}));
            second.ApplyTo(frames);Assert(frames[2].Status=="PDF создан"&&frames[0].Status!="PDF создан");
            new WireProgress{Statuses=new[]{"a","b"}}.ApplyTo(frames);Assert(frames[0].Status=="a"&&frames[1].Status=="b");
        });
        Run("A register saved as Unicode text (UTF-16) keeps Cyrillic",()=>
        {
            var text="Лист\tНаименование\r\n1\tПлан этажа\r\n";
            Assert(RegisterImport.ReadText(System.Text.Encoding.Unicode.GetPreamble().Concat(System.Text.Encoding.Unicode.GetBytes(text)).ToArray())==text);
            Assert(RegisterImport.ReadText(System.Text.Encoding.BigEndianUnicode.GetPreamble().Concat(System.Text.Encoding.BigEndianUnicode.GetBytes(text)).ToArray())==text);
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
