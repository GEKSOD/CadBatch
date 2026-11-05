using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace CadBatch.Core
{
    // Information and certification sheet (ИУЛ) per ГОСТ Р 21.101-2020: one sheet per electronic document,
    // with the file checksum, so the issued PDF can be verified by an expert review.
    [DataContract] public sealed class IulSettings
    {
        [DataMember] public bool Enabled {get;set;}
        [DataMember] public string Developer {get;set;}="";
        [DataMember] public string Checker {get;set;}="";
        [DataMember] public string NormControl {get;set;}="";
        [DataMember] public string Chief {get;set;}="";
        public IulSettings Copy()=>(IulSettings)MemberwiseClone();
        public List<(string Role,string Name)> Signers()=>new List<(string,string)>{("Разработал",Developer),("Проверил",Checker),("Н. контроль",NormControl),("ГИП",Chief)};
    }
    public sealed class IulSheet
    {
        public string Designation {get;set;}="";
        public string Title {get;set;}="";
        public string Revision {get;set;}="";
        public string FileName {get;set;}="";
        public DateTime Modified {get;set;}
        public long Size {get;set;}
        public string Checksum {get;set;}="";
        public string Algorithm {get;set;}="CRC32";
        public List<(string Role,string Name)> Signers {get;set;}=new List<(string,string)>();
        public static IulSheet Describe(string path,string designation,string title,string revision,IulSettings? settings)
        {
            var info=new FileInfo(path);if(!info.Exists)throw new FileNotFoundException("Файл для ИУЛ не найден.",path);
            return new IulSheet{Designation=designation.Trim().Length>0?designation.Trim():Path.GetFileNameWithoutExtension(path),Title=title.Trim().Length>0?title.Trim():Path.GetFileNameWithoutExtension(path),
                Revision=revision.Trim().Length>0?revision.Trim():"—",FileName=info.Name,Modified=info.LastWriteTime,Size=info.Length,Checksum=Crc32.OfFile(path),Signers=(settings??new IulSettings()).Signers()};
        }
        // "Проект.pdf" → "Проект-УЛ.pdf" next to it.
        public static string PathFor(string document)=>Path.Combine(Path.GetDirectoryName(Path.GetFullPath(document))!,Path.GetFileNameWithoutExtension(document)+"-УЛ.pdf");
        public string ModifiedText=>Modified.ToString("dd.MM.yyyy HH:mm",CultureInfo.InvariantCulture);
    }
    public static class Crc32
    {
        private static readonly uint[] Table=Enumerable.Range(0,256).Select(n=>{uint c=(uint)n;for(int k=0;k<8;k++)c=(c&1)!=0?0xEDB88320u^(c>>1):c>>1;return c;}).ToArray();
        public static string Of(byte[] data){uint crc=Update(0xFFFFFFFFu,data,data.Length);return (crc^0xFFFFFFFFu).ToString("X8",CultureInfo.InvariantCulture);}
        public static string OfFile(string path)
        {
            uint crc=0xFFFFFFFFu;var buffer=new byte[81920];
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){int read;while((read=stream.Read(buffer,0,buffer.Length))>0)crc=Update(crc,buffer,read);}
            return (crc^0xFFFFFFFFu).ToString("X8",CultureInfo.InvariantCulture);
        }
        private static uint Update(uint crc,byte[] data,int count){for(int i=0;i<count;i++)crc=Table[(crc^data[i])&0xFF]^(crc>>8);return crc;}
    }
    public static class OutputFiles
    {
        // Moves a finished PDF into place. With "replace on reissue" the old file is removed first;
        // a file held open by a viewer gives a clear message instead of a system error.
        // Puts a finished file in place of an older one without ever losing both: the old file is set aside first (an open
        // or protected old file stays untouched, and the new one is kept beside it), restored if the new one cannot be
        // moved, and removed only after the replacement succeeded.
        public static void MoveReplacing(string source,string destination)
        {
            if(!File.Exists(destination)){File.Move(source,destination);return;}
            string aside=destination+".cadbatch-old";
            try{if(File.Exists(aside))File.Delete(aside);File.Move(destination,aside);}
            catch(IOException){Keep(source,destination,"открыт в другой программе");}
            catch(UnauthorizedAccessException){Keep(source,destination,"защищён от записи");}
            try{File.Move(source,destination);}
            catch{try{if(!File.Exists(destination))File.Move(aside,destination);}catch(IOException){}catch(UnauthorizedAccessException){}throw;}
            try{File.Delete(aside);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        private static void Keep(string source,string destination,string reason)
        {
            string folder=Path.GetDirectoryName(destination)??"",name=Path.GetFileNameWithoutExtension(destination),extension=Path.GetExtension(destination);
            string kept=Path.Combine(folder,name+" (новый)"+extension);for(int i=2;File.Exists(kept);i++)kept=Path.Combine(folder,name+" (новый "+i+")"+extension);
            try{File.Move(source,kept);}catch(IOException){kept="";}catch(UnauthorizedAccessException){kept="";}
            throw new IOException("Файл «"+Path.GetFileName(destination)+"» "+reason+", он не заменён."+(kept.Length>0?" Новый PDF сохранён рядом: «"+Path.GetFileName(kept)+"».":"")+" Закройте старый файл и повторите вывод.");
        }
    }
}
