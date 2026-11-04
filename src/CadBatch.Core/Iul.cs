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
        public static void MoveReplacing(string source,string destination)
        {
            if(File.Exists(destination))
                try{File.Delete(destination);}
                catch(IOException){throw new IOException("Файл «"+Path.GetFileName(destination)+"» открыт в другой программе. Закройте его и повторите вывод.");}
                catch(UnauthorizedAccessException){throw new IOException("Файл «"+Path.GetFileName(destination)+"» защищён от записи. Выберите другую папку.");}
            File.Move(source,destination);
        }
    }
}
