using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CadBatch.Core
{
    [DataContract] public sealed class WindowSettings
    {
        [DataMember] public double Width {get;set;}
        [DataMember] public double Height {get;set;}
        [DataMember] public bool Maximized {get;set;}
        [DataMember] public List<ColumnSettings> Columns {get;set;}=new List<ColumnSettings>();
    }
    [DataContract] public sealed class ColumnSettings
    {
        [DataMember] public string Name {get;set;}="";
        [DataMember] public bool Visible {get;set;}
        [DataMember] public double Width {get;set;}
        [DataMember] public int Order {get;set;}
    }
}
