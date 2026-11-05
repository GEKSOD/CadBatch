using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using CadBatch.Core;
using CadBatch.UI;
using CadBatch.Updates;
using CadBatch.Transport;

namespace CadBatch.Desktop
{
    internal static class DesktopUi
    {
        public static void OpenUpdates(Window owner,bool busy=false){var w=new UpdateWindow(busy){Owner=owner};w.ShowDialog();}
        public static IntPtr Handle(Window window)=>new WindowInteropHelper(window).Handle;
        public static FrameworkElement AppIcon(double size)=>UiTheme.Logo(size);
        // Follows the text size chosen in the main window: the whole window grows, not only the letters.
        public static void Scale(Window window)
        {
            double scale=UiPreferences.Load(UiPreferences.DefaultPath).TextScale;ModernDialog.TextScale=scale;if(scale<1.001)return;
            if(window.Content is FrameworkElement content)content.LayoutTransform=new ScaleTransform(scale,scale);
            window.Width=Math.Min(window.Width*scale,SystemParameters.WorkArea.Width-20);window.Height=Math.Min(window.Height*scale,SystemParameters.WorkArea.Height-20);window.MinWidth=Math.Min(window.MinWidth*scale,window.Width);window.MinHeight=Math.Min(window.MinHeight*scale,window.Height);
        }
        public static void Open(string target){try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}catch(Exception ex){ModernDialog.Message(IntPtr.Zero,"Не удалось открыть",ex.Message,DialogKind.Warning);}}
        public static void OpenGuide(){string file=Path.Combine(AppContext.BaseDirectory,"Help","Start.html");Open(File.Exists(file)?file:UpdateClient.ReleasesPage);}
        // The main window opened from the lobby always gets the application menu: reconnect and updates.
        public static void ShowMain(Window lobby,IBatchHost host,string? settings=null)
        {
            var window=new BatchWindow.FluentWindow(host,settings);
            window.AddApplicationMenu(
                ()=>{if(!window.CanCloseForUpdate){ModernDialog.Message(Handle(window),"Подождите","Дождитесь завершения текущей операции.");return;}var start=new ConnectionWindow(false);Application.Current.MainWindow=start;start.Show();window.Close();},
                ()=>OpenUpdates(window,!window.CanCloseForUpdate));
            Application.Current.MainWindow=window;window.Show();
        }
    }
    internal sealed class ConnectionWindow : Window
    {
        private readonly ComboBox sessions=new ComboBox{DisplayMemberPath="Description",Margin=new Thickness(0,0,0,12)};
        private readonly TextBlock updateState=UiTheme.Hint("");
        private readonly CheckBox autoUpdates=new CheckBox{Content="Проверять обновления при запуске",FontSize=14};
        private readonly Button connect=UiTheme.Button("Подключиться",UiTheme.IconPlug,"Primary");
        private readonly StackPanel checklist=new StackPanel(),sessionArea=new StackPanel();
        private readonly DispatcherTimer poll=new DispatcherTimer{Interval=TimeSpan.FromSeconds(2)};
        private List<BridgeEndpoint> found=new List<BridgeEndpoint>();
        private string lastSignature="";
        private bool busy;
        private readonly string preferences=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","preferences.json");
        private static readonly string Bundle=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","ApplicationPlugins","CadBatch.bundle");
        private sealed class Preferences{public bool CheckUpdates {get;set;}=true;public bool AutoConnect {get;set;}=true;public bool ConnectedBefore {get;set;}}
        private Preferences settings=new Preferences();
        private readonly CheckBox autoConnect=new CheckBox{Content="Подключаться автоматически, если AutoCAD один",FontSize=14,Margin=new Thickness(0,12,0,0)};
        // Automatic connection only on a normal start, never when the person came back here to choose another AutoCAD.
        private readonly bool allowAuto;private bool autoTried;
        public ConnectionWindow(bool allowAuto=true)
        {
            this.allowAuto=allowAuto;
            Title="CAD Batch";Width=640;Height=800;MinWidth=540;MinHeight=600;WindowStartupLocation=WindowStartupLocation.CenterScreen;UiTheme.Apply(this);UiTheme.UseLogo(this);
            var stack=new StackPanel{Margin=new Thickness(36,36,36,24),MaxWidth=520};Content=new ScrollViewer{Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=UiTheme.Canvas};
            var icon=DesktopUi.AppIcon(68);icon.HorizontalAlignment=HorizontalAlignment.Center;icon.Margin=new Thickness(0,0,0,16);stack.Children.Add(icon);
            var title=UiTheme.Title("CAD Batch",30);title.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(title);
            var lead=UiTheme.Text("Чертежи AutoCAD — в готовые PDF за пару кликов",16,null,UiTheme.Secondary);lead.HorizontalAlignment=HorizontalAlignment.Center;lead.Margin=new Thickness(0,6,0,26);stack.Children.Add(lead);

            var card=new StackPanel();card.Children.Add(UiTheme.Title("Подключение к AutoCAD",21));card.Children.Add(UiTheme.Hint("Три шага — и можно работать. Окно само заметит, когда AutoCAD будет готов.",new Thickness(0,4,0,14)));
            card.Children.Add(checklist);card.Children.Add(sessionArea);
            connect.Height=52;connect.FontSize=16;connect.Margin=new Thickness(0,6,0,0);connect.Click+=async(s,e)=>await ConnectAsync();card.Children.Add(connect);card.Children.Add(autoConnect);
            stack.Children.Add(UiTheme.Card(card,22));

            var links=new WrapPanel{HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,2,0,18)};
            var demo=UiTheme.Button("Посмотреть демо",UiTheme.IconPlay,"Plain");demo.ToolTip="Интерфейс на примере, без AutoCAD";demo.Click+=(s,e)=>OpenDemo();links.Children.Add(demo);
            var guide=UiTheme.Button("Инструкция",UiTheme.IconHelp,"Plain");guide.Click+=(s,e)=>DesktopUi.OpenGuide();links.Children.Add(guide);
            var repo=UiTheme.Button("GitHub",UiTheme.IconLink,"Plain");repo.Click+=(s,e)=>DesktopUi.Open("https://github.com/"+UpdateClient.Repository);links.Children.Add(repo);stack.Children.Add(links);

            var updates=new DockPanel();var check=UiTheme.Button("Проверить",UiTheme.IconRefresh,"Outline");check.VerticalAlignment=VerticalAlignment.Center;check.Click+=(s,e)=>DesktopUi.OpenUpdates(this);DockPanel.SetDock(check,Dock.Right);updates.Children.Add(check);
            var info=new StackPanel{VerticalAlignment=VerticalAlignment.Center};info.Children.Add(UiTheme.Text("Версия "+ProductInfo.Version,15,FontWeights.SemiBold));updateState.Text="Обновления приходят из GitHub Releases.";updateState.Margin=new Thickness(0,2,0,6);info.Children.Add(updateState);info.Children.Add(autoUpdates);updates.Children.Add(info);
            stack.Children.Add(UiTheme.Card(updates,18));
            try{if(File.Exists(preferences))settings=System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferences))??new Preferences();}catch{settings=new Preferences();}
            autoUpdates.IsChecked=settings.CheckUpdates;autoConnect.IsChecked=settings.AutoConnect;
            autoUpdates.Click+=(s,e)=>{settings.CheckUpdates=autoUpdates.IsChecked==true;SavePreferences();};autoConnect.Click+=(s,e)=>{settings.AutoConnect=autoConnect.IsChecked==true;SavePreferences();};
            DesktopUi.Scale(this);
            sessions.SelectionChanged+=(s,e)=>UpdateConnect();
            poll.Tick+=(s,e)=>Refresh();
            Loaded+=async(s,e)=>{Refresh();poll.Start();if(autoUpdates.IsChecked==true)await BackgroundUpdateCheck();};
            Closing+=(s,e)=>e.Cancel=busy;Closed+=(s,e)=>poll.Stop();
        }
        private void Refresh()
        {
            if(busy)return;
            bool component=Directory.Exists(Bundle);
            bool running=CadRunning();
            found=RemoteHost.Discover();
            string signature=component+"|"+running+"|"+string.Join(";",found.Select(f=>f.Pipe+f.Description));
            if(signature==lastSignature)return;lastSignature=signature;
            var keep=(sessions.SelectedItem as BridgeEndpoint)?.Pipe;
            checklist.Children.Clear();
            checklist.Children.Add(Step(1,"Компонент для AutoCAD",component?"Установлен":"Нужен, чтобы AutoCAD мог работать с CAD Batch",component,component?null:("Установить",InstallComponent)));
            checklist.Children.Add(Step(2,"AutoCAD запущен",running?"Работает на этом компьютере":"Запустите полный AutoCAD 2021 или новее",running,running?null:("Запустить",LaunchCad)));
            string third=found.Count>0?(found.Count==1?"AutoCAD готов к работе":"Найдено копий AutoCAD: "+found.Count):running?"Если AutoCAD открыли до установки компонента — перезапустите его":"Появится автоматически после запуска AutoCAD";
            checklist.Children.Add(Step(3,"Связь с CAD Batch",third,found.Count>0,null,last:true));
            sessionArea.Children.Clear();
            if(found.Count>1){sessionArea.Children.Add(UiTheme.Hint("Выберите AutoCAD",new Thickness(2,4,0,6)));sessions.ItemsSource=found;sessions.SelectedItem=found.FirstOrDefault(f=>f.Pipe==keep)??found[0];sessionArea.Children.Add(sessions);}
            else{sessions.ItemsSource=found;sessions.SelectedIndex=found.Count>0?0:-1;if(found.Count==1){var one=UiTheme.Hint(found[0].Description,new Thickness(2,4,0,10));one.TextTrimming=TextTrimming.CharacterEllipsis;one.TextWrapping=TextWrapping.NoWrap;one.ToolTip=found[0].Description;sessionArea.Children.Add(one);}}
            UpdateConnect();
            if(allowAuto&&!autoTried&&settings.AutoConnect&&settings.ConnectedBefore&&found.Count==1){autoTried=true;Dispatcher.BeginInvoke(new Action(async()=>await ConnectAsync()));}
        }
        private void SavePreferences(){try{Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);File.WriteAllText(preferences,System.Text.Json.JsonSerializer.Serialize(settings));}catch(IOException){}catch(UnauthorizedAccessException){}}
        private void UpdateConnect()
        {
            connect.IsEnabled=!busy&&sessions.SelectedItem is BridgeEndpoint;
            connect.Content=UiTheme.Content(busy?"Подключение…":found.Count==0?"Ждём AutoCAD…":"Подключиться",UiTheme.IconPlug);
        }
        private static bool CadRunning(){var list=Process.GetProcessesByName("acad");try{return list.Length>0;}finally{foreach(var p in list)p.Dispose();}}
        private static UIElement Step(int number,string title,string text,bool done,(string Label,Action Action)? action,bool last=false)
        {
            var row=new DockPanel{Margin=new Thickness(0,0,0,last?14:12)};
            var mark=new Border{Width=30,Height=30,CornerRadius=new CornerRadius(15),Background=done?UiTheme.Success:UiTheme.Fill,VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(0,1,12,0),
                Child=done?(UIElement)UiTheme.Icon(UiTheme.IconCheck,14,UiTheme.OnAccent):new TextBlock{Text=number.ToString(),FontSize=14,FontWeight=FontWeights.SemiBold,Foreground=UiTheme.Secondary,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            if(done)((TextBlock)mark.Child).HorizontalAlignment=HorizontalAlignment.Center;
            DockPanel.SetDock(mark,Dock.Left);row.Children.Add(mark);
            if(action!=null){var b=UiTheme.Button(action.Value.Label,null,"Outline");b.VerticalAlignment=VerticalAlignment.Center;b.Margin=new Thickness(12,0,0,0);var run=action.Value.Action;b.Click+=(s,e)=>run();DockPanel.SetDock(b,Dock.Right);row.Children.Add(b);}
            var texts=new StackPanel();texts.Children.Add(UiTheme.Text(title,16,FontWeights.SemiBold));texts.Children.Add(UiTheme.Hint(text,new Thickness(0,2,0,0)));row.Children.Add(texts);
            return row;
        }
        private async Task ConnectAsync()
        {
            if(busy||sessions.SelectedItem is not BridgeEndpoint endpoint)return;busy=true;UpdateConnect();
            try
            {
                var host=new RemoteHost(endpoint);await host.ConnectAsync();if(!settings.ConnectedBefore){settings.ConnectedBefore=true;SavePreferences();}DesktopUi.ShowMain(this,host);busy=false;Close();
            }
            catch(Exception ex){ModernDialog.Message(DesktopUi.Handle(this),"Не удалось подключиться",ex.Message+"\n\nПроверьте, что AutoCAD не занят командой, и попробуйте ещё раз.",DialogKind.Warning);lastSignature="";}
            finally{busy=false;UpdateConnect();}
        }
        private void OpenDemo()
        {
            if(busy)return;
            DesktopUi.ShowMain(this,new DemoHost(),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CadBatch","demo-profile.json"));Close();
        }
        private async Task BackgroundUpdateCheck()
        {
            try{using var http=UpdateClient.CreateHttpClient();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));var latest=await new UpdateClient(http).LatestAsync(timeout.Token);
                if(latest!=null&&UpdateClient.IsNewer(ProductInfo.Version,latest.Version)){updateState.Text="Доступна версия "+latest.Version+" — нажмите «Проверить».";updateState.Foreground=UiTheme.Accent;}
                else updateState.Text=latest==null?"Опубликованных обновлений пока нет.":"У вас последняя версия.";}
            catch{updateState.Text="Сервер обновлений сейчас недоступен. Работе это не мешает.";}
        }
        private void InstallComponent()
        {
            try
            {
                if(CadRunning())throw new InvalidOperationException("Сначала сохраните чертежи и полностью закройте AutoCAD. После установки запустите его снова.");
                string maintenance=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","CadBatch.Setup.exe"));
                if(File.Exists(maintenance)){Process.Start(new ProcessStartInfo(maintenance,"--update --wait-pid "+Environment.ProcessId+" --launch"){UseShellExecute=true});Application.Current.Shutdown();return;}
                string? source=new[]{Path.Combine(AppContext.BaseDirectory,"CadBatch.bundle"),Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","CadBatch.bundle"))}.FirstOrDefault(Directory.Exists);
                if(source==null)throw new InvalidOperationException("Не найдены файлы компонента. Используйте полный установщик CAD Batch.");
                string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","ApplicationPlugins"),target=Path.Combine(root,"CadBatch.bundle"),stage=Path.Combine(root,"CadBatch.stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                Copy(source,stage);string? backup=null;
                try{if(Directory.Exists(target)){backup=Path.Combine(root,"CadBatch.backup-"+Guid.NewGuid().ToString("N"));Directory.Move(target,backup);}Directory.Move(stage,target);}
                catch{if(backup!=null&&!Directory.Exists(target))Directory.Move(backup,target);if(Directory.Exists(stage))Directory.Delete(stage,true);throw;}
                if(backup!=null)try{Directory.Delete(backup,true);}catch(IOException){}catch(UnauthorizedAccessException){}
                lastSignature="";Refresh();ModernDialog.Message(DesktopUi.Handle(this),"Компонент установлен","Теперь запустите AutoCAD — окно подключится к нему автоматически.",DialogKind.Success);
            }catch(Exception ex){ModernDialog.Message(DesktopUi.Handle(this),"Компонент AutoCAD",ex.Message,DialogKind.Warning);}
        }
        private static void Copy(string source,string target){if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Каталог компонента содержит ссылку.");Directory.CreateDirectory(target);foreach(string f in Directory.GetFiles(source))File.Copy(f,Path.Combine(target,Path.GetFileName(f)),false);foreach(string d in Directory.GetDirectories(source))Copy(d,Path.Combine(target,Path.GetFileName(d)));}
        private void LaunchCad()
        {
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try{using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD");if(key!=null)foreach(string version in key.GetSubKeyNames())using(var v=key.OpenSubKey(version))if(v!=null)foreach(string product in v.GetSubKeyNames())using(var p=v.OpenSubKey(product)){string? dir=p?.GetValue("AcadLocation") as string;if(dir!=null&&File.Exists(Path.Combine(dir,"acad.exe")))paths.Add(Path.Combine(dir,"acad.exe"));}}catch(System.Security.SecurityException){}catch(UnauthorizedAccessException){}
            string autodesk=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Autodesk");if(Directory.Exists(autodesk))foreach(string dir in Directory.GetDirectories(autodesk,"AutoCAD 20*"))if(File.Exists(Path.Combine(dir,"acad.exe")))paths.Add(Path.Combine(dir,"acad.exe"));
            if(paths.Count==0){ModernDialog.Message(DesktopUi.Handle(this),"AutoCAD не найден","Установите полный AutoCAD 2021 или новее. AutoCAD LT не поддерживается.",DialogKind.Warning);return;}
            // Newest version first; "AutoCAD 2026" sorts after "AutoCAD 2025" by name.
            var ordered=paths.OrderByDescending(p=>p,StringComparer.OrdinalIgnoreCase).ToArray();
            string? chosen=ordered.Length==1?ordered[0]:ModernDialog.Choice(DesktopUi.Handle(this),"Какой AutoCAD запустить?",null,ordered);
            if(chosen!=null)DesktopUi.Open(chosen);
        }
    }
    internal sealed class UpdateWindow : Window
    {
        private readonly TextBlock status=UiTheme.Text("Проверяем версию…",15,FontWeights.SemiBold),details=UiTheme.Hint("");
        private readonly ProgressBar progress=new ProgressBar{Margin=new Thickness(0,14,0,4),Maximum=1,Visibility=Visibility.Collapsed};
        private readonly Button download=UiTheme.Button("Скачать",UiTheme.IconDownload,"Primary"),install=UiTheme.Button("Установить и перезапустить",null,"Primary");
        private readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        private readonly bool operationBusy;
        private UpdateRelease? release;private string? file;private bool downloading,installing;
        public UpdateWindow(bool operationBusy)
        {
            this.operationBusy=operationBusy;Title="Обновления CAD Batch";Width=560;Height=520;MinHeight=420;MinWidth=460;WindowStartupLocation=WindowStartupLocation.CenterOwner;ResizeMode=ResizeMode.CanResizeWithGrip;UiTheme.Apply(this);UiTheme.UseLogo(this);
            var root=new DockPanel{Margin=new Thickness(28)};Content=root;
            var buttons=new DockPanel{LastChildFill=false,Margin=new Thickness(0,18,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            download.IsEnabled=false;install.Visibility=Visibility.Collapsed;var close=UiTheme.Button("Закрыть");close.IsCancel=true;close.Click+=(s,e)=>Close();
            DockPanel.SetDock(install,Dock.Right);DockPanel.SetDock(download,Dock.Right);DockPanel.SetDock(close,Dock.Right);download.Margin=install.Margin=new Thickness(8,0,0,0);buttons.Children.Add(install);buttons.Children.Add(download);buttons.Children.Add(close);
            var head=new DockPanel{Margin=new Thickness(0,0,0,16)};var icon=DesktopUi.AppIcon(48);icon.Margin=new Thickness(0,0,14,0);DockPanel.SetDock(icon,Dock.Left);head.Children.Add(icon);
            var titles=new StackPanel{VerticalAlignment=VerticalAlignment.Center};titles.Children.Add(UiTheme.Title("Обновления",22));titles.Children.Add(UiTheme.Hint("Установлена версия "+ProductInfo.Version+". Настройки и задания сохраняются.",new Thickness(0,2,0,0)));head.Children.Add(titles);
            DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
            var top=new StackPanel();top.Children.Add(status);top.Children.Add(progress);DockPanel.SetDock(top,Dock.Top);root.Children.Add(top);
            details.FontSize=12.5;var notes=new Border{Child=new ScrollViewer{Content=details,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Padding=new Thickness(14,12,14,12)},Background=UiTheme.Surface,BorderBrush=UiTheme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(12),Margin=new Thickness(0,12,0,0)};root.Children.Add(notes);
            DesktopUi.Scale(this);
            download.Click+=async(s,e)=>await DownloadAsync();install.Click+=async(s,e)=>await InstallAsync();
            Loaded+=async(s,e)=>await CheckAsync();Closing+=(s,e)=>{if(installing){e.Cancel=true;return;}cancellation.Cancel();};
        }
        private async Task CheckAsync()
        {
            try{using var http=UpdateClient.CreateHttpClient();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);timeout.CancelAfter(TimeSpan.FromSeconds(15));release=await new UpdateClient(http).LatestAsync(timeout.Token);
                if(release==null){status.Text="Выпуски ещё не опубликованы.";details.Text="Когда выйдет новая версия, она появится здесь.";return;}
                details.Text=string.IsNullOrWhiteSpace(release.Notes)?"Описание выпуска отсутствует.":release.Notes;
                if(!UpdateClient.IsNewer(ProductInfo.Version,release.Version)){status.Text="✓  У вас последняя версия";status.Foreground=UiTheme.Success;return;}
                status.Text="Доступна версия "+release.Version+" · "+(release.Size/1024d/1024).ToString("0.0")+" МБ";download.IsEnabled=true;}
            catch(Exception ex){if(!cancellation.IsCancellationRequested){status.Text="Не удалось проверить обновления";details.Text=ex.Message+"\n\nПроверьте подключение к интернету и попробуйте позже.";}}
        }
        private async Task DownloadAsync()
        {
            if(release==null||downloading)return;downloading=true;download.IsEnabled=false;progress.Visibility=Visibility.Visible;
            try{using var http=UpdateClient.CreateHttpClient();status.Text="Загрузка версии "+release.Version+"…";file=await new UpdateClient(http).DownloadAsync(release,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","Updates",release.Version),new Progress<double>(v=>progress.Value=v),cancellation.Token);
                status.Text="✓  Загружено и проверено";status.Foreground=UiTheme.Success;download.Visibility=Visibility.Collapsed;install.Visibility=Visibility.Visible;install.IsEnabled=!operationBusy;details.Text="Файл скачан полностью и не повреждён. Закройте AutoCAD и нажмите «Установить и перезапустить».\n\n"+details.Text;
                if(operationBusy)details.Text="Установка станет доступна после завершения текущего задания CAD Batch.\n\n"+details.Text;}
            catch(Exception ex){if(!cancellation.IsCancellationRequested){status.Text=ex.Message;download.IsEnabled=true;}}finally{downloading=false;}
        }
        private async Task InstallAsync()
        {
            if(file==null||release==null||installing)return;
            try
            {
                if(operationBusy)throw new InvalidOperationException("Дождитесь завершения задания CAD Batch.");
                var list=Process.GetProcessesByName("acad");bool cad=list.Length>0;foreach(var p in list)p.Dispose();
                if(cad)throw new InvalidOperationException("Сначала сохраните чертежи и полностью закройте AutoCAD, затем нажмите «Установить».");
                installing=true;install.IsEnabled=false;status.Text="Подготовка к установке…";
                if(!await UpdateClient.VerifyAsync(file,release,CancellationToken.None))throw new InvalidDataException("Установщик изменён после загрузки. Скачайте его заново.");
                Process.Start(new ProcessStartInfo(file,"--update --wait-pid "+Environment.ProcessId+" --launch"){UseShellExecute=true});Application.Current.Shutdown();
            }
            catch(Exception ex){installing=false;install.IsEnabled=!operationBusy;status.Text="Готово к установке";ModernDialog.Message(DesktopUi.Handle(this),"Обновление CAD Batch",ex.Message,DialogKind.Warning);}
        }
    }
}
