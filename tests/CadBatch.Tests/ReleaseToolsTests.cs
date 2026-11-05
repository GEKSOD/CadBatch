using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CadBatch.Core;
using CadBatch.Pdf;

internal static partial class Program
{
    private static void RunReleaseToolsTests()
    {
        string folder=Path.Combine(TestRoot,"artifacts","tests","release-tools-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            List<DetectedFrame> Sheets()
            {
                var frames=Detect(Candidate("R1",0,0,420,297),Candidate("R2",500,0,420,297)).Frames.ToList();frames[0].Title="План";frames[1].Title="План";return frames;
            }
            Run("Reissue replaces a PDF with the same name, a normal run adds a number",()=>
            {
                var frames=Sheets();File.WriteAllText(Path.Combine(folder,"План.pdf"),"old");
                var normal=OutputPlanner.Build(frames,folder,"{Title}");Assert(normal[0].Path.EndsWith("План (2).pdf")&&normal[1].Path.EndsWith("План (3).pdf"));
                var reissue=OutputPlanner.Build(frames,folder,"{Title}",".pdf",true);Assert(reissue[0].Path==Path.Combine(folder,"План.pdf")&&reissue[1].Path.EndsWith("План (2).pdf"));
                File.WriteAllText(Path.Combine(folder,"Комплект.pdf"),"old");
                Assert(PdfGroupPlanner.Build(reissue,PdfOutputMode.OneFile,folder,"Комплект",true).Single().Path==Path.Combine(folder,"Комплект.pdf"));
                Assert(PdfGroupPlanner.Build(reissue,PdfOutputMode.OneFile,folder,"Комплект").Single().Path.EndsWith("Комплект (2).pdf"));
            });
            Run("A finished PDF replaces the previous file",()=>
            {
                string target=Path.Combine(folder,"replace.pdf"),source=Path.Combine(folder,"new.tmp");File.WriteAllText(target,"old");File.WriteAllText(source,"new");
                OutputFiles.MoveReplacing(source,target);Assert(File.ReadAllText(target)=="new"&&!File.Exists(source)&&!File.Exists(target+".cadbatch-old"));
            });
            Run("An open old PDF is not lost and the new one is kept beside it",()=>
            {
                string target=Path.Combine(folder,"open.pdf"),source=Path.Combine(folder,"open-new.tmp");File.WriteAllText(target,"old");File.WriteAllText(source,"new");
                using(new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))
                {
                    try{OutputFiles.MoveReplacing(source,target);throw new Exception("Expected IOException");}
                    catch(IOException ex){Assert(ex.Message.Contains("open (новый).pdf"));}
                }
                Assert(File.ReadAllText(target)=="old"&&File.ReadAllText(Path.Combine(folder,"open (новый).pdf"))=="new"&&!File.Exists(source));
            });
            Run("CRC32 matches the reference value",()=>{Assert(Crc32.Of(Encoding.ASCII.GetBytes("123456789"))=="CBF43926");string file=Path.Combine(folder,"crc.txt");File.WriteAllText(file,"123456789");Assert(Crc32.OfFile(file)=="CBF43926");});
            Run("ИУЛ is an A4 sheet with the checksum of the final file",()=>
            {
                string document=Path.Combine(folder,"АР-01.pdf");File.WriteAllBytes(document,Encoding.ASCII.GetBytes("123456789"));
                var sheet=IulSheet.Describe(document,"АР-01","План первого этажа","2",new IulSettings{Developer="Иванов",Checker="Петров"});
                Assert(sheet.Checksum=="CBF43926"&&sheet.Size==9&&sheet.Signers.Count==4&&sheet.Signers[0].Name=="Иванов");
                string iul=IulSheet.PathFor(document);Assert(iul.EndsWith("АР-01-УЛ.pdf"));
                TargetSheetPdf.WriteIul(sheet,iul);TargetSheetPdf.WriteIul(sheet,iul);
                using(var pdf=PdfSharp.Pdf.IO.PdfReader.Open(iul,PdfSharp.Pdf.IO.PdfDocumentOpenMode.Import))Assert(pdf.PageCount==1&&Math.Abs(pdf.Pages[0].Width.Millimeter-210)<0.5&&Math.Abs(pdf.Pages[0].Height.Millimeter-297)<0.5);
                Assert(IulSheet.Describe(document,"","","",null).Designation=="АР-01");
            });
            Run("Error journal and the diagnostics archive",()=>
            {
                string old=ErrorLog.Folder;ErrorLog.Folder=Path.Combine(folder,"logs");
                try
                {
                    ErrorLog.Write("Проверка",new InvalidOperationException("тестовая ошибка"));
                    var log=ErrorLog.Files().Single();Assert(File.ReadAllText(log).Contains("тестовая ошибка"));
                    string settings=Path.Combine(folder,"profile.json");File.WriteAllText(settings,"{}");string zip=Path.Combine(folder,"diag.zip");
                    ErrorLog.Collect(zip,ErrorLog.Summary("Проверка"),"сеанс",new[]{settings,Path.Combine(folder,"missing.json")});
                    using(var archive=ZipFile.OpenRead(zip)){var names=archive.Entries.Select(e=>e.FullName).ToList();Assert(names.Contains("Сведения.txt")&&names.Contains("Сеанс.txt")&&names.Any(n=>n.StartsWith("Журнал/"))&&names.Contains("Настройки/profile.json"));}
                }
                finally{ErrorLog.Folder=old;}
            });
        }
        finally{try{Directory.Delete(folder,true);}catch(IOException){}catch(UnauthorizedAccessException){}}
    }
}
