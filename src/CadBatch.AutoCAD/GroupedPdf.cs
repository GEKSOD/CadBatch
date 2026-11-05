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
        // A complete set gets its name and counts as done; a set with missing pages is saved as "(неполный)" and stays
        // unfinished, so "Повторить незавершённые" builds it again.
        private static async Task SaveGroupAsync(ExportRequest request,ExportReport report,PdfGroup group,List<string> pages,List<string> titles,List<int> failed,string scratch,bool cancelled)
        {
            bool complete=failed.Count==0&&!cancelled;
            string combined=Path.Combine(scratch,"combined.pdf");
            await Task.Run(()=>PdfRunner.Run(PdfJob.Merge(pages.ToArray(),combined,titles.ToArray(),Path.GetFileNameWithoutExtension(group.Path))));
            string target=complete?group.Path:Path.Combine(Path.GetDirectoryName(group.Path)!,Path.GetFileNameWithoutExtension(group.Path)+" (неполный)"+Path.GetExtension(group.Path));
            OutputFiles.MoveReplacing(combined,target);report.Files.Add(target);
            if(complete){request.OutputCompleted?.Invoke(group.Path);foreach(var item in group.Items)item.Frame.Status="В PDF: "+Path.GetFileName(group.Path);}
            else
            {
                if(failed.Count>0)report.Errors.Add(Path.GetFileName(target)+": нет листов "+string.Join(", ",failed)+". Исправьте их и нажмите «Повторить незавершённые».");
                foreach(var item in group.Items.Where(i=>!i.Frame.Status.StartsWith("Ошибка",StringComparison.Ordinal)))item.Frame.Status="В неполном PDF: "+Path.GetFileName(target);
            }
        }
        private async Task<ExportReport> ExportGroupedAsync(ExportRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation)
        {
            if(request.Groups.Count==0)throw new ArgumentException("Не сформирован план объединения PDF.");
            var report=new ExportReport();int done=0;int total=request.Groups.Sum(g=>g.Items.Count);
            foreach(var group in request.Groups)
            {
                string directory=Path.GetDirectoryName(group.Path)!;
                string scratch=Path.Combine(directory,".cadbatch-"+Guid.NewGuid().ToString("N"));
                var pages=new List<string>();var titles=new List<string>();var failed=new List<int>();bool stop=false;
                try
                {
                    Directory.CreateDirectory(scratch);
                    foreach(var item in group.Items)
                    {
                        cancellation.ThrowIfCancellationRequested();progress.Report(new BatchProgress(done,total,"Подготовка страницы "+(done+1)+" / "+total));
                        string page=Path.Combine(scratch,(done+1).ToString("00000")+".pdf");var trace=new OperationTrace();
                        try
                        {
                            var effective=EffectiveRequest(request,item.Frame);ValidateOutputDevice(effective);
                            await InContext(()=>PlotOne(new OutputItem{Frame=item.Frame,Path=page},effective,trace));
                            pages.Add(page);titles.Add((string.IsNullOrWhiteSpace(item.Frame.SheetNumber)?item.Frame.Page.ToString():item.Frame.SheetNumber)+" — "+
                                (string.IsNullOrWhiteSpace(item.Frame.Title)?item.Frame.FormatName:item.Frame.Title));
                            item.Frame.Status="Страница подготовлена";
                        }
                        // One failed sheet costs that sheet only; a failure to restore AutoCAD's state stops everything.
                        catch(Exception ex) when(!(ex is OperationCanceledException)&&!(ex is AggregateException))
                        {
                            MediaCatalog.Clear();failed.Add(item.Frame.Page);item.Frame.Status="Ошибка: "+ex.Message;
                            report.Errors.Add(Path.GetFileName(group.Path)+", лист "+item.Frame.Page+": "+ex.Message);
                        }
                        finally{report.Details.Add("Страница "+item.Frame.Page+" / "+item.Frame.Source.HandlePath+Environment.NewLine+trace);}
                        done++;progress.Report(new BatchProgress(done,total,item.Frame.Status));
                    }
                    if(pages.Count==0)throw new InvalidOperationException("ни одна страница комплекта не создана.");
                    await SaveGroupAsync(request,report,group,pages,titles,failed,scratch,false);
                }
                catch(OperationCanceledException)
                {
                    report.Cancelled=true;stop=true;
                    // Pages already printed are kept in a file that says it is incomplete.
                    if(pages.Count>0)try{await SaveGroupAsync(request,report,group,pages,titles,failed,scratch,true);}catch(Exception ex){report.Details.Add("Неполный комплект не сохранён: "+ex.Message);}
                    foreach(var item in group.Items.Where(i=>!i.Frame.Status.StartsWith("Ошибка",StringComparison.Ordinal)))item.Frame.Status=pages.Count>0?"Вывод отменён — сохранён неполный комплект":"Комплект отменён — PDF не сохранён";
                }
                catch(Exception ex)
                {
                    MediaCatalog.Clear();report.Errors.Add(Path.GetFileName(group.Path)+": "+ex.Message);report.Details.Add(ex.ToString());
                    foreach(var item in group.Items.Where(i=>!i.Frame.Status.StartsWith("Ошибка",StringComparison.Ordinal)))item.Frame.Status="Комплект не сохранён: ошибка";
                    // AutoCAD could not be put back as it was: further plots would build on a wrong state.
                    stop=ex is AggregateException;
                }
                finally
                {
                    // This directory was created by this operation, directly below the chosen output directory.
                    try{if(Directory.Exists(scratch))Directory.Delete(scratch,true);}catch(Exception ex){report.Details.Add("Не удалены временные файлы "+scratch+": "+ex.Message);}
                }
                if(stop)break;
            }
            return report;
        }
    }
}
