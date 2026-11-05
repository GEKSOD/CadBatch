using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.DatabaseServices.Filters;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using CadBatch.Core;
using CadBatch.UI;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        public async Task<SearchRegion?> PickSearchRegionAsync()
        {
            SearchRegion? result=null;await InContext(()=>
            {
                using var interaction=CadInteraction.Begin();
                var doc=ActiveDocument();doc.Window.Focus();var a=doc.Editor.GetPoint("\nCAD Batch: первый угол области поиска: ");if(a.Status!=PromptStatus.OK)return;
                var b=doc.Editor.GetCorner(new PromptCornerOptions("\nCAD Batch: противоположный угол области: ",a.Value));if(b.Status!=PromptStatus.OK)return;
                var ucs=doc.Editor.CurrentUserCoordinateSystem;var corners=new[]{a.Value,new Point3d(b.Value.X,a.Value.Y,a.Value.Z),b.Value,new Point3d(a.Value.X,b.Value.Y,a.Value.Z)}.Select(p=>p.TransformBy(ucs)).ToArray();
                if(corners.Max(p=>p.Z)-corners.Min(p=>p.Z)>.01)throw new ArgumentException("Выберите область в плоскости XY.");
                result=new SearchRegion{DocumentPath=doc.Name,Layout=Autodesk.AutoCAD.ApplicationServices.Core.Application.GetSystemVariable("CTAB").ToString()??"Model",Corners=corners.Select(p=>new[]{p.X,p.Y}).ToArray()};result.Validate();
            });return result;
        }
        public async Task<SearchSelection?> PickSelectionAsync()
        {
            // The objects selected before the button was pressed: read in the application context, before a command
            // context can clear the pick-first set. Nothing selected: AutoCAD asks to select the sheets.
            ObjectId[] ids=Array.Empty<ObjectId>();var doc=ActiveDocument();
            try{var implied=doc.Editor.SelectImplied();if(implied.Status==PromptStatus.OK&&implied.Value!=null)ids=implied.Value.GetObjectIds();}
            catch(Autodesk.AutoCAD.Runtime.Exception){}
            SearchSelection? result=null;
            await InContext(()=>
            {
                doc=ActiveDocument();
                if(ids.Length==0||ids[0].Database!=doc.Database)
                {
                    ids=Array.Empty<ObjectId>();
                    var again=doc.Editor.SelectImplied();if(again.Status==PromptStatus.OK&&again.Value!=null)ids=again.Value.GetObjectIds();
                }
                if(ids.Length==0)
                {
                    using var interaction=CadInteraction.Begin();doc.Window.Focus();
                    var options=new PromptSelectionOptions{MessageForAdding="\nCAD Batch: выделите листы — рамки целиком, можно рамкой выбора — и нажмите Enter: ",MessageForRemoval="\nCAD Batch: уберите лишнее: "};
                    var picked=doc.Editor.GetSelection(options);if(picked.Status!=PromptStatus.OK||picked.Value==null)return;
                    ids=picked.Value.GetObjectIds();
                }
                // Selected objects of one tab; with objects of several tabs (selected through viewports) the tab with most.
                using(var tr=doc.Database.TransactionManager.StartOpenCloseTransaction())
                {
                    var byTab=new Dictionary<string,List<string>>(StringComparer.OrdinalIgnoreCase);
                    foreach(var id in ids)
                    {
                        if(id.IsErased||id.Database!=doc.Database)continue;
                        if(!(tr.GetObject(id,OpenMode.ForRead) is Entity entity))continue;
                        if(!(tr.GetObject(entity.OwnerId,OpenMode.ForRead) is BlockTableRecord owner)||!owner.IsLayout)continue;
                        string tab=((Layout)tr.GetObject(owner.LayoutId,OpenMode.ForRead)).LayoutName;
                        if(!byTab.TryGetValue(tab,out var handles))byTab[tab]=handles=new List<string>();
                        handles.Add(entity.Handle.ToString());
                    }
                    if(byTab.Count==0)return;
                    var best=byTab.OrderByDescending(p=>p.Value.Count).First();
                    result=new SearchSelection{DocumentPath=doc.Name,Layout=best.Key,Handles=best.Value};result.Validate();
                }
            });
            return result;
        }
        private sealed class ClipBoundary
        {
            public List<Point2> Points=new List<Point2>();public bool Inverted;
            public bool Allows(IReadOnlyList<Point2> frame)=>PolygonVisibility.FullyVisible(frame,Points,Inverted);
        }
        private static ClipBoundary? ReadClip(BlockReference block,Transaction tr,Matrix3d parent)
        {
            if(block.ExtensionDictionary.IsNull)return null;var ext=(DBDictionary)tr.GetObject(block.ExtensionDictionary,OpenMode.ForRead);
            if(!ext.Contains("ACAD_FILTER"))return null;var filters=(DBDictionary)tr.GetObject(ext.GetAt("ACAD_FILTER"),OpenMode.ForRead);if(!filters.Contains("SPATIAL"))throw new NotSupportedException("Неизвестный фильтр блока "+block.Handle);
            var filter=(SpatialFilter)tr.GetObject(filters.GetAt("SPATIAL"),OpenMode.ForRead);var definition=filter.Definition;if(!definition.Enabled)return null;
            if((definition.FrontClip>0&&definition.FrontClip<1e20)||(definition.BackClip>0&&definition.BackClip<1e20))throw new NotSupportedException("XCLIP с глубиной отсечения не поддерживается: "+block.Handle);
            if(filter.HasPerspectiveCamera||Math.Abs(Math.Abs(definition.Normal.Z)-1)>1e-8)throw new NotSupportedException("Наклонное или перспективное XCLIP не поддерживается: "+block.Handle);
            var points=definition.GetPoints().Cast<Point2d>().ToList();if(points.Count==2){var a=points[0];var b=points[1];points=new List<Point2d>{a,new Point2d(b.X,a.Y),b,new Point2d(a.X,b.Y)};}
            if(points.Count<3)throw new NotSupportedException("Недопустимая граница XCLIP: "+block.Handle);
            var matrix=parent*block.BlockTransform*filter.OriginalInverseBlockTransform*filter.ClipSpaceToWorldCoordinateSystemTransform;
            return new ClipBoundary{Inverted=filter.Inverted,Points=points.Select(p=>new Point3d(p.X,p.Y,0).TransformBy(matrix)).Select(p=>new Point2(p.X,p.Y)).ToList()};
        }
    }
}
