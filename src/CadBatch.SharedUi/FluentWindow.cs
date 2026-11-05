using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CadBatch.Core;
using WButton=System.Windows.Controls.Button;
using WCheck=System.Windows.Controls.CheckBox;
using WGrid=System.Windows.Controls.Grid;
using WText=System.Windows.Controls.TextBox;
using Forms=System.Windows.Forms;
using WDock=System.Windows.Controls.Dock;

namespace CadBatch.UI
{
    public sealed partial class BatchWindow
    {
        private DetectedFrame? fluentSelection;
        private Forms.IWin32Window? fluentOwner;
        private Forms.IWin32Window DialogOwner=>fluentOwner??this;
        private sealed class DialogWindowOwner : Forms.IWin32Window {public DialogWindowOwner(IntPtr handle){Handle=handle;}public IntPtr Handle {get;private set;}}
        // The visible application shell. It follows one path: 1) find frames, 2) check sheets, 3) save PDF.
        // All state and validation stay in BatchWindow; this class only presents and forwards it.
        // Simple mode (default) hides CAD jargon; the "Расширенный режим" switch in the top bar shows everything.
        public sealed class FluentWindow : Window
        {
            private readonly BatchWindow c;
            private readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
            private readonly List<Action> refresh=new List<Action>();
            private readonly DataGrid table=new DataGrid();
            private System.ComponentModel.ICollectionView? view;
            private string tableFilter="";
            private bool isClosed;
            private int knownFrames=-1;
            private readonly StackPanel selected=new StackPanel(),statusIcon=new StackPanel{Orientation=Orientation.Horizontal};
            private readonly TextBlock summary=new TextBlock(),state=new TextBlock(),readiness=new TextBlock();
            private readonly ProgressBar activity=new ProgressBar();
            private readonly WButton output=new WButton(),stop=UiTheme.Button("Отмена",UiTheme.IconClose,"Danger"),openResult=UiTheme.Button("Открыть папку",UiTheme.IconOpen,"Plain");
            private readonly WGrid body=new WGrid(),shell=new WGrid();
            private readonly ScrollViewer bodyScroll=new ScrollViewer{HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,VerticalScrollBarVisibility=ScrollBarVisibility.Disabled};
            private readonly Menu menus=new Menu();
            private readonly Border findPane=new Border(),pdfPane=new Border(),emptyState=new Border();
            private readonly WText search=new WText{Tag="Поиск листа"};
            private WText? nameInput;
            private Border step1=null!,step2=null!,step3=null!;
            private readonly bool demo;
            private readonly UiPreferences prefs;
            private readonly string prefsPath;
            private readonly List<UIElement> advancedOnly=new List<UIElement>();
            private readonly List<MenuItem> toolMenus=new List<MenuItem>();
            private readonly Dictionary<DataGridColumn,Visibility> advancedColumns=new Dictionary<DataGridColumn,Visibility>();
            private readonly HashSet<DataGridColumn> simpleColumns=new HashSet<DataGridColumn>();
            private Action? reconnect;
            private ReturnBar? returnBar;
            private const double BodyMinWidth=860,CompactWidth=1150;
            private WGrid tablePane=null!;private ScrollViewer rightScroll=null!;
            private readonly StackPanel tabs=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,20,14)};
            private readonly WButton sheetsTab=UiTheme.Button("Листы",null,"Primary"),saveTab=UiTheme.Button("Сохранение PDF",null,"Outline");
            private bool compact;private int tab;private TextBlock? tableHint;
            // Number check above the table and before saving.
            private readonly Border numberBanner=new Border{Visibility=Visibility.Collapsed};private readonly TextBlock numberText=new TextBlock();private List<DetectedFrame> numberProblems=new List<DetectedFrame>();private int numberCursor;
            // "Вернуть" after mass changes of the check marks.
            private readonly WButton undo=UiTheme.Button("Вернуть как было",UiTheme.IconRefresh,"Outline");private Dictionary<DetectedFrame,bool>? undoMarks;private DateTime undoUntil;private string undoText="";
            // Understandable progress: sheet N of M and the remaining time.
            private readonly TextBlock progressText=new TextBlock();private DateTime busySince;private bool wasBusy;
            private readonly StackPanel recent=new StackPanel{Margin=new Thickness(0,26,0,0)};
            public bool ShowIntroOnStart {get;set;}=true;
            public FluentWindow(IBatchHost host,string? settingsFile=null)
            {
                c=new BatchWindow(host,settingsFile);c.grid.BindingContext=new Forms.BindingContext();demo=host.Description.IndexOf("ДЕМО",StringComparison.OrdinalIgnoreCase)>=0;
                prefsPath=Path.Combine(Path.GetDirectoryName(c.profilePath)!,"interface.json");prefs=UiPreferences.Load(prefsPath);ModernDialog.TextScale=prefs.TextScale;
                Title="CAD Batch";Width=Math.Min(1600,SystemParameters.WorkArea.Width-40);Height=Math.Min(1000,SystemParameters.WorkArea.Height-40);MinWidth=Math.Min(1000,SystemParameters.WorkArea.Width-24);MinHeight=Math.Min(660,SystemParameters.WorkArea.Height-32);WindowStartupLocation=WindowStartupLocation.CenterScreen;
                // Window-scoped dictionaries do not replace AutoCAD's application resources or theme.
                UiTheme.Apply(this);UiTheme.UseLogo(this);
                shell.Background=UiTheme.Canvas;shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.RowDefinitions.Add(new RowDefinition());shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Content=shell;
                shell.Children.Add(BuildTopBar(host));
                body.Margin=new Thickness(24,20,24,8);body.MinWidth=BodyMinWidth;body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(290)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(350)});
                // Very large text on a small screen still fits: the work area scrolls sideways instead of squeezing the table.
                bodyScroll.Content=body;bodyScroll.SizeChanged+=(s,e)=>Relayout();WGrid.SetRow(bodyScroll,1);shell.Children.Add(bodyScroll);
                BuildSearch();BuildTable();BuildDetails();BuildTabs();
                c.ErrorPresenter=ExplainError;c.JobRemembered=RememberJob;
                var footer=BuildFooter();WGrid.SetRow(footer,2);shell.Children.Add(footer);
                ApplyScale();
                timer.Tick+=(s,e)=>Sync();timer.Start();
                Loaded+=(s,e)=>{CadInteraction.VisibilityChanged=OnCadInteraction;c.fluentOwner=new DialogWindowOwner(Handle);RestoreView();ApplyMode();RenderRecent();Sync();};
                ContentRendered+=(s,e)=>{if(ShowIntroOnStart&&!prefs.SeenIntro)Dispatcher.BeginInvoke(new Action(ShowIntro),DispatcherPriority.ContextIdle);};
                Activated+=(s,e)=>CloseReturnBar();
                Closing+=(s,e)=>{if(c.running!=null){c.running.Cancel();e.Cancel=true;c.status.Text="Отмена задания. Дождитесь завершения операции.";return;}CommitGrid();SaveView();try{c.ReadProfile();ProfileStore.Save(c.profilePath,c.profile);}catch(Exception ex){c.diagnostics.Add(ex.Message);}};
                Closed+=(s,e)=>{isClosed=true;timer.Stop();CloseReturnBar();CadInteraction.VisibilityChanged=null;c.Dispose();};
                PreviewKeyDown+=OnKey;
            }
            public bool CanCloseForUpdate=>c.running==null;
            public void AddApplicationMenu(Action connection,Action updates)
            {
                reconnect=connection;
                var application=new MenuItem{Header="Приложение",Foreground=UiTheme.Ink};var connect=new MenuItem{Header="Подключение к AutoCAD…",Foreground=UiTheme.Ink};connect.Click+=(s,e)=>connection();var update=new MenuItem{Header="Проверить обновления…",Foreground=UiTheme.Ink};update.Click+=(s,e)=>updates();application.Items.Add(connect);application.Items.Add(update);menus.Items.Insert(menus.Items.Count-1,application);
            }

            private UIElement BuildTopBar(IBatchHost host)
            {
                var bar=new Border{Background=UiTheme.Surface,BorderBrush=UiTheme.Line,BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(20,8,16,8)};
                var top=new DockPanel();bar.Child=top;
                var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,18,0),VerticalAlignment=VerticalAlignment.Center};
                brand.Children.Add(UiTheme.Logo(38));
                var name=UiTheme.Text("CAD Batch",18,FontWeights.SemiBold);name.Margin=new Thickness(10,0,0,1);name.VerticalAlignment=VerticalAlignment.Center;brand.Children.Add(name);top.Children.Add(brand);

                var help=UiTheme.Button("Помощь",UiTheme.IconHelp,"Outline");help.ToolTip="Знакомство с программой: три шага";help.Click+=(s,e)=>ShowIntro();help.Margin=new Thickness(10,0,0,0);DockPanel.SetDock(help,WDock.Right);top.Children.Add(help);
                var badge=new ContentControl{VerticalAlignment=VerticalAlignment.Center,ToolTip=host.Description,Margin=new Thickness(14,0,0,0)};DockPanel.SetDock(badge,WDock.Right);top.Children.Add(badge);
                bool? lastState=null;refresh.Add(()=>{bool ok=host.CanPlot;if(lastState==ok)return;lastState=ok;badge.Content=ok?UiTheme.Pill("AutoCAD подключён",UiTheme.Success,UiTheme.SuccessSoft,"●"):demo?UiTheme.Pill("Демо-режим",UiTheme.Warning,UiTheme.WarningSoft,"●"):UiTheme.Pill("Нет связи с AutoCAD",UiTheme.Danger,UiTheme.DangerSoft,"●");});

                // The mode switch is always visible: it is how a confident user reaches every tool.
                var mode=new WCheck{Style=(Style)Resources["Toggle"],IsChecked=prefs.Advanced,VerticalAlignment=VerticalAlignment.Center};System.Windows.Automation.AutomationProperties.SetName(mode,"Расширенный режим");
                var modeRow=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(14,0,0,0),Background=Brushes.Transparent,Cursor=Cursors.Hand,ToolTip="Показать все инструменты: меню, точную настройку поиска, плоттер и дополнительные столбцы"};
                var modeText=UiTheme.Text("Расширенный режим",14.5);modeText.VerticalAlignment=VerticalAlignment.Center;modeText.Margin=new Thickness(0,0,10,0);modeRow.Children.Add(modeText);modeRow.Children.Add(mode);
                modeRow.MouseLeftButtonUp+=(s,e)=>{if(!IsInside((DependencyObject)e.OriginalSource,mode))mode.IsChecked=mode.IsChecked!=true;};
                mode.Checked+=(s,e)=>SetMode(true);mode.Unchecked+=(s,e)=>SetMode(false);DockPanel.SetDock(modeRow,WDock.Right);top.Children.Add(modeRow);

                var size=new ComboBox{Name="TextSize",ItemsSource=UiPreferences.ScaleNames,SelectedIndex=Array.IndexOf(UiPreferences.Scales,prefs.TextScale),MinWidth=200,VerticalAlignment=VerticalAlignment.Center,ToolTip="Размер текста и кнопок во всех окнах CAD Batch"};
                System.Windows.Automation.AutomationProperties.SetName(size,"Размер текста");System.Windows.Automation.AutomationProperties.SetName(search,"Поиск листа");
                size.SelectionChanged+=(s,e)=>{if(size.SelectedIndex<0)return;prefs.TextScale=UiPreferences.Scales[size.SelectedIndex];prefs.Save(prefsPath);ApplyScale();};
                var sizeRow=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(14,0,0,0)};var sizeIcon=UiTheme.Text("Aa",16,FontWeights.SemiBold,UiTheme.Secondary);sizeIcon.VerticalAlignment=VerticalAlignment.Center;sizeIcon.Margin=new Thickness(0,0,8,0);sizeRow.Children.Add(sizeIcon);sizeRow.Children.Add(size);
                DockPanel.SetDock(sizeRow,WDock.Right);top.Children.Add(sizeRow);

                menus.Background=Brushes.Transparent;menus.FontSize=14.5;menus.VerticalAlignment=VerticalAlignment.Center;
                foreach(var item in c.toolsMenu.Items.OfType<Forms.ToolStripMenuItem>()){var converted=ConvertMenu(item);toolMenus.Add(converted);menus.Items.Add(converted);}
                var helpMenu=new MenuItem{Header="Справка",Foreground=UiTheme.Ink};
                helpMenu.Items.Add(Command("Знакомство с программой",ShowIntro));helpMenu.Items.Add(Command("Подробная инструкция",ShowGuide));helpMenu.Items.Add(Command("Горячие клавиши",ShowShortcuts));helpMenu.Items.Add(new System.Windows.Controls.Separator());helpMenu.Items.Add(Command("Диагностика",()=>c.ShowDiagnostics()));helpMenu.Items.Add(Command("Собрать диагностику (ZIP)…",()=>Guard(()=>c.CollectDiagnostics())));
                menus.Items.Add(helpMenu);top.Children.Add(menus);
                return bar;
            }
            private static MenuItem Command(string text,Action action){var item=new MenuItem{Header=text,Foreground=UiTheme.Ink};item.Click+=(s,e)=>action();return item;}
            private static MenuItem Command(string text,Func<Task> action){var item=new MenuItem{Header=text,Foreground=UiTheme.Ink};item.Click+=async(s,e)=>await action();return item;}
            private MenuItem ConvertMenu(Forms.ToolStripMenuItem source)
            {
                var item=new MenuItem{Header=source.Text,IsEnabled=source.Enabled,Foreground=UiTheme.Ink};
                foreach(var child in source.DropDownItems.OfType<Forms.ToolStripMenuItem>())item.Items.Add(ConvertMenu(child));
                if(item.Items.Count==0)item.Click+=(s,e)=>{CommitGrid();source.PerformClick();table.Items.Refresh();Sync();};
                bool? enabled=null,check=null;refresh.Add(()=>{bool on=c.running==null&&source.Enabled;if(enabled!=on){enabled=on;item.IsEnabled=on;}if(check!=source.Checked){check=source.Checked;item.IsChecked=source.Checked;}});return item;
            }

            private void BuildSearch()
            {
                var pane=new WGrid{Margin=new Thickness(0,0,20,0)};pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});pane.RowDefinitions.Add(new RowDefinition());pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});WGrid.SetRowSpan(pane,2);body.Children.Add(pane);
                pane.Children.Add(UiTheme.StepHeader(1,"Найдите рамки","Где и что искать в чертеже",out step1));
                var stack=new StackPanel();findPane.Child=stack;var scroll=new ScrollViewer{Content=findPane,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,-10,0),Padding=new Thickness(0,0,10,0)};WGrid.SetRow(scroll,1);pane.Children.Add(scroll);
                stack.Children.Add(UiTheme.Caption("Где искать"));
                stack.Children.Add(Group(ToggleRow("Модель",c.model,"Основное пространство чертежа"),ToggleRow("Листы",c.layouts,"Вкладки листов внизу окна AutoCAD"),ToggleRow("Все открытые чертежи",c.allDocs,"Не только тот, что открыт сейчас")));
                stack.Children.Add(UiTheme.Caption("Что считать рамкой"));
                stack.Children.Add(Group(ToggleRow("Полилинии",c.polylines,"Рамка нарисована замкнутой линией"),ToggleRow("Блоки",c.blocks,"Рамка вставлена готовым блоком"),ToggleRow("Масштаб по штампу",c.stampScale,"Масштаб определяется по ширине штампа")));
                var fine=new StackPanel();
                fine.Children.Add(InlineInput("Слой",c.layer,"Маска слоя, * — любой"));fine.Children.Add(InlineInput("Имя блока",c.block,"Маска имени блока, * — любой"));fine.Children.Add(InlineInput("Масштабы",c.scales,"Несколько значений — через «;». 100 = 100 единиц DWG на 1 мм бумаги"));fine.Children.Add(InlineInput("Допуск, мм",c.tolerance,"Допустимое отклонение размера рамки от формата"));
                fine.Children.Add(UiTheme.Hint("Масштаб 100 — это 100 единиц DWG на 1 мм бумаги. Несколько значений разделяйте «;».",new Thickness(0,6,0,10)));
                var more=new WrapPanel();var sources=UiTheme.Button("Источники…",null,"Outline");sources.Margin=new Thickness(0,0,6,6);sources.Click+=(s,e)=>Guard(()=>c.EditSearchSources());more.Children.Add(sources);
                var stamp=UiTheme.Button("Штамп…",null,"Outline");stamp.Margin=new Thickness(0,0,6,6);stamp.Click+=(s,e)=>Guard(()=>c.EditStampScale());more.Children.Add(stamp);fine.Children.Add(more);
                var fineCard=UiTheme.Card(new Expander{Header="Точная настройка",Content=fine,IsExpanded=false},16);stack.Children.Add(fineCard);advancedOnly.Add(fineCard);
                var commands=new StackPanel{Margin=new Thickness(0,12,0,0)};
                var find=UiTheme.Button("Найти рамки",UiTheme.IconSearch,"Primary");find.Height=52;find.FontSize=16;find.ToolTip="Клавиша F5";find.Click+=async(s,e)=>await FindAsync();commands.Children.Add(find);
                // Only the sheets selected in AutoCAD: of forty sheets only the chosen ones are found and saved.
                var inSelection=UiTheme.Button("Найти в выделенном",UiTheme.IconLocate,"Outline");inSelection.Height=44;inSelection.Margin=new Thickness(0,8,0,0);
                inSelection.ToolTip="Выделите нужные листы в AutoCAD и нажмите — найдутся и сохранятся только они. Если ничего не выделено, AutoCAD попросит выделить.";
                inSelection.Click+=async(s,e)=>await FindInSelectionAsync();commands.Children.Add(inSelection);refresh.Add(()=>inSelection.IsEnabled=c.running==null);
                var log=UiTheme.Button("Диагностика",null,"Plain");log.HorizontalAlignment=HorizontalAlignment.Center;log.Margin=new Thickness(0,6,0,0);log.FontWeight=FontWeights.Normal;log.Click+=(s,e)=>c.ShowDiagnostics();commands.Children.Add(log);advancedOnly.Add(log);
                WGrid.SetRow(commands,2);pane.Children.Add(commands);refresh.Add(()=>find.IsEnabled=c.running==null);
            }
            private void BuildTable()
            {
                var pane=new WGrid{Margin=new Thickness(0,0,20,0)};tablePane=pane;WGrid.SetColumn(pane,1);WGrid.SetRow(pane,1);body.Children.Add(pane);pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});pane.RowDefinitions.Add(new RowDefinition());pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                var head=new StackPanel{Margin=new Thickness(0,0,0,16)};var row=new StackPanel{Orientation=Orientation.Horizontal};step2=UiTheme.Step(2);row.Children.Add(step2);
                var caption=UiTheme.Title("Проверьте листы",22);caption.Margin=new Thickness(11,0,0,0);caption.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(caption);head.Children.Add(row);
                summary.Foreground=UiTheme.Secondary;summary.FontSize=13.5;summary.Margin=new Thickness(39,3,0,0);summary.TextTrimming=TextTrimming.CharacterEllipsis;head.Children.Add(summary);pane.Children.Add(head);

                var tools=new WrapPanel{Margin=new Thickness(0,0,0,4)};
                var moreButton=UiTheme.Button("Ещё",UiTheme.IconMore,"Outline");moreButton.ToolTip="Сохранить список, столбцы, настройки печати";moreButton.Click+=(s,e)=>TableOptions(moreButton);
                var locate=UiTheme.Button("Показать в AutoCAD",UiTheme.IconLocate,"Outline");locate.Margin=new Thickness(8,0,0,0);locate.ToolTip="Приблизить выбранную рамку в окне AutoCAD";locate.Click+=async(s,e)=>await LocateInCadAsync();
                var none=UiTheme.Button("Снять все",null,"Outline");none.Margin=new Thickness(8,0,0,0);none.ToolTip="Ни один лист не будет выведен";none.Click+=(s,e)=>Guard(()=>MarkAll(false));
                var all=UiTheme.Button("Отметить все",UiTheme.IconCheck,"Outline");all.Margin=new Thickness(8,0,0,0);all.ToolTip="Все листы будут выведены в PDF";all.Click+=(s,e)=>Guard(()=>MarkAll(true));
                var searchBox=new WGrid{Width=230,Margin=new Thickness(0,0,0,8)};search.Padding=new Thickness(36,0,11,0);searchBox.Children.Add(search);var lens=UiTheme.Icon(UiTheme.IconSearch,15,UiTheme.Tertiary);lens.Margin=new Thickness(13,0,0,0);lens.HorizontalAlignment=HorizontalAlignment.Left;lens.IsHitTestVisible=false;searchBox.Children.Add(lens);
                search.TextChanged+=(s,e)=>ApplyFilter(search.Text);tools.Children.Add(searchBox);
                // The order of the sheets is the order of the PDF pages; sorting by number is kept for every next search.
                var order=new ComboBox{Name="SheetOrder",ItemsSource=new[]{"Порядок: как в чертеже","Порядок: по номеру листа"},SelectedIndex=c.profile.SheetOrder==FrameOrder.ByNumber?1:0,MinWidth=236,Margin=new Thickness(8,0,0,8),VerticalAlignment=VerticalAlignment.Top,
                    ToolTip="В таком порядке листы идут в таблице и в PDF"};
                System.Windows.Automation.AutomationProperties.SetName(order,"Порядок листов");
                order.SelectionChanged+=(s,e)=>{if(order.SelectedIndex>=0&&c.running==null&&(order.SelectedIndex==1)!=(c.profile.SheetOrder==FrameOrder.ByNumber))Guard(()=>{c.SetSheetOrder(order.SelectedIndex==1);table.Items.Refresh();Sync();});};
                refresh.Add(()=>{int want=c.profile.SheetOrder==FrameOrder.ByNumber?1:0;if(order.SelectedIndex!=want&&!order.IsDropDownOpen)order.SelectedIndex=want;order.IsEnabled=c.running==null;});
                tools.Children.Add(order);
                foreach(var b in new[]{all,none,locate,moreButton}){b.Margin=new Thickness(8,0,0,8);tools.Children.Add(b);}
                WGrid.SetRow(tools,1);pane.Children.Add(tools);refresh.Add(()=>{bool on=c.running==null&&c.frames.Count>0;tools.IsEnabled=c.running==null;all.IsEnabled=none.IsEnabled=locate.IsEnabled=on;});

                table.AutoGenerateColumns=false;table.CanUserAddRows=false;table.CanUserDeleteRows=false;table.CanUserSortColumns=false;table.CanUserReorderColumns=true;table.CanUserResizeRows=false;table.HeadersVisibility=DataGridHeadersVisibility.Column;table.SelectionMode=DataGridSelectionMode.Extended;table.SelectionUnit=DataGridSelectionUnit.FullRow;
                table.RowHeight=50;table.BorderThickness=new Thickness(0);table.Foreground=UiTheme.Ink;table.Background=UiTheme.Surface;table.RowBackground=UiTheme.Surface;table.AlternatingRowBackground=UiTheme.Surface;table.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;table.HorizontalGridLinesBrush=new SolidColorBrush(Color.FromRgb(236,236,240));
                table.EnableRowVirtualization=true;table.EnableColumnVirtualization=true;VirtualizingPanel.SetVirtualizationMode(table,VirtualizationMode.Recycling);
                // ModernWpf sets its own RowStyle/CellStyle on DataGrid, so the window styles must be assigned explicitly.
                if(Resources[typeof(DataGridRow)] is Style rowStyle)table.RowStyle=rowStyle;if(Resources[typeof(DataGridCell)] is Style cellStyle)table.CellStyle=cellStyle;
                CheckColumn();
                Col("№","Page",52,simple:true);Col("Наименование","Title",260,false,simple:true);table.Columns[table.Columns.Count-1].Width=new DataGridLength(1,DataGridLengthUnitType.Star);table.Columns[table.Columns.Count-1].MinWidth=150;Col("Лист","SheetNumber",76,false,simple:true);FormatColumn();Col("Размер, мм","SizeText",124,simple:true);Col("Масштаб","Scale",92);Col("Угол","AngleDegrees",70);Col("Вкладка","Layout",104,converter:new LayoutNames());
                Col("Чертёж","File",190,true,false);Col("Слой","Layer",110,true,false);Col("Шифр","Code",140,false,false);Col("Ревизия","Revision",95,false,false);Col("Источник","Kind",150,true,false);Col("Состояние","Status",230,true,false);Col("Копии","Copies",75,false,false);Col("Устройство","Device",170,false,false);Col("CTB / STB","StyleSheet",160,false,false);Col("Масштаб из","ScaleSource",170,true,false);
                view=new ListCollectionView((System.Collections.IList)c.frames);table.ItemsSource=view;
                // Property edits update rows by themselves; only list changes (search, order) rebuild the view.
                c.frames.ListChanged+=(s,e)=>{numbersDirty=true;if(e.ListChangedType==System.ComponentModel.ListChangedType.ItemChanged)return;if(view is System.ComponentModel.IEditableCollectionView editing&&!editing.IsEditingItem&&!editing.IsAddingNew)view.Refresh();};
                table.SelectionChanged+=(s,e)=>{c.fluentSelection=table.SelectedItem as DetectedFrame;foreach(Forms.DataGridViewRow r in c.grid.Rows)r.Selected=table.SelectedItems.Contains(r.DataBoundItem);RenderSelection();};
                table.MouseDoubleClick+=async(s,e)=>{if(table.CurrentColumn?.IsReadOnly==true&&table.SelectedItem!=null&&IsRow(e.OriginalSource))await LocateInCadAsync();};table.CellEditEnding+=(s,e)=>Dispatcher.BeginInvoke(new Action(()=>c.UpdateCount()));
                table.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Space&&!IsEditing()&&table.SelectedItems.Count>0){var rows=table.SelectedItems.OfType<DetectedFrame>().ToList();bool value=rows.Any(f=>!f.Enabled);if(rows.Count>1)RememberMarks(rows,(value?"Отмечено":"Снято отметок")+": "+rows.Count);foreach(var f in rows)f.Enabled=value;c.UpdateCount();e.Handled=true;}};
                var holder=new WGrid();var border=UiTheme.Card(table,1,false);border.Margin=new Thickness(0);border.ClipToBounds=true;holder.Children.Add(border);holder.Children.Add(BuildEmptyState());WGrid.SetRow(holder,3);pane.Children.Add(holder);
                var bannerRow=new DockPanel();var bannerIcon=UiTheme.Icon(UiTheme.IconWarning,17,UiTheme.Warning);bannerIcon.Margin=new Thickness(0,0,10,0);DockPanel.SetDock(bannerIcon,WDock.Left);bannerRow.Children.Add(bannerIcon);
                var showProblem=UiTheme.Button("Показать",null,"Outline");showProblem.Margin=new Thickness(12,0,0,0);showProblem.Click+=(s,e)=>ShowNumberProblem();DockPanel.SetDock(showProblem,WDock.Right);bannerRow.Children.Add(showProblem);
                numberText.TextWrapping=TextWrapping.Wrap;numberText.VerticalAlignment=VerticalAlignment.Center;numberText.Foreground=UiTheme.Ink;numberText.FontSize=14.5;bannerRow.Children.Add(numberText);
                numberBanner.Child=bannerRow;numberBanner.Background=UiTheme.WarningSoft;numberBanner.BorderBrush=UiTheme.Warning;numberBanner.BorderThickness=new Thickness(1);numberBanner.CornerRadius=new CornerRadius(12);numberBanner.Padding=new Thickness(14,8,8,8);numberBanner.Margin=new Thickness(0,0,0,12);
                WGrid.SetRow(numberBanner,2);pane.Children.Add(numberBanner);
                var hint=UiTheme.Hint("Галочка — лист попадёт в PDF. Щёлкните по строке, чтобы справа изменить название или номер листа.",new Thickness(2,10,0,0));WGrid.SetRow(hint,4);pane.Children.Add(hint);tableHint=hint;
            }
            private void CheckColumn()
            {
                // A template column toggles on the first click; the stock checkbox column needs two.
                var check=new FrameworkElementFactory(typeof(WCheck));check.SetValue(StyleProperty,Resources["SheetCheck"]);
                check.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("Enabled"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
                check.SetValue(System.Windows.Automation.AutomationProperties.NameProperty,"Вывести лист в PDF");
                var column=new DataGridTemplateColumn{Header="В PDF",CellTemplate=new DataTemplate{VisualTree=check},Width=72,CanUserResize=false,CanUserReorder=false};
                table.Columns.Add(column);simpleColumns.Add(column);
            }
            private UIElement BuildEmptyState()
            {
                var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=440,Margin=new Thickness(24)};
                stack.Children.Add(new Border{Width=76,Height=76,CornerRadius=new CornerRadius(38),Background=UiTheme.AccentSoft,Margin=new Thickness(0,0,0,20),Child=new TextBlock{Text=UiTheme.IconSearch,FontFamily=UiTheme.Icons,FontSize=32,Foreground=UiTheme.Accent,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});
                var title=UiTheme.Title("Листов пока нет",24);title.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(title);
                string firstText=demo?"Это демо-режим. Нажмите «Найти рамки», чтобы посмотреть, как выглядят найденные листы.":"Откройте чертёж в AutoCAD и нажмите «Найти рамки». Найденные листы появятся здесь.";
                var text=UiTheme.Text(firstText,16,null,UiTheme.Secondary);
                bool? shownEmpty=null;refresh.Add(()=>{if(shownEmpty==c.searchedEmpty)return;shownEmpty=c.searchedEmpty;title.Text=c.searchedEmpty?"Рамки не найдены":"Листов пока нет";text.Text=c.searchedEmpty?"В открытом чертеже нет рамок стандартных форматов. Проверьте, что в AutoCAD активен нужный чертёж, и нажмите «Найти рамки» ещё раз.":firstText;});
                text.TextAlignment=TextAlignment.Center;text.Margin=new Thickness(0,10,0,22);text.LineHeight=23;stack.Children.Add(text);
                var find=UiTheme.Button("Найти рамки",UiTheme.IconSearch,"Primary");find.HorizontalAlignment=HorizontalAlignment.Center;find.Height=50;find.FontSize=16;find.MinWidth=220;find.Click+=async(s,e)=>await FindAsync();stack.Children.Add(find);stack.Children.Add(recent);
                emptyState.Child=stack;emptyState.Background=UiTheme.Surface;emptyState.CornerRadius=new CornerRadius(14);emptyState.BorderBrush=UiTheme.Line;emptyState.BorderThickness=new Thickness(1);
                refresh.Add(()=>{var v=c.frames.Count==0?Visibility.Visible:Visibility.Collapsed;if(emptyState.Visibility!=v)emptyState.Visibility=v;find.IsEnabled=c.running==null;});
                return emptyState;
            }
            private void BuildDetails()
            {
                var right=new StackPanel();var scroll=new ScrollViewer{Content=right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,-10,0),Padding=new Thickness(0,0,10,0)};rightScroll=scroll;WGrid.SetRow(scroll,1);WGrid.SetColumn(scroll,2);body.Children.Add(scroll);
                right.Children.Add(UiTheme.StepHeader(3,"Сохраните PDF","Куда и как сохранить листы",out step3));
                var settings=new StackPanel();
                var modes=new ComboBox{ItemsSource=c.pdfModes.Select(m=>m.Text).ToArray()};modes.SelectionChanged+=(s,e)=>{if(modes.SelectedIndex>=0&&modes.SelectedIndex!=(int)c.profile.PdfMode&&c.running==null){c.ChangePdfMode((PdfOutputMode)modes.SelectedIndex);Sync();}};
                refresh.Add(()=>{if(modes.SelectedIndex!=(int)c.profile.PdfMode)modes.SelectedIndex=(int)c.profile.PdfMode;});settings.Children.Add(Field("Как сохранить",modes));
                var browse=new DockPanel();var choose=UiTheme.Button("Выбрать",UiTheme.IconFolder,"Outline");choose.ToolTip="Выбрать папку для PDF";choose.Margin=new Thickness(8,0,0,0);choose.Click+=(s,e)=>Guard(()=>c.ChooseFolder());DockPanel.SetDock(choose,WDock.Right);browse.Children.Add(choose);
                var folderInput=Input(c.folder);folderInput.Tag="Папка не выбрана";browse.Children.Add(folderInput);settings.Children.Add(Field("Папка для PDF",browse,out var folderCaption));
                var folderNote=UiTheme.Hint("",new Thickness(2,-6,0,8));settings.Children.Add(folderNote);
                var openFolder=UiTheme.Button("Открыть папку",UiTheme.IconOpen,"Plain");openFolder.HorizontalAlignment=HorizontalAlignment.Left;openFolder.Margin=new Thickness(-10,-8,0,6);openFolder.FontWeight=FontWeights.Normal;openFolder.Click+=(s,e)=>OpenFolder(c.folder.Text);settings.Children.Add(openFolder);
                refresh.Add(()=>{string f=c.folder.Text.Trim();bool exists=FolderExists(f);var v=exists?Visibility.Visible:Visibility.Collapsed;if(openFolder.Visibility!=v)openFolder.Visibility=v;
                    bool missing=f.Length==0&&c.frames.Any(x=>x.Enabled);folderCaption.Foreground=missing?UiTheme.Warning:UiTheme.Secondary;folderNote.Text=missing?"Выберите папку — или её предложат выбрать при выводе.":"";folderNote.Visibility=missing?Visibility.Visible:Visibility.Collapsed;});
                nameInput=Input(c.pattern);var nameField=Field("Имя файла",nameInput,out var nameCaption);settings.Children.Add(nameField);
                var tokenHint=UiTheme.Hint("Добавить в имя:",new Thickness(2,-6,0,6));settings.Children.Add(tokenHint);
                var tokens=new WrapPanel{Margin=new Thickness(0,0,0,10)};
                foreach(var token in new[]{("Документ","Document"),("№ листа","SheetNumber"),("Наименование","Title"),("Формат","Format"),("Порядковый №","Page"),("Шифр","Code")}){var key=token.Item2;var b=UiTheme.Button(token.Item1,UiTheme.IconAdd,"Chip");b.ToolTip="Вставить {"+key+"}";b.Click+=(s,e)=>InsertToken(key);tokens.Children.Add(b);}
                settings.Children.Add(tokens);refresh.Add(()=>{bool one=c.profile.PdfMode==PdfOutputMode.OneFile;var v=one?Visibility.Collapsed:Visibility.Visible;if(tokens.Visibility!=v){tokens.Visibility=v;tokenHint.Visibility=v;}nameCaption.Text=one?"Имя общего PDF":"Имя файла";});
                var colors=new ComboBox{ItemsSource=c.color.Items.Cast<string>().ToArray(),SelectedIndex=c.color.SelectedIndex};colors.SelectionChanged+=(s,e)=>{if(colors.SelectedIndex>=0)c.color.SelectedIndex=colors.SelectedIndex;};refresh.Add(()=>{if(colors.SelectedIndex!=c.color.SelectedIndex)colors.SelectedIndex=c.color.SelectedIndex;});settings.Children.Add(Field("Цвет",colors));
                var styleField=Field("Таблица стилей CTB / STB",Input(c.style));settings.Children.Add(styleField);refresh.Add(()=>{var v=c.style.Enabled?Visibility.Visible:Visibility.Collapsed;if(styleField.Visibility!=v)styleField.Visibility=v;});
                var fitRow=ToggleRow("Вписать в лист бумаги",c.fit,"Выключите, чтобы печатать точно 1:1");((FrameworkElement)fitRow).Margin=new Thickness(0,0,0,12);settings.Children.Add(fitRow);
                var overwrite=Option("Заменять файлы с тем же именем","При перевыпуске старый PDF заменяется, а не появляется копия «(2)».",()=>c.profile.OverwriteExisting==true,on=>c.profile.OverwriteExisting=on?true:(bool?)null);settings.Children.Add(overwrite);
                var iul=Option("Создавать ИУЛ для экспертизы","Информационно-удостоверяющий лист по ГОСТ Р 21.101-2020 с контрольной суммой — рядом с каждым PDF.",()=>c.profile.Iul?.Enabled==true,on=>{var value=c.profile.Iul?.Copy()??new IulSettings();value.Enabled=on;c.profile.Iul=value;});settings.Children.Add(iul);
                var signers=UiTheme.Button("Подписи в ИУЛ…",null,"Plain");signers.HorizontalAlignment=HorizontalAlignment.Left;signers.Margin=new Thickness(24,-10,0,8);signers.FontWeight=FontWeights.Normal;signers.Click+=(s,e)=>Guard(()=>c.EditIulSigners());settings.Children.Add(signers);
                refresh.Add(()=>{var v=c.profile.Iul?.Enabled==true?Visibility.Visible:Visibility.Collapsed;if(signers.Visibility!=v)signers.Visibility=v;});
                var plotter=new StackPanel();plotter.Children.Add(Field("Плоттер PDF (PC3)",Input(c.device)));
                var templates=UiTheme.Button("Настройки по форматам…",UiTheme.IconSettings,"Outline");templates.HorizontalAlignment=HorizontalAlignment.Stretch;templates.Click+=(s,e)=>Guard(()=>{c.EditProfile();table.Items.Refresh();});plotter.Children.Add(templates);
                plotter.Children.Add(UiTheme.Hint("Своё устройство, бумага, стиль и поворот для каждого формата.",new Thickness(0,8,0,12)));
                var copies=new WCheck{Content=UiTheme.Text("Повторять листы в PDF по числу копий",14.5)};copies.Click+=(s,e)=>c.profile.RepeatCopiesInPdf=copies.IsChecked==true;refresh.Add(()=>{bool on=c.profile.RepeatCopiesInPdf==true;if(copies.IsChecked!=on)copies.IsChecked=on;});
                plotter.Children.Add(copies);plotter.Children.Add(UiTheme.Hint("Число копий задаётся в столбце «Копии».",new Thickness(34,2,0,0)));
                var expander=new Expander{Header="Плоттер и шаблоны",Content=plotter,Margin=new Thickness(0,2,0,0)};settings.Children.Add(expander);advancedOnly.Add(expander);
                pdfPane.Child=settings;right.Children.Add(UiTheme.Card(pdfPane));
                right.Children.Add(UiTheme.Card(selected));RenderSelection();
            }
            private UIElement BuildFooter()
            {
                var footer=new Border{Background=UiTheme.Surface,BorderBrush=UiTheme.Line,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(24,12,24,12)};
                var grid=new WGrid();footer.Child=grid;grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                var left=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var line=new DockPanel();statusIcon.Margin=new Thickness(0,0,10,0);statusIcon.VerticalAlignment=VerticalAlignment.Center;line.Children.Add(statusIcon);
                stop.Margin=new Thickness(12,0,0,0);stop.Click+=(s,e)=>c.running?.Cancel();DockPanel.SetDock(stop,WDock.Right);openResult.Margin=new Thickness(8,0,0,0);openResult.Click+=(s,e)=>OpenFolder(c.LastOutputFolder??c.folder.Text);DockPanel.SetDock(openResult,WDock.Right);
                undo.Margin=new Thickness(12,0,0,0);undo.Visibility=Visibility.Collapsed;undo.Click+=(s,e)=>RestoreMarks();DockPanel.SetDock(undo,WDock.Right);
                retry.Margin=new Thickness(12,0,0,0);retry.Visibility=Visibility.Collapsed;retry.Click+=async(s,e)=>await RetryUnfinishedAsync();DockPanel.SetDock(retry,WDock.Right);
                line.Children.Add(stop);line.Children.Add(openResult);line.Children.Add(undo);line.Children.Add(retry);
                state.TextTrimming=TextTrimming.CharacterEllipsis;state.Foreground=UiTheme.Ink;state.FontSize=15;state.VerticalAlignment=VerticalAlignment.Center;line.Children.Add(state);left.Children.Add(line);
                activity.Margin=new Thickness(0,10,0,0);activity.Height=8;activity.MaxWidth=620;activity.HorizontalAlignment=HorizontalAlignment.Stretch;left.Children.Add(activity);
                progressText.FontSize=14;progressText.Foreground=UiTheme.Secondary;progressText.Margin=new Thickness(0,6,0,0);progressText.Visibility=Visibility.Collapsed;left.Children.Add(progressText);grid.Children.Add(left);
                var right=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(24,0,0,0)};
                readiness.Foreground=UiTheme.Secondary;readiness.FontSize=14;readiness.VerticalAlignment=VerticalAlignment.Center;readiness.Margin=new Thickness(0,0,16,0);readiness.TextAlignment=TextAlignment.Right;readiness.MaxWidth=360;readiness.TextWrapping=TextWrapping.Wrap;right.Children.Add(readiness);
                output.SetResourceReference(StyleProperty,"Primary");output.MinWidth=250;output.Height=52;output.FontSize=16;output.Click+=async(s,e)=>await OutputAsync();right.Children.Add(output);
                WGrid.SetColumn(right,1);grid.Children.Add(right);
                return footer;
            }

            private void Col(string text,string property,double width,bool readOnly=true,bool visible=true,bool simple=false,IValueConverter? converter=null)
            {
                var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));style.Setters.Add(new Setter(VerticalAlignmentProperty,VerticalAlignment.Center));
                var column=new DataGridTextColumn{Header=text,Binding=new Binding(property){Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged,Converter=converter},Width=width,IsReadOnly=readOnly,Visibility=visible?Visibility.Visible:Visibility.Collapsed,ElementStyle=style};
                table.Columns.Add(column);advancedColumns[column]=column.Visibility;if(simple)simpleColumns.Add(column);
            }
            private void FormatColumn()
            {
                var pill=new FrameworkElementFactory(typeof(Border));pill.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(240,240,245)));pill.SetValue(Border.CornerRadiusProperty,new CornerRadius(7));pill.SetValue(Border.PaddingProperty,new Thickness(10,3,10,4));pill.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Left);pill.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
                var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding("FormatName"){Mode=BindingMode.OneWay});text.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);text.SetValue(TextBlock.FontSizeProperty,14.0);pill.AppendChild(text);
                var column=new DataGridTemplateColumn{Header="Формат",CellTemplate=new DataTemplate{VisualTree=pill},Width=92,IsReadOnly=true};table.Columns.Add(column);advancedColumns[column]=Visibility.Visible;simpleColumns.Add(column);
            }
            private sealed class LayoutNames : IValueConverter
            {
                public object Convert(object value,Type targetType,object parameter,CultureInfo culture)=>string.Equals(value as string,"Model",StringComparison.OrdinalIgnoreCase)?"Модель":value;
                public object ConvertBack(object value,Type targetType,object parameter,CultureInfo culture)=>Binding.DoNothing;
            }

            // Text size and mode.
            private void ApplyScale()
            {
                double scale=prefs.TextScale;ModernDialog.TextScale=scale;shell.LayoutTransform=scale>1.001?new ScaleTransform(scale,scale):Transform.Identity;
                Dispatcher.BeginInvoke(new Action(Relayout),DispatcherPriority.Loaded);
            }
            private void Relayout()
            {
                double scale=Math.Max(1,prefs.TextScale),available=bodyScroll.ActualWidth>0?bodyScroll.ActualWidth:ActualWidth/scale;
                body.Width=Math.Max(BodyMinWidth,available-body.Margin.Left-body.Margin.Right);double width=body.Width;
                // Small screens or very large text: steps 2 and 3 share the middle as two tabs instead of squeezing three columns.
                compact=width<CompactWidth;
                body.ColumnDefinitions[0].Width=new GridLength(compact?250:width<1320?262:290);body.ColumnDefinitions[2].Width=new GridLength(compact?0:width<1320?318:350);
                WGrid.SetColumn(rightScroll,compact?1:2);rightScroll.Margin=compact?new Thickness(0,0,10,0):new Thickness(0,0,-10,0);ShowTab(tab);
            }
            private void SetMode(bool advanced){if(prefs.Advanced==advanced)return;prefs.Advanced=advanced;prefs.Save(prefsPath);ApplyMode();}
            private void ApplyMode()
            {
                bool advanced=prefs.Advanced;var v=advanced?Visibility.Visible:Visibility.Collapsed;
                foreach(var element in advancedOnly)element.Visibility=v;foreach(var menu in toolMenus)menu.Visibility=v;
                foreach(var column in table.Columns)
                {
                    if(simpleColumns.Contains(column)){column.Visibility=Visibility.Visible;continue;}
                    column.Visibility=advanced&&advancedColumns.TryGetValue(column,out var saved)?saved:Visibility.Collapsed;
                }
            }

            private bool IsEditing()=>Keyboard.FocusedElement is WText box&&box.IsDescendantOf(table);
            private static bool IsRow(object source){var d=source as DependencyObject;while(d!=null){if(d is DataGridRow)return true;if(d is DataGridColumnHeader)return false;d=d is Visual||d is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);}return false;}
            private void CommitGrid(){table.CommitEdit(DataGridEditingUnit.Cell,true);table.CommitEdit(DataGridEditingUnit.Row,true);}
            private void SaveView()
            {
                // In simple mode the hidden columns keep the user's advanced choice.
                c.profile.Window=new WindowSettings{Width=WindowState==WindowState.Maximized?RestoreBounds.Width:Width,Height=WindowState==WindowState.Maximized?RestoreBounds.Height:Height,Maximized=WindowState==WindowState.Maximized,
                    Columns=table.Columns.Select(x=>new ColumnSettings{Name=x.Header?.ToString()??"",Visible=(prefs.Advanced||simpleColumns.Contains(x)?x.Visibility:advancedColumns.TryGetValue(x,out var saved)?saved:x.Visibility)==Visibility.Visible,Width=x.ActualWidth,Order=x.DisplayIndex}).ToList()};
            }
            private void RestoreView()
            {
                var settings=c.profile.Window;if(settings==null||!Numeric.Positive(settings.Width)){WindowState=WindowState.Maximized;return;}
                if(Numeric.Positive(settings.Width)&&Numeric.Positive(settings.Height)){Width=Math.Max(MinWidth,Math.Min(settings.Width,SystemParameters.WorkArea.Width));Height=Math.Max(MinHeight,Math.Min(settings.Height,SystemParameters.WorkArea.Height));}if(settings.Maximized)WindowState=WindowState.Maximized;
                if(settings.Columns==null)return;
                foreach(var col in settings.Columns.OrderBy(x=>x.Order))
                {
                    var target=table.Columns.FirstOrDefault(x=>(x.Header?.ToString()??"")==col.Name);if(target==null)continue;
                    advancedColumns[target]=col.Visible?Visibility.Visible:Visibility.Collapsed;if(Numeric.Positive(col.Width)&&target.Width.UnitType!=DataGridLengthUnitType.Star)target.Width=Math.Max(35,Math.Min(col.Width,700));if(col.Order>=0&&col.Order<table.Columns.Count&&target.CanUserReorder)target.DisplayIndex=col.Order;
                }
            }
            private void ApplyFilter(string text)
            {
                CommitGrid();tableFilter=text.Trim();if(view==null)return;
                view.Filter=tableFilter.Length==0?null:new Predicate<object>(obj=>obj is DetectedFrame f&&new[]{f.Title,f.SheetNumber,f.Code,f.FormatName,f.File,f.Page.ToString()}.Any(x=>(x??"").IndexOf(tableFilter,StringComparison.CurrentCultureIgnoreCase)>=0));
            }
            private void TableOptions(FrameworkElement anchor)
            {
                var menu=new ContextMenu{PlacementTarget=anchor,Placement=PlacementMode.Bottom,FontSize=14.5};
                menu.Items.Add(Command("Сохранить комплект для повторного вывода…",()=>Guard(()=>c.SaveJob())));
                menu.Items.Add(Command("Открыть сохранённый комплект…",async()=>await OpenJobAsync(null)));
                foreach(var path in RecentJobs().Take(5))menu.Items.Add(Command("    "+Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)),async()=>await OpenJobAsync(path)));
                menu.Items.Add(new System.Windows.Controls.Separator());
                menu.Items.Add(Command("Сохранить ведомость в Excel…",()=>Guard(()=>c.SaveRegisterXlsx())));
                menu.Items.Add(Command("Загрузить названия и номера из ведомости…",()=>Guard(()=>c.ImportRegister())));
                menu.Items.Add(Command("ИУЛ для готовых файлов…",()=>Guard(()=>c.IulForFiles())));
                menu.Items.Add(Command("Сохранить список листов в CSV…",()=>Guard(()=>c.SaveCsv())));
                if(prefs.Advanced){menu.Items.Add(Command("Столбцы таблицы…",()=>Guard(ChooseColumns)));menu.Items.Add(new System.Windows.Controls.Separator());menu.Items.Add(Command("Настройки печати по форматам…",()=>Guard(()=>{c.EditProfile();table.Items.Refresh();})));}
                if(Math.Abs(prefs.TextScale-1)>0.001)menu.LayoutTransform=new ScaleTransform(prefs.TextScale,prefs.TextScale);
                menu.IsOpen=true;
            }
            private void ChooseColumns()
            {
                var columns=table.Columns.Where(x=>!simpleColumns.Contains(x)).ToList();
                var values=c.AskChecks("Дополнительные столбцы",columns.Select(x=>(x.Header?.ToString()??"",x.Visibility==Visibility.Visible)).ToArray());if(values==null)return;
                for(int i=0;i<values.Length;i++){columns[i].Visibility=values[i]?Visibility.Visible:Visibility.Collapsed;advancedColumns[columns[i]]=columns[i].Visibility;}
            }
            private UIElement InlineInput(string caption,Forms.TextBox source,string tip)
            {
                var row=new DockPanel{Margin=new Thickness(0,4,0,4),ToolTip=tip};var input=Input(source);input.Width=104;input.TextAlignment=TextAlignment.Right;DockPanel.SetDock(input,WDock.Right);row.Children.Add(input);
                var label=UiTheme.Text(caption,15);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);return row;
            }
            private WText Input(Forms.TextBox source)
            {
                var input=new WText{Text=source.Text,MinWidth=40};System.Windows.Automation.AutomationProperties.SetName(input,source.AccessibleName??"");input.TextChanged+=(s,e)=>{if(source.Text!=input.Text)source.Text=input.Text;};refresh.Add(()=>{if(!input.IsKeyboardFocusWithin&&input.Text!=source.Text)input.Text=source.Text;});return input;
            }
            private Border Group(params UIElement[] rows)
            {
                var stack=new StackPanel();for(int i=0;i<rows.Length;i++){if(i>0)stack.Children.Add(new Border{Height=1,Background=UiTheme.Line,Margin=new Thickness(16,0,0,0)});stack.Children.Add(rows[i]);}
                var card=UiTheme.Card(stack,0);card.Margin=new Thickness(0,0,0,20);return card;
            }
            // A whole row is the click target; the explanation is always visible, never only in a tooltip.
            private UIElement ToggleRow(string text,Forms.CheckBox source,string description)
            {
                var row=new WGrid{Margin=new Thickness(16,11,14,11),Background=Brushes.Transparent,Cursor=Cursors.Hand};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                var toggle=new WCheck{IsChecked=source.Checked,Style=(Style)Resources["Toggle"],VerticalAlignment=VerticalAlignment.Center};WGrid.SetColumn(toggle,1);row.Children.Add(toggle);
                toggle.Checked+=(s,e)=>source.Checked=true;toggle.Unchecked+=(s,e)=>source.Checked=false;refresh.Add(()=>{if(toggle.IsChecked!=source.Checked)toggle.IsChecked=source.Checked;});
                row.MouseLeftButtonUp+=(s,e)=>{if(!(e.OriginalSource is DependencyObject d&&IsInside(d,toggle))&&toggle.IsEnabled)toggle.IsChecked=toggle.IsChecked!=true;};
                var texts=new StackPanel{VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,12,0)};texts.Children.Add(UiTheme.Text(text,15,FontWeights.SemiBold));texts.Children.Add(UiTheme.Hint(description,new Thickness(0,2,0,0)));row.Children.Add(texts);
                System.Windows.Automation.AutomationProperties.SetName(toggle,text);System.Windows.Automation.AutomationProperties.SetHelpText(toggle,description);return row;
            }
            private static bool IsInside(DependencyObject d,DependencyObject parent){while(d!=null){if(d==parent)return true;d=d is Visual?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);}return false;}
            private static StackPanel Field(string caption,UIElement child)=>Field(caption,child,out _);
            private static StackPanel Field(string caption,UIElement child,out TextBlock label){var field=new StackPanel{Margin=new Thickness(0,0,0,16)};label=UiTheme.Hint(caption,new Thickness(2,0,0,7));label.FontSize=14;label.FontWeight=FontWeights.SemiBold;field.Children.Add(label);field.Children.Add(child);
                var target=child is Panel panel?panel.Children.OfType<Control>().LastOrDefault(x=>!(x is WButton))??(UIElement)child:child;System.Windows.Automation.AutomationProperties.SetLabeledBy(target,label);System.Windows.Automation.AutomationProperties.SetName(target,caption);return field;}
            private void InsertToken(string key)
            {
                if(nameInput==null)return;string value="{"+key+"}";int at=nameInput.IsKeyboardFocusWithin||nameInput.CaretIndex>0?nameInput.CaretIndex:nameInput.Text.Length;string text=nameInput.Text;
                string before=text.Substring(0,at),after=text.Substring(at);if(before.Length>0&&!before.EndsWith("_")&&!before.EndsWith(" ")&&!before.EndsWith("-"))value="_"+value;
                nameInput.Text=before+value+after;nameInput.Focus();nameInput.CaretIndex=before.Length+value.Length;
            }
            private void Guard(Action action)
            {
                if(c.running!=null)return;
                try{CommitGrid();action();Sync();}catch(Exception ex){ModernDialog.Message(Handle,ex is ArgumentException?"Проверьте данные":"Не получилось",ex.Message,DialogKind.Warning);}
            }
            private IntPtr Handle=>new WindowInteropHelper(this).Handle;
            private void OpenFolder(string? folder)
            {
                if(string.IsNullOrWhiteSpace(folder)||!Directory.Exists(folder)){ModernDialog.Message(Handle,"Папка не найдена","Выберите существующую папку для PDF.",DialogKind.Warning);return;}
                Open(folder!);
            }
            private void Open(string target){try{Process.Start(new ProcessStartInfo(target){UseShellExecute=true});}catch(Exception ex){ModernDialog.Message(Handle,"Не удалось открыть",ex.Message,DialogKind.Warning);}}
            private async Task FindAsync(){if(c.running!=null)return;await Execute(c.ScanAsync);if(c.frames.Count>0&&table.SelectedItem==null)table.SelectedIndex=0;}
            private async Task FindInSelectionAsync(){if(c.running!=null)return;await Execute(c.SelectionScanAsync);if(c.frames.Count>0&&table.SelectedItem==null)table.SelectedIndex=0;}
            // The output button is never a dead grey block: it explains what is missing and offers to do it.
            private async Task OutputAsync()
            {
                if(c.running!=null)return;CommitGrid();
                int marked=c.frames.Count(f=>f.Enabled);
                if(!c.host.CanPlot)
                {
                    string text=demo?"В демо-режиме PDF не создаются. Чтобы вывести настоящие чертежи, подключитесь к AutoCAD.":"Связь с AutoCAD потеряна. Убедитесь, что AutoCAD открыт, и подключитесь снова.";
                    if(reconnect!=null){if(ModernDialog.Actions(Handle,demo?"Демо-режим":"Нет связи с AutoCAD",text,DialogKind.Info,"Подключиться к AutoCAD","Не сейчас")==0)reconnect();}
                    else ModernDialog.Message(Handle,demo?"Демо-режим":"Нет связи с AutoCAD",text);
                    return;
                }
                if(c.frames.Count==0){if(ModernDialog.Actions(Handle,"Сначала найдите рамки","Листы для PDF ещё не найдены. Нажмите «Найти рамки» — CAD Batch найдёт все листы в открытом чертеже.",DialogKind.Info,"Найти рамки","Не сейчас")==0)await FindAsync();return;}
                if(marked==0){if(ModernDialog.Actions(Handle,"Не отмечено ни одного листа","Поставьте галочки у листов, которые нужно сохранить в PDF, или отметьте все сразу.",DialogKind.Info,"Отметить все листы","Не сейчас")==0){c.SetAll(true);Sync();}return;}
                if(numberProblems.Count>0)
                {
                    int answer=ModernDialog.Actions(Handle,"Проверьте номера листов",numberText.Text+"\n\nЛисты можно сохранить и так, но в комплекте могут быть путаница или пропуски.",DialogKind.Warning,"Сохранить всё равно","Показать листы","Отмена");
                    if(answer==1){ShowNumberProblem();return;}if(answer!=0)return;
                }
                if(c.scaleNeedsSearch){if(ModernDialog.Actions(Handle,"Нужно найти рамки заново","Параметры поиска изменились, поэтому размеры листов могли измениться.",DialogKind.Info,"Найти рамки заново","Не сейчас")==0)await FindAsync();return;}
                // Choosing the folder is part of the first export, not a separate hurdle.
                if(string.IsNullOrWhiteSpace(c.folder.Text)){c.ChooseFolder();if(string.IsNullOrWhiteSpace(c.folder.Text))return;}
                await Execute(()=>c.ExportAsync(false));ShowResult();
            }
            private void ShowResult()
            {
                if(c.LastOutputCount==0||!c.status.Text.StartsWith("Готово",StringComparison.Ordinal))return;
                if(!IsActive)Attention();
                var files=c.LastOutputFiles;string folder=c.LastOutputFolder??c.folder.Text;
                string text=(files.Count==1?"PDF сохранён":"Сохранено PDF-файлов: "+files.Count)+"\nПапка: "+folder;
                var choice=ModernDialog.Actions(Handle,"Готово!",text,DialogKind.Success,"Открыть папку",files.Count==1?"Открыть PDF":"Открыть первый PDF","Закрыть");
                if(choice==0)OpenFolder(folder);else if(choice==1&&files.Count>0&&File.Exists(files[0]))Open(files[0]);
            }
            private async Task LocateInCadAsync()
            {
                if(table.SelectedItem==null){ModernDialog.Message(Handle,"Выберите лист","Щёлкните по строке в таблице, чтобы показать её рамку в AutoCAD.");return;}
                if(!c.host.CanPlot){ModernDialog.Message(Handle,demo?"Демо-режим":"Нет связи с AutoCAD",demo?"Показ рамки работает после подключения к AutoCAD.":"Подключитесь к AutoCAD заново через меню «Приложение».");return;}
                await Execute(c.LocateAsync);
                if(c.status.Text.StartsWith("Показана рамка",StringComparison.Ordinal)){WindowState=WindowState.Minimized;ShowReturnBar("Рамка показана в AutoCAD",true);}
            }
            // While AutoCAD needs the screen (pick a point, view a frame) a small bar stays on top, so the program is never "lost".
            private void OnCadInteraction(bool hide)
            {
                if(hide){Hide();ShowReturnBar("Укажите нужное в окне AutoCAD. Чтобы отменить — нажмите Esc в AutoCAD.",false);}
                else if(!isClosed){CloseReturnBar();Show();Activate();}
            }
            private void ShowReturnBar(string message,bool withButton)
            {
                CloseReturnBar();returnBar=new ReturnBar(message,withButton?BringBack:(Action?)null,prefs.TextScale);returnBar.Show();
            }
            private void BringBack(){CloseReturnBar();if(isClosed)return;if(!IsVisible)Show();if(WindowState==WindowState.Minimized)WindowState=WindowState.Normal;Activate();}
            private void CloseReturnBar(){var bar=returnBar;returnBar=null;bar?.Close();}
            private sealed class ReturnBar : Window
            {
                public ReturnBar(string message,Action? back,double scale)
                {
                    UiTheme.Apply(this,false);WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Topmost=true;ShowInTaskbar=false;ShowActivated=false;ResizeMode=ResizeMode.NoResize;SizeToContent=SizeToContent.WidthAndHeight;Title="CAD Batch";
                    var row=new StackPanel{Orientation=Orientation.Horizontal};
                    row.Children.Add(UiTheme.Logo(36));
                    var text=UiTheme.Text(message,15,FontWeights.SemiBold);text.VerticalAlignment=VerticalAlignment.Center;text.Margin=new Thickness(12,0,16,0);text.MaxWidth=460;row.Children.Add(text);
                    if(back!=null){var button=UiTheme.Button("Вернуться в CAD Batch",null,"Primary");button.Click+=(s,e)=>back();row.Children.Add(button);}
                    var surface=new Border{Child=row,Background=UiTheme.Surface,CornerRadius=new CornerRadius(14),BorderBrush=new SolidColorBrush(Color.FromArgb(40,0,0,0)),BorderThickness=new Thickness(1),Padding=new Thickness(14,10,10,10),Margin=new Thickness(14),
                        Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=24,ShadowDepth=4,Direction=270,Opacity=0.22}};
                    if(scale>1.001)surface.LayoutTransform=new ScaleTransform(scale,scale);
                    Content=surface;
                    Loaded+=(s,e)=>{var area=SystemParameters.WorkArea;Left=area.Left+(area.Width-ActualWidth)/2;Top=area.Top+8;};
                    surface.MouseLeftButtonDown+=(s,e)=>{if(e.OriginalSource is TextBlock||e.OriginalSource is Border)try{DragMove();}catch(InvalidOperationException){}};
                }
            }
            [StructLayout(LayoutKind.Sequential)] private struct FlashInfo {public uint Size;public IntPtr Window;public uint Flags;public uint Count;public uint Timeout;}
            [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo info);
            // Long batches finish while people look elsewhere: blink the taskbar button and play the system sound.
            private void Attention()
            {
                try{var info=new FlashInfo{Size=(uint)Marshal.SizeOf(typeof(FlashInfo)),Window=Handle,Flags=3|12,Count=5,Timeout=0};FlashWindowEx(ref info);System.Media.SystemSounds.Asterisk.Play();}catch(Exception){}
            }
            private void BuildTabs()
            {
                foreach(var b in new[]{sheetsTab,saveTab}){b.MinWidth=170;b.Margin=new Thickness(0,0,8,0);tabs.Children.Add(b);}
                sheetsTab.Click+=(s,e)=>ShowTab(0);saveTab.Click+=(s,e)=>ShowTab(1);tabs.Visibility=Visibility.Collapsed;WGrid.SetColumn(tabs,1);WGrid.SetRow(tabs,0);body.Children.Add(tabs);
            }
            private void ShowTab(int index)
            {
                tab=index;tabs.Visibility=compact?Visibility.Visible:Visibility.Collapsed;if(tableHint!=null)tableHint.Visibility=compact?Visibility.Collapsed:Visibility.Visible;
                tablePane.Visibility=!compact||tab==0?Visibility.Visible:Visibility.Collapsed;rightScroll.Visibility=!compact||tab==1?Visibility.Visible:Visibility.Collapsed;
                sheetsTab.SetResourceReference(StyleProperty,tab==0?"Primary":"Outline");saveTab.SetResourceReference(StyleProperty,tab==1?"Primary":"Outline");
            }
            private readonly WButton retry=UiTheme.Button("Повторить незавершённые",UiTheme.IconRefresh,"Outline");
            // After an error or a cancel: only the sheets of the last batch that have no finished PDF are marked and saved again.
            private async Task RetryUnfinishedAsync()
            {
                var unfinished=c.LastBatch.Where(f=>c.frames.Contains(f)&&!Finished(f)).ToList();if(unfinished.Count==0||c.running!=null)return;
                RememberMarks(c.frames.ToList(),"Отмечены только незавершённые листы: "+unfinished.Count);var set=new HashSet<DetectedFrame>(unfinished);foreach(var f in c.frames)f.Enabled=set.Contains(f);c.UpdateCount();
                await OutputAsync();
            }
            // A plain check box with its explanation always visible underneath.
            private UIElement Option(string text,string hint,Func<bool> read,Action<bool> write)
            {
                var panel=new StackPanel{Margin=new Thickness(0,0,0,12)};var box=new WCheck{Content=UiTheme.Text(text,15),IsChecked=read()};box.Click+=(s,e)=>write(box.IsChecked==true);
                refresh.Add(()=>{bool on=read();if(box.IsChecked!=on)box.IsChecked=on;});panel.Children.Add(box);panel.Children.Add(UiTheme.Hint(hint,new Thickness(34,0,0,0)));return panel;
            }
            private void MarkAll(bool value)
            {
                int changed=c.frames.Count(f=>f.Enabled!=value);if(changed==0)return;
                RememberMarks(c.frames.ToList(),(value?"Отмечено листов: ":"Снято отметок: ")+changed);c.SetAll(value);
            }
            private void RememberMarks(List<DetectedFrame> rows,string text){undoMarks=rows.ToDictionary(f=>f,f=>f.Enabled);undoUntil=DateTime.Now.AddSeconds(30);undoText=text;c.status.Text=text+". Можно вернуть как было.";}
            private void RestoreMarks()
            {
                var marks=undoMarks;undoMarks=null;if(marks==null)return;foreach(var pair in marks)if(c.frames.Contains(pair.Key))pair.Key.Enabled=pair.Value;
                c.UpdateCount();c.status.Text="Отметки возвращены как было.";Sync();
            }
            private string ProgressText()
            {
                int done=(int)c.progress.Value,total=(int)c.progress.Maximum;if(total<2)return "";
                string text="Лист "+Math.Min(done+1,total)+" из "+total;
                if(done>0){double left=(DateTime.Now-busySince).TotalSeconds/done*(total-done);text+=" · "+(left<50?"осталось меньше минуты":"осталось около "+Math.Max(1,(int)Math.Round(left/60))+" мин");}
                return text;
            }
            private void UpdateNumberCheck()
            {
                var marked=c.frames.Where(f=>f.Enabled).ToList();var empty=marked.Where(f=>string.IsNullOrWhiteSpace(f.SheetNumber)).ToList();
                var repeats=marked.Where(f=>!string.IsNullOrWhiteSpace(f.SheetNumber)).GroupBy(f=>f.Source.DocumentKey+"|"+f.SheetNumber.Trim(),StringComparer.OrdinalIgnoreCase).Where(g=>g.Count()>1).ToList();
                numberProblems=empty.Concat(repeats.SelectMany(g=>g)).ToList();
                var parts=new List<string>();if(empty.Count>0)parts.Add(empty.Count+" "+Plural(empty.Count,"лист","листа","листов")+" без номера");
                foreach(var g in repeats.Take(3))parts.Add("номер «"+g.First().SheetNumber.Trim()+"» повторяется "+g.Count()+" "+Plural(g.Count(),"раз","раза","раз"));if(repeats.Count>3)parts.Add("и ещё повторы");
                string message=parts.Count==0?"":"Проверьте номера: "+string.Join(", ",parts)+".";
                if(numberText.Text!=message)numberText.Text=message;var v=parts.Count>0?Visibility.Visible:Visibility.Collapsed;if(numberBanner.Visibility!=v)numberBanner.Visibility=v;
            }
            private void ShowNumberProblem()
            {
                if(numberProblems.Count==0)return;if(compact)ShowTab(0);if(search.Text.Length>0)search.Text="";
                var frame=numberProblems[numberCursor++%numberProblems.Count];table.SelectedItem=frame;table.ScrollIntoView(frame);
            }
            // Recent jobs: the same set of sheets is often printed again next month.
            private List<string> RecentJobs()=>(prefs.RecentJobs??new List<string>()).Where(File.Exists).ToList();
            private void RememberJob(string path)
            {
                var list=prefs.RecentJobs??new List<string>();list.RemoveAll(x=>string.Equals(x,path,StringComparison.OrdinalIgnoreCase));list.Insert(0,path);prefs.RecentJobs=list.Take(8).ToList();prefs.Save(prefsPath);RenderRecent();
            }
            private void RenderRecent()
            {
                recent.Children.Clear();var jobs=RecentJobs().Take(4).ToList();if(jobs.Count==0)return;
                var caption=UiTheme.Caption("Недавние комплекты");caption.HorizontalAlignment=HorizontalAlignment.Center;recent.Children.Add(caption);
                foreach(var path in jobs)
                {
                    var b=UiTheme.Button(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)),UiTheme.IconDocument,"Outline");b.HorizontalAlignment=HorizontalAlignment.Stretch;b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,0,0,8);b.ToolTip=path;
                    var target=path;b.Click+=async(s,e)=>await OpenJobAsync(target);recent.Children.Add(b);
                }
            }
            private async Task OpenJobAsync(string? path){if(c.running!=null)return;await Execute(()=>c.LoadJobAsync(path));if(c.frames.Count>0&&table.SelectedItem==null)table.SelectedIndex=0;}
            private void ShowIntro()
            {
                if(!IsLoaded||isClosed)return;new IntroWindow(Handle,prefs.TextScale).ShowDialog();
                if(!prefs.SeenIntro){prefs.SeenIntro=true;prefs.Save(prefsPath);}
            }
            // Errors in plain words: what happened, what to do, and a button that does it.
            private bool ExplainError(Exception ex)
            {
                string title,text;string? action=null;Action? run=null;
                if(ex is IOException&&!(ex is FileNotFoundException)&&!(ex is PathTooLongException)&&(ex is EndOfStreamException||ex.Message.IndexOf("связи",StringComparison.OrdinalIgnoreCase)>=0||ex.Message.IndexOf("pipe",StringComparison.OrdinalIgnoreCase)>=0||ex.Message.IndexOf("канал",StringComparison.OrdinalIgnoreCase)>=0))
                {title="Нет связи с AutoCAD";text="Похоже, AutoCAD закрыт или занят другой командой.\n\nЧто сделать: откройте AutoCAD, нажмите там Esc, чтобы завершить команду, и подключитесь снова.";if(reconnect!=null){action="Подключиться снова";run=reconnect;}}
                else if(ex is UnauthorizedAccessException)
                {title="Нет доступа к папке";text="В эту папку нельзя записывать файлы.\n\nЧто сделать: выберите другую папку, например «Документы».";action="Выбрать другую папку";run=()=>c.ChooseFolder();}
                else if(ex is PathTooLongException||ex.Message.IndexOf("слишком длинный",StringComparison.OrdinalIgnoreCase)>=0)
                {title="Слишком длинный путь";text="Имя файла вместе с папкой получилось слишком длинным для Windows.\n\nЧто сделать: выберите папку поближе к корню диска (например D:\\PDF) или сократите шаблон имени.";action="Выбрать другую папку";run=()=>c.ChooseFolder();}
                else if(ex is FileNotFoundException)
                {title="Файл не найден";text=ex.Message+"\n\nЧто сделать: проверьте, что файл не переместили и диск подключён.";}
                else if(ex is ArgumentException)
                {title="Проверьте данные";text=ex.Message+"\n\nЧто сделать: исправьте значение и повторите.";}
                else
                {title="Не получилось";text=ex.Message+"\n\nЧто сделать: попробуйте ещё раз. Если ошибка повторяется, нажмите «Собрать диагностику» и отправьте файл разработчику.";action="Собрать диагностику";run=()=>Guard(()=>c.CollectDiagnostics());}
                if(action==null)ModernDialog.Message(Handle,title,text,DialogKind.Warning);
                else if(ModernDialog.Actions(Handle,title,text,DialogKind.Warning,action,"Закрыть")==0)Dispatcher.BeginInvoke(run!);
                return true;
            }
            // The window refreshes five times a second; disk checks there would freeze it on a slow network drive.
            // The folder is checked in the background once per change of its path (and again after each operation).
            private string checkedFolder="";private bool folderExists,numbersDirty=true;
            private bool FolderExists(string folder)
            {
                if(folder==checkedFolder)return folderExists;checkedFolder=folder;folderExists=false;if(folder.Length==0||!Path.IsPathRooted(folder))return false;
                Task.Run(()=>Directory.Exists(folder)).ContinueWith(t=>{if(checkedFolder==folder&&!t.IsFaulted)folderExists=t.Result;},TaskScheduler.FromCurrentSynchronizationContext());
                return false;
            }
            private async Task Execute(Func<Task> action){CommitGrid();await c.RunAsync(action);checkedFolder="\0";numbersDirty=true;Sync();}
            private void OnKey(object sender,KeyEventArgs e)
            {
                bool ctrl=(Keyboard.Modifiers&System.Windows.Input.ModifierKeys.Control)!=0;
                if(e.Key==Key.F5){e.Handled=true;_=FindAsync();}
                else if(ctrl&&(e.Key==Key.Enter||e.Key==Key.Return)){e.Handled=true;_=OutputAsync();}
                else if(ctrl&&e.Key==Key.F){e.Handled=true;search.Focus();search.SelectAll();}
                else if(e.Key==Key.Escape&&c.running!=null){e.Handled=true;c.running.Cancel();}
                else if(e.Key==Key.Escape&&search.IsKeyboardFocusWithin&&search.Text.Length>0){e.Handled=true;search.Text="";}
            }
            private void ShowGuide()
            {
                ModernDialog.Text(Handle,"Как пользоваться CAD Batch",
                    "Работа идёт в три шага — слева направо.\n\n"+
                    "Шаг 1. Найдите рамки\nСлева включите, где искать: в модели, на листах или во всех открытых чертежах. Нажмите большую синюю кнопку «Найти рамки».\n\n"+
                    "Шаг 2. Проверьте листы\nВ середине появится список листов. Галочка «В PDF» означает, что лист будет сохранён. Щёлкните по строке — справа можно исправить название и номер листа. Кнопка «Показать в AutoCAD» покажет, где находится рамка; чтобы вернуться, нажмите «Вернуться в CAD Batch» вверху экрана.\n\n"+
                    "Шаг 3. Сохраните PDF\nСправа выберите, как сохранить (отдельные файлы или один общий PDF) и папку. Нажмите синюю кнопку внизу справа. Сначала вы увидите план и сможете отменить вывод. Когда всё будет готово, программа предложит открыть папку.\n\n"+
                    "Если текст мелкий\nВверху выберите «Крупный текст» или «Очень крупный текст».\n\n"+
                    "Нужно больше возможностей?\nВключите вверху «Расширенный режим»: появятся меню, точная настройка поиска, плоттер и дополнительные столбцы.");
            }
            private void ShowShortcuts()=>ModernDialog.Text(Handle,"Горячие клавиши","F5 — найти рамки\nCtrl+Enter — сохранить PDF\nCtrl+F — найти лист в списке\nПробел — отметить или снять выбранные строки\nДвойной щелчок — показать рамку в AutoCAD\nEsc — отменить текущую операцию");
            private void Sync()
            {
                foreach(var action in refresh)action();bool busy=c.running!=null;findPane.IsEnabled=pdfPane.IsEnabled=table.IsEnabled=selected.IsEnabled=!busy;
                int total=c.frames.Count,marked=c.frames.Count(f=>f.Enabled);bool one=c.profile.PdfMode==PdfOutputMode.OneFile;
                output.IsEnabled=!busy;
                string outputText=one?"Сохранить один PDF":marked==0?"Сохранить PDF":"Сохранить "+marked+" "+Plural(marked,"лист","листа","листов");output.Content=UiTheme.Content(outputText,UiTheme.IconDownload);System.Windows.Automation.AutomationProperties.SetName(output,outputText);
                bool ready=c.host.CanPlot&&marked>0&&!c.scaleNeedsSearch;
                readiness.Text=busy?"Идёт работа…":!c.host.CanPlot?(demo?"В демо-режиме PDF не создаются":"Нет связи с AutoCAD"):total==0?"Сначала найдите рамки (шаг 1)":marked==0?"Отметьте листы галочками (шаг 2)":c.scaleNeedsSearch?"Параметры изменились — найдите рамки заново":string.IsNullOrWhiteSpace(c.folder.Text)?"Папку предложим выбрать":"Всё готово";
                readiness.Foreground=ready||busy?UiTheme.Secondary:UiTheme.Warning;
                if(busy&&!wasBusy)busySince=DateTime.Now;wasBusy=busy;progressText.Text=busy?ProgressText():"";progressText.Visibility=progressText.Text.Length>0?Visibility.Visible:Visibility.Collapsed;
                if(undoMarks!=null&&(DateTime.Now>undoUntil||busy))undoMarks=null;undo.Visibility=undoMarks!=null?Visibility.Visible:Visibility.Collapsed;
                int unfinished=busy?0:c.LastBatch.Count(f=>c.frames.Contains(f)&&!Finished(f));retry.Visibility=unfinished>0?Visibility.Visible:Visibility.Collapsed;if(unfinished>0)retry.Content=UiTheme.Content("Повторить незавершённые ("+unfinished+")",UiTheme.IconRefresh);
                if(!busy&&numbersDirty){numbersDirty=false;UpdateNumberCheck();}
                stop.Visibility=busy?Visibility.Visible:Visibility.Collapsed;activity.Visibility=busy&&c.progress.Maximum>0?Visibility.Visible:Visibility.Collapsed;
                openResult.Visibility=!busy&&c.LastOutputFolder!=null&&c.status.Text.StartsWith("Готово",StringComparison.Ordinal)?Visibility.Visible:Visibility.Collapsed;
                c.RefreshWorkspaceCount();summary.Text=total==0?"Здесь появятся найденные листы":c.frameSummary.Text;if(state.Text!=c.status.Text){state.Text=c.status.Text;RenderStatusIcon(busy);}
                activity.Maximum=c.progress.Maximum;activity.Value=c.progress.Value;
                step2.Background=total>0?UiTheme.Accent:UiTheme.Fill;((TextBlock)step2.Child).Foreground=total>0?UiTheme.OnAccent:UiTheme.Secondary;
                step3.Background=marked>0?UiTheme.Accent:UiTheme.Fill;((TextBlock)step3.Child).Foreground=marked>0?UiTheme.OnAccent:UiTheme.Secondary;
                if(knownFrames!=total){knownFrames=total;if(table.SelectedItem==null&&total>0)table.SelectedIndex=0;RenderSelection();}
            }
            private void RenderStatusIcon(bool busy)
            {
                statusIcon.Children.Clear();string text=c.status.Text;string glyph;Brush color;
                if(busy){glyph=UiTheme.IconRefresh;color=UiTheme.Accent;}
                else if(text.StartsWith("Готово",StringComparison.Ordinal)||text.StartsWith("Найдено",StringComparison.Ordinal)||text.StartsWith("Задание",StringComparison.Ordinal)){glyph=UiTheme.IconSuccess;color=UiTheme.Success;}
                else if(text.IndexOf("ошиб",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("отмен",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("измен",StringComparison.OrdinalIgnoreCase)>=0){glyph=UiTheme.IconWarning;color=UiTheme.Warning;}
                else{glyph=UiTheme.IconInfo;color=UiTheme.Tertiary;}
                statusIcon.Children.Add(UiTheme.Icon(glyph,17,color));
            }
            private static string Plural(int n,string one,string few,string many){int m=n%100;if(m>=11&&m<=14)return many;switch(n%10){case 1:return one;case 2:case 3:case 4:return few;default:return many;}}
            // The selected sheet: plain fields instead of in-table editing, which needs a precise double click.
            private void RenderSelection()
            {
                selected.Children.Clear();var f=table.SelectedItem as DetectedFrame;
                selected.Children.Add(UiTheme.Caption("Выбранный лист"));
                if(f==null){selected.Children.Add(UiTheme.Hint("Щёлкните по строке в списке — здесь можно будет исправить название и номер листа.",new Thickness(4,0,0,0)));return;}
                var head=UiTheme.Title((f.SheetNumber.Length>0?"Лист "+f.SheetNumber:"Строка "+f.Page)+" · "+f.FormatName,19);head.Margin=new Thickness(4,0,0,12);selected.Children.Add(head);
                selected.Children.Add(Paper(f));
                var include=new WCheck{Content=UiTheme.Text("Сохранять этот лист в PDF",15),Margin=new Thickness(2,14,0,12)};include.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,new Binding("Enabled"){Source=f,Mode=BindingMode.TwoWay});include.Click+=(s,e)=>c.UpdateCount();selected.Children.Add(include);
                selected.Children.Add(EditField("Наименование",f,"Title"));selected.Children.Add(EditField("Номер листа",f,"SheetNumber"));
                var nav=new WGrid{Margin=new Thickness(0,2,0,14)};nav.ColumnDefinitions.Add(new ColumnDefinition());nav.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(8)});nav.ColumnDefinitions.Add(new ColumnDefinition());
                var prev=UiTheme.Button("Предыдущий",null,"Outline");prev.HorizontalAlignment=HorizontalAlignment.Stretch;prev.IsEnabled=table.SelectedIndex>0;prev.Click+=(s,e)=>Step(-1);nav.Children.Add(prev);
                var next=UiTheme.Button("Следующий",null,"Outline");next.HorizontalAlignment=HorizontalAlignment.Stretch;next.IsEnabled=table.SelectedIndex<table.Items.Count-1;next.Click+=(s,e)=>Step(1);WGrid.SetColumn(next,2);nav.Children.Add(next);selected.Children.Add(nav);
                var facts=new WGrid{Margin=new Thickness(4,0,0,10)};facts.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(104)});facts.ColumnDefinitions.Add(new ColumnDefinition());
                var rows=new List<(string,string)>{("Размер",f.SizeText+" мм"),("Масштаб",f.Scale.ToString("0.########")+(f.AngleDegrees!=0?" · "+f.AngleDegrees+"°":"")),("Где",string.Equals(f.Layout,"Model",StringComparison.OrdinalIgnoreCase)?"Модель":"Лист «"+f.Layout+"»"),("Чертёж",f.File)};
                if(f.Code.Length>0)rows.Add(("Шифр",f.Code));if(f.Revision.Length>0)rows.Add(("Ревизия",f.Revision));
                if(prefs.Advanced){if(f.Layer.Length>0)rows.Add(("Слой",f.Layer));foreach(var k in f.Metadata.Where(k=>k.Key!="Title"&&k.Key!="Code"&&k.Key!="SheetNumber"&&k.Key!="Revision"&&!string.IsNullOrWhiteSpace(k.Value)).Take(6))rows.Add((k.Key,k.Value));}
                for(int i=0;i<rows.Count;i++)
                {
                    facts.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var l=UiTheme.Hint(rows[i].Item1,new Thickness(0,3,8,3));WGrid.SetRow(l,i);facts.Children.Add(l);
                    var v=UiTheme.Text(rows[i].Item2,14);v.Margin=new Thickness(0,3,0,3);v.TextTrimming=TextTrimming.CharacterEllipsis;v.TextWrapping=TextWrapping.NoWrap;v.ToolTip=rows[i].Item2;WGrid.SetRow(v,i);WGrid.SetColumn(v,1);facts.Children.Add(v);
                }
                selected.Children.Add(facts);
                var actions=new StackPanel();var show=UiTheme.Button("Показать в AutoCAD",UiTheme.IconLocate,"Outline");show.HorizontalAlignment=HorizontalAlignment.Stretch;show.Margin=new Thickness(0,0,0,8);show.Click+=async(s,e)=>await LocateInCadAsync();actions.Children.Add(show);
                var pdf=UiTheme.Button("Сохранить только этот лист",UiTheme.IconDocument,"Outline");pdf.HorizontalAlignment=HorizontalAlignment.Stretch;pdf.IsEnabled=c.host.CanPlot&&c.running==null;pdf.Click+=async(s,e)=>{if(string.IsNullOrWhiteSpace(c.folder.Text)){c.ChooseFolder();if(string.IsNullOrWhiteSpace(c.folder.Text))return;}await Execute(()=>c.ExportAsync(true));ShowResult();};actions.Children.Add(pdf);selected.Children.Add(actions);
            }
            private UIElement EditField(string caption,DetectedFrame frame,string property)
            {
                var box=new WText();box.SetBinding(WText.TextProperty,new Binding(property){Source=frame,Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged});
                System.Windows.Automation.AutomationProperties.SetName(box,caption);return Field(caption,box);
            }
            private void Step(int direction)
            {
                int index=table.SelectedIndex+direction;if(index<0||index>=table.Items.Count)return;
                table.SelectedIndex=index;table.ScrollIntoView(table.Items[index]);
            }
            private static UIElement Paper(DetectedFrame f)
            {
                // A proportional sheet with the 185 × 55 mm title block, not a picture of the DWG content.
                double w=Math.Max(1,f.PaperWidthMm),h=Math.Max(1,f.PaperHeightMm),factor=Math.Min(230/w,108/h),pw=w*factor,ph=h*factor;
                var sheet=new WGrid{Width=pw,Height=ph,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
                sheet.Children.Add(new System.Windows.Shapes.Rectangle{Fill=UiTheme.Surface,Stroke=new SolidColorBrush(Color.FromRgb(174,174,178)),StrokeThickness=1,RadiusX=1.5,RadiusY=1.5,Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=8,ShadowDepth=1,Direction=270,Opacity=0.12}});
                double sw=Math.Min(185*factor,pw-4),sh=Math.Min(55*factor,ph-4);
                if(sw>6&&sh>3)sheet.Children.Add(new System.Windows.Shapes.Rectangle{Width=sw,Height=sh,Stroke=UiTheme.Accent,StrokeThickness=1,Fill=UiTheme.AccentSoft,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(2)});
                sheet.Children.Add(new TextBlock{Text=f.FormatName,FontSize=13,FontWeight=FontWeights.SemiBold,Foreground=UiTheme.Secondary,Margin=new Thickness(6,4,0,0),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Top});
                return new Border{Child=sheet,Height=140,CornerRadius=new CornerRadius(10),Background=UiTheme.Canvas};
            }
            public async Task SmokeAsync(string path)
            {
                await Execute(c.ScanAsync);c.VerifyCombinedPdfUiForSmokeTest();Sync();table.SelectedIndex=0;UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if(c.frames.Count!=12||c.ToolMenuCount!=6||table.Columns.Count<16)throw new InvalidOperationException("WPF smoke: missing frames, menus or columns.");
                // Editing a sheet in the card must reach the shared row.
                var first=(DetectedFrame)table.SelectedItem;string title=first.Title;first.Title="Проверка";if(c.frames[c.frames.IndexOf(first)].Title!="Проверка")throw new InvalidOperationException("WPF smoke: sheet edit lost.");first.Title=title;
                // Selected rows once lost their text (white on light blue): the cell text must stay dark.
                table.Focus();UpdateLayout();if(SelectedRowForeground().StartsWith("#FFFFFFFF",StringComparison.Ordinal))throw new InvalidOperationException("WPF smoke: selected row text is white.");
                RenderTo(path);
                File.WriteAllText(path+".txt","WPF + ModernWpf smoke PASS: 12 frames, six menus, editable grid, PDF modes/names, selected frame, scoped theme. CAD output disabled in demo.");
            }
            // Smoke: a computer with old settings. With frames on a layout at 1:100 the window offers wider settings and keeps
            // them; with no standard frames at all it explains why. The dialog is captured and answered with its first button.
            public async Task FallbackSmokeAsync(string folder,string mode,string settingsPath)
            {
                bool nothing=mode=="nothing",missed=mode=="missed";string name=mode;string? dialogText=null;
                string accept=nothing?"Понятно":missed?"Добавить и запомнить":"Показать и запомнить";
                var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};
                timer.Tick+=(s,e)=>
                {
                    var sheet=Application.Current.Windows.OfType<Window>().FirstOrDefault(w=>w!=this&&w.IsVisible);if(sheet==null)return;timer.Stop();
                    sheet.UpdateLayout();UiTheme.Snapshot((FrameworkElement)sheet.Content,Path.Combine(folder,name+"-dialog.png"));
                    dialogText=string.Join("\n",Descendants<TextBlock>(sheet).Select(t=>t.Text));
                    var primary=Descendants<WButton>(sheet).First(b=>Descendants<TextBlock>(b).Any(t=>t.Text==accept)||b.Content as string==accept);
                    ((System.Windows.Automation.Provider.IInvokeProvider)new System.Windows.Automation.Peers.ButtonAutomationPeer(primary).GetPattern(System.Windows.Automation.Peers.PatternInterface.Invoke)).Invoke();
                };
                timer.Start();
                try{await Execute(c.ScanAsync);}finally{timer.Stop();}
                Sync();UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if(dialogText==null)throw new InvalidOperationException("Fallback smoke: no dialog was shown.");
                var saved=ProfileStore.Load(settingsPath);
                if(nothing)
                {
                    if(c.frames.Count!=0||!c.searchedEmpty||!dialogText.Contains("Самые крупные прямоугольники")||saved.Search.Layouts)throw new InvalidOperationException("Nothing smoke: wrong result.\n"+dialogText);
                }
                else if(missed)
                {
                    if(c.frames.Count!=12||!c.profile.Search.Lines||!saved.Search.Lines||!dialogText.Contains("Ещё 6 рамок")||!dialogText.Contains("отдельных отрезков"))throw new InvalidOperationException("Missed smoke: frames drawn with lines were not added. Frames: "+c.frames.Count+"\n"+dialogText);
                }
                else if(c.frames.Count!=12||!c.layouts.Checked||!c.profile.Search.Scales.Contains(100)||!saved.Search.Layouts||!saved.Search.Scales.Contains(100)||!dialogText.Contains("добавить масштаб 1:100"))
                    throw new InvalidOperationException("Fallback smoke: wider settings were not applied. Frames: "+c.frames.Count+"\n"+dialogText);
                RenderTo(Path.Combine(folder,name+".png"));
                File.WriteAllText(Path.Combine(folder,name+".txt"),(nothing?"NOTHING SMOKE PASS: the window explains that no frames exist and names the largest rectangles.":missed?"MISSED SMOKE PASS: 6 frames found; the check found 6 more drawn with lines, the user added them, settings were saved.":"FALLBACK SMOKE PASS: old settings found nothing; the wider search found 12 frames on a layout at 1:100, the user accepted, settings were saved.")+"\n\n"+dialogText);
            }
            // Smoke: "find in selection" finds only the selected sheets and is not remembered; sorting by sheet number is kept
            // for the next search and shown by the order list.
            public async Task SelectionSmokeAsync(string folder)
            {
                await FindInSelectionAsync();
                if(c.frames.Count!=3||c.profile.Search.Selection!=null||!c.status.Text.Contains("В выделенном"))throw new InvalidOperationException("Selection smoke: expected 3 selected sheets, got "+c.frames.Count+"; "+c.status.Text);
                await Execute(c.ScanAsync);if(c.frames.Count!=12)throw new InvalidOperationException("Selection smoke: the next search must find all sheets.");
                for(int i=0;i<c.frames.Count;i++)c.frames[i].SheetNumber=(12-i).ToString(CultureInfo.InvariantCulture);
                var order=Descendants<ComboBox>(this).First(b=>b.Name=="SheetOrder");order.SelectedIndex=1;Sync();
                if(c.frames[0].SheetNumber!="1"||c.frames[11].SheetNumber!="12"||c.frames[0].Page!=1)throw new InvalidOperationException("Selection smoke: sheets are not ordered by number.");
                await Execute(c.ScanAsync);Sync();UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if(c.frames[0].SheetNumber!="1"||c.profile.SheetOrder!=FrameOrder.ByNumber||order.SelectedIndex!=1)throw new InvalidOperationException("Selection smoke: the order by number was not kept for the next search.");
                RenderTo(Path.Combine(folder,"selection.png"));
                File.WriteAllText(Path.Combine(folder,"selection.txt"),"SELECTION SMOKE PASS: find in selection gave the 3 selected sheets and was not remembered; a normal search found 12; ordering by sheet number reordered the table and the PDF order and was kept for the next search.");
            }
            private static IEnumerable<T> Descendants<T>(DependencyObject node) where T:DependencyObject
            {
                for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);if(child is T found)yield return found;foreach(var deeper in Descendants<T>(child))yield return deeper;}
            }
            // Smoke support: switch mode and text size without touching the user's saved preferences file.
            public void PreviewLook(bool advanced,double scale){prefs.Advanced=advanced;prefs.TextScale=scale;ApplyMode();ApplyScale();UpdateLayout();Relayout();UpdateLayout();}
            public void PreviewTab(int index){ShowTab(index);UpdateLayout();}
            public void RenderEditors(string folder)
            {
                var print=new PrintSettingsWindow(c.profile,c.host,Handle);print.Show();print.UpdateLayout();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);UiTheme.Snapshot((FrameworkElement)print.Content,Path.Combine(folder,"print-settings.png"));print.Close();
                var stamp=new StampZonesWindow(c.profile.Metadata,Handle);stamp.Show();Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);stamp.RenderPreview(Path.Combine(folder,"stamp-zones.png"));stamp.Close();
            }
            public void PreviewSelection(){Activate();table.SelectedIndex=1;table.Focus();table.CurrentCell=new DataGridCellInfo(table.Items[1],table.Columns[2]);var row=(DataGridRow)table.ItemContainerGenerator.ContainerFromIndex(1);row?.Focus();UpdateLayout();}
            public string SelectedRowForeground(){var row=(DataGridRow?)table.ItemContainerGenerator.ContainerFromIndex(table.SelectedIndex);var cell=row==null?null:FindChild<DataGridCell>(row);return cell==null?"":((cell.Foreground as SolidColorBrush)?.Color.ToString()??"")+" selected="+cell.IsSelected+" active="+Selector.GetIsSelectionActive(table)+" rowBg="+((row!.Background as SolidColorBrush)?.Color.ToString()??"?")+" src="+DependencyPropertyHelper.GetValueSource(row,BackgroundProperty).BaseValueSource+" rowSel="+row.IsSelected;}
            private static T? FindChild<T>(DependencyObject node) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++){var child=VisualTreeHelper.GetChild(node,i);if(child is T found)return found;var deeper=FindChild<T>(child);if(deeper!=null)return deeper;}return null;}
            // What a screen reader (Narrator) would announce without a name: every visible interactive control must have one.
            public List<string> AccessibilityReport()
            {
                var missing=new List<string>();int total=0;
                void Walk(DependencyObject node)
                {
                    if(node is UIElement element&&!element.IsVisible)return;
                    if(node is ButtonBase||node is WText||node is ComboBox||node is WCheck)
                    {
                        total++;var peer=System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement((UIElement)node);string name=peer?.GetName()??"";
                        bool insideTemplate=node is FrameworkElement fe&&fe.TemplatedParent!=null&&!(fe.TemplatedParent is ContentPresenter);
                        if(string.IsNullOrWhiteSpace(name)&&!insideTemplate)missing.Add(node.GetType().Name+" ← "+(VisualTreeHelper.GetParent(node)?.GetType().Name??"")+" ← "+((node as FrameworkElement)?.TemplatedParent?.GetType().Name??"-")+" «"+((node as ContentControl)?.Content as string??(node as WText)?.Text??"")+"»");
                    }
                    for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)Walk(VisualTreeHelper.GetChild(node,i));
                }
                Walk(this);missing.Insert(0,"Проверено элементов: "+total+"; без названия: "+missing.Count);return missing;
            }
            public void PreviewProblems(){if(c.frames.Count>2){c.frames[1].SheetNumber="";c.frames[2].SheetNumber=c.frames[0].SheetNumber;}MarkAll(false);RestoreMarks();RememberMarks(c.frames.ToList(),"Снято отметок: 12");Sync();UpdateLayout();}
            public void RenderCard(string path)=>UiTheme.Snapshot((FrameworkElement)selected.Parent,path);
            public void RenderTo(string path)
            {
                UpdateLayout();var visual=(FrameworkElement)Content;if(visual.ActualWidth<1)throw new InvalidOperationException("WPF smoke: the window content was not laid out.");
                double scale=Math.Max(1,prefs.TextScale);var bmp=new RenderTargetBitmap((int)(visual.ActualWidth*scale),(int)(visual.ActualHeight*scale),96,96,PixelFormats.Pbgra32);bmp.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))encoder.Save(file);
            }
        }
    }
}
