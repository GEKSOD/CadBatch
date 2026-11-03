using System;
using System.IO;
using System.Linq;
using System.Threading;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CadBatch.Core;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;

[assembly: CommandClass(typeof(CadBatch.AutoCAD.JobCommands))]
namespace CadBatch.AutoCAD
{
    public sealed class JobCommands
    {
        private static bool running;
        [CommandMethod("CADBATCHRUN",CommandFlags.Session)]
        public async void Run()
        {
            var editor=AcApp.DocumentManager.MdiActiveDocument?.Editor;if(editor==null||running)return;
            var file=editor.GetFileNameForOpen(new PromptOpenFileOptions("\nСохранённое задание CAD Batch: "){Filter="Задание CAD Batch (*.json)|*.json"});if(file.Status!=PromptStatus.OK)return;
            running=true;
            try
            {
                var job=JobStore.Load(file.StringResult);var profile=job.Profile;
                profile.Search.Files=job.Rows.Select(r=>r.DocumentPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if(profile.Search.Files.Any(f=>!Path.IsPathRooted(f)||!File.Exists(f)))throw new InvalidOperationException("У задания есть несохранённые или недоступные исходные DWG.");
                var host=new AutoCadHost();var scan=await host.SearchAsync(profile,CancellationToken.None);var frames=JobStore.Restore(job,scan.Frames,out var warnings);
                if(warnings.Count>0)throw new InvalidOperationException("Исходники изменены. Повторите поиск и сохраните новое задание.\n"+string.Join("\n",warnings));
                var items=OutputPlanner.Build(frames,profile.OutputFolder,profile.FilePattern);var groups=PdfGroupPlanner.Build(items,profile.PdfMode,profile.OutputFolder,profile.CombinedPdfName??"Комплект");
                var checkpoint=CheckpointStore.Create(profile,frames,groups,profile.PdfMode);string journal=Path.Combine(profile.OutputFolder,".CadBatch-jobs","Auto-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".cadbatchjob.json");CheckpointStore.Save(journal,checkpoint);
                var request=new ExportRequest{Items=items,Groups=groups,PdfMode=profile.PdfMode,Device=profile.Device,StyleSheet=profile.StyleSheet,ColorMode=profile.ColorMode,FitToPaper=profile.FitToPaper??true,OutputCompleted=path=>{CheckpointStore.Complete(checkpoint,path);CheckpointStore.Save(journal,checkpoint);}};
                editor.WriteMessage("\nCAD Batch: PDF по сохранённому заданию. Файлов: "+groups.Count+"; папка: "+profile.OutputFolder+"\n");
                var report=await host.ExportAsync(request,new Progress<BatchProgress>(p=>editor.WriteMessage("\n"+p.Text)),CancellationToken.None);
                string reportPath=Path.Combine(profile.OutputFolder,"CadBatch-auto-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".txt");File.WriteAllText(reportPath,ExportReportText.Format(report,request,host.Description));
                editor.WriteMessage("\n"+ExportReportText.State(report)+"; PDF: "+report.Files.Count+"; отчёт: "+reportPath+"\n");
            }
            catch(System.Exception ex){editor.WriteMessage("\nCAD Batch: "+ex.Message+"\n");}
            finally{running=false;}
        }
    }
}
