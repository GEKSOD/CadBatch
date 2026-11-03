using System;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace CadBatch.UI
{
    // How the interface looks for this person: text size and simple/advanced mode.
    // Stored next to the profile, shared by the desktop application and the AutoCAD component.
    [DataContract]
    public sealed class UiPreferences
    {
        public static readonly double[] Scales={1,1.15,1.3};
        public static readonly string[] ScaleNames={"Обычный текст","Крупный текст","Очень крупный текст"};
        [DataMember] public double TextScale {get;set;}=1;
        [DataMember] public bool Advanced {get;set;}
        public static string DefaultPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"CadBatch","interface.json");
        public static UiPreferences Load(string path)
        {
            try
            {
                if(File.Exists(path))using(var input=File.OpenRead(path))
                {
                    var value=(UiPreferences?)new DataContractJsonSerializer(typeof(UiPreferences)).ReadObject(input);
                    if(value!=null){value.TextScale=Scales.OrderBy(x=>Math.Abs(x-value.TextScale)).First();return value;}
                }
            }
            catch(IOException){}catch(UnauthorizedAccessException){}catch(SerializationException){}
            return new UiPreferences();
        }
        public void Save(string path)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
                using(var output=File.Create(path))new DataContractJsonSerializer(typeof(UiPreferences)).WriteObject(output,this);
            }
            catch(IOException){}catch(UnauthorizedAccessException){}
        }
    }
}
