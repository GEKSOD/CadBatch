using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CadBatch.Core;

namespace CadBatch.UI
{
    public sealed partial class BatchWindow : Form
    {
        private readonly IBatchHost host;
        private ProjectProfile profile = new ProjectProfile();
        private readonly BindingList<DetectedFrame> frames = new BindingList<DetectedFrame>();
        private readonly DataGridView grid = new DataGridView();
        private readonly CheckBox model = Check("Модель",true), layouts=Check("Листы"), allDocs=Check("Все открытые DWG");
        private readonly CheckBox polylines=Check("Полилинии",true),blocks=Check("Вложенные блоки",true), fit=Check("Вписать в бумагу",true);
        private readonly CheckBox stampScale=Check("По штампу");
        private readonly Label stampHint=Label("Верхняя линия: 185 мм • параметры в меню «Поиск»");
        private readonly Panel findBox=new Panel();
        private bool scaleNeedsSearch;
        private readonly TextBox scales=Field("1",100), tolerance=Field("2",55),layer=Field("*",130),block=Field("*",130);
        private readonly TextBox folder=Field("",400),pattern=Field("{Document}_{Page}_{Format}",300),device=Field("DWG To PDF.pc3",190),style=Field("",150);
        private readonly ComboBox color=new ComboBox {DropDownStyle=ComboBoxStyle.DropDownList,Width=170,Margin=new Padding(3,4,10,3)};
        private readonly CheckBox onePdf=Check("В один PDF");
        private readonly Label pdfNameLabel=Label("Имя:");
        private bool updatingPdfMode;
        private readonly Button search=Button("Найти рамки",Color.FromArgb(34,93,192)), pdf=Button("PDF отмеченных",Color.FromArgb(29,125,90));
        private readonly Button singlePdf=SmallButton("PDF выбранной"), show=SmallButton("Показать");
        private readonly Button cancel=Button("Отмена",Color.FromArgb(158,71,38));
        private readonly Label status=new Label { AutoSize=true,Text="Настройте поиск и нажмите «Найти рамки».",ForeColor=Color.FromArgb(48,63,80),Margin=new Padding(8,12,8,8) };
        private readonly ProgressBar progress=new ProgressBar { Width=170,Height=15,Margin=new Padding(12,13,8,8) };
        private readonly Panel outputBox=new Panel();
        private CancellationTokenSource? running;
        private List<string> diagnostics=new List<string>();
        private readonly string profilePath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CadBatch","profile.json");
        public int FrameCount=>frames.Count;
        public string ColorModeText=>color.Text;

        public BatchWindow(IBatchHost host,string? settingsFile=null)
        {
            this.host=host;
            if(settingsFile!=null)profilePath=Path.GetFullPath(settingsFile);
            Text="CAD Batch "+ProductInfo.Version+" • Пакетная печать";
            BuildWorkspace();
            stampScale.CheckedChanged+=(s,e)=>MarkScaleDirty();
            scales.TextChanged+=(s,e)=>MarkScaleDirty();
            color.Items.AddRange(new object[]{"Цвета чертежа","Стиль из DWG","Монохромный","Указанный CTB/STB"});color.SelectedIndex=0;
            color.SelectedIndexChanged+=(s,e)=>{style.Enabled=color.SelectedIndex==(int)PrintColorMode.CustomStyle;RefreshPdfMode();};
            onePdf.CheckedChanged+=(s,e)=>{if(!updatingPdfMode)ChangePdfMode(onePdf.Checked?PdfOutputMode.OneFile:PdfOutputMode.Separate);};
            search.Click+=async(s,e)=>await RunAsync(ScanAsync);
            pdf.Click+=async(s,e)=>await RunAsync(()=>ExportAsync(false));
            singlePdf.Click+=async(s,e)=>await RunAsync(()=>ExportAsync(true));
            cancel.Enabled=false;cancel.Click+=(s,e)=>running?.Cancel();
            show.Click+=async(s,e)=>await RunAsync(LocateAsync);
            grid.DataSource=frames;
            grid.CurrentCellDirtyStateChanged+=(s,e)=>{if(grid.IsCurrentCellDirty)grid.CommitEdit(DataGridViewDataErrorContexts.Commit);};
            grid.CellValueChanged+=(s,e)=>{UpdateCount();RefreshSelection();};
            grid.SelectionChanged+=(s,e)=>RefreshSelection();
            grid.DataError+=(s,e)=>{e.ThrowException=false;status.Text="Проверьте введённое значение.";};
            grid.CellDoubleClick+=async(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex>=0&&grid.Columns[e.ColumnIndex].ReadOnly)await RunAsync(LocateAsync);};
            frames.ListChanged+=(s,e)=>RefreshWorkspaceCount();
            InitializeTools(workspace);
            DecorateMenu();
            pdf.Enabled=singlePdf.Enabled=host.CanPlot;
            if(File.Exists(profilePath))
                try { profile=ProfileStore.Load(profilePath); } catch(Exception ex) { diagnostics.Add("Профиль не загружен: "+ex.Message); }
            ApplyProfile();
            FormClosing+=(s,e)=>
            {
                if(running!=null) { e.Cancel=true;running.Cancel();status.Text="Отмена операции. Закройте окно после её завершения.";return; }
                try { ReadProfile();ProfileStore.Save(profilePath,profile); } catch(Exception ex) { diagnostics.Add(ex.Message); }
            };
        }
        private void Column(string property,string caption,int width,bool readOnly=true)=>grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName=property,HeaderText=caption,Width=width,ReadOnly=readOnly,SortMode=DataGridViewColumnSortMode.NotSortable });
        private static CheckBox Check(string text,bool value=false)=>new CheckBox {Text=text,Checked=value,AutoSize=true,AccessibleName=text};
        private static TextBox Field(string text,int width)=>new TextBox { Text=text,Width=width,Margin=new Padding(3,4,10,3) };
        private static Label Label(string text)=>new Label { Text=text,AutoSize=true,Margin=new Padding(4,8,4,4),ForeColor=Color.FromArgb(60,77,96) };
        private static Button Button(string text,Color color)=>new Button {Text=text,AutoSize=true,BackColor=color,ForeColor=Color.White};
        private static Button SmallButton(string text)=>new Button {Text=text,AutoSize=true,Height=34,Padding=new Padding(8,3,8,3)};
        private static void Add(Control parent,params Control[] children) { foreach(var c in children)parent.Controls.Add(c); }

        private void ApplyProfile()
        {
            var s=profile.Search;model.Checked=s.Model;layouts.Checked=s.Layouts;allDocs.Checked=s.AllOpenDocuments;
            polylines.Checked=s.Polylines;blocks.Checked=s.Blocks;layer.Text=s.LayerPattern;block.Text=s.BlockPattern;
            scales.Text=string.Join("; ",s.Scales.Select(x=>x.ToString(CultureInfo.CurrentCulture)));
            stampScale.Checked=s.Stamp?.Enabled==true;
            RefreshStampHint();
            tolerance.Text=s.SizeToleranceMm.ToString(CultureInfo.CurrentCulture);folder.Text=profile.OutputFolder;
            pattern.Text=profile.PdfMode==PdfOutputMode.OneFile?(profile.CombinedPdfName??"Комплект"):profile.FilePattern;device.Text=profile.Device;style.Text=profile.StyleSheet;
            fit.Checked=profile.FitToPaper??true;
            color.SelectedIndex=(int)profile.ColorMode;style.Enabled=profile.ColorMode==PrintColorMode.CustomStyle;
            RefreshPdfMode();
        }
        private void ReadProfile()
        {
            profile.Search=new SearchOptions
            {
                Model=model.Checked,Layouts=layouts.Checked,AllOpenDocuments=allDocs.Checked,Polylines=polylines.Checked,Blocks=blocks.Checked,
                LayerPattern=layer.Text.Trim(),BlockPattern=block.Text.Trim(),SizeToleranceMm=Number(tolerance.Text),
                Scales=scales.Text.Split(new[]{';'},StringSplitOptions.RemoveEmptyEntries).Select(Number).ToList()
                ,Lines=profile.Search.Lines,ExternalReferences=profile.Search.ExternalReferences,Viewports=profile.Search.Viewports,ActiveLayoutOnly=profile.Search.ActiveLayoutOnly
                ,Files=profile.Search.Files,Region=profile.Search.Region,SpecialEntities=profile.Search.SpecialEntities
                ,Stamp=profile.Search.Stamp?.Copy()??new StampScaleSettings()
            };
            profile.Search.Stamp.Enabled=stampScale.Checked;
            profile.OutputFolder=folder.Text.Trim();SaveVisiblePdfName();profile.Device=device.Text.Trim();profile.StyleSheet=style.Text.Trim();profile.ColorMode=(PrintColorMode)color.SelectedIndex;profile.FitToPaper=fit.Checked;profile.Validate();
        }
        private static double Number(string value)
        {
            if(double.TryParse((value??"").Trim().Replace(',','.').Replace(" ",""),NumberStyles.Float,CultureInfo.InvariantCulture,out double number))return number;
            throw new ArgumentException("Введите число вместо «"+(value??"").Trim()+"».");
        }
        private static int Integer(string value,string caption)
        {
            if(int.TryParse((value??"").Trim(),NumberStyles.Integer,CultureInfo.InvariantCulture,out int number))return number;
            throw new ArgumentException(caption+": введите целое число вместо «"+(value??"").Trim()+"».");
        }
        private IntPtr DialogHandle=>DialogOwner.Handle;
        private void Notify(string text,string title="CAD Batch",DialogKind kind=DialogKind.Warning)=>ModernDialog.Message(DialogHandle,title,text,kind);
        // Last finished PDF batch: the main window offers to open its folder.
        public string? LastOutputFolder {get;private set;}
        public int LastOutputCount {get;private set;}
        public List<string> LastOutputFiles {get;private set;}=new List<string>();
        // The visible shell explains errors in plain words (returns true when it showed them) and remembers jobs.
        private Func<Exception,bool>? ErrorPresenter;
        private Action<string>? JobRemembered;
        private async Task RunAsync(Func<Task> action)
        {
            if(running!=null)return;
            running=new CancellationTokenSource();search.Enabled=false;pdf.Enabled=singlePdf.Enabled=show.Enabled=false;cancel.Enabled=true;toolsMenu.Enabled=false;grid.Enabled=false;findBox.Enabled=outputBox.Enabled=false;workspaceToolbar.Enabled=false;
            try { await action(); }
            catch(OperationCanceledException) { status.Text="Операция отменена."; }
            catch(Exception ex) { diagnostics.Add(ex.ToString());ErrorLog.Write("Операция",ex);status.Text=ex.Message;if(ErrorPresenter?.Invoke(ex)!=true)Notify(ex.Message,ex is ArgumentException?"Проверьте настройки":"Не получилось"); }
            finally { running.Dispose();running=null;search.Enabled=show.Enabled=true;pdf.Enabled=singlePdf.Enabled=host.CanPlot;cancel.Enabled=false;progress.Value=0;toolsMenu.Enabled=true;grid.Enabled=true;findBox.Enabled=outputBox.Enabled=true;workspaceToolbar.Enabled=true;RefreshSelection();pdf.Enabled=singlePdf.Enabled=host.CanPlot; }
        }
        public Task SearchForSmokeTestAsync()=>RunAsync(ScanAsync);
        private async Task ScanAsync()
        {
            ReadProfile();status.Text="Чтение объектов и поиск рамок…";
            var result=await host.SearchAsync(profile,running!.Token);
            var empty=result;widerWarnings=new List<string>();
            result=result.Frames.Count==0?await WiderSearchAsync()??result:await MissedFramesAsync(result)??result;
            searchedEmpty=result.Frames.Count==0;
            FrameOrder.PreserveEdits(frames,result.Frames);
            diagnostics=result.Warnings;frames.RaiseListChangedEvents=false;
            try { frames.Clear();foreach(var f in result.Frames)frames.Add(f); }
            finally { frames.RaiseListChangedEvents=true;frames.ResetBindings(); }
            scaleNeedsSearch=false;
            if(string.IsNullOrWhiteSpace(folder.Text)&&frames.Count>0&&Path.IsPathRooted(frames[0].Source.DocumentPath))folder.Text=Path.Combine(Path.GetDirectoryName(frames[0].Source.DocumentPath)!,"PDF");
            diagnostics.Insert(0,$"Поиск: найдено {frames.Count}, проверено контуров {result.ObjectsVisited}, совпадений {result.Duplicates}.");
            diagnostics.AddRange(widerWarnings.Select(w=>"Расширенный поиск: "+w));
            status.Text=frames.Count==0?"Рамки не найдены — ни при текущих, ни при расширенных настройках.":"Найдено листов: "+frames.Count+(diagnostics.Count>1?" · есть замечания — «Справка → Диагностика»":"");
            if(frames.Count==0&&!widerDeclined)ExplainNothingFound(empty.Warnings.Concat(widerWarnings));
        }
        // True after a search that found nothing: the empty table then says so instead of inviting a first search.
        private bool searchedEmpty,widerDeclined;private List<string> widerWarnings=new List<string>();
        // Nothing found with the user's settings: search everywhere, then with the common scales, and offer to keep
        // only the settings the found frames need. A new computer starts with "model only, scale 1".
        private async Task<ScanResult?> WiderSearchAsync()
        {
            widerDeclined=false;var original=profile.Search;
            for(int stage=1;stage<=SearchFallback.Stages;stage++)
            {
                status.Text=stage==1?"Рамок нет. Ищу везде: модель, листы, блоки, отрезки, штамп…":"Пробую распространённые масштабы…";
                var found=await SearchWithAsync(SearchFallback.Wider(original,stage));
                if(found==null)continue;
                if(found.Frames.Count==0){widerWarnings=found.Warnings;continue;}
                var offered=await OfferAsync(original,found.Frames,shown=>"С текущими настройками поиска рамок нет, но с другими нашлось листов: "+shown.Count+" ("+Where(shown)+").","Рамки нашлись при других настройках","Показать и запомнить",found);
                if(offered!=null||widerDeclined)return offered;
            }
            return null;
        }
        // Some frames found: once per drawing and settings in this session, check whether wider settings find more
        // (frames drawn as separate lines, in blocks of an Xref, on layout tabs) and offer to add them.
        private readonly HashSet<string> missedChecked=new HashSet<string>(StringComparer.Ordinal);
        private async Task<ScanResult?> MissedFramesAsync(ScanResult current)
        {
            var original=profile.Search;var wider=SearchFallback.Wider(original,1);
            if(SearchFallback.Signature(wider)==SearchFallback.Signature(original))return null;
            string key=SearchFallback.Signature(original)+"#"+string.Join(",",current.Frames.Select(f=>f.Source.DocumentKey).Distinct(StringComparer.Ordinal).OrderBy(k=>k,StringComparer.Ordinal));
            if(!missedChecked.Add(key))return null;
            status.Text="Найдено листов: "+current.Frames.Count+". Проверяю, не пропущены ли другие рамки…";
            var found=await SearchWithAsync(wider);if(found==null)return null;
            var extra=found.Frames.Where(f=>!current.Frames.Any(c=>c.Source.DocumentKey==f.Source.DocumentKey&&c.Layout==f.Layout&&c.Geometry.SameBoundary(f.Geometry,Math.Max(original.GeometryTolerance,.01)))).ToList();
            if(extra.Count==0)return null;
            return await OfferAsync(original,extra,shown=>"Найдено листов: "+current.Frames.Count+". Ещё "+shown.Count+" рамок текущие настройки пропускают ("+Where(shown)+").","Найдены ещё рамки","Добавить и запомнить",null);
        }
        // A search with other settings; its failure (a busy plan, a broken object) never spoils the main result.
        private async Task<ScanResult?> SearchWithAsync(SearchOptions options)
        {
            var original=profile.Search;profile.Search=options;
            try{return await host.SearchAsync(profile,running!.Token);}
            catch(Exception ex) when(!(ex is OperationCanceledException)){ErrorLog.Write("Расширенный поиск",ex);widerWarnings.Add(ex.Message);return null;}
            finally{profile.Search=original;}
        }
        private static string Where(IEnumerable<DetectedFrame> found)=>string.Join(", ",found.Select(f=>string.Equals(f.Layout,"Model",StringComparison.OrdinalIgnoreCase)?"модель":"лист «"+f.Layout+"»").Distinct().Take(4));
        // Asks to keep the settings the given frames need; on yes searches again with them.
        private async Task<ScanResult?> OfferAsync(SearchOptions original,IReadOnlyCollection<DetectedFrame> needed,Func<List<DetectedFrame>,string> leadFor,string title,string accept,ScanResult? fallback)
        {
            var adopted=SearchFallback.Adopt(original,needed,profile.Formats,out var alternatives);var changes=SearchFallback.Changes(original,adopted);
            // Small rectangles that fit only at odd scales are not offered; nothing left means nothing to ask about.
            var shown=SearchFallback.Explained(adopted,needed,profile.Formats);if(shown.Count==0||changes.Count==0)return null;
            string lead=leadFor(shown);
            if(alternatives.Count>0)changes.Add("масштаб в чертеже без штампа определён не однозначно: подходят и "+string.Join(", ",alternatives.Take(2).Select(SearchFallback.Scale))+". Проверьте форматы листов в таблице");
            string text=lead+"\n\n"+(changes.Count>0?"Что изменится в настройках:\n• "+string.Join("\n• ",changes)+"\n\n":"")+(fallback==null?"Добавить":"Показать")+" эти листы и запомнить настройки для следующих поисков?";
            if(ModernDialog.Actions(DialogHandle,title,text,DialogKind.Question,accept,"Не надо")!=0){widerDeclined=true;return null;}
            profile.Search=adopted;ApplyProfile();
            try{ProfileStore.Save(profilePath,profile);}catch(Exception ex){diagnostics.Add("Настройки не сохранены: "+ex.Message);}
            status.Text="Поиск с новыми настройками…";
            var again=await host.SearchAsync(profile,running!.Token);
            return again.Frames.Count>0?again:fallback;
        }
        private void ExplainNothingFound(IEnumerable<string> warnings)
        {
            string detail=warnings.Where(w=>w.StartsWith(FrameDetection.NothingFoundPrefix,StringComparison.Ordinal)).Select(w=>w.Substring(FrameDetection.NothingFoundPrefix.Length)).LastOrDefault()??"";
            string text="CAD Batch проверил модель и все листы, полилинии, блоки, отрезки, внешние ссылки и распространённые масштабы — рамок стандартных форматов (А0–А4 и кратных) нет.\n\nЧто проверить:\n• В AutoCAD открыт и активен нужный чертёж.\n• Рамка — это замкнутая полилиния, блок или четыре отрезка точно по размеру листа.\n• Если рамка нестандартного размера, нажмите «Собрать диагностику» и отправьте файл разработчику.";
            if(detail.Length>0)text+="\n\nЧто нашлось в чертеже: "+detail;
            if(ModernDialog.Actions(DialogHandle,"Рамки не найдены",text,DialogKind.Info,"Понятно","Собрать диагностику")==1)CollectDiagnostics();
        }
        private async Task LocateAsync()
        {
            var frame=CurrentFrame();
            await host.LocateAsync(frame);
            status.Text="Показана рамка №"+frame.Page+" ("+frame.FormatName+"). Посмотрите на область чертежа в AutoCAD.";
        }
        private DetectedFrame CurrentFrame()
        {
            RequireFreshScale();return fluentSelection??grid.CurrentRow?.DataBoundItem as DetectedFrame??throw new InvalidOperationException("Сначала щёлкните по номеру строки в таблице рамок.");
        }
        private async Task ExportAsync(bool selectedOnly)
        {
            RequireFreshScale();
            grid.EndEdit();ReadProfile();
            IEnumerable<DetectedFrame> selected=frames;
            if(selectedOnly)selected=new[]{CurrentFrame()};
            // A single-frame test uses the current row, independently of the batch checkboxes.
            bool wasEnabled=selectedOnly&&CurrentFrame().Enabled;
            List<OutputItem> items;
            try
            {
                if(selectedOnly)CurrentFrame().Enabled=true;
                items=OutputPlanner.Build(selected,profile.OutputFolder,profile.FilePattern,".pdf",profile.OverwriteExisting==true);
            }
            finally {if(selectedOnly)CurrentFrame().Enabled=wasEnabled;}
            var mode=selectedOnly?PdfOutputMode.Separate:profile.PdfMode;
            var groups=PdfGroupPlanner.Build(items,mode,profile.OutputFolder,profile.CombinedPdfName??"Комплект",profile.OverwriteExisting==true);LastBatch=items.Select(i=>i.Frame).ToList();
            var baseSettings=new ExportRequest{Device=profile.Device,StyleSheet=profile.StyleSheet,FitToPaper=fit.Checked,ColorMode=profile.ColorMode};
            var printPlan=items.Select(i=>new{Frame=i.Frame,Settings=PrintTemplates.Resolve(baseSettings,i.Frame)}).ToList();
            foreach(var entry in printPlan)if(entry.Settings.Device.IndexOf("pdf",StringComparison.OrdinalIgnoreCase)<0||!entry.Settings.Device.EndsWith(".pc3",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Формат "+entry.Frame.FormatName+": для PDF задано устройство «"+entry.Settings.Device+"». Выберите PDF PC3 в шаблоне или настройке строки.");
            string message="Файлы:\n"+string.Join("\n",groups.Take(40).Select(i=>Path.GetFileName(i.Path)+(i.Items.Count>1?" — страниц: "+i.Items.Count:"")))+(groups.Count>40?"\n… и ещё "+(groups.Count-40):"");
            message+="\n\nНастройки по форматам:\n"+string.Join("\n",printPlan.Select(e=>e.Frame.FormatName+": "+e.Settings.Device+"; "+(e.Settings.Media.Length==0?"бумага по рамке":e.Settings.Media)+"; "+PrintStylePolicy.Describe(e.Settings.ColorMode)).Distinct().Take(10));
            var overrides=items.Where(i=>!string.IsNullOrWhiteSpace(i.Frame.Device)||!string.IsNullOrWhiteSpace(i.Frame.StyleSheet)).ToList();
            if(overrides.Count>0)message+="\n\nСобственные настройки строк: "+overrides.Count+"\n"+string.Join("\n",overrides.Take(10).Select(i=>i.Frame.Page+": "+i.Frame.Device+" / "+i.Frame.StyleSheet));
            message+=(profile.RepeatCopiesInPdf==true?"\n\nЛисты с копиями повторяются в PDF нужное число раз.":"\n\nКопии для PDF не учитываются (включается в «Плоттер и шаблоны»).")+""+(profile.OverwriteExisting==true?" Файлы с теми же именами будут заменены.":" Существующие файлы не перезаписываются: новым файлам добавляется номер.")+(profile.Iul?.Enabled==true?" Для каждого PDF будет создан ИУЛ.":"");
            var facts=new[]{("PDF-файлов",groups.Count.ToString()),("Страниц",items.Count.ToString()),("Папка",profile.OutputFolder),("Плоттер",profile.Device),("Цвет",color.Text)};
            if(!ModernDialog.Plan(DialogHandle,selectedOnly?"PDF выбранного листа":"Вывод PDF","Проверьте план. Вывод можно отменить в любой момент, готовые файлы сохранятся.",facts,message,groups.Count==1?"Создать PDF":"Создать "+groups.Count+" PDF"))return;
            var request=new ExportRequest { Items=items,Device=profile.Device,StyleSheet=profile.StyleSheet,FitToPaper=fit.Checked,ColorMode=profile.ColorMode,PdfMode=mode,Groups=groups };
            var checkpoint=CheckpointStore.Create(profile,frames.ToList(),groups,mode);
            string checkpointPath=Path.Combine(profile.OutputFolder,".CadBatch-jobs","Вывод-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".cadbatchjob.json");CheckpointStore.Save(checkpointPath,checkpoint);
            request.OutputCompleted=path=>{RepeatCopies(path,groups);WriteIul(path,groups);CheckpointStore.Complete(checkpoint,path);CheckpointStore.Save(checkpointPath,checkpoint);};
            var report=await host.ExportAsync(request,new Progress<BatchProgress>(p=>
            { progress.Maximum=Math.Max(1,p.Total);progress.Value=Math.Min(p.Done,progress.Maximum);status.Text=p.Text;frames.ResetBindings(); }),running!.Token);
            diagnostics.Add("Журнал для возобновления: "+checkpointPath);
            diagnostics.AddRange(report.Errors);diagnostics.AddRange(report.Details);frames.ResetBindings();foreach(string error in report.Errors)ErrorLog.Write("Вывод PDF",error);
            Directory.CreateDirectory(profile.OutputFolder);
            string reportPath=Path.Combine(profile.OutputFolder,"CadBatch-report-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".txt");
            File.WriteAllText(reportPath,ExportReportText.Format(report,request,host.Description),Encoding.UTF8);
            LastOutputFolder=report.Files.Count>0?profile.OutputFolder:null;LastOutputCount=report.Files.Count;LastOutputFiles=report.Files.ToList();
            status.Text=report.Cancelled?"Вывод отменён · создано PDF: "+report.Files.Count:report.Errors.Count>0?"Создано PDF: "+report.Files.Count+" · ошибок: "+report.Errors.Count:"Готово · создано PDF: "+report.Files.Count;
            if(report.Errors.Count>0)
                Notify("Создано PDF: "+report.Files.Count+".\n\n"+report.Errors[0]+"\n\nПодробный отчёт:\n"+reportPath,"Вывод завершён с ошибками");
        }
        // Runs before the checkpoint hash, so a resumed job sees the final file.
        private void RepeatCopies(string path,List<PdfGroup> groups)
        {
            if(profile.RepeatCopiesInPdf!=true)return;var group=groups.FirstOrDefault(g=>string.Equals(Path.GetFullPath(g.Path),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase));
            if(group==null||group.Items.All(i=>i.Frame.Copies<=1))return;
            try{CadBatch.Pdf.PdfRunner.Run(CadBatch.Pdf.PdfJob.Repeat(path,group.Items.Select(i=>Math.Max(1,i.Frame.Copies)).ToArray(),group.Items.Count>1?group.Items.Select(i=>i.Frame.Title).ToArray():null));}
            catch(Exception ex){diagnostics.Add("Копии не добавлены в "+Path.GetFileName(path)+": "+ex.Message);}
        }
        // Frames of the last batch: "repeat unfinished" picks those without a finished PDF.
        public List<DetectedFrame> LastBatch {get;private set;}=new List<DetectedFrame>();
        public static bool Finished(DetectedFrame frame)=>frame.Status.StartsWith("PDF создан",StringComparison.Ordinal)||frame.Status.StartsWith("В PDF",StringComparison.Ordinal);
        // ИУЛ is written from the final PDF (after copies), so its checksum matches the issued file.
        private void WriteIul(string path,List<PdfGroup> groups)
        {
            if(profile.Iul?.Enabled!=true)return;var group=groups.FirstOrDefault(g=>string.Equals(Path.GetFullPath(g.Path),Path.GetFullPath(path),StringComparison.OrdinalIgnoreCase));
            var first=group?.Items.FirstOrDefault()?.Frame;string title=group==null||group.Items.Count!=1?Path.GetFileNameWithoutExtension(path):first?.Title??"";
            try{CadBatch.Pdf.PdfRunner.Run(CadBatch.Pdf.PdfJob.Iul(path,IulSheet.PathFor(path),first?.Code??"",title,first?.Revision??"",profile.Iul));}
            catch(Exception ex){diagnostics.Add("ИУЛ не создан для "+Path.GetFileName(path)+": "+ex.Message);ErrorLog.Write("ИУЛ",ex);}
        }
        // ИУЛ for files already issued (PDF, DWG or any other document), without AutoCAD.
        private void IulForFiles()
        {
            using(var dialog=new OpenFileDialog{Title="Файлы, для которых нужен ИУЛ",Filter="Документы|*.pdf;*.dwg;*.docx;*.xlsx|Все файлы|*.*",Multiselect=true})
            {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;string designation="",title="",revision="";
                if(dialog.FileNames.Length==1){var values=AskValues("Данные для ИУЛ",("Обозначение документа (шифр)",Path.GetFileNameWithoutExtension(dialog.FileName)),("Наименование документа",Path.GetFileNameWithoutExtension(dialog.FileName)),("Номер последнего изменения","—"));if(values==null)return;designation=values[0];title=values[1];revision=values[2];}
                var made=new List<string>();
                foreach(string file in dialog.FileNames){string target=IulSheet.PathFor(file);CadBatch.Pdf.PdfRunner.Run(CadBatch.Pdf.PdfJob.Iul(file,target,designation,title,revision,profile.Iul));made.Add(target);}
                status.Text="Готово · ИУЛ создано: "+made.Count;LastOutputFolder=Path.GetDirectoryName(made[0]);
                ShowText("ИУЛ созданы",string.Join(Environment.NewLine,made)+Environment.NewLine+Environment.NewLine+"Контрольная сумма — CRC32. Проверьте форму по требованиям вашей экспертизы.");
            }
        }
        private void EditIulSigners()
        {
            var current=profile.Iul?.Copy()??new IulSettings();
            var values=AskValues("Подписи в ИУЛ",("Разработал",current.Developer),("Проверил",current.Checker),("Н. контроль",current.NormControl),("ГИП",current.Chief));if(values==null)return;
            current.Developer=values[0].Trim();current.Checker=values[1].Trim();current.NormControl=values[2].Trim();current.Chief=values[3].Trim();profile.Iul=current;
        }
        private void ChooseFolder()
        {
            using(var dialog=new FolderBrowserDialog { Description="Папка для PDF",SelectedPath=folder.Text })
                if(dialog.ShowDialog(DialogOwner)==DialogResult.OK)folder.Text=dialog.SelectedPath;
        }
        private void EditProfile()
        {
            if(running!=null)return;
            try
            {
                ReadProfile();var dialog=new PrintSettingsWindow(profile,host,DialogHandle);
                if(dialog.ShowDialog()==true) { profile=dialog.Profile;ApplyProfile();
                    foreach(var f in frames){var format=profile.Formats.FirstOrDefault(p=>p.Name==f.FormatName&&p.WidthMm==f.Format.WidthMm&&p.HeightMm==f.Format.HeightMm);if(format!=null)f.Format=format;else MarkScaleDirty();} }
            }
            catch(Exception ex) { Notify(ex.Message,"Шаблоны"); }
        }
        private void SaveCsv()
        {
            if(running!=null)return;
            try
            {
                using(var dialog=new SaveFileDialog { Filter="CSV UTF-8 (*.csv)|*.csv",FileName="Рамки.csv" })
                    if(dialog.ShowDialog(DialogOwner)==DialogResult.OK)
                    {
                        Func<string,string> q=RegisterExport.CsvCell;
                        var rows=new List<string> { "Страница;Чертёж;Вкладка;Формат;Номер листа;Наименование;Масштаб;Состояние" };
                        rows.AddRange(frames.Select(f=>string.Join(";",new[]{f.Page.ToString(),f.File,f.Layout,f.FormatName,f.SheetNumber,f.Title,f.Scale.ToString(CultureInfo.InvariantCulture),f.Status}.Select(q))));
                        File.WriteAllLines(dialog.FileName,rows,new UTF8Encoding(true));
                    }
            }
            catch(Exception ex) { Notify(ex.Message,"CSV"); }
        }
        private void ShowDiagnostics()
        {
            string text=diagnostics.Count==0?"Замечаний нет. Здесь появятся предупреждения поиска и подробности вывода.":string.Join(Environment.NewLine+Environment.NewLine,diagnostics);
            ModernDialog.Text(DialogHandle,"Диагностика",text,null,("Собрать диагностику (ZIP)…",owner=>CollectDiagnostics()));
        }
        // Everything support needs in one file the user can attach to a message: versions, this session, recent journals, settings.
        private void CollectDiagnostics()
        {
            using(var dialog=new SaveFileDialog{Filter="Архив ZIP|*.zip",FileName="CadBatch-диагностика-"+DateTime.Now.ToString("yyyyMMdd-HHmm",CultureInfo.InvariantCulture)+".zip",InitialDirectory=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)})
            {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;
                string folder=Path.GetDirectoryName(profilePath)!;
                ErrorLog.Collect(dialog.FileName,ErrorLog.Summary(host.Description),string.Join(Environment.NewLine+Environment.NewLine,diagnostics),new[]{profilePath,Path.Combine(folder,"interface.json"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CadBatch","preferences.json")});
                try{Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+dialog.FileName+"\""){UseShellExecute=true});}catch(System.ComponentModel.Win32Exception){}
                Notify("Файл сохранён:\n"+dialog.FileName+"\n\nОтправьте его разработчику. Чертежей в нём нет; в настройках есть названия папок.","Диагностика собрана",DialogKind.Success);
            }
        }
        private void UpdateCount() { RefreshWorkspaceCount();if(running==null)status.Text=$"Рамок: {frames.Count} • отмечено: {frames.Count(f=>f.Enabled)}"; }
    }
}
