using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PartitionPilot.MarketingHero;

internal static class Program
{
    private const int HeroWidth = 1280;
    private const int HeroHeight = 640;

    private static readonly Rectangle ScreenshotCrop = new(200, 160, 1220, 660);

    private static readonly string[] VisibleCopy =
    [
        "PartitionPilot",
        "Windows storage control",
        "See the plan.",
        "Then touch the disk.",
        "Partition changes, drive health, and recovery evidence in one guarded workspace.",
        "Queued changes",
        "Identity checks",
        "SMART health",
        "GUI + CLI",
        "Open source for Windows 10 and 11",
        "Guarded Windows disk management",
        "Know what changes.",
        "Before anything does.",
        "Review first. Apply when ready.",
        "Plan partition work beside SMART health, snapshots, and imaging tools.",
        "Recovery evidence",
        "Real product UI",
        "Simulated disks"
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var options = ParseArguments(args);
            var repoRoot = Path.GetFullPath(options.GetValueOrDefault("repo", FindRepoRoot()));
            var archiveRoot = Path.GetFullPath(options.GetValueOrDefault(
                "archive",
                Path.Combine(repoRoot, "assets", "concepts", "2026-09-12-readme-hero")));
            var selected = options.GetValueOrDefault("select", "none");
            if (selected is not ("none" or "1" or "2"))
                throw new ArgumentException("--select must be none, 1, or 2.");

            Directory.CreateDirectory(archiveRoot);
            Directory.CreateDirectory(Path.Combine(archiveRoot, "review"));

            var logoPath = Path.Combine(repoRoot, "assets", "brand", "partitionpilot-mark.png");
            var screenshotPath = Path.Combine(
                archiveRoot,
                "release-source-captures",
                "01-partition-workspace.png");
            var referencePath = Path.Combine(
                archiveRoot,
                "source",
                "previous-social-preview-versioned.png");
            RequireFile(logoPath);
            RequireFile(screenshotPath);
            RequireFile(referencePath);
            RejectReleaseNumbers(VisibleCopy);

            using var logo = Image.FromFile(logoPath);
            using var screenshot = Image.FromFile(screenshotPath);
            using var reference = Image.FromFile(referencePath);
            ValidateScreenshotCrop(screenshot);

            using var candidateOne = RenderContinuityHero(logo, screenshot);
            using var candidateTwo = RenderSafetyLedHero(logo, screenshot);

            var candidateOnePath = Path.Combine(
                archiveRoot,
                "readme-hero-candidate-01-continuity.png");
            var candidateTwoPath = Path.Combine(
                archiveRoot,
                "readme-hero-candidate-02-safety-led.png");
            SavePng(candidateOne, candidateOnePath);
            SavePng(candidateTwo, candidateTwoPath);

            SaveScaled(candidateOne, Path.Combine(archiveRoot, "review", "candidate-01-960.png"), 960, 480);
            SaveScaled(candidateOne, Path.Combine(archiveRoot, "review", "candidate-01-640.png"), 640, 320);
            SaveScaled(candidateTwo, Path.Combine(archiveRoot, "review", "candidate-02-960.png"), 960, 480);
            SaveScaled(candidateTwo, Path.Combine(archiveRoot, "review", "candidate-02-640.png"), 640, 320);
            SaveComparison(candidateOne, candidateTwo, Path.Combine(archiveRoot, "review", "candidate-comparison.png"));
            SaveReferenceComparison(
                reference,
                candidateOne,
                candidateTwo,
                Path.Combine(archiveRoot, "review", "reference-and-candidates.png"));

            string? finalPath = null;
            if (selected != "none")
            {
                var chosen = selected == "1" ? candidateOne : candidateTwo;
                finalPath = Path.Combine(archiveRoot, "readme-hero-final.png");
                SavePng(chosen, finalPath);
                SavePng(chosen, Path.Combine(repoRoot, "assets", "marketing", "readme-hero.png"));
                SavePng(chosen, Path.Combine(repoRoot, "assets", "social-preview.png"));
                File.WriteAllLines(
                    Path.Combine(repoRoot, "assets", "marketing", "readme-hero-copy.txt"),
                    VisibleCopy);
                SaveScaled(chosen, Path.Combine(archiveRoot, "review", "selected-960.png"), 960, 480);
                SaveScaled(chosen, Path.Combine(archiveRoot, "review", "selected-640.png"), 640, 320);
            }

            var outputFiles = Directory.EnumerateFiles(archiveRoot, "*.png", SearchOption.AllDirectories)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(path => new
                {
                    file = Path.GetRelativePath(archiveRoot, path).Replace('\\', '/'),
                    sha256 = Sha256(path),
                    bytes = new FileInfo(path).Length
                })
                .ToArray();
            var report = new
            {
                width = HeroWidth,
                height = HeroHeight,
                selectedCandidate = selected == "none" ? null : $"candidate-{selected}",
                containsReleaseNumber = false,
                screenshotCrop = new
                {
                    ScreenshotCrop.X,
                    ScreenshotCrop.Y,
                    ScreenshotCrop.Width,
                    ScreenshotCrop.Height,
                    excludesApplicationVersion = true
                },
                sources = new
                {
                    logo = Path.GetRelativePath(repoRoot, logoPath).Replace('\\', '/'),
                    screenshot = Path.GetRelativePath(repoRoot, screenshotPath).Replace('\\', '/')
                },
                final = finalPath is null ? null : new
                {
                    file = Path.GetRelativePath(archiveRoot, finalPath).Replace('\\', '/'),
                    sha256 = Sha256(finalPath)
                },
                files = outputFiles
            };
            File.WriteAllText(
                Path.Combine(archiveRoot, "render-report.json"),
                JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
            Console.WriteLine(JsonSerializer.Serialize(report));
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static Bitmap RenderContinuityHero(Image logo, Image screenshot)
    {
        var bitmap = NewCanvas();
        using var graphics = Prepare(bitmap);
        graphics.Clear(Color.FromArgb(6, 17, 31));

        using (var cyanField = new SolidBrush(Color.FromArgb(18, 0, 154, 231)))
            graphics.FillEllipse(cyanField, -320, -405, 860, 860);
        using (var greenField = new SolidBrush(Color.FromArgb(13, 67, 224, 162)))
            graphics.FillEllipse(greenField, 170, 460, 670, 470);

        graphics.DrawImage(logo, new Rectangle(54, 47, 82, 82));
        DrawText(graphics, "PartitionPilot", 155, 51, 300, 42, 29, FontStyle.Bold, Color.FromArgb(247, 250, 255));
        DrawText(graphics, "WINDOWS STORAGE CONTROL", 155, 92, 300, 26, 15, FontStyle.Bold, Color.FromArgb(40, 210, 249));

        DrawText(graphics, "See the plan.", 54, 181, 375, 60, 43, FontStyle.Bold, Color.FromArgb(247, 250, 255));
        DrawText(graphics, "Then touch the disk.", 54, 232, 390, 60, 35, FontStyle.Bold, Color.FromArgb(247, 250, 255));
        DrawText(
            graphics,
            "Partition changes, drive health, and recovery evidence in one guarded workspace.",
            54,
            312,
            382,
            78,
            19,
            FontStyle.Regular,
            Color.FromArgb(177, 194, 214));

        DrawFeatureCard(graphics, "QUEUED CHANGES", new Rectangle(54, 429, 178, 42), Color.FromArgb(39, 205, 248));
        DrawFeatureCard(graphics, "IDENTITY CHECKS", new Rectangle(242, 429, 188, 42), Color.FromArgb(76, 229, 170));
        DrawFeatureCard(graphics, "SMART HEALTH", new Rectangle(54, 482, 178, 42), Color.FromArgb(76, 229, 170));
        DrawFeatureCard(graphics, "GUI + CLI", new Rectangle(242, 482, 188, 42), Color.FromArgb(39, 205, 248));
        DrawText(graphics, "Open source for Windows 10 and 11", 54, 576, 365, 28, 16, FontStyle.Regular, Color.FromArgb(139, 160, 187));

        DrawProductFrame(graphics, screenshot, new Rectangle(467, 57, 779, 526), new Rectangle(488, 112, 737, 400));
        return bitmap;
    }

    private static Bitmap RenderSafetyLedHero(Image logo, Image screenshot)
    {
        var bitmap = NewCanvas();
        using var graphics = Prepare(bitmap);
        graphics.Clear(Color.FromArgb(5, 14, 27));

        using (var topGlow = new SolidBrush(Color.FromArgb(22, 10, 170, 239)))
            graphics.FillEllipse(topGlow, -245, -420, 820, 820);
        using (var bottomGlow = new SolidBrush(Color.FromArgb(18, 67, 224, 162)))
            graphics.FillEllipse(bottomGlow, 85, 470, 730, 460);
        using (var productGlow = new SolidBrush(Color.FromArgb(12, 18, 116, 201)))
            graphics.FillEllipse(productGlow, 760, -160, 660, 820);

        graphics.DrawImage(logo, new Rectangle(55, 48, 72, 72));
        DrawText(graphics, "PartitionPilot", 145, 51, 292, 38, 28, FontStyle.Bold, Color.FromArgb(247, 250, 255));
        DrawText(graphics, "GUARDED WINDOWS DISK MANAGEMENT", 145, 90, 306, 27, 14, FontStyle.Bold, Color.FromArgb(42, 210, 249));

        DrawText(graphics, "Know what changes.", 55, 171, 394, 56, 40, FontStyle.Bold, Color.FromArgb(247, 250, 255));
        DrawText(graphics, "Before anything does.", 55, 220, 400, 56, 35, FontStyle.Bold, Color.FromArgb(247, 250, 255));
        DrawText(graphics, "Review first. Apply when ready.", 55, 286, 392, 42, 22, FontStyle.Bold, Color.FromArgb(77, 231, 173));
        DrawText(
            graphics,
            "Plan partition work beside SMART health, snapshots, and imaging tools.",
            55,
            342,
            383,
            64,
            19,
            FontStyle.Regular,
            Color.FromArgb(178, 196, 216));

        DrawFeatureCard(graphics, "QUEUED CHANGES", new Rectangle(55, 436, 178, 42), Color.FromArgb(39, 205, 248));
        DrawFeatureCard(graphics, "IDENTITY CHECKS", new Rectangle(243, 436, 188, 42), Color.FromArgb(76, 229, 170));
        DrawFeatureCard(graphics, "RECOVERY EVIDENCE", new Rectangle(55, 489, 188, 42), Color.FromArgb(76, 229, 170));
        DrawFeatureCard(graphics, "GUI + CLI", new Rectangle(253, 489, 178, 42), Color.FromArgb(39, 205, 248));
        DrawText(graphics, "Open source for Windows 10 and 11", 55, 577, 365, 27, 16, FontStyle.Regular, Color.FromArgb(139, 160, 187));

        DrawProductFrame(graphics, screenshot, new Rectangle(467, 57, 779, 526), new Rectangle(488, 112, 737, 400));
        return bitmap;
    }

    private static void DrawProductFrame(Graphics graphics, Image screenshot, Rectangle frame, Rectangle screen)
    {
        for (var offset = 18; offset >= 6; offset -= 4)
        {
            using var shadow = new SolidBrush(Color.FromArgb(8 + (18 - offset), 0, 0, 0));
            using var shadowPath = RoundedRectangle(
                new Rectangle(frame.X - offset / 2, frame.Y + offset / 2, frame.Width + offset, frame.Height + offset),
                24);
            graphics.FillPath(shadow, shadowPath);
        }

        using (var frameBrush = new SolidBrush(Color.FromArgb(8, 23, 42)))
        using (var framePath = RoundedRectangle(frame, 20))
            graphics.FillPath(frameBrush, framePath);
        using (var borderPen = new Pen(Color.FromArgb(46, 118, 163), 1.4f))
        using (var framePath = RoundedRectangle(frame, 20))
            graphics.DrawPath(borderPen, framePath);

        using (var dotOne = new SolidBrush(Color.FromArgb(40, 209, 247)))
            graphics.FillEllipse(dotOne, frame.X + 18, frame.Y + 16, 9, 9);
        using (var dotTwo = new SolidBrush(Color.FromArgb(48, 92, 126)))
            graphics.FillEllipse(dotTwo, frame.X + 34, frame.Y + 16, 9, 9);
        using (var dotThree = new SolidBrush(Color.FromArgb(48, 92, 126)))
            graphics.FillEllipse(dotThree, frame.X + 50, frame.Y + 16, 9, 9);
        DrawText(
            graphics,
            "REAL PRODUCT UI  •  SIMULATED DISKS",
            frame.Right - 300,
            frame.Y + 12,
            280,
            22,
            13,
            FontStyle.Bold,
            Color.FromArgb(81, 226, 185),
            StringAlignment.Far);

        var state = graphics.Save();
        using (var clip = RoundedRectangle(screen, 10))
        {
            graphics.SetClip(clip);
            graphics.DrawImage(screenshot, screen, ScreenshotCrop, GraphicsUnit.Pixel);
        }
        graphics.Restore(state);
        using var screenBorder = new Pen(Color.FromArgb(48, 110, 151), 1.1f);
        using var screenPath = RoundedRectangle(screen, 10);
        graphics.DrawPath(screenBorder, screenPath);
    }

    private static void DrawFeatureCard(Graphics graphics, string text, Rectangle bounds, Color accent)
    {
        using var path = RoundedRectangle(bounds, 8);
        using var fill = new SolidBrush(Color.FromArgb(13, 34, 56));
        graphics.FillPath(fill, path);
        using var border = new Pen(Color.FromArgb(68, accent), 1f);
        graphics.DrawPath(border, path);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillRectangle(accentBrush, bounds.X + 11, bounds.Y + 13, 4, 16);
        DrawText(graphics, text, bounds.X + 25, bounds.Y + 10, bounds.Width - 34, 22, 12, FontStyle.Bold, Color.FromArgb(224, 234, 245));
    }

    private static void DrawText(
        Graphics graphics,
        string text,
        float x,
        float y,
        float width,
        float height,
        float size,
        FontStyle style,
        Color color,
        StringAlignment alignment = StringAlignment.Near)
    {
        using var font = Font(size, style);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = alignment,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.LineLimit
        };
        graphics.DrawString(text, font, brush, new RectangleF(x, y, width, height), format);
    }

    private static Font Font(float size, FontStyle style) => new("Segoe UI", size, style, GraphicsUnit.Pixel);

    private static Bitmap NewCanvas() => new(HeroWidth, HeroHeight, PixelFormat.Format24bppRgb);

    private static Graphics Prepare(Bitmap bitmap)
    {
        var graphics = Graphics.FromImage(bitmap);
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        return graphics;
    }

    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static void SaveScaled(Image source, string path, int width, int height)
    {
        using var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using var graphics = Prepare(bitmap);
        graphics.DrawImage(source, new Rectangle(0, 0, width, height));
        SavePng(bitmap, path);
    }

    private static void SaveComparison(Image candidateOne, Image candidateTwo, string path)
    {
        using var bitmap = new Bitmap(1040, 1196, PixelFormat.Format24bppRgb);
        using var graphics = Prepare(bitmap);
        graphics.Clear(Color.FromArgb(240, 245, 250));
        DrawText(graphics, "PartitionPilot README hero review", 40, 25, 960, 42, 28, FontStyle.Bold, Color.FromArgb(11, 28, 49));
        DrawText(graphics, "Candidate 01: brand continuity", 40, 83, 960, 30, 19, FontStyle.Bold, Color.FromArgb(35, 72, 104));
        graphics.DrawImage(candidateOne, new Rectangle(40, 122, 960, 480));
        DrawText(graphics, "Candidate 02: safety-led clarity", 40, 637, 960, 30, 19, FontStyle.Bold, Color.FromArgb(35, 72, 104));
        graphics.DrawImage(candidateTwo, new Rectangle(40, 676, 960, 480));
        SavePng(bitmap, path);
    }

    private static void SaveReferenceComparison(Image reference, Image candidateOne, Image candidateTwo, string path)
    {
        using var bitmap = new Bitmap(1040, 1750, PixelFormat.Format24bppRgb);
        using var graphics = Prepare(bitmap);
        graphics.Clear(Color.FromArgb(240, 245, 250));
        DrawText(graphics, "PartitionPilot README hero review", 40, 25, 960, 42, 28, FontStyle.Bold, Color.FromArgb(11, 28, 49));
        DrawText(graphics, "Reference: previous versioned social card", 40, 83, 960, 30, 19, FontStyle.Bold, Color.FromArgb(35, 72, 104));
        graphics.DrawImage(reference, new Rectangle(40, 122, 960, 480));
        DrawText(graphics, "Candidate 01: brand continuity", 40, 637, 960, 30, 19, FontStyle.Bold, Color.FromArgb(35, 72, 104));
        graphics.DrawImage(candidateOne, new Rectangle(40, 676, 960, 480));
        DrawText(graphics, "Candidate 02: safety-led clarity", 40, 1191, 960, 30, 19, FontStyle.Bold, Color.FromArgb(35, 72, 104));
        graphics.DrawImage(candidateTwo, new Rectangle(40, 1230, 960, 480));
        SavePng(bitmap, path);
    }

    private static void SavePng(Image image, string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        image.Save(path, ImageFormat.Png);
    }

    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Arguments must use --name value pairs.");
            options[args[index][2..]] = args[++index];
        }
        return options;
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(Environment.CurrentDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "README.md")) &&
                File.Exists(Path.Combine(current.FullName, "src", "PartitionPilot", "PartitionPilot.csproj")))
            {
                return current.FullName;
            }
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("Run this tool inside the PartitionPilot repository or pass --repo.");
    }

    private static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Required marketing source was not found.", path);
    }

    private static void ValidateScreenshotCrop(Image screenshot)
    {
        if (ScreenshotCrop.Right > screenshot.Width || ScreenshotCrop.Bottom > screenshot.Height)
            throw new InvalidOperationException("The current product capture is smaller than the reviewed hero crop.");
    }

    private static void RejectReleaseNumbers(IEnumerable<string> copy)
    {
        foreach (var value in copy)
        {
            if (Regex.IsMatch(value, @"\bv?\d+\.\d+\.\d+\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
                throw new InvalidOperationException($"Visible hero copy contains a release number: {value}");
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
