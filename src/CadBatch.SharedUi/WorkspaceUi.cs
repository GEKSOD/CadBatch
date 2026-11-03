using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using CadBatch.Core;

namespace CadBatch.UI
{
    public sealed partial class BatchWindow
    {
        // Shared command/dialog session. FluentWindow is the visible application shell.
        // These controls retain the existing validation/binding contracts for CAD commands.
        private Control workspace=null!;
        private FlowLayoutPanel workspaceToolbar=null!;
        private readonly Label frameSummary=new Label(),pdfDescription=new Label();
        private void BuildWorkspace()
        {
            Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
            var panel=new Panel{Dock=DockStyle.Fill};workspace=panel;Controls.Add(panel);
            workspaceToolbar=new FlowLayoutPanel();findBox.Controls.AddRange(new Control[]{model,layouts,allDocs,polylines,blocks,scales,tolerance,layer,block,stampScale});
            outputBox.Controls.AddRange(new Control[]{folder,pattern,device,style,color,fit,onePdf});panel.Controls.AddRange(new Control[]{findBox,outputBox,grid,workspaceToolbar});
            grid.AutoGenerateColumns=false;grid.AllowUserToAddRows=false;grid.SelectionMode=DataGridViewSelectionMode.FullRowSelect;grid.MultiSelect=true;
            grid.Columns.Add(new DataGridViewCheckBoxColumn{DataPropertyName="Enabled",HeaderText="Вывод"});
            Column("Page","№",42);Column("Title","Наименование",230,false);Column("SheetNumber","Лист",65,false);Column("FormatName","Формат",75);Column("SizeText","Размер, мм",113);Column("Scale","Масштаб",84);Column("AngleDegrees","Угол",68);Column("Layout","Вкладка",85);
            Column("File","Чертёж",170);Column("Layer","Слой",100);Column("Kind","Источник",120);Column("ScaleSource","Масштаб из",150);Column("Status","Состояние",230);Column("Code","Шифр",110,false);Column("Copies","Копии",65,false);Column("Device","Устройство строки",190,false);Column("StyleSheet","CTB / STB строки",150,false);
        }
        private void DecorateMenu(){}
        private void SetAll(bool enabled){foreach(var f in frames)f.Enabled=enabled;frames.ResetBindings();UpdateCount();}
        private void RefreshWorkspaceCount()
        {
            int count=frames.Count(f=>f.Enabled);
            frameSummary.Text=(frames.Count>0?frames.Select(f=>f.File).Distinct().Count()==1?frames[0].File:"Документов: "+frames.Select(f=>f.File).Distinct().Count():"Откройте DWG в AutoCAD")+" · найдено "+frames.Count+", к выводу "+count+(profile.Search.Region!=null?" · в выбранной области":"");
            pdf.Text=profile.PdfMode==PdfOutputMode.OneFile?"Сохранить один PDF":"Вывести "+count+" PDF";
            pdfDescription.Text=profile.PdfMode==PdfOutputMode.OneFile?"Все отмеченные рамки — в одном файле":"Режим: "+(pdfModes.Count>0?pdfModes[(int)profile.PdfMode].Text:"отдельный PDF на рамку");
        }
        private void RefreshSelection(){show.Enabled=running==null;singlePdf.Enabled=host.CanPlot&&running==null;RefreshWorkspaceCount();}
    }
}
