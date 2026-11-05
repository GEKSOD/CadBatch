using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CadBatch.Updates
{
    public sealed class UpdateRelease
    {
        public string Version {get;set;}="";
        public string Notes {get;set;}="";
        public string Page {get;set;}="";
        public string Download {get;set;}="";
        public string FileName {get;set;}="";
        public string Sha256 {get;set;}="";
        public long Size {get;set;}
    }
    public sealed class UpdateClient
    {
        public const string Repository="Geksod/CadBatch";
        public const string ReleasesPage="https://github.com/"+Repository+"/releases";
        public const string LatestApi="https://api.github.com/repos/"+Repository+"/releases/latest";
        private readonly HttpClient http;
        public UpdateClient(HttpClient http){this.http=http;}
        public static HttpClient CreateHttpClient()
        {
            var client=new HttpClient(new HttpClientHandler{AllowAutoRedirect=true}){Timeout=TimeSpan.FromMinutes(15)};
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CadBatch-Updater/1.0");client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");client.DefaultRequestHeaders.Add("X-GitHub-Api-Version","2022-11-28");return client;
        }
        public static bool IsNewer(string installed,string available)=>Parse(available)>Parse(installed);
        // Downloaded installers (about 85 MB each) of versions not newer than the running one: they are no longer needed.
        // Folders with other names are left alone. Returns the number of removed folders; never throws.
        public static int RemoveOldDownloads(string root,string currentVersion)
        {
            int removed=0;
            try
            {
                if(!Directory.Exists(root))return 0;
                foreach(string folder in Directory.GetDirectories(root))
                {
                    string name=Path.GetFileName(folder);
                    if(!Regex.IsMatch(name,@"^\d+\.\d+\.\d+(\.\d+)?$",RegexOptions.CultureInvariant)||IsNewer(currentVersion,name))continue;
                    if((new DirectoryInfo(folder).Attributes&FileAttributes.ReparsePoint)!=0)continue;
                    try{Directory.Delete(folder,true);removed++;}catch(IOException){}catch(UnauthorizedAccessException){}
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}catch(InvalidDataException){}
            return removed;
        }
        private static Version Parse(string version)
        {
            if(!Regex.IsMatch(version,@"^\d+\.\d+\.\d+(\.\d+)?$",RegexOptions.CultureInvariant)||!Version.TryParse(version,out var value))throw new InvalidDataException("Некорректная версия обновления.");return value;
        }
        public static UpdateRelease ParseRelease(string json)
        {
            using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
            if(root.GetProperty("draft").GetBoolean()||root.GetProperty("prerelease").GetBoolean())throw new InvalidDataException("Этот выпуск не предназначен для автоматического обновления.");
            string tag=root.GetProperty("tag_name").GetString()??"";if(!tag.StartsWith("v",StringComparison.Ordinal))throw new InvalidDataException("Некорректный тег выпуска.");string version=tag.Substring(1);Parse(version);
            string file="CadBatch-Setup-"+version+".exe",expected="https://github.com/"+Repository+"/releases/download/"+tag+"/"+file;
            var candidates=root.GetProperty("assets").EnumerateArray().Where(a=>a.GetProperty("name").GetString()==file).ToArray();if(candidates.Length!=1)throw new InvalidDataException("В выпуске отсутствует единственный установщик CAD Batch.");
            var asset=candidates[0];string url=asset.GetProperty("browser_download_url").GetString()??"",digest=asset.GetProperty("digest").GetString()??"";long size=asset.GetProperty("size").GetInt64();
            if(!url.Equals(expected,StringComparison.OrdinalIgnoreCase)||!digest.StartsWith("sha256:",StringComparison.Ordinal)||!Regex.IsMatch(digest.Substring(7),"^[a-fA-F0-9]{64}$")||size<1024||size>1024L*1024*1024)throw new InvalidDataException("Не удалось проверить происхождение установщика или его контрольную сумму.");
            return new UpdateRelease{Version=version,FileName=file,Download=url,Page=ReleasesPage+"/tag/"+tag,Sha256=digest.Substring(7).ToLowerInvariant(),Size=size,Notes=root.TryGetProperty("body",out var notes)?notes.GetString()??"":""};
        }
        public async Task<UpdateRelease?> LatestAsync(CancellationToken cancellation)
        {
            using var response=await http.GetAsync(LatestApi,cancellation).ConfigureAwait(false);if(response.StatusCode==HttpStatusCode.NotFound)return null;
            response.EnsureSuccessStatusCode();return ParseRelease(await response.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false));
        }
        public static async Task<bool> VerifyAsync(string path,UpdateRelease release,CancellationToken cancellation)
        {
            if(!File.Exists(path)||new FileInfo(path).Length!=release.Size)return false;
            using var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,81920,true);byte[] hash=await SHA256.HashDataAsync(file,cancellation).ConfigureAwait(false);
            return Convert.ToHexString(hash).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase);
        }
        public async Task<string> DownloadAsync(UpdateRelease release,string directory,IProgress<double>? progress,CancellationToken cancellation)
        {
            // Revalidate public fields; callers cannot redirect the updater to another executable/source.
            Parse(release.Version);string expected="https://github.com/"+Repository+"/releases/download/v"+release.Version+"/CadBatch-Setup-"+release.Version+".exe";
            if(!release.Download.Equals(expected,StringComparison.OrdinalIgnoreCase)||release.FileName!="CadBatch-Setup-"+release.Version+".exe"||!Regex.IsMatch(release.Sha256,"^[a-fA-F0-9]{64}$")||release.Size<1024||release.Size>1024L*1024*1024)throw new InvalidDataException("Некорректный план обновления.");
            Directory.CreateDirectory(directory);string path=Path.Combine(Path.GetFullPath(directory),release.FileName);if(await VerifyAsync(path,release,cancellation).ConfigureAwait(false))return path;
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".download";
            try
            {
                using(var response=await http.GetAsync(release.Download,HttpCompletionOption.ResponseHeadersRead,cancellation).ConfigureAwait(false))
                {
                    response.EnsureSuccessStatusCode();var final=response.RequestMessage?.RequestUri;
                    if(final==null||final.Scheme!="https"||!(final.Host=="github.com"||final.Host.EndsWith(".githubusercontent.com",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("Неожиданный сервер загрузки обновления.");
                    if(response.Content.Headers.ContentLength.HasValue&&response.Content.Headers.ContentLength.Value!=release.Size)throw new InvalidDataException("Размер установщика не совпадает с выпуском.");
                    using var input=await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);using var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);
                    byte[] buffer=new byte[81920];long total=0;int count;
                    while((count=await input.ReadAsync(buffer.AsMemory(),cancellation).ConfigureAwait(false))>0){total+=count;if(total>release.Size)throw new InvalidDataException("Получен лишний объём данных.");await output.WriteAsync(buffer.AsMemory(0,count),cancellation).ConfigureAwait(false);progress?.Report((double)total/release.Size);}
                    await output.FlushAsync(cancellation).ConfigureAwait(false);
                }
                if(!await VerifyAsync(temporary,release,cancellation).ConfigureAwait(false))throw new InvalidDataException("Контрольная сумма обновления не совпала. Установка не запущена.");
                File.Move(temporary,path,true);return path;
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
