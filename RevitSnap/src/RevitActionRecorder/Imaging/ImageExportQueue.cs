using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using RevitActionRecorder.Infrastructure;

namespace RevitActionRecorder.Imaging;

/// <summary>
/// Очередь PNG-экспорта. ExportImage нельзя звать из обработчиков событий,
/// поэтому запросы копятся и разгребаются через ExternalEvent в свободном API-контексте.
/// </summary>
internal sealed class ImageExportQueue : IExternalEventHandler
{
    internal sealed record Request(
        Document Doc,
        List<ElementId> ViewIds,
        string Directory,
        string FilePrefix,
        int PixelSize);

    private static ImageExportQueue? _instance;
    private readonly Queue<Request> _queue = new();
    private ExternalEvent? _event;

    public static ImageExportQueue Instance =>
        _instance ?? throw new InvalidOperationException("ImageExportQueue is not created yet");

    /// <summary>Вызывать из API-контекста (команды): ExternalEvent.Create вне его не работает.</summary>
    public static void EnsureCreated()
    {
        if (_instance is not null)
            return;
        var queue = new ImageExportQueue();
        queue._event = ExternalEvent.Create(queue);
        _instance = queue;
    }

    public void Enqueue(Request request)
    {
        if (request.ViewIds.Count == 0)
            return;
        _queue.Enqueue(request);
        _event?.Raise();
    }

    public void Execute(UIApplication app)
    {
        while (_queue.Count > 0)
        {
            var request = _queue.Dequeue();
            try
            {
                if (!request.Doc.IsValidObject)
                    continue;

                Directory.CreateDirectory(request.Directory);
                var options = new ImageExportOptions
                {
                    FilePath = Path.Combine(request.Directory, request.FilePrefix),
                    ExportRange = ExportRange.SetOfViews,
                    ZoomType = ZoomFitType.FitToPage,
                    PixelSize = Math.Max(64, request.PixelSize),
                    FitDirection = FitDirectionType.Horizontal,
                    HLRandWFViewsFileType = ImageFileType.PNG,
                    ShadowViewsFileType = ImageFileType.PNG,
                    ImageResolution = ImageResolution.DPI_150,
                };
                options.SetViewsAndSheets(request.ViewIds);
                request.Doc.ExportImage(options);
            }
            catch (Exception ex)
            {
                AddinLog.Error("ImageExportQueue", ex);
            }
        }
    }

    public string GetName() => "RevitActionRecorder image export";
}
