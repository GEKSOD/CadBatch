using System;
using System.Linq;
using System.Threading;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using CadBatch.Core;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        private static void InsertFrameTemplate(CadOperationRequest request,CadOperationResult result,CancellationToken cancellation)
        {
            if(request.Files.Length!=1||!Numeric.Positive(request.Scale))throw new ArgumentException("Выберите один DWG-шаблон и положительный масштаб вставки.");
            var doc=ActiveDocument();if(LayoutManager.Current.CurrentLayout!="Model")throw new InvalidOperationException("Переключитесь на вкладку «Модель».");
            using(var source=new Database(false,true))
            {
                source.ReadDwgFile(request.Files[0],FileOpenMode.OpenForReadAndAllShare,true,"");source.CloseInput(true);ObjectIdCollection ids;
                using(var tr=source.TransactionManager.StartOpenCloseTransaction())
                {
                    var bt=(BlockTable)tr.GetObject(source.BlockTableId,OpenMode.ForRead);var model=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForRead);ids=new ObjectIdCollection(model.Cast<ObjectId>().ToArray());
                }
                if(ids.Count==0)throw new ArgumentException("Модель DWG-шаблона пуста.");var point=AskPoint(doc,"Укажите точку вставки рамки из DWG-шаблона");if(!point.HasValue){result.Cancelled=true;return;}
                using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction())
                {
                    cancellation.ThrowIfCancellationRequested();var bt=(BlockTable)tr.GetObject(doc.Database.BlockTableId,OpenMode.ForWrite);var block=new BlockTableRecord{Name="CB_TEMPLATE_"+Guid.NewGuid().ToString("N")};bt.Add(block);tr.AddNewlyCreatedDBObject(block,true);
                    source.WblockCloneObjects(ids,block.ObjectId,new IdMapping(),DuplicateRecordCloning.MangleName,false);
                    var model=(BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace],OpenMode.ForWrite);var reference=new BlockReference(point.Value,block.ObjectId){ScaleFactors=new Scale3d(request.Scale)};model.AppendEntity(reference);tr.AddNewlyCreatedDBObject(reference,true);
                    foreach(ObjectId id in block)if(tr.GetObject(id,OpenMode.ForRead) is AttributeDefinition definition&&!definition.Constant)
                    {
                        var attribute=new AttributeReference();attribute.SetAttributeFromBlock(definition,reference.BlockTransform);attribute.TextString=definition.TextString;reference.AttributeCollection.AppendAttribute(attribute);tr.AddNewlyCreatedDBObject(attribute,true);
                    }
                    tr.Commit();result.Changed=1;result.Details.Add("Вставлена модель DWG-шаблона: "+request.Files[0]+". Точка отсчёта — начало координат шаблона. Геометрия формы и состав полей определяются вашим шаблоном. Шаблон не изменён.");
                }
                doc.Editor.Regen();
            }
        }
    }
}
