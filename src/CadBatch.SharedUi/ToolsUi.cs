using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using CadBatch.Core;
using CadBatch.Pdf;

namespace CadBatch.UI
{
    public sealed partial class BatchWindow
    {
        private readonly MenuStrip toolsMenu=new MenuStrip();
        private readonly List<ToolStripMenuItem> pdfModes=new List<ToolStripMenuItem>();
        private double sortTolerance=2;
        public int ToolMenuCount=>toolsMenu.Items.OfType<ToolStripMenuItem>().Count();
        private void InitializeTools(Control main)
        {
            var shell=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=2,ColumnCount=1,Margin=new Padding(0),Padding=new Padding(0)};
            shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));shell.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            Controls.Remove(main);shell.Controls.Add(toolsMenu,0,0);shell.Controls.Add(main,0,1);Controls.Add(shell);MainMenuStrip=toolsMenu;
            toolsMenu.Dock=DockStyle.Fill;toolsMenu.BackColor=Color.White;toolsMenu.Font=new Font("Segoe UI",9);
            var job=new ToolStripMenuItem("Задание");toolsMenu.Items.Add(job);
            Item(job,"Сохранить задание…",SaveJob);Item(job,"Открыть задание…",async()=>await RunAsync(LoadJobAsync));
            Item(job,"Проверить номера листов",()=>ShowText("Проверка номеров",string.Join(Environment.NewLine,Numbering.Warnings(frames).DefaultIfEmpty("Пустых и повторяющихся номеров нет."))));
            Item(job,"По первой выбранной строке…",CopyFirst);Item(job,"Ведомость в CSV…",SaveRegisterCsv);Item(job,"Ведомость в XLSX…",SaveRegisterXlsx);
            var order=new ToolStripMenuItem("Порядок");toolsMenu.Items.Add(order);
            string[] labels={"Строками: слева направо","Строками: справа налево","Строками: снизу вверх","Столбцами: сверху вниз","Столбцами: снизу вверх","По номеру листа","По формату","По документу","По шифру"};
            for(int i=0;i<labels.Length;i++){var mode=(FrameSort)i;Item(order,labels[i],()=>ReplaceFrames(FrameOrder.Sort(frames,mode,sortTolerance)));}
            Item(order,"Допуск группировки…",()=>{var values=AskValues("Допуск сортировки",("Допуск в единицах DWG",sortTolerance.ToString(CultureInfo.CurrentCulture)));if(values!=null){double v=Number(values[0]);if(!Numeric.NonNegative(v))throw new ArgumentException("Допуск должен быть неотрицательным.");sortTolerance=v;}});
            Item(order,"Выбранные строки вверх",()=>MoveRows(-1));Item(order,"Выбранные строки вниз",()=>MoveRows(1));
            Item(order,"Нумерация в таблице…",NumberRows);
            var pdfMenu=new ToolStripMenuItem("PDF");toolsMenu.Items.Add(pdfMenu);
            string[] modes={"Отдельный PDF на рамку","Один PDF на всё задание","PDF по документам","PDF по форматам","PDF по шифрам"};
            for(int i=0;i<modes.Length;i++)
            {
                int index=i;var entry=Item(pdfMenu,modes[i],()=>ChangePdfMode((PdfOutputMode)index));pdfModes.Add(entry);
            }
            Item(pdfMenu,"Имя общего PDF…",()=>{SaveVisiblePdfName();var values=AskValues("Имя общего PDF",("Имя без расширения",profile.CombinedPdfName??"Комплект"));if(values!=null){profile.CombinedPdfName=values[0];if(profile.PdfMode==PdfOutputMode.OneFile)pattern.Text=values[0];}});
            Item(pdfMenu,"Предпросмотр выбранной рамки",async()=>await RunAsync(PreviewAsync),true);
            Item(pdfMenu,"Разбить выбранную рамку на малые листы…",async()=>await RunAsync(()=>TilePdfAsync(true)),true);
            Item(pdfMenu,"Разбить готовый одностраничный PDF…",async()=>await RunAsync(()=>TilePdfAsync(false)));
            Item(pdfMenu,"Список устройств AutoCAD",async()=>await RunAsync(async()=>ShowText("Устройства AutoCAD",string.Join(Environment.NewLine,await host.GetPlotDevicesAsync()))),true);
            var cad=new ToolStripMenuItem("Чертёж");toolsMenu.Items.Add(cad);
            Item(cad,"Создать листы из отмеченных рамок модели…",async()=>await RunAsync(CreateLayoutsAsync),true);
            Item(cad,"Экспорт отмеченных рамок модели в DWG…",async()=>await RunAsync(ExportDwgAsync),true);
            Item(cad,"Вставить ведомость в AutoCAD…",async()=>await RunAsync(()=>RunOperationAsync(new CadOperationRequest{Operation=CadOperation.InsertRegister,Frames=CheckedFrames()})),true);
            Item(cad,"Записать номера в атрибуты…",async()=>await RunAsync(WriteAttributesAsync),true);
            Item(cad,"Создать собственную рамку…",async()=>await RunAsync(CreateFrameAsync),true);
            Item(cad,"Собрать несколько DWG в новый файл…",async()=>await RunAsync(MergeDrawingsAsync),true);
            Item(cad,"Вставить несколько растров…",async()=>await RunAsync(InsertRastersAsync),true);
            var searchMenu=new ToolStripMenuItem("Поиск");toolsMenu.Items.Add(searchMenu);Item(searchMenu,"Дополнительные источники…",EditSearchSources);
            Item(searchMenu,"Параметры масштаба по штампу…",EditStampScale);
            Item(searchMenu,"Взять масштаб с верхней линии LINE…",async()=>await RunAsync(()=>MeasureStampAsync(false)),true);
            Item(searchMenu,"По двум концам верхней линии…",async()=>await RunAsync(()=>MeasureStampAsync(true)),true);
            AddAdvancedTools(job,cad,searchMenu,pdfMenu);
            var printMenu=new ToolStripMenuItem("Печать");toolsMenu.Items.Add(printMenu);Item(printMenu,"Отправить отмеченные рамки на принтер…",async()=>await RunAsync(PrintAsync),true);
        }
        private ToolStripMenuItem Item(ToolStripMenuItem menu,string text,Action action,bool cad=false)
        {
            var item=new ToolStripMenuItem(text){Enabled=!cad||host.CanPlot};item.Click+=(s,e)=>{if(running!=null)return;try{action();}catch(Exception ex){MessageBox.Show(DialogOwner,ex.Message,"CAD Batch",MessageBoxButtons.OK,MessageBoxIcon.Warning);}};
            menu.DropDownItems.Add(item);return item;
        }
        private void SaveVisiblePdfName()
        {
            if(profile.PdfMode==PdfOutputMode.OneFile)profile.CombinedPdfName=pattern.Text.Trim();else profile.FilePattern=pattern.Text.Trim();
        }
        public void VerifyCombinedPdfUiForSmokeTest()
        {
            var oldMode=profile.PdfMode;string oldPattern=profile.FilePattern,oldCombined=profile.CombinedPdfName,visible=pattern.Text;
            try
            {
                ChangePdfMode(PdfOutputMode.Separate);string separate=pattern.Text;
                onePdf.Checked=true;
                if(profile.PdfMode!=PdfOutputMode.OneFile||!pdfModes[(int)PdfOutputMode.OneFile].Checked||pdfNameLabel.Text!="Имя PDF:")throw new InvalidOperationException("Combined PDF checkbox/menu mismatch.");
                pattern.Text="Проверка общего PDF";onePdf.Checked=false;
                if(profile.PdfMode!=PdfOutputMode.Separate||profile.FilePattern!=separate||pattern.Text!=separate||profile.CombinedPdfName!="Проверка общего PDF")throw new InvalidOperationException("PDF mode switch lost a filename.");
                ChangePdfMode(PdfOutputMode.OneFile);
                if(!onePdf.Checked||pattern.Text!="Проверка общего PDF")throw new InvalidOperationException("Combined PDF name was not restored.");
            }
            finally{profile.PdfMode=oldMode;profile.FilePattern=oldPattern;profile.CombinedPdfName=oldCombined;pattern.Text=visible;RefreshPdfMode();}
        }
        private void ChangePdfMode(PdfOutputMode mode)
        {
            SaveVisiblePdfName();profile.PdfMode=mode;RefreshPdfMode(true);
        }
        private void RefreshPdfMode(bool loadName=false)
        {
            updatingPdfMode=true;
            try
            {
                bool combined=profile.PdfMode==PdfOutputMode.OneFile;onePdf.Checked=combined;pdfNameLabel.Text=combined?"Имя PDF:":"Имя:";
                if(loadName)pattern.Text=combined?(profile.CombinedPdfName??"Комплект"):profile.FilePattern;
                pdf.Text=combined?"Сохранить в один PDF":"PDF отмеченных";
            }
            finally{updatingPdfMode=false;}
            for(int i=0;i<pdfModes.Count;i++)pdfModes[i].Checked=i==(int)profile.PdfMode;
            RefreshWorkspaceCount();
            if(pdfModes.Count>0)
            {
                string mode=pdfModes[(int)profile.PdfMode].Text??"PDF";outputBox.Text="Вывод PDF • "+mode+" • "+color.Text;
                status.Text="Режим: "+mode;
            }
        }
        private void ReplaceFrames(IEnumerable<DetectedFrame> values)
        {
            var rows=values.ToList();var selected=new HashSet<DetectedFrame>(grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.DataBoundItem).OfType<DetectedFrame>());
            frames.RaiseListChangedEvents=false;try{frames.Clear();FrameOrder.Repage(rows);foreach(var row in rows)frames.Add(row);}finally{frames.RaiseListChangedEvents=true;frames.ResetBindings();}
            grid.ClearSelection();foreach(DataGridViewRow row in grid.Rows)row.Selected=row.DataBoundItem is DetectedFrame frame&&selected.Contains(frame);UpdateCount();
        }
        private void MoveRows(int direction)
        {
            grid.EndEdit();var selected=new HashSet<DetectedFrame>(grid.SelectedRows.Cast<DataGridViewRow>().Select(r=>r.DataBoundItem).OfType<DetectedFrame>());
            var rows=frames.ToList();FrameOrder.Move(rows,selected,direction);ReplaceFrames(rows);
        }
        private List<DetectedFrame> CheckedFrames()
        {
            RequireFreshScale();
            grid.EndEdit();var selected=frames.Where(f=>f.Enabled).ToList();
            if(selected.Count==0)throw new ArgumentException("Отметьте рамки галочками «Вывод».");
            if(selected.Any(f=>!f.Available))throw new ArgumentException("Есть недоступные рамки. Откройте их DWG и повторите поиск.");
            if(selected.Any(f=>f.Copies<1||f.Copies>999))throw new ArgumentException("Копий должно быть от 1 до 999.");return selected;
        }
        private void SaveJob()
        {
            RequireFreshScale();
            grid.EndEdit();ReadProfile();using(var dialog=new SaveFileDialog{Filter="Задание CAD Batch (*.json)|*.json",FileName="Комплект.cadbatch.json"})
                if(dialog.ShowDialog(DialogOwner)==DialogResult.OK){JobStore.Save(dialog.FileName,JobStore.Capture(profile,frames));status.Text="Задание сохранено: "+dialog.FileName;}
        }
        private async Task LoadJobAsync()
        {
            using(var dialog=new OpenFileDialog{Filter="Задание CAD Batch (*.json)|*.json"})
            {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;var job=JobStore.Load(dialog.FileName);profile=job.Profile;ApplyProfile();
                bool all=profile.Search.AllOpenDocuments;ScanResult live;
                try{profile.Search.AllOpenDocuments=true;live=await host.SearchAsync(profile,running!.Token);}finally{profile.Search.AllOpenDocuments=all;}
                var restored=JobStore.Restore(job,live.Frames,out var warnings);ReplaceFrames(restored);diagnostics.AddRange(warnings);
                scaleNeedsSearch=false;
                status.Text="Задание открыто: "+restored.Count+" строк; недоступно: "+restored.Count(f=>!f.Available);
                if(warnings.Count>0)ShowText("Недоступные строки задания","Откройте перечисленные исходные DWG в AutoCAD и снова откройте задание.\r\n\r\n"+string.Join(Environment.NewLine,warnings));
            }
        }
        private void CopyFirst()
        {
            grid.EndEdit();var selected=grid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r=>r.Index).Select(r=>r.DataBoundItem).OfType<DetectedFrame>().ToList();
            if(selected.Count<2)throw new ArgumentException("Выделите хотя бы две строки с Ctrl/Shift. Первая — верхняя выбранная строка.");
            var choices=AskChecks("По первой выбранной строке",new[]{("Копии",true),("Устройство строки",true),("CTB/STB строки",true)});if(choices==null)return;
            foreach(var f in selected.Skip(1)){if(choices[0])f.Copies=selected[0].Copies;if(choices[1])f.Device=selected[0].Device;if(choices[2])f.StyleSheet=selected[0].StyleSheet;}frames.ResetBindings();
        }
        private void NumberRows()
        {
            grid.EndEdit();var values=AskValues("Нумерация отмеченных строк",("Первый номер","1"),("Шаг","1"),("Цифр (0 — без нулей)","0"),("Префикс",""),("Суффикс",""));if(values==null)return;
            var plan=Numbering.Plan(frames,int.Parse(values[0]),int.Parse(values[1]),values[3],values[4],int.Parse(values[2]));
            if(plan.Count==0)throw new ArgumentException("Нет отмеченных строк.");
            if(!ShowText("План нумерации",string.Join(Environment.NewLine,plan.Select(p=>"Страница "+p.Frame.Page+": «"+p.OldNumber+"» → «"+p.NewNumber+"»"))+"\r\n\r\nМеняются значения в таблице задания. Для записи в DWG используйте меню «Чертёж → Записать номера в атрибуты».","Применить к таблице"))return;
            foreach(var p in plan)p.Frame.SheetNumber=p.NewNumber;frames.ResetBindings();
        }
        private void EditSearchSources()
        {
            ReadProfile();var s=profile.Search;var values=AskChecks("Дополнительные источники",new[]{("Собирать рамки из LINE (задайте слой рамок)",s.Lines),("Загруженные внешние ссылки (с учётом XCLIP)",s.ExternalReferences),("Прямоугольные viewport на листах",s.Viewports),("Только активная вкладка каждого DWG",s.ActiveLayoutOnly),("СПДС / proxy: доступная через Explode геометрия",s.SpecialEntities)});
            if(values==null)return;s.Lines=values[0];s.ExternalReferences=values[1];s.Viewports=values[2];s.ActiveLayoutOnly=values[3];s.SpecialEntities=values[4];
        }
        private async Task PreviewAsync()
        {
            grid.EndEdit();ReadProfile();var frame=CurrentFrame();string path=Path.Combine(Path.GetTempPath(),"CadBatch-preview-"+Guid.NewGuid().ToString("N")+".pdf");
            var request=new ExportRequest{Device=profile.Device,StyleSheet=profile.StyleSheet,ColorMode=profile.ColorMode,FitToPaper=fit.Checked,Items=new List<OutputItem>{new OutputItem{Frame=frame,Path=path}}};
            var report=await host.ExportAsync(request,ExportProgress(),running!.Token);diagnostics.AddRange(report.Details);diagnostics.AddRange(report.Errors);
            if(report.Files.Count!=1)throw new InvalidOperationException(report.Errors.FirstOrDefault()??"Предпросмотр отменён.");
            Process.Start(new ProcessStartInfo(path){UseShellExecute=true});status.Text="Предпросмотр открыт в просмотрщике PDF.";
        }
        private async Task TilePdfAsync(bool fromFrame)
        {
            string? temporary=null;
            try
            {
                string source,label;
                if(fromFrame)
                {
                    grid.EndEdit();ReadProfile();var frame=CurrentFrame();temporary=Path.Combine(Path.GetTempPath(),"CadBatch-tile-"+Guid.NewGuid().ToString("N")+".pdf");
                    var request=new ExportRequest{Device=profile.Device,StyleSheet=profile.StyleSheet,ColorMode=profile.ColorMode,FitToPaper=fit.Checked,Items=new List<OutputItem>{new OutputItem{Frame=frame,Path=temporary}}};
                    var report=await host.ExportAsync(request,ExportProgress(),running!.Token);diagnostics.AddRange(report.Details);diagnostics.AddRange(report.Errors);
                    if(report.Files.Count!=1)throw new InvalidOperationException(report.Errors.FirstOrDefault()??"Подготовка PDF отменена.");
                    source=temporary;label=frame.File+", страница "+frame.Page;
                }
                else using(var file=new OpenFileDialog{Filter="Одностраничный PDF|*.pdf"}){if(file.ShowDialog(DialogOwner)!=DialogResult.OK)return;source=file.FileName;label=source;}
                var values=AskValues("Бумага для склейки",("Ширина малого листа, мм","210"),("Высота малого листа, мм","297"),("Поля с каждой стороны, мм (не менее 5 для номеров)","10"),("Перекрытие, мм","5"),("Масштаб относительно PDF (1 = 100%)","1"));if(values==null)return;
                var plan=TargetSheetPdf.PlanTiles(source,Number(values[0]),Number(values[1]),Number(values[2]),Number(values[3]),Number(values[4]));
                string summary="Источник: "+label+"\r\nБумага: "+plan.SheetWidthMm+" × "+plan.SheetHeightMm+" мм; поля: "+plan.MarginMm+" мм; перекрытие: "+plan.OverlapMm+" мм\r\nМасштаб к PDF: "+plan.Scale+"\r\nВсего страниц: "+plan.Count+"; рядов: "+plan.Rows+"; столбцов: "+plan.Columns+"\r\n\r\n";
                if(plan.Columns<=20&&plan.Rows<=30)for(int row=0;row<plan.Rows;row++)summary+=string.Join(" ",Enumerable.Range(0,plan.Columns).Select(col=>"["+(row*plan.Columns+col+1).ToString("000")+"]"))+"\r\n";
                if(!ShowText("Схема разбивки PDF",summary+"\r\nПорядок: слева направо, сверху вниз. В просмотрщике печатайте результат в масштабе 100%, без дополнительного вписывания.","Создать PDF для склейки"))return;
                using(var output=new SaveFileDialog{Filter="PDF|*.pdf",FileName="Листы-для-склейки.pdf",InitialDirectory=Path.IsPathRooted(folder.Text)?folder.Text:""})
                {
                    if(output.ShowDialog(DialogOwner)!=DialogResult.OK)return;
                    await Task.Run(()=>TargetSheetPdf.Tile(source,output.FileName,plan,running!.Token));
                    string report=SaveOperationReport(host.Description+"\r\nРазбивка PDF\r\n"+summary+"\r\nСоздано: "+output.FileName);status.Text="PDF для склейки: "+plan.Count+" страниц";
                    ShowText("PDF для склейки создан",output.FileName+"\r\nОтчёт: "+report+"\r\n\r\nПечатайте в масштабе 100%.");
                }
            }
            finally{if(temporary!=null&&File.Exists(temporary))File.Delete(temporary);}
        }
        private IProgress<BatchProgress> ExportProgress()=>new Progress<BatchProgress>(p=>{progress.Maximum=Math.Max(1,p.Total);progress.Value=Math.Min(p.Done,progress.Maximum);status.Text=p.Text;frames.ResetBindings();});
        private async Task RunOperationAsync(CadOperationRequest request)
        {
            status.Text="Выполнение: "+request.Operation;var result=await host.RunCadOperationAsync(request,ExportProgress(),running!.Token);frames.ResetBindings();
            string text=host.Description+"\r\nОперация: "+request.Operation+"\r\n"+(result.Cancelled?"ОТМЕНЕНО":result.Errors.Count>0?"ЕСТЬ ОШИБКИ":"ЗАВЕРШЕНО")+
                "\r\nИзменено/создано: "+result.Changed+"\r\n\r\n"+string.Join(Environment.NewLine,result.Files.Concat(result.Details).Concat(result.Errors));
            diagnostics.Add(text);string report=SaveOperationReport(text);status.Text="Операция: "+result.Changed+" • ошибок: "+result.Errors.Count;
            ShowText("Результат операции",text+"\r\n\r\nОтчёт: "+report);
        }
        private string SaveOperationReport(string text)
        {
            string directory=Path.IsPathRooted(folder.Text)?folder.Text:Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"CadBatch-reports");
            Directory.CreateDirectory(directory);string path=Path.Combine(directory,"CadBatch-operation-"+DateTime.Now.ToString("yyyyMMdd-HHmmss-fff")+".txt");File.WriteAllText(path,text,Encoding.UTF8);return path;
        }
        private async Task CreateLayoutsAsync()
        {
            ReadProfile();var selected=CheckedFrames();var values=AskValues("Создание Layout",("Имя: {Page}, {SheetNumber}, {Format}, {Title}","CB_{Page}_{SheetNumber}"),("PDF PC3",profile.Device));if(values==null)return;
            if(!ShowText("План создания листов",string.Join(Environment.NewLine,selected.Select(f=>f.Page+". "+f.File+" / "+f.Layout+" / "+f.FormatName))+"\r\n\r\nСоздаются новые Layout с viewport. При совпадении имени добавляется счётчик. Изменения будут в исходном DWG; сохраните результат сами.","Создать листы"))return;
            await RunOperationAsync(new CadOperationRequest{Operation=CadOperation.CreateLayouts,Frames=selected,NamePattern=values[0],Device=values[1]});
        }
        private async Task ExportDwgAsync()
        {
            ReadProfile();var selected=CheckedFrames();using(var dialog=new FolderBrowserDialog{Description="Папка для отдельных DWG",SelectedPath=folder.Text})
            {
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;var choices=AskChecks("Отбор объектов DWG",new[]{("Включать пересекающие рамку объекты целиком (без геометрической обрезки)",false)});if(choices==null)return;
                var items=OutputPlanner.Build(selected,dialog.SelectedPath,profile.FilePattern,".dwg");
                if(!ShowText("План экспорта DWG",string.Join(Environment.NewLine,items.Select(i=>i.Path))+"\r\n\r\nИсходный DWG не сохраняется. Выводятся только рамки модели.","Экспортировать"))return;
                await RunOperationAsync(new CadOperationRequest{Operation=CadOperation.ExportDwg,Frames=selected,Items=items,Crossing=choices[0]});
            }
        }
        private async Task WriteAttributesAsync()
        {
            var selected=CheckedFrames();var values=AskValues("Запись существующих атрибутов",("Тег номера листа","SHEET"),("Тег всего листов (пусто — не писать)",""),("Тег наименования (пусто — не писать)",""),("Тег шифра (при записи наименования)","CODE"));if(values==null)return;
            var request=new CadOperationRequest{Operation=CadOperation.WriteAttributes,Frames=selected,NumberTag=values[0].Trim(),TotalTag=values[1].Trim(),TitleTag=values[2].Trim(),CodeTag=values[3].Trim(),WriteTotals=!string.IsNullOrWhiteSpace(values[1]),WriteTitles=!string.IsNullOrWhiteSpace(values[2])};
            var plan=await host.RunCadOperationAsync(request,ExportProgress(),running!.Token);
            if(plan.Errors.Count>0){ShowText("Запись заблокирована",string.Join(Environment.NewLine,plan.Errors.Concat(plan.Details)));return;}
            if(!ShowText("Старые и новые значения",string.Join(Environment.NewLine,plan.Details)+"\r\n\r\nБудут изменены атрибуты внешних вхождений блоков в исходных DWG. Сохранение DWG вручную.","Записать в DWG"))return;
            request.ApplyChanges=true;await RunOperationAsync(request);
        }
        private async Task CreateFrameAsync()
        {
            var values=AskValues("Собственная рамка с атрибутами",("Ширина бумаги, мм","420"),("Высота бумаги, мм","297"),("Единиц DWG на мм бумаги","1"));if(values==null)return;
            await RunOperationAsync(new CadOperationRequest{Operation=CadOperation.CreateFrame,Format=new FormatDefinition("Собственная",Number(values[0]),Number(values[1])),Scale=Number(values[2])});
        }
        private async Task MergeDrawingsAsync()
        {
            using(var files=new OpenFileDialog{Filter="Чертежи DWG|*.dwg",Multiselect=true,Title="Выберите DWG в порядке сборки"})
            {
                if(files.ShowDialog(DialogOwner)!=DialogResult.OK)return;using(var output=new SaveFileDialog{Filter="DWG|*.dwg",FileName="Сборка.dwg"})
                {
                    if(output.ShowDialog(DialogOwner)!=DialogResult.OK)return;var values=AskValues("Размещение моделей",("Колонок (1 — одна колонка)","2"),("Интервал, единицы DWG","100"),("Общий масштаб","1"));if(values==null)return;
                    if(!ShowText("План сборки",string.Join(Environment.NewLine,files.FileNames.Select((f,i)=>(i+1)+". "+f))+"\r\n\r\nВыводится модель каждого DWG отдельным блоком. Единицы INSUNITS должны совпадать. Слои и блоки изолируются. Растры и подложки остаются внешними ссылками.","Собрать DWG"))return;
                    await RunOperationAsync(new CadOperationRequest{Operation=CadOperation.MergeDrawings,Files=files.FileNames,Folder=Path.GetDirectoryName(output.FileName)!,NamePattern=Path.GetFileNameWithoutExtension(output.FileName),Columns=int.Parse(values[0]),Spacing=Number(values[1]),Scale=Number(values[2])});
                }
            }
        }
        private async Task InsertRastersAsync()
        {
            using(var files=new OpenFileDialog{Filter="Растры|*.png;*.jpg;*.jpeg;*.tif;*.tiff;*.bmp",Multiselect=true})
            {
                if(files.ShowDialog(DialogOwner)!=DialogResult.OK)return;var values=AskValues("Размещение растров",("Ширина каждой картинки в единицах DWG","297"),("Колонок","2"),("Интервал, единицы DWG","20"));if(values==null)return;
                await RunOperationAsync(new CadOperationRequest{Operation=CadOperation.InsertRasters,Files=files.FileNames,Scale=Number(values[0]),Columns=int.Parse(values[1]),Spacing=Number(values[2])});
            }
        }
        private async Task PrintAsync()
        {
            ReadProfile();var selected=CheckedFrames();var devices=(await host.GetPlotDevicesAsync()).Where(d=>d.IndexOf("pdf",StringComparison.OrdinalIgnoreCase)<0&&d.IndexOf("dwf",StringComparison.OrdinalIgnoreCase)<0&&d.IndexOf("xps",StringComparison.OrdinalIgnoreCase)<0&&d!="None").ToArray();
            if(devices.Length==0)throw new InvalidOperationException("AutoCAD не вернул обычных принтеров. Установите принтер на этом ПК.");
            using(var dialog=new Form{Text="Принтер / плоттер",Size=new Size(600,160),StartPosition=FormStartPosition.CenterParent,Font=Font})
            {
                var combo=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top};combo.Items.AddRange(devices);combo.SelectedIndex=0;dialog.Controls.Add(combo);
                var button=new Button{Text="Далее",Dock=DockStyle.Bottom,Height=38,DialogResult=DialogResult.OK};dialog.Controls.Add(button);dialog.AcceptButton=button;
                if(dialog.ShowDialog(DialogOwner)!=DialogResult.OK)return;string printer=Convert.ToString(combo.SelectedItem)??"";
                if(!ShowText("План физической печати","Принтер: "+printer+"\r\nРамок: "+selected.Count+"; страниц с копиями: "+selected.Sum(f=>f.Copies)+"\r\n\r\n"+string.Join(Environment.NewLine,selected.Select(f=>f.Page+". "+f.FormatName+" × "+f.Copies+(string.IsNullOrWhiteSpace(f.Device)?"":"; устройство строки: "+f.Device)))+"\r\n\r\nЗадания будут переданы в очередь Windows. Отмена не удаляет уже переданные задания. Используется текущее значение «Вписать в бумагу».","Отправить в печать"))return;
                var request=new ExportRequest{PhysicalPrint=true,Device=printer,StyleSheet=profile.StyleSheet,ColorMode=profile.ColorMode,FitToPaper=fit.Checked,Items=selected.Select(f=>new OutputItem{Frame=f,Path=Path.Combine(Path.GetTempPath(),"print-"+Guid.NewGuid().ToString("N")+".pdf")}).ToList()};
                var report=await host.ExportAsync(request,ExportProgress(),running!.Token);string text=ExportReportText.Format(report,request,host.Description);string path=SaveOperationReport(text);diagnostics.Add(text);ShowText("Результат передачи в печать",text+"\r\nОтчёт: "+path);
            }
        }
        private string[]? AskValues(string title,params (string Label,string Value)[] fields)
        {
            using(var dialog=new Form{Text=title,Size=new Size(680,125+fields.Length*55),StartPosition=FormStartPosition.CenterParent,Font=Font,MinimizeBox=false,MaximizeBox=false})
            {
                var panel=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=fields.Length*2+1};var boxes=new List<TextBox>();
                foreach(var field in fields){panel.Controls.Add(new Label{Text=field.Label,AutoSize=true,Margin=new Padding(0,6,0,3)});var box=new TextBox{Text=field.Value,Dock=DockStyle.Top};boxes.Add(box);panel.Controls.Add(box);}
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42,FlowDirection=FlowDirection.RightToLeft};var ok=new Button{Text="Продолжить",DialogResult=DialogResult.OK,AutoSize=true};var cancelButton=new Button{Text="Отмена",DialogResult=DialogResult.Cancel,AutoSize=true};buttons.Controls.Add(ok);buttons.Controls.Add(cancelButton);
                dialog.Controls.Add(panel);dialog.Controls.Add(buttons);dialog.AcceptButton=ok;dialog.CancelButton=cancelButton;return dialog.ShowDialog(DialogOwner)==DialogResult.OK?boxes.Select(b=>b.Text).ToArray():null;
            }
        }
        private bool[]? AskChecks(string title,(string Label,bool Value)[] fields)
        {
            using(var dialog=new Form{Text=title,Size=new Size(760,Math.Min(650,140+fields.Length*35)),StartPosition=FormStartPosition.CenterParent,Font=Font})
            {
                var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(14),WrapContents=false,AutoScroll=true};var checks=fields.Select(f=>new CheckBox{Text=f.Label,Checked=f.Value,AutoSize=true,Margin=new Padding(0,6,0,6)}).ToArray();panel.Controls.AddRange(checks);
                var ok=new Button{Text="Применить",Dock=DockStyle.Bottom,Height=38,DialogResult=DialogResult.OK};dialog.Controls.Add(panel);dialog.Controls.Add(ok);dialog.AcceptButton=ok;return dialog.ShowDialog(DialogOwner)==DialogResult.OK?checks.Select(c=>c.Checked).ToArray():null;
            }
        }
        private bool ShowText(string title,string text,string? apply=null)
        {
            using(var dialog=new Form{Text=title,Size=new Size(900,560),StartPosition=FormStartPosition.CenterParent,Font=Font})
            {
                var box=new TextBox{Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill,Font=new Font("Consolas",10),Text=text};
                var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=42,FlowDirection=FlowDirection.RightToLeft};var close=new Button{Text=apply==null?"Закрыть":"Отмена",DialogResult=DialogResult.Cancel,AutoSize=true};buttons.Controls.Add(close);
                if(apply!=null){var ok=new Button{Text=apply,DialogResult=DialogResult.OK,AutoSize=true};buttons.Controls.Add(ok);dialog.AcceptButton=ok;}
                dialog.Controls.Add(box);dialog.Controls.Add(buttons);dialog.CancelButton=close;return dialog.ShowDialog(DialogOwner)==DialogResult.OK;
            }
        }
        private void SaveRegisterCsv()
        {
            var selected=CheckedFrames();using(var dialog=new SaveFileDialog{Filter="CSV UTF-8|*.csv",FileName="Ведомость.csv"})if(dialog.ShowDialog(DialogOwner)==DialogResult.OK)RegisterExport.Csv(dialog.FileName,selected);
        }
        private void SaveRegisterXlsx()
        {
            var selected=CheckedFrames();using(var dialog=new SaveFileDialog{Filter="Книга Excel|*.xlsx",FileName="Ведомость.xlsx"})if(dialog.ShowDialog(DialogOwner)==DialogResult.OK)RegisterExport.Xlsx(dialog.FileName,selected);
        }
    }
}
