using System;
using System.Collections.Generic;
using System.Linq;

namespace CadBatch.Core
{
    public sealed class RegisterOptions
    {
        public string[] Columns {get;set;}=new[]{"Лист","Наименование","Шифр","Формат","Чертёж","Вкладка"};
        // One field or several separated by ";": "Шифр; Формат" gives nested headings.
        public string GroupBy {get;set;}="";
        public static readonly string[] Available={"№","Лист","Наименование","Шифр","Ревизия","Формат","Размер, мм","Масштаб","Чертёж","Полный путь","Вкладка","Слой","Источник"};
        public static string Value(DetectedFrame f,string field)
        {
            switch(field){case "№":return f.Page.ToString();case "Лист":return f.SheetNumber;case "Наименование":return f.Title;case "Шифр":return f.Code;case "Ревизия":return f.Revision;case "Формат":return f.FormatName;case "Размер, мм":return f.SizeText;case "Масштаб":return f.Scale.ToString("0.########");case "Чертёж":return f.File;case "Полный путь":return f.Source.DocumentPath;case "Вкладка":return f.Layout;case "Слой":return f.Layer;case "Источник":return f.Kind;default:return f.Metadata.TryGetValue(field,out var value)?value:"";}
        }
        public string[] GroupFields=>(GroupBy??"").Split(';').Select(x=>x.Trim()).Where(x=>x.Length>0).ToArray();
        public List<string[]> Rows(IEnumerable<DetectedFrame> source)
        {
            if(Columns==null||Columns.Length<1||Columns.Length>26||Columns.Any(c=>string.IsNullOrWhiteSpace(c)))throw new ArgumentException("Выберите от 1 до 26 столбцов ведомости.");
            var fields=GroupFields;if(fields.Length>4)throw new ArgumentException("Группировать можно не более чем по четырём полям.");
            var rows=new List<string[]>{Columns};Add(rows,source.Where(f=>f.Enabled).ToList(),fields,0);return rows;
        }
        private void Add(List<string[]> rows,List<DetectedFrame> frames,string[] fields,int level)
        {
            if(level==fields.Length){rows.AddRange(frames.Select(f=>Columns.Select(c=>Value(f,c)).ToArray()));return;}
            foreach(var group in frames.GroupBy(f=>Value(f,fields[level])))
            {
                var heading=Enumerable.Repeat("",Columns.Length).ToArray();heading[0]=new string(' ',level*2)+fields[level]+": "+(group.Key.Length>0?group.Key:"Без значения");rows.Add(heading);
                Add(rows,group.ToList(),fields,level+1);
            }
        }
    }
}
