// Tracer.Service/Services/ScreenshotService.cs
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ActiMetrics.Data;

namespace ActiMetrics.Service.Services
{
    public class ScreenshotService
    {
        private readonly string _workerId;
        private readonly string _screenshotFolder;
        private readonly ScreenshotRepository _screenshotRepository;
        private static readonly ImageCodecInfo JpegCodec =
            ImageCodecInfo.GetImageDecoders()
                .First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        private static EncoderParameters GetEncoderParams(long quality = 60)
        {
            var encoderParams = new EncoderParameters(1);
            encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
            return encoderParams;
        }
        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
            MonitorEnumProc lpfnEnum, IntPtr dwData);

        private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor,
            ref RECT lprcMonitor, IntPtr dwData);

        private static List<Rectangle> GetAllMonitors()
        {
            var monitors = new List<Rectangle>();
            EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
                {
                    monitors.Add(new Rectangle(rect.Left, rect.Top,
                        rect.Right - rect.Left, rect.Bottom - rect.Top));
                    return true;
                }, IntPtr.Zero);
            return monitors;
        }

        public ScreenshotService(ScreenshotRepository screenshotRepository)
        {
            _workerId = Environment.MachineName;
            _screenshotFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Tracer", "Screenshots"
            );
            _screenshotRepository = screenshotRepository;
            Directory.CreateDirectory(_screenshotFolder);
        }


        public async Task<string[]?> TickAsync()
        {

            try
            {
                var monitors = GetAllMonitors();
                var timestamp = TimeZoneInfo.ConvertTime(DateTime.Now, TimeZoneInfo.Local).ToString("yyyyMMdd-HHmmss");
                string[] files = [];

                for (int i = 0; i < monitors.Count; i++)
                {
                    var bounds = monitors[i];

                    using var bitmap = new Bitmap(bounds.Width, bounds.Height);
                    using var graphics = Graphics.FromImage(bitmap);
                    graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);

                    var fileName = $"{_workerId}_{timestamp}_Monitor{i + 1}.jpg";
                    var filePath = Path.Combine(_screenshotFolder, fileName);

                    bitmap.Save(filePath, JpegCodec, GetEncoderParams(60));

                    files.Append<string>(fileName);
                    await _screenshotRepository.LogIntervalAsync(_workerId, filePath);




                    Console.WriteLine($"[Screenshot] Monitor {i + 1} → {fileName}");

                }

                return files;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Screenshot] Error: {ex.Message}");
                return null;
            }
        }
    }
}