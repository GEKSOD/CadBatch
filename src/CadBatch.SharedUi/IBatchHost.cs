using System;
using System.Threading;
using System.Threading.Tasks;
using CadBatch.Core;

namespace CadBatch.UI
{
    public interface IBatchHost
    {
        string Description { get; }
        bool CanPlot { get; }
        Task<string[]> GetPlotDevicesAsync();
        Task<PlotCapabilities> GetPlotCapabilitiesAsync(string device);
        Task<SearchRegion?> PickSearchRegionAsync();
        // The objects selected in AutoCAD, or asks to select them there; null when the user cancels.
        Task<SearchSelection?> PickSelectionAsync();
        Task<string[]> GetLayoutNamesAsync();
        Task<StampScaleMeasurement?> MeasureStampScaleAsync(double referenceWidthMm,bool twoPoints);
        Task<ScanResult> SearchAsync(ProjectProfile profile,CancellationToken cancellation);
        Task LocateAsync(DetectedFrame frame);
        Task<ExportReport> ExportAsync(ExportRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation);
        Task<CadOperationResult> RunCadOperationAsync(CadOperationRequest request,IProgress<BatchProgress> progress,CancellationToken cancellation);
    }
}
