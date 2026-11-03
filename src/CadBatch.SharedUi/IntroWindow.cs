using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace CadBatch.UI
{
    // First start: three short screens that show the whole path once — find, check, save.
    public sealed class IntroWindow : Window
    {
        private static readonly (string Title,string Text)[] Pages=
        {
            ("Найдите рамки","Откройте чертёж в AutoCAD. Слева выберите, где искать, и нажмите большую синюю кнопку «Найти рамки». CAD Batch сам найдёт все листы."),
            ("Проверьте листы","В середине появится список листов. Галочка означает, что лист попадёт в PDF. Щёлкните по строке — справа можно исправить название и номер."),
            ("Сохраните PDF","Справа выберите папку и нажмите синюю кнопку внизу. Сначала вы увидите план, а когда всё будет готово — кнопку «Открыть папку»."),
        };
        private int page;
        private readonly Border picture=new Border{Height=170,CornerRadius=new CornerRadius(14),Background=UiTheme.Canvas,Margin=new Thickness(0,0,0,22)};
        private readonly TextBlock title=UiTheme.Title("",24),text=UiTheme.Text("",16,null,UiTheme.Secondary);
        private readonly StackPanel dots=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Center,Margin=new Thickness(0,18,0,0)};
        private readonly Button back=UiTheme.Button("Назад",null,"Outline"),next=UiTheme.Button("Далее",null,"Primary");
        public IntroWindow(IntPtr owner,double scale)
        {
            UiTheme.Apply(this,false);Title="Знакомство с CAD Batch";Width=560*Math.Max(1,scale)+32;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            if(owner!=IntPtr.Zero){new WindowInteropHelper(this).Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;}else WindowStartupLocation=WindowStartupLocation.CenterScreen;
            var root=new StackPanel{Margin=new Thickness(30,28,30,24)};
            var welcome=UiTheme.Hint("Добро пожаловать в CAD Batch · три шага",new Thickness(0,0,0,14));welcome.FontSize=14;welcome.FontWeight=FontWeights.SemiBold;root.Children.Add(welcome);
            root.Children.Add(picture);title.Margin=new Thickness(0,0,0,8);root.Children.Add(title);text.LineHeight=24;root.Children.Add(text);root.Children.Add(dots);
            var buttons=new DockPanel{Margin=new Thickness(0,22,0,0),LastChildFill=false};
            var skip=UiTheme.Button("Пропустить",null,"Plain");skip.Click+=(s,e)=>Close();DockPanel.SetDock(skip,Dock.Left);buttons.Children.Add(skip);
            next.MinWidth=150;next.IsDefault=true;next.Margin=new Thickness(8,0,0,0);next.Click+=(s,e)=>{if(page==Pages.Length-1)Close();else Show(page+1);};DockPanel.SetDock(next,Dock.Right);buttons.Children.Add(next);
            back.MinWidth=110;back.Click+=(s,e)=>Show(page-1);DockPanel.SetDock(back,Dock.Right);buttons.Children.Add(back);
            root.Children.Add(buttons);
            if(scale>1.001)root.LayoutTransform=new ScaleTransform(scale,scale);
            var surface=new Border{Child=root,Background=UiTheme.Surface,CornerRadius=new CornerRadius(18),BorderBrush=UiTheme.Line,BorderThickness=new Thickness(1),Margin=new Thickness(16),Effect=new DropShadowEffect{BlurRadius=30,ShadowDepth=6,Direction=270,Opacity=0.24}};
            surface.MouseLeftButtonDown+=(s,e)=>{try{DragMove();}catch(InvalidOperationException){}};
            Content=surface;PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape)Close();else if(e.Key==Key.Right&&page<Pages.Length-1)Show(page+1);else if(e.Key==Key.Left&&page>0)Show(page-1);};
            Show(0);
        }
        private void Show(int index)
        {
            page=Math.Max(0,Math.Min(Pages.Length-1,index));title.Text=(page+1)+". "+Pages[page].Title;text.Text=Pages[page].Text;picture.Child=Picture(page);
            back.Visibility=page==0?Visibility.Hidden:Visibility.Visible;next.Content=page==Pages.Length-1?"Начать работу":"Далее";
            dots.Children.Clear();for(int i=0;i<Pages.Length;i++)dots.Children.Add(new Ellipse{Width=i==page?10:8,Height=i==page?10:8,Margin=new Thickness(5,0,5,0),Fill=i==page?UiTheme.Accent:UiTheme.Line,VerticalAlignment=VerticalAlignment.Center});
        }
        // Simple drawings in the app's own visual language: no screenshots that age with every release.
        private static UIElement Picture(int index)
        {
            var canvas=new Canvas{Width=300,Height=150,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};
            if(index==0)
            {
                for(int i=0;i<3;i++)Add(canvas,Sheet(64,46),20+i*72,20+(i%2)*14);
                Add(canvas,Badge(UiTheme.IconSearch,54),220,62);
                Add(canvas,new Rectangle{Width=210,Height=36,RadiusX=10,RadiusY=10,Fill=UiTheme.Accent},20,104);
                var label=new TextBlock{Text="Найти рамки",Foreground=UiTheme.OnAccent,FontWeight=FontWeights.SemiBold,FontSize=15};Add(canvas,label,82,112);
            }
            else if(index==1)
            {
                for(int i=0;i<4;i++)
                {
                    Add(canvas,new Rectangle{Width=260,Height=28,RadiusX=6,RadiusY=6,Fill=UiTheme.Surface,Stroke=UiTheme.Line},20,12+i*34);
                    var on=i!=2;Add(canvas,new Ellipse{Width=18,Height=18,Fill=on?UiTheme.Accent:UiTheme.Surface,Stroke=on?UiTheme.Accent:UiTheme.Tertiary,StrokeThickness=1.5},30,17+i*34);
                    if(on)Add(canvas,new TextBlock{Text=UiTheme.IconCheck,FontFamily=UiTheme.Icons,FontSize=10,Foreground=UiTheme.OnAccent},34,21+i*34);
                    Add(canvas,new Rectangle{Width=120-(i*13)%40,Height=8,RadiusX=4,RadiusY=4,Fill=UiTheme.Tertiary,Opacity=0.5},62,22+i*34);
                    Add(canvas,new Rectangle{Width=30,Height=14,RadiusX=4,RadiusY=4,Fill=UiTheme.Fill},236,19+i*34);
                }
            }
            else
            {
                Add(canvas,Sheet(84,110),48,20);
                Add(canvas,new TextBlock{Text="PDF",FontSize=20,FontWeight=FontWeights.Bold,Foreground=UiTheme.Danger},70,62);
                Add(canvas,Badge(UiTheme.IconDownload,54),160,36);
                Add(canvas,Badge(UiTheme.IconFolder,54),230,36);
                Add(canvas,new TextBlock{Text="Готово!",FontSize=15,FontWeight=FontWeights.SemiBold,Foreground=UiTheme.Success},178,104);
            }
            return canvas;
        }
        private static Border Sheet(double w,double h)=>new Border{Width=w,Height=h,Background=UiTheme.Surface,BorderBrush=UiTheme.Tertiary,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(3),
            Child=new Border{Width=w*0.45,Height=h*0.22,BorderBrush=UiTheme.Accent,BorderThickness=new Thickness(1),Background=UiTheme.AccentSoft,HorizontalAlignment=HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(3)}};
        private static Border Badge(string glyph,double size)=>new Border{Width=size,Height=size,CornerRadius=new CornerRadius(size/2),Background=UiTheme.AccentSoft,
            Child=new TextBlock{Text=glyph,FontFamily=UiTheme.Icons,FontSize=size*0.42,Foreground=UiTheme.Accent,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        private static void Add(Canvas canvas,UIElement element,double left,double top){Canvas.SetLeft(element,left);Canvas.SetTop(element,top);canvas.Children.Add(element);}
    }
}
