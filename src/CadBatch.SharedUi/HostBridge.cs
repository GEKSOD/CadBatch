using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CadBatch.Core;
using CadBatch.Transport;
#if NETFRAMEWORK
using System.Security.AccessControl;
using System.Security.Principal;
#endif

namespace CadBatch.UI
{
    public sealed class HostBridge : IDisposable
    {
        private readonly IBatchHost host;
        private readonly Func<Func<Task<string>>,Task<string>> dispatch;
        private readonly CancellationTokenSource stop=new CancellationTokenSource();
        private readonly BridgeEndpoint endpoint;
        private readonly string registration;
        private NamedPipeServerStream? pipe;
        public HostBridge(IBatchHost host,Func<Func<Task<string>>,Task<string>> dispatch,string directory)
        {
            this.host=host;this.dispatch=dispatch;
            int pid=System.Diagnostics.Process.GetCurrentProcess().Id;
            endpoint=new BridgeEndpoint{Version=ProductInfo.Version,ProcessId=pid,Pipe="CadBatch-"+pid+"-"+Guid.NewGuid().ToString("N"),Token=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),Description=host.Description};
            Directory.CreateDirectory(directory);registration=Path.Combine(directory,pid+".json");File.WriteAllText(registration,WireJson.Write(endpoint));
            _=Task.Run(ServeAsync);
        }
        private NamedPipeServerStream CreatePipe()
        {
#if NETFRAMEWORK
            var security=new PipeSecurity();var user=WindowsIdentity.GetCurrent().User??throw new InvalidOperationException("Не удалось определить пользователя Windows.");
            security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(user,PipeAccessRights.FullControl,AccessControlType.Allow));
            return new NamedPipeServerStream(endpoint.Pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,security);
#else
            return new NamedPipeServerStream(endpoint.Pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly,65536,65536);
#endif
        }
        private async Task ServeAsync()
        {
            while(!stop.IsCancellationRequested)
            {
                try{using(var server=CreatePipe()){pipe=server;await server.WaitForConnectionAsync(stop.Token).ConfigureAwait(false);await HandleAsync(server).ConfigureAwait(false);}}
                catch(OperationCanceledException){break;}catch(ObjectDisposedException){if(stop.IsCancellationRequested)break;}
                catch(Exception){if(!stop.IsCancellationRequested)await Task.Delay(150).ConfigureAwait(false);}
                finally{pipe=null;}
            }
        }
        private async Task HandleAsync(NamedPipeServerStream server)
        {
            using(var operation=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
            {
                BridgeMessage request;
                using(var handshake=CancellationTokenSource.CreateLinkedTokenSource(stop.Token)){handshake.CancelAfter(5000);request=await BridgeProtocol.ReadAsync(server,handshake.Token).ConfigureAwait(false);}
                if(request.Token!=endpoint.Token||request.Kind!="request"||request.Id.Length>100)return;
                var writes=new OrderedWriter(server,request.Id,stop.Token);
                using(var readStop=CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
                {
                    var monitor=MonitorCancellation(server,request.Id,operation,readStop.Token);
                    try
                    {
                        string result=await dispatch(async()=>
                        {
                            var previous=CadInteraction.VisibilityChanged;
                            CadInteraction.VisibilityChanged=hide=>{previous?.Invoke(hide);writes.Send("interaction",WireJson.Write(hide));};
                            try{return await InvokeAsync(request,writes,operation.Token);}
                            finally{CadInteraction.VisibilityChanged=previous;}
                        }).ConfigureAwait(false);
                        writes.Send("result",result);
                    }
                    catch(OperationCanceledException){writes.Send("cancelled","null");}
                    catch(Exception ex){ErrorLog.Write("Компонент AutoCAD",ex);writes.Send("error",WireJson.Write(ex.Message));}
                    finally{try{await writes.Completion.ConfigureAwait(false);}catch(IOException){}readStop.Cancel();try{await monitor.ConfigureAwait(false);}catch{} }
                }
            }
        }
        private static async Task MonitorCancellation(Stream stream,string id,CancellationTokenSource operation,CancellationToken token)
        {
            try{while(!token.IsCancellationRequested){var msg=await BridgeProtocol.ReadAsync(stream,token).ConfigureAwait(false);if(msg.Kind=="cancel"&&msg.Id==id){operation.Cancel();return;}}}
            catch(OperationCanceledException){}catch{operation.Cancel();}
        }
        private async Task<string> InvokeAsync(BridgeMessage message,OrderedWriter writes,CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            switch(message.Method)
            {
                case "info":return WireJson.Write(new BridgeInfo{Version=ProductInfo.Version,Description=host.Description,CanPlot=host.CanPlot});
                case "devices":return WireJson.Write(await host.GetPlotDevicesAsync());
                case "capabilities":return WireJson.Write(await host.GetPlotCapabilitiesAsync(WireJson.Read<string>(message.Body)));
                case "layouts":return WireJson.Write(await host.GetLayoutNamesAsync());
                case "region":return WireJson.Write(await host.PickSearchRegionAsync());
                case "selection":return WireJson.Write(await host.PickSelectionAsync());
                case "measure":var measure=WireJson.Read<WireMeasure>(message.Body);return WireJson.Write(await host.MeasureStampScaleAsync(measure.Reference,measure.TwoPoints));
                case "search":return WireJson.Write(WireScan.From(await host.SearchAsync(WireJson.Read<ProjectProfile>(message.Body),cancellation)));
                case "locate":await host.LocateAsync(WireJson.Read<WireFrame>(message.Body).ToFrame());return "null";
                case "export":
                    var export=WireJson.Read<WireExport>(message.Body).ToRequest();export.OutputCompleted=path=>writes.Send("completed",WireJson.Write(path));
                    var sent=new string?[export.Items.Count];
                    var report=await host.ExportAsync(export,new DirectProgress(p=>writes.Send("progress",WireJson.Write(WireProgress.Delta(p,export.Items.Select(i=>i.Frame.Status).ToList(),sent)))),cancellation);
                    return WireJson.Write(WireReport.From(report,export));
                case "operation":
                    var request=WireJson.Read<WireOperation>(message.Body).ToRequest();var result=await host.RunCadOperationAsync(request,new DirectProgress(p=>writes.Send("progress",WireJson.Write(new WireProgress{Done=p.Done,Total=p.Total,Text=p.Text}))),cancellation);
                    return WireJson.Write(WireOperationResult.From(result,request));
                default:throw new InvalidOperationException("Команда подключения CAD Batch не поддерживается. Обновите приложение и компонент AutoCAD.");
            }
        }
        private sealed class DirectProgress : IProgress<BatchProgress>{private readonly Action<BatchProgress> action;public DirectProgress(Action<BatchProgress> action){this.action=action;}public void Report(BatchProgress value)=>action(value);}
        private sealed class OrderedWriter
        {
            private readonly Stream stream;private readonly string id;private readonly CancellationToken token;private readonly object sync=new object();private Task tail=Task.CompletedTask;
            public OrderedWriter(Stream stream,string id,CancellationToken token){this.stream=stream;this.id=id;this.token=token;}
            public void Send(string kind,string body){lock(sync)tail=tail.ContinueWith(_=>BridgeProtocol.WriteAsync(stream,new BridgeMessage{Id=id,Kind=kind,Body=body},token),CancellationToken.None,TaskContinuationOptions.None,TaskScheduler.Default).Unwrap();}
            public Task Completion{get{lock(sync)return tail;}}
        }
        public void Dispose(){stop.Cancel();pipe?.Dispose();try{if(File.Exists(registration))File.Delete(registration);}catch(IOException){} }
    }
}
