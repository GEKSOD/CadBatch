using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CadBatch.Core;
using CadBatch.UI;

namespace CadBatch.Desktop
{
    internal static class Program
    {
        private static async Task LiveUpdateSmokeAsync(string output)
        {
            using(var http=CadBatch.Updates.UpdateClient.CreateHttpClient())
            {
                var client=new CadBatch.Updates.UpdateClient(http);var release=await client.LatestAsync(CancellationToken.None)??throw new IOException("No published release.");
                string path=await client.DownloadAsync(release,Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output))!,"update-live"),null,CancellationToken.None);
                if(!await CadBatch.Updates.UpdateClient.VerifyAsync(path,release,CancellationToken.None))throw new IOException("Live asset hash verification failed.");
                File.WriteAllText(output,"LIVE UPDATE PASS: "+release.Version+"; "+release.Size+" bytes; SHA256 "+release.Sha256+"; installer downloaded and verified, not executed.");
            }
        }
        private static void FormsStartup(){Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);}
        [STAThread]
        private static void Main(string[] args)
        {
            if(args.Length==2&&args[0]=="--update-smoke")
            {
                try{LiveUpdateSmokeAsync(args[1]).GetAwaiter().GetResult();}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;
            }
            if(args.Length==3&&args[0]=="--bridge-client"){try{BridgeSmoke.RunClientAsync(args[1],args[2]).GetAwaiter().GetResult();}catch(Exception ex){File.WriteAllText(args[2]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;}
            if(args.Length==2&&args[0]=="--bridge-smoke"){try{BridgeSmoke.RunAsync(args[1]).GetAwaiter().GetResult();}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}return;}
            FormsStartup();
            var app=new System.Windows.Application {ShutdownMode=System.Windows.ShutdownMode.OnMainWindowClose};
            AppDomain.CurrentDomain.UnhandledException+=(s,e)=>ErrorLog.Write("Сбой приложения",e.ExceptionObject as Exception??new Exception(Convert.ToString(e.ExceptionObject)));
            TaskScheduler.UnobservedTaskException+=(s,e)=>{ErrorLog.Write("Фоновая задача",e.Exception);e.SetObserved();};
            app.DispatcherUnhandledException+=(s,e)=>{e.Handled=true;ErrorLog.Write("Непредвиденная ошибка",e.Exception);if(args.Length==2&&args[0].EndsWith("smoke",StringComparison.Ordinal)){File.AppendAllText(args[1]+".dispatcher.txt",e.Exception+Environment.NewLine);return;}ModernDialog.Message(IntPtr.Zero,"Что-то пошло не так",e.Exception.Message,DialogKind.Warning);};
            if(args.Length==0){app.Run(new ConnectionWindow());return;}
            if(args.Length==2&&args[0]=="--lobby-smoke")
            {
                var lobby=new ConnectionWindow();lobby.ContentRendered+=(s,e)=>{try{lobby.UpdateLayout();var content=(System.Windows.FrameworkElement)lobby.Content;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)content.ActualWidth,(int)content.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(content);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(args[1]))encoder.Save(output);File.WriteAllText(args[1]+".txt","Connection window PASS; no CAD or demo data required.");}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{lobby.Close();}};app.Run(lobby);return;
            }
            if(args.Length==2&&args[0]=="--look-smoke")
            {
                // Renders simple mode, advanced mode and the largest text size for visual review.
                Directory.CreateDirectory(args[1]);var look=new BatchWindow.FluentWindow(new DemoHost(),Path.Combine(args[1],"look-profile.json")){ShowIntroOnStart=false};
                look.ContentRendered+=async(s,e)=>{try{await look.SmokeAsync(Path.Combine(args[1],"simple.png"));
                    look.PreviewLook(true,1);await look.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);look.RenderTo(Path.Combine(args[1],"advanced.png"));look.RenderCard(Path.Combine(args[1],"card.png"));
                    var capture=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(700)};capture.Tick+=(_,__)=>{capture.Stop();var sheet=System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w=>w!=look);if(sheet!=null){UiTheme.Snapshot((System.Windows.FrameworkElement)sheet.Content,Path.Combine(args[1],"done.png"));sheet.Close();}};capture.Start();
                    ModernDialog.Actions(new System.Windows.Interop.WindowInteropHelper(look).Handle,"Готово!","Сохранено PDF-файлов: 12\nПапка: D:\\Проекты\\АР\\PDF",DialogKind.Success,"Открыть папку","Открыть первый PDF","Закрыть");
                    File.WriteAllLines(Path.Combine(args[1],"accessibility.txt"),look.AccessibilityReport());
                    look.PreviewSelection();await look.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);look.RenderTo(Path.Combine(args[1],"selected.png"));File.WriteAllText(Path.Combine(args[1],"selected.txt"),look.SelectedRowForeground());
                    look.RenderEditors(args[1]);
                    look.PreviewProblems();await look.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);look.RenderTo(Path.Combine(args[1],"problems.png"));
                    var intro=new IntroWindow(IntPtr.Zero,1);intro.Show();await intro.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);UiTheme.Snapshot((System.Windows.FrameworkElement)intro.Content,Path.Combine(args[1],"intro.png"));intro.Close();
                    look.WindowState=System.Windows.WindowState.Normal;look.Width=1090;look.Height=610;look.PreviewLook(false,1);await look.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);look.PreviewLook(false,1);look.RenderTo(Path.Combine(args[1],"compact.png"));
                    look.PreviewTab(1);await look.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);look.RenderTo(Path.Combine(args[1],"compact-save.png"));look.PreviewTab(0);
                    look.WindowState=System.Windows.WindowState.Maximized;
                    look.PreviewLook(false,1.3);await look.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);look.RenderTo(Path.Combine(args[1],"large.png"));}
                    catch(Exception ex){File.WriteAllText(Path.Combine(args[1],"look.error.txt"),ex.ToString());Environment.ExitCode=1;}finally{look.Close();}};app.Run(look);return;
            }
            if(args.Length==2&&(args[0]=="--fallback-smoke"||args[0]=="--nothing-smoke"||args[0]=="--missed-smoke"))
            {
                // A computer with the old settings (model only, scale 1): the window must search wider by itself.
                string mode=args[0].Substring(2,args[0].Length-8);Directory.CreateDirectory(args[1]);string settingsPath=Path.Combine(args[1],mode+"-profile.json");
                var old=new ProjectProfile();old.Search.Layouts=false;old.Search.Scales=new List<double>{1};ProfileStore.Save(settingsPath,old);
                var smoke=new BatchWindow.FluentWindow(new DemoHost(mode=="fallback"?"layout100":mode),settingsPath){ShowIntroOnStart=false};
                smoke.ContentRendered+=async(s,e)=>{try{await smoke.FallbackSmokeAsync(args[1],mode,settingsPath);}catch(Exception ex){File.WriteAllText(Path.Combine(args[1],mode+".error.txt"),ex.ToString());Environment.ExitCode=1;}finally{smoke.Close();}};
                app.Run(smoke);return;
            }
            if(args.Length==2&&args[0]=="--empty-smoke")
            {
                var empty=new BatchWindow.FluentWindow(new DemoHost(),Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,"empty-profile.json")){ShowIntroOnStart=false};
                empty.ContentRendered+=async(s,e)=>{try{await empty.Dispatcher.InvokeAsync(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);empty.RenderTo(args[1]);
                    // Renders the plan sheet that precedes every PDF batch, then dismisses it.
                    var capture=new System.Windows.Threading.DispatcherTimer{Interval=TimeSpan.FromMilliseconds(700)};capture.Tick+=(_,__)=>{capture.Stop();var sheet=System.Windows.Application.Current.Windows.OfType<System.Windows.Window>().FirstOrDefault(w=>w!=empty);if(sheet!=null){var root=(System.Windows.FrameworkElement)sheet.Content;var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap((int)root.ActualWidth,(int)root.ActualHeight,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(args[1]+".dialog.png"))encoder.Save(file);sheet.Close();}};capture.Start();
                    ModernDialog.Plan(new System.Windows.Interop.WindowInteropHelper(empty).Handle,"Вывод PDF","Проверьте план. Вывод можно отменить в любой момент, готовые файлы сохранятся.",new[]{("PDF-файлов","12"),("Страниц","12"),("Папка",@"D:\Проекты\АР\PDF"),("Плоттер","DWG To PDF.pc3"),("Цвет","Цвета чертежа")},"Файлы:\nАР_001_A1.pdf\nАР_002_A2.pdf","Создать 12 PDF");}catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}finally{empty.Close();}};app.Run(empty);return;
            }
            var settings=args.Length==2&&args[0]=="--smoke"?Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1]))!,"smoke-profile.json"):Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CadBatch","demo-profile.json");
            var window=new BatchWindow.FluentWindow(new DemoHost(),settings){ShowIntroOnStart=!(args.Length==2&&args[0]=="--smoke")};
            if(args.Length==2&&args[0]=="--smoke")window.ContentRendered+=async(s,e)=>
            {
                try{await window.SmokeAsync(args[1]);}
                catch(Exception ex){File.WriteAllText(args[1]+".error.txt",ex.ToString());Environment.ExitCode=1;}
                finally{window.Close();}
            };
            app.Run(window);
        }
    }
    internal sealed class DemoHost : IBatchHost
    {
        // Smoke variants that honour the search settings like AutoCAD does:
        // "layout100" — frames on a layout tab drawn at 1:100; "nothing" — rectangles of no standard size.
        private readonly string variant;
        public DemoHost(string variant=""){this.variant=variant;}
        public string Description=>"ДЕМОНСТРАЦИЯ ИНТЕРФЕЙСА • AutoCAD не подключён • тестовые данные";
        public bool CanPlot=>false;
        public Task<string[]> GetLayoutNamesAsync()=>Task.FromResult(Array.Empty<string>());
        public Task<SearchRegion?> PickSearchRegionAsync()=>throw new NotSupportedException("Область выбирается в AutoCAD.");
        public Task<PlotCapabilities> GetPlotCapabilitiesAsync(string device)=>Task.FromResult(new PlotCapabilities());
        public Task<string[]> GetPlotDevicesAsync()=>Task.FromResult(Array.Empty<string>());
        public Task<StampScaleMeasurement?> MeasureStampScaleAsync(double referenceWidthMm,bool twoPoints)=>throw new NotSupportedException("Укажите линию внутри AutoCAD.");
        public Task<ScanResult> SearchAsync(ProjectProfile profile,CancellationToken cancellation)
        {
            var candidates=new List<FrameCandidate>();
            var formats=FormatDefinition.Defaults();
            for(int i=0;i<12;i++)
            {
                var f=formats[i%5];double x=(i%4)*1600,y=-(i/4)*1400;
                candidates.Add(new FrameCandidate
                {
                    DocumentKey="demo",DocumentName="Пример раздела АР.dwg",Layout="Model",HandlePath=(i+100).ToString("X"),Layer="РАМКИ",Kind="Демонстрация",
                    Vertices=new List<Point2> { new Point2(x,y),new Point2(x+f.WidthMm,y),new Point2(x+f.WidthMm,y+f.HeightMm),new Point2(x,y+f.HeightMm) },
                    Attributes=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) { ["SHEET"]=(i+1).ToString(),["TITLE"]=i==0?"Общие данные":"План. Разрез. Узлы — лист "+(i+1),["CODE"]="АР-01" }
                });
            }
            if(variant.Length>0)
            {
                double factor=variant=="layout100"?100:variant=="missed"?1:1.37;
                foreach(var c in candidates){c.Layout=variant=="layout100"?"3.Вертикалка":"Model";c.Vertices=c.Vertices.Select(p=>new Point2(p.X*factor,p.Y*factor)).ToList();}
                // "missed": every second frame is drawn with separate lines and is seen only when lines are searched.
                if(variant=="missed"){for(int i=1;i<candidates.Count;i+=2){candidates[i].GeometryKind="Lines";candidates[i].Kind="Отрезки";}if(!profile.Search.Lines)candidates.RemoveAll(c=>c.GeometryKind=="Lines");}
                if(!profile.Search.Layouts)candidates.RemoveAll(c=>c.Layout!="Model");if(!profile.Search.Model)candidates.RemoveAll(c=>c.Layout=="Model");
                return Task.Run(()=>FrameDetection.Detect(candidates,profile.Search,formats,cancellation),cancellation);
            }
            // Demo uses the same detection kernel, with a fixed deterministic sample catalog.
            return Task.Run(()=>FrameDetection.Detect(candidates,new SearchOptions(),formats,cancellation),cancellation);
        }
        public Task LocateAsync(DetectedFrame frame)
        {
            ModernDialog.Message(IntPtr.Zero,"Демо-режим","Это демонстрационные данные. Переход к рамке работает после подключения к AutoCAD.");return Task.CompletedTask;
        }
        public Task<ExportReport> ExportAsync(ExportRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation)=>throw new NotSupportedException("PDF создаётся только внутри AutoCAD.");
        public Task<CadOperationResult> RunCadOperationAsync(CadOperationRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation)=>throw new NotSupportedException("Изменения DWG доступны внутри AutoCAD.");
    }
}
