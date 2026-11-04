using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using CadBatch.Core;

namespace CadBatch.UI
{
    public sealed partial class BatchWindow
    {
        private void AddAdvancedTools(ToolStripMenuItem job,ToolStripMenuItem cad,ToolStripMenuItem searchMenu,ToolStripMenuItem pdfMenu)
        {
            Item(job,"Расширенная ведомость…",SaveAdvancedRegister);
            Item(cad,"Вставить рамку из DWG-шаблона…",async()=>await RunAsync(InsertFrameTemplateAsync),true);
            Item(cad,"Переименовать существующие Layout…",async()=>await RunAsync(()=>LayoutToolAsync(CadOperation.RenameLayouts)),true);
            Item(cad,"Изменить порядок Layout…",async()=>await RunAsync(()=>LayoutToolAsync(CadOperation.ReorderLayouts)),true);
            Item(cad,"Экспорт существующих Layout в DWG…",async()=>await RunAsync(()=>LayoutToolAsync(CadOperation.ExportLayouts)),true);
            Item(cad,"Упаковать DWG и зависимости в ZIP…",async()=>await RunAsync(PackageDrawingAsync),true);
            Item(pdfMenu,"Создать DSD для Layout…",async()=>await RunAsync(()=>LayoutToolAsync(CadOperation.WriteDsd)),true);
            Item(pdfMenu,"Опубликовать Layout через Publisher…",async()=>await RunAsync(()=>LayoutToolAsync(CadOperation.PublishDsd)),true);
            Item(job,"Возобновить вывод по журналу…",async()=>await RunAsync(ResumeExportAsync),true);
            Item(searchMenu,"Выбрать DWG-файлы…",async()=>await RunAsync(()=>SearchFilesAsync(false)),true);
            Item(searchMenu,"Искать в папке…",async()=>await RunAsync(()=>SearchFilesAsync(true)),true);
            Item(searchMenu,"Выбрать область в AutoCAD…",async()=>await RunAsync(async()=>{var region=await host.PickSearchRegionAsync();if(region==null)return;profile.Search.Region=region;profile.Search.Files=null;profile.Search.AllOpenDocuments=false;allDocs.Checked=false;await ScanAsync();}),true);
            Item(searchMenu,"Сбросить область и список файлов",()=>{profile.Search.Region=null;profile.Search.Files=null;status.Text="Область сброшена. Поиск — по текущему документу или открытым DWG.";});
            Item(searchMenu,"Поля штампа: текст и таблицы…",EditMetadata);
        }
        private async Task InsertFrameTemplateAsync()
        {
            using(var dialog=new OpenFileDialog{Title="DWG с одной рамкой и штампом в модели",Filter="DWG-шаблон|*.dwg"})
            {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;var values=AskValues("Вставка DWG-шаблона",("Коэффициент вставки (1 — размеры как в исходном DWG)","1"));if(values==null)return;
                if(!ShowText("Вставка формы","В AutoCAD будет вставлена ВСЯ модель выбранного DWG как один блок. Выберите файл только с рамкой и нужной формой СПДС/ЕСКД. Точка вставки соответствует началу координат шаблона. Исходный шаблон не изменяется.\r\n\r\n"+dialog.FileName,"Выбрать точку вставки"))return;
                await RunOperationAsync(new CadOperationRequest{Operation=CadOperation.InsertFrameTemplate,Files=new[]{dialog.FileName},Scale=Number(values[0])});
            }
        }
        private void SaveAdvancedRegister()
        {
            var frames=CheckedFrames();var columns=RegisterOptions.Available.Concat(frames.SelectMany(f=>f.Metadata.Keys)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();var choices=AskChecks("Столбцы ведомости",columns.Select(c=>(c,RegisterOptions.Available.Contains(c))).ToArray());if(choices==null)return;
            var grouping=AskValues("Группировка ведомости",("Поля группировки через «;», например «Шифр; Формат» (пусто — без группировки)","Шифр"));if(grouping==null)return;
            string group=grouping[0].Trim();var unknown=group.Split(';').Select(x=>x.Trim()).Where(x=>x.Length>0&&!columns.Contains(x)).ToList();if(unknown.Count>0)throw new ArgumentException("Группировка: нет столбца «"+unknown[0]+"». Укажите имена столбцов ведомости.");
            var options=new RegisterOptions{Columns=columns.Where((c,i)=>choices[i]).ToArray(),GroupBy=group};options.Rows(frames);
            using(var dialog=new SaveFileDialog{Filter="Книга Excel|*.xlsx|Таблица CSV UTF-8|*.csv",FileName="Ведомость.xlsx"})if(dialog.ShowDialog(DialogOwner)==DialogResult.OK){if(dialog.FilterIndex==2)RegisterExport.Csv(dialog.FileName,frames,options);else RegisterExport.Xlsx(dialog.FileName,frames,options);}
        }
        private async Task LayoutToolAsync(CadOperation operation)
        {
            var names=await host.GetLayoutNamesAsync();if(names.Length==0)throw new ArgumentException("В активном DWG нет бумажных Layout.");
            var chosen=AskChecks("Выберите Layout активного DWG",names.Select(n=>(n,true)).ToArray());if(chosen==null)return;var selected=names.Where((n,i)=>chosen[i]).ToArray();if(selected.Length==0)return;
            var request=new CadOperationRequest{Operation=operation,Files=selected,Device=device.Text,NamePattern="Публикация"};
            if(operation==CadOperation.RenameLayouts){var values=AskValues("Имена Layout",("Шаблон: {Index} — номер, {Name} — прежнее имя","Лист {Index}"));if(values==null)return;request.NamePattern=values[0];}
            if(operation==CadOperation.ReorderLayouts){var values=AskValues("Порядок Layout",("Имена через ; — в нужном порядке",string.Join(";",selected)));if(values==null)return;var order=values[0].Split(';').Select(n=>n.Trim()).Where(n=>n.Length>0).ToArray();if(order.Length!=selected.Length||order.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=order.Length||order.Any(n=>!selected.Contains(n,StringComparer.OrdinalIgnoreCase)))throw new ArgumentException("Укажите каждое выбранное имя один раз.");request.Files=order;}
            if(operation==CadOperation.ExportLayouts||operation==CadOperation.WriteDsd||operation==CadOperation.PublishDsd)using(var folderDialog=new FolderBrowserDialog{Description="Папка результата",SelectedPath=folder.Text}){if(folderDialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;request.Folder=folderDialog.SelectedPath;}
            string plan=string.Join(Environment.NewLine,request.Files.Select((n,i)=>operation==CadOperation.RenameLayouts?n+" → "+request.NamePattern.Replace("{Index}",(i+1).ToString()).Replace("{Name}",n):(i+1)+". "+n));
            if(operation==CadOperation.ExportLayouts)plan+="\r\n\r\nКаждый DWG: вся модель и один выбранный Layout. Зависимости остаются ссылками.";
            if(operation==CadOperation.WriteDsd||operation==CadOperation.PublishDsd)plan+="\r\n\r\nИспользуются СОХРАНЁННЫЕ Layout и их параметры печати. Для рамок модели сначала создайте Layout. Для фоновой публикации загрузите DSD в PUBLISH.";
            if(!ShowText("План операции Layout",plan,"Выполнить"))return;await RunOperationAsync(request);
        }
        private async Task PackageDrawingAsync()
        {
            using(var dialog=new FolderBrowserDialog{Description="Папка ZIP-комплекта",SelectedPath=folder.Text})
            {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;var choices=AskChecks("Упаковка зависимостей",new[]{("Добавить используемые файлы шрифтов",false)});if(choices==null)return;
                if(!ShowText("Состав ZIP","Текущая база DWG, доступные вложенные Xref, растры и PDF/DWF/DGN-подложки. Ссылки в копиях станут относительными. Исходные файлы не изменяются. Не найденные зависимости перечисляются в отчёте.","Создать комплект"))return;
                await RunOperationAsync(new CadOperationRequest{Operation=CadOperation.PackageDrawing,Folder=dialog.SelectedPath,Crossing=choices[0]});
            }
        }
        private async Task ResumeExportAsync()
        {
            using(var dialog=new OpenFileDialog{Title="Журнал вывода (папка .CadBatch-jobs рядом с PDF)",Filter="Журнал CAD Batch|*.cadbatchjob.json"})
            {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;string path=dialog.FileName;var checkpoint=CheckpointStore.Load(path);profile=checkpoint.Job.Profile;ApplyProfile();
                profile.Search.Files=checkpoint.Job.Rows.Select(r=>r.DocumentPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                var scan=await host.SearchAsync(profile,running!.Token);var restored=JobStore.Restore(checkpoint.Job,scan.Frames,out var warnings);ReplaceFrames(restored);diagnostics.AddRange(warnings);scaleNeedsSearch=false;
                var groups=CheckpointStore.Pending(checkpoint,restored);if(groups.Count==0){status.Text="Все PDF из журнала уже созданы и проверены по SHA-256.";return;}
                if(!ShowText("Возобновление вывода","Готовые PDF проверены по SHA-256 и будут пропущены.\r\nОсталось PDF: "+groups.Count+"; страниц: "+groups.Sum(g=>g.Items.Count)+"\r\n\r\n"+string.Join(Environment.NewLine,groups.Select(g=>g.Path)),"Продолжить вывод"))return;
                var request=new ExportRequest{Items=groups.SelectMany(g=>g.Items).ToList(),Groups=groups,PdfMode=checkpoint.Mode,Device=profile.Device,StyleSheet=profile.StyleSheet,ColorMode=profile.ColorMode,FitToPaper=profile.FitToPaper??true,OutputCompleted=file=>{RepeatCopies(file,groups);WriteIul(file,groups);CheckpointStore.Complete(checkpoint,file);CheckpointStore.Save(path,checkpoint);}};
                var report=await host.ExportAsync(request,ExportProgress(),running.Token);diagnostics.AddRange(report.Errors);diagnostics.AddRange(report.Details);status.Text=ExportReportText.State(report)+" · создано PDF: "+report.Files.Count;SaveOperationReport(ExportReportText.Format(report,request,host.Description));
            }
        }
        private async Task SearchFilesAsync(bool fromFolder)
        {
            string[] files;
            if(fromFolder)
            {
                using(var dialog=new FolderBrowserDialog{Description="Папка исходных DWG (откроются только для чтения)"})
                {
                    if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;
                    var choices=AskChecks("Поиск по папке",new[]{("Включать вложенные папки",false)});if(choices==null)return;
                    files=EnumerateDrawings(dialog.SelectedPath,choices[0]).OrderBy(f=>f,NaturalTextComparer.Instance).ToArray();
                }
            }
            else using(var dialog=new OpenFileDialog{Title="Исходные DWG",Filter="Чертежи DWG|*.dwg",Multiselect=true}){if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;files=dialog.FileNames;}
            if(files.Length==0)throw new ArgumentException("DWG-файлы не найдены.");
            if(!ShowText("План поиска",files.Length+" DWG. Закрытые файлы откроются в AutoCAD только для чтения и останутся открыты для показа рамок и вывода PDF. Сохранение исходных DWG не выполняется.\r\n\r\n"+string.Join(Environment.NewLine,files),"Найти рамки"))return;
            profile.Search.Region=null;profile.Search.Files=files.ToList();await ScanAsync();
        }
        private static IEnumerable<string> EnumerateDrawings(string folder,bool recursive)
        {
            foreach(var file in Directory.EnumerateFiles(folder,"*.dwg",SearchOption.TopDirectoryOnly))yield return Path.GetFullPath(file);
            if(recursive)foreach(var dir in Directory.EnumerateDirectories(folder))
            {
                if((File.GetAttributes(dir)&FileAttributes.ReparsePoint)!=0)continue;
                foreach(var file in EnumerateDrawings(dir,true))yield return file;
            }
        }
        private void EditMetadata()
        {
            var dialog=new StampZonesWindow(profile.Metadata,DialogHandle);
            if(dialog.ShowDialog()==true){profile.Metadata=dialog.Settings;MarkScaleDirty();}
        }
    }
}
