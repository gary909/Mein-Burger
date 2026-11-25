using System.Text;
using PDFtoImage;
using SkiaSharp;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Images;

Console.OutputEncoding = Encoding.UTF8;

if (args.Length >= 4 && args[0] == "render")
{
    var pdfToRender = args[1];
    var pageToRender = int.Parse(args[2]);
    var outputPath = args[3];

    using var pdfStream = File.OpenRead(pdfToRender);
    Conversion.SavePng(outputPath, pdfStream, page: pageToRender, options: new RenderOptions { Dpi = 150 });
    Console.WriteLine($"Rendered page {pageToRender} to {outputPath}");
    return 0;
}

if (args.Length < 2)
{
    Console.WriteLine("Usage: extract <pdf> <page> [page...]");
    Console.WriteLine("       render <pdf> <page> <out.png>");
    return 1;
}

var pdfPath = args[0];
var pages = args.Skip(1).Select(int.Parse).ToArray();

using var document = PdfDocument.Open(pdfPath);

foreach (var pageNumber in pages)
{
    var page = document.GetPage(pageNumber);
    Console.WriteLine($"================ PAGE {pageNumber} ================");

    var lines = BuildLines(page.Letters);
    foreach (var line in lines)
    {
        var text = string.Concat(line.Letters.Select(l => l.Value));
        if (string.IsNullOrWhiteSpace(text)) continue;

        var boldCount = line.Letters.Count(l => (l.FontName ?? "").Contains("Bold", StringComparison.OrdinalIgnoreCase));
        var boldRatio = line.Letters.Count == 0 ? 0 : boldCount / (double)line.Letters.Count;
        var fonts = string.Join(", ", line.Letters
            .Select(l => l.FontName ?? "?")
            .Distinct()
            .Select(ShortFont));
        var pua = string.Join(" ", line.Letters
            .Where(l => l.Value.Length > 0 && l.Value[0] >= 0xE000 && l.Value[0] <= 0xF8FF)
            .Select(l => $"U+{(int)l.Value[0]:X4}@{l.FontName}"));

        Console.WriteLine($"y={line.Bottom,7:F1} x={line.Letters[0].BoundingBox.Left,6:F1} bold={boldRatio:F2} [{fonts}]{(pua.Length > 0 ? " PUA=" + pua : "")} | {text}");
    }

    foreach (var image in page.GetImages())
    {
        var pngLength = image.TryGetPng(out var png) ? png.Length : 0;
        Console.WriteLine($"IMAGE box={image.BoundingBox} px={image.WidthInSamples}x{image.HeightInSamples} png={pngLength}");
    }
}

return 0;

static List<Line> BuildLines(IReadOnlyList<Letter> letters)
{
    var lines = new List<Line>();
    foreach (var letter in letters.OrderByDescending(l => l.GlyphRectangle.Bottom))
    {
        var bottom = letter.GlyphRectangle.Bottom;
        var tolerance = Math.Max(1.5, letter.FontSize * 0.35);
        var line = lines.Count > 0 ? lines[^1] : null;

        if (line is not null && Math.Abs(line.Bottom - bottom) <= tolerance)
        {
            line.Letters.Add(letter);
        }
        else
        {
            lines.Add(new Line(bottom, new List<Letter> { letter }));
        }
    }

    foreach (var line in lines)
    {
        line.Letters.Sort((a, b) => a.GlyphRectangle.Left.CompareTo(b.GlyphRectangle.Left));
    }

    return lines;
}

static string ShortFont(string? fontName)
{
    if (string.IsNullOrEmpty(fontName)) return "?";
    var plus = fontName.IndexOf('+');
    return plus >= 0 ? fontName[(plus + 1)..] : fontName;
}

internal sealed class Line(double bottom, List<Letter> letters)
{
    public double Bottom { get; } = bottom;
    public List<Letter> Letters { get; } = letters;
}
