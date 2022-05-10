using System.Diagnostics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Bmp;
using SixLabors.ImageSharp.PixelFormats;
using Spectre.Console;
using TheoraSharp;


public static class Program
{
    public static void Main(string[] args)
    {
        var perf = new OggVideoReader(args[0]);
        var reader = perf.StartReading<uint>();

        AnsiConsole.Live(Text.Empty)
            .StartAsync(async (context) =>
            {
                var sw = Stopwatch.StartNew();
                while (reader.MoveNext())
                {
                    using (var ms = new MemoryStream())
                    {
                        var str = new Image<Rgba32>(perf.Width, perf.Height);
                        var i = 0;
                        for (int y = 0; y < perf.Height; y++)
                        for (int x = 0; x < perf.Width; x++)
                        {
                            var pixel = reader.Current[i++];
                            var bytes = BitConverter.GetBytes(pixel);
                            str[x, y] = new Rgba32(bytes[2],bytes[1],bytes[0],bytes[3]);
                        }
                        str.Save(ms, new BmpEncoder());
                        var ci = new CanvasImage(ms.ToArray());
                        context.UpdateTarget(ci);
                    }

                    var ts = TimeSpan.FromSeconds(1 / perf.Fps) - sw.Elapsed;
                    var rts = ts > TimeSpan.Zero ? ts : TimeSpan.Zero;
                    await Task.Delay(rts);
                    sw.Restart();
                }
            }).Wait();
    }
}