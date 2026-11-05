using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CadBatch.Core;
using CadBatch.Transport;
using CadBatch.UI;

namespace CadBatch.Desktop
{
    internal sealed class RemoteHost : IBatchHost
    {
        private readonly BridgeEndpoint endpoint;
        private readonly SemaphoreSlim gate=new SemaphoreSlim(1);
        private BridgeInfo? info;
        public RemoteHost(BridgeEndpoint endpoint){this.endpoint=endpoint;}
        public string Description=>info?.Description??endpoint.Description;
        public bool CanPlot=>info?.CanPlot==true;
        public int ProcessId=>endpoint.ProcessId;
        public async Task ConnectAsync(){info=await Call<object?,BridgeInfo>("info",null,CancellationToken.None);if(info.Protocol!=BridgeProtocol.Version)throw new InvalidOperationException("Несовместимый компонент AutoCAD. Обновите установку CAD Batch.");}
        private async Task<TResult> Call<TRequest,TResult>(string method,TRequest value,CancellationToken cancellation,Action<BridgeMessage>? notify=null)
        {
            if(method=="locate"||method=="measure"||method=="region"||method=="operation")try{using(var process=System.Diagnostics.Process.GetProcessById(endpoint.ProcessId)){AllowSetForegroundWindow(endpoint.ProcessId);if(process.MainWindowHandle!=IntPtr.Zero)BringToFront(process.MainWindowHandle);}}catch(ArgumentException){}
            await gate.WaitAsync(cancellation);try
            {
                using(var client=new NamedPipeClientStream(".",endpoint.Pipe,PipeDirection.InOut,PipeOptions.Asynchronous))
                {
                    try{await client.ConnectAsync(4000,cancellation);}catch(TimeoutException){info=null;throw new IOException("Нет связи с AutoCAD. Откройте AutoCAD и подключитесь заново.");}
                    // The program never waits forever: quick questions get a time limit, and a cancelled operation
                    // that AutoCAD does not acknowledge is abandoned after a short grace period. The limit covers
                    // sending the request too: a stuck AutoCAD may not even take it from the pipe.
                    using var reading=new CancellationTokenSource();if(QuickMethods.Contains(method))reading.CancelAfter(QuickLimit);
                    using var sending=CancellationTokenSource.CreateLinkedTokenSource(cancellation,reading.Token);
                    Exception Stuck(){info=null;return cancellation.IsCancellationRequested?new OperationCanceledException("AutoCAD не ответил на отмену. Операция прервана; проверьте AutoCAD и подключитесь снова.",cancellation):new IOException("AutoCAD не отвечает уже "+QuickLimit.TotalSeconds+" секунд — нет связи с CAD Batch. Возможно, в AutoCAD открыто окно или идёт команда.");}
                    string id=Guid.NewGuid().ToString("N");
                    try{await BridgeProtocol.WriteAsync(client,new BridgeMessage{Id=id,Token=endpoint.Token,Method=method,Body=WireJson.Write(value)},sending.Token);}
                    catch(OperationCanceledException) when(reading.IsCancellationRequested){throw Stuck();}
                    var sendGate=new SemaphoreSlim(1);Task cancelSend=Task.CompletedTask;
                    using(var registration=cancellation.Register(()=>{reading.CancelAfter(CancelGrace);cancelSend=Task.Run(async()=>{try{await sendGate.WaitAsync();using var limit=new CancellationTokenSource(CancelGrace);await BridgeProtocol.WriteAsync(client,new BridgeMessage{Id=id,Kind="cancel"},limit.Token);}catch(IOException){}catch(ObjectDisposedException){}catch(OperationCanceledException){}finally{sendGate.Release();}});}))
                    {
                        while(true)
                        {
                            BridgeMessage message;
                            try{message=await BridgeProtocol.ReadAsync(client,reading.Token);}
                            catch(OperationCanceledException) when(reading.IsCancellationRequested){throw Stuck();}
                            if(message.Id!=id)throw new IOException("Нарушена последовательность сообщений AutoCAD.");
                            if(message.Kind=="result")return WireJson.Read<TResult>(message.Body);
                            if(message.Kind=="error")throw new InvalidOperationException(WireJson.Read<string>(message.Body));
                            if(message.Kind=="cancelled")throw new OperationCanceledException(cancellation);
                            if(message.Kind=="interaction"){bool hide=WireJson.Read<bool>(message.Body);CadInteraction.VisibilityChanged?.Invoke(hide);}
                            else notify?.Invoke(message);
                        }
                    }
                }
            }
            catch(EndOfStreamException){info=null;throw;}finally{gate.Release();CadInteraction.VisibilityChanged?.Invoke(false);}
        }
        private static readonly HashSet<string> QuickMethods=new HashSet<string>(StringComparer.Ordinal){"info","devices","capabilities","layouts"};
        internal static TimeSpan QuickLimit=TimeSpan.FromSeconds(30),CancelGrace=TimeSpan.FromSeconds(20);
        public Task<string[]> GetPlotDevicesAsync()=>Call<object?,string[]>("devices",null,CancellationToken.None);
        public Task<PlotCapabilities> GetPlotCapabilitiesAsync(string device)=>Call<string,PlotCapabilities>("capabilities",device,CancellationToken.None);
        public Task<string[]> GetLayoutNamesAsync()=>Call<object?,string[]>("layouts",null,CancellationToken.None);
        public Task<SearchRegion?> PickSearchRegionAsync()=>Call<object?,SearchRegion?>("region",null,CancellationToken.None);
        public Task<SearchSelection?> PickSelectionAsync()=>Call<object?,SearchSelection?>("selection",null,CancellationToken.None);
        public Task<StampScaleMeasurement?> MeasureStampScaleAsync(double referenceWidthMm,bool twoPoints)=>Call<WireMeasure,StampScaleMeasurement?>("measure",new WireMeasure{Reference=referenceWidthMm,TwoPoints=twoPoints},CancellationToken.None);
        public async Task<ScanResult> SearchAsync(ProjectProfile profile,CancellationToken cancellation)=>(await Call<ProjectProfile,WireScan>("search",profile,cancellation)).ToScan();
        public Task LocateAsync(DetectedFrame frame)=>Call<WireFrame,object?>("locate",WireFrame.From(frame),CancellationToken.None);
        public async Task<ExportReport> ExportAsync(ExportRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation)
        {
            void Statuses(string[] values){for(int i=0;i<Math.Min(values.Length,request.Items.Count);i++)request.Items[i].Frame.Status=values[i];}
            var report=await Call<WireExport,WireReport>("export",WireExport.From(request),cancellation,message=>
            {
                if(message.Kind=="completed")request.OutputCompleted?.Invoke(WireJson.Read<string>(message.Body));
                if(message.Kind=="progress"){var p=WireJson.Read<WireProgress>(message.Body);p.ApplyTo(request.Items.Select(i=>i.Frame).ToList());progress.Report(new BatchProgress(p.Done,p.Total,p.Text));}
            });Statuses(report.Statuses);return report.ToReport();
        }
        public async Task<CadOperationResult> RunCadOperationAsync(CadOperationRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation)
        {
            var result=await Call<WireOperation,WireOperationResult>("operation",WireOperation.From(request),cancellation,message=>{if(message.Kind=="progress"){var p=WireJson.Read<WireProgress>(message.Body);progress.Report(new BatchProgress(p.Done,p.Total,p.Text));}});
            for(int i=0;i<Math.Min(request.Frames.Count,result.Frames.Count);i++)request.Frames[i].Source.Attributes=result.Frames[i].ToFrame().Source.Attributes;
            return result.ToResult();
        }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool AllowSetForegroundWindow(int processId);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr window,int command);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsIconic(IntPtr window);
        // AutoCAD comes to the front as the user left it. "Restore" un-maximizes a maximized window, so it is used only
        // when AutoCAD is minimized: then Windows brings back its previous state, full screen included.
        internal static void BringToFront(IntPtr window)
        {
            if(IsIconic(window))ShowWindow(window,9);
            SetForegroundWindow(window);
        }
        public static string DiscoveryDirectory=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","bridge");
        public static List<BridgeEndpoint> Discover()=>Scan(true);
        // Running AutoCAD with a component of another protocol (updated CAD Batch, AutoCAD not restarted): the window says
        // so instead of waiting for an AutoCAD that is already open.
        public static List<BridgeEndpoint> Outdated()=>Scan(false);
        private static List<BridgeEndpoint> Scan(bool compatible)
        {
            var list=new List<BridgeEndpoint>();if(!Directory.Exists(DiscoveryDirectory))return list;
            foreach(string path in Directory.GetFiles(DiscoveryDirectory,"*.json"))try
            {
                var e=WireJson.Read<BridgeEndpoint>(File.ReadAllText(path));if((e.Protocol==BridgeProtocol.Version)!=compatible||!e.Pipe.StartsWith("CadBatch-",StringComparison.Ordinal)||e.Token.Length<32)continue;
                using(var process=System.Diagnostics.Process.GetProcessById(e.ProcessId))if(!process.HasExited&&process.ProcessName.Equals("acad",StringComparison.OrdinalIgnoreCase)){e.Description+=" · PID "+e.ProcessId+" · "+process.MainWindowTitle;list.Add(e);}
            }catch(IOException){}catch(ArgumentException){}catch(System.Runtime.Serialization.SerializationException){}catch(System.ComponentModel.Win32Exception){}
            return list;
        }
    }
}
