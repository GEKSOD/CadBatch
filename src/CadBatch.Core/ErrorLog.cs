using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CadBatch.Core
{
    // One journal for the application and the AutoCAD component: every unexpected error is written to a daily file,
    // so a user who cannot describe the problem can still send it. Writing never throws.
    public static class ErrorLog
    {
        private static readonly object gate=new object();
        public static string Folder {get;set;}=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","logs");
        private const int KeepDays=14;
        public static void Write(string area,Exception error)=>Write(area,error.GetType().Name+": "+error.Message+Environment.NewLine+error);
        public static void Write(string area,string text)
        {
            try
            {
                lock(gate)
                {
                    Directory.CreateDirectory(Folder);
                    string path=Path.Combine(Folder,"cadbatch-"+DateTime.Now.ToString("yyyyMMdd",CultureInfo.InvariantCulture)+".log");bool fresh=!File.Exists(path);
                    File.AppendAllText(path,DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff",CultureInfo.InvariantCulture)+" ["+ProductInfo.Version+" · "+Process()+"] "+area+Environment.NewLine+text.Trim()+Environment.NewLine+Environment.NewLine,Encoding.UTF8);
                    if(fresh)foreach(var old in new DirectoryInfo(Folder).GetFiles("cadbatch-*.log").Where(f=>f.LastWriteTime<DateTime.Now.AddDays(-KeepDays)))old.Delete();
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
        private static string Process(){try{using(var p=System.Diagnostics.Process.GetCurrentProcess())return p.ProcessName;}catch(InvalidOperationException){return "?";}}
        public static IEnumerable<string> Files()=>Directory.Exists(Folder)?Directory.GetFiles(Folder,"cadbatch-*.log").OrderByDescending(f=>f,StringComparer.Ordinal):Enumerable.Empty<string>();

        // "Собрать диагностику": one ZIP with versions, the session notes, recent journals and settings. No drawings are included.
        public static void Collect(string zipPath,string summary,string session,IEnumerable<string> settingsFiles)
        {
            string full=Path.GetFullPath(zipPath);Directory.CreateDirectory(Path.GetDirectoryName(full)!);string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var file=File.Create(temp))using(var zip=new ZipArchive(file,ZipArchiveMode.Create))
                {
                    Text(zip,"Сведения.txt",summary);Text(zip,"Сеанс.txt",session.Length==0?"Замечаний в этом сеансе нет.":session);
                    foreach(var log in Files().Take(KeepDays))Copy(zip,log,"Журнал/"+Path.GetFileName(log));
                    foreach(var setting in settingsFiles.Where(File.Exists))Copy(zip,setting,"Настройки/"+Path.GetFileName(setting));
                }
                if(File.Exists(full))File.Delete(full);File.Move(temp,full);
            }
            finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public static string Summary(string host)
        {
            var text=new StringBuilder();
            text.AppendLine("CAD Batch "+ProductInfo.Version).AppendLine("Подключение: "+host).AppendLine("Windows: "+Environment.OSVersion.VersionString+(Environment.Is64BitOperatingSystem?" (64-bit)":""));
            text.AppendLine(".NET: "+Environment.Version+" · процесс "+Process()+(Environment.Is64BitProcess?" (64-bit)":"")).AppendLine("Язык: "+CultureInfo.CurrentUICulture.Name+" · время: "+DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss",CultureInfo.InvariantCulture));
            return text.ToString();
        }
        private static void Text(ZipArchive zip,string name,string text){using(var writer=new StreamWriter(zip.CreateEntry(name).Open(),new UTF8Encoding(true)))writer.Write(text);}
        // Logs may be open for appending by another process: read with sharing.
        private static void Copy(ZipArchive zip,string path,string name)
        {
            try{using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var output=zip.CreateEntry(name).Open())input.CopyTo(output);}
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
