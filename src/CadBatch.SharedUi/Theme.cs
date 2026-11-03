using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace CadBatch.UI
{
    // One visual language for every CAD Batch window. Resources are always window-scoped,
    // so the AutoCAD process keeps its own application theme.
    public static class UiTheme
    {
        public static readonly Color InkColor=Color.FromRgb(29,29,31),AccentColor=Color.FromRgb(0,113,227);
        public static readonly Brush Ink=Freeze(InkColor),Secondary=Freeze(Color.FromRgb(110,110,115)),Tertiary=Freeze(Color.FromRgb(142,142,147)),
            Canvas=Freeze(Color.FromRgb(245,245,247)),Surface=Brushes.White,Line=Freeze(Color.FromRgb(229,229,234)),Fill=Freeze(Color.FromRgb(232,232,237)),
            Accent=Freeze(AccentColor),AccentSoft=Freeze(Color.FromRgb(232,242,253)),Success=Freeze(Color.FromRgb(36,138,61)),SuccessSoft=Freeze(Color.FromRgb(232,247,237)),
            Warning=Freeze(Color.FromRgb(178,80,0)),WarningSoft=Freeze(Color.FromRgb(255,244,229)),Danger=Freeze(Color.FromRgb(215,0,21)),DangerSoft=Freeze(Color.FromRgb(253,236,238));
        public static readonly FontFamily Font=new FontFamily("Segoe UI Variable Text, Segoe UI"),Display=new FontFamily("Segoe UI Variable Display, Segoe UI"),Icons=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
        public const string IconSearch="",IconFolder="",IconOpen="",IconDocument="",IconCheck="",IconClose="",IconSettings="",
            IconHelp="",IconRefresh="",IconDownload="",IconInfo="",IconWarning="",IconError="",IconMore="",IconLocate="",
            IconPrint="",IconPlay="",IconPlug="",IconSuccess="",IconFilter="",IconAdd="",IconPage="",IconLink="";
        private static Brush Freeze(Color color){var brush=new SolidColorBrush(color);brush.Freeze();return brush;}

        public static ResourceDictionary Resources(bool modern=true)
        {
            var dictionary=(ResourceDictionary)XamlReader.Parse(Xaml.Replace("{MODERN}",modern?"<ResourceDictionary.MergedDictionaries><ui:ThemeResources/><ui:XamlControlsResources/></ResourceDictionary.MergedDictionaries>":""));
            return dictionary;
        }
        public static void Apply(Window window,bool modern=true)
        {
            window.Resources=Resources(modern);window.FontFamily=Font;window.FontSize=13;window.Foreground=Ink;window.Background=Canvas;
            TextOptions.SetTextFormattingMode(window,TextFormattingMode.Display);UseLayoutRounding(window);
            if(modern)ModernWpf.ThemeManager.SetRequestedTheme(window,ModernWpf.ElementTheme.Light);
        }
        private static void UseLayoutRounding(Window window){window.UseLayoutRounding=true;window.SnapsToDevicePixels=true;}

        public static TextBlock Text(string text,double size=13,FontWeight? weight=null,Brush? color=null)=>new TextBlock{Text=text,FontSize=size,FontWeight=weight??FontWeights.Normal,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap,FontFamily=size>=20?Display:Font};
        public static TextBlock Title(string text,double size=22)=>Text(text,size,FontWeights.SemiBold);
        public static TextBlock Hint(string text,Thickness? margin=null)=>new TextBlock{Text=text,FontSize=12,Foreground=Secondary,TextWrapping=TextWrapping.Wrap,Margin=margin??new Thickness(0),LineHeight=17};
        public static TextBlock Caption(string text)=>new TextBlock{Text=text.ToUpperInvariant(),FontSize=11,FontWeight=FontWeights.SemiBold,Foreground=Tertiary,Margin=new Thickness(4,0,0,7)};
        public static TextBlock Icon(string glyph,double size=14,Brush? color=null)=>new TextBlock{Text=glyph,FontFamily=Icons,FontSize=size,Foreground=color??Ink,VerticalAlignment=VerticalAlignment.Center};
        // Large scrolling content (the frame table) gets no shadow: effects force off-screen rendering of the whole subtree.
        public static Border Card(UIElement child,double padding=18,bool shadow=true)=>new Border{Child=child,Background=Surface,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(14),Padding=new Thickness(padding),Margin=new Thickness(0,0,0,14),
            Effect=shadow?new DropShadowEffect{BlurRadius=18,ShadowDepth=1,Direction=270,Opacity=0.05,Color=Colors.Black}:null};
        public static Border Pill(string text,Brush foreground,Brush background,string? glyph=null)
        {
            var row=new StackPanel{Orientation=Orientation.Horizontal};
            if(glyph=="●")row.Children.Add(new System.Windows.Shapes.Ellipse{Width=7,Height=7,Fill=foreground,Margin=new Thickness(0,1,7,0),VerticalAlignment=VerticalAlignment.Center});
            else if(glyph!=null){var icon=Icon(glyph,10,foreground);icon.Margin=new Thickness(0,0,6,0);row.Children.Add(icon);}
            row.Children.Add(new TextBlock{Text=text,FontSize=12,FontWeight=FontWeights.SemiBold,Foreground=foreground,VerticalAlignment=VerticalAlignment.Center});
            return new Border{Child=row,Background=background,CornerRadius=new CornerRadius(20),Padding=new Thickness(10,4,11,5),VerticalAlignment=VerticalAlignment.Center};
        }
        public static Border Step(int number,bool active=true)=>new Border{Width=22,Height=22,CornerRadius=new CornerRadius(11),Background=active?Accent:Fill,VerticalAlignment=VerticalAlignment.Center,
            Child=new TextBlock{Text=number.ToString(),Foreground=active?Brushes.White:Secondary,FontSize=12,FontWeight=FontWeights.SemiBold,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center}};
        public static FrameworkElement StepHeader(int number,string title,string subtitle,out Border badge)
        {
            var stack=new StackPanel{Margin=new Thickness(0,0,0,16)};var row=new StackPanel{Orientation=Orientation.Horizontal};badge=Step(number);row.Children.Add(badge);
            var caption=Title(title,19);caption.Margin=new Thickness(10,0,0,0);caption.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(caption);stack.Children.Add(row);
            stack.Children.Add(Hint(subtitle,new Thickness(32,3,0,0)));return stack;
        }
        public static Button Button(string text,string? glyph=null,string style="")
        {
            var button=new Button{Content=Content(text,glyph)};if(style.Length>0)button.SetResourceReference(FrameworkElement.StyleProperty,style);return button;
        }
        public static object Content(string text,string? glyph)
        {
            if(glyph==null)return text;
            var row=new StackPanel{Orientation=Orientation.Horizontal};var icon=new TextBlock{Text=glyph,FontFamily=Icons,FontSize=13,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,1,text.Length>0?8:0,0)};
            row.Children.Add(icon);if(text.Length>0)row.Children.Add(new TextBlock{Text=text,VerticalAlignment=VerticalAlignment.Center});return row;
        }
        public static Border Separator()=>new Border{Height=1,Background=Line,Margin=new Thickness(0)};

        private const string Xaml=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' xmlns:ui='http://schemas.modernwpf.com/2019'>
{MODERN}
<SolidColorBrush x:Key='Ink' Color='#1D1D1F'/><SolidColorBrush x:Key='Secondary' Color='#6E6E73'/><SolidColorBrush x:Key='Line' Color='#E5E5EA'/>
<SolidColorBrush x:Key='Accent' Color='#0071E3'/><SolidColorBrush x:Key='AccentSoft' Color='#E8F2FD'/><SolidColorBrush x:Key='Field' Color='#D2D2D7'/>
<Style x:Key='FocusRing'><Setter Property='Control.Template'><Setter.Value><ControlTemplate><Border Margin='-3' CornerRadius='12' BorderBrush='#660071E3' BorderThickness='3'/></ControlTemplate></Setter.Value></Setter></Style>

<Style TargetType='Button'>
  <Setter Property='Background' Value='#E8E8ED'/><Setter Property='Foreground' Value='#1D1D1F'/><Setter Property='Padding' Value='14,8'/><Setter Property='MinHeight' Value='34'/>
  <Setter Property='BorderThickness' Value='0'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FontWeight' Value='SemiBold'/><Setter Property='FontSize' Value='13'/>
  <Setter Property='HorizontalContentAlignment' Value='Center'/><Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
    <Grid><Border x:Name='surface' CornerRadius='10' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'/>
      <Border x:Name='shade' CornerRadius='10' Background='#000000' Opacity='0'/>
      <ContentPresenter Margin='{TemplateBinding Padding}' HorizontalAlignment='{TemplateBinding HorizontalContentAlignment}' VerticalAlignment='Center' TextElement.Foreground='{TemplateBinding Foreground}' RecognizesAccessKey='True'/></Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='shade' Property='Opacity' Value='0.05'/></Trigger>
      <Trigger Property='IsPressed' Value='True'><Setter TargetName='shade' Property='Opacity' Value='0.11'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger>
    </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
<Style x:Key='Primary' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'><Setter Property='Background' Value='#0071E3'/><Setter Property='Foreground' Value='White'/><Setter Property='Padding' Value='18,9'/></Style>
<Style x:Key='Plain' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='#0066CC'/><Setter Property='Padding' Value='10,6'/></Style>
<Style x:Key='Outline' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'><Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='#D2D2D7'/><Setter Property='BorderThickness' Value='1'/></Style>
<Style x:Key='Danger' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'><Setter Property='Background' Value='#FDECEE'/><Setter Property='Foreground' Value='#D70015'/></Style>
<Style x:Key='Chip' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'><Setter Property='Background' Value='#E8F2FD'/><Setter Property='Foreground' Value='#0066CC'/><Setter Property='Padding' Value='9,3'/><Setter Property='MinHeight' Value='24'/><Setter Property='FontSize' Value='11.5'/><Setter Property='Margin' Value='0,0,6,6'/></Style>
<Style x:Key='Square' TargetType='Button' BasedOn='{StaticResource {x:Type Button}}'><Setter Property='Padding' Value='0'/><Setter Property='Width' Value='34'/><Setter Property='Height' Value='34'/></Style>

<Style TargetType='TextBox'>
  <Setter Property='Foreground' Value='#1D1D1F'/><Setter Property='Background' Value='White'/><Setter Property='BorderBrush' Value='#D2D2D7'/><Setter Property='BorderThickness' Value='1'/>
  <Setter Property='Padding' Value='9,0'/><Setter Property='MinHeight' Value='34'/><Setter Property='VerticalContentAlignment' Value='Center'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='CaretBrush' Value='#0071E3'/><Setter Property='ScrollViewer.VerticalScrollBarVisibility' Value='Hidden'/><Setter Property='ScrollViewer.HorizontalScrollBarVisibility' Value='Hidden'/><Setter Property='SelectionBrush' Value='#0071E3'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TextBox'>
    <Grid><Border x:Name='ring' Margin='-3' CornerRadius='11' BorderBrush='#550071E3' BorderThickness='3' Opacity='0'/>
      <Border x:Name='box' CornerRadius='8' Background='{TemplateBinding Background}' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}'>
        <Grid><TextBlock x:Name='placeholder' Text='{TemplateBinding Tag}' Foreground='#A1A1A6' Margin='{TemplateBinding Padding}' Padding='2,0,0,0' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' IsHitTestVisible='False' Visibility='Collapsed'/>
          <ScrollViewer x:Name='PART_ContentHost' Style='{x:Null}' VerticalAlignment='{TemplateBinding VerticalContentAlignment}' Focusable='False' HorizontalScrollBarVisibility='{TemplateBinding ScrollViewer.HorizontalScrollBarVisibility}' VerticalScrollBarVisibility='{TemplateBinding ScrollViewer.VerticalScrollBarVisibility}'/></Grid></Border></Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='Text' Value=''><Setter TargetName='placeholder' Property='Visibility' Value='Visible'/></Trigger>
      <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#B9B9BF'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#0071E3'/><Setter TargetName='ring' Property='Opacity' Value='1'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger>
      <Trigger Property='IsReadOnly' Value='True'><Setter TargetName='box' Property='Background' Value='#F5F5F7'/></Trigger>
    </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>

<Style TargetType='ComboBoxItem'>
  <Setter Property='Foreground' Value='#1D1D1F'/><Setter Property='Padding' Value='10,7'/><Setter Property='Margin' Value='4,1'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'>
    <Border x:Name='bg' CornerRadius='6' Background='Transparent' Padding='{TemplateBinding Padding}'><ContentPresenter/></Border>
    <ControlTemplate.Triggers>
      <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bg' Property='Background' Value='#0071E3'/><Setter Property='Foreground' Value='White'/></Trigger>
      <Trigger Property='IsSelected' Value='True'><Setter Property='FontWeight' Value='SemiBold'/></Trigger>
    </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='ComboBox'>
  <Setter Property='Foreground' Value='#1D1D1F'/><Setter Property='MinHeight' Value='34'/><Setter Property='Cursor' Value='Hand'/><Setter Property='FocusVisualStyle' Value='{StaticResource FocusRing}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'>
    <Grid>
      <ToggleButton Focusable='False' HorizontalAlignment='Stretch' VerticalAlignment='Stretch' IsChecked='{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' ClickMode='Press'>
        <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border x:Name='box' Background='White' BorderBrush='#D2D2D7' BorderThickness='1' CornerRadius='8'/>
          <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#B9B9BF'/></Trigger><Trigger Property='IsChecked' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#0071E3'/></Trigger></ControlTemplate.Triggers></ControlTemplate></ToggleButton.Template>
      </ToggleButton>
      <ContentPresenter Content='{TemplateBinding SelectionBoxItem}' ContentTemplate='{TemplateBinding SelectionBoxItemTemplate}' Margin='11,6,32,6' VerticalAlignment='Center' HorizontalAlignment='Left' IsHitTestVisible='False'/>
      <TextBlock Text='&#xE70D;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='10' HorizontalAlignment='Right' VerticalAlignment='Center' Margin='0,1,12,0' Foreground='#6E6E73' IsHitTestVisible='False'/>
      <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' VerticalOffset='4' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'>
        <Border Margin='8,0,8,12' Background='White' BorderBrush='#E5E5EA' BorderThickness='1' CornerRadius='10' Padding='0,4' MinWidth='{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}' MaxHeight='340'>
          <Border.Effect><DropShadowEffect BlurRadius='22' ShadowDepth='4' Direction='270' Opacity='0.14'/></Border.Effect>
          <ScrollViewer CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border></Popup>
    </Grid>
    <ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.5'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>

<Style TargetType='CheckBox'>
  <Setter Property='Foreground' Value='#1D1D1F'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Margin' Value='0,4'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
    <Grid Background='Transparent'><Grid.ColumnDefinitions><ColumnDefinition Width='Auto'/><ColumnDefinition/></Grid.ColumnDefinitions>
      <Border x:Name='box' Width='18' Height='18' CornerRadius='5' BorderBrush='#C7C7CC' BorderThickness='1.5' Background='White' VerticalAlignment='Top' Margin='0,1,0,0'>
        <TextBlock x:Name='mark' Text='&#xE73E;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='11' Foreground='White' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/></Border>
      <ContentPresenter Grid.Column='1' Margin='10,0,0,0' VerticalAlignment='Center' RecognizesAccessKey='True'/></Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsChecked' Value='True'><Setter TargetName='box' Property='Background' Value='#0071E3'/><Setter TargetName='box' Property='BorderBrush' Value='#0071E3'/><Setter TargetName='mark' Property='Visibility' Value='Visible'/></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='box' Property='BorderBrush' Value='#0071E3'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
<Style x:Key='Toggle' TargetType='CheckBox'>
  <Setter Property='Cursor' Value='Hand'/><Setter Property='Focusable' Value='True'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
    <Grid Width='42' Height='25' Background='Transparent'>
      <Border x:Name='ring' Margin='-3' CornerRadius='15' BorderBrush='#550071E3' BorderThickness='3' Opacity='0'/>
      <Border x:Name='track' Background='#E3E3E8' CornerRadius='12.5'/>
      <Ellipse x:Name='thumb' Width='21' Height='21' Fill='White' HorizontalAlignment='Left' Margin='2,0,0,0'>
        <Ellipse.RenderTransform><TranslateTransform X='0'/></Ellipse.RenderTransform>
        <Ellipse.Effect><DropShadowEffect BlurRadius='5' ShadowDepth='1' Direction='270' Opacity='0.25'/></Ellipse.Effect></Ellipse></Grid>
    <ControlTemplate.Triggers>
      <Trigger Property='IsChecked' Value='True'><Setter TargetName='track' Property='Background' Value='#0071E3'/>
        <Trigger.EnterActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='thumb' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='17' Duration='0:0:0.16'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation></Storyboard></BeginStoryboard></Trigger.EnterActions>
        <Trigger.ExitActions><BeginStoryboard><Storyboard><DoubleAnimation Storyboard.TargetName='thumb' Storyboard.TargetProperty='(UIElement.RenderTransform).(TranslateTransform.X)' To='0' Duration='0:0:0.16'><DoubleAnimation.EasingFunction><CubicEase EasingMode='EaseOut'/></DoubleAnimation.EasingFunction></DoubleAnimation></Storyboard></BeginStoryboard></Trigger.ExitActions></Trigger>
      <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='ring' Property='Opacity' Value='1'/></Trigger>
      <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger>
    </ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
</Style>
<Style x:Key='SheetCheck' TargetType='CheckBox'>
  <Setter Property='HorizontalAlignment' Value='Center'/><Setter Property='VerticalAlignment' Value='Center'/><Setter Property='Cursor' Value='Hand'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='CheckBox'>
    <Border x:Name='box' Width='20' Height='20' CornerRadius='10' BorderBrush='#C7C7CC' BorderThickness='1.5' Background='White'>
      <TextBlock x:Name='mark' Text='&#xE73E;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='11' Foreground='White' HorizontalAlignment='Center' VerticalAlignment='Center' Visibility='Collapsed'/></Border>
    <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='box' Property='Background' Value='#0071E3'/><Setter TargetName='box' Property='BorderBrush' Value='#0071E3'/><Setter TargetName='mark' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>

<Style TargetType='Expander'>
  <Setter Property='Foreground' Value='#1D1D1F'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Expander'>
    <StackPanel>
      <ToggleButton x:Name='head' HorizontalAlignment='Stretch' HorizontalContentAlignment='Stretch' Content='{TemplateBinding Header}' Foreground='{TemplateBinding Foreground}' IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}' Cursor='Hand' FocusVisualStyle='{x:Null}'>
        <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'>
          <Border Background='Transparent' Padding='0,2'><DockPanel>
            <TextBlock x:Name='chevron' DockPanel.Dock='Right' Text='&#xE70D;' FontFamily='Segoe Fluent Icons, Segoe MDL2 Assets' FontSize='10' Foreground='#6E6E73' VerticalAlignment='Center'/>
            <ContentPresenter VerticalAlignment='Center' TextElement.FontWeight='SemiBold' TextElement.Foreground='#1D1D1F'/></DockPanel></Border>
          <ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='chevron' Property='Text' Value='&#xE70E;'/></Trigger></ControlTemplate.Triggers>
        </ControlTemplate></ToggleButton.Template></ToggleButton>
      <ContentPresenter x:Name='body' Margin='0,12,0,0' Visibility='Collapsed'/>
    </StackPanel>
    <ControlTemplate.Triggers><Trigger Property='IsExpanded' Value='True'><Setter TargetName='body' Property='Visibility' Value='Visible'/></Trigger></ControlTemplate.Triggers>
  </ControlTemplate></Setter.Value></Setter>
</Style>

<Style TargetType='ProgressBar'>
  <Setter Property='Height' Value='6'/><Setter Property='Foreground' Value='#0071E3'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ProgressBar'>
    <Grid><Border x:Name='PART_Track' CornerRadius='3' Background='#E5E5EA'/><Border x:Name='PART_Indicator' CornerRadius='3' Background='{TemplateBinding Foreground}' HorizontalAlignment='Left'/></Grid>
  </ControlTemplate></Setter.Value></Setter>
</Style>

<Style TargetType='ToolTip'>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ToolTip'>
    <Border Background='#F21D1D1F' CornerRadius='7' Padding='9,5' Margin='6'><ContentPresenter TextElement.Foreground='White' TextElement.FontSize='12'/></Border>
  </ControlTemplate></Setter.Value></Setter>
</Style>

<Style TargetType='ScrollBar'>
  <Setter Property='Width' Value='10'/><Setter Property='MinWidth' Value='10'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
    <Track x:Name='PART_Track' IsDirectionReversed='True'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='t' CornerRadius='4' Background='#C7C7CC' Margin='2'/>
      <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='t' Property='Background' Value='#A1A1A6'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track>
  </ControlTemplate></Setter.Value></Setter>
  <Style.Triggers><Trigger Property='Orientation' Value='Horizontal'><Setter Property='Width' Value='Auto'/><Setter Property='MinWidth' Value='0'/><Setter Property='Height' Value='10'/><Setter Property='MinHeight' Value='10'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Track x:Name='PART_Track' IsDirectionReversed='False'><Track.Thumb><Thumb><Thumb.Template><ControlTemplate TargetType='Thumb'><Border x:Name='t' CornerRadius='4' Background='#C7C7CC' Margin='2'/>
        <ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='t' Property='Background' Value='#A1A1A6'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track>
    </ControlTemplate></Setter.Value></Setter></Trigger></Style.Triggers>
</Style>

<Style TargetType='DataGridColumnHeader'>
  <Setter Property='Foreground' Value='#6E6E73'/><Setter Property='FontSize' Value='12'/><Setter Property='FontWeight' Value='SemiBold'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridColumnHeader'>
    <Grid><Border Background='#FAFAFC' BorderBrush='#E5E5EA' BorderThickness='0,0,0,1' Padding='10,11,10,10'><ContentPresenter VerticalAlignment='Center'/></Border>
      <Thumb x:Name='PART_RightHeaderGripper' HorizontalAlignment='Right' Width='7' Cursor='SizeWE'><Thumb.Template><ControlTemplate TargetType='Thumb'><Border Background='Transparent'/></ControlTemplate></Thumb.Template></Thumb></Grid>
  </ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='DataGridCell'>
  <Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='10,0'/><Setter Property='FocusVisualStyle' Value='{x:Null}'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='DataGridCell'>
    <Border Background='Transparent' Padding='{TemplateBinding Padding}'><ContentPresenter VerticalAlignment='Center'/></Border>
  </ControlTemplate></Setter.Value></Setter>
</Style>
<Style TargetType='DataGridRow'>
  <Setter Property='Background' Value='White'/><Setter Property='Foreground' Value='#1D1D1F'/>
  <Style.Triggers>
    <Trigger Property='IsMouseOver' Value='True'><Setter Property='Background' Value='#F7F7FA'/></Trigger>
    <Trigger Property='IsSelected' Value='True'><Setter Property='Background' Value='#E8F2FD'/></Trigger>
  </Style.Triggers>
</Style>
</ResourceDictionary>";
    }
}
