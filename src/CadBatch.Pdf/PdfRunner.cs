using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using CadBatch.Core;

namespace CadBatch.Pdf
{
    // One PDF operation, serializable so that it can run in the separate worker process.
    [DataContract]
    public sealed class PdfJob
    {
        [DataMember] public string Operation {get;set;}="";
        [DataMember] public string Source {get;set;}="";
        [DataMember] public string Destination {get;set;}="";
        [DataMember] public string[] Sources {get;set;}=new string[0];
        [DataMember] public string[] Titles {get;set;}=new string[0];
        [DataMember] public string Title {get;set;}="";
        [DataMember] public double Width {get;set;}
        [DataMember] public double Height {get;set;}
        [DataMember] public double Scale {get;set;}
        [DataMember] public int[] Copies {get;set;}=new int[0];
        [DataMember] public string Designation {get;set;}="";
        [DataMember] public string Revision {get;set;}="";
        [DataMember] public string[] Signers {get;set;}=new string[0];
        [DataMember] public double Margin {get;set;}
        [DataMember] public double Overlap {get;set;}
        public static PdfJob Restore(string source,string destination,double width,double height,double scale)=>new PdfJob{Operation="restore",Source=source,Destination=destination,Width=width,Height=height,Scale=scale};
        // Size of the source page (written as "width height" in mm) and the PDF for gluing: in the worker when AutoCAD
        // has an incompatible PDF library, like every other PDF operation.
        public static PdfJob Size(string source,string destination)=>new PdfJob{Operation="size",Source=source,Destination=destination};
        public static PdfJob Tile(string source,string destination,double sheetWidth,double sheetHeight,double margin,double overlap,double scale)=>new PdfJob{Operation="tile",Source=source,Destination=destination,Width=sheetWidth,Height=sheetHeight,Margin=margin,Overlap=overlap,Scale=scale};
        public static PdfJob Crop(string source,string destination,double width,double height)=>new PdfJob{Operation="crop",Source=source,Destination=destination,Width=width,Height=height};
        public static PdfJob Merge(string[] sources,string destination,string[] titles,string title)=>new PdfJob{Operation="merge",Sources=sources,Destination=destination,Titles=titles,Title=title};
        public static PdfJob Repeat(string path,int[] copies,string[]? titles)=>new PdfJob{Operation="repeat",Source=path,Copies=copies,Titles=titles??new string[0]};
        public static PdfJob Iul(string document,string destination,string designation,string title,string revision,IulSettings? settings)
        {
            var s=settings??new IulSettings();
            return new PdfJob{Operation="iul",Source=document,Destination=destination,Designation=designation,Title=title,Revision=revision,Signers=new[]{s.Developer,s.Checker,s.NormControl,s.Chief}};
        }
    }
    // Runs PDF work in the current process; if the host process (AutoCAD) has bound an incompatible version of a
    // PDF dependency — another plug-in or the host itself redirecting Microsoft.Extensions.Logging, for example —
    // the work moves to CadBatch.PdfWorker.exe, a separate process with its own libraries, for the rest of the session.
    public static class PdfRunner
    {
        public static string? WorkerPath {get;set;}
        public static bool ForceWorker {get;set;}
        private static bool isolated;
        public static bool Isolated=>isolated||ForceWorker;
        public static void Run(PdfJob job)
        {
            if(!Isolated)
            {
                try{Execute(job);return;}
                catch(Exception ex) when(IsConflict(ex)&&WorkerAvailable)
                {
                    isolated=true;ErrorLog.Write("PDF: в процессе несовместимая версия библиотеки, обработка PDF переносится в отдельный процесс",ex);
                }
            }
            RunWorker(job);
        }
        public static bool WorkerAvailable=>WorkerPath!=null&&File.Exists(WorkerPath);
        // Assembly binding problems surface as these types, often wrapped in a type initializer failure.
        public static bool IsConflict(Exception error)
        {
            for(var e=error;e!=null;e=e.InnerException)
                if(e is TypeLoadException||e is FileLoadException||e is MissingMethodException||e is MissingFieldException||e is BadImageFormatException||(e is FileNotFoundException f&&(f.FileName??"").Contains("Version=")))return true;
            return false;
        }
        public static void Execute(PdfJob job)
        {
            switch(job.Operation)
            {
                case "restore":TargetSheetPdf.RestoreCentered(job.Source,job.Destination,job.Width,job.Height,job.Scale);break;
                case "crop":TargetSheetPdf.CropCentered(job.Source,job.Destination,job.Width,job.Height);break;
                case "merge":TargetSheetPdf.Merge(job.Sources,job.Destination,job.Titles,job.Title.Length>0?job.Title:null);break;
                case "repeat":TargetSheetPdf.RepeatPages(job.Source,job.Copies,job.Titles.Length>0?job.Titles:null);break;
                case "size":TargetSheetPdf.WriteSourceSize(job.Source,job.Destination);break;
                case "tile":TargetSheetPdf.Tile(job.Source,job.Destination,TargetSheetPdf.PlanTiles(job.Source,job.Width,job.Height,job.Margin,job.Overlap,job.Scale));break;
                case "iul":
                    var signers=job.Signers.Concat(Enumerable.Repeat("",4)).Take(4).ToArray();
                    TargetSheetPdf.WriteIul(IulSheet.Describe(job.Source,job.Designation,job.Title,job.Revision,new IulSettings{Developer=signers[0],Checker=signers[1],NormControl=signers[2],Chief=signers[3]}),job.Destination);break;
                default:throw new ArgumentException("Неизвестная операция PDF: "+job.Operation);
            }
        }
        private static void RunWorker(PdfJob job)
        {
            if(!WorkerAvailable)throw new InvalidOperationException("Не найден модуль обработки PDF (CadBatch.PdfWorker.exe). Переустановите CAD Batch.");
            string folder=Path.Combine(Path.GetTempPath(),"CadBatch-pdf");Directory.CreateDirectory(folder);
            string file=Path.Combine(folder,Guid.NewGuid().ToString("N")+".json"),error=file+".error.txt";
            try
            {
                using(var output=File.Create(file))new DataContractJsonSerializer(typeof(PdfJob)).WriteObject(output,job);
                using(var process=Process.Start(new ProcessStartInfo(WorkerPath!,"--job \""+file+"\""){UseShellExecute=false,CreateNoWindow=true})!)
                {
                    if(!process.WaitForExit(10*60*1000)){try{process.Kill();}catch(InvalidOperationException){}throw new TimeoutException("Обработка PDF не завершилась за 10 минут.");}
                    if(process.ExitCode!=0)throw new IOException(File.Exists(error)?File.ReadAllText(error).Trim():"Обработка PDF завершилась с ошибкой "+process.ExitCode+".");
                }
            }
            finally{foreach(string f in new[]{file,error})try{if(File.Exists(f))File.Delete(f);}catch(IOException){}}
        }
        // Worker entry point: CadBatch.PdfWorker.exe --job <file>.
        public static int WorkerMain(string[] args)
        {
            if(args.Length!=2||args[0]!="--job")return 2;
            try
            {
                PdfJob job;using(var input=File.OpenRead(args[1]))job=(PdfJob)new DataContractJsonSerializer(typeof(PdfJob)).ReadObject(input)!;
                Execute(job);return 0;
            }
            catch(Exception ex){try{File.WriteAllText(args[1]+".error.txt",ex.Message);}catch(IOException){}ErrorLog.Write("Модуль обработки PDF",ex);return 1;}
        }
    }
}
