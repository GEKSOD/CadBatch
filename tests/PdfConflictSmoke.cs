using System;
using System.IO;
using CadBatch.Pdf;

// Reproduces an AutoCAD process that redirects Microsoft.Extensions.Logging.Abstractions to 1.1.1:
// PDF work inside the process fails, PdfRunner must finish it in CadBatch.PdfWorker.exe.
class PdfConflictSmoke
{
    static int Main(string[] args)
    {
        string folder=args[0],worker=args[1],report=Path.Combine(folder,"conflict.txt");
        try
        {
            string a=Path.Combine(folder,"a.pdf"),b=Path.Combine(folder,"b.pdf"),merged=Path.Combine(folder,"merged.pdf"),fallback=Path.Combine(folder,"merged-worker.pdf");
            bool failedInProcess=false;string reason="";
            try{PdfRunner.Execute(PdfJob.Merge(new[]{a,b},merged,new[]{"A","B"},"Проверка"));}
            catch(Exception ex){failedInProcess=PdfRunner.IsConflict(ex);reason=ex.GetType().Name+": "+ex.Message;}
            if(!failedInProcess)throw new Exception("The legacy redirect did not reproduce the conflict: "+reason);
            PdfRunner.WorkerPath=worker;
            PdfRunner.Run(PdfJob.Merge(new[]{a,b},fallback,new[]{"A","B"},"Проверка"));
            if(!PdfRunner.Isolated||!File.Exists(fallback)||new FileInfo(fallback).Length<200)throw new Exception("The worker fallback did not produce the PDF.");
            // The stage that failed in AutoCAD 2023: exact size of a multiple format.
            string restored=Path.Combine(folder,"restored.pdf");PdfRunner.Run(PdfJob.Restore(a,restored,210,297,1));
            if(!File.Exists(restored)||new FileInfo(restored).Length<200)throw new Exception("The worker did not restore the exact sheet size.");
            File.WriteAllText(report,"PDF CONFLICT SMOKE PASS: in-process failure reproduced ("+reason.Split('\n')[0]+"); the worker process produced the merged and the exact-size PDF.");
            return 0;
        }
        catch(Exception ex){File.WriteAllText(report+".error.txt",ex.ToString());return 1;}
    }
}
