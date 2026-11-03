using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CadBatch.Core;
using WButton=System.Windows.Controls.Button;
using WCheck=System.Windows.Controls.CheckBox;
using WGrid=System.Windows.Controls.Grid;
using WText=System.Windows.Controls.TextBox;
using Forms=System.Windows.Forms;

namespace CadBatch.UI
{
    public sealed partial class BatchWindow
    {
        private DetectedFrame? fluentSelection;
        private Forms.IWin32Window? fluentOwner;
        private Forms.IWin32Window DialogOwner=>fluentOwner??this;
        private sealed class DialogWindowOwner : Forms.IWin32Window {public DialogWindowOwner(IntPtr handle){Handle=handle;}public IntPtr Handle {get;private set;}}
        public sealed class FluentWindow : Window
        {
            private readonly BatchWindow c;
            private readonly DispatcherTimer timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(180)};
            private readonly List<Action> refresh=new List<Action>();
            private readonly DataGrid table=new DataGrid();
            private System.ComponentModel.ICollectionView? view;
            private string tableFilter="";
            private bool isClosed;
            private readonly StackPanel selected=new StackPanel(),menuItems=new StackPanel{Orientation=Orientation.Horizontal};
            private readonly TextBlock summary=new TextBlock(),state=new TextBlock(),modeHint=new TextBlock();
            private readonly ProgressBar activity=new ProgressBar();
            private readonly WButton output=new WButton(),stop=new WButton();
            private readonly WGrid body=new WGrid();
            private readonly Menu menus=new Menu();
            private readonly Border findPane=new Border(),pdfPane=new Border();
            private static readonly Brush Ink=new SolidColorBrush(Color.FromRgb(30,31,35)),Muted=new SolidColorBrush(Color.FromRgb(118,121,130)),Accent=new SolidColorBrush(Color.FromRgb(0,113,227));
            public FluentWindow(IBatchHost host,string? settingsFile=null)
            {
                c=new BatchWindow(host,settingsFile);c.grid.BindingContext=new Forms.BindingContext();
                Title="CAD Batch "+ProductInfo.Version;Width=Math.Min(1580,SystemParameters.WorkArea.Width-40);Height=Math.Min(1000,SystemParameters.WorkArea.Height-40);MinWidth=Math.Min(1120,SystemParameters.WorkArea.Width-24);MinHeight=Math.Min(720,SystemParameters.WorkArea.Height-32);WindowStartupLocation=WindowStartupLocation.CenterScreen;
                FontFamily=new FontFamily("Segoe UI");FontSize=13;Background=new SolidColorBrush(Color.FromRgb(246,246,249));Foreground=Ink;
                // Window-scoped dictionaries do not replace AutoCAD's application resources or theme.
                Resources=(ResourceDictionary)XamlReader.Parse(@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:ui='http://schemas.modernwpf.com/2019'>
                  <ResourceDictionary.MergedDictionaries><ui:ThemeResources/><ui:XamlControlsResources/></ResourceDictionary.MergedDictionaries>
                  <Style TargetType='Button'><Setter Property='Background' Value='#EDEEF2'/><Setter Property='Foreground' Value='#202126'/><Setter Property='Padding' Value='14,10'/><Setter Property='Margin' Value='0,0,7,0'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='surface' CornerRadius='9' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='surface' Property='Opacity' Value='0.8'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter TargetName='surface' Property='Opacity' Value='0.38'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='surface' Property='BorderBrush' Value='#0071E3'/><Setter TargetName='surface' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
                  <Style x:Key='Toggle' TargetType='CheckBox'><Setter Property='Focusable' Value='True'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Grid Width='42' Height='24'><Border x:Name='track' Background='#999DA6' CornerRadius='12'/><Ellipse x:Name='thumb' Width='18' Height='18' Fill='White' HorizontalAlignment='Left' Margin='3'/></Grid><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='track' Property='Background' Value='#0071E3'/><Setter TargetName='thumb' Property='HorizontalAlignment' Value='Right'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='track' Property='BorderBrush' Value='#222222'/><Setter TargetName='track' Property='BorderThickness' Value='1'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
                  <Style x:Key='SheetCheck' TargetType='CheckBox'><Setter Property='HorizontalAlignment' Value='Center'/><Setter Property='VerticalAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'><Border x:Name='box' Width='20' Height='20' CornerRadius='5' BorderBrush='#C3C7D0' BorderThickness='1' Background='White'><TextBlock x:Name='mark' Text='✓' Foreground='White' FontSize='14' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/></Border><ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='box' Property='Background' Value='#0071E3'/><Setter TargetName='box' Property='BorderBrush' Value='#0071E3'/><Setter TargetName='mark' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
                  <Style TargetType='ComboBox'><Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#202126'/><Setter Property='BorderBrush' Value='#D7D9E0'/><Setter Property='MinHeight' Value='36'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid><ToggleButton Focusable='False' HorizontalAlignment='Stretch' VerticalAlignment='Stretch' Margin='0' MinWidth='0' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'><ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='White' BorderBrush='#D7D9E0' BorderThickness='1' CornerRadius='7'/></ControlTemplate></ToggleButton.Template></ToggleButton><ContentPresenter Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='10,6,28,6' VerticalAlignment='Center' IsHitTestVisible='False'/><TextBlock Text='⌄' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,0,11,0' Foreground='#777B85' IsHitTestVisible='False'/><Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'><Border Background='White' BorderBrush='#D7D9E0' BorderThickness='1' CornerRadius='7' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='320'><ScrollViewer CanContentScroll='True'><ItemsPresenter/></ScrollViewer></Border></Popup></Grid></ControlTemplate></Setter.Value></Setter></Style>
                  <Style TargetType='ComboBoxItem'><Setter Property='Foreground' Value='#202126'/><Setter Property='Padding' Value='10,8'/></Style>
                  <Style TargetType='TextBox'><Setter Property='Padding' Value='9,7'/><Setter Property='BorderBrush' Value='#D7D9E0'/><Setter Property='BorderThickness' Value='1'/><Setter Property='MinHeight' Value='34'/><Setter Property='VerticalContentAlignment' Value='Center'/></Style>
                  <Style TargetType='DataGridCell'><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='8,10'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridCell'><Border Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter VerticalAlignment='Center'/></Border></ControlTemplate></Setter.Value></Setter></Style>
                  <Style TargetType='DataGridColumnHeader'><Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#777B85'/><Setter Property='Padding' Value='10,14'/><Setter Property='BorderThickness' Value='0,0,0,1'/><Setter Property='BorderBrush' Value='#EFF0F4'/></Style>
                </ResourceDictionary>");
                ModernWpf.ThemeManager.SetRequestedTheme(this,ModernWpf.ElementTheme.Light);
                var shell=new WGrid{Background=Background};shell.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});shell.RowDefinitions.Add(new RowDefinition());shell.RowDefinitions.Add(new RowDefinition{Height=new GridLength(78)});Content=shell;
                var top=new DockPanel{Margin=new Thickness(22,10,22,10)};
                var brand=Text("▣  CAD Batch",17,true);brand.Margin=new Thickness(0,0,25,0);brand.VerticalAlignment=VerticalAlignment.Center;top.Children.Add(brand);
                var badge=Text((host.CanPlot?"● AutoCAD подключён":"● Демо · AutoCAD не подключён")+"   "+ProductInfo.Version,12);badge.Foreground=host.CanPlot?Brushes.SeaGreen:Brushes.DarkOrange;refresh.Add(()=>{badge.Text=(host.CanPlot?"● AutoCAD подключён":"● Нет подключения")+"   "+ProductInfo.Version;badge.Foreground=host.CanPlot?Brushes.SeaGreen:Brushes.DarkOrange;});badge.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(badge,System.Windows.Controls.Dock.Right);top.Children.Add(badge);
                menus.Foreground=Ink;menus.Background=Brushes.Transparent;menus.FontSize=13;foreach(var item in c.toolsMenu.Items.OfType<Forms.ToolStripMenuItem>())menus.Items.Add(ConvertMenu(item));top.Children.Add(menus);shell.Children.Add(top);
                body.Margin=new Thickness(24,20,24,12);body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(230)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(300)});WGrid.SetRow(body,1);shell.Children.Add(body);
                BuildSearch();BuildTable();BuildDetails();
                var footer=new WGrid{Background=Brushes.White,Margin=new Thickness(0),};footer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(150)});footer.ColumnDefinitions.Add(new ColumnDefinition());footer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(95)});footer.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(235)});
                activity.Height=5;activity.Margin=new Thickness(24,0,16,0);activity.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(activity);
                state.TextWrapping=TextWrapping.Wrap;state.Foreground=Muted;state.Margin=new Thickness(0,10,12,10);state.VerticalAlignment=VerticalAlignment.Center;WGrid.SetColumn(state,1);footer.Children.Add(state);
                stop.Content="Отмена";stop.Background=Brushes.White;stop.Foreground=Brushes.IndianRed;stop.Margin=new Thickness(0,15,4,15);stop.Click+=(s,e)=>c.running?.Cancel();WGrid.SetColumn(stop,2);footer.Children.Add(stop);
                output.Margin=new Thickness(8,14,24,14);output.Background=Accent;output.Foreground=Brushes.White;output.FontWeight=FontWeights.SemiBold;output.Click+=async(s,e)=>await Execute(()=>c.ExportAsync(false));WGrid.SetColumn(output,3);footer.Children.Add(output);WGrid.SetRow(footer,2);shell.Children.Add(footer);
                timer.Tick+=(s,e)=>Sync();timer.Start();Loaded+=(s,e)=>{CadInteraction.VisibilityChanged=hide=>{if(hide)Hide();else if(!isClosed){Show();Activate();}};c.fluentOwner=new DialogWindowOwner(new System.Windows.Interop.WindowInteropHelper(this).Handle);RestoreView();Sync();};
                Closing+=(s,e)=>{if(c.running!=null){c.running.Cancel();e.Cancel=true;c.status.Text="Отмена задания. Дождитесь завершения операции.";return;}CommitGrid();SaveView();try{c.ReadProfile();ProfileStore.Save(c.profilePath,c.profile);}catch(Exception ex){c.diagnostics.Add(ex.Message);}};
                Closed+=(s,e)=>{isClosed=true;timer.Stop();CadInteraction.VisibilityChanged=null;c.Dispose();};
                SizeChanged+=(s,e)=>{body.ColumnDefinitions[0].Width=new GridLength(ActualWidth<1300?208:230);body.ColumnDefinitions[2].Width=new GridLength(ActualWidth<1300?270:300);};
            }
            public bool CanCloseForUpdate=>c.running==null;
            public void AddApplicationMenu(Action connection,Action updates)
            {
                var application=new MenuItem{Header="Приложение",Foreground=Ink};var connect=new MenuItem{Header="Подключение к AutoCAD…",Foreground=Ink};connect.Click+=(s,e)=>connection();var update=new MenuItem{Header="Проверить обновления…",Foreground=Ink};update.Click+=(s,e)=>updates();application.Items.Add(connect);application.Items.Add(update);menus.Items.Add(application);
            }
            private MenuItem ConvertMenu(Forms.ToolStripMenuItem source)
            {
                var item=new MenuItem{Header=source.Text,IsEnabled=source.Enabled,Foreground=Ink};
                foreach(var child in source.DropDownItems.OfType<Forms.ToolStripMenuItem>())item.Items.Add(ConvertMenu(child));
                if(item.Items.Count==0)item.Click+=(s,e)=>{CommitGrid();source.PerformClick();Sync();};
                refresh.Add(()=>{item.IsEnabled=c.running==null&&source.Enabled;item.IsChecked=source.Checked;});return item;
            }
            private void BuildSearch()
            {
                var pane=new WGrid{Margin=new Thickness(0,0,20,0)};pane.RowDefinitions.Add(new RowDefinition());pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.Children.Add(pane);
                var stack=new StackPanel();findPane.Child=stack;var scroll=new ScrollViewer{Content=findPane,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};pane.Children.Add(scroll);
                stack.Children.Add(Text("Поиск рамок",19,true));stack.Children.Add(Hint("Что и где искать в чертеже",new Thickness(0,2,0,22)));
                stack.Children.Add(Section("Где искать"));stack.Children.Add(Card(Toggles(("Модель",c.model),("Листы",c.layouts),("Все открытые DWG",c.allDocs))));
                stack.Children.Add(Section("Что считать рамкой"));stack.Children.Add(Card(Toggles(("Полилинии",c.polylines),("Вложенные блоки",c.blocks))));
                var filters=new StackPanel();filters.Children.Add(InlineInput("Слой (маска)",c.layer));filters.Children.Add(InlineInput("Имя блока",c.block));filters.Children.Add(InlineInput("Масштабы",c.scales));filters.Children.Add(InlineInput("Допуск, мм",c.tolerance));stack.Children.Add(Card(new Expander{Header="Фильтры и геометрия",Content=filters,Foreground=Ink,IsExpanded=false}));
                stack.Children.Add(Card(Toggles(("По верхней линии штампа",c.stampScale))));
                var parameters=ActionButton("Параметры поиска…",()=>c.EditSearchSources());parameters.Margin=new Thickness(0,3,0,8);parameters.HorizontalAlignment=HorizontalAlignment.Stretch;stack.Children.Add(parameters);
                var scale=ActionButton("Настроить штамп…",()=>c.EditStampScale());scale.Margin=new Thickness(0,0,0,8);stack.Children.Add(scale);
                stack.Children.Add(Hint("Несколько масштабов — через «;».\n100 = 100 единиц DWG / мм бумаги.",new Thickness(1,4,0,12)));
                var commands=new StackPanel{Margin=new Thickness(0,12,0,0)};var find=ActionButton("⌕  Найти рамки",async()=>await Execute(c.ScanAsync));find.Background=Accent;find.Foreground=Brushes.White;find.Height=48;find.Margin=new Thickness(0);commands.Children.Add(find);
                var log=ActionButton("⌁  Диагностика",()=>c.ShowDiagnostics());log.Background=Background;log.Foreground=Accent;log.Margin=new Thickness(0,5,0,0);commands.Children.Add(log);WGrid.SetRow(commands,1);pane.Children.Add(commands);refresh.Add(()=>find.IsEnabled=c.running==null);
            }
            private void BuildTable()
            {
                var pane=new WGrid{Margin=new Thickness(0,0,20,0)};WGrid.SetColumn(pane,1);body.Children.Add(pane);pane.RowDefinitions.Add(new RowDefinition{Height=new GridLength(76)});pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});pane.RowDefinitions.Add(new RowDefinition());pane.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                var title=new StackPanel();title.Children.Add(Text("Рамки",32,true));summary.Foreground=Muted;summary.Margin=new Thickness(0,2,0,0);summary.TextTrimming=TextTrimming.CharacterEllipsis;title.Children.Add(summary);pane.Children.Add(title);
                var tools=new WrapPanel{Margin=new Thickness(0,0,0,13)};tools.Children.Add(ActionButton("Отметить все",()=>{c.SetAll(true);table.Items.Refresh();}));tools.Children.Add(ActionButton("Снять отметки",()=>{c.SetAll(false);table.Items.Refresh();}));tools.Children.Add(ActionButton("⌖ Показать",async()=>await LocateInCadAsync()));tools.Children.Add(ActionButton("Шаблоны…",()=>c.EditProfile()));tools.Children.Add(ActionButton("CSV",()=>c.SaveCsv()));tools.Children.Add(ActionButton("⋯",TableOptions));WGrid.SetRow(tools,1);pane.Children.Add(tools);
                table.AutoGenerateColumns=false;table.CanUserAddRows=false;table.CanUserDeleteRows=false;table.CanUserSortColumns=false;table.CanUserReorderColumns=true;table.HeadersVisibility=DataGridHeadersVisibility.Column;table.SelectionMode=DataGridSelectionMode.Extended;table.SelectionUnit=DataGridSelectionUnit.FullRow;table.RowHeight=48;table.BorderThickness=new Thickness(0);table.Foreground=Ink;table.Background=Brushes.White;
                var rowStyle=new Style(typeof(DataGridRow));rowStyle.Setters.Add(new Setter(Control.ForegroundProperty,Ink));table.RowStyle=rowStyle;table.HorizontalGridLinesBrush=new SolidColorBrush(Color.FromRgb(240,241,244));table.GridLinesVisibility=DataGridGridLinesVisibility.Horizontal;
                table.Columns.Add(new DataGridCheckBoxColumn{Header="",Binding=new Binding("Enabled"){Mode=BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},ElementStyle=(Style)Resources["SheetCheck"],EditingElementStyle=(Style)Resources["SheetCheck"],Width=42});
                Col("№","Page",42);Col("Наименование","Title",215,false);Col("Лист","SheetNumber",60,false);Col("Формат","FormatName",70);Col("Размер, мм","SizeText",108);Col("Масштаб","Scale",78);Col("Угол","AngleDegrees",64);Col("Вкладка","Layout",90);
                Col("Чертёж","File",180,true,false);Col("Слой","Layer",100,true,false);Col("Шифр","Code",130,false,false);Col("Ревизия","Revision",85,false,false);Col("Источник","Kind",140,true,false);Col("Состояние","Status",220,true,false);Col("Копии","Copies",65,false,false);Col("Устройство","Device",160,false,false);Col("CTB / STB","StyleSheet",150,false,false);Col("Масштаб из","ScaleSource",160,true,false);
                view=new ListCollectionView((System.Collections.IList)c.frames);table.ItemsSource=view;
                c.frames.ListChanged+=(s,e)=>{if(view is System.ComponentModel.IEditableCollectionView editing&&!editing.IsEditingItem&&!editing.IsAddingNew)view.Refresh();};table.SelectionChanged+=(s,e)=>{c.fluentSelection=table.SelectedItem as DetectedFrame;foreach(Forms.DataGridViewRow row in c.grid.Rows)row.Selected=table.SelectedItems.Contains(row.DataBoundItem);RenderSelection();};
                table.MouseDoubleClick+=async(s,e)=>{if(table.CurrentColumn?.IsReadOnly==true&&table.SelectedItem!=null)await LocateInCadAsync();};table.CellEditEnding+=(s,e)=>Dispatcher.BeginInvoke(new Action(()=>{c.UpdateCount();RenderSelection();}));
                var border=Card(table);border.Padding=new Thickness(1);border.Margin=new Thickness(0);border.ClipToBounds=true;WGrid.SetRow(border,2);pane.Children.Add(border);
                var hint=Hint("Название и номер листа можно исправить в таблице. Дополнительные инструменты — в меню сверху.",new Thickness(2,9,0,0));WGrid.SetRow(hint,3);pane.Children.Add(hint);refresh.Add(()=>tools.IsEnabled=c.running==null);
            }
            private void BuildDetails()
            {
                var right=new StackPanel();var scroll=new ScrollViewer{Content=right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};WGrid.SetColumn(scroll,2);body.Children.Add(scroll);
                var details=new Expander{Header="▸  Выбранный лист",Content=selected,IsExpanded=false,Foreground=Ink};right.Children.Add(Card(details));RenderSelection();var settings=new StackPanel();settings.Children.Add(Text("Вывод PDF",20,true));modeHint.Foreground=Muted;modeHint.TextWrapping=TextWrapping.Wrap;modeHint.Margin=new Thickness(0,3,0,14);settings.Children.Add(modeHint);
                var browse=new DockPanel();var choose=ActionButton("…",()=>c.ChooseFolder());choose.Margin=new Thickness(6,0,0,0);DockPanel.SetDock(choose,System.Windows.Controls.Dock.Right);browse.Children.Add(choose);browse.Children.Add(Input(c.folder));settings.Children.Add(Field("Папка",browse));settings.Children.Add(Field("Имя файла",Input(c.pattern)));
                var tokens=new WrapPanel{Margin=new Thickness(0,-5,0,8)};foreach(string token in new[]{"Document","Page","Format"}){string value="{"+token+"}";var b=ActionButton(value,()=>c.pattern.Text+=value);b.FontSize=11;b.Padding=new Thickness(7,5,7,5);b.Foreground=Accent;b.Background=new SolidColorBrush(Color.FromRgb(229,241,254));tokens.Children.Add(b);}settings.Children.Add(tokens);refresh.Add(()=>tokens.Visibility=c.profile.PdfMode==PdfOutputMode.OneFile?Visibility.Collapsed:Visibility.Visible);
                settings.Children.Add(Toggles(("Всё в один PDF",c.onePdf)));settings.Children.Add(Field("Плоттер (PC3)",Input(c.device)));
                var colors=new ComboBox{Foreground=Ink,Background=Brushes.White,HorizontalContentAlignment=HorizontalAlignment.Stretch,ItemsSource=c.color.Items.Cast<string>().ToArray(),SelectedIndex=c.color.SelectedIndex,MinHeight=36};colors.SelectionChanged+=(s,e)=>{if(colors.SelectedIndex>=0)c.color.SelectedIndex=colors.SelectedIndex;};refresh.Add(()=>{if(colors.SelectedIndex!=c.color.SelectedIndex)colors.SelectedIndex=c.color.SelectedIndex;});settings.Children.Add(Field("Цвет",colors));
                var styles=Input(c.style);var styleField=Field("CTB / STB",styles);settings.Children.Add(styleField);refresh.Add(()=>styleField.Visibility=c.style.Enabled?Visibility.Visible:Visibility.Collapsed);settings.Children.Add(Toggles(("Вписать в бумагу",c.fit)));
                pdfPane.Child=settings;right.Children.Add(Card(pdfPane));
            }
            private void Col(string text,string property,double width,bool readOnly=true,bool visible=true){table.Columns.Add(new DataGridTextColumn{Header=text,Binding=new Binding(property){Mode=readOnly?BindingMode.OneWay:BindingMode.TwoWay,UpdateSourceTrigger=UpdateSourceTrigger.PropertyChanged},Width=width,IsReadOnly=readOnly,Visibility=visible?Visibility.Visible:Visibility.Collapsed});}
            private void CommitGrid(){table.CommitEdit(DataGridEditingUnit.Cell,true);table.CommitEdit(DataGridEditingUnit.Row,true);}
            private void SaveView(){c.profile.Window=new WindowSettings{Width=WindowState==WindowState.Maximized?RestoreBounds.Width:Width,Height=WindowState==WindowState.Maximized?RestoreBounds.Height:Height,Maximized=WindowState==WindowState.Maximized,Columns=table.Columns.Select(x=>new ColumnSettings{Name=x.Header?.ToString()??"",Visible=x.Visibility==Visibility.Visible,Width=x.ActualWidth,Order=x.DisplayIndex}).ToList()};}
            private void RestoreView(){var settings=c.profile.Window;if(settings==null)return;if(Numeric.Positive(settings.Width)&&Numeric.Positive(settings.Height)){Width=Math.Max(MinWidth,Math.Min(settings.Width,SystemParameters.WorkArea.Width));Height=Math.Max(MinHeight,Math.Min(settings.Height,SystemParameters.WorkArea.Height));}if(settings.Maximized)WindowState=WindowState.Maximized;if(settings.Columns==null)return;foreach(var col in settings.Columns.OrderBy(x=>x.Order)){var target=table.Columns.FirstOrDefault(x=>(x.Header?.ToString()??"")==col.Name);if(target==null)continue;target.Visibility=col.Visible?Visibility.Visible:Visibility.Collapsed;if(Numeric.Positive(col.Width))target.Width=Math.Max(35,Math.Min(col.Width,700));if(col.Order>=0&&col.Order<table.Columns.Count)target.DisplayIndex=col.Order;}}
            private void TableOptions(){var menu=new ContextMenu();var columns=new MenuItem{Header="Столбцы…",Foreground=Ink};columns.Click+=(s,e)=>ChooseColumns();menu.Items.Add(columns);var filter=new MenuItem{Header="Фильтр таблицы…",Foreground=Ink};filter.Click+=(s,e)=>{CommitGrid();var values=c.AskValues("Фильтр таблицы",("Название, номер, шифр, формат или имя DWG",tableFilter));if(values==null)return;tableFilter=values[0].Trim();view!.Filter=obj=>obj is DetectedFrame f&&(tableFilter.Length==0||new[]{f.Title,f.SheetNumber,f.Code,f.FormatName,f.File}.Any(x=>x.IndexOf(tableFilter,StringComparison.CurrentCultureIgnoreCase)>=0));state.Text="Фильтр: "+tableFilter+". Выводятся все отмеченные строки, включая скрытые фильтром.";};menu.Items.Add(filter);menu.IsOpen=true;}
            private void ChooseColumns(){var values=c.AskChecks("Столбцы таблицы",table.Columns.Skip(1).Select(x=>(x.Header?.ToString()??"",x.Visibility==Visibility.Visible)).ToArray());if(values==null)return;for(int i=0;i<values.Length;i++)table.Columns[i+1].Visibility=values[i]?Visibility.Visible:Visibility.Collapsed;}
            private UIElement InlineInput(string caption,Forms.TextBox source){var row=new DockPanel{Margin=new Thickness(0,3,0,3)};var input=Input(source);input.Width=78;input.TextAlignment=TextAlignment.Right;DockPanel.SetDock(input,System.Windows.Controls.Dock.Right);row.Children.Add(input);var label=Text(caption,12);label.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(label);return row;}
            private WText Input(Forms.TextBox source)
            {
                var input=new WText{Text=source.Text,MinWidth=40};System.Windows.Automation.AutomationProperties.SetName(input,source.AccessibleName??"");input.TextChanged+=(s,e)=>{if(source.Text!=input.Text)source.Text=input.Text;};refresh.Add(()=>{if(!input.IsKeyboardFocusWithin&&input.Text!=source.Text)input.Text=source.Text;});return input;
            }
            private UIElement Toggles(params (string Text,Forms.CheckBox Source)[] values)
            {
                var stack=new StackPanel();foreach(var value in values)
                {
                    var row=new WGrid{Margin=new Thickness(0,7,0,7)};row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(46)});
                    var toggle=new WCheck{IsChecked=value.Source.Checked,Style=(Style)Resources["Toggle"],HorizontalAlignment=HorizontalAlignment.Right};WGrid.SetColumn(toggle,1);row.Children.Add(toggle);
                    toggle.Checked+=(s,e)=>value.Source.Checked=true;toggle.Unchecked+=(s,e)=>value.Source.Checked=false;refresh.Add(()=>{if(toggle.IsChecked!=value.Source.Checked)toggle.IsChecked=value.Source.Checked;});
                    var caption=Text(value.Text,12);caption.VerticalAlignment=VerticalAlignment.Center;caption.TextWrapping=TextWrapping.Wrap;caption.Margin=new Thickness(0,0,6,0);row.Children.Add(caption);System.Windows.Automation.AutomationProperties.SetName(toggle,value.Text);stack.Children.Add(row);
                }return stack;
            }
            private static Border Card(UIElement child)=>new Border{Child=child,Background=Brushes.White,BorderBrush=new SolidColorBrush(Color.FromRgb(232,233,238)),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(16),Padding=new Thickness(17),Margin=new Thickness(0,0,0,14)};
            private static StackPanel Field(string caption,UIElement child){var field=new StackPanel{Margin=new Thickness(0,0,0,13)};field.Children.Add(Hint(caption,new Thickness(0,0,0,5)));field.Children.Add(child);return field;}
            private static TextBlock Text(string text,double size=13,bool bold=false)=>new TextBlock{Text=text,FontSize=size,FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,Foreground=Ink};
            private static TextBlock Hint(string text,Thickness margin)=>new TextBlock{Text=text,Foreground=Muted,FontSize=12,Margin=margin,TextWrapping=TextWrapping.Wrap};
            private static TextBlock Section(string text)=>new TextBlock{Text=text,Foreground=Muted,FontSize=12,FontWeight=FontWeights.SemiBold,Margin=new Thickness(1,5,0,9)};
            private WButton ActionButton(string caption,Action action){var button=new WButton{Content=caption,Margin=new Thickness(0,0,6,5)};button.Click+=(s,e)=>{if(c.running!=null)return;try{CommitGrid();action();Sync();}catch(Exception ex){MessageBox.Show(this,ex.Message,"CAD Batch",MessageBoxButton.OK,MessageBoxImage.Warning);}};return button;}
            private async Task LocateInCadAsync(){if(!c.host.CanPlot){MessageBox.Show(this,"Показ рамки доступен после запуска CAD Batch из AutoCAD.","Демонстрация");return;}await Execute(c.LocateAsync);if(c.status.Text.StartsWith("Показана рамка",StringComparison.Ordinal))WindowState=WindowState.Minimized;}
            private async Task Execute(Func<Task> action){CommitGrid();await c.RunAsync(action);Sync();}
            private void Sync()
            {
                foreach(var action in refresh)action();bool busy=c.running!=null;findPane.IsEnabled=pdfPane.IsEnabled=table.IsEnabled=!busy;stop.IsEnabled=busy;output.IsEnabled=!busy&&c.host.CanPlot&&c.frames.Any(f=>f.Enabled);output.Content=c.profile.PdfMode==PdfOutputMode.OneFile?"⇩  Сохранить один PDF":"⇩  Вывести "+c.frames.Count(f=>f.Enabled)+" PDF";
                c.RefreshWorkspaceCount();summary.Text=c.frameSummary.Text;state.Text=c.status.Text;modeHint.Text=c.pdfDescription.Text;activity.Maximum=c.progress.Maximum;activity.Value=c.progress.Value;
                if(table.SelectedItem==null&&c.frames.Count>0)table.SelectedIndex=0;
            }
            private void RenderSelection()
            {
                selected.Children.Clear();var f=table.SelectedItem as DetectedFrame;
                selected.Children.Add(Text(f==null?"Выберите рамку":"Лист "+(f.SheetNumber.Length>0?f.SheetNumber:f.Page.ToString())+" · "+f.FormatName,19,true));
                if(f==null){selected.Children.Add(Hint("После поиска здесь появятся параметры листа.",new Thickness(0,8,0,0)));return;}
                selected.Children.Add(Hint(f.Title.Length>0?f.Title:"Без наименования",new Thickness(0,4,0,14)));
                var canvas=new Canvas{Height=145,Background=new SolidColorBrush(Color.FromRgb(247,248,251)),ClipToBounds=true};double w=f.PaperWidthMm,h=f.PaperHeightMm,factor=Math.Min(220/w,110/h);
                var frame=new System.Windows.Shapes.Rectangle{Width=w*factor,Height=h*factor,Fill=Brushes.White,Stroke=Brushes.DimGray,StrokeThickness=1};Canvas.SetLeft(frame,12);Canvas.SetTop(frame,10);canvas.Children.Add(frame);selected.Children.Add(canvas);selected.Children.Add(Hint("Схема рамки · без содержимого DWG",new Thickness(0,4,0,11)));
                selected.Children.Add(Hint("Размер   "+f.SizeText+" мм\nВкладка   "+f.Layout+"\nМасштаб   "+f.Scale.ToString("0.########")+" · "+f.AngleDegrees+"°\nСлой   "+f.Layer+"\nИсточник   "+f.Kind,new Thickness(0,0,0,14)));
                selected.Children.Add(Hint("Шифр   "+f.Code+"\nРевизия   "+f.Revision+"\n"+string.Join("\n",f.Metadata.Where(k=>k.Key!="Title"&&k.Key!="Code"&&k.Key!="SheetNumber"&&k.Key!="Revision").Take(8).Select(k=>k.Key+": "+k.Value)),new Thickness(0,0,0,10)));
                var actions=new WrapPanel();actions.Children.Add(ActionButton("⌖ Показать",async()=>await LocateInCadAsync()));var pdf=ActionButton("PDF выбранной",async()=>await Execute(()=>c.ExportAsync(true)));pdf.IsEnabled=c.host.CanPlot;actions.Children.Add(pdf);selected.Children.Add(actions);
            }
            public async Task SmokeAsync(string path)
            {
                await Execute(c.ScanAsync);c.VerifyCombinedPdfUiForSmokeTest();Sync();table.SelectedIndex=0;UpdateLayout();await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                if(c.frames.Count!=12||c.ToolMenuCount!=6||table.Columns.Count<16)throw new InvalidOperationException("WPF smoke: missing frames, menus or columns.");
                var visual=(FrameworkElement)Content;var bmp=new RenderTargetBitmap((int)visual.ActualWidth,(int)visual.ActualHeight,96,96,PixelFormats.Pbgra32);bmp.Render(visual);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bmp));using(var file=File.Create(path))encoder.Save(file);
                File.WriteAllText(path+".txt","WPF + ModernWpf smoke PASS: 12 frames, six menus, editable grid, PDF modes/names, selected frame, scoped theme. CAD output disabled in demo.");
            }
        }
    }
}
