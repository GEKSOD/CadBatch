using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CadBatch.UI
{
    public enum DialogKind {Info,Success,Warning,Error,Question}

    // Sheet-style dialogs shared by the desktop application and the AutoCAD component.
    // Every dialog has one clear primary action; Enter confirms and Esc cancels.
    public static class ModernDialog
    {
        public static string[]? Values(IntPtr owner,string title,string? lead,params (string Label,string Value)[] fields)
        {
            var boxes=new List<TextBox>();var stack=new StackPanel();
            foreach(var field in fields)
            {
                stack.Children.Add(UiTheme.Hint(field.Label,new Thickness(2,0,0,6)));
                var box=new TextBox{Text=field.Value,Margin=new Thickness(0,0,0,14)};boxes.Add(box);stack.Children.Add(box);
            }
            var sheet=new Sheet(owner,title,lead,DialogKind.Question,stack,"Продолжить","Отмена",440);
            sheet.Loaded+=(s,e)=>{if(boxes.Count>0){boxes[0].Focus();boxes[0].SelectAll();}};
            return sheet.ShowDialog()==true?boxes.Select(b=>b.Text).ToArray():null;
        }
        public static bool[]? Checks(IntPtr owner,string title,string? lead,(string Label,bool Value)[] fields)
        {
            var list=new StackPanel();var checks=new List<CheckBox>();
            for(int i=0;i<fields.Length;i++)
            {
                if(i>0)list.Children.Add(UiTheme.Separator());
                var check=new CheckBox{Content=new TextBlock{Text=fields[i].Label,TextWrapping=TextWrapping.Wrap},IsChecked=fields[i].Value,Margin=new Thickness(14,11,14,11)};checks.Add(check);list.Children.Add(check);
            }
            UIElement content=new Border{Child=new ScrollViewer{Content=list,MaxHeight=380,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Background=Brushes.White,BorderBrush=UiTheme.Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10)};
            if(fields.Length>4)
            {
                var all=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,0,0,8)};
                var on=UiTheme.Button("Выбрать все",null,"Plain");on.Click+=(s,e)=>checks.ForEach(c=>c.IsChecked=true);var off=UiTheme.Button("Снять все",null,"Plain");off.Click+=(s,e)=>checks.ForEach(c=>c.IsChecked=false);
                all.Children.Add(on);all.Children.Add(off);var wrap=new StackPanel();wrap.Children.Add(all);wrap.Children.Add(content);content=wrap;
            }
            var sheet=new Sheet(owner,title,lead,DialogKind.Question,content,"Применить","Отмена",fields.Max(f=>f.Label.Length)>60?600:480);
            return sheet.ShowDialog()==true?checks.Select(c=>c.IsChecked==true).ToArray():null;
        }
        public static bool Text(IntPtr owner,string title,string text,string? apply=null,(string Label,Action<Window> Action)? extra=null)
        {
            string lead="";string body=text.Replace("\r\n","\n");
            var box=new TextBox{Text=body.Trim(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,BorderThickness=new Thickness(0),
                Background=UiTheme.Canvas,Padding=new Thickness(14,12,14,12),VerticalContentAlignment=VerticalAlignment.Top,MaxHeight=420,MinHeight=60,FontSize=13};
            var frame=new Border{Child=box,CornerRadius=new CornerRadius(10),Background=UiTheme.Canvas,ClipToBounds=true,Padding=new Thickness(2)};
            var sheet=new Sheet(owner,title,lead.Length>0?lead:null,apply==null?DialogKind.Info:DialogKind.Question,frame,apply??"Готово",apply==null?null:"Отмена",660,extra);
            return sheet.ShowDialog()==true&&apply!=null;
        }
        public static string? Choice(IntPtr owner,string title,string? lead,string[] options)
        {
            var list=new ComboBox{ItemsSource=options,SelectedIndex=options.Length>0?0:-1};
            var sheet=new Sheet(owner,title,lead,DialogKind.Question,list,"Далее","Отмена",460);sheet.Loaded+=(s,e)=>list.Focus();
            return sheet.ShowDialog()==true?list.SelectedItem as string:null;
        }
        public static void Message(IntPtr owner,string title,string text,DialogKind kind=DialogKind.Info)
        {
            new Sheet(owner,title,text,kind,null,"Понятно",null,440).ShowDialog();
        }
        public static bool Confirm(IntPtr owner,string title,string text,string apply,DialogKind kind=DialogKind.Question,string cancel="Отмена")
        {
            return new Sheet(owner,title,text,kind,null,apply,cancel,440,null,kind==DialogKind.Warning).ShowDialog()==true;
        }
        // A plan: short facts on top, the full list below, so the decision is visible at a glance.
        public static bool Plan(IntPtr owner,string title,string lead,(string Label,string Value)[] facts,string details,string apply)
        {
            var stack=new StackPanel();var grid=new Grid{Margin=new Thickness(0,0,0,14)};grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});grid.ColumnDefinitions.Add(new ColumnDefinition());
            for(int i=0;i<facts.Length;i++)
            {
                grid.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
                var label=UiTheme.Hint(facts[i].Label,new Thickness(0,5,18,5));Grid.SetRow(label,i);grid.Children.Add(label);
                var value=UiTheme.Text(facts[i].Value,13,FontWeights.SemiBold);value.Margin=new Thickness(0,4,0,4);Grid.SetRow(value,i);Grid.SetColumn(value,1);grid.Children.Add(value);
            }
            stack.Children.Add(new Border{Child=grid,Background=UiTheme.Canvas,CornerRadius=new CornerRadius(10),Padding=new Thickness(14,8,14,8),Margin=new Thickness(0,0,0,12)});
            if(details.Trim().Length>0)
            {
                var more=new Expander{Header="Подробности",Content=new TextBox{Text=details.Replace("\r\n","\n").Trim(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MaxHeight=240,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Background=UiTheme.Canvas,BorderThickness=new Thickness(0),Padding=new Thickness(10),VerticalContentAlignment=VerticalAlignment.Top}};
                stack.Children.Add(more);
            }
            return new Sheet(owner,title,lead,DialogKind.Question,stack,apply,"Отмена",560).ShowDialog()==true;
        }

        private sealed class Sheet : Window
        {
            public Sheet(IntPtr owner,string title,string? lead,DialogKind kind,UIElement? content,string apply,string? cancel,double width,(string Label,Action<Window> Action)? extra=null,bool destructive=false)
            {
                UiTheme.Apply(this,false);Title=title;Width=width+32;SizeToContent=SizeToContent.Height;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;
                WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
                if(owner!=IntPtr.Zero){new WindowInteropHelper(this).Owner=owner;WindowStartupLocation=WindowStartupLocation.CenterOwner;}else WindowStartupLocation=WindowStartupLocation.CenterScreen;
                if(owner==IntPtr.Zero&&Application.Current?.MainWindow!=null&&Application.Current.MainWindow.IsVisible){Owner=Application.Current.MainWindow;WindowStartupLocation=WindowStartupLocation.CenterOwner;}
                var root=new StackPanel{Margin=new Thickness(24,24,24,20)};
                var head=new DockPanel{Margin=new Thickness(0,0,0,lead==null&&content==null?6:8)};
                var badge=Badge(kind);DockPanel.SetDock(badge,Dock.Left);head.Children.Add(badge);
                var caption=UiTheme.Title(title,17);caption.VerticalAlignment=VerticalAlignment.Center;caption.Margin=new Thickness(12,0,0,0);head.Children.Add(caption);root.Children.Add(head);
                if(!string.IsNullOrWhiteSpace(lead)){var text=UiTheme.Text(lead!.Trim(),13,null,UiTheme.Secondary);text.Margin=new Thickness(44,0,0,16);text.LineHeight=19;root.Children.Add(text);}
                else root.Children.Add(new Border{Height=8});
                if(content!=null)root.Children.Add(content);
                var buttons=new DockPanel{Margin=new Thickness(0,20,0,0),LastChildFill=false};
                var ok=UiTheme.Button(apply,null,destructive?"Danger":"Primary");ok.IsDefault=true;ok.MinWidth=110;ok.Margin=new Thickness(8,0,0,0);ok.Click+=(s,e)=>DialogResult=true;DockPanel.SetDock(ok,Dock.Right);buttons.Children.Add(ok);
                if(cancel!=null){var no=UiTheme.Button(cancel);no.IsCancel=true;no.MinWidth=96;DockPanel.SetDock(no,Dock.Right);buttons.Children.Add(no);}
                if(extra!=null){var more=UiTheme.Button(extra.Value.Label,null,"Plain");more.Click+=(s,e)=>{try{extra.Value.Action(this);}catch(Exception ex){Message(new WindowInteropHelper(this).Handle,"Не получилось",ex.Message,DialogKind.Warning);}};DockPanel.SetDock(more,Dock.Left);buttons.Children.Add(more);}
                root.Children.Add(buttons);
                var surface=new Border{Child=root,Background=Brushes.White,CornerRadius=new CornerRadius(16),BorderBrush=new SolidColorBrush(Color.FromArgb(40,0,0,0)),BorderThickness=new Thickness(1),Margin=new Thickness(16),
                    Effect=new DropShadowEffect{BlurRadius=28,ShadowDepth=6,Direction=270,Opacity=0.22}};
                surface.MouseLeftButtonDown+=(s,e)=>{if(e.ButtonState==MouseButtonState.Pressed&&!(e.OriginalSource is TextBox))try{DragMove();}catch(InvalidOperationException){}};
                Content=surface;
                PreviewKeyDown+=(s,e)=>{if(e.Key==Key.Escape){DialogResult=cancel==null;e.Handled=true;}};
            }
            private static Border Badge(DialogKind kind)
            {
                Brush fg,bg;string glyph;
                switch(kind)
                {
                    case DialogKind.Success:fg=UiTheme.Success;bg=UiTheme.SuccessSoft;glyph=UiTheme.IconCheck;break;
                    case DialogKind.Warning:fg=UiTheme.Warning;bg=UiTheme.WarningSoft;glyph=UiTheme.IconWarning;break;
                    case DialogKind.Error:fg=UiTheme.Danger;bg=UiTheme.DangerSoft;glyph=UiTheme.IconError;break;
                    case DialogKind.Question:fg=UiTheme.Accent;bg=UiTheme.AccentSoft;glyph=UiTheme.IconDocument;break;
                    default:fg=UiTheme.Accent;bg=UiTheme.AccentSoft;glyph=UiTheme.IconInfo;break;
                }
                return new Border{Width=32,Height=32,CornerRadius=new CornerRadius(9),Background=bg,Child=new TextBlock{Text=glyph,FontFamily=UiTheme.Icons,FontSize=15,Foreground=fg,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
            }
        }
    }
}
