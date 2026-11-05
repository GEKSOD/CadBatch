using System;
using Autodesk.AutoCAD.Runtime;
using CadBatch.UI;
using CadBatch.Core;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(CadBatch.AutoCAD.Commands))]
[assembly: ExtensionApplication(typeof(CadBatch.AutoCAD.Commands))]
namespace CadBatch.AutoCAD
{
    public sealed class Commands : IExtensionApplication
    {
        private static BatchWindow.FluentWindow? window;
        private static HostBridge? bridge;
        public void Initialize()
        {
            try
            {
                // PDF work falls back to this separate process if AutoCAD has an incompatible PDF dependency loaded.
                CadBatch.Pdf.PdfRunner.WorkerPath=System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(Commands).Assembly.Location)!,"..","PdfWorker","CadBatch.PdfWorker.exe"));
                var dispatcher=System.Windows.Threading.Dispatcher.CurrentDispatcher;
                bridge=new HostBridge(new AutoCadHost(),async action=>await (await dispatcher.InvokeAsync(action)),System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","bridge"));
            }
            catch(System.Exception ex){ErrorLog.Write("Запуск компонента AutoCAD",ex);AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nCAD Batch: компонент подключения не запущен: "+ex.Message+"\n");}
        }
        public void Terminate(){bridge?.Dispose();}
        [CommandMethod("CADBATCH",CommandFlags.Session)]
        public void Open()
        {
            if(window==null){window=new BatchWindow.FluentWindow(new AutoCadHost());window.Closed+=(s,e)=>window=null;}
            if(!window.IsVisible)Autodesk.AutoCAD.ApplicationServices.Application.ShowModelessWindow(window);
            if(window.WindowState==System.Windows.WindowState.Minimized)window.WindowState=System.Windows.WindowState.Normal;
            window.Activate();
        }
        [CommandMethod("CADBATCHINFO",CommandFlags.Modal)]
        public void Info()
        {
            AcApp.DocumentManager.MdiActiveDocument?.Editor.WriteMessage("\nCAD Batch "+ProductInfo.Version+" | AutoCAD "+AcApp.GetSystemVariable("ACADVER")+" | .NET "+Environment.Version+" | "+typeof(Commands).Assembly.Location+"\n");
        }
    }
}
