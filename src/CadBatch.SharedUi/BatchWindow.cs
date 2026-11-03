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
        private static double Number(string value)=>double.Parse(value.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture);
        private async Task RunAsync(Func<Task> action)
        {
            if(running!=null)return;
            running=new CancellationTokenSource();search.Enabled=false;pdf.Enabled=singlePdf.Enabled=show.Enabled=false;cancel.Enabled=true;toolsMenu.Enabled=false;grid.Enabled=false;findBox.Enabled=outputBox.Enabled=false;workspaceToolbar.Enabled=false;
            try { await action(); }
            catch(OperationCanceledException) { status.Text="Операция отменена."; }
            catch(Exception ex) { diagnostics.Add(ex.ToString());status.Text=ex.Message;MessageBox.Show(DialogOwner,ex.Message,"CAD Batch",MessageBoxButtons.OK,MessageBoxIcon.Warning); }
            finally { running.Dispose();running=null;search.Enabled=show.Enabled=true;pdf.Enabled=singlePdf.Enabled=host.CanPlot;cancel.Enabled=false;progress.Value=0;toolsMenu.Enabled=true;grid.Enabled=true;findBox.Enabled=outputBox.Enabled=true;workspaceToolbar.Enabled=true;RefreshSelection();pdf.Enabled=singlePdf.Enabled=host.CanPlot; }
        }
        public Task SearchForSmokeTestAsync()=>RunAsync(ScanAsync);
        private async Task ScanAsync()
        {
            ReadProfile();status.Text="Чтение объектов и поиск рамок…";
            var result=await host.SearchAsync(profile,running!.Token);
            FrameOrder.PreserveEdits(frames,result.Frames);
            diagnostics=result.Warnings;frames.RaiseListChangedEvents=false;
            try { frames.Clear();foreach(var f in result.Frames)frames.Add(f); }
            finally { frames.RaiseListChangedEvents=true;frames.ResetBindings(); }
            scaleNeedsSearch=false;
            if(string.IsNullOrWhiteSpace(folder.Text)&&frames.Count>0&&Path.IsPathRooted(frames[0].Source.DocumentPath))folder.Text=Path.Combine(Path.GetDirectoryName(frames[0].Source.DocumentPath)!,"PDF");
            status.Text=$"Найдено: {frames.Count} • проверено контуров: {result.ObjectsVisited} • совпадений: {result.Duplicates} • замечаний: {diagnostics.Count}";
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
                items=OutputPlanner.Build(selected,profile.OutputFolder,profile.FilePattern);
            }
            finally {if(selectedOnly)CurrentFrame().Enabled=wasEnabled;}
            var mode=selectedOnly?PdfOutputMode.Separate:profile.PdfMode;
            var groups=PdfGroupPlanner.Build(items,mode,profile.OutputFolder,profile.CombinedPdfName??"Комплект");
            var baseSettings=new ExportRequest{Device=profile.Device,StyleSheet=profile.StyleSheet,FitToPaper=fit.Checked,ColorMode=profile.ColorMode};
            var printPlan=items.Select(i=>new{Frame=i.Frame,Settings=PrintTemplates.Resolve(baseSettings,i.Frame)}).ToList();
            foreach(var entry in printPlan)if(entry.Settings.Device.IndexOf("pdf",StringComparison.OrdinalIgnoreCase)<0||!entry.Settings.Device.EndsWith(".pc3",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Формат "+entry.Frame.FormatName+": для PDF задано устройство «"+entry.Settings.Device+"». Выберите PDF PC3 в шаблоне или настройке строки.");
            var lines=groups.Take(7).Select(i=>Path.GetFileName(i.Path)+" — страниц: "+i.Items.Count);
            string message=$"Будет создано PDF: {groups.Count}\nВсего страниц: {items.Count}\nУстройство: {profile.Device}\nПапка: {profile.OutputFolder}\n\n"+string.Join("\n",lines)+(groups.Count>7?"\n…":"")+"\n\nКопии для PDF игнорируются. Существующие файлы сохранятся. Начать вывод?";
            message+="\n\nНастройки по форматам / строкам:\n"+string.Join("\n",printPlan.Select(e=>e.Frame.FormatName+": "+e.Settings.Device+"; "+(e.Settings.Media.Length==0?"бумага по рамке":e.Settings.Media)+"; "+PrintStylePolicy.Describe(e.Settings.ColorMode)).Distinct().Take(10));
            var overrides=items.Where(i=>!string.IsNullOrWhiteSpace(i.Frame.Device)||!string.IsNullOrWhiteSpace(i.Frame.StyleSheet)).ToList();
            if(overrides.Count>0)message+="\n\nСобственные настройки строк: "+overrides.Count+"\n"+string.Join("\n",overrides.Take(10).Select(i=>i.Frame.Page+": "+i.Frame.Device+" / "+i.Frame.StyleSheet));
            if(MessageBox.Show(DialogOwner,message,"План вывода PDF",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
            var request=new ExportRequest { Items=items,Device=profile.Device,StyleSheet=profile.StyleSheet,FitToPaper=fit.Checked,ColorMode=profile.ColorMode,PdfMode=mode,Groups=groups };
            var checkpoint=CheckpointStore.Create(profile,frames.ToList(),groups,mode);
            string checkpointPath=Path.Combine(profile.OutputFolder,".CadBatch-jobs","Вывод-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".cadbatchjob.json");CheckpointStore.Save(checkpointPath,checkpoint);
            request.OutputCompleted=path=>{CheckpointStore.Complete(checkpoint,path);CheckpointStore.Save(checkpointPath,checkpoint);};
            var report=await host.ExportAsync(request,new Progress<BatchProgress>(p=>
            { progress.Maximum=Math.Max(1,p.Total);progress.Value=Math.Min(p.Done,progress.Maximum);status.Text=p.Text;frames.ResetBindings(); }),running!.Token);
            diagnostics.Add("Журнал для возобновления: "+checkpointPath);
            diagnostics.AddRange(report.Errors);diagnostics.AddRange(report.Details);frames.ResetBindings();
            Directory.CreateDirectory(profile.OutputFolder);
            string reportPath=Path.Combine(profile.OutputFolder,"CadBatch-report-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".txt");
            File.WriteAllText(reportPath,ExportReportText.Format(report,request,host.Description),Encoding.UTF8);
            status.Text=ExportReportText.State(report)+$" • PDF: {report.Files.Count} • ошибок: {report.Errors.Count}";
            if(report.Errors.Count>0)
                MessageBox.Show(DialogOwner,"PDF создано: "+report.Files.Count+".\n\n"+report.Errors[0]+"\n\nПодробный отчёт:\n"+reportPath,"Ошибка вывода PDF",MessageBoxButtons.OK,MessageBoxIcon.Warning);
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
                ReadProfile();using(var dialog=new ProfileDialog(profile,host))
                    if(dialog.ShowDialog(DialogOwner)==DialogResult.OK) { profile=dialog.Profile;ApplyProfile();
                        foreach(var f in frames){var format=profile.Formats.FirstOrDefault(p=>p.Name==f.FormatName&&p.WidthMm==f.Format.WidthMm&&p.HeightMm==f.Format.HeightMm);if(format!=null)f.Format=format;else MarkScaleDirty();} }
            }
            catch(Exception ex) { MessageBox.Show(DialogOwner,ex.Message,"Настройки"); }
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
            catch(Exception ex) { MessageBox.Show(DialogOwner,ex.Message,"CSV"); }
        }
        private void ShowDiagnostics()
        {
            using(var dialog=new Form { Text="Диагностика CAD Batch",Size=new Size(850,500),StartPosition=FormStartPosition.CenterParent })
            {
                string text=diagnostics.Count==0?"Замечаний нет.":string.Join(Environment.NewLine+Environment.NewLine,diagnostics);
                var save=new Button {Text="Сохранить диагностику в TXT…",Dock=DockStyle.Bottom,Height=38};
                save.Click+=(s,e)=>
                {
                    using(var file=new SaveFileDialog {Filter="Текстовый отчёт|*.txt",FileName="CadBatch-diagnostics.txt"})
                        if(file.ShowDialog(dialog)==DialogResult.OK)
                            try {File.WriteAllText(file.FileName,host.Description+Environment.NewLine+text,Encoding.UTF8);}catch(Exception ex){MessageBox.Show(dialog,ex.Message);}
                };
                dialog.Controls.Add(new TextBox { Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,Dock=DockStyle.Fill,Font=new Font("Consolas",10),Text=text });
                dialog.Controls.Add(save);
                dialog.ShowDialog(DialogOwner);
            }
        }
        private void UpdateCount() { RefreshWorkspaceCount();if(running==null)status.Text=$"Рамок: {frames.Count} • отмечено: {frames.Count(f=>f.Enabled)}"; }
    }
}
