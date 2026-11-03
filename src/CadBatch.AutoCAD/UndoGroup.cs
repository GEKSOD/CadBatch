using System;
using Autodesk.AutoCAD.ApplicationServices;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private sealed class UndoGroup : IDisposable
        {
            private readonly Document doc;private readonly bool started;
            public UndoGroup(Document doc)
            {
                this.doc=doc;int control=Convert.ToInt32(AcApp.GetSystemVariable("UNDOCTL"));
                if((control&1)!=0&&(control&8)==0){doc.Editor.Command("_.UNDO","_Begin");started=true;}
            }
            public void Dispose(){if(started)doc.Editor.Command("_.UNDO","_End");}
        }
        private static void WithUndo(Action action){var doc=ActiveDocument();using(doc.LockDocument())using(var group=new UndoGroup(doc))action();}
    }
}
