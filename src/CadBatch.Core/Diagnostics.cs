using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CadBatch.Core
{
    public static class ProductInfo
    {
        public const string Version="0.4.0";
    }

    public sealed class OperationTrace
    {
        private readonly List<string> entries=new List<string>();
        public IReadOnlyList<string> Entries=>entries;
        public void Note(string value)=>entries.Add(DateTime.Now.ToString("HH:mm:ss.fff",CultureInfo.InvariantCulture)+" "+value);
        public void Step(string name,Action action)=>Step(name,()=>{action();return true;});
        public T Step<T>(string name,Func<T> action)
        {
            Note("НАЧАЛО: "+name);
            try { var value=action();Note("ГОТОВО: "+name);return value; }
            catch(Exception ex)
            {
                Note("ОШИБКА: "+name+" — "+ex.Message);
                throw new InvalidOperationException("Этап «"+name+"»: "+ex.Message,ex);
            }
        }
        public override string ToString()=>string.Join(Environment.NewLine,entries);
    }

    public readonly struct ViewWindow
    {
        public ViewWindow(Point2 center,double width,double height){Center=center;Width=width;Height=height;}
        public Point2 Center {get;}
        public double Width {get;}
        public double Height {get;}
        public static ViewWindow Around(IEnumerable<Point2> points,double aspect,double padding=1.2)
        {
            var vertices=points.ToArray();
            if(vertices.Length==0 || vertices.Any(p=>!p.IsFinite))throw new ArgumentException("Некорректная область вида.");
            if(!Numeric.Positive(aspect)||!Numeric.Positive(padding))throw new ArgumentException("Некорректные пропорции вида.");
            double minX=vertices.Min(p=>p.X),maxX=vertices.Max(p=>p.X),minY=vertices.Min(p=>p.Y),maxY=vertices.Max(p=>p.Y);
            double width=(maxX-minX)*padding,height=(maxY-minY)*padding;
            if(!Numeric.Positive(width)||!Numeric.Positive(height))throw new ArgumentException("Пустая область вида.");
            if(width/height>aspect)height=width/aspect;else width=height*aspect;
            return new ViewWindow(new Point2((minX+maxX)/2,(minY+maxY)/2),width,height);
        }
    }

    public static class ExportReportText
    {
        public static string State(ExportReport report)=>report.Cancelled?"ОТМЕНЕНО":report.Errors.Count>0?
            (report.Files.Count==0&&report.Queued==0?(report.PhysicalPrint?"НЕ ПЕРЕДАНО В ПЕЧАТЬ — ОШИБКА":"PDF НЕ СОЗДАН — ОШИБКА"):"ЧАСТИЧНО ВЫПОЛНЕНО — ЕСТЬ ОШИБКИ"):
            report.PhysicalPrint?"ПЕРЕДАНО В ОЧЕРЕДЬ ПЕЧАТИ":"ЗАВЕРШЕНО";
        public static string Format(ExportReport report,ExportRequest request,string host)
        {
            var text=new StringBuilder();
            text.AppendLine("CAD Batch "+ProductInfo.Version).AppendLine(host).AppendLine(State(report));
            text.AppendLine("Устройство: "+request.Device).AppendLine("Указанный CTB/STB: "+(string.IsNullOrWhiteSpace(request.StyleSheet)?"не задан":request.StyleSheet));
            text.AppendLine("Режим цвета: "+PrintStylePolicy.Describe(request.ColorMode));
            text.AppendLine("Масштаб: "+(request.FitToPaper?"вписать":"1:1 относительно размера формата"));
            text.AppendLine("Режим PDF: "+request.PdfMode);
            if(request.PhysicalPrint)text.AppendLine("Передано страниц с копиями: "+report.Queued).AppendLine("Передача в очередь не подтверждает выход бумаги из принтера.");
            else text.AppendLine("Запланировано страниц: "+request.Items.Count).AppendLine("Создано PDF: "+report.Files.Count);
            foreach(var file in report.Files)text.AppendLine(file);
            text.AppendLine().AppendLine("Ошибки:");
            foreach(var error in report.Errors)text.AppendLine(error);
            text.AppendLine().AppendLine("Подробная диагностика:");
            foreach(var detail in report.Details)text.AppendLine(detail);
            return text.ToString();
        }
    }
}
