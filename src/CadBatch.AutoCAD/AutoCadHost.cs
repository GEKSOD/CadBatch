using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.PlottingServices;
using CadBatch.Core;
using CadBatch.UI;
using CadBatch.Pdf;
using AcApp=Autodesk.AutoCAD.ApplicationServices.Core.Application;
using AcException=Autodesk.AutoCAD.Runtime.Exception;

namespace CadBatch.AutoCAD
{
    public sealed partial class AutoCadHost : IBatchHost
    {
        public string Description=>"AutoCAD "+AcApp.GetSystemVariable("ACADVER")+"  •  .NET "+Environment.Version+"  •  версия "+ProductInfo.Version;
        public bool CanPlot=>true;
        public async Task<PlotCapabilities> GetPlotCapabilitiesAsync(string device)
        {
            var result=new PlotCapabilities();await InContext(()=>{using(var settings=new PlotSettings(true)){var validator=PlotSettingsValidator.Current;validator.SetPlotConfigurationName(settings,device,null);validator.RefreshLists(settings);result.Media=validator.GetCanonicalMediaNameList(settings).Cast<string>().ToArray();result.Styles=validator.GetPlotStyleSheetList().Cast<string>().ToArray();}});return result;
        }
        public async Task<string[]> GetPlotDevicesAsync()
        {
            string[] devices=Array.Empty<string>();await InContext(()=>devices=PlotSettingsValidator.Current.GetPlotDeviceList().Cast<string>().ToArray());return devices;
        }
        public async Task<ScanResult> SearchAsync(ProjectProfile profile,CancellationToken cancellation)
        {
            var originalActive=AcApp.DocumentManager.MdiActiveDocument;
            try{if(profile.Search.Files?.Count>0)
            {
                foreach(var path in profile.Search.Files)
                {
                    cancellation.ThrowIfCancellationRequested();
                    if(!File.Exists(path)||!path.EndsWith(".dwg",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("DWG не найден: "+path);
                    if(!AcApp.DocumentManager.Cast<Document>().Any(d=>string.Equals(d.Name,path,StringComparison.OrdinalIgnoreCase)))AcApp.DocumentManager.Open(path,true);
                }
            }
            }finally{if(originalActive!=null)AcApp.DocumentManager.MdiActiveDocument=originalActive;}
            var dependencies=new Dictionary<string,List<string>>();
            var texts=new List<DrawingText>();var candidates=new List<FrameCandidate>();var stampGeometry=new List<StampSegment>();var lineGroups=new List<LineScanGroup>();var warnings=new List<string>();int visited=0;
            await InContext(()=>
            {
                var active=AcApp.DocumentManager.MdiActiveDocument;
                if(active==null)throw new InvalidOperationException("Откройте чертёж в AutoCAD.");
                var documents=new List<Document>();
                if(profile.Search.Files?.Count>0)documents.AddRange(AcApp.DocumentManager.Cast<Document>().Where(d=>profile.Search.Files.Contains(d.Name,StringComparer.OrdinalIgnoreCase)));
                else if(profile.Search.AllOpenDocuments)foreach(Document d in AcApp.DocumentManager)documents.Add(d);
                else documents.Add(active);
                try
                {
                foreach(var doc in documents)
                {
                    cancellation.ThrowIfCancellationRequested();
                    AcApp.DocumentManager.MdiActiveDocument=doc;
                    using(doc.LockDocument())dependencies[DocumentKey(doc)]=DependencySnapshot(doc.Database,doc.Name);
                    using(doc.LockDocument())using(var tr=doc.Database.TransactionManager.StartTransaction())
                    {
                        var layouts=(DBDictionary)tr.GetObject(doc.Database.LayoutDictionaryId,OpenMode.ForRead);
                        foreach(DBDictionaryEntry entry in layouts)
                        {
                            var layout=(Layout)tr.GetObject(entry.Value,OpenMode.ForRead);
                            if(layout.ModelType?!profile.Search.Model:!profile.Search.Layouts)continue;
                            if(profile.Search.ActiveLayoutOnly && layout.LayoutName!=Convert.ToString(AcApp.GetSystemVariable("CTAB"),CultureInfo.InvariantCulture))continue;
                            var record=(BlockTableRecord)tr.GetObject(layout.BlockTableRecordId,OpenMode.ForRead);
                            Walk(doc,layout.LayoutName,record,tr,Matrix3d.Identity,"","",new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase),new HashSet<ObjectId>(),0,profile.Search,profile.Formats,candidates,lineGroups,stampGeometry,texts,warnings,cancellation,false,"",ref visited);
                        }
                    }
                }
                }
                finally{AcApp.DocumentManager.MdiActiveDocument=active;}
            });
            var result=await Task.Run(()=>
            {
                foreach(var group in lineGroups)
                    foreach(var line in LineFrames.Find(group.Segments,profile.Formats,profile.Search,cancellation))
                    {
                        if(group.Clips.Any(clip=>!clip.Allows(line.Rectangle.Corners)))continue;
                        var c=group.Template;
                        candidates.Add(new FrameCandidate{DocumentKey=c.DocumentKey,DocumentName=c.DocumentName,DocumentPath=c.DocumentPath,Layout=c.Layout,Layer=c.Layer,BlockName=c.BlockName,
                            IsExternal=c.IsExternal,DrawingModified=c.DrawingModified,Elevation=c.Elevation,Attributes=c.Attributes,Kind="Отрезки"+(c.IsExternal?" в Xref":""),GeometryKind="Lines",HandlePath="LINE:"+string.Join("+",line.Members),
                            MemberPaths=line.Members,Vertices=line.Rectangle.Corners.ToList()});
                    }
                foreach(var c in candidates)if(dependencies.TryGetValue(c.DocumentKey,out var links))c.Dependencies=links;
                return FrameDetection.Detect(candidates,profile.Search,profile.Formats,cancellation,stampGeometry);
            },cancellation);
            var region=profile.Search.Region;
            if(region!=null)result.Frames.RemoveAll(f=>!string.Equals(f.Source.DocumentPath,region.DocumentPath,StringComparison.OrdinalIgnoreCase)||f.Layout!=region.Layout||!PolygonVisibility.FullyVisible(f.Geometry.Corners,region.Points));
            FrameOrder.Repage(result.Frames);
            StampMetadata.Apply(result.Frames,texts,profile.Metadata??new StampMetadataSettings());
            result.Warnings.InsertRange(0,warnings);
            result.Warnings.Add("Прототип: единицы геометрии задаются масштабом (единиц DWG на мм бумаги). Для дюймов добавьте коэффициент 1/25,4 в масштаб.");
            return result;
        }
        private static string DocumentKey(Document d)=>d.Database.FingerprintGuid+"|"+d.Name;
        private static Document FindDocument(DetectedFrame frame)
        {
            foreach(Document doc in AcApp.DocumentManager)if(DocumentKey(doc)==frame.Source.DocumentKey)return doc;
            throw new InvalidOperationException("Исходный документ закрыт, переименован или заменён. Повторите поиск.");
        }
        private static ObjectId Resolve(Database db,string handle)=>db.GetObjectId(false,new Handle(long.Parse(handle,NumberStyles.HexNumber,CultureInfo.InvariantCulture)),0);

        public async Task LocateAsync(DetectedFrame frame)
        {
            await InContext(()=>
            {
                var doc=FindDocument(frame);AcApp.DocumentManager.MdiActiveDocument=doc;
                using(doc.LockDocument())
                {
                    // End the read transaction before modifying the live viewport.
                    using(var tr=doc.Database.TransactionManager.StartTransaction())
                    { ValidateFrame(doc,frame,tr);tr.Commit(); }
                    LayoutManager.Current.CurrentLayout=frame.Layout;
                    EnsurePaperSpace(doc,frame.Layout);
                    using(var view=doc.Editor.GetCurrentView())
                    {
                        view.ViewDirection=Vector3d.ZAxis;view.Target=Point3d.Origin;view.ViewTwist=0;
                        var window=ViewWindow.Around(frame.Geometry.Corners,ViewAspect(view));
                        view.CenterPoint=new Point2d(window.Center.X,window.Center.Y);view.Width=window.Width;view.Height=window.Height;
                        doc.Editor.SetCurrentView(view);
                    }
                    doc.Editor.Regen();doc.Editor.UpdateScreen();AcApp.UpdateScreen();
                    doc.Window.Focus();
                    doc.Editor.WriteMessage("\nCAD Batch: показана рамка №"+frame.Page+" ("+frame.FormatName+"), "+frame.Source.HandlePath+".\n");
                }
            });
        }
        private static double ViewAspect(ViewTableRecord view)=>Numeric.Positive(view.Height)&&Numeric.Positive(view.Width)?view.Width/view.Height:1.6;
        private static void EnsurePaperSpace(Document doc,string layout)
        {
            if(!string.Equals(layout,"Model",StringComparison.OrdinalIgnoreCase) && Convert.ToInt32(AcApp.GetSystemVariable("CVPORT"),CultureInfo.InvariantCulture)>1)
                doc.Editor.SwitchToPaperSpace();
        }

        public async Task<ExportReport> ExportAsync(ExportRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation)
        {
            if(!request.PhysicalPrint&&request.PdfMode!=PdfOutputMode.Separate)return await ExportGroupedAsync(request,progress,cancellation);
            var report=new ExportReport{PhysicalPrint=request.PhysicalPrint};
            for(int i=0;i<request.Items.Count;i++)
            {
                if(cancellation.IsCancellationRequested){report.Cancelled=true;break;}
                var item=request.Items[i];progress.Report(new BatchProgress(i,request.Items.Count,"PDF "+(i+1)+" / "+request.Items.Count+": "+item.Frame.File));
                var trace=new OperationTrace();
                try
                {
                    if(item.Frame.Copies<1||item.Frame.Copies>999)throw new ArgumentException("Копий должно быть от 1 до 999.");
                    var effective=EffectiveRequest(request,item.Frame);ValidateOutputDevice(effective);
                    await InContext(()=>PlotOne(item,effective,trace));
                    item.Frame.Status=request.PhysicalPrint?"Передано в очередь":"PDF создан";
                    if(request.PhysicalPrint)report.Queued+=item.Frame.Copies;else {report.Files.Add(item.Path);request.OutputCompleted?.Invoke(item.Path);}
                }
                catch(System.Exception ex)
                {
                    item.Frame.Status="Ошибка: "+ex.Message;report.Errors.Add(item.Frame.File+" / "+item.Frame.Source.HandlePath+": "+ex.Message);
                    trace.Note(ex.ToString());
                    // Stop after failure: the next page should never hide a state-restoration error.
                    break;
                }
                finally { report.Details.Add("Рамка №"+item.Frame.Page+" / "+item.Frame.Source.HandlePath+Environment.NewLine+trace); }
                progress.Report(new BatchProgress(i+1,request.Items.Count,item.Frame.Status));
                await Task.Delay(10);
            }
            if(cancellation.IsCancellationRequested)report.Cancelled=true;
            return report;
        }

        private static void PlotOne(OutputItem item,ExportRequest request,OperationTrace trace)
        {
            var doc=FindDocument(item.Frame);var previousDoc=AcApp.DocumentManager.MdiActiveDocument;
            string? previousLayout=null;string? plotLayout=null;ViewTableRecord? previousView=null;
            object? background=null;int previousViewport=1;
            string directory=Path.GetDirectoryName(item.Path)!;
            string temporary=Path.Combine(directory,"cadbatch-tmp-"+Guid.NewGuid().ToString("N")+".pdf");
            string cropped=temporary+".crop.pdf";
            System.Exception? failure=null;
            var restoreErrors=new List<System.Exception>();
            trace.Note("Источник: "+doc.Name+"; вкладка: "+item.Frame.Layout+"; handle: "+item.Frame.Source.HandlePath);
            trace.Note("PC3: "+request.Device+"; PDF: "+item.Path);
            trace.Note("Источник масштаба: "+item.Frame.ScaleSource);
            if(item.Frame.StampEvidence!=null)
            {
                var stamp=item.Frame.StampEvidence;trace.Note("Верхняя линия штампа: "+stamp.HeaderStart.Distance(stamp.HeaderEnd).ToString("R",CultureInfo.InvariantCulture)+" единиц DWG / "+stamp.Settings.ReferenceWidthMm+" мм; подтверждающих сегментов: "+stamp.Members.Count);
            }
            trace.Note("Бумага: "+item.Frame.PaperWidthMm+" x "+item.Frame.PaperHeightMm+" мм; масштаб геометрии: "+item.Frame.Scale);
            try
            {
                if(!request.PhysicalPrint)trace.Step("Доступ к папке PDF",()=>Directory.CreateDirectory(directory));
                trace.Step("Проверка очереди печати",()=>
                {
                    if(PlotFactory.ProcessPlotState!=ProcessPlotState.NotPlotting)throw new InvalidOperationException("AutoCAD уже выполняет печать или публикацию.");
                });
                AcApp.DocumentManager.MdiActiveDocument=doc;
                using(doc.LockDocument())
                {
                    previousLayout=LayoutManager.Current.CurrentLayout;
                    previousViewport=Convert.ToInt32(AcApp.GetSystemVariable("CVPORT"),CultureInfo.InvariantCulture);
                    trace.Step("Переключение на вкладку рамки",()=>
                    {
                        LayoutManager.Current.CurrentLayout=item.Frame.Layout;EnsurePaperSpace(doc,item.Frame.Layout);plotLayout=item.Frame.Layout;
                    });
                    previousView=doc.Editor.GetCurrentView();background=AcApp.GetSystemVariable("BACKGROUNDPLOT");
                    trace.Step("Отключение фоновой печати",()=>AcApp.SetSystemVariable("BACKGROUNDPLOT",0));
                    Rectangle2 rectangle=null!;ObjectId layoutId=ObjectId.Null;
                    using(var settings=trace.Step("Чтение рамки и параметров листа",()=>
                    {
                        using(var tr=doc.Database.TransactionManager.StartTransaction())
                        {
                            rectangle=ValidateFrame(doc,item.Frame,tr);
                            layoutId=LayoutManager.Current.GetLayoutId(item.Frame.Layout);
                            var layout=(Layout)tr.GetObject(layoutId,OpenMode.ForRead);
                            var copy=new PlotSettings(layout.ModelType);
                            try { copy.CopyFrom(layout);tr.Commit();return copy; }
                            catch {copy.Dispose();throw;}
                        }
                    }))
                    {
                        // The read transaction is already closed. Its abort cannot undo the viewport.
                        var angle=rectangle.Angle+(request.UpsideDown?Math.PI:0);
                        var wcsToDcs=Matrix3d.Rotation(angle,Vector3d.ZAxis,Point3d.Origin).Inverse();
                        var points=rectangle.Corners.Select(p=>new Point3d(p.X,p.Y,0).TransformBy(wcsToDcs)).ToArray();
                        double minX=points.Min(p=>p.X),maxX=points.Max(p=>p.X),minY=points.Min(p=>p.Y),maxY=points.Max(p=>p.Y);
                        trace.Step("Применение вида рамки",()=>
                        {
                            using(var view=doc.Editor.GetCurrentView())
                            {
                                var window=ViewWindow.Around(points.Select(p=>new Point2(p.X,p.Y)),ViewAspect(view),1.1);
                                view.ViewDirection=Vector3d.ZAxis;view.Target=Point3d.Origin;view.ViewTwist=-angle;
                                view.CenterPoint=new Point2d(window.Center.X,window.Center.Y);view.Width=window.Width;view.Height=window.Height;
                                doc.Editor.SetCurrentView(view);
                            }
                            doc.Editor.Regen();doc.Editor.UpdateScreen();
                        });
                        var validator=PlotSettingsValidator.Current;
                        trace.Step("Подключение PDF-устройства",()=>
                        {
                            string? device=validator.GetPlotDeviceList().Cast<string>().FirstOrDefault(n=>string.Equals(n,request.Device,StringComparison.OrdinalIgnoreCase));
                            if(device==null)
                            {
                                var available=validator.GetPlotDeviceList().Cast<string>().Where(n=>n.IndexOf("pdf",StringComparison.OrdinalIgnoreCase)>=0);
                                throw new InvalidOperationException("PC3 не найден: "+request.Device+". Доступные PDF-устройства: "+string.Join(", ",available));
                            }
                            validator.SetPlotConfigurationName(settings,device,null);validator.RefreshLists(settings);
                        });
                        double paperWidth=item.Frame.PaperWidthMm,paperHeight=item.Frame.PaperHeightMm;
                        if(!string.IsNullOrWhiteSpace(request.Media))
                        {
                            string chosen=validator.GetCanonicalMediaNameList(settings).Cast<string>().FirstOrDefault(n=>string.Equals(n,request.Media,StringComparison.OrdinalIgnoreCase))??throw new InvalidOperationException("Бумага шаблона недоступна: "+request.Media);
                            using(var probe=new PlotSettings(settings.ModelType))
                            {
                                probe.CopyFrom(settings);validator.SetCanonicalMediaName(probe,chosen);validator.SetPlotPaperUnits(probe,PlotPaperUnit.Millimeters);validator.SetPlotRotation(probe,PlotRotation.Degrees000);
                                var size=probe.PlotPaperSize;bool rotated=Math.Abs(Math.Log(size.X/size.Y/(paperWidth/paperHeight)))>Math.Abs(Math.Log(size.Y/size.X/(paperWidth/paperHeight)));
                                paperWidth=rotated?size.Y:size.X;paperHeight=rotated?size.X:size.Y;
                            }
                            trace.Note("Бумага из шаблона: "+paperWidth+" × "+paperHeight+" мм; "+chosen);
                        }
                        if(!request.FitToPaper && (rectangle.Width/item.Frame.Scale>paperWidth+0.1 || rectangle.Height/item.Frame.Scale>paperHeight+0.1))
                            throw new InvalidOperationException("Размер контура превышает бумагу при 1:1. Включите «Вписать в бумагу».");
                        var media=trace.Step("Выбор размера бумаги",()=>PickMedia(settings,validator,paperWidth,paperHeight,request.FitToPaper,trace,request.Media));
                        if(!string.IsNullOrWhiteSpace(request.Media))
                        {
                            if(!validator.GetCanonicalMediaNameList(settings).Cast<string>().Contains(request.Media,StringComparer.OrdinalIgnoreCase))throw new InvalidOperationException("Бумага шаблона недоступна: "+request.Media);
                            if(!string.Equals(media.Name,request.Media,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Бумага шаблона не соответствует размеру рамки. Выберите подходящую бумагу или оставьте автоматический выбор.");
                        }
                        if(request.PhysicalPrint&&media.IsCarrier)throw new InvalidOperationException("Принтер не содержит точной бумаги "+paperWidth+" × "+paperHeight+" мм. Для физической печати обрезка PDF не применяется.");
                        trace.Note("Выбран носитель: "+media.Name+"; поворот: "+media.Rotation);
                        trace.Step("Применение бумаги и ориентации",()=>
                        {
                            validator.SetCanonicalMediaName(settings,media.Name);
                            validator.SetPlotPaperUnits(settings,PlotPaperUnit.Millimeters);validator.SetPlotRotation(settings,media.Rotation);
                        });
                        trace.Note(string.Format(CultureInfo.InvariantCulture,"Окно DCS: ({0:R}, {1:R}) - ({2:R}, {3:R})",minX,minY,maxX,maxY));
                        trace.Step("Назначение области печати",()=>
                        {
                            // Some layouts have a zero/invalid saved window. Seed the window before selecting Window.
                            // If a host requires Window first, retry only that documented InvalidInput case.
                            var area=new Extents2d(minX,minY,maxX,maxY);
                            try {validator.SetPlotWindowArea(settings,area);}
                            catch(AcException ex) when(ex.ErrorStatus==Autodesk.AutoCAD.Runtime.ErrorStatus.InvalidInput)
                            {trace.Note("Повтор задания окна с предварительным PlotType.Window");validator.SetPlotType(settings,Autodesk.AutoCAD.DatabaseServices.PlotType.Window);validator.SetPlotWindowArea(settings,area);}
                            validator.SetPlotType(settings,Autodesk.AutoCAD.DatabaseServices.PlotType.Window);
                        });
                        trace.Step("Назначение масштаба и центрирования",()=>
                        {
                                bool nativeFit=request.FitToPaper && !media.IsCarrier;
                                validator.SetUseStandardScale(settings,nativeFit);
                                if(nativeFit)validator.SetStdScaleType(settings,StdScaleType.ScaleToFit);
                                else
                                {
                                    double factor=request.FitToPaper?Math.Min(paperWidth/rectangle.Width,paperHeight/rectangle.Height):1/item.Frame.Scale;
                                    validator.SetCustomPrintScale(settings,new CustomScale(factor*media.ScaleFactor,1));
                                }
                            validator.SetPlotCentered(settings,true);
                            if(request.OffsetX!=0||request.OffsetY!=0)
                            {
                                if(media.IsCarrier)throw new InvalidOperationException("Смещение недоступно для промежуточной бумаги с обрезкой. Выберите точный формат или обнулите смещение.");
                                validator.SetPlotCentered(settings,false);validator.SetPlotOrigin(settings,new Point2d(request.OffsetX,request.OffsetY));
                            }
                        });
                        trace.Step("Назначение стиля печати",()=>
                        {
                            var policy=PrintStylePolicy.Resolve(request.ColorMode,request.StyleSheet,doc.Database.PlotStyleMode);
                            trace.Note("Режим цвета: "+PrintStylePolicy.Describe(request.ColorMode)+"; исходный стиль: "+settings.CurrentStyleSheet);
                            if(policy.StyleSheet!=null)
                            {
                                string selected=policy.StyleSheet;
                                if(selected.Length>0)
                                {
                                    string? available=validator.GetPlotStyleSheetList().Cast<string>().FirstOrDefault(n=>string.Equals(n,selected,StringComparison.OrdinalIgnoreCase));
                                    if(available==null)throw new InvalidOperationException("Стиль печати не найден: "+selected);
                                    selected=available;
                                }
                                validator.SetCurrentStyleSheet(settings,selected);
                            }
                            if(policy.ApplyPlotStyles.HasValue)settings.PlotPlotStyles=policy.ApplyPlotStyles.Value;
                            settings.PrintLineweights=true;
                            trace.Note("Итоговый стиль: "+settings.CurrentStyleSheet+"; применять стили: "+settings.PlotPlotStyles);
                        });
                        using(var info=new PlotInfo {Layout=layoutId,OverrideSettings=settings})
                        using(var infoValidator=new PlotInfoValidator {MediaMatchingPolicy=MatchingPolicy.MatchEnabled})
                        {
                            // 0.1.1 failed with NoMatchingMedia despite finding the requested A3 in the PC3.
                            // Let the native validator resolve that selection, without custom PC3/PMP sizes;
                            // verify the actual device and physical paper before starting PDF generation.
                            trace.Note("Согласование носителя: MatchEnabled; создание пользовательской бумаги отключено.");
                            trace.Note("До проверки: "+DescribePaper(settings));
                            trace.Step("Проверка настроек AutoCAD",()=>infoValidator.Validate(info));
                            trace.Step("Проверка согласованной бумаги",()=>
                            {
                                var actual=info.ValidatedSettings;
                                if(actual==null)throw new InvalidOperationException("AutoCAD не вернул согласованные параметры бумаги.");
                                trace.Note("После проверки: "+DescribePaper(actual));
                                var size=actual.PlotPaperSize;
                                bool quarterTurn=actual.PlotRotation==PlotRotation.Degrees090 || actual.PlotRotation==PlotRotation.Degrees270;
                                ValidatedPaper.EnsureUnchanged(request.Device,media.IsCarrier?media.Width:paperWidth,media.IsCarrier?media.Height:paperHeight,
                                    actual.PlotConfigurationName,size.X,size.Y,quarterTurn);
                                if(media.IsCarrier)
                                {
                                    var m=actual.PlotPaperMargins;
                                    if(Math.Abs(m.MinPoint.X)+Math.Abs(m.MinPoint.Y)+Math.Abs(m.MaxPoint.X)+Math.Abs(m.MaxPoint.Y)>0.05)
                                        throw new InvalidOperationException("Промежуточный носитель получил поля: точное центрирование кратного формата невозможно.");
                                }
                            });
                            GeneratePdf(info,doc.Name,temporary,trace,request.PhysicalPrint,item.Frame.Copies);
                            if(media.IsCarrier)
                            {
                                trace.Step("Точный размер кратного формата PDF",()=>
                                {
                                    TargetSheetPdf.RestoreCentered(temporary,cropped,paperWidth,paperHeight,media.ScaleFactor);
                                    File.Delete(temporary);File.Move(cropped,temporary);
                                });
                            }
                        }
                    }
                }
            }
            catch(System.Exception ex){failure=ex;}
            finally
            {
                Restore("Восстановление вкладки и вида",()=>
                {
                    if(previousLayout==null)return;
                    AcApp.DocumentManager.MdiActiveDocument=doc;
                    using(doc.LockDocument())
                    {
                        if(previousView!=null && plotLayout!=null)
                        {LayoutManager.Current.CurrentLayout=plotLayout;EnsurePaperSpace(doc,plotLayout);doc.Editor.SetCurrentView(previousView);}
                        LayoutManager.Current.CurrentLayout=previousLayout;
                        if(previousViewport>1 && !string.Equals(previousLayout,"Model",StringComparison.OrdinalIgnoreCase))
                        {doc.Editor.SwitchToModelSpace();AcApp.SetSystemVariable("CVPORT",previousViewport);}
                        doc.Editor.UpdateScreen();
                    }
                },trace,restoreErrors);
                previousView?.Dispose();
                Restore("Восстановление BACKGROUNDPLOT",()=>{if(background!=null)AcApp.SetSystemVariable("BACKGROUNDPLOT",background);},trace,restoreErrors);
                Restore("Восстановление активного документа",()=>{if(previousDoc!=null)AcApp.DocumentManager.MdiActiveDocument=previousDoc;},trace,restoreErrors);
            }
            try
            {
                if(failure!=null)
                {
                    if(restoreErrors.Count>0)throw new AggregateException("Ошибка PDF и восстановления состояния AutoCAD",new[]{failure}.Concat(restoreErrors));
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
                }
                if(restoreErrors.Count>0)throw new AggregateException("Не удалось восстановить состояние AutoCAD",restoreErrors);
                if(!request.PhysicalPrint)
                {
                    trace.Step("Проверка созданного PDF",()=>
                    {
                        using(var file=File.OpenRead(temporary))
                        {
                            byte[] header=new byte[5];
                            if(file.Length<100 || file.Read(header,0,5)!=5 || System.Text.Encoding.ASCII.GetString(header)!="%PDF-")throw new IOException("PDF-драйвер не создал корректный файл.");
                        }
                    });
                    trace.Step("Сохранение итогового PDF",()=>OutputFiles.MoveReplacing(temporary,item.Path));
                }
            }
            finally {if(File.Exists(temporary))File.Delete(temporary);if(File.Exists(cropped))File.Delete(cropped);}
        }
        private static void Restore(string name,Action action,OperationTrace trace,List<System.Exception> errors)
        {
            try {trace.Step(name,action);}catch(System.Exception ex){errors.Add(ex);}
        }
        private static string DescribePaper(PlotSettings settings)
        {
            var size=settings.PlotPaperSize;
            return string.Format(CultureInfo.InvariantCulture,
                "PC3={0}; носитель={1}; размер без поворота={2:R} x {3:R} мм; поворот={4}; единицы={5}",
                settings.PlotConfigurationName,settings.CanonicalMediaName,size.X,size.Y,settings.PlotRotation,settings.PlotPaperUnits);
        }
        private static void GeneratePdf(PlotInfo info,string document,string file,OperationTrace trace,bool physical=false,int copies=1)
        {
            using(var engine=trace.Step("Создание механизма печати",()=>PlotFactory.CreatePublishEngine()))
            {
                bool plot=false,doc=false,page=false,graphics=false;
                try
                {
                    trace.Step("Начало печати (BeginPlot)",()=>engine.BeginPlot(null,null));plot=true;
                    trace.Step("Начало документа (BeginDocument)",()=>engine.BeginDocument(info,document,null,physical?copies:1,!physical,physical?null:file));doc=true;
                    using(var pageInfo=new PlotPageInfo())
                    {
                        trace.Step("Начало страницы (BeginPage)",()=>engine.BeginPage(pageInfo,info,true,null));page=true;
                        trace.Step("Формирование графики",()=>engine.BeginGenerateGraphics(null));graphics=true;
                        trace.Step("Завершение графики",()=>engine.EndGenerateGraphics(null));graphics=false;
                        trace.Step("Завершение страницы",()=>engine.EndPage(null));page=false;
                    }
                    trace.Step("Завершение PDF-документа",()=>engine.EndDocument(null));doc=false;
                    trace.Step("Завершение печати",()=>engine.EndPlot(null));plot=false;
                }
                finally
                {
                    // Close only levels which actually started, preserving the original failing call in the trace.
                    var cleanup=new List<System.Exception>();
                    if(graphics)Restore("Закрытие графики после ошибки",()=>engine.EndGenerateGraphics(null),trace,cleanup);
                    if(page)Restore("Закрытие страницы после ошибки",()=>engine.EndPage(null),trace,cleanup);
                    if(doc)Restore("Закрытие документа после ошибки",()=>engine.EndDocument(null),trace,cleanup);
                    if(plot)Restore("Закрытие печати после ошибки",()=>engine.EndPlot(null),trace,cleanup);
                }
            }
        }
        private sealed class MediaChoice
        {
            public string Name="";public PlotRotation Rotation;public double Score;public double Margins;
            public double Width;public double Height;public bool IsCarrier;public double ScaleFactor=1;
        }
        private static MediaChoice PickMedia(PlotSettings settings,PlotSettingsValidator validator,double width,double height,bool fit,OperationTrace trace,string preferred="")
        {
            MediaChoice? best=null,carrier=null,smallCarrier=null;int skipped=0;
            var names=validator.GetCanonicalMediaNameList(settings).Cast<string>().ToArray();
            trace.Note("Носителей в PC3: "+names.Length);
            foreach(string name in names.Where(n=>string.IsNullOrWhiteSpace(preferred)||string.Equals(n,preferred,StringComparison.OrdinalIgnoreCase)))
            {
                // Probe in a disposable COPY. One stale/unsupported PC3 media must not fail the entire job.
                using(var probe=new PlotSettings(settings.ModelType))
                {
                    try
                    {
                        probe.CopyFrom(settings);validator.SetCanonicalMediaName(probe,name);
                        validator.SetPlotPaperUnits(probe,PlotPaperUnit.Millimeters);validator.SetPlotRotation(probe,PlotRotation.Degrees000);
                        var size=probe.PlotPaperSize;var margins=probe.PlotPaperMargins;
                        double margin=Math.Abs(margins.MinPoint.X)+Math.Abs(margins.MinPoint.Y)+Math.Abs(margins.MaxPoint.X)+Math.Abs(margins.MaxPoint.Y);
                        foreach(bool rotated in new[]{false,true})
                        {
                            double w=rotated?size.Y:size.X,h=rotated?size.X:size.Y;
                            double error=Math.Abs(w-width)+Math.Abs(h-height);
                            double carrierScale=Math.Min(1,Math.Min(w/width,h/height));
                            if(margin<=.05&&carrierScale>0&&(smallCarrier==null||carrierScale>smallCarrier.ScaleFactor))smallCarrier=new MediaChoice{Name=name,Rotation=rotated?PlotRotation.Degrees090:PlotRotation.Degrees000,Margins=margin,Width=w,Height=h,IsCarrier=true,ScaleFactor=carrierScale};
                            var rotation=rotated?PlotRotation.Degrees090:PlotRotation.Degrees000;
                            if(Math.Abs(w-width)<=1 && Math.Abs(h-height)<=1)
                            {
                                double score=error+margin;
                                if(best==null || score<best.Score)best=new MediaChoice {Name=name,Rotation=rotation,Score=score,Margins=margin,Width=w,Height=h};
                            }
                            else if(w>=width-0.01 && h>=height-0.01 && margin<=0.05)
                            {
                                // Full-bleed carrier preserves a centered physical-size window. The PDF is
                                // subsequently trimmed by page boxes, retaining vector content and colors.
                                double score=w*h;
                                if(carrier==null || score<carrier.Score)carrier=new MediaChoice{Name=name,Rotation=rotation,Score=score,Margins=margin,Width=w,Height=h,IsCarrier=true};
                            }
                        }
                    }
                    catch(AcException ex){skipped++;trace.Note("Пропущен носитель "+name+": "+ex.ErrorStatus+" / "+ex.Message);}
                }
            }
            trace.Note("Недоступных носителей пропущено: "+skipped);
            if(best==null)
            {
                if(carrier==null)carrier=smallCarrier;
                if(carrier==null)throw new InvalidOperationException("В PC3 нет бумаги "+width+" × "+height+" мм и полного носителя достаточного размера. Проверено: "+names.Length+"; недоступно: "+skipped+".");
                best=carrier;
                trace.Note("Кратный формат: промежуточная бумага "+best.Width+" x "+best.Height+" мм; масштаб носителя "+best.ScaleFactor+"; итоговый PDF "+width+" x "+height+" мм.");
            }
            if(!fit && best.Margins>1)throw new InvalidOperationException("Носитель имеет поля. Включите «Вписать в бумагу» для первого теста.");
            return best;
        }
        private static async Task InContext(Action action)
        {
            // Do not allow a managed exception to escape AutoCAD's native command callback.
            System.Exception? failure=null;
            await AcApp.DocumentManager.ExecuteInCommandContextAsync(_=>
            {
                try { action(); } catch(System.Exception ex) { failure=ex; }
                return Task.CompletedTask;
            },null);
            if(failure!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
