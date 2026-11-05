using System;

namespace CadBatch.PdfWorker
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)=>CadBatch.Pdf.PdfRunner.WorkerMain(args);
    }
}
