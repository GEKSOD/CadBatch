using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CadBatch.Core;
using CadBatch.UI;

class WpfFrameworkSmoke : IBatchHost
{
    public string Description {get{return "NET48 WPF compatibility smoke";}}
    public bool CanPlot {get{return false;}}
    public Task<PlotCapabilities> GetPlotCapabilitiesAsync(string device){return Task.FromResult(new PlotCapabilities());}
    public Task<string[]> GetPlotDevicesAsync(){return Task.FromResult(new string[0]);}
    public Task<string[]> GetLayoutNamesAsync(){return Task.FromResult(new string[0]);}
    public Task<SearchRegion> PickSearchRegionAsync(){return Task.FromResult<SearchRegion>(null);}
    public Task<StampScaleMeasurement> MeasureStampScaleAsync(double width,bool twoPoints){return Task.FromResult<StampScaleMeasurement>(null);}
    public Task<ScanResult> SearchAsync(ProjectProfile profile,CancellationToken token)
    {
        var sources=new List<FrameCandidate>();
        for(int i=0;i<12;i++)sources.Add(new FrameCandidate{DocumentKey="demo",DocumentName="Framework.dwg",HandlePath=(i+1).ToString("X"),Vertices=new List<Point2>{new Point2(i*500,0),new Point2(i*500+420,0),new Point2(i*500+420,297),new Point2(i*500,297)}});
        return Task.FromResult(FrameDetection.Detect(sources,new SearchOptions(),FormatDefinition.Defaults(),token));
    }
    public Task LocateAsync(DetectedFrame f){return Task.FromResult(0);}
    public Task<ExportReport> ExportAsync(ExportRequest r,IProgress<BatchProgress> p,CancellationToken t){throw new NotSupportedException();}
    public Task<CadOperationResult> RunCadOperationAsync(CadOperationRequest r,IProgress<BatchProgress> p,CancellationToken t){throw new NotSupportedException();}
    [STAThread] static int Main(string[] args)
    {
        try
        {
            if(args.Length>1&&args[1]=="--bridge-server")
            {
                Directory.CreateDirectory(args[0]);using(var bridge=new HostBridge(new WpfFrameworkSmoke(),action=>action(),args[0]))
                {for(int n=0;n<1200&&!File.Exists(Path.Combine(args[0],"stop"));n++)System.Threading.Thread.Sleep(100);}
                return 0;
            }
            System.Windows.Forms.Application.EnableVisualStyles();bool noApp=args.Length>1&&args[1]=="--no-app";var app=noApp?null:new System.Windows.Application();var window=new BatchWindow.FluentWindow(new WpfFrameworkSmoke(),Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[0])),"smoke-profile.json"));window.ShowIntroOnStart=false;
            window.ContentRendered+=async(s,e)=>{try{await window.SmokeAsync(args[0]);}catch(Exception ex){File.WriteAllText(args[0]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{window.Close();}};
            if(noApp){window.Closed+=(s,e)=>System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvokeShutdown(System.Windows.Threading.DispatcherPriority.Background);window.Show();System.Windows.Threading.Dispatcher.Run();}else app.Run(window);return Environment.ExitCode;
        }
        catch(Exception ex){File.WriteAllText(args[0]+".error.txt",ex.ToString());return 1;}
    }
}
