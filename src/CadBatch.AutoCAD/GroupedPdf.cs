using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CadBatch.Core;
using CadBatch.Pdf;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private static ExportRequest EffectiveRequest(ExportRequest request,DetectedFrame frame)=>PrintTemplates.Resolve(request,frame);
        private static void ValidateOutputDevice(ExportRequest request)
        {
            bool pdf=request.Device.IndexOf("pdf",StringComparison.OrdinalIgnoreCase)>=0;
            if(request.PhysicalPrint)
            {
                if(pdf)throw new ArgumentException("Для физической печати выберите обычный принтер/плоттер, а не PDF-драйвер.");
            }
            else if(!pdf||!request.Device.EndsWith(".pc3",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Для PDF выберите PDF PC3 AutoCAD.");
        }
        private async Task<ExportReport> ExportGroupedAsync(ExportRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation)
        {
            if(request.Groups.Count==0)throw new ArgumentException("Не сформирован план объединения PDF.");
            var report=new ExportReport();int done=0;int total=request.Groups.Sum(g=>g.Items.Count);
            foreach(var group in request.Groups)
            {
                string directory=Path.GetDirectoryName(group.Path)!;
                string scratch=Path.Combine(directory,".cadbatch-"+Guid.NewGuid().ToString("N"));
                try
                {
                    Directory.CreateDirectory(scratch);
                    var pages=new List<string>();var titles=new List<string>();
                    foreach(var item in group.Items)
                    {
                        cancellation.ThrowIfCancellationRequested();progress.Report(new BatchProgress(done,total,"Подготовка страницы "+(done+1)+" / "+total));
                        string page=Path.Combine(scratch,(done+1).ToString("00000")+".pdf");var trace=new OperationTrace();
                        var effective=EffectiveRequest(request,item.Frame);ValidateOutputDevice(effective);
                        try{await InContext(()=>PlotOne(new OutputItem{Frame=item.Frame,Path=page},effective,trace));}
                        finally{report.Details.Add("Страница "+item.Frame.Page+" / "+item.Frame.Source.HandlePath+Environment.NewLine+trace);}
                        pages.Add(page);titles.Add((string.IsNullOrWhiteSpace(item.Frame.SheetNumber)?item.Frame.Page.ToString():item.Frame.SheetNumber)+" — "+
                            (string.IsNullOrWhiteSpace(item.Frame.Title)?item.Frame.FormatName:item.Frame.Title));
                        item.Frame.Status="Страница подготовлена";done++;progress.Report(new BatchProgress(done,total,item.Frame.Status));
                    }
                    cancellation.ThrowIfCancellationRequested();string combined=Path.Combine(scratch,"combined.pdf");
                    await Task.Run(()=>PdfRunner.Run(PdfJob.Merge(pages.ToArray(),combined,titles.ToArray(),Path.GetFileNameWithoutExtension(group.Path))));cancellation.ThrowIfCancellationRequested();
                    OutputFiles.MoveReplacing(combined,group.Path);report.Files.Add(group.Path);request.OutputCompleted?.Invoke(group.Path);foreach(var item in group.Items)item.Frame.Status="В PDF: "+Path.GetFileName(group.Path);
                }
                catch(OperationCanceledException){report.Cancelled=true;foreach(var item in group.Items)item.Frame.Status="Комплект отменён — PDF не сохранён";break;}
                catch(Exception ex){MediaCatalog.Clear();report.Errors.Add(Path.GetFileName(group.Path)+": "+ex.Message);report.Details.Add(ex.ToString());foreach(var item in group.Items)item.Frame.Status="Комплект не сохранён: ошибка";break;}
                finally
                {
                    // This directory was created by this operation, directly below the chosen output directory.
                    try{if(Directory.Exists(scratch))Directory.Delete(scratch,true);}catch(Exception ex){report.Details.Add("Не удалены временные файлы "+scratch+": "+ex.Message);}
                }
            }
            return report;
        }
    }
}
