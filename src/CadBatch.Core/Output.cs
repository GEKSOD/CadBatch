using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Text.RegularExpressions;

namespace CadBatch.Core
{
    public sealed class OutputItem
    {
        public DetectedFrame Frame { get; set; } = null!;
        public string Path { get; set; } = "";
    }
    public sealed class ExportRequest
    {
        public List<OutputItem> Items { get; set; } = new List<OutputItem>();
        public string Device { get; set; } = "DWG To PDF.pc3";
        public string StyleSheet { get; set; } = "";
        public bool FitToPaper { get; set; }
        public PrintColorMode ColorMode { get; set; } = PrintColorMode.DrawingColors;
        public PdfOutputMode PdfMode { get; set; }
        public Action<string>? OutputCompleted {get;set;}
        public List<PdfGroup> Groups { get; set; } = new List<PdfGroup>();
        public bool PhysicalPrint { get; set; }
        public string Media {get;set;}="";
        public bool UpsideDown {get;set;}
        public double OffsetX {get;set;}
        public double OffsetY {get;set;}
    }
    public sealed class ExportReport
    {
        public bool PhysicalPrint {get;set;}
        public int Queued {get;set;}
        public List<string> Files { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
        public List<string> Details { get; } = new List<string>();
        public bool Cancelled { get; set; }
    }
    public sealed class BatchProgress
    {
        public BatchProgress(int done,int total,string text) { Done=done; Total=total; Text=text; }
        public int Done { get; }
        public int Total { get; }
        public string Text { get; }
    }
    public static class OutputPlanner
    {
        private static readonly Regex Tokens = new Regex(@"\{([^{}]+)\}",RegexOptions.CultureInvariant);
        public static List<OutputItem> Build(IEnumerable<DetectedFrame> frames,string folder,string pattern,string extension=".pdf")
        {
            if (string.IsNullOrWhiteSpace(folder) || !System.IO.Path.IsPathRooted(folder)) throw new ArgumentException("Выберите абсолютный путь к папке PDF.");
            if (string.IsNullOrWhiteSpace(pattern)) throw new ArgumentException("Укажите шаблон имени файла.");
            folder = System.IO.Path.GetFullPath(folder);
            if(extension!=".pdf" && extension!=".dwg")throw new ArgumentException("Неподдерживаемое расширение результата.");
            var occupied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<OutputItem>();
            foreach (var frame in frames.Where(f=>f.Enabled))
            {
                if(!frame.Available)throw new ArgumentException("Рамка недоступна: "+frame.File+" / "+frame.Source.HandlePath+". Откройте источник и повторите поиск.");
                var values = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Document"]=System.IO.Path.GetFileNameWithoutExtension(frame.File), ["Layout"]=frame.Layout,
                    ["Page"]=frame.Page.ToString("000"), ["SheetNumber"]=frame.SheetNumber, ["Title"]=frame.Title,
                    ["Code"]=frame.Code, ["Revision"]=frame.Revision, ["Format"]=frame.FormatName, ["Layer"]=frame.Layer
                };
                foreach(var extra in frame.Metadata)if(!values.ContainsKey(extra.Key))values[extra.Key]=extra.Value;
                string name = Tokens.Replace(pattern,m => values.TryGetValue(m.Groups[1].Value,out var v) ? v : throw new ArgumentException("Неизвестное поле имени: " + m.Value));
                if (name.Contains("{") || name.Contains("}")) throw new ArgumentException("Незакрытое поле в шаблоне имени.");
                name = Sanitize(name);
                if (name.Length > 140) name = name.Substring(0,140).TrimEnd(' ','.');
                string candidate = System.IO.Path.Combine(folder,name+extension); int suffix = 2;
                while (occupied.Contains(candidate) || File.Exists(candidate) || Directory.Exists(candidate))
                    candidate = System.IO.Path.Combine(folder,name+" ("+(suffix++)+")"+extension);
                if (candidate.Length > 240) throw new ArgumentException("Путь слишком длинный: выберите более короткую папку или имя.");
                occupied.Add(candidate);
                result.Add(new OutputItem { Frame=frame,Path=candidate });
            }
            if (result.Count == 0) throw new ArgumentException("Не отмечено ни одной рамки для вывода.");
            return result;
        }
        public static string Sanitize(string value)
        {
            string name = Regex.Replace(value,@"[<>:""/\\|?*\x00-\x1f]","_").Trim().TrimEnd(' ','.');
            if (string.IsNullOrWhiteSpace(name)) name="Чертеж";
            var stem=name.Split('.')[0];
            if (Regex.IsMatch(stem,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$",RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) name="_"+name;
            return name;
        }
    }

    public static class ProfileStore
    {
        public static ProjectProfile Clone(ProjectProfile profile)
        {
            using(var stream=new MemoryStream()){var serializer=new DataContractJsonSerializer(typeof(ProjectProfile));serializer.WriteObject(stream,profile);stream.Position=0;return (ProjectProfile)serializer.ReadObject(stream)!;}
        }
        public static ProjectProfile Load(string path)
        {
            using (var input=File.OpenRead(path))
            {
                var profile=(ProjectProfile?)new DataContractJsonSerializer(typeof(ProjectProfile)).ReadObject(input);
                if(profile==null) throw new ArgumentException("Пустой профиль.");
                profile.UpgradeCatalog();profile.Validate(); return profile;
            }
        }
        public static void Save(string path,ProjectProfile profile)
        {
            profile.Validate();
            string full=Path.GetFullPath(path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            string temporary=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var output=File.Create(temporary))
                { new DataContractJsonSerializer(typeof(ProjectProfile)).WriteObject(output,profile); output.Flush(true); }
                if(File.Exists(full)) File.Replace(temporary,full,full+".bak");
                else File.Move(temporary,full);
            }
            finally { if(File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
