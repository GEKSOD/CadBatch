using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CadBatch.Core;
using CadBatch.Transport;
using CadBatch.Updates;

internal static partial class Program
{
    private static void RunApplicationTests()
    {
        Run("Desktop transport preserves frame geometry, attributes and print settings",()=>
        {
            var f=Detect(Candidate("PIPE",100,200,420,297)).Frames.Single();f.Title="План";f.Scale=1;f.Metadata["Organization"]="Проект";f.Source.Attributes["SHEET"]="007";f.Format.Print=new FormatPrintSettings{Device="Custom.pc3",UpsideDown=true};
            var copy=WireJson.Read<WireFrame>(WireJson.Write(WireFrame.From(f))).ToFrame();Assert(copy.Geometry.SameBoundary(f.Geometry,.0001)&&copy.Title==f.Title&&copy.Source.Attributes["SHEET"]=="007"&&copy.Metadata["Organization"]=="Проект"&&copy.Format.Print!.UpsideDown);
        });
        Run("Desktop transport preserves stamp evidence for native revalidation",()=>
        {
            var f=Detect(Candidate("STAMP",0,0,420,297)).Frames.Single();f.StampEvidence=new StampScaleEvidence{Settings=new StampScaleSettings{ReferenceWidthMm=185},Scale=1,HeaderStart=new Point2(230,60),HeaderEnd=new Point2(415,60),Members=new System.Collections.Generic.List<StampSegment>{new StampSegment{Path="A/B",Index=3,Start=new Point2(230,60),End=new Point2(415,60),Elevation=0}}};
            var copy=WireJson.Read<WireFrame>(WireJson.Write(WireFrame.From(f))).ToFrame();Assert(copy.StampEvidence!=null&&copy.StampEvidence.IsCurrent(f.StampEvidence.Members,.0001)&&copy.StampEvidence.HeaderEnd.X==415);
        });
        Run("Desktop transport keeps grouped PDF items shared and ordered",()=>
        {
            var frames=Detect(Candidate("A",0,0,420,297),Candidate("B",500,0,420,297)).Frames;var request=new ExportRequest{Items=frames.Select(f=>new OutputItem{Frame=f,Path=f.Source.HandlePath+".pdf"}).ToList(),PdfMode=PdfOutputMode.OneFile};request.Groups.Add(new PdfGroup{Path="all.pdf",Items=request.Items.ToList()});
            var copy=WireJson.Read<WireExport>(WireJson.Write(WireExport.From(request))).ToRequest();Assert(copy.Items.Count==2&&ReferenceEquals(copy.Items[1],copy.Groups[0].Items[1])&&copy.PdfMode==PdfOutputMode.OneFile);
        });
        Run("Desktop protocol rejects oversized wire payloads before allocation",()=>
        {
            using(var stream=new MemoryStream(BitConverter.GetBytes(int.MaxValue)))Throws<IOException>(()=>BridgeProtocol.ReadAsync(stream,CancellationToken.None).GetAwaiter().GetResult());
        });
        Run("Desktop protocol reports truncated payloads instead of hanging",()=>
        {
            using(var stream=new MemoryStream(new byte[]{8,0,0,0,1,2}))Throws<EndOfStreamException>(()=>BridgeProtocol.ReadAsync(stream,CancellationToken.None).GetAwaiter().GetResult());
        });
        Run("Update versions compare numerically",()=>{Assert(UpdateClient.IsNewer("0.4.9","0.4.10"));Assert(!UpdateClient.IsNewer("0.4.0","0.4.0"));Assert(!UpdateClient.IsNewer("1.0.0","0.9.9"));Throws<InvalidDataException>(()=>UpdateClient.IsNewer("0.4.0","1.2.3-beta"));});
        string Json(string url="",string hash="",bool draft=false)=>JsonSerializer.Serialize(new{tag_name="v0.5.0",draft,prerelease=false,body="Изменения",assets=new[]{new{name="CadBatch-Setup-0.5.0.exe",size=2048,browser_download_url=url.Length>0?url:"https://github.com/Geksod/CadBatch/releases/download/v0.5.0/CadBatch-Setup-0.5.0.exe",digest="sha256:"+(hash.Length>0?hash:new string('a',64))}}});
        Run("Updater accepts only a release asset from the pinned project",()=>{var r=UpdateClient.ParseRelease(Json());Assert(r.Version=="0.5.0"&&r.Size==2048);Throws<InvalidDataException>(()=>UpdateClient.ParseRelease(Json("https://github.com/other/project/releases/download/v0.5.0/CadBatch-Setup-0.5.0.exe")));Throws<InvalidDataException>(()=>UpdateClient.ParseRelease(Json("https://github.com.evil.example/Geksod/CadBatch/setup.exe")));});
        Run("Updater rejects drafts and missing valid SHA256 digests",()=>{Throws<InvalidDataException>(()=>UpdateClient.ParseRelease(Json(draft:true)));Throws<InvalidDataException>(()=>UpdateClient.ParseRelease(Json(hash:"bad")));});
        string directory=Path.Combine(TestRoot,"artifacts","verification","updates-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            byte[] payload=Enumerable.Range(0,2048).Select(i=>(byte)(i%251)).ToArray();string hash=Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();
            Run("Updater verifies downloaded bytes and reuses only a matching cache",()=>
            {
                using var http=new HttpClient(new DownloadHandler(payload));var release=UpdateClient.ParseRelease(Json(url:"https://github.com/GEKSOD/CadBatch/releases/download/v0.5.0/CadBatch-Setup-0.5.0.exe",hash:hash));var client=new UpdateClient(http);string file=client.DownloadAsync(release,directory,null,CancellationToken.None).GetAwaiter().GetResult();Assert(File.ReadAllBytes(file).SequenceEqual(payload));
                File.WriteAllBytes(file,new byte[2048]);file=client.DownloadAsync(release,directory,null,CancellationToken.None).GetAwaiter().GetResult();Assert(File.ReadAllBytes(file).SequenceEqual(payload));
            });
            Run("Updater never promotes corrupt downloads to an installer",()=>
            {
                string bad=Path.Combine(directory,"bad");using var http=new HttpClient(new DownloadHandler(payload));var release=UpdateClient.ParseRelease(Json());Throws<InvalidDataException>(()=>new UpdateClient(http).DownloadAsync(release,bad,null,CancellationToken.None).GetAwaiter().GetResult());Assert(Directory.GetFiles(bad).Length==0);Directory.Delete(bad);
            });
            Run("Updater refuses a tampered plan with an external executable URL",()=>
            {
                using var http=new HttpClient(new DownloadHandler(payload));var release=UpdateClient.ParseRelease(Json(hash:hash));release.Download="https://example.com/installer.exe";Throws<InvalidDataException>(()=>new UpdateClient(http).DownloadAsync(release,directory,null,CancellationToken.None).GetAwaiter().GetResult());
            });
        }
        finally{foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}
    }
    private sealed class DownloadHandler : HttpMessageHandler
    {
        private readonly byte[] payload;public DownloadHandler(byte[] payload){this.payload=payload;}
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){RequestMessage=request,Content=new ByteArrayContent(payload)});
    }
}
