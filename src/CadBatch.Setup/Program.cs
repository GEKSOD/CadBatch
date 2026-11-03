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
                int wait=Array.IndexOf(args,"--wait-pid");if(wait>=0&&wait+1<args.Length&&int.TryParse(args[wait+1],out int pid))try{using(var process=Process.GetProcessById(pid))if(!process.WaitForExit(60000))throw new InvalidOperationException("Закройте CAD Batch перед обновлением.");}catch(ArgumentException){}
                Application.Run(new InstallerForm(args.Contains("--update"),args.Contains("--launch")));return 0;
            }
            catch(Exception ex){MessageBox.Show(ex.Message,"Установка CAD Batch",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
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
            if(Process.GetProcessesByName("acad").Length>0)throw new InvalidOperationException("Сохраните чертежи и полностью закройте AutoCAD, затем повторите установку. Программа не закрывает AutoCAD принудительно.");
            if(Process.GetProcessesByName("CadBatch").Length>0)throw new InvalidOperationException("Закройте приложение CAD Batch и повторите установку.");
        }
        private static void CopyTree(string source,string destination)
        {
            NoLinks(source);NoLinks(destination);Directory.CreateDirectory(destination);foreach(string file in Directory.GetFiles(source))File.Copy(file,Child(destination,Path.GetFileName(file)),false);
            foreach(string directory in Directory.GetDirectories(source))CopyTree(directory,Child(destination,Path.GetFileName(directory)));
        }
        private static void Install(IProgress<string> progress)
        {
            EnsureClosed();NoLinks(Root);NoLinks(PluginRoot);Directory.CreateDirectory(Root);Directory.CreateDirectory(PluginRoot);
            string marker=Child(Root,"product.id");if(Directory.Exists(Child(Root,"Current"))&&!File.Exists(marker))throw new IOException("Каталог занят другой установкой. Выберите чистую установку CAD Batch.");if(File.Exists(marker)&&File.ReadAllText(marker)!=Product)throw new IOException("В каталоге установлена другая программа.");
            string stage=Child(Root,"stage-"+Guid.NewGuid().ToString("N")),current=Child(Root,"Current"),backup=Child(Root,"backup-"+Guid.NewGuid().ToString("N")),bundle=Child(PluginRoot,"CadBatch.bundle"),pluginStage=Child(PluginRoot,"CadBatch.stage-"+Guid.NewGuid().ToString("N")),pluginBackup=Child(PluginRoot,"CadBatch.backup-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);bool oldApp=false,newApp=false,oldPlugin=false,newPlugin=false;
            try
            {
                progress.Report("Проверка файлов приложения…");Extract(stage);EnsureClosed();CopyTree(Child(stage,"CadBatch.bundle"),pluginStage);
                progress.Report("Установка CAD Batch…");if(Directory.Exists(current)){NoLinks(current);Directory.Move(current,backup);oldApp=true;}Directory.Move(Child(stage,"App"),current);newApp=true;
                if(Directory.Exists(bundle)){NoLinks(bundle);Directory.Move(bundle,pluginBackup);oldPlugin=true;}Directory.Move(pluginStage,bundle);newPlugin=true;
                string docs=Child(current,"Help");CopyTree(Child(stage,"docs"),docs);
                File.WriteAllText(marker,Product);string maintenance=Child(Root,"CadBatch.Setup.exe");if(!Path.GetFullPath(Application.ExecutablePath).Equals(maintenance,StringComparison.OrdinalIgnoreCase))File.Copy(Application.ExecutablePath,maintenance,true);
                progress.Report("Создание ярлыков…");CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"CAD Batch.lnk"),Child(current,"CadBatch.exe"));CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"CAD Batch.lnk"),Child(current,"CadBatch.exe"));
                using(var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CadBatch"))
                {key.SetValue("DisplayName","CAD Batch");key.SetValue("DisplayVersion",Version);key.SetValue("Publisher","Geksod");key.SetValue("InstallLocation",Root);key.SetValue("UninstallString","\""+maintenance+"\" --uninstall");key.SetValue("DisplayIcon",Child(current,"CadBatch.exe"));key.SetValue("NoModify",1);key.SetValue("NoRepair",1);}
                progress.Report("CAD Batch установлен. Откройте AutoCAD, затем подключитесь из приложения.");
            }
            catch
            {
                if(newPlugin&&Directory.Exists(bundle))DeleteOwn(bundle,PluginRoot);if(oldPlugin&&Directory.Exists(pluginBackup))Directory.Move(pluginBackup,bundle);
                if(newApp&&Directory.Exists(current))DeleteOwn(current,Root);if(oldApp&&Directory.Exists(backup))Directory.Move(backup,current);throw;
            }
            finally{if(Directory.Exists(stage))DeleteOwn(stage,Root);if(Directory.Exists(pluginStage))DeleteOwn(pluginStage,PluginRoot);}
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
            string bundle=Child(PluginRoot,"CadBatch.bundle");if(Directory.Exists(bundle))DeleteOwn(bundle,PluginRoot);
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
                Text="CAD Batch "+Version+" • Установка";Size=new Size(630,350);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Segoe UI",11);BackColor=Color.White;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;
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
                catch(Exception ex){status.Text=ex.Message;MessageBox.Show(this,ex.Message,"Установка CAD Batch",MessageBoxButtons.OK,MessageBoxIcon.Warning);install.Enabled=true;}
                finally{busy=false;}
            }
        }
    }
}
