using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.PlottingServices;
using CadBatch.Core;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost
    {
        public async Task<string[]> GetLayoutNamesAsync()
        {
            string[] names=Array.Empty<string>();await InContext(()=>{var doc=ActiveDocument();using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction())names=((DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId,OpenMode.ForRead)).Cast<DBDictionaryEntry>().Select(e=>(Layout)tr.GetObject(e.Value,OpenMode.ForRead)).Where(l=>!l.ModelType).OrderBy(l=>l.TabOrder).Select(l=>l.LayoutName).ToArray();});return names;
        }
        private static void LayoutOperation(CadOperationRequest request,CadOperationResult result,CancellationToken cancellation)
        {
            var doc=ActiveDocument();using(doc.LockDocument())
            {
                if(request.Operation==CadOperation.WriteDsd||request.Operation==CadOperation.PublishDsd){PublishLayouts(doc,request,result,cancellation);return;}
                if(request.Files.Length==0)throw new ArgumentException("Выберите листы Layout.");
                using(var tr=doc.Database.TransactionManager.StartTransaction())
                {
                    var dictionary=(DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId,OpenMode.ForRead);
                    foreach(string name in request.Files)if(!dictionary.Contains(name)||((Layout)tr.GetObject(dictionary.GetAt(name),OpenMode.ForRead)).ModelType)throw new ArgumentException("Лист не найден: "+name);
                }
                if(request.Operation==CadOperation.RenameLayouts)
                {
                    using var undo=new UndoGroup(doc);
                    string[] names=request.Files.Select((name,i)=>request.NamePattern.Replace("{Index}",(i+1).ToString()).Replace("{Name}",name)).ToArray();
                    if(names.Any(n=>string.IsNullOrWhiteSpace(n)||n.Length>200||n.IndexOfAny("<>/\\\":;?*|=,`".ToCharArray())>=0)||names.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=names.Length)throw new ArgumentException("Повторные или недопустимые имена листов.");
                    using(var tr=doc.Database.TransactionManager.StartTransaction())
                    {
                        var dictionary=(DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId,OpenMode.ForRead);var ids=request.Files.Select(n=>dictionary.GetAt(n)).ToArray();
                        foreach(string name in names)if(dictionary.Contains(name)&&!ids.Contains(dictionary.GetAt(name)))throw new ArgumentException("Лист уже существует: "+name);
                        // Temporary names permit swapping names without collisions; the transaction is atomic.
                        var temp=request.Files.Select(n=>"CB_RENAME_"+Guid.NewGuid().ToString("N")).ToArray();
                        for(int i=0;i<temp.Length;i++)LayoutManager.Current.RenameLayout(request.Files[i],temp[i]);
                        for(int i=0;i<temp.Length;i++){LayoutManager.Current.RenameLayout(temp[i],names[i]);result.Details.Add(request.Files[i]+" → "+names[i]);}
                        tr.Commit();result.Changed=names.Length;
                    }return;
                }
                if(request.Operation==CadOperation.ReorderLayouts)
                {
                    using var undo=new UndoGroup(doc);
                    using(var tr=doc.Database.TransactionManager.StartTransaction())
                    {
                        var dict=(DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId,OpenMode.ForRead);var all=dict.Cast<DBDictionaryEntry>().Select(e=>(Layout)tr.GetObject(e.Value,OpenMode.ForRead)).Where(l=>!l.ModelType).OrderBy(l=>l.TabOrder).ToList();
                        var order=request.Files.Concat(all.Select(l=>l.LayoutName).Where(n=>!request.Files.Contains(n,StringComparer.OrdinalIgnoreCase))).ToArray();
                        for(int i=0;i<order.Length;i++){var layout=(Layout)tr.GetObject(dict.GetAt(order[i]),OpenMode.ForWrite);layout.TabOrder=i+1;}tr.Commit();result.Changed=order.Length;result.Details.Add("Порядок: "+string.Join(" → ",order));
                    }return;
                }
                if(request.Operation!=CadOperation.ExportLayouts)throw new NotSupportedException("Неизвестная операция листов.");
                foreach(string name in request.Files)
                {
                    cancellation.ThrowIfCancellationRequested();string output=UniqueFile(request.Folder,Path.GetFileNameWithoutExtension(doc.Name)+"_"+name,".dwg");string temp=output+"."+Guid.NewGuid().ToString("N")+".tmp.dwg";
                    try
                    {
                        doc.Database.ForceWblockDatabaseCopy();using(var copy=doc.Database.Wblock())
                        {
                            var previous=HostApplicationServices.WorkingDatabase;
                            try
                            {
                                HostApplicationServices.WorkingDatabase=copy;var manager=LayoutManager.Current;manager.CurrentLayout=name;
                                string[] remove;using(var tr=copy.TransactionManager.StartTransaction())remove=((DBDictionary)tr.GetObject(copy.LayoutDictionaryId,OpenMode.ForRead)).Cast<DBDictionaryEntry>().Select(e=>(Layout)tr.GetObject(e.Value,OpenMode.ForRead)).Where(l=>!l.ModelType&&l.LayoutName!=name).Select(l=>l.LayoutName).ToArray();
                                foreach(string other in remove)manager.DeleteLayout(other);
                                copy.SaveAs(temp,DwgVersion.AC1032);
                            }finally{HostApplicationServices.WorkingDatabase=previous;}
                        }
                        File.Move(temp,output);result.Files.Add(output);result.Changed++;
                    }finally{if(File.Exists(temp))File.Delete(temp);}
                }
                result.Details.Add("Экспорт: вся модель и один выбранный Layout в каждом DWG. Viewport сохранены. Внешние зависимости остаются ссылками; для переноса используйте упаковку DWG.");
            }
        }
        private static void PublishLayouts(Document doc,CadOperationRequest request,CadOperationResult result,CancellationToken cancellation)
        {
            if(request.Files.Length==0)throw new ArgumentException("Выберите Layout для публикации.");
            if(!File.Exists(doc.Name)||(Convert.ToInt32(AcApp.GetSystemVariable("DBMOD"))&1)!=0)throw new InvalidOperationException("Перед созданием DSD сохраните исходный DWG в AutoCAD. Публикация использует сохранённый файл.");
            string output=UniqueFile(request.Folder,request.NamePattern,".pdf"),dsd=UniqueFile(request.Folder,request.NamePattern,".dsd");
            using(var entries=new DsdEntryCollection())using(var data=new DsdData())
            {
                using(var tr=doc.Database.TransactionManager.StartTransaction())
                {
                    var dictionary=(DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId,OpenMode.ForRead);
                    foreach(string name in request.Files)
                    {
                        cancellation.ThrowIfCancellationRequested();if(!dictionary.Contains(name))throw new ArgumentException("Лист удалён: "+name);
                        var layout=(Layout)tr.GetObject(dictionary.GetAt(name),OpenMode.ForRead);if(layout.ModelType)throw new ArgumentException("Для DSD выберите бумажные Layout.");
                        using(var entry=new DsdEntry{DwgName=doc.Name,Layout=name,Title=name,Nps="",NpsSourceDwg=""})entries.Add(entry);
                    }
                }
                data.SetDsdEntryCollection(entries);data.ProjectPath=request.Folder;data.DestinationName=output;data.SheetType=SheetType.MultiPdf;data.NoOfCopies=1;data.IsHomogeneous=false;data.LogFilePath=dsd+".log";
                data.SetUnrecognizedData("PromptForDwfName","FALSE");data.WriteDsd(dsd);result.Files.Add(dsd);
                if(request.Operation==CadOperation.WriteDsd){result.Details.Add("DSD создан. Откройте в AutoCAD команду PUBLISH и загрузите этот DSD. Параметры страниц берутся из сохранённых Layout; в окне PUBLISH можно включить фоновую публикацию.");return;}
                object old=AcApp.GetSystemVariable("BACKGROUNDPLOT");
                try
                {
                    AcApp.SetSystemVariable("BACKGROUNDPLOT",0);cancellation.ThrowIfCancellationRequested();
                    using(var config=PlotConfigManager.SetCurrentConfig(request.Device))AcApp.Publisher.PublishExecute(data,config);
                    if(!File.Exists(output)||new FileInfo(output).Length==0)throw new InvalidOperationException("AutoCAD не создал PDF. Проверьте журнал публикации: "+data.LogFilePath);
                    result.Files.Add(output);result.Changed=request.Files.Length;result.Details.Add("Публикация завершена через штатный Publisher AutoCAD. Геометрия и настройки — из сохранённых Layout.");
                }finally{AcApp.SetSystemVariable("BACKGROUNDPLOT",old);}
            }
        }
    }
}
