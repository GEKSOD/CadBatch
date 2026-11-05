using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace CadBatch.Setup
{
    // Files of the previous version may be held for a moment by an antivirus scan, or for good by a forgotten
    // CAD Batch window or an old installer. The installer retries, then names the program that holds the files.
    internal sealed class FilesLockedException : IOException
    {
        public IReadOnlyList<Process> Holders {get;}
        public bool OnlyOurs=>Holders.Count>0&&Holders.All(p=>FileLocks.IsOurs(p));
        public FilesLockedException(string message,IReadOnlyList<Process> holders):base(message){Holders=holders;}
    }
    internal static class FileLocks
    {
        private static readonly string[] OurNames={"CadBatch","CadBatch.PdfWorker","CadBatch.Setup"};
        public static bool IsOurs(Process p){try{return OurNames.Contains(p.ProcessName,StringComparer.OrdinalIgnoreCase)&&p.Id!=Process.GetCurrentProcess().Id&&SameSession(p);}catch(InvalidOperationException){return false;}}
        // On a terminal server other users' processes are listed too; only this session's can be ours to close or wait for.
        public static bool SameSession(Process p){try{return p.SessionId==Process.GetCurrentProcess().SessionId;}catch(InvalidOperationException){return false;}catch(System.ComponentModel.Win32Exception){return false;}}
        // Runs a file system step; an antivirus or indexer usually lets go within a few seconds.
        // quick: a rename that fails a few times will not start working; the caller has another way.
        public static void Retry(string what,Action action,IEnumerable<string>? lockedPaths=null,bool quick=false)
        {
            int attempts=quick?6:16;
            for(int attempt=1;;attempt++)
            {
                try{action();return;}
                catch(Exception ex) when((ex is IOException||ex is UnauthorizedAccessException)&&!(ex is FilesLockedException)&&!(ex is FileNotFoundException)&&!(ex is DirectoryNotFoundException))
                {
                    if(attempt<attempts){Thread.Sleep(Math.Min(250*attempt,1500));continue;}
                    var holders=Holders(lockedPaths??Enumerable.Empty<string>());
                    SetupLog.Write(what+": "+ex);
                    if(holders.Count==0)throw new IOException(what+": файлы заняты другой программой. Чаще всего это антивирус, который проверяет новые файлы. Подождите минуту и нажмите «Установить» ещё раз.",ex);
                    throw new FilesLockedException(what+": файлы заняты программой "+string.Join(", ",holders.Select(Describe).Distinct())+".",holders);
                }
            }
        }
        public static string Describe(Process p){try{return "«"+(string.IsNullOrWhiteSpace(p.MainWindowTitle)?p.ProcessName:p.MainWindowTitle)+"»";}catch(InvalidOperationException){return "(завершена)";}}
        public static IEnumerable<string> FilesUnder(params string[] roots)
        {
            foreach(string root in roots)
            {
                if(File.Exists(root)){yield return root;continue;}
                if(!Directory.Exists(root))continue;
                foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))yield return file;
            }
        }
        // Windows Restart Manager: which processes hold these files open.
        public static List<Process> Holders(IEnumerable<string> paths)
        {
            var files=paths.Distinct(StringComparer.OrdinalIgnoreCase).Take(2000).ToArray();var result=new List<Process>();if(files.Length==0)return result;
            if(RmStartSession(out uint session,0,new StringBuilder(64))!=0)return result;
            try
            {
                if(RmRegisterResources(session,(uint)files.Length,files,0,null,0,null)!=0)return result;
                uint needed=0,count=0,reasons=0;int code=RmGetList(session,out needed,ref count,null,ref reasons);
                if(code==234&&needed>0)
                {
                    var info=new RM_PROCESS_INFO[needed];count=needed;
                    if(RmGetList(session,out needed,ref count,info,ref reasons)==0)
                        foreach(var item in info.Take((int)count))try{result.Add(Process.GetProcessById(item.Process.dwProcessId));}catch(ArgumentException){}
                }
            }
            finally{RmEndSession(session);}
            return result;
        }
        [StructLayout(LayoutKind.Sequential)] private struct RM_UNIQUE_PROCESS {public int dwProcessId;public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct RM_PROCESS_INFO
        {
            public RM_UNIQUE_PROCESS Process;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)] public string strAppName;
            [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)] public string strServiceShortName;
            public int ApplicationType;public uint AppStatus;public uint TSSessionId;[MarshalAs(UnmanagedType.Bool)] public bool bRestartable;
        }
        [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] private static extern int RmStartSession(out uint pSessionHandle,int dwSessionFlags,StringBuilder strSessionKey);
        [DllImport("rstrtmgr.dll")] private static extern int RmEndSession(uint pSessionHandle);
        [DllImport("rstrtmgr.dll",CharSet=CharSet.Unicode)] private static extern int RmRegisterResources(uint pSessionHandle,uint nFiles,string[] rgsFilenames,uint nApplications,RM_UNIQUE_PROCESS[]? rgApplications,uint nServices,string[]? rgsServiceNames);
        [DllImport("rstrtmgr.dll")] private static extern int RmGetList(uint dwSessionHandle,out uint pnProcInfoNeeded,ref uint pnProcInfo,[In,Out] RM_PROCESS_INFO[]? rgAffectedApps,ref uint lpdwRebootReasons);
    }
    internal static class SetupLog
    {
        public static void Write(string text)
        {
            try
            {
                string folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","logs");Directory.CreateDirectory(folder);
                File.AppendAllText(Path.Combine(folder,"setup-"+DateTime.Now.ToString("yyyyMMdd")+".log"),DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")+" "+text+Environment.NewLine+Environment.NewLine,Encoding.UTF8);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
