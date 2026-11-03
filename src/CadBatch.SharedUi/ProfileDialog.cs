using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using CadBatch.Core;

namespace CadBatch.UI
{
    public sealed class TemplateFormatRow
    {
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
        internal static readonly string[] Colors={"Общие","Цвета чертежа","Стиль из DWG","Монохромный","Указанный CTB/STB"};
        internal static TemplateFormatRow From(FormatDefinition f){var p=f.Print??new FormatPrintSettings();return new TemplateFormatRow{Name=f.Name,Width=f.WidthMm,Height=f.HeightMm,Device=p.Device,Media=p.Media,Style=p.StyleSheet,Color=p.Color.HasValue?Colors[(int)p.Color.Value+1]:Colors[0],Fit=p.Fit,Turn180=p.UpsideDown,X=p.OffsetX,Y=p.OffsetY};}
        internal FormatDefinition ToFormat()=>new FormatDefinition(Name,Width,Height){Print=new FormatPrintSettings{Device=Device,Media=Media,StyleSheet=Style,Color=Array.IndexOf(Colors,Color)>0?(PrintColorMode?)(Array.IndexOf(Colors,Color)-1):null,Fit=Fit,UpsideDown=Turn180,OffsetX=X,OffsetY=Y}};
    }
    public sealed class ProfileDialog : Form
    {
        public ProjectProfile Profile {get;private set;}
        private readonly DataGridView table=new DataGridView();
        private readonly ComboBox names=new ComboBox{Width=245,DropDownStyle=ComboBoxStyle.DropDown};
        private readonly string directory=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CadBatch","Templates");
        private BindingList<TemplateFormatRow> rows=new BindingList<TemplateFormatRow>();
        public ProfileDialog(ProjectProfile profile,IBatchHost host)
        {
            Profile=ProfileStore.Clone(profile);Text="Шаблоны и печать по форматам";Size=new Size(1120,650);MinimumSize=new Size(850,500);StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
            var top=new FlowLayoutPanel{Dock=DockStyle.Top,Height=95,Padding=new Padding(12),AutoScroll=true};
            top.Controls.Add(new Label{Text="Шаблон:",AutoSize=true,Margin=new Padding(0,7,8,0)});top.Controls.Add(names);ReloadNames();
            AddButton(top,"Загрузить",()=>{if(string.IsNullOrWhiteSpace(names.Text))throw new ArgumentException("Выберите шаблон в списке.");string path=PrintTemplates.PathFor(directory,names.Text);if(!File.Exists(path))throw new ArgumentException("Шаблон «"+names.Text+"» не найден.");Profile=ProfileStore.Load(path);Fill();});
            AddButton(top,"Сохранить как",()=>{if(string.IsNullOrWhiteSpace(names.Text))throw new ArgumentException("Введите имя шаблона.");Commit();string path=PrintTemplates.PathFor(directory,names.Text);ProfileStore.Save(path,Profile);ReloadNames();});
            AddButton(top,"Удалить",()=>{string path=PrintTemplates.PathFor(directory,names.Text);if(File.Exists(path)&&ModernDialog.Confirm(Handle,"Удалить шаблон?","Шаблон «"+names.Text+"» будет удалён без возможности восстановления.","Удалить",DialogKind.Warning)){File.Delete(path);names.Text="";ReloadNames();}});
            top.Controls.Add(new Label{Text="Пустые устройство / бумага / стиль и «Общие» берутся из главного окна. Fit: пусто — общая настройка. X/Y — начало печати в мм.\r\nРазмеры формата — мм бумаги. Поворот 180° разворачивает содержимое. Для кратного PDF оставьте бумагу пустой — подбор автоматический.",AutoSize=true,Margin=new Padding(0,10,0,0)});
            table.Dock=DockStyle.Fill;table.AutoGenerateColumns=false;table.BackgroundColor=Color.White;table.AllowUserToOrderColumns=true;
            string[] props={"Name","Width","Height","Device","Media","Style","Color","Fit","Turn180","X","Y"};string[] captions={"Формат","Ширина","Высота","Принтер / PC3","Бумага (canonical)","CTB / STB","Цвет","Fit","180°","X, мм","Y, мм"};
            for(int i=0;i<props.Length;i++)
            {
                DataGridViewColumn column=i==6?new DataGridViewComboBoxColumn{DataSource=TemplateFormatRow.Colors}:i==7?new DataGridViewCheckBoxColumn{ThreeState=true}:i==8?new DataGridViewCheckBoxColumn():new DataGridViewTextBoxColumn();column.DataPropertyName=props[i];column.HeaderText=captions[i];column.Width=i==3||i==4?185:i==6?170:i==5?150:75;table.Columns.Add(column);
            }
            table.DataError+=(s,e)=>{e.ThrowException=false;};Fill();
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(10),FlowDirection=FlowDirection.RightToLeft};
            AddButton(bottom,"Применить",()=>{Commit();DialogResult=DialogResult.OK;});AddButton(bottom,"Отмена",()=>DialogResult=DialogResult.Cancel);
            AddButton(bottom,"Импорт JSON",()=>{using(var d=new OpenFileDialog{Filter="Шаблон CAD Batch|*.json"})if(d.ShowDialog(this)==DialogResult.OK){Profile=ProfileStore.Load(d.FileName);Fill();}});
            AddButton(bottom,"Экспорт JSON",()=>{Commit();using(var d=new SaveFileDialog{Filter="Шаблон CAD Batch|*.json",FileName="CadBatch-profile.json"})if(d.ShowDialog(this)==DialogResult.OK)ProfileStore.Save(d.FileName,Profile);});
            var deviceButton=new Button{Text="Выбрать устройство / бумагу…",AutoSize=true,Enabled=host.CanPlot};bottom.Controls.Add(deviceButton);
            deviceButton.Click+=async(s,e)=>
            {
                try
                {
                    table.EndEdit();var row=table.CurrentRow?.DataBoundItem as TemplateFormatRow;if(row==null)return;var devices=await host.GetPlotDevicesAsync();if(IsDisposed)return;
                    using(var dialog=new Form{Text="Печать формата "+row.Name,Size=new Size(760,325),StartPosition=FormStartPosition.CenterParent,Font=Font})
                    {
                        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,Padding=new Padding(12),WrapContents=false};
                        var printers=new ComboBox{Width=710,DropDownStyle=ComboBoxStyle.DropDownList};printers.Items.AddRange(devices.Cast<object>().ToArray());printers.SelectedItem=string.IsNullOrWhiteSpace(row.Device)?Profile.Device:row.Device;
                        var media=new ComboBox{Width=710,DropDownStyle=ComboBoxStyle.DropDownList};var styles=new ComboBox{Width=710,DropDownStyle=ComboBoxStyle.DropDownList};
                        var apply=new Button{Text="Применить к формату",AutoSize=true,Enabled=false};var loading=new Label{AutoSize=true};int revision=0;
                        async System.Threading.Tasks.Task Reload()
                        {
                            if(!(printers.SelectedItem is string selected))return;int current=++revision;apply.Enabled=false;loading.Text="Чтение бумаги и стилей AutoCAD…";var caps=await host.GetPlotCapabilitiesAsync(selected);if(dialog.IsDisposed||revision!=current||printers.Text!=selected)return;
                            media.Items.Clear();media.Items.Add("Автоматически по рамке");media.Items.AddRange(caps.Media.Cast<object>().ToArray());media.SelectedItem=row.Media;if(media.SelectedIndex<0)media.SelectedIndex=0;
                            styles.Items.Clear();styles.Items.Add("Общий стиль");styles.Items.AddRange(caps.Styles.Cast<object>().ToArray());styles.SelectedItem=row.Style;if(styles.SelectedIndex<0)styles.SelectedIndex=0;loading.Text="Для уменьшения большого формата на малую бумагу включите Fit в таблице.";apply.Enabled=true;
                        }
                        printers.SelectedIndexChanged+=async(sender,args)=>{try{await Reload();}catch(Exception ex){if(!dialog.IsDisposed)loading.Text=ex.Message;}};
                        dialog.Shown+=async(sender,args)=>{try{await Reload();}catch(Exception ex){loading.Text=ex.Message;}};
                        apply.Click+=(sender,args)=>{row.Device=printers.Text;row.Media=media.SelectedIndex==0?"":media.Text;row.Style=styles.SelectedIndex==0?"":styles.Text;table.Refresh();dialog.DialogResult=DialogResult.OK;};
                        panel.Controls.Add(new Label{Text="Принтер / PC3",AutoSize=true});panel.Controls.Add(printers);panel.Controls.Add(new Label{Text="Бумага",AutoSize=true});panel.Controls.Add(media);panel.Controls.Add(new Label{Text="CTB / STB",AutoSize=true});panel.Controls.Add(styles);panel.Controls.Add(loading);panel.Controls.Add(apply);dialog.Controls.Add(panel);WinFormsLook.Primary(apply);WinFormsLook.Apply(dialog);dialog.ShowDialog(this);
                    }
                }catch(Exception ex){if(!IsDisposed)ModernDialog.Message(Handle,"Устройства AutoCAD",ex.Message,DialogKind.Warning);}
            };
            Controls.Add(table);Controls.Add(top);Controls.Add(bottom);WinFormsLook.Apply(this);
        }
        private void Fill(){rows=new BindingList<TemplateFormatRow>(Profile.Formats.Select(TemplateFormatRow.From).ToList());table.DataSource=rows;}
        private void Commit(){if(!table.EndEdit())throw new ArgumentException("Завершите редактирование ячейки.");var formats=rows.Select(r=>r.ToFormat()).ToList();var old=Profile.Formats;Profile.Formats=formats;try{Profile.Validate();}catch{Profile.Formats=old;throw;}}
        private void ReloadNames(){string selected=names.Text;names.Items.Clear();if(Directory.Exists(directory))names.Items.AddRange(Directory.GetFiles(directory,"*.json").Select(Path.GetFileNameWithoutExtension).Cast<object>().ToArray());names.Text=selected;}
        private void AddButton(Control target,string text,Action action){var button=new Button{Text=text,AutoSize=true};button.Click+=(s,e)=>{try{action();}catch(Exception ex){ModernDialog.Message(Handle,"Шаблоны",ex.Message,DialogKind.Warning);}};target.Controls.Add(button);if(text=="Применить")WinFormsLook.Primary(button);}
    }
}
