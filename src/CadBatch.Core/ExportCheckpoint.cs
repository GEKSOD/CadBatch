using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;

namespace CadBatch.Core
{
    [DataContract] public sealed class ExportCheckpoint
    {
        [DataMember] public int Version {get;set;}=1;
        [DataMember] public BatchJob Job {get;set;}=new BatchJob();
        [DataMember] public bool ResumeAllowed {get;set;}=true;
        [DataMember] public Dictionary<string,string> Sources {get;set;}=new Dictionary<string,string>();
        [DataMember] public PdfOutputMode Mode {get;set;}
        [DataMember] public List<CheckpointOutput> Outputs {get;set;}=new List<CheckpointOutput>();
    }
    [DataContract] public sealed class CheckpointOutput
    {
        [DataMember] public string Path {get;set;}="";
        [DataMember] public List<int> Rows {get;set;}=new List<int>();
        [DataMember] public string Hash {get;set;}="";
        [DataMember] public long Length {get;set;}
    }
    public static class CheckpointStore
    {
        public static ExportCheckpoint Create(ProjectProfile profile,IReadOnlyList<DetectedFrame> frames,IEnumerable<PdfGroup> groups,PdfOutputMode mode)
        {
            var c=new ExportCheckpoint{Job=JobStore.Capture(ProfileStore.Clone(profile),frames),Mode=mode};
            foreach(var group in groups)c.Outputs.Add(new CheckpointOutput{Path=System.IO.Path.GetFullPath(group.Path),Rows=group.Items.Select(i=>Enumerable.Range(0,frames.Count).Single(n=>ReferenceEquals(frames[n],i.Frame))).ToList()});
            c.ResumeAllowed=!frames.Any(f=>f.Source.DrawingModified||f.Source.Dependencies.Any(p=>!File.Exists(p)));
            foreach(string path in frames.SelectMany(f=>new[]{f.Source.DocumentPath}.Concat(f.Source.Dependencies)).Distinct(StringComparer.OrdinalIgnoreCase))if(File.Exists(path))c.Sources[path]=Hash(path);
            Validate(c);return c;
        }
        private static string Hash(string path){using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
        public static void Complete(ExportCheckpoint c,string path){var o=c.Outputs.Single(x=>string.Equals(x.Path,path,StringComparison.OrdinalIgnoreCase));o.Length=new FileInfo(path).Length;o.Hash=Hash(path);}
        public static bool IsComplete(CheckpointOutput o)=>o.Hash.Length>0&&File.Exists(o.Path)&&new FileInfo(o.Path).Length==o.Length&&Hash(o.Path)==o.Hash;
        public static List<PdfGroup> Pending(ExportCheckpoint c,IReadOnlyList<DetectedFrame> frames)
        {
            Validate(c);
            if(!c.ResumeAllowed||frames.Any(f=>f.Source.DrawingModified))throw new ArgumentException("В DWG есть несохранённые изменения объектов. Сохраните исходные DWG и создайте новое задание для возобновляемого вывода.");
            if(c.Sources.Any(s=>!File.Exists(s.Key)||Hash(s.Key)!=s.Value))throw new ArgumentException("Исходный DWG изменён после начала задания. Создайте новое задание, чтобы не смешивать разные версии чертежей.");
            if(frames.Count!=c.Job.Rows.Count)throw new ArgumentException("Не совпадает число строк задания.");
            var result=new List<PdfGroup>();
            foreach(var o in c.Outputs)
            {
                if(IsComplete(o))continue;
                if(o.Rows.Any(r=>!frames[r].Available))throw new ArgumentException("Исходная геометрия изменилась или недоступна. Создайте новое задание.");
                if(File.Exists(o.Path)||Directory.Exists(o.Path))throw new ArgumentException("Результат не совпадает с журналом: "+o.Path+". Переместите его в другую папку и повторите возобновление.");
                result.Add(new PdfGroup{Path=o.Path,Items=o.Rows.Select(r=>new OutputItem{Frame=frames[r],Path=o.Path}).ToList()});
            }return result;
        }
        private static void Validate(ExportCheckpoint c)
        {
            if(c==null||c.Version!=1||c.Job==null||c.Outputs==null||c.Sources==null||c.Job.Rows==null||!Enum.IsDefined(typeof(PdfOutputMode),c.Mode)||c.Outputs.Any(o=>o==null||!System.IO.Path.IsPathRooted(o.Path)||o.Hash==null||o.Rows==null||o.Rows.Count==0||o.Rows.Any(r=>r<0||r>=c.Job.Rows.Count)))throw new ArgumentException("Повреждён журнал вывода.");
            var rows=c.Outputs.SelectMany(o=>o.Rows).ToArray();if(rows.Distinct().Count()!=rows.Length||c.Outputs.Select(o=>o.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=c.Outputs.Count)throw new ArgumentException("Журнал содержит повторные страницы или пути.");
        }
        public static void Save(string path,ExportCheckpoint c)
        {
            Validate(c);string full=System.IO.Path.GetFullPath(path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);string temp=full+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{using(var stream=File.Create(temp)){new DataContractJsonSerializer(typeof(ExportCheckpoint)).WriteObject(stream,c);stream.Flush(true);}if(File.Exists(full))File.Replace(temp,full,full+".bak");else File.Move(temp,full);}finally{if(File.Exists(temp))File.Delete(temp);}
        }
        public static ExportCheckpoint Load(string path){using(var stream=File.OpenRead(path)){var c=(ExportCheckpoint)new DataContractJsonSerializer(typeof(ExportCheckpoint)).ReadObject(stream)!;Validate(c);c.Job.Profile.Validate();return c;}}
    }
}
