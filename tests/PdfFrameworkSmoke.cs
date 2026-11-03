using System;
using System.Globalization;

// Deliberately runs under CLR 4 without an exe.config/binding redirects.
// This exercises the same serviced dependencies required by the AutoCAD 2023 build.
class PdfFrameworkSmoke
{
    static int Main(string[] args)
    {
        try
        {
            CadBatch.Pdf.TargetSheetPdf.CropCentered(args[0],args[1],
                double.Parse(args[2],CultureInfo.InvariantCulture),double.Parse(args[3],CultureInfo.InvariantCulture));
            CadBatch.Pdf.TargetSheetPdf.Merge(new[]{args[1],args[1]},args[1]+".merged.pdf",new[]{"001 — Лист","002 — Лист"});
            CadBatch.Pdf.TargetSheetPdf.RestoreCentered(args[0],args[1]+".restored.pdf",double.Parse(args[2],CultureInfo.InvariantCulture)*2,double.Parse(args[3],CultureInfo.InvariantCulture)*2,.5);
            var tiles=CadBatch.Pdf.TargetSheetPdf.PlanTiles(args[1],210,297,10,5,1);
            CadBatch.Pdf.TargetSheetPdf.Tile(args[1],args[1]+".tiles.pdf",tiles);
            CadBatch.Core.RegisterExport.Xlsx(args[1]+".xlsx",new[]{new CadBatch.Core.DetectedFrame{SheetNumber="007",Title="Проверка XLSX",Code="АР"}});
            var profile=new CadBatch.Core.ProjectProfile();profile.Search.Stamp=new CadBatch.Core.StampScaleSettings{Enabled=true,OnlyStamp=true};
            CadBatch.Core.ProfileStore.Save(args[1]+".profile.json",profile);
            if(CadBatch.Core.ProfileStore.Load(args[1]+".profile.json").Search.Stamp.ReferenceWidthMm!=185||CadBatch.Core.StampScaleIndex.Measure(18500,185)!=100)throw new Exception("Stamp profile/measurement failed.");
            Console.WriteLine("NET48 PDF/XLSX SMOKE PASS; crop + restore vector size + merge + tiles + register; CLR="+Environment.Version);
            return 0;
        }
        catch(Exception error)
        {
            Console.Error.WriteLine(error.GetType().FullName+": "+error.Message);
            if(error.InnerException!=null)Console.Error.WriteLine(error.InnerException.GetType().FullName+": "+error.InnerException.Message);
            return 1;
        }
    }
}
