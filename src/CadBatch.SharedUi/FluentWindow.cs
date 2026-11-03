using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            private readonly WGrid body=new WGrid();
            private readonly Menu menus=new Menu();
            private readonly Border findPane=new Border(),pdfPane=new Border(),emptyState=new Border();
            private readonly WText search=new WText{Tag="Поиск по названию, номеру, шифру…"};
            private WText? nameInput;
            private Border step1=null!,step2=null!,step3=null!;
            private bool demo;
            public FluentWindow(IBatchHost host,string? settingsFile=null)
            {
                c=new BatchWindow(host,settingsFile);c.grid.BindingContext=new Forms.BindingContext();demo=host.Description.IndexOf("ДЕМО",StringComparison.OrdinalIgnoreCase)>=0;
                Title="CAD Batch";Width=Math.Min(1580,SystemParameters.WorkArea.Width-40);Height=Math.Min(1000,SystemParameters.WorkArea.Height-40);MinWidth=Math.Min(1120,SystemParameters.WorkArea.Width-24);MinHeight=Math.Min(700,SystemParameters.WorkArea.Height-32);WindowStartupLocation=WindowStartupLocation.CenterScreen;
                // Window-scoped dictionaries do not replace AutoCAD's application resources or theme.
                UiTheme.Apply(this);
                var shell=new WGrid{Background=UiTheme.Canvas};shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.RowDefinitions.Add(new RowDefinition());shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Content=shell;
                shell.Children.Add(BuildTopBar(host));
                body.Margin=new Thickness(24,20,24,8);body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(272)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(330)});WGrid.SetRow(body,1);shell.Children.Add(body);
                BuildSearch();BuildTable();BuildDetails();
                var footer=BuildFooter();WGrid.SetRow(footer,2);shell.Children.Add(footer);
                timer.Tick+=(s,e)=>Sync();timer.Start();
                Loaded+=(s,e)=>{CadInteraction.VisibilityChanged=hide=>{if(hide)Hide();else if(!isClosed){Show();Activate();}};c.fluentOwner=new DialogWindowOwner(new WindowInteropHelper(this).Handle);RestoreView();Sync();};
                Closing+=(s,e)=>{if(c.running!=null){c.running.Cancel();e.Cancel=true;c.status.Text="Отмена задания. Дождитесь завершения операции.";return;}CommitGrid();SaveView();try{c.ReadProfile();ProfileStore.Save(c.profilePath,c.profile);}catch(Exception ex){c.diagnostics.Add(ex.Message);}};
                Closed+=(s,e)=>{isClosed=true;timer.Stop();CadInteraction.VisibilityChanged=null;c.Dispose();};
                SizeChanged+=(s,e)=>{bool narrow=ActualWidth<1320;body.ColumnDefinitions[0].Width=new GridLength(narrow?244:272);body.ColumnDefinitions[2].Width=new GridLength(narrow?300:330);};
                PreviewKeyDown+=OnKey;
            }
            public bool CanCloseForUpdate=>c.running==null;
            public void AddApplicationMenu(Action connection,Action updates)
            {
                var application=new MenuItem{Header="Приложение",Foreground=UiTheme.Ink};var connect=new MenuItem{Header="Подключение к AutoCAD…",Foreground=UiTheme.Ink};connect.Click+=(s,e)=>connection();var update=new MenuItem{Header="Проверить обновления…",Foreground=UiTheme.Ink};update.Click+=(s,e)=>updates();application.Items.Add(connect);application.Items.Add(update);menus.Items.Insert(menus.Items.Count-1,application);
            }

            private UIElement BuildTopBar(IBatchHost host)
            {
                var bar=new Border{Background=Brushes.White,BorderBrush=UiTheme.Line,BorderThickness=new Thickness(0,0,0,1),Padding=new Thickness(20,8,16,8)};
                var top=new DockPanel();bar.Child=top;
                var brand=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,22,0),VerticalAlignment=VerticalAlignment.Center};
                brand.Children.Add(new Border{Width=28,Height=28,CornerRadius=new CornerRadius(8),Background=new LinearGradientBrush(Color.FromRgb(10,132,255),Color.FromRgb(0,98,204),90),Child=UiTheme.Icon(UiTheme.IconDocument,14,Brushes.White)});
                ((TextBlock)((Border)brand.Children[0]).Child).HorizontalAlignment=HorizontalAlignment.Center;
                var name=UiTheme.Text("CAD Batch",16,FontWeights.SemiBold);name.Margin=new Thickness(10,0,0,1);name.VerticalAlignment=VerticalAlignment.Center;brand.Children.Add(name);top.Children.Add(brand);
                var help=UiTheme.Button("",UiTheme.IconHelp,"Square");help.Background=Brushes.Transparent;help.ToolTip="Как пользоваться CAD Batch";help.Click+=(s,e)=>ShowGuide();help.Margin=new Thickness(8,0,0,0);DockPanel.SetDock(help,WDock.Right);top.Children.Add(help);
                var badge=new ContentControl{VerticalAlignment=VerticalAlignment.Center,ToolTip=host.Description};DockPanel.SetDock(badge,WDock.Right);top.Children.Add(badge);
                bool? lastState=null;refresh.Add(()=>{bool ok=host.CanPlot;if(lastState==ok)return;lastState=ok;badge.Content=ok?UiTheme.Pill("AutoCAD подключён",UiTheme.Success,UiTheme.SuccessSoft,"●"):demo?UiTheme.Pill("Демо-режим",UiTheme.Warning,UiTheme.WarningSoft,"●"):UiTheme.Pill("Нет связи с AutoCAD",UiTheme.Danger,UiTheme.DangerSoft,"●");});
                menus.Background=Brushes.Transparent;menus.FontSize=13;menus.VerticalAlignment=VerticalAlignment.Center;
                foreach(var item in c.toolsMenu.Items.OfType<Forms.ToolStripMenuItem>())menus.Items.Add(ConvertMenu(item));
                var helpMenu=new MenuItem{Header="Справка",Foreground=UiTheme.Ink};
                helpMenu.Items.Add(Command("Как начать работу",ShowGuide));helpMenu.Items.Add(Command("Горячие клавиши",ShowShortcuts));helpMenu.Items.Add(new System.Windows.Controls.Separator());helpMenu.Items.Add(Command("Диагностика",()=>c.ShowDiagnostics()));
                menus.Items.Add(helpMenu);top.Children.Add(menus);
                return bar;
            }
            private static MenuItem Command(string text,Action action){var item=new MenuItem{Header=text,Foreground=UiTheme.Ink};item.Click+=(s,e)=>action();return item;}
            private MenuItem ConvertMenu(Forms.ToolStripMenuItem source)
            {
                var item=new MenuItem{Header=source.Text,IsEnabled=source.Enabled,Foreground=UiTheme.Ink};
                foreach(var child in source.DropDownItems.OfType<Forms.ToolStripMenuItem>())item.Items.Add(ConvertMenu(child));
                if(item.Items.Count==0)item.Click+=(s,e)=>{CommitGrid();source.PerformClick();table.Items.Refresh();Sync();};
                bool? enabled=null,check=null;refresh.Add(()=>{bool on=c.running==null&&source.Enabled;if(enabled!=on){enabled=on;item.IsEnabled=on;}if(check!=source.Checked){check=source.Checked;item.IsChecked=source.Checked;}});return item;
            }

            private void BuildSearch()
            {
                var pane=new WGrid{Margin=new Thickness(0,0,20,0)};pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});pane.RowDefinitions.Add(new RowDefinition());pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.Children.Add(pane);
                pane.Children.Add(UiTheme.StepHeader(1,"Найдите рамки","Где и что искать в чертеже",out step1));
                var stack=new StackPanel();findPane.Child=stack;var scroll=new ScrollViewer{Content=findPane,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,-8,0),Padding=new Thickness(0,0,8,0)};WGrid.SetRow(scroll,1);pane.Children.Add(scroll);
                stack.Children.Add(UiTheme.Caption("Где искать"));
                stack.Children.Add(Group(ToggleRow("Модель",c.model,"Рамки в пространстве модели"),ToggleRow("Листы (Layout)",c.layouts,"Рамки на листах чертежа"),ToggleRow("Все открытые DWG",c.allDocs,"Все чертежи, открытые в AutoCAD, а не только активный")));
                stack.Children.Add(UiTheme.Caption("Что считать рамкой"));
                stack.Children.Add(Group(ToggleRow("Полилинии",c.polylines,"Замкнутые прямоугольные полилинии"),ToggleRow("Блоки и вложенные блоки",c.blocks,"Рамки, вставленные блоками, в том числе вложенными"),ToggleRow("Масштаб по штампу",c.stampScale,"Определять масштаб по верхней линии штампа")));
                var fine=new StackPanel();
                fine.Children.Add(InlineInput("Слой",c.layer,"Маска слоя, * — любой"));fine.Children.Add(InlineInput("Имя блока",c.block,"Маска имени блока, * — любой"));fine.Children.Add(InlineInput("Масштабы",c.scales,"Несколько значений — через «;». 100 = 100 единиц DWG на 1 мм бумаги"));fine.Children.Add(InlineInput("Допуск, мм",c.tolerance,"Допустимое отклонение размера рамки от формата"));
                fine.Children.Add(UiTheme.Hint("Масштаб 100 — это 100 единиц DWG на 1 мм бумаги. Несколько значений разделяйте «;».",new Thickness(0,6,0,10)));
                var more=new WrapPanel();var sources=UiTheme.Button("Источники…",null,"Outline");sources.Margin=new Thickness(0,0,6,6);sources.Click+=(s,e)=>Guard(()=>c.EditSearchSources());more.Children.Add(sources);
                var stamp=UiTheme.Button("Штамп…",null,"Outline");stamp.Margin=new Thickness(0,0,6,6);stamp.Click+=(s,e)=>Guard(()=>c.EditStampScale());more.Children.Add(stamp);fine.Children.Add(more);
                stack.Children.Add(UiTheme.Card(new Expander{Header="Точная настройка",Content=fine,IsExpanded=false},14));
                var commands=new StackPanel{Margin=new Thickness(0,12,0,0)};
                var find=UiTheme.Button("Найти рамки",UiTheme.IconSearch,"Primary");find.Height=44;find.FontSize=14;find.ToolTip="Найти рамки · F5";find.Click+=async(s,e)=>await FindAsync();commands.Children.Add(find);
                var log=UiTheme.Button("Диагностика",null,"Plain");log.HorizontalAlignment=HorizontalAlignment.Center;log.Margin=new Thickness(0,6,0,0);log.FontWeight=FontWeights.Normal;log.Click+=(s,e)=>c.ShowDiagnostics();commands.Children.Add(log);
                WGrid.SetRow(commands,2);pane.Children.Add(commands);refresh.Add(()=>find.IsEnabled=c.running==null);
            }
            private void BuildTable()
            {
                var pane=new WGrid{Margin=new Thickness(0,0,20,0)};WGrid.SetColumn(pane,1);body.Children.Add(pane);pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});pane.RowDefinitions.Add(new RowDefinition());pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                var head=new StackPanel{Margin=new Thickness(0,0,0,16)};var row=new StackPanel{Orientation=Orientation.Horizontal};step2=UiTheme.Step(2);row.Children.Add(step2);
                var caption=UiTheme.Title("Проверьте листы",19);caption.Margin=new Thickness(10,0,0,0);caption.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(caption);head.Children.Add(row);
                summary.Foreground=UiTheme.Secondary;summary.FontSize=12;summary.Margin=new Thickness(32,3,0,0);summary.TextTrimming=TextTrimming.CharacterEllipsis;head.Children.Add(summary);pane.Children.Add(head);

                var tools=new DockPanel{Margin=new Thickness(0,0,0,12),LastChildFill=false};
                var searchBox=new WGrid{Width=290};search.Padding=new Thickness(32,0,9,0);searchBox.Children.Add(search);var lens=UiTheme.Icon(UiTheme.IconSearch,13,UiTheme.Tertiary);lens.Margin=new Thickness(11,0,0,0);lens.HorizontalAlignment=HorizontalAlignment.Left;lens.IsHitTestVisible=false;searchBox.Children.Add(lens);
                search.TextChanged+=(s,e)=>ApplyFilter(search.Text);DockPanel.SetDock(searchBox,WDock.Left);tools.Children.Add(searchBox);
                var moreButton=UiTheme.Button("",UiTheme.IconMore,"Square");moreButton.ToolTip="Ещё: столбцы, CSV, шаблоны";moreButton.Click+=(s,e)=>TableOptions(moreButton);DockPanel.SetDock(moreButton,WDock.Right);tools.Children.Add(moreButton);
                var locate=UiTheme.Button("Показать в AutoCAD",UiTheme.IconLocate,"Outline");locate.Margin=new Thickness(0,0,8,0);locate.ToolTip="Приблизить выбранную рамку в AutoCAD · двойной щелчок по строке";locate.Click+=async(s,e)=>await LocateInCadAsync();DockPanel.SetDock(locate,WDock.Right);tools.Children.Add(locate);
                var none=UiTheme.Button("Снять все",null,"Outline");none.Margin=new Thickness(0,0,8,0);none.Click+=(s,e)=>Guard(()=>{c.SetAll(false);table.Items.Refresh();});DockPanel.SetDock(none,WDock.Right);tools.Children.Add(none);
                var all=UiTheme.Button("Выбрать все",null,"Outline");all.Margin=new Thickness(0,0,8,0);all.Click+=(s,e)=>Guard(()=>{c.SetAll(true);table.Items.Refresh();});DockPanel.SetDock(all,WDock.Right);tools.Children.Add(all);
                WGrid.SetRow(tools,1);pane.Children.Add(tools);refresh.Add(()=>{bool on=c.running==null&&c.frames.Count>0;tools.IsEnabled=c.running==null;all.IsEnabled=none.IsEnabled=locate.IsEnabled=on;});

                table.AutoGenerateColumns=false;table.CanUserAddRows=false;table.CanUserDeleteRows=false;table.CanUserSortColumns=false;table.CanUserReorderColumns=true;table.CanUserResizeRows=false;table.HeadersVisibility=DataGridHeadersVisibility.Column;table.SelectionMode=DataGridSelectionMode.Extended;table.SelectionUnit=DataGridSelectionUnit.FullRow;
                table.RowHeight=44;table.BorderThickness=new Thickness(0);table.Foreground=UiTheme.Ink;table.Background=Brushes.White;table.RowBackground=Brushes.White;table.AlternatingRowBackground=Brushes.White;table.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;table.HorizontalGridLinesBrush=new SolidColorBrush(Color.FromRgb(242,242,246));
                table.EnableRowVirtualization=true;table.EnableColumnVirtualization=true;VirtualizingPanel.SetVirtualizationMode(table,VirtualizationMode.Recycling);
                table.Columns.Add(new DataGridCheckBoxColumn{Header="",Binding=new Binding("Enabled"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},ElementStyle=(Style)Resources["SheetCheck"],EditingElementStyle=(Style)Resources["SheetCheck"],Width=46,CanUserResize=false});
                Col("№","Page",48);Col("Наименование","Title",240,false);table.Columns[table.Columns.Count-1].Width=new DataGridLength(1,DataGridLengthUnitType.Star);table.Columns[table.Columns.Count-1].MinWidth=180;Col("Лист","SheetNumber",64,false);FormatColumn();Col("Размер, мм","SizeText",112);Col("Масштаб","Scale",82);Col("Угол","AngleDegrees",62);Col("Вкладка","Layout",96);
                Col("Чертёж","File",180,true,false);Col("Слой","Layer",100,true,false);Col("Шифр","Code",130,false,false);Col("Ревизия","Revision",85,false,false);Col("Источник","Kind",140,true,false);Col("Состояние","Status",220,true,false);Col("Копии","Copies",65,false,false);Col("Устройство","Device",160,false,false);Col("CTB / STB","StyleSheet",150,false,false);Col("Масштаб из","ScaleSource",160,true,false);
                view=new ListCollectionView((System.Collections.IList)c.frames);table.ItemsSource=view;
                c.frames.ListChanged+=(s,e)=>{if(view is System.ComponentModel.IEditableCollectionView editing&&!editing.IsEditingItem&&!editing.IsAddingNew)view.Refresh();};table.SelectionChanged+=(s,e)=>{c.fluentSelection=table.SelectedItem as DetectedFrame;foreach(Forms.DataGridViewRow r in c.grid.Rows)r.Selected=table.SelectedItems.Contains(r.DataBoundItem);RenderSelection();};
                table.MouseDoubleClick+=async(s,e)=>{if(table.CurrentColumn?.IsReadOnly==true&&table.SelectedItem!=null&&IsRow(e.OriginalSource))await LocateInCadAsync();};table.CellEditEnding+=(s,e)=>Dispatcher.BeginInvoke(new Action(()=>{c.UpdateCount();RenderSelection();}));
                table.PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Space&&!IsEditing()&&table.SelectedItems.Count>0){var rows=table.SelectedItems.OfType<DetectedFrame>().ToList();bool value=rows.Any(f=>!f.Enabled);foreach(var f in rows)f.Enabled=value;c.UpdateCount();table.Items.Refresh();e.Handled=true;}};
                var holder=new WGrid();var border=UiTheme.Card(table,1,false);border.Margin=new Thickness(0);border.ClipToBounds=true;holder.Children.Add(border);holder.Children.Add(BuildEmptyState());WGrid.SetRow(holder,2);pane.Children.Add(holder);
                var hint=UiTheme.Hint("Пробел — отметить строки · двойной щелчок — показать в AutoCAD · наименование и номер листа можно исправить прямо в таблице",new Thickness(2,9,0,0));WGrid.SetRow(hint,3);pane.Children.Add(hint);
            }
            private UIElement BuildEmptyState()
            {
                var stack=new StackPanel{HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center,MaxWidth=380};
                stack.Children.Add(new Border{Width=64,Height=64,CornerRadius=new CornerRadius(32),Background=UiTheme.AccentSoft,Margin=new Thickness(0,0,0,18),Child=new TextBlock{Text=UiTheme.IconSearch,FontFamily=UiTheme.Icons,FontSize=26,Foreground=UiTheme.Accent,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}});
                var title=UiTheme.Title("Листов пока нет",20);title.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(title);
                var text=UiTheme.Text(demo?"Это демо-режим: нажмите «Найти рамки», чтобы посмотреть, как выглядят найденные листы.":"Откройте чертёж в AutoCAD, выберите слева, где искать, и нажмите «Найти рамки». Найденные листы появятся здесь.",13,null,UiTheme.Secondary);
                text.TextAlignment=TextAlignment.Center;text.Margin=new Thickness(0,8,0,20);text.LineHeight=19;stack.Children.Add(text);
                var find=UiTheme.Button("Найти рамки",UiTheme.IconSearch,"Primary");find.HorizontalAlignment=HorizontalAlignment.Center;find.Click+=async(s,e)=>await FindAsync();stack.Children.Add(find);
                var keys=UiTheme.Hint("или нажмите F5",new Thickness(0,10,0,0));keys.HorizontalAlignment=HorizontalAlignment.Center;stack.Children.Add(keys);
                emptyState.Child=stack;emptyState.Background=Brushes.White;emptyState.CornerRadius=new CornerRadius(14);emptyState.BorderBrush=UiTheme.Line;emptyState.BorderThickness=new Thickness(1);
                refresh.Add(()=>{var v=c.frames.Count==0?Visibility.Visible:Visibility.Collapsed;if(emptyState.Visibility!=v)emptyState.Visibility=v;find.IsEnabled=c.running==null;});
                return emptyState;
            }
            private void BuildDetails()
            {
                var right=new StackPanel();var scroll=new ScrollViewer{Content=right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(0,0,-8,0),Padding=new Thickness(0,0,8,0)};WGrid.SetColumn(scroll,2);body.Children.Add(scroll);
                right.Children.Add(UiTheme.StepHeader(3,"Сохраните PDF","Куда и как выводить листы",out step3));
                var settings=new StackPanel();
                var modes=new ComboBox{ItemsSource=c.pdfModes.Select(m=>m.Text).ToArray()};modes.SelectionChanged+=(s,e)=>{if(modes.SelectedIndex>=0&&modes.SelectedIndex!=(int)c.profile.PdfMode&&c.running==null){c.ChangePdfMode((PdfOutputMode)modes.SelectedIndex);Sync();}};
                refresh.Add(()=>{if(modes.SelectedIndex!=(int)c.profile.PdfMode)modes.SelectedIndex=(int)c.profile.PdfMode;});settings.Children.Add(Field("Как сохранить",modes));
                var browse=new DockPanel();var choose=UiTheme.Button("",UiTheme.IconFolder,"Square");choose.ToolTip="Выбрать папку";choose.Margin=new Thickness(6,0,0,0);choose.Click+=(s,e)=>Guard(()=>c.ChooseFolder());DockPanel.SetDock(choose,WDock.Right);browse.Children.Add(choose);
                var folderInput=Input(c.folder);folderInput.Tag="Выберите папку…";browse.Children.Add(folderInput);settings.Children.Add(Field("Папка",browse,out var folderCaption));
                var openFolder=UiTheme.Button("Открыть папку",UiTheme.IconOpen,"Plain");openFolder.HorizontalAlignment=HorizontalAlignment.Left;openFolder.Margin=new Thickness(-8,-8,0,6);openFolder.FontWeight=FontWeights.Normal;openFolder.FontSize=12;openFolder.Click+=(s,e)=>OpenFolder(c.folder.Text);settings.Children.Add(openFolder);
                refresh.Add(()=>{string f=c.folder.Text.Trim();bool exists=f.Length>0&&Path.IsPathRooted(f)&&Directory.Exists(f);var v=exists?Visibility.Visible:Visibility.Collapsed;if(openFolder.Visibility!=v)openFolder.Visibility=v;folderCaption.Foreground=f.Length==0&&c.frames.Any(x=>x.Enabled)?UiTheme.Warning:UiTheme.Secondary;});
                nameInput=Input(c.pattern);var nameField=Field("Имя файла",nameInput,out var nameCaption);settings.Children.Add(nameField);
                var tokens=new WrapPanel{Margin=new Thickness(0,-6,0,10)};
                foreach(var token in new[]{("Документ","Document"),("№ листа","SheetNumber"),("Наименование","Title"),("Формат","Format"),("Порядковый №","Page"),("Шифр","Code")}){var key=token.Item2;var b=UiTheme.Button(token.Item1,null,"Chip");b.ToolTip="Вставить {"+key+"}";b.Click+=(s,e)=>InsertToken(key);tokens.Children.Add(b);}
                settings.Children.Add(tokens);refresh.Add(()=>{bool one=c.profile.PdfMode==PdfOutputMode.OneFile;var v=one?Visibility.Collapsed:Visibility.Visible;if(tokens.Visibility!=v)tokens.Visibility=v;nameCaption.Text=one?"Имя общего PDF":"Имя файла";});
                var colors=new ComboBox{ItemsSource=c.color.Items.Cast<string>().ToArray(),SelectedIndex=c.color.SelectedIndex};colors.SelectionChanged+=(s,e)=>{if(colors.SelectedIndex>=0)c.color.SelectedIndex=colors.SelectedIndex;};refresh.Add(()=>{if(colors.SelectedIndex!=c.color.SelectedIndex)colors.SelectedIndex=c.color.SelectedIndex;});settings.Children.Add(Field("Цвет",colors));
                var styleField=Field("Таблица стилей CTB / STB",Input(c.style));settings.Children.Add(styleField);refresh.Add(()=>{var v=c.style.Enabled?Visibility.Visible:Visibility.Collapsed;if(styleField.Visibility!=v)styleField.Visibility=v;});
                var fitRow=ToggleRow("Вписать в бумагу",c.fit,"Выключите для печати в точном масштабе 1:1");((FrameworkElement)fitRow).Margin=new Thickness(0,0,0,12);settings.Children.Add(fitRow);
                var plotter=new StackPanel();plotter.Children.Add(Field("Плоттер PDF (PC3)",Input(c.device)));
                var templates=UiTheme.Button("Настройки по форматам…",UiTheme.IconSettings,"Outline");templates.HorizontalAlignment=HorizontalAlignment.Stretch;templates.Click+=(s,e)=>Guard(()=>{c.EditProfile();table.Items.Refresh();});plotter.Children.Add(templates);
                plotter.Children.Add(UiTheme.Hint("Своё устройство, бумага, стиль и поворот для каждого формата.",new Thickness(0,8,0,0)));
                var expander=new Expander{Header="Плоттер и шаблоны",Content=plotter,Margin=new Thickness(0,2,0,0)};settings.Children.Add(expander);
                pdfPane.Child=settings;right.Children.Add(UiTheme.Card(pdfPane));
                right.Children.Add(UiTheme.Card(selected));RenderSelection();
            }
            private UIElement BuildFooter()
            {
                var footer=new Border{Background=Brushes.White,BorderBrush=UiTheme.Line,BorderThickness=new Thickness(0,1,0,0),Padding=new Thickness(24,12,24,12)};
                var grid=new WGrid();footer.Child=grid;grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                var left=new StackPanel{VerticalAlignment=VerticalAlignment.Center};var line=new DockPanel();statusIcon.Margin=new Thickness(0,0,9,0);statusIcon.VerticalAlignment=VerticalAlignment.Center;line.Children.Add(statusIcon);
                stop.Margin=new Thickness(12,0,0,0);stop.Click+=(s,e)=>c.running?.Cancel();DockPanel.SetDock(stop,WDock.Right);openResult.Margin=new Thickness(8,0,0,0);openResult.Click+=(s,e)=>OpenFolder(c.LastOutputFolder??c.folder.Text);DockPanel.SetDock(openResult,WDock.Right);
                line.Children.Add(stop);line.Children.Add(openResult);
                state.TextTrimming=TextTrimming.CharacterEllipsis;state.Foreground=UiTheme.Ink;state.VerticalAlignment=VerticalAlignment.Center;line.Children.Add(state);left.Children.Add(line);
                activity.Margin=new Thickness(0,9,0,0);activity.MaxWidth=560;activity.HorizontalAlignment=HorizontalAlignment.Stretch;left.Children.Add(activity);grid.Children.Add(left);
                var right=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(24,0,0,0)};
                readiness.Foreground=UiTheme.Secondary;readiness.FontSize=12;readiness.VerticalAlignment=VerticalAlignment.Center;readiness.Margin=new Thickness(0,0,16,0);readiness.TextAlignment=TextAlignment.Right;readiness.MaxWidth=340;readiness.TextWrapping=TextWrapping.Wrap;right.Children.Add(readiness);
                output.SetResourceReference(StyleProperty,"Primary");output.MinWidth=210;output.Height=44;output.FontSize=14;output.Click+=async(s,e)=>await OutputAsync();right.Children.Add(output);
                WGrid.SetColumn(right,1);grid.Children.Add(right);
                return footer;
            }

            private void Col(string text,string property,double width,bool readOnly=true,bool visible=true)
            {
                var style=new Style(typeof(TextBlock));style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty,TextTrimming.CharacterEllipsis));style.Setters.Add(new Setter(VerticalAlignmentProperty,VerticalAlignment.Center));
                if(!readOnly)style.Setters.Add(new Setter(TextBlock.ToolTipProperty,"Можно изменить: щёлкните по ячейке дважды или нажмите F2"));
                table.Columns.Add(new DataGridTextColumn{Header=text,Binding=new Binding(property){Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=width,IsReadOnly=readOnly,Visibility=visible?Visibility.Visible:Visibility.Collapsed,ElementStyle=style});
            }
            private void FormatColumn()
            {
                var pill=new FrameworkElementFactory(typeof(Border));pill.SetValue(Border.BackgroundProperty,new SolidColorBrush(Color.FromRgb(242,242,247)));pill.SetValue(Border.CornerRadiusProperty,new CornerRadius(6));pill.SetValue(Border.PaddingProperty,new Thickness(8,2,8,3));pill.SetValue(HorizontalAlignmentProperty,HorizontalAlignment.Left);pill.SetValue(VerticalAlignmentProperty,VerticalAlignment.Center);
                var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new Binding("FormatName"){Mode=BindingMode.OneWay});text.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);text.SetValue(TextBlock.FontSizeProperty,12.0);pill.AppendChild(text);
                table.Columns.Add(new DataGridTemplateColumn{Header="Формат",CellTemplate=new DataTemplate{VisualTree=pill},Width=78,IsReadOnly=true});
            }
            private bool IsEditing()=>Keyboard.FocusedElement is WText box&&box.IsDescendantOf(table);
            private static bool IsRow(object source){var d=source as DependencyObject;while(d!=null){if(d is DataGridRow)return true;if(d is DataGridColumnHeader)return false;d=d is Visual||d is System.Windows.Media.Media3D.Visual3D?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);}return false;}
            private void CommitGrid(){table.CommitEdit(DataGridEditingUnit.Cell,true);table.CommitEdit(DataGridEditingUnit.Row,true);}
            private void SaveView(){c.profile.Window=new WindowSettings{Width=WindowState==WindowState.Maximized?RestoreBounds.Width:Width,Height=WindowState==WindowState.Maximized?RestoreBounds.Height:Height,Maximized=WindowState==WindowState.Maximized,Columns=table.Columns.Select(x=>new ColumnSettings{Name=x.Header?.ToString()??"",Visible=x.Visibility==Visibility.Visible,Width=x.ActualWidth,Order=x.DisplayIndex}).ToList()};}
            private void RestoreView(){var settings=c.profile.Window;if(settings==null)return;if(Numeric.Positive(settings.Width)&&Numeric.Positive(settings.Height)){Width=Math.Max(MinWidth,Math.Min(settings.Width,SystemParameters.WorkArea.Width));Height=Math.Max(MinHeight,Math.Min(settings.Height,SystemParameters.WorkArea.Height));}if(settings.Maximized)WindowState=WindowState.Maximized;if(settings.Columns==null)return;foreach(var col in settings.Columns.OrderBy(x=>x.Order)){var target=table.Columns.FirstOrDefault(x=>(x.Header?.ToString()??"")==col.Name);if(target==null)continue;target.Visibility=col.Visible?Visibility.Visible:Visibility.Collapsed;if(Numeric.Positive(col.Width))target.Width=Math.Max(35,Math.Min(col.Width,700));if(col.Order>=0&&col.Order<table.Columns.Count)target.DisplayIndex=col.Order;}}
            private void ApplyFilter(string text)
            {
                CommitGrid();tableFilter=text.Trim();if(view==null)return;
                view.Filter=tableFilter.Length==0?null:new Predicate<object>(obj=>obj is DetectedFrame f&&new[]{f.Title,f.SheetNumber,f.Code,f.FormatName,f.File,f.Page.ToString()}.Any(x=>(x??"").IndexOf(tableFilter,StringComparison.CurrentCultureIgnoreCase)>=0));
            }
            private void TableOptions(FrameworkElement anchor)
            {
                var menu=new ContextMenu{PlacementTarget=anchor,Placement=PlacementMode.Bottom};
                menu.Items.Add(Command("Столбцы таблицы…",()=>Guard(ChooseColumns)));menu.Items.Add(Command("Сохранить список в CSV…",()=>Guard(()=>c.SaveCsv())));
                menu.Items.Add(new System.Windows.Controls.Separator());menu.Items.Add(Command("Настройки печати по форматам…",()=>Guard(()=>{c.EditProfile();table.Items.Refresh();})));
                menu.IsOpen=true;
            }
            private void ChooseColumns(){var values=c.AskChecks("Столбцы таблицы",table.Columns.Skip(1).Select(x=>(x.Header?.ToString()??"",x.Visibility==Visibility.Visible)).ToArray());if(values==null)return;for(int i=0;i<values.Length;i++)table.Columns[i+1].Visibility=values[i]?Visibility.Visible:Visibility.Collapsed;}
            private UIElement InlineInput(string caption,Forms.TextBox source,string tip)
            {
                var row=new DockPanel{Margin=new Thickness(0,4,0,4),ToolTip=tip};var input=Input(source);input.Width=96;input.TextAlignment=TextAlignment.Right;DockPanel.SetDock(input,WDock.Right);row.Children.Add(input);
                var label=UiTheme.Text(caption,13);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);return row;
            }
            private WText Input(Forms.TextBox source)
            {
                var input=new WText{Text=source.Text,MinWidth=40};System.Windows.Automation.AutomationProperties.SetName(input,source.AccessibleName??"");input.TextChanged+=(s,e)=>{if(source.Text!=input.Text)source.Text=input.Text;};refresh.Add(()=>{if(!input.IsKeyboardFocusWithin&&input.Text!=source.Text)input.Text=source.Text;});return input;
            }
            private Border Group(params UIElement[] rows)
            {
                var stack=new StackPanel();for(int i=0;i<rows.Length;i++){if(i>0)stack.Children.Add(new Border{Height=1,Background=UiTheme.Line,Margin=new Thickness(14,0,0,0)});stack.Children.Add(rows[i]);}
                var card=UiTheme.Card(stack,0);card.Margin=new Thickness(0,0,0,18);return card;
            }
            private UIElement ToggleRow(string text,Forms.CheckBox source,string tip)
            {
                var row=new WGrid{Margin=new Thickness(14,9,12,9),Background=Brushes.Transparent,ToolTip=tip,Cursor=Cursors.Hand};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
                var toggle=new WCheck{IsChecked=source.Checked,Style=(Style)Resources["Toggle"],VerticalAlignment=VerticalAlignment.Center};WGrid.SetColumn(toggle,1);row.Children.Add(toggle);
                toggle.Checked+=(s,e)=>source.Checked=true;toggle.Unchecked+=(s,e)=>source.Checked=false;refresh.Add(()=>{if(toggle.IsChecked!=source.Checked)toggle.IsChecked=source.Checked;});
                row.MouseLeftButtonUp+=(s,e)=>{if(!(e.OriginalSource is DependencyObject d&&IsInside(d,toggle))&&toggle.IsEnabled)toggle.IsChecked=toggle.IsChecked!=true;};
                var caption=UiTheme.Text(text,13);caption.VerticalAlignment=VerticalAlignment.Center;caption.Margin=new Thickness(0,0,10,0);row.Children.Add(caption);System.Windows.Automation.AutomationProperties.SetName(toggle,text);return row;
            }
            private static bool IsInside(DependencyObject d,DependencyObject parent){while(d!=null){if(d==parent)return true;d=d is Visual?VisualTreeHelper.GetParent(d):LogicalTreeHelper.GetParent(d);}return false;}
            private static StackPanel Field(string caption,UIElement child)=>Field(caption,child,out _);
            private static StackPanel Field(string caption,UIElement child,out TextBlock label){var field=new StackPanel{Margin=new Thickness(0,0,0,14)};label=UiTheme.Hint(caption,new Thickness(2,0,0,6));label.FontWeight=FontWeights.SemiBold;field.Children.Add(label);field.Children.Add(child);return field;}
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
                try{Process.Start(new ProcessStartInfo(folder){UseShellExecute=true});}catch(Exception ex){ModernDialog.Message(Handle,"Не удалось открыть папку",ex.Message,DialogKind.Warning);}
            }
            private async Task FindAsync(){if(c.running!=null)return;await Execute(c.ScanAsync);if(c.frames.Count>0&&table.SelectedItem==null)table.SelectedIndex=0;}
            private async Task OutputAsync()
            {
                if(c.running!=null||!output.IsEnabled)return;CommitGrid();
                // Choosing the folder is part of the first export, not a separate hurdle.
                if(string.IsNullOrWhiteSpace(c.folder.Text)){c.ChooseFolder();if(string.IsNullOrWhiteSpace(c.folder.Text))return;}
                await Execute(()=>c.ExportAsync(false));
            }
            private async Task LocateInCadAsync()
            {
                if(table.SelectedItem==null){ModernDialog.Message(Handle,"Выберите лист","Щёлкните по строке в таблице, чтобы показать её рамку в AutoCAD.");return;}
                if(!c.host.CanPlot){ModernDialog.Message(Handle,demo?"Демо-режим":"Нет связи с AutoCAD",demo?"Показ рамки работает после подключения к AutoCAD.":"Подключитесь к AutoCAD заново через меню «Приложение».");return;}
                await Execute(c.LocateAsync);if(c.status.Text.StartsWith("Показана рамка",StringComparison.Ordinal))WindowState=WindowState.Minimized;
            }
            private async Task Execute(Func<Task> action){CommitGrid();await c.RunAsync(action);table.Items.Refresh();Sync();}
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
                    "1. Найдите рамки\nСлева выберите, где искать (модель, листы, все открытые DWG) и что считать рамкой. Нажмите «Найти рамки» или F5.\n\n"+
                    "2. Проверьте листы\nВ таблице появятся найденные листы. Снимите галочки с лишних, исправьте наименование или номер прямо в ячейке. Двойной щелчок показывает рамку в AutoCAD. Порядок листов меняется в меню «Порядок».\n\n"+
                    "3. Сохраните PDF\nСправа выберите, как сохранить (отдельные файлы, один общий PDF или группы), папку и шаблон имени. Нажмите синюю кнопку внизу или Ctrl+Enter. Перед выводом вы увидите план и сможете отменить его.\n\n"+
                    "Дополнительно\nМеню «Чертёж» — листы Layout, экспорт DWG, ведомости и атрибуты. «Задание» — сохранить и повторить комплект. «Печать» — вывод на принтер.");
            }
            private void ShowShortcuts()=>ModernDialog.Text(Handle,"Горячие клавиши","F5 — найти рамки\nCtrl+Enter — вывести PDF\nCtrl+F — поиск по таблице\nПробел — отметить или снять выбранные строки\nF2 — изменить ячейку\nДвойной щелчок — показать рамку в AutoCAD\nEsc — отменить текущую операцию");
            private void Sync()
            {
                foreach(var action in refresh)action();bool busy=c.running!=null;findPane.IsEnabled=pdfPane.IsEnabled=table.IsEnabled=!busy;
                int total=c.frames.Count,marked=c.frames.Count(f=>f.Enabled);bool one=c.profile.PdfMode==PdfOutputMode.OneFile;
                output.IsEnabled=!busy&&c.host.CanPlot&&marked>0&&!c.scaleNeedsSearch;
                output.Content=UiTheme.Content(one?"Сохранить один PDF":marked==0?"Вывести PDF":"Вывести "+marked+" "+Plural(marked,"лист","листа","листов"),UiTheme.IconDownload);
                readiness.Text=busy?"Идёт операция…":!c.host.CanPlot?(demo?"В демо-режиме PDF не создаются":"Подключите AutoCAD, чтобы создавать PDF"):total==0?"Сначала найдите рамки":marked==0?"Отметьте хотя бы один лист":c.scaleNeedsSearch?"Параметры изменились — найдите рамки заново":string.IsNullOrWhiteSpace(c.folder.Text)?"Папку предложим выбрать при выводе":"Ctrl+Enter";
                readiness.Foreground=output.IsEnabled||busy?UiTheme.Tertiary:UiTheme.Warning;
                stop.Visibility=busy?Visibility.Visible:Visibility.Collapsed;activity.Visibility=busy&&c.progress.Maximum>0?Visibility.Visible:Visibility.Collapsed;
                openResult.Visibility=!busy&&c.LastOutputFolder!=null&&c.status.Text.StartsWith("Готово",StringComparison.Ordinal)?Visibility.Visible:Visibility.Collapsed;
                c.RefreshWorkspaceCount();summary.Text=total==0?"Здесь появятся найденные листы":c.frameSummary.Text;if(state.Text!=c.status.Text){state.Text=c.status.Text;RenderStatusIcon(busy);}
                activity.Maximum=c.progress.Maximum;activity.Value=c.progress.Value;
                step2.Background=total>0?UiTheme.Accent:UiTheme.Fill;((TextBlock)step2.Child).Foreground=total>0?Brushes.White:UiTheme.Secondary;
                step3.Background=marked>0?UiTheme.Accent:UiTheme.Fill;((TextBlock)step3.Child).Foreground=marked>0?Brushes.White:UiTheme.Secondary;
                if(knownFrames!=total){knownFrames=total;if(table.SelectedItem==null&&total>0)table.SelectedIndex=0;RenderSelection();}
            }
            private void RenderStatusIcon(bool busy)
            {
                statusIcon.Children.Clear();string text=c.status.Text;string glyph;Brush color;
                if(busy){glyph=UiTheme.IconRefresh;color=UiTheme.Accent;}
                else if(text.StartsWith("Готово",StringComparison.Ordinal)||text.StartsWith("Найдено",StringComparison.Ordinal)||text.StartsWith("Задание",StringComparison.Ordinal)){glyph=UiTheme.IconSuccess;color=UiTheme.Success;}
                else if(text.IndexOf("ошиб",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("отмен",StringComparison.OrdinalIgnoreCase)>=0||text.IndexOf("измен",StringComparison.OrdinalIgnoreCase)>=0){glyph=UiTheme.IconWarning;color=UiTheme.Warning;}
                else{glyph=UiTheme.IconInfo;color=UiTheme.Tertiary;}
                statusIcon.Children.Add(UiTheme.Icon(glyph,15,color));
            }
            private static string Plural(int n,string one,string few,string many){int m=n%100;if(m>=11&&m<=14)return many;switch(n%10){case 1:return one;case 2:case 3:case 4:return few;default:return many;}}
            private void RenderSelection()
            {
                selected.Children.Clear();var f=table.SelectedItem as DetectedFrame;
                selected.Children.Add(UiTheme.Caption("Выбранный лист"));
                if(f==null){selected.Children.Add(UiTheme.Hint("Щёлкните по строке таблицы — здесь появятся размеры, масштаб и данные штампа.",new Thickness(4,0,0,0)));return;}
                var head=UiTheme.Title((f.SheetNumber.Length>0?"Лист "+f.SheetNumber:"Строка "+f.Page)+" · "+f.FormatName,17);head.Margin=new Thickness(4,0,0,2);selected.Children.Add(head);
                var title=UiTheme.Hint(f.Title.Length>0?f.Title:"Без наименования",new Thickness(4,0,0,12));selected.Children.Add(title);
                selected.Children.Add(Paper(f));
                var facts=new WGrid{Margin=new Thickness(4,12,0,8)};facts.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(92)});facts.ColumnDefinitions.Add(new ColumnDefinition());
                var rows=new List<(string,string)>{("Размер",f.SizeText+" мм"),("Масштаб",f.Scale.ToString("0.########")+(f.AngleDegrees!=0?" · "+f.AngleDegrees+"°":"")),("Вкладка",f.Layout),("Чертёж",f.File)};
                if(f.Code.Length>0)rows.Add(("Шифр",f.Code));if(f.Revision.Length>0)rows.Add(("Ревизия",f.Revision));if(f.Layer.Length>0)rows.Add(("Слой",f.Layer));
                foreach(var k in f.Metadata.Where(k=>k.Key!="Title"&&k.Key!="Code"&&k.Key!="SheetNumber"&&k.Key!="Revision"&&!string.IsNullOrWhiteSpace(k.Value)).Take(6))rows.Add((k.Key,k.Value));
                if(f.Status.Length>0)rows.Add(("Состояние",f.Status));
                for(int i=0;i<rows.Count;i++)
                {
                    facts.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});var l=UiTheme.Hint(rows[i].Item1,new Thickness(0,3,8,3));WGrid.SetRow(l,i);facts.Children.Add(l);
                    var v=UiTheme.Text(rows[i].Item2,12);v.Margin=new Thickness(0,3,0,3);v.TextTrimming=TextTrimming.CharacterEllipsis;v.TextWrapping=TextWrapping.NoWrap;v.ToolTip=rows[i].Item2;WGrid.SetRow(v,i);WGrid.SetColumn(v,1);facts.Children.Add(v);
                }
                selected.Children.Add(facts);
                var actions=new WrapPanel();var show=UiTheme.Button("Показать",UiTheme.IconLocate,"Outline");show.Margin=new Thickness(0,0,8,0);show.Click+=async(s,e)=>await LocateInCadAsync();actions.Children.Add(show);
                var pdf=UiTheme.Button("PDF этого листа",UiTheme.IconDocument,"Outline");pdf.IsEnabled=c.host.CanPlot&&c.running==null;pdf.ToolTip="Создать PDF только для выбранной строки";pdf.Click+=async(s,e)=>{if(string.IsNullOrWhiteSpace(c.folder.Text)){c.ChooseFolder();if(string.IsNullOrWhiteSpace(c.folder.Text))return;}await Execute(()=>c.ExportAsync(true));};actions.Children.Add(pdf);selected.Children.Add(actions);
            }
            private static UIElement Paper(DetectedFrame f)
            {
                // A proportional sheet with the 185 × 55 mm title block, not a picture of the DWG content.
                var canvas=new Canvas{Height=150,Background=UiTheme.Canvas,ClipToBounds=true};var host=new Border{Child=canvas,CornerRadius=new CornerRadius(10),Background=UiTheme.Canvas,ClipToBounds=true};
                double w=Math.Max(1,f.PaperWidthMm),h=Math.Max(1,f.PaperHeightMm),factor=Math.Min(228/w,118/h),pw=w*factor,ph=h*factor,left=(256-pw)/2,top=(150-ph)/2;
                var sheet=new System.Windows.Shapes.Rectangle{Width=pw,Height=ph,Fill=Brushes.White,Stroke=new SolidColorBrush(Color.FromRgb(174,174,178)),StrokeThickness=1,RadiusX=1.5,RadiusY=1.5,Effect=new System.Windows.Media.Effects.DropShadowEffect{BlurRadius=8,ShadowDepth=1,Direction=270,Opacity=0.12}};
                Canvas.SetLeft(sheet,Math.Max(8,left));Canvas.SetTop(sheet,top);canvas.Children.Add(sheet);
                double sw=Math.Min(185*factor,pw-4),sh=Math.Min(55*factor,ph-4);
                if(sw>6&&sh>3){var stamp=new System.Windows.Shapes.Rectangle{Width=sw,Height=sh,Stroke=UiTheme.Accent,StrokeThickness=1,Fill=UiTheme.AccentSoft};Canvas.SetLeft(stamp,Math.Max(8,left)+pw-sw-2);Canvas.SetTop(stamp,top+ph-sh-2);canvas.Children.Add(stamp);}
                var size=new TextBlock{Text=f.FormatName,FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=UiTheme.Secondary};Canvas.SetLeft(size,Math.Max(8,left)+6);Canvas.SetTop(size,top+4);canvas.Children.Add(size);
                return host;
            }
            public async Task SmokeAsync(string path)
            {
                await Execute(c.ScanAsync);c.VerifyCombinedPdfUiForSmokeTest();Sync();table.SelectedIndex=0;UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if(c.frames.Count!=12||c.ToolMenuCount!=6||table.Columns.Count<16)throw new InvalidOperationException("WPF smoke: missing frames, menus or columns.");
                RenderTo(path);
                File.WriteAllText(path+".txt","WPF + ModernWpf smoke PASS: 12 frames, six menus, editable grid, PDF modes/names, selected frame, scoped theme. CAD output disabled in demo.");
            }
            public void RenderTo(string path)
            {
                var visual=(FrameworkElement)Content;if(visual.ActualWidth<1)throw new InvalidOperationException("WPF smoke: the window content was not laid out.");
                var bmp=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))encoder.Save(file);
            }
        }
    }
}
