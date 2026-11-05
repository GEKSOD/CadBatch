using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace CadBatch.Setup
{
    internal static class Program
    {
        private const string Product="CadBatch.Desktop";
        private static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","CadBatch");
        private static readonly string PluginRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","ApplicationPlugins");
        private static string Version=>Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
        [STAThread] private static int Main(string[] args)
        {
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                if(args.Length==2&&args[0]=="--smoke"){Directory.CreateDirectory(args[1]);Extract(args[1]);File.WriteAllText(Path.Combine(args[1],"setup-smoke.txt"),"Installer payload verified; no shortcuts/registry/user installation modified.");return 0;}
                if(args.Contains("--uninstall")){Uninstall();return 0;}
                if(args.Length==2&&args[0]=="--locks-smoke")return LocksSmoke(args[1]);
                int wait=Array.IndexOf(args,"--wait-pid");if(wait>=0&&wait+1<args.Length&&int.TryParse(args[wait+1],out int pid))try{using(var process=Process.GetProcessById(pid))if(!process.WaitForExit(60000))throw new InvalidOperationException("Закройте CAD Batch перед обновлением.");}catch(ArgumentException){}
                Application.Run(new InstallerForm(args.Contains("--update"),args.Contains("--launch")));return 0;
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"Установка CAD Batch",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
        // Self-check of the busy-file handling: a folder with a file held open must not be moved, and the holder is named.
        private static int LocksSmoke(string folder)
        {
            string busy=Path.Combine(folder,"busy"),file=Path.Combine(busy,"held.dll");Directory.CreateDirectory(busy);
            using(var held=new FileStream(file,FileMode.Create,FileAccess.ReadWrite,FileShare.None))
                try{FileLocks.Retry("Проверка",()=>Directory.Move(busy,busy+"-moved"),FileLocks.FilesUnder(busy));}
                catch(FilesLockedException ex)when(ex.Holders.Any(p=>p.Id==Process.GetCurrentProcess().Id)){File.AppendAllText(Path.Combine(folder,"locks.txt"),"SETUP LOCKS PASS: busy file detected, holder named: "+ex.Message+Environment.NewLine);}
            if(!File.Exists(Path.Combine(folder,"locks.txt")))return 1;
            // A folder that cannot be renamed although its files are free (here: it is the working folder of this process).
            string target=Path.Combine(folder,"app"),source=Path.Combine(folder,"new"),backup=Path.Combine(folder,"app-backup");
            Directory.CreateDirectory(Path.Combine(target,"Help"));Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(target,"a.txt"),"old");File.WriteAllText(Path.Combine(target,"obsolete.txt"),"old");File.WriteAllText(Path.Combine(source,"a.txt"),"new");File.WriteAllText(Path.Combine(source,"b.txt"),"new");
            string previous=Environment.CurrentDirectory;Environment.CurrentDirectory=target;
            try
            {
                bool renamable=true;try{Directory.Move(target,target+"-x");Directory.Move(target+"-x",target);}catch(IOException){renamable=false;}
                if(renamable)return 2;
                var swap=new Swap("Проверка папки",target,source,backup,folder);swap.Run();
                if(File.ReadAllText(Path.Combine(target,"a.txt"))!="new"||!File.Exists(Path.Combine(target,"b.txt"))||File.Exists(Path.Combine(target,"obsolete.txt"))||!File.Exists(Path.Combine(backup,"a.txt")))return 3;
                swap.Undo();if(File.ReadAllText(Path.Combine(target,"a.txt"))!="old"||File.Exists(Path.Combine(target,"b.txt"))||!File.Exists(Path.Combine(target,"obsolete.txt")))return 4;
            }
            finally{Environment.CurrentDirectory=previous;}
            File.AppendAllText(Path.Combine(folder,"locks.txt"),"SETUP LOCKS PASS: a folder that cannot be renamed is updated in place and restored on undo."+Environment.NewLine);
            // A retried copy meets the files of the first attempt: it must overwrite them, not fail with "file exists".
            string copySource=Path.Combine(folder,"copy-src"),copyTarget=Path.Combine(folder,"copy-dst");Directory.CreateDirectory(Path.Combine(copySource,"Help"));
            File.WriteAllText(Path.Combine(copySource,"a.dll"),"1");File.WriteAllText(Path.Combine(copySource,"Help","b.txt"),"1");
            CopyTree(copySource,copyTarget);File.WriteAllText(Path.Combine(copySource,"a.dll"),"2");CopyTree(copySource,copyTarget);
            if(File.ReadAllText(Path.Combine(copyTarget,"a.dll"))!="2")return 5;
            if(!FileLocks.SameSession(Process.GetCurrentProcess()))return 6;
            File.AppendAllText(Path.Combine(folder,"locks.txt"),"SETUP LOCKS PASS: a repeated copy overwrites the files of an interrupted attempt; only this session's processes count."+Environment.NewLine);
            return 0;
        }
        private static string Child(string root,string relative)
        {
            string full=Path.GetFullPath(Path.Combine(root,relative)),prefix=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Недопустимый путь установочного пакета.");return full;
        }
        private static void NoLinks(string path)
        {
            var info=new DirectoryInfo(path);while(info!=null){if(info.Exists&&(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("Установка в каталог со ссылкой запрещена: "+info.FullName);info=info.Parent;}
        }
        private static void Extract(string destination)
        {
            NoLinks(destination);using(var data=Assembly.GetExecutingAssembly().GetManifestResourceStream("CadBatch.Payload.zip")??throw new InvalidDataException("В установщике отсутствуют файлы приложения."))using(var archive=new ZipArchive(data,ZipArchiveMode.Read))
            {
                if(archive.Entries.Count>3000)throw new InvalidDataException("Некорректный пакет.");var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long bytes=0;
                foreach(var entry in archive.Entries)
                {
                    string name=entry.FullName.Replace('/',Path.DirectorySeparatorChar);if(name.EndsWith(Path.DirectorySeparatorChar.ToString()))continue;
                    var parts=name.Split(Path.DirectorySeparatorChar);if(parts.Any(p=>p.Length==0||p=="."||p==".."||p.EndsWith(".")||p.EndsWith(" ")||p.IndexOfAny(Path.GetInvalidFileNameChars())>=0)||!names.Add(name)||(entry.ExternalAttributes&0x400)!=0||((entry.ExternalAttributes>>16)&0xf000)==0xa000)throw new InvalidDataException("Некорректное имя файла в установщике.");
                    if(!new[]{"App","CadBatch.bundle","docs","product.json","SHA256SUMS.txt"}.Contains(parts[0]))throw new InvalidDataException("Неизвестный компонент установщика.");
                    bytes+=entry.Length;if(bytes>2L*1024*1024*1024)throw new InvalidDataException("Слишком большой пакет.");
                    string target=Child(destination,name);Directory.CreateDirectory(Path.GetDirectoryName(target)!);using(var input=entry.Open())using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
                }
            }
            string manifest=Child(destination,"SHA256SUMS.txt");if(!File.Exists(manifest))throw new InvalidDataException("Нет контрольных сумм установщика.");var verified=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(string row in File.ReadAllLines(manifest))
            {
                int split=row.IndexOf("  ",StringComparison.Ordinal);if(split!=64)throw new InvalidDataException("Повреждён список контрольных сумм.");string relative=row.Substring(split+2).Replace('/',Path.DirectorySeparatorChar),file=Child(destination,relative);
                if(!verified.Add(file)||!File.Exists(file))throw new InvalidDataException("Файл установщика не найден.");
                using(var input=File.OpenRead(file))using(var hash=SHA256.Create())if(BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=row.Substring(0,64))throw new InvalidDataException("Повреждён установщик: "+relative);
            }
            if(Directory.GetFiles(destination,"*",SearchOption.AllDirectories).Length!=verified.Count+1||!File.Exists(Child(destination,"App\\CadBatch.exe"))||!File.Exists(Child(destination,"CadBatch.bundle\\PackageContents.xml")))throw new InvalidDataException("Неполный установочный пакет.");
        }
        private static void EnsureClosed()
        {
            // Only this user's session counts: on a terminal server a colleague's AutoCAD or CAD Batch is not ours to wait for.
            if(Process.GetProcessesByName("acad").Any(FileLocks.SameSession))throw new InvalidOperationException("Сохраните чертежи и полностью закройте AutoCAD, затем повторите установку. Программа не закрывает AutoCAD принудительно.");
            // Forgotten parts of CAD Batch (a hidden window, the PDF worker, an old installer window) can be closed for the user.
            var ours=new[]{"CadBatch","CadBatch.PdfWorker","CadBatch.Setup"}.SelectMany(Process.GetProcessesByName).Where(FileLocks.IsOurs).ToList();
            if(ours.Count>0)throw new FilesLockedException("Ещё запущены части CAD Batch: "+string.Join(", ",ours.Select(FileLocks.Describe).Distinct())+".",ours);
        }
        private static void CopyTree(string source,string destination)
        {
            NoLinks(source);NoLinks(destination);Directory.CreateDirectory(destination);foreach(string file in Directory.GetFiles(source))File.Copy(file,Child(destination,Path.GetFileName(file)),true);
            // Overwrite: a retried copy (after an antivirus held a file) meets the files the first attempt already copied.
            foreach(string directory in Directory.GetDirectories(source))CopyTree(directory,Child(destination,Path.GetFileName(directory)));
        }
        private static void Install(IProgress<string> progress)
        {
            EnsureClosed();NoLinks(Root);NoLinks(PluginRoot);Directory.CreateDirectory(Root);Directory.CreateDirectory(PluginRoot);
            string marker=Child(Root,"product.id");if(Directory.Exists(Child(Root,"Current"))&&!File.Exists(marker))throw new IOException("Каталог занят другой установкой. Выберите чистую установку CAD Batch.");if(File.Exists(marker)&&File.ReadAllText(marker)!=Product)throw new IOException("В каталоге установлена другая программа.");
            string stage=Child(Root,"stage-"+Guid.NewGuid().ToString("N")),current=Child(Root,"Current"),backup=Child(Root,"backup-"+Guid.NewGuid().ToString("N")),bundle=Child(PluginRoot,"CadBatch.bundle"),pluginStage=Child(PluginRoot,"CadBatch.stage-"+Guid.NewGuid().ToString("N")),pluginBackup=Child(PluginRoot,"CadBatch.backup-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);Swap? app=null,plugin=null;
            try
            {
                progress.Report("Проверка файлов приложения…");Extract(stage);EnsureClosed();FileLocks.Retry("Подготовка компонента AutoCAD",()=>CopyTree(Child(stage,"CadBatch.bundle"),pluginStage));
                progress.Report("Установка CAD Batch…");app=new Swap("Замена программы",current,Child(stage,"App"),backup,Root);app.Run();
                progress.Report("Установка компонента AutoCAD…");plugin=new Swap("Замена компонента AutoCAD",bundle,pluginStage,pluginBackup,PluginRoot);plugin.Run();
                string docs=Child(current,"Help");CopyTree(Child(stage,"docs"),docs);
                File.WriteAllText(marker,Product);string maintenance=Child(Root,"CadBatch.Setup.exe");if(!Path.GetFullPath(Application.ExecutablePath).Equals(maintenance,StringComparison.OrdinalIgnoreCase))
                    // The maintenance copy only serves updates and removal; if it is busy, the previous copy keeps working.
                    try{FileLocks.Retry("Служебная копия установщика",()=>File.Copy(Application.ExecutablePath,maintenance,true),new[]{maintenance});}catch(IOException ex){SetupLog.Write("Служебная копия не обновлена: "+ex.Message);}
                progress.Report("Создание ярлыков…");CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"CAD Batch.lnk"),Child(current,"CadBatch.exe"));CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"CAD Batch.lnk"),Child(current,"CadBatch.exe"));
                using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CadBatch"))
                {key.SetValue("DisplayName","CAD Batch");key.SetValue("DisplayVersion",Version);key.SetValue("Publisher","Geksod");key.SetValue("InstallLocation",Root);key.SetValue("UninstallString","\""+maintenance+"\" --uninstall");key.SetValue("DisplayIcon",Child(current,"CadBatch.exe"));key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}
                progress.Report("CAD Batch установлен. Откройте AutoCAD, затем подключитесь из приложения.");
            }
            catch
            {
                // Each part goes back to the previous version; a failure here is logged and never hides the original error.
                foreach(var swap in new[]{plugin,app})try{swap?.Undo();}catch(Exception undo){SetupLog.Write("Откат: "+undo);}
                throw;
            }
            finally{if(Directory.Exists(stage))DeleteOwn(stage,Root);if(Directory.Exists(pluginStage))DeleteOwn(pluginStage,PluginRoot);}
            // The new version is in place: the previous copy and leftovers of interrupted runs are no longer needed.
            RemoveLeftovers();
        }
        // Puts a new folder in place of the old one. Renaming is quick and atomic, but Windows refuses to rename a folder that
        // another program watches (Autodesk services, antivirus, sync tools) even when every file in it is free — and such a
        // watcher is invisible to Restart Manager. Then the files are updated in place, with a copy of the old ones for undo.
        private sealed class Swap
        {
            private readonly string what,target,source,backup,parent;private bool renamed,inPlace,touched;
            public Swap(string what,string target,string source,string backup,string parent){this.what=what;this.target=target;this.source=source;this.backup=backup;this.parent=parent;}
            public void Run()
            {
                if(Directory.Exists(target))
                {
                    NoLinks(target);
                    try{FileLocks.Retry(what,()=>Directory.Move(target,backup),FileLocks.FilesUnder(target),true);renamed=true;}
                    catch(IOException ex) when(!(ex is FilesLockedException locked&&locked.OnlyOurs))
                    {
                        SetupLog.Write(what+": папку нельзя переименовать, файлы обновляются на месте. "+ex.Message);
                        if(Directory.Exists(backup))DeleteOwn(backup,parent);FileLocks.Retry(what,()=>CopyTree(target,backup));inPlace=true;
                    }
                }
                touched=true;
                if(inPlace){FileLocks.Retry(what,()=>OverwriteTree(source,target),FileLocks.FilesUnder(target));RemoveExtra(source,target);return;}
                try{FileLocks.Retry(what,()=>Directory.Move(source,target),FileLocks.FilesUnder(source),true);}
                catch(IOException ex) when(!(ex is FilesLockedException))
                {
                    // A freshly extracted folder may still be held by an antivirus scan: copying works where renaming does not.
                    SetupLog.Write(what+": новую папку нельзя переместить, файлы копируются. "+ex.Message);
                    if(Directory.Exists(target))DeleteOwn(target,parent);FileLocks.Retry(what,()=>CopyTree(source,target));
                }
            }
            public void Undo()
            {
                if(!touched&&!renamed)return;
                if(inPlace){if(Directory.Exists(backup)){OverwriteTree(backup,target);RemoveExtra(backup,target);}return;}
                if(Directory.Exists(target))DeleteOwn(target,parent);
                if(renamed&&Directory.Exists(backup))try{Directory.Move(backup,target);}catch(IOException){CopyTree(backup,target);}
            }
        }
        private static void OverwriteTree(string source,string destination)
        {
            NoLinks(source);NoLinks(destination);Directory.CreateDirectory(destination);
            foreach(string file in Directory.GetFiles(source))File.Copy(file,Child(destination,Path.GetFileName(file)),true);
            foreach(string directory in Directory.GetDirectories(source))OverwriteTree(directory,Child(destination,Path.GetFileName(directory)));
        }
        // Files of the old version that the new one no longer has; best effort.
        private static void RemoveExtra(string source,string destination)
        {
            foreach(string file in Directory.GetFiles(destination,"*",SearchOption.AllDirectories))
            {
                string relative=file.Substring(destination.TrimEnd('\\').Length+1);
                if(!File.Exists(Path.Combine(source,relative)))try{File.Delete(file);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
        }
        // Best effort: a locked file only keeps its folder until the next update, it never fails the installation.
        private static void RemoveLeftovers()
        {
            foreach(var (parent,pattern) in new[]{(Root,"backup-*"),(Root,"stage-*"),(PluginRoot,"CadBatch.backup-*"),(PluginRoot,"CadBatch.stage-*")})
            {
                if(!Directory.Exists(parent))continue;
                foreach(string folder in Directory.GetDirectories(parent,pattern))try{DeleteOwn(folder,parent);}catch(IOException){}catch(UnauthorizedAccessException){}
            }
            // Downloaded installers: the installed version and older ones are no longer needed (the running one is kept).
            string updates=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","Updates");
            if(Directory.Exists(updates))
                foreach(string folder in Directory.GetDirectories(updates))
                {
                    if(Path.GetFullPath(Application.ExecutablePath).StartsWith(Path.GetFullPath(folder).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))continue;
                    try{DeleteOwn(folder,updates);}catch(IOException){}catch(UnauthorizedAccessException){}
                }
        }
        private static void DeleteOwn(string directory,string parent)
        {
            string full=Path.GetFullPath(directory);if(!full.StartsWith(Path.GetFullPath(parent).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Небезопасный путь очистки.");
            foreach(string sub in Directory.GetDirectories(full,"*",SearchOption.AllDirectories))NoLinks(sub);NoLinks(full);Directory.Delete(full,true);
        }
        private static void CreateShortcut(string path,string executable)
        {
            object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!);try{dynamic shortcut=((dynamic)shell).CreateShortcut(path);shortcut.TargetPath=executable;shortcut.WorkingDirectory=Path.GetDirectoryName(executable);shortcut.Description="CAD Batch — пакетный вывод DWG в PDF";shortcut.Save();System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);}finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        }
        private static void Uninstall()
        {
            EnsureClosed();NoLinks(Root);if(!File.Exists(Child(Root,"product.id"))||File.ReadAllText(Child(Root,"product.id"))!=Product)throw new IOException("Не найдена установка CAD Batch.");
            if(MessageBox.Show("Удалить CAD Batch и компонент AutoCAD? Настройки пользователя и созданные PDF сохранятся.","Удаление CAD Batch",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
            string bundle=Child(PluginRoot,"CadBatch.bundle");if(Directory.Exists(bundle))DeleteOwn(bundle,PluginRoot);RemoveLeftovers();
            foreach(string folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.Programs),Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)}){string shortcut=Path.Combine(folder,"CAD Batch.lnk");if(File.Exists(shortcut))File.Delete(shortcut);}
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CadBatch",false);
            string safe=Root.Replace("'","''"),script="$p="+Process.GetCurrentProcess().Id+"; while(Get-Process -Id $p -ErrorAction SilentlyContinue){Start-Sleep -Milliseconds 200}; $r='"+safe+"'; if((Get-Content -LiteralPath (Join-Path $r 'product.id') -Raw) -eq 'CadBatch.Desktop'){Remove-Item -LiteralPath $r -Recurse -Force}";
            Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(script))){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});
        }
        private sealed class InstallerForm : Form
        {
            private readonly Button install=new Button(),launch=new Button();private readonly Label status=new Label();private bool busy;
            public InstallerForm(bool automatic,bool autoLaunch)
            {
                Text="CAD Batch "+Version+" • Установка";try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch(ArgumentException){}Size=new Size(630,350);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",11);BackColor=Color.White;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
                Controls.Add(new Label{Text="CAD Batch",Font=new Font("Segoe UI",24,FontStyle.Bold),AutoSize=true,Location=new Point(25,22)});
                Controls.Add(new Label{Text="Пакетный вывод DWG в PDF\nУстановка для текущего пользователя, без прав администратора.\n\nКаталог: "+Root,Location=new Point(28,82),Size=new Size(560,100)});
                status.SetBounds(28,190,560,50);Controls.Add(status);install.Text="Установить";install.SetBounds(28,251,230,40);install.BackColor=Color.FromArgb(0,113,227);install.ForeColor=Color.White;Controls.Add(install);
                launch.Text="Запустить CAD Batch";launch.SetBounds(275,251,285,40);launch.Enabled=false;Controls.Add(launch);launch.Click+=(s,e)=>{Process.Start(new ProcessStartInfo(Child(Root,"Current\\CadBatch.exe")){UseShellExecute=true});Close();};
                install.Click+=async(s,e)=>await Run(autoLaunch);FormClosing+=(s,e)=>e.Cancel=busy;
                if(automatic)Shown+=async(s,e)=>await Run(autoLaunch);
            }
            private async Task Run(bool autoLaunch)
            {
                if(busy)return;busy=true;install.Enabled=false;
                try{await Task.Run(()=>Install(new Progress<string>(text=>{if(!IsDisposed&&IsHandleCreated)try{BeginInvoke(new Action(()=>status.Text=text));}catch(InvalidOperationException){}})));launch.Enabled=true;if(autoLaunch){Process.Start(new ProcessStartInfo(Child(Root,"Current\\CadBatch.exe")){UseShellExecute=true});busy=false;Close();}}
                catch(FilesLockedException ex) when(ex.OnlyOurs)
                {
                    SetupLog.Write(ex.Message);busy=false;
                    if(MessageBox.Show(this,ex.Message+"\n\nЗакрыть их и продолжить установку?","Установка CAD Batch",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes)
                    {
                        foreach(var process in ex.Holders)try{if(!process.HasExited){process.Kill();process.WaitForExit(5000);}}catch(Exception){}
                        await Run(autoLaunch);return;
                    }
                    status.Text="Закройте CAD Batch и нажмите «Установить» ещё раз.";install.Enabled=true;
                }
                catch(Exception ex)
                {
                    SetupLog.Write(ex.ToString());
                    string text=ex.Message+(ex is FilesLockedException?"\n\nЗакройте эту программу и нажмите «Установить» ещё раз.":"")+"\n\nПодробности: %LOCALAPPDATA%\\CadBatch\\logs";
                    status.Text=ex.Message;MessageBox.Show(this,text,"Установка CAD Batch",MessageBoxButtons.OK,MessageBoxIcon.Warning);install.Enabled=true;
                }
                finally{busy=false;}
            }
        }
    }
}
