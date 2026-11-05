using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using CadBatch.Core;

namespace CadBatch.UI
{
    // Where CAD Batch reads the title, code and sheet number inside the 185 × 55 mm title block.
    // The zones are drawn on a picture of the stamp, so a coordinate typo is visible at once.
    public sealed class StampZonesWindow : Window
    {
        private const double StampWidth=185,StampHeight=55,K=3;
        private static readonly (string Key,string Name)[] Known={("Title","Наименование листа"),("Object","Объект (здание)"),("Code","Шифр"),("Stage","Стадия"),("SheetNumber","Номер листа"),("SheetCount","Всего листов"),("Revision","Ревизия")};
        public StampMetadataSettings Settings {get;private set;}
        private readonly ObservableCollection<Zone> zones=new ObservableCollection<Zone>();
        private readonly ListBox list=new ListBox();
        private readonly Canvas picture=new Canvas{Width=StampWidth*K,Height=StampHeight*K,ClipToBounds=true};
        private readonly ComboBox field=new ComboBox{IsEditable=true,ItemsSource=Known.Select(k=>k.Name).ToArray()};
        private readonly TextBox x=new TextBox(),y=new TextBox(),w=new TextBox(),h=new TextBox();
        private readonly CheckBox enabled=new CheckBox{Content="Читать текст и таблицы из штампа"};
        private readonly StackPanel form=new StackPanel();
        private Zone? current;private bool loading;
        private sealed class Zone : System.ComponentModel.INotifyPropertyChanged
        {
            public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
            public string Field {get;set;}="Title";public double X {get;set;}public double Y {get;set;}public double Width {get;set;}=20;public double Height {get;set;}=10;
            public string Caption=>FieldName(Field);
            public string Place=>"X "+X.ToString("0.#",CultureInfo.CurrentCulture)+" · Y "+Y.ToString("0.#",CultureInfo.CurrentCulture)+" · "+Width.ToString("0.#",CultureInfo.CurrentCulture)+" × "+Height.ToString("0.#",CultureInfo.CurrentCulture)+" мм";
            public void Changed()=>PropertyChanged?.Invoke(this,new System.ComponentModel.PropertyChangedEventArgs(""));
        }
        private static string FieldName(string key)=>Known.Where(k=>k.Key==key).Select(k=>k.Name).FirstOrDefault()??key;
        private static string Key(string name){name=(name??"").Trim();return Known.Where(k=>string.Equals(k.Name,name,StringComparison.CurrentCultureIgnoreCase)).Select(k=>k.Key).FirstOrDefault()??name;}
        public StampZonesWindow(StampMetadataSettings? settings,IntPtr owner)
        {
            var source=settings??new StampMetadataSettings();Settings=source;double scale=Math.Max(1,ModernDialog.TextScale);
            UiTheme.Apply(this);Title="Поля штампа";Width=Math.Min(1000*scale,SystemParameters.WorkArea.Width-40);Height=Math.Min(700*scale,SystemParameters.WorkArea.Height-40);MinWidth=Math.Min(800,Width);MinHeight=Math.Min(560,Height);ShowInTaskbar=false;
            if(owner!=IntPtr.Zero){new WindowInteropHelper(this).Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;}else WindowStartupLocation=WindowStartupLocation.CenterScreen;
            var root=new DockPanel{Margin=new Thickness(24,20,24,18)};
            var head=new StackPanel{Margin=new Thickness(0,0,0,14)};head.Children.Add(UiTheme.Title("Поля штампа",22));
            head.Children.Add(UiTheme.Hint("CAD Batch читает наименование, шифр и номер листа из этих мест штампа 185 × 55 мм. Координаты — в миллиметрах от левого нижнего угла штампа. Атрибуты блоков всегда важнее текста.",new Thickness(0,4,0,12)));
            var presets=new WrapPanel();enabled.IsChecked=source.Enabled;enabled.Margin=new Thickness(0,8,24,8);presets.Children.Add(enabled);
            var spds=UiTheme.Button("Взять зоны СПДС",null,"Outline");spds.Margin=new Thickness(0,0,8,8);spds.Click+=(s,e)=>Load(StampMetadataSettings.SpdsZones());presets.Children.Add(spds);
            var eskd=UiTheme.Button("Взять зоны ЕСКД",null,"Outline");eskd.Margin=new Thickness(0,0,8,8);eskd.Click+=(s,e)=>Load(StampMetadataSettings.EskdZones());presets.Children.Add(eskd);
            head.Children.Add(presets);DockPanel.SetDock(head,Dock.Top);root.Children.Add(head);
            var buttons=new DockPanel{LastChildFill=false,Margin=new Thickness(0,14,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);root.Children.Add(buttons);
            var apply=UiTheme.Button("Применить",null,"Primary");apply.MinWidth=130;apply.IsDefault=true;apply.Margin=new Thickness(8,0,0,0);apply.Click+=(s,e)=>ApplyZones();DockPanel.SetDock(apply,Dock.Right);buttons.Children.Add(apply);
            var cancel=UiTheme.Button("Отмена");cancel.MinWidth=110;cancel.IsCancel=true;DockPanel.SetDock(cancel,Dock.Right);buttons.Children.Add(cancel);

            var body=new Grid();body.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});body.RowDefinitions.Add(new RowDefinition());root.Children.Add(body);
            var stamp=new Border{Child=new Viewbox{Child=picture,Stretch=Stretch.Uniform,MaxHeight=StampHeight*K},Background=UiTheme.Canvas,CornerRadius=new CornerRadius(12),Padding=new Thickness(16),Margin=new Thickness(0,0,0,14)};body.Children.Add(stamp);
            var lower=new Grid();lower.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(320)});lower.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(18)});lower.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetRow(lower,1);body.Children.Add(lower);
            var left=new DockPanel();var add=UiTheme.Button("Добавить",UiTheme.IconAdd,"Outline");var remove=UiTheme.Button("Удалить",null,"Outline");
            var row=new UniformGrid{Columns=2};add.Margin=new Thickness(0,0,8,0);row.Children.Add(add);row.Children.Add(remove);DockPanel.SetDock(row,Dock.Bottom);left.Children.Add(row);
            add.Click+=(s,e)=>{var zone=new Zone{Field="Свое поле",X=5,Y=5};zones.Add(zone);list.SelectedItem=zone;Draw();field.Focus();};
            remove.Click+=(s,e)=>{if(current==null)return;int index=zones.IndexOf(current);zones.Remove(current);list.SelectedIndex=Math.Min(index,zones.Count-1);Draw();};
            list.ItemsSource=zones;list.ItemTemplate=ItemTemplate();list.BorderThickness=new Thickness(0);if(Resources[typeof(ListBoxItem)] is Style itemStyle)list.ItemContainerStyle=itemStyle;list.Background=UiTheme.Surface;System.Windows.Automation.AutomationProperties.SetName(list,"Поля штампа");
            list.SelectionChanged+=(s,e)=>{Show(list.SelectedItem as Zone);Draw();};
            var card=UiTheme.Card(list,6,false);card.Margin=new Thickness(0,0,0,10);left.Children.Add(card);lower.Children.Add(left);
            BuildForm();var formCard=UiTheme.Card(form,20,false);formCard.Margin=new Thickness(0);var scroll=new ScrollViewer{Content=formCard,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetColumn(scroll,2);lower.Children.Add(scroll);
            if(scale>1.001)root.LayoutTransform=new ScaleTransform(scale,scale);
            Content=root;Load(source.Zones);
        }
        private static DataTemplate ItemTemplate()
        {
            var stack=new FrameworkElementFactory(typeof(StackPanel));stack.SetValue(MarginProperty,new Thickness(6,6,6,6));
            var title=new FrameworkElementFactory(typeof(TextBlock));title.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Caption"));title.SetValue(TextBlock.FontSizeProperty,15.0);title.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);stack.AppendChild(title);
            var place=new FrameworkElementFactory(typeof(TextBlock));place.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding("Place"));place.SetValue(TextBlock.FontSizeProperty,13.0);place.SetValue(TextBlock.ForegroundProperty,UiTheme.Secondary);stack.AppendChild(place);
            return new DataTemplate{VisualTree=stack};
        }
        private void BuildForm()
        {
            form.Children.Add(Field("Что читать",field));
            form.Children.Add(UiTheme.Hint("Выберите из списка или впишите своё имя поля — оно появится в ведомости и в шаблоне имени файла.",new Thickness(2,-6,0,14)));
            var grid=new Grid();for(int i=0;i<7;i++)grid.ColumnDefinitions.Add(new ColumnDefinition{Width=i%2==1?new GridLength(10):new GridLength(1,GridUnitType.Star)});
            Place(grid,Field("X, мм",x),0);Place(grid,Field("Y, мм",y),2);Place(grid,Field("Ширина",w),4);Place(grid,Field("Высота",h),6);form.Children.Add(grid);
            form.Children.Add(UiTheme.Hint("Изменения сразу видны на схеме штампа выше. Зону можно выбрать и щелчком по схеме.",new Thickness(2,0,0,0)));
            foreach(var box in new[]{x,y,w,h})box.TextChanged+=(s,e)=>Save();field.AddHandler(TextBoxBase.TextChangedEvent,new TextChangedEventHandler((s,e)=>Save()));field.SelectionChanged+=(s,e)=>Dispatcher.BeginInvoke(new Action(Save));
        }
        private static void Place(Grid grid,UIElement element,int column){Grid.SetColumn(element,column);grid.Children.Add(element);}
        private static StackPanel Field(string caption,Control control)
        {
            var panel=new StackPanel{Margin=new Thickness(0,0,0,14)};var label=UiTheme.Hint(caption,new Thickness(2,0,0,6));label.FontWeight=FontWeights.SemiBold;label.FontSize=14;panel.Children.Add(label);panel.Children.Add(control);
            System.Windows.Automation.AutomationProperties.SetName(control,caption);return panel;
        }
        private void Load(IEnumerable<StampFieldZone> source)
        {
            zones.Clear();foreach(var z in source)zones.Add(new Zone{Field=z.Field,X=z.X,Y=z.Y,Width=z.Width,Height=z.Height});list.SelectedIndex=zones.Count>0?0:-1;Draw();
        }
        private void Show(Zone? zone)
        {
            current=zone;form.IsEnabled=zone!=null;if(zone==null)return;loading=true;
            try{field.Text=FieldName(zone.Field);x.Text=Format(zone.X);y.Text=Format(zone.Y);w.Text=Format(zone.Width);h.Text=Format(zone.Height);foreach(var box in new[]{x,y,w,h})box.ClearValue(BorderBrushProperty);}finally{loading=false;}
        }
        private static string Format(double value)=>value.ToString("0.##",CultureInfo.CurrentCulture);
        private void Save()
        {
            if(loading||current==null)return;var zone=current;zone.Field=Key(field.Text);
            if(Number(x,out double vx,false))zone.X=vx;if(Number(y,out double vy,false))zone.Y=vy;if(Number(w,out double vw,true))zone.Width=vw;if(Number(h,out double vh,true))zone.Height=vh;
            zone.Changed();Draw();
        }
        private static bool Number(TextBox box,out double value,bool positive)
        {
            bool ok=double.TryParse(box.Text.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out value)&&!double.IsNaN(value)&&!double.IsInfinity(value)&&(positive?value>0:value>=0);
            if(ok)box.ClearValue(BorderBrushProperty);else box.BorderBrush=UiTheme.Danger;return ok;
        }
        // The stamp outline with the main ESKD/SPDS grid lines and every zone; the selected zone is filled.
        private void Draw()
        {
            picture.Children.Clear();
            picture.Children.Add(new Rectangle{Width=StampWidth*K,Height=StampHeight*K,Stroke=UiTheme.Ink,StrokeThickness=2,Fill=UiTheme.Surface});
            foreach(double gx in new double[]{7,17,40,55,65,135,150,165})GridLine(gx,0,gx,gx>=135?40:StampHeight);foreach(double gy in new double[]{15,40})GridLine(65,gy,StampWidth,gy);
            foreach(var zone in zones)
            {
                bool selected=zone==current;var rect=new Rectangle{Width=Math.Max(1,zone.Width*K),Height=Math.Max(1,zone.Height*K),Stroke=selected?UiTheme.Accent:UiTheme.Warning,StrokeThickness=selected?3:2,Fill=selected?UiTheme.AccentSoft:Brushes.Transparent,Opacity=selected?0.95:0.85,Cursor=Cursors.Hand};
                Canvas.SetLeft(rect,zone.X*K);Canvas.SetTop(rect,(StampHeight-zone.Y-zone.Height)*K);var target=zone;rect.MouseLeftButtonDown+=(s,e)=>{list.SelectedItem=target;e.Handled=true;};picture.Children.Add(rect);
                var label=new TextBlock{Text=zone.Caption,FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=selected?UiTheme.Accent:UiTheme.Warning,IsHitTestVisible=false,TextTrimming=TextTrimming.CharacterEllipsis,MaxWidth=Math.Max(10,zone.Width*K-6)};
                Canvas.SetLeft(label,zone.X*K+4);Canvas.SetTop(label,(StampHeight-zone.Y-zone.Height)*K+2);picture.Children.Add(label);
            }
        }
        private void GridLine(double x1,double y1,double x2,double y2){picture.Children.Add(new Line{X1=x1*K,Y1=(StampHeight-y1)*K,X2=x2*K,Y2=(StampHeight-y2)*K,Stroke=UiTheme.Tertiary,StrokeThickness=1,Opacity=0.6});}
        private void ApplyZones()
        {
            try
            {
                var result=new StampMetadataSettings{Enabled=enabled.IsChecked==true,Form="Пользовательские зоны",Zones=zones.Select(z=>new StampFieldZone(z.Field,z.X,z.Y,z.Width,z.Height)).ToList()};
                if(result.Zones.Any(z=>string.IsNullOrWhiteSpace(z.Field)))throw new ArgumentException("У каждой зоны должно быть название поля.");
                result.Validate();Settings=result;DialogResult=true;
            }
            catch(Exception ex){ModernDialog.Message(new WindowInteropHelper(this).Handle,"Проверьте зоны",ex.Message,DialogKind.Warning);}
        }
        public void RenderPreview(string path){UpdateLayout();UiTheme.Snapshot((FrameworkElement)Content,path);}
    }
}
