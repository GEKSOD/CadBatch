using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;

namespace CadBatch.Core
{
    // Objects the user selected in AutoCAD: the search looks only at them (and inside the selected blocks), so of forty
    // sheets only the chosen ones are found and saved.
    [DataContract]
    public sealed class SearchSelection
    {
        [DataMember] public string DocumentPath {get;set;}="";
        [DataMember] public string Layout {get;set;}="";
        [DataMember] public List<string> Handles {get;set;}=new List<string>();
        public const int MaxObjects=500000;
        public void Validate()
        {
            if(string.IsNullOrWhiteSpace(DocumentPath)||string.IsNullOrWhiteSpace(Layout))throw new ArgumentException("Выделение не относится к чертежу.");
            if(Handles==null||Handles.Count==0)throw new ArgumentException("В AutoCAD ничего не выделено.");
            if(Handles.Count>MaxObjects)throw new ArgumentException("Выделено слишком много объектов. Выделите только листы.");
            if(Handles.Any(h=>string.IsNullOrEmpty(h)||h.Length>16||!long.TryParse(h,NumberStyles.HexNumber,CultureInfo.InvariantCulture,out _)))throw new ArgumentException("Повреждён список выделенных объектов.");
        }
        public HashSet<string> HandleSet()=>new HashSet<string>(Handles,StringComparer.OrdinalIgnoreCase);
    }
}
