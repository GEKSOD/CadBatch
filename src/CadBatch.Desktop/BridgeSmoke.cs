using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CadBatch.Core;
using CadBatch.Transport;
using CadBatch.UI;

namespace CadBatch.Desktop
{
    internal static class BridgeSmoke
    {
        public static async Task RunClientAsync(string endpointFile,string output)
        {
            var client=new RemoteHost(WireJson.Read<BridgeEndpoint>(File.ReadAllText(endpointFile)));await client.ConnectAsync();var scan=await client.SearchAsync(new ProjectProfile(),CancellationToken.None);
            if(scan.Frames.Count!=12||scan.Frames.Any(f=>f.Scale!=1))throw new Exception("Cross-runtime IPC did not preserve frames.");
            File.WriteAllText(output,"CROSS-RUNTIME IPC PASS: .NET Framework 4.8 server → standalone .NET client; 12 frames preserved.");
        }
        public static async Task RunAsync(string output)
        {
            string directory=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"bridge-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
            try
            {
                using(var server=new HostBridge(new DemoHost(),action=>action(),directory))
                {
                    var endpoint=WireJson.Read<BridgeEndpoint>(File.ReadAllText(Directory.GetFiles(directory,"*.json").Single()));var client=new RemoteHost(endpoint);await client.ConnectAsync();
                    var scan=await client.SearchAsync(new ProjectProfile(),CancellationToken.None);if(scan.Frames.Count!=12||scan.Frames.Any(f=>f.Scale!=1||f.Geometry.Corners.Length!=4))throw new Exception("Invalid frame round-trip.");
                    if((await client.GetPlotDevicesAsync()).Length!=0)throw new Exception("Invalid empty device round-trip.");
                    bool failed=false;try{await client.ExportAsync(new ExportRequest{Items=new System.Collections.Generic.List<OutputItem>{new OutputItem{Frame=scan.Frames[0],Path=Path.Combine(directory,"test.pdf")}}},new Progress<BatchProgress>(),CancellationToken.None);}catch(InvalidOperationException){failed=true;}if(!failed)throw new Exception("Native error was not propagated.");
                    // A failed command must not poison the next connection.
                    await client.ConnectAsync();if((await client.SearchAsync(new ProjectProfile(),CancellationToken.None)).Frames.Count!=12)throw new Exception("Server did not recover after a failed command.");
                    File.WriteAllText(output,"IPC PASS: authenticated same-user named pipe; 12 frames/metadata/geometry round-trip; error propagation; reconnection after error.\n");
                }
            }
            finally{foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}
        }
    }
}
