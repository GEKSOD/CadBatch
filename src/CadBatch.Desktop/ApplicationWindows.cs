using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using CadBatch.Core;
using CadBatch.UI;
using CadBatch.Updates;
using CadBatch.Transport;

namespace CadBatch.Desktop
{
    internal static class DesktopUi
    {
        public static readonly Brush Blue=new SolidColorBrush(Color.FromRgb(0,113,227));
        public static TextBlock Text(string value,double size=14,bool bold=false)=>new TextBlock{Text=value,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,0,0,12),Foreground=new SolidColorBrush(Color.FromRgb(30,33,40))};
        public static Button Button(string text,Action action,bool primary=false){var b=new Button{Content=text,Padding=new Thickness(16,10,16,10),Margin=new Thickness(0,0,8,8),Background=primary?Blue:Brushes.White,Foreground=primary?Brushes.White:Brushes.Black,BorderBrush=new SolidColorBrush(Color.FromRgb(220,223,230))};b.Template=(ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Button'><Border x:Name='surface' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='1' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='0.4'/></Trigger><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.8'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");b.Click+=(s,e)=>action();return b;}
        public static Border Card(UIElement child)=>new Border{Child=child,Background=Brushes.White,CornerRadius=new CornerRadius(16),BorderBrush=new SolidColorBrush(Color.FromRgb(230,232,239)),BorderThickness=new Thickness(1),Padding=new Thickness(22),Margin=new Thickness(0,0,0,16)};
        public static void Style(Window window){window.FontFamily=new FontFamily("Segoe UI");window.FontSize=14;window.Background=new SolidColorBrush(Color.FromRgb(246,247,250));window.WindowStartupLocation=WindowStartupLocation.CenterScreen;}
        public static void OpenUpdates(Window owner,bool busy=false){var w=new UpdateWindow(busy){Owner=owner};w.ShowDialog();}
    }
    internal sealed class ConnectionWindow : Window
    {
        private readonly ComboBox sessions=new ComboBox{MinHeight=38,DisplayMemberPath="Description",Margin=new Thickness(0,0,0,12)};
        private readonly TextBlock status=DesktopUi.Text("Откройте AutoCAD с установленным компонентом CAD Batch.");
        private readonly TextBlock updateState=DesktopUi.Text("Обновления проверяются через GitHub Releases.",12);
        private readonly CheckBox autoUpdates=new CheckBox{Content="Проверять обновления при запуске",Margin=new Thickness(0,4,0,12)};
        private readonly Button connect;
        private bool busy;
        private readonly string preferences=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","preferences.json");
        private sealed class Preferences{public bool CheckUpdates {get;set;}=true;}
        public ConnectionWindow()
        {
            Title="CAD Batch "+ProductInfo.Version;Width=790;Height=735;MinWidth=620;MinHeight=560;DesktopUi.Style(this);
            var stack=new StackPanel{Margin=new Thickness(32)};Content=new ScrollViewer{Background=Background,Content=stack,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
            stack.Children.Add(DesktopUi.Text("CAD Batch",32,true));stack.Children.Add(DesktopUi.Text("Чертежи AutoCAD → готовые PDF",18));
            var card=new StackPanel();card.Children.Add(DesktopUi.Text("Подключение к AutoCAD",20,true));card.Children.Add(DesktopUi.Text("Выберите запущенный AutoCAD. Поиск рамок, штампы и печать доступны после подключения."));card.Children.Add(sessions);card.Children.Add(status);
            var row=new WrapPanel();connect=DesktopUi.Button("Подключиться",async()=>await ConnectAsync(),true);row.Children.Add(connect);row.Children.Add(DesktopUi.Button("Обновить список",Refresh));row.Children.Add(DesktopUi.Button("Запустить AutoCAD",LaunchCad));card.Children.Add(row);
            card.Children.Add(DesktopUi.Button("Установить / обновить компонент AutoCAD",InstallComponent));stack.Children.Add(DesktopUi.Card(card));
            var updates=new StackPanel();updates.Children.Add(DesktopUi.Text("Приложение и обновления",20,true));updates.Children.Add(DesktopUi.Text("Установлена версия "+ProductInfo.Version+". Настройки и задания сохраняются при обновлении."));updates.Children.Add(updateState);
            try{if(File.Exists(preferences))autoUpdates.IsChecked=System.Text.Json.JsonSerializer.Deserialize<Preferences>(File.ReadAllText(preferences))?.CheckUpdates??true;else autoUpdates.IsChecked=true;}catch{autoUpdates.IsChecked=true;}
            autoUpdates.Click+=(s,e)=>{Directory.CreateDirectory(Path.GetDirectoryName(preferences)!);File.WriteAllText(preferences,System.Text.Json.JsonSerializer.Serialize(new Preferences{CheckUpdates=autoUpdates.IsChecked==true}));};updates.Children.Add(autoUpdates);updates.Children.Add(DesktopUi.Button("Проверить обновления…",()=>DesktopUi.OpenUpdates(this)));stack.Children.Add(DesktopUi.Card(updates));
            var help=new WrapPanel();help.Children.Add(DesktopUi.Button("Инструкция",()=>{string file=Path.Combine(AppContext.BaseDirectory,"Help","Start.html");if(File.Exists(file))Process.Start(new ProcessStartInfo(file){UseShellExecute=true});else Process.Start(new ProcessStartInfo(UpdateClient.ReleasesPage){UseShellExecute=true});}));help.Children.Add(DesktopUi.Button("Репозиторий проекта",()=>Process.Start(new ProcessStartInfo("https://github.com/"+UpdateClient.Repository){UseShellExecute=true})));stack.Children.Add(help);
            Loaded+=async(s,e)=>{Refresh();if(autoUpdates.IsChecked==true)await BackgroundUpdateCheck();};Closing+=(s,e)=>e.Cancel=busy;
        }
        private void Refresh()
        {
            var list=RemoteHost.Discover();sessions.ItemsSource=list;sessions.SelectedIndex=list.Count>0?0:-1;connect.IsEnabled=!busy&&list.Count>0;
            status.Text=list.Count>0?"Найдено подключений: "+list.Count:"Подключений нет. Если компонент установлен недавно, перезапустите AutoCAD.";
        }
        private async Task ConnectAsync()
        {
            if(busy||sessions.SelectedItem is not BridgeEndpoint endpoint)return;busy=true;connect.IsEnabled=false;status.Text="Подключение…";
            try
            {
                var host=new RemoteHost(endpoint);await host.ConnectAsync();var window=new BatchWindow.FluentWindow(host);window.AddApplicationMenu(
                    ()=>{if(!window.CanCloseForUpdate){MessageBox.Show(window,"Дождитесь завершения текущей операции.");return;}var start=new ConnectionWindow();Application.Current.MainWindow=start;start.Show();window.Close();},
                    ()=>DesktopUi.OpenUpdates(window,!window.CanCloseForUpdate));
                Application.Current.MainWindow=window;window.Show();busy=false;Close();
            }
            catch(Exception ex){status.Text=ex.Message;MessageBox.Show(this,ex.Message,"Подключение CAD Batch",MessageBoxButton.OK,MessageBoxImage.Warning);}
            finally{busy=false;connect.IsEnabled=sessions.SelectedItem!=null;}
        }
        private async Task BackgroundUpdateCheck()
        {
            try{using var http=UpdateClient.CreateHttpClient();using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(12));var latest=await new UpdateClient(http).LatestAsync(timeout.Token);updateState.Text=latest==null?"Опубликованных обновлений пока нет.":UpdateClient.IsNewer(ProductInfo.Version,latest.Version)?"Доступна новая версия "+latest.Version+". Нажмите «Проверить обновления».":"Установлена актуальная версия.";}
            catch{updateState.Text="Сейчас нет связи с сервером обновлений. Подключение к AutoCAD доступно.";}
        }
        private void InstallComponent()
        {
            try
            {
                if(Process.GetProcessesByName("acad").Length>0)throw new InvalidOperationException("Сначала сохраните чертежи и полностью закройте AutoCAD. После установки запустите его снова.");
                string maintenance=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","CadBatch.Setup.exe"));
                if(File.Exists(maintenance)){Process.Start(new ProcessStartInfo(maintenance,"--update --wait-pid "+Environment.ProcessId+" --launch"){UseShellExecute=true});Application.Current.Shutdown();return;}
                string? source=new[]{Path.Combine(AppContext.BaseDirectory,"CadBatch.bundle"),Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","CadBatch.bundle"))}.FirstOrDefault(Directory.Exists);
                if(source==null)throw new InvalidOperationException("Не найдены файлы компонента. Используйте полный установщик CAD Batch.");
                string root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Autodesk","ApplicationPlugins"),target=Path.Combine(root,"CadBatch.bundle"),stage=Path.Combine(root,"CadBatch.stage-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
                Copy(source,stage);string? backup=null;
                try{if(Directory.Exists(target)){backup=Path.Combine(root,"CadBatch.backup-"+Guid.NewGuid().ToString("N"));Directory.Move(target,backup);}Directory.Move(stage,target);}catch{if(backup!=null&&!Directory.Exists(target))Directory.Move(backup,target);throw;}
                status.Text="Компонент установлен. Перезапустите AutoCAD, затем нажмите «Обновить список».";
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"Компонент AutoCAD",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
        private static void Copy(string source,string target){if((File.GetAttributes(source)&FileAttributes.ReparsePoint)!=0)throw new IOException("Каталог компонента содержит ссылку.");Directory.CreateDirectory(target);foreach(string f in Directory.GetFiles(source))File.Copy(f,Path.Combine(target,Path.GetFileName(f)),false);foreach(string d in Directory.GetDirectories(source))Copy(d,Path.Combine(target,Path.GetFileName(d)));}
        private void LaunchCad()
        {
            var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try{using var key=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Autodesk\AutoCAD");if(key!=null)foreach(string version in key.GetSubKeyNames())using(var v=key.OpenSubKey(version))if(v!=null)foreach(string product in v.GetSubKeyNames())using(var p=v.OpenSubKey(product)){string? dir=p?.GetValue("AcadLocation") as string;if(dir!=null&&File.Exists(Path.Combine(dir,"acad.exe")))paths.Add(Path.Combine(dir,"acad.exe"));}}catch(System.Security.SecurityException){}
            string autodesk=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Autodesk");if(Directory.Exists(autodesk))foreach(string dir in Directory.GetDirectories(autodesk,"AutoCAD 20*"))if(File.Exists(Path.Combine(dir,"acad.exe")))paths.Add(Path.Combine(dir,"acad.exe"));
            if(paths.Count==0){MessageBox.Show(this,"AutoCAD не найден. Установите полный AutoCAD 2023 или новее на этом компьютере.","CAD Batch");return;}
            var choose=new Window{Title="Запуск AutoCAD",Width=620,Height=210,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner};DesktopUi.Style(choose);var stack=new StackPanel{Margin=new Thickness(20)};var list=new ComboBox{ItemsSource=paths.OrderByDescending(p=>p).ToList(),SelectedIndex=0,MinHeight=36,Margin=new Thickness(0,0,0,18)};stack.Children.Add(list);stack.Children.Add(DesktopUi.Button("Запустить",()=>{Process.Start(new ProcessStartInfo((string)list.SelectedItem){UseShellExecute=true});choose.Close();},true));choose.Content=stack;choose.ShowDialog();
        }
    }
    internal sealed class UpdateWindow : Window
    {
        private readonly TextBlock status=DesktopUi.Text("Проверка версии…"),details=DesktopUi.Text("",12);
        private readonly ProgressBar progress=new ProgressBar{Height=8,Margin=new Thickness(0,12,0,16),Maximum=1};
        private readonly Button download,install;
        private readonly CancellationTokenSource cancellation=new CancellationTokenSource();
        private readonly bool operationBusy;
        private UpdateRelease? release;private string? file;private bool downloading;
        public UpdateWindow(bool operationBusy)
        {
            this.operationBusy=operationBusy;Title="Обновления CAD Batch";Width=680;Height=550;MinHeight=400;DesktopUi.Style(this);
            var stack=new StackPanel{Background=Background,Margin=new Thickness(28)};Content=stack;stack.Children.Add(DesktopUi.Text("Обновления",25,true));stack.Children.Add(DesktopUi.Text("Установлена версия "+ProductInfo.Version));stack.Children.Add(status);stack.Children.Add(new ScrollViewer{Content=details,Height=220,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});stack.Children.Add(progress);
            var buttons=new WrapPanel();download=DesktopUi.Button("Скачать обновление",async()=>await DownloadAsync(),true);download.IsEnabled=false;install=DesktopUi.Button("Установить и перезапустить",Install);install.IsEnabled=false;buttons.Children.Add(download);buttons.Children.Add(install);buttons.Children.Add(DesktopUi.Button("Закрыть",Close));stack.Children.Add(buttons);
            Loaded+=async(s,e)=>await CheckAsync();Closing+=(s,e)=>{cancellation.Cancel();};
        }
        private async Task CheckAsync()
        {
            try{using var http=UpdateClient.CreateHttpClient();using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation.Token);timeout.CancelAfter(TimeSpan.FromSeconds(15));release=await new UpdateClient(http).LatestAsync(timeout.Token);if(release==null){status.Text="Выпуски ещё не опубликованы.";return;}details.Text=release.Notes;if(!UpdateClient.IsNewer(ProductInfo.Version,release.Version)){status.Text="Установлена актуальная версия "+ProductInfo.Version;return;}status.Text="Доступна версия "+release.Version+" · "+(release.Size/1024d/1024).ToString("0.0")+" МБ";download.IsEnabled=true;}
            catch(Exception ex){if(!cancellation.IsCancellationRequested)status.Text="Не удалось проверить обновления: "+ex.Message;}
        }
        private async Task DownloadAsync()
        {
            if(release==null||downloading)return;downloading=true;download.IsEnabled=false;
            try{using var http=UpdateClient.CreateHttpClient();status.Text="Загрузка версии "+release.Version+"…";file=await new UpdateClient(http).DownloadAsync(release,Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","Updates",release.Version),new Progress<double>(v=>progress.Value=v),cancellation.Token);status.Text="Установщик загружен. Контрольная сумма SHA-256 подтверждена.";install.IsEnabled=!operationBusy;}
            catch(Exception ex){if(!cancellation.IsCancellationRequested){status.Text=ex.Message;download.IsEnabled=true;}}finally{downloading=false;}
        }
        private void Install()
        {
            try
            {
                if(file==null||release==null)return;if(operationBusy)throw new InvalidOperationException("Дождитесь завершения задания CAD Batch.");if(Process.GetProcessesByName("acad").Length>0)throw new InvalidOperationException("Сначала сохраните чертежи и полностью закройте AutoCAD, затем нажмите «Установить».");
                if(!UpdateClient.VerifyAsync(file,release,CancellationToken.None).GetAwaiter().GetResult())throw new InvalidDataException("Установщик изменён после загрузки. Скачайте его заново.");
                Process.Start(new ProcessStartInfo(file,"--update --wait-pid "+Environment.ProcessId+" --launch"){UseShellExecute=true});Application.Current.Shutdown();
            }catch(Exception ex){MessageBox.Show(this,ex.Message,"Обновление CAD Batch",MessageBoxButton.OK,MessageBoxImage.Warning);}
        }
    }
}
