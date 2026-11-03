using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using CadBatch.Core;

namespace CadBatch.UI
{
    public sealed class TemplateFormatRow : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
        // The format list redraws its caption, size and note after every edit in the form.
        internal void Changed()=>PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(""));
        public string Name {get;set;}="";
        public double Width {get;set;}
        public double Height {get;set;}
        public string Device {get;set;}="";
        public string Media {get;set;}="";
        public string Style {get;set;}="";
        public string Color {get;set;}="Общие";
        public bool? Fit {get;set;}
        public bool Turn180 {get;set;}
        public double X {get;set;}
        public double Y {get;set;}
        internal static readonly string[] Colors={"Как в общих настройках","Цвета чертежа","Стиль из DWG","Монохромный","Указанный CTB/STB"};
        internal static TemplateFormatRow From(FormatDefinition f){var p=f.Print??new FormatPrintSettings();return new TemplateFormatRow{Name=f.Name,Width=f.WidthMm,Height=f.HeightMm,Device=p.Device,Media=p.Media,Style=p.StyleSheet,Color=p.Color.HasValue?Colors[(int)p.Color.Value+1]:Colors[0],Fit=p.Fit,Turn180=p.UpsideDown,X=p.OffsetX,Y=p.OffsetY};}
        internal FormatDefinition ToFormat()=>new FormatDefinition(Name.Trim(),Width,Height){Print=new FormatPrintSettings{Device=Device.Trim(),Media=Media.Trim(),StyleSheet=Style.Trim(),Color=Array.IndexOf(Colors,Color)>0?(PrintColorMode?)(Array.IndexOf(Colors,Color)-1):null,Fit=Fit,UpsideDown=Turn180,OffsetX=X,OffsetY=Y}};
        internal bool Custom=>Device.Length>0||Media.Length>0||Style.Length>0||Array.IndexOf(Colors,Color)>0||Fit.HasValue||Turn180||X!=0||Y!=0;
        public string Size=>Width.ToString("0.##",CultureInfo.CurrentCulture)+" × "+Height.ToString("0.##",CultureInfo.CurrentCulture)+" мм";
        public string Note=>Custom?"свои настройки печати":"как в общих настройках";
    }
    // Print settings per paper format: a list of formats on the left and a plain form for the chosen one on the right.
    public sealed class PrintSettingsWindow : Window
    {
        public ProjectProfile Profile {get;private set;}
        private readonly IBatchHost host;
        private readonly string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CadBatch","Templates");
        private ObservableCollection<TemplateFormatRow> rows=new ObservableCollection<TemplateFormatRow>();
        private readonly ListBox list=new ListBox();
        private readonly ComboBox names=new ComboBox{IsEditable=true,MinWidth=240};
        private readonly TextBox name=new TextBox(),width=new TextBox(),height=new TextBox(),offsetX=new TextBox(),offsetY=new TextBox();
        private readonly ComboBox device=new ComboBox{IsEditable=true},media=new ComboBox{IsEditable=true},style=new ComboBox{IsEditable=true},color=new ComboBox{ItemsSource=TemplateFormatRow.Colors},fit=new ComboBox{ItemsSource=new[]{"Как в общих настройках","Вписать в лист","Точно 1:1"}};
        private readonly CheckBox turn=new CheckBox{Content="Повернуть содержимое на 180°"};
        private readonly TextBlock status=UiTheme.Hint("");
        private readonly StackPanel form=new StackPanel();
        private TemplateFormatRow? current;private bool loading;
        public PrintSettingsWindow(ProjectProfile profile,IBatchHost host,IntPtr owner)
        {
            Profile=ProfileStore.Clone(profile);this.host=host;double scale=Math.Max(1,ModernDialog.TextScale);
            UiTheme.Apply(this);Title="Печать по форматам";Width=Math.Min(1040*scale,SystemParameters.WorkArea.Width-40);Height=Math.Min(720*scale,SystemParameters.WorkArea.Height-40);MinWidth=Math.Min(820,Width);MinHeight=Math.Min(560,Height);ShowInTaskbar=false;
            if(owner!=IntPtr.Zero){new WindowInteropHelper(this).Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;}else WindowStartupLocation=WindowStartupLocation.CenterScreen;
            var root=new DockPanel{Margin=new Thickness(24,20,24,18)};
            var head=new StackPanel{Margin=new Thickness(0,0,0,14)};head.Children.Add(UiTheme.Title("Печать по форматам",22));
            head.Children.Add(UiTheme.Hint("Здесь можно задать свой принтер, бумагу и стиль для отдельного формата. Пустое поле — значит «как в общих настройках главного окна».",new Thickness(0,4,0,12)));
            var templates=new WrapPanel();var templateLabel=UiTheme.Text("Шаблон:",15,FontWeights.SemiBold);templateLabel.VerticalAlignment=VerticalAlignment.Center;templateLabel.Margin=new Thickness(0,0,10,8);templates.Children.Add(templateLabel);
            names.Margin=new Thickness(0,0,8,8);System.Windows.Automation.AutomationProperties.SetName(names,"Имя шаблона");templates.Children.Add(names);
            foreach(var (text,action) in new (string,Action)[]{("Загрузить",LoadTemplate),("Сохранить как",SaveTemplate),("Удалить",DeleteTemplate)}){var b=UiTheme.Button(text,null,"Outline");b.Margin=new Thickness(0,0,8,8);var run=action;b.Click+=(s,e)=>Guard(run);templates.Children.Add(b);}
            head.Children.Add(templates);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);

            var buttons=new DockPanel{LastChildFill=false,Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            var apply=UiTheme.Button("Применить",null,"Primary");apply.MinWidth=130;apply.IsDefault=true;apply.Margin=new Thickness(8,0,0,0);apply.Click+=(s,e)=>Guard(()=>{Commit();DialogResult=true;});DockPanel.SetDock(apply,Dock.Right);buttons.Children.Add(apply);
            var cancel=UiTheme.Button("Отмена");cancel.MinWidth=110;cancel.IsCancel=true;DockPanel.SetDock(cancel,Dock.Right);buttons.Children.Add(cancel);
            var import=UiTheme.Button("Импорт JSON…",null,"Plain");import.Click+=(s,e)=>Guard(ImportJson);DockPanel.SetDock(import,Dock.Left);buttons.Children.Add(import);
            var export=UiTheme.Button("Экспорт JSON…",null,"Plain");export.Click+=(s,e)=>Guard(ExportJson);DockPanel.SetDock(export,Dock.Left);buttons.Children.Add(export);
            status.VerticalAlignment=VerticalAlignment.Center;status.Margin=new Thickness(12,0,12,0);buttons.Children.Add(status);

            var body=new Grid();body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(300)});body.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});body.ColumnDefinitions.Add(new ColumnDefinition());root.Children.Add(body);
            var left=new DockPanel();var listButtons=new UniformGrid2(new[]{UiTheme.Button("Добавить",UiTheme.IconAdd,"Outline"),UiTheme.Button("Удалить",null,"Outline")});DockPanel.SetDock(listButtons,Dock.Bottom);left.Children.Add(listButtons);
            ((Button)listButtons.Children[0]).Click+=(s,e)=>Guard(AddFormat);((Button)listButtons.Children[1]).Click+=(s,e)=>Guard(RemoveFormat);
            list.ItemTemplate=ItemTemplate();list.BorderThickness=new Thickness(0);if(Resources[typeof(ListBoxItem)] is Style itemStyle)list.ItemContainerStyle=itemStyle;list.Background=UiTheme.Surface;ScrollViewer.SetHorizontalScrollBarVisibility(list,ScrollBarVisibility.Disabled);System.Windows.Automation.AutomationProperties.SetName(list,"Форматы");
            list.SelectionChanged+=(s,e)=>Show(list.SelectedItem as TemplateFormatRow);
            var listCard=UiTheme.Card(list,6,false);listCard.Margin=new Thickness(0,0,0,10);left.Children.Add(listCard);body.Children.Add(left);
            BuildForm();var formScroll=new ScrollViewer{Content=UiTheme.Card(form,20,false),VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(formScroll,2);body.Children.Add(formScroll);
            if(scale>1.001)root.LayoutTransform=new ScaleTransform(scale,scale);
            Content=root;ReloadNames();Fill();
        }
        private sealed class UniformGrid2 : System.Windows.Controls.Primitives.UniformGrid {public UniformGrid2(UIElement[] children){Columns=children.Length;foreach(var c in children){if(c is FrameworkElement f)f.Margin=new Thickness(0,0,8,0);Children.Add(c);}}}
        private static DataTemplate ItemTemplate()
        {
            var stack=new FrameworkElementFactory(typeof(StackPanel));stack.SetValue(MarginProperty,new Thickness(6,7,6,7));
            var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Name"));title.SetValue(TextBlock.FontSizeProperty,15.0);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);stack.AppendChild(title);
            var size=new FrameworkElementFactory(typeof(TextBlock));size.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Size"));size.SetValue(TextBlock.FontSizeProperty,13.0);size.SetValue(TextBlock.ForegroundProperty,UiTheme.Secondary);stack.AppendChild(size);
            var note=new FrameworkElementFactory(typeof(TextBlock));note.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Note"));note.SetValue(TextBlock.FontSizeProperty,12.5);note.SetValue(TextBlock.ForegroundProperty,UiTheme.Accent);stack.AppendChild(note);
            return new DataTemplate{VisualTree=stack};
        }
        private void BuildForm()
        {
            var size=new Grid();for(int i=0;i<5;i++)size.ColumnDefinitions.Add(new ColumnDefinition{Width=i%2==1?new GridLength(12):new GridLength(1,GridUnitType.Star)});
            Place(size,Field("Название",name),0);Place(size,Field("Ширина, мм",width),2);Place(size,Field("Высота, мм",height),4);form.Children.Add(size);
            var load=UiTheme.Button("Взять список из AutoCAD",UiTheme.IconRefresh,"Outline");load.HorizontalAlignment=HorizontalAlignment.Left;load.Margin=new Thickness(0,0,0,14);load.IsEnabled=host.CanPlot;load.ToolTip=host.CanPlot?"Загрузить принтеры, бумагу и стили из AutoCAD":"Доступно после подключения к AutoCAD";load.Click+=async(s,e)=>await LoadDevicesAsync();
            form.Children.Add(Field("Принтер или плоттер (PC3)",device));form.Children.Add(load);form.Children.Add(Field("Бумага",media));form.Children.Add(Field("Стиль печати CTB / STB",style));
            var two=new Grid();two.ColumnDefinitions.Add(new ColumnDefinition());two.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});two.ColumnDefinitions.Add(new ColumnDefinition());
            Place(two,Field("Цвет",color),0);Place(two,Field("Масштаб",fit),2);form.Children.Add(two);
            turn.Margin=new Thickness(0,0,0,16);form.Children.Add(turn);
            var offsets=new Grid();offsets.ColumnDefinitions.Add(new ColumnDefinition());offsets.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});offsets.ColumnDefinitions.Add(new ColumnDefinition());
            Place(offsets,Field("Смещение по X, мм",offsetX),0);Place(offsets,Field("Смещение по Y, мм",offsetY),2);form.Children.Add(offsets);
            form.Children.Add(UiTheme.Hint("Для длинных и кратных форматов оставьте «Бумагу» пустой — CAD Batch подберёт её сам. Чтобы уменьшить большой формат на малую бумагу, выберите «Вписать в лист».",new Thickness(2,4,0,0)));
            foreach(var box in new[]{name,width,height,offsetX,offsetY})box.TextChanged+=(s,e)=>Save();
            foreach(var combo in new[]{device,media,style})combo.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler((s,e)=>Save()));
            device.SelectionChanged+=async(s,e)=>{Save();if(device.SelectedItem is string d&&host.CanPlot)await LoadCapabilitiesAsync(d);};
            color.SelectionChanged+=(s,e)=>Save();fit.SelectionChanged+=(s,e)=>Save();turn.Click+=(s,e)=>Save();
        }
        private static void Place(Grid grid,UIElement element,int column){Grid.SetColumn(element,column);grid.Children.Add(element);}
        private static StackPanel Field(string caption,Control control)
        {
            var panel=new StackPanel{Margin=new Thickness(0,0,0,14)};var label=UiTheme.Hint(caption,new Thickness(2,0,0,6));label.FontWeight=FontWeights.SemiBold;label.FontSize=14;panel.Children.Add(label);panel.Children.Add(control);
            System.Windows.Automation.AutomationProperties.SetName(control,caption);return panel;
        }
        private void Show(TemplateFormatRow? row)
        {
            current=row;form.IsEnabled=row!=null;if(row==null)return;loading=true;
            try
            {
                name.Text=row.Name;width.Text=row.Width.ToString(CultureInfo.CurrentCulture);height.Text=row.Height.ToString(CultureInfo.CurrentCulture);offsetX.Text=row.X.ToString(CultureInfo.CurrentCulture);offsetY.Text=row.Y.ToString(CultureInfo.CurrentCulture);
                device.Text=row.Device;media.Text=row.Media;style.Text=row.Style;color.SelectedIndex=Math.Max(0,Array.IndexOf(TemplateFormatRow.Colors,row.Color));fit.SelectedIndex=row.Fit==null?0:row.Fit==true?1:2;turn.IsChecked=row.Turn180;
                foreach(var box in new[]{width,height,offsetX,offsetY})box.ClearValue(BorderBrushProperty);
            }
            finally{loading=false;}
        }
        // Every edit is written to the row at once; numbers that do not parse are marked and kept until corrected.
        private void Save()
        {
            if(loading||current==null)return;var row=current;
            row.Name=name.Text;row.Device=device.Text??"";row.Media=media.Text??"";row.Style=style.Text??"";row.Color=color.SelectedItem as string??TemplateFormatRow.Colors[0];row.Fit=fit.SelectedIndex<=0?(bool?)null:fit.SelectedIndex==1;row.Turn180=turn.IsChecked==true;
            if(Number(width,out double w))row.Width=w;if(Number(height,out double h))row.Height=h;if(Number(offsetX,out double x))row.X=x;if(Number(offsetY,out double y))row.Y=y;
            row.Changed();
        }
        private static bool Number(TextBox box,out double value)
        {
            bool ok=double.TryParse(box.Text.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value);
            if(ok)box.ClearValue(BorderBrushProperty);else box.BorderBrush=UiTheme.Danger;return ok;
        }
        private void Fill(){rows=new ObservableCollection<TemplateFormatRow>(Profile.Formats.Select(TemplateFormatRow.From));list.ItemsSource=rows;list.SelectedIndex=rows.Count>0?0:-1;Show(list.SelectedItem as TemplateFormatRow);}
        private void Commit()
        {
            var formats=rows.Select(r=>r.ToFormat()).ToList();if(formats.Any(f=>f.Name.Length==0))throw new ArgumentException("У каждого формата должно быть название.");
            var old=Profile.Formats;Profile.Formats=formats;try{Profile.Validate();}catch{Profile.Formats=old;throw;}
        }
        private void AddFormat(){var row=new TemplateFormatRow{Name="Свой формат",Width=420,Height=297};rows.Add(row);list.SelectedItem=row;list.ScrollIntoView(row);name.Focus();name.SelectAll();}
        private void RemoveFormat()
        {
            if(current==null)return;if(rows.Count<=1)throw new ArgumentException("Должен остаться хотя бы один формат.");
            if(!ModernDialog.Confirm(Handle,"Удалить формат?","Формат «"+current.Name+"» будет удалён из этого набора. Рамки этого размера перестанут распознаваться.","Удалить",DialogKind.Warning))return;
            int index=rows.IndexOf(current);rows.Remove(current);list.SelectedIndex=Math.Min(index,rows.Count-1);
        }
        private IntPtr Handle=>new WindowInteropHelper(this).Handle;
        private void Guard(Action action){try{action();}catch(Exception ex){ModernDialog.Message(Handle,"Проверьте настройки",ex.Message,DialogKind.Warning);}}
        private void ReloadNames(){string selected=names.Text;names.ItemsSource=Directory.Exists(directory)?Directory.GetFiles(directory,"*.json").Select(Path.GetFileNameWithoutExtension).OrderBy(x=>x,StringComparer.CurrentCultureIgnoreCase).ToArray():new string[0];names.Text=selected;}
        private void LoadTemplate()
        {
            if(string.IsNullOrWhiteSpace(names.Text))throw new ArgumentException("Выберите шаблон в списке.");string path=PrintTemplates.PathFor(directory,names.Text.Trim());
            if(!File.Exists(path))throw new ArgumentException("Шаблон «"+names.Text+"» не найден.");Profile=ProfileStore.Load(path);Fill();status.Text="Загружен шаблон «"+names.Text.Trim()+"».";
        }
        private void SaveTemplate()
        {
            if(string.IsNullOrWhiteSpace(names.Text))throw new ArgumentException("Введите имя шаблона в поле «Шаблон».");Commit();string path=PrintTemplates.PathFor(directory,names.Text.Trim());
            if(File.Exists(path)&&!ModernDialog.Confirm(Handle,"Заменить шаблон?","Шаблон «"+names.Text.Trim()+"» уже есть. Заменить его текущими настройками?","Заменить"))return;
            ProfileStore.Save(path,Profile);ReloadNames();status.Text="Шаблон «"+names.Text.Trim()+"» сохранён.";
        }
        private void DeleteTemplate()
        {
            if(string.IsNullOrWhiteSpace(names.Text))throw new ArgumentException("Выберите шаблон в списке.");string path=PrintTemplates.PathFor(directory,names.Text.Trim());
            if(!File.Exists(path))throw new ArgumentException("Шаблон «"+names.Text+"» не найден.");
            if(!ModernDialog.Confirm(Handle,"Удалить шаблон?","Шаблон «"+names.Text.Trim()+"» будет удалён без возможности восстановления.","Удалить",DialogKind.Warning))return;
            File.Delete(path);names.Text="";ReloadNames();status.Text="Шаблон удалён.";
        }
        private void ImportJson()
        {
            var dialog=new Microsoft.Win32.OpenFileDialog{Filter="Шаблон CAD Batch|*.json"};if(dialog.ShowDialog(this)!=true)return;Profile=ProfileStore.Load(dialog.FileName);Fill();status.Text="Импортировано: "+Path.GetFileName(dialog.FileName);
        }
        private void ExportJson()
        {
            Commit();var dialog=new Microsoft.Win32.SaveFileDialog{Filter="Шаблон CAD Batch|*.json",FileName="CadBatch-profile.json"};if(dialog.ShowDialog(this)!=true)return;ProfileStore.Save(dialog.FileName,Profile);status.Text="Сохранено: "+Path.GetFileName(dialog.FileName);
        }
        private async System.Threading.Tasks.Task LoadDevicesAsync()
        {
            try{status.Text="Получаю список принтеров из AutoCAD…";var devices=await host.GetPlotDevicesAsync();string keep=device.Text;device.ItemsSource=devices;device.Text=keep;status.Text="Принтеров: "+devices.Length+". Выберите нужный.";device.IsDropDownOpen=true;}
            catch(Exception ex){status.Text=ex.Message;}
        }
        private async System.Threading.Tasks.Task LoadCapabilitiesAsync(string selected)
        {
            try
            {
                status.Text="Читаю бумагу и стили «"+selected+"»…";var caps=await host.GetPlotCapabilitiesAsync(selected);if(device.Text!=selected)return;
                string m=media.Text,st=style.Text;media.ItemsSource=caps.Media;style.ItemsSource=caps.Styles;media.Text=m;style.Text=st;status.Text="Бумага: "+caps.Media.Count()+", стилей: "+caps.Styles.Count()+".";
            }
            catch(Exception ex){status.Text=ex.Message;}
        }
    }
}
