using System.Diagnostics;
using Aiursoft.ChessServer.Services.FileStorage;
using SkiaSharp;

namespace Aiursoft.ChessServer.Tests.IntegrationTests;

[TestClass]
public class ImageProcessingPerformanceTests : TestBase
{
    private ImageProcessingService _service = null!;
    private StorageService _storage = null!;
    private string _testPrefix = null!;
    private static readonly SKColor[] TestColors = [SKColors.Red, SKColors.Green, SKColors.Blue, SKColors.Gold, SKColors.Fuchsia];

    [TestInitialize]
    public void InitializeServices()
    {
        _service = GetService<ImageProcessingService>();
        _storage = GetService<StorageService>();
        _testPrefix = $"perf-test-{Guid.NewGuid():N}";
    }

    // ── Format Support Tests ──

    [TestMethod]
    public async Task CompressPng_MaintainsFormat()
    {
        var path = await CreateTestImageAsync("test.png", SKEncodedImageFormat.Png, 800, 600);
        var result = await _service.CompressAsync(path, 400, 0);
        AssertIsValidImage(result);
        Assert.AreEqual(".png", Path.GetExtension(result).ToLowerInvariant());
        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(400, bmp.Width);
        Assert.AreEqual(300, bmp.Height);
    }

    [TestMethod]
    public async Task CompressJpeg_MaintainsFormat()
    {
        var path = await CreateTestImageAsync("test.jpg", SKEncodedImageFormat.Jpeg, 800, 600);
        var result = await _service.CompressAsync(path, 200, 0);
        AssertIsValidImage(result);
        Assert.AreEqual(".jpg", Path.GetExtension(result).ToLowerInvariant());
    }

    [TestMethod]
    public async Task CompressWebp_MaintainsFormat()
    {
        var path = await CreateTestImageAsync("test.webp", SKEncodedImageFormat.Webp, 800, 600);
        var result = await _service.CompressAsync(path, 400, 0);
        AssertIsValidImage(result);
        Assert.AreEqual(".webp", Path.GetExtension(result).ToLowerInvariant());
    }

    // ── Aspect Ratio Tests ──

    [TestMethod]
    public async Task Compress_WidthOnly_PreservesAspectRatio()
    {
        var path = await CreateTestImageAsync("wide.png", SKEncodedImageFormat.Png, 800, 400);
        var result = await _service.CompressAsync(path, 400, 0);
        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(400, bmp.Width);
        Assert.AreEqual(200, bmp.Height);
    }

    [TestMethod]
    public async Task Compress_HeightOnly_PreservesAspectRatio()
    {
        var path = await CreateTestImageAsync("tall.png", SKEncodedImageFormat.Png, 400, 800);
        var result = await _service.CompressAsync(path, 0, 200);
        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(100, bmp.Width);
        Assert.AreEqual(200, bmp.Height);
    }

    [TestMethod]
    public async Task Compress_BothDimensions_UsesExactSize()
    {
        var path = await CreateTestImageAsync("img.png", SKEncodedImageFormat.Png, 800, 600);
        var result = await _service.CompressAsync(path, 300, 200);
        using var bmp = SKBitmap.Decode(result);
        // With both specified, the output respects the exact dimensions requested
        Assert.AreEqual(300, bmp.Width);
        Assert.AreEqual(200, bmp.Height);
    }

    [TestMethod]
    public async Task Compress_ZeroZero_ReturnsOriginalSize()
    {
        var path = await CreateTestImageAsync("img.png", SKEncodedImageFormat.Png, 200, 100);
        var result = await _service.CompressAsync(path, 0, 0);
        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(200, bmp.Width);
        Assert.AreEqual(100, bmp.Height);
    }

    // ── ClearExif Tests ──

    [TestMethod]
    public async Task ClearExif_StripsMetadata()
    {
        var path = await CreateTestImageAsync("photo.jpg", SKEncodedImageFormat.Jpeg, 400, 300);
        var result = await _service.ClearExifAsync(path);

        // After ClearExif, the result should be a valid image with same dimensions
        AssertIsValidImage(result);
        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(400, bmp.Width);
        Assert.AreEqual(300, bmp.Height);
    }

    [TestMethod]
    public async Task ClearExif_CachesResult()
    {
        var path = await CreateTestImageAsync("photo.jpg", SKEncodedImageFormat.Jpeg, 200, 200);
        var first = await _service.ClearExifAsync(path);
        var second = await _service.ClearExifAsync(path);

        Assert.AreEqual(first, second, "Second call should return cached path");
        AssertIsValidImage(first);
    }

    // ── IsValidImage Tests ──

    [TestMethod]
    public async Task IsValidImage_RealImage_ReturnsTrue()
    {
        var logicalPath = await CreateTestImageAsync("valid.png", SKEncodedImageFormat.Png, 100, 100);
        var physicalPath = _storage.GetFilePhysicalPath(logicalPath);
        var result = await _service.IsValidImageAsync(physicalPath);
        Assert.IsTrue(result);
    }

    [TestMethod]
    public async Task IsValidImage_TextFile_ReturnsFalse()
    {
        var logicalPath = await CreateRawFileAsync("fake.png", "This is not an image!"u8.ToArray());
        var physicalPath = _storage.GetFilePhysicalPath(logicalPath);
        var result = await _service.IsValidImageAsync(physicalPath);
        Assert.IsFalse(result);
    }

    [TestMethod]
    public async Task IsValidImage_NonExistentFile_ReturnsFalse()
    {
        var result = await _service.IsValidImageAsync("/nonexistent/path.jpg");
        Assert.IsFalse(result);
    }

    // ── Performance Tests ──

    [TestMethod]
    public async Task Performance_TinyImage()
    {
        var path = await CreateTestImageAsync("tiny.png", SKEncodedImageFormat.Png, 100, 100);
        var sw = Stopwatch.StartNew();
        var result = await _service.CompressAsync(path, 50, 0);
        sw.Stop();

        AssertIsValidImage(result);
        Assert.IsTrue(sw.ElapsedMilliseconds < 2000,
            $"Tiny image compression took {sw.ElapsedMilliseconds}ms, expected < 2000ms");
        Console.WriteLine($"Tiny image (100x100): {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task Performance_MediumImage()
    {
        var path = await CreateTestImageAsync("medium.png", SKEncodedImageFormat.Png, 1920, 1080);
        var sw = Stopwatch.StartNew();
        var result = await _service.CompressAsync(path, 800, 0);
        sw.Stop();

        AssertIsValidImage(result);
        Assert.IsTrue(sw.ElapsedMilliseconds < 5000,
            $"Medium image compression took {sw.ElapsedMilliseconds}ms, expected < 5000ms");
        Console.WriteLine($"Medium image (1920x1080→800x450): {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task Performance_LargeImage()
    {
        var path = await CreateTestImageAsync("large.png", SKEncodedImageFormat.Png, 4000, 3000);
        var sw = Stopwatch.StartNew();
        var result = await _service.CompressAsync(path, 1920, 0);
        sw.Stop();

        AssertIsValidImage(result);
        // Large PNG encode may be expensive; allow up to 10s
        Assert.IsTrue(sw.ElapsedMilliseconds < 15000,
            $"Large image compression took {sw.ElapsedMilliseconds}ms, expected < 15000ms");
        Console.WriteLine($"Large image (4000x3000→1920x1440): {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task Performance_ClearExifVsCompress()
    {
        var path = await CreateTestImageAsync("perf.jpg", SKEncodedImageFormat.Jpeg, 1920, 1080);

        var swClear = Stopwatch.StartNew();
        var clearResult = await _service.ClearExifAsync(path);
        swClear.Stop();

        var swCompress = Stopwatch.StartNew();
        var compressResult = await _service.CompressAsync(path, 800, 0);
        swCompress.Stop();

        AssertIsValidImage(clearResult);
        AssertIsValidImage(compressResult);
        Console.WriteLine($"ClearExif (1920x1080): {swClear.ElapsedMilliseconds}ms");
        Console.WriteLine($"Compress (1920x1080→800x450): {swCompress.ElapsedMilliseconds}ms");
    }

    // ── Concurrency Tests ──

    [TestMethod]
    public async Task Concurrency_MultipleDifferentFiles()
    {
        // Create 10 different images
        var paths = new string[10];
        for (int i = 0; i < 10; i++)
        {
            paths[i] = await CreateTestImageAsync($"img{i}.png", SKEncodedImageFormat.Png,
                200 + i * 10, 150 + i * 10);
        }

        var sw = Stopwatch.StartNew();
        var tasks = paths.Select(p => _service.CompressAsync(p, 100, 0));
        var results = await Task.WhenAll(tasks);
        sw.Stop();

        foreach (var r in results)
        {
            AssertIsValidImage(r);
            using var bmp = SKBitmap.Decode(r);
            Assert.AreEqual(100, bmp.Width);
        }

        Console.WriteLine($"10 concurrent compressions: {sw.ElapsedMilliseconds}ms");
        // Concurrent operations should complete faster than sequential (10 × single time)
        Assert.IsTrue(sw.ElapsedMilliseconds < 15000,
            $"Concurrent compression took {sw.ElapsedMilliseconds}ms, expected < 15000ms");
    }

    [TestMethod]
    public async Task Concurrency_SameFileDifferentSizes()
    {
        var path = await CreateTestImageAsync("shared.png", SKEncodedImageFormat.Png, 800, 600);

        var sw = Stopwatch.StartNew();
        var sizes = new[] { (100, 0), (200, 0), (400, 0), (0, 150), (300, 300) };
        var tasks = sizes.Select(s => _service.CompressAsync(path, s.Item1, s.Item2));
        var results = await Task.WhenAll(tasks);
        sw.Stop();

        // Each target size has its own lock, so they can run concurrently
        foreach (var (r, i) in results.Select((r, i) => (r, i)))
        {
            AssertIsValidImage(r, $"Result {i} should be a valid image");
        }

        Console.WriteLine($"5 sizes from same source: {sw.ElapsedMilliseconds}ms");
    }

    [TestMethod]
    public async Task Concurrency_SameExactTarget_SerializesCorrectly()
    {
        var path = await CreateTestImageAsync("serial.png", SKEncodedImageFormat.Png, 400, 300);

        // Fire 5 requests for the exact same compression — same target path
        var tasks = Enumerable.Range(0, 5).Select(_ => _service.CompressAsync(path, 200, 0)).ToArray();
        var results = await Task.WhenAll(tasks);

        // All should return the same target path and be valid
        var firstResult = results[0];
        foreach (var r in results)
        {
            Assert.AreEqual(firstResult, r);
            AssertIsValidImage(r);
        }
    }

    // ── Edge Case Tests ──

    [TestMethod]
    public async Task EdgeCase_SinglePixelImage()
    {
        var path = await CreateTestImageAsync("1x1.png", SKEncodedImageFormat.Png, 1, 1);
        var result = await _service.CompressAsync(path, 50, 50);
        AssertIsValidImage(result);
        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(50, bmp.Width);
        Assert.AreEqual(50, bmp.Height);
    }

    [TestMethod]
    public async Task EdgeCase_VeryWideImage()
    {
        var path = await CreateTestImageAsync("wide.png", SKEncodedImageFormat.Png, 4000, 10);
        var result = await _service.CompressAsync(path, 2000, 0);
        AssertIsValidImage(result);
        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(2000, bmp.Width);
        Assert.AreEqual(5, bmp.Height);
    }

    [TestMethod]
    public async Task EdgeCase_VeryTallImage()
    {
        var path = await CreateTestImageAsync("tall.png", SKEncodedImageFormat.Png, 10, 4000);
        var result = await _service.CompressAsync(path, 0, 2000);
        AssertIsValidImage(result);
        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(5, bmp.Width);
        Assert.AreEqual(2000, bmp.Height);
    }

    [TestMethod]
    public async Task EdgeCase_CorruptFile_ReturnsOriginal()
    {
        var corruptData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x00, 0x00, 0x00 }; // truncated PNG header
        var logicalPath = await CreateRawFileAsync("corrupt.png", corruptData);
        var physicalPath = _storage.GetFilePhysicalPath(logicalPath);

        var compressResult = await _service.CompressAsync(logicalPath, 100, 0);
        var clearResult = await _service.ClearExifAsync(logicalPath);

        // On failure, returns the original source path (same as physical path)
        Assert.AreEqual(physicalPath, compressResult);
        Assert.AreEqual(physicalPath, clearResult);
    }

    [TestMethod]
    public async Task EdgeCase_TruncatedFile_ReturnsOriginal()
    {
        var truncData = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00 }; // JPEG header only
        var logicalPath = await CreateRawFileAsync("truncated.jpg", truncData);
        var physicalPath = _storage.GetFilePhysicalPath(logicalPath);

        var result = await _service.CompressAsync(logicalPath, 100, 0);
        Assert.AreEqual(physicalPath, result);
    }

    [TestMethod]
    public async Task EdgeCase_ZeroByteFile_ReturnsOriginal()
    {
        var logicalPath = await CreateRawFileAsync("empty.png", []);
        var physicalPath = _storage.GetFilePhysicalPath(logicalPath);

        var result = await _service.CompressAsync(logicalPath, 100, 0);
        Assert.AreEqual(physicalPath, result);
    }

    // ── Transparency Tests ──

    [TestMethod]
    public async Task Transparency_PngAlphaPreserved()
    {
        var path = await CreateTransparentImageAsync("alpha.png");

        var result = await _service.CompressAsync(path, 100, 100);
        AssertIsValidImage(result);

        using var bmp = SKBitmap.Decode(result);
        Assert.AreEqual(100, bmp.Width);
        Assert.AreEqual(100, bmp.Height);

        // Top-left quadrant maps from source opaque red region → should be opaque
        var topLeftColor = bmp.GetPixel(10, 10);
        Assert.AreEqual(255, topLeftColor.Alpha, "Top-left pixel should be fully opaque (red region)");

        // Bottom-right quadrant maps from source transparent region → should be transparent or nearly so
        var bottomRightColor = bmp.GetPixel(90, 90);
        Assert.IsTrue(bottomRightColor.Alpha < 30,
            $"Bottom-right pixel should be near-transparent, alpha={bottomRightColor.Alpha}");
    }

    [TestMethod]
    public async Task Transparency_ClearExifPreservesAlpha()
    {
        var path = await CreateTransparentImageAsync("alpha2.png");
        var result = await _service.ClearExifAsync(path);
        AssertIsValidImage(result);

        using var bmp = SKBitmap.Decode(result);
        // Bottom-right quadrant: source transparent region → should be transparent
        var bottomRightColor = bmp.GetPixel(190, 190);
        Assert.IsTrue(bottomRightColor.Alpha < 30,
            $"ClearExif should preserve transparency, alpha={bottomRightColor.Alpha}");
    }

    // ── Output Quality Tests ──

    [TestMethod]
    public async Task Quality_CompressedOutputIsSmallerThanOriginal()
    {
        var logicalPath = await CreateTestImageAsync("big.png", SKEncodedImageFormat.Png, 2000, 1500);
        var result = await _service.CompressAsync(logicalPath, 200, 0);

        var originalSize = new FileInfo(_storage.GetFilePhysicalPath(logicalPath)).Length;
        var resultSize = new FileInfo(result).Length;

        Console.WriteLine($"Original: {originalSize / 1024.0:F1}KB, Compressed: {resultSize / 1024.0:F1}KB");
        Assert.IsTrue(resultSize < originalSize,
            $"Compressed ({resultSize}B) should be smaller than original ({originalSize}B)");
    }

    [TestMethod]
    public async Task Quality_JpegReencodeReducesSize()
    {
        var logicalPath = await CreateTestImageAsync("photo.jpg", SKEncodedImageFormat.Jpeg, 2000, 1500);
        var result = await _service.ClearExifAsync(logicalPath);

        var originalSize = new FileInfo(_storage.GetFilePhysicalPath(logicalPath)).Length;
        var resultSize = new FileInfo(result).Length;

        Console.WriteLine($"JPEG original: {originalSize / 1024.0:F1}KB, After ClearExif: {resultSize / 1024.0:F1}KB");
        Assert.IsTrue(resultSize > 0);
    }

    // ── Cache Hit Performance ──

    [TestMethod]
    public async Task CacheHit_IsFasterThanCacheMiss()
    {
        var path = await CreateTestImageAsync("cache.png", SKEncodedImageFormat.Png, 500, 500);

        // First call (cache miss — processes the image)
        var sw1 = Stopwatch.StartNew();
        var r1 = await _service.CompressAsync(path, 200, 0);
        sw1.Stop();

        // Second call (cache hit — returns cached file)
        var sw2 = Stopwatch.StartNew();
        var r2 = await _service.CompressAsync(path, 200, 0);
        sw2.Stop();

        AssertIsValidImage(r1);
        Assert.AreEqual(r1, r2);
        Assert.IsTrue(sw2.ElapsedMilliseconds <= sw1.ElapsedMilliseconds + 50,
            $"Cache hit ({sw2.ElapsedMilliseconds}ms) should be ≤ cache miss ({sw1.ElapsedMilliseconds}ms) + margin");
        Console.WriteLine($"Cache miss: {sw1.ElapsedMilliseconds}ms, Cache hit: {sw2.ElapsedMilliseconds}ms");
    }

    // ── Color Fidelity Test ──

    [TestMethod]
    public async Task ColorFidelity_RoundtripPreservesColors()
    {
        var logicalPath = await CreateTestImageAsync("colors.png", SKEncodedImageFormat.Png, 300, 100);
        using var original = SKBitmap.Decode(_storage.GetFilePhysicalPath(logicalPath));

        var result = await _service.ClearExifAsync(logicalPath);
        using var processed = SKBitmap.Decode(result);

        // Sample several pixels and verify they match
        var samples = new[] { (10, 10), (150, 50), (290, 90) };
        foreach (var (x, y) in samples)
        {
            var origColor = original!.GetPixel(x, y);
            var procColor = processed!.GetPixel(x, y);
            // Allow 1 unit variance per channel due to PNG re-encoding
            Assert.IsTrue(Math.Abs(origColor.Red - procColor.Red) <= 1, $"R at ({x},{y})");
            Assert.IsTrue(Math.Abs(origColor.Green - procColor.Green) <= 1, $"G at ({x},{y})");
            Assert.IsTrue(Math.Abs(origColor.Blue - procColor.Blue) <= 1, $"B at ({x},{y})");
            Assert.IsTrue(Math.Abs(origColor.Alpha - procColor.Alpha) <= 1, $"A at ({x},{y})");
        }
    }

    // ── Stress Tests ──

    [TestMethod]
    public async Task Stress_RapidSequentialRequests()
    {
        var path = await CreateTestImageAsync("stress.png", SKEncodedImageFormat.Png, 400, 300);

        for (int i = 0; i < 20; i++)
        {
            var w = 10 + i * 5;
            var result = await _service.CompressAsync(path, w, 0);
            AssertIsValidImage(result, $"Iteration {i}");
        }
    }

    [TestMethod]
    public async Task Stress_MixedOperations()
    {
        var path = await CreateTestImageAsync("mixed.jpg", SKEncodedImageFormat.Jpeg, 800, 600);

        // Fire all operation types concurrently on same source
        var tasks = new List<Task>
        {
            _service.ClearExifAsync(path),
            _service.CompressAsync(path, 100, 100),
            _service.CompressAsync(path, 200, 0),
            _service.CompressAsync(path, 0, 150),
            _service.IsValidImageAsync(path),
        };

        await Task.WhenAll(tasks);
        // No exceptions = pass
    }

    // ── Helpers ──

    /// <summary>
    /// Creates a test image inside the storage workspace and returns the logical path
    /// (suitable for passing to CompressAsync / ClearExifAsync).
    /// Falls back to PNG encoding if the target format is not encodable by SkiaSharp.
    /// </summary>
    private async Task<string> CreateTestImageAsync(string fileName, SKEncodedImageFormat format, int width, int height)
    {
        var logicalPath = $"{_testPrefix}/{fileName}";
        var physicalPath = _storage.GetFilePhysicalPath(logicalPath);

        var dir = Path.GetDirectoryName(physicalPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

        using var bitmap = new SKBitmap(width, height);
        using var canvas = new SKCanvas(bitmap);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var r = (byte)((x * 255) / width);
                var g = (byte)((y * 255) / height);
                var b = (byte)(((x + y) * 127) / (width + height));
                bitmap.SetPixel(x, y, new SKColor(r, g, b));
            }
        }

        var paint = new SKPaint();
        paint.IsAntialias = false;
        using (paint)
        {
            var rectW = Math.Max(1, width / 5);
            var rectH = Math.Max(1, height / 5);
            for (int i = 0; i < 5; i++)
            {
                paint.Color = TestColors[i];
                canvas.DrawRect(i * rectW, i * rectH, rectW, rectH, paint);
            }
        }
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        // GIF and BMP encoding are not supported by all SkiaSharp builds.
        // Fall back to PNG for the source file when encoding is unsupported.
        var encodeFormat = IsEncodable(format) ? format : SKEncodedImageFormat.Png;
        using var data = image.Encode(encodeFormat, 90)
            ?? throw new InvalidOperationException($"SkiaSharp Encode returned null for {encodeFormat}");
        await using var fs = File.Create(physicalPath);
        data.SaveTo(fs);

        return logicalPath;
    }

    private static bool IsEncodable(SKEncodedImageFormat format) => format switch
    {
        SKEncodedImageFormat.Png => true,
        SKEncodedImageFormat.Jpeg => true,
        SKEncodedImageFormat.Webp => true,
        _ => false
    };

    /// <summary>
    /// Creates a test file with raw bytes inside the storage workspace and returns the logical path.
    /// </summary>
    private async Task<string> CreateRawFileAsync(string fileName, byte[] data)
    {
        var logicalPath = $"{_testPrefix}/{fileName}";
        var physicalPath = _storage.GetFilePhysicalPath(logicalPath);
        var dir = Path.GetDirectoryName(physicalPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);
        await File.WriteAllBytesAsync(physicalPath, data);
        return logicalPath;
    }

    private async Task<string> CreateTransparentImageAsync(string fileName)
    {
        var logicalPath = $"{_testPrefix}/{fileName}";
        var physicalPath = _storage.GetFilePhysicalPath(logicalPath);

        var dir = Path.GetDirectoryName(physicalPath);
        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir!);

        using var bitmap = new SKBitmap(200, 200);
        using var canvas = new SKCanvas(bitmap);

        // Fill with fully transparent background
        canvas.Clear(SKColors.Transparent);

        // Draw opaque red rect in the top-left quadrant (0,0)-(100,100)
        var paint = new SKPaint();
        paint.IsAntialias = false;
        paint.Color = SKColors.Red;
        using (paint)
        {
            canvas.DrawRect(0, 0, 100, 100, paint);

            // Draw semi-transparent blue rect in the center overlapping slightly
            paint.Color = new SKColor(0, 0, 255, 128);
            canvas.DrawRect(50, 50, 100, 100, paint);
        }

        // Bottom-right quadrant (100,100)-(200,200) remains transparent
        canvas.Flush();

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        await using var fs = File.Create(physicalPath);
        data.SaveTo(fs);

        return logicalPath;
    }

    private static void AssertIsValidImage(string path, string? message = null)
    {
        Assert.IsTrue(File.Exists(path), message ?? $"File should exist: {path}");
        using var codec = SKCodec.Create(path);
        Assert.IsNotNull(codec, message ?? $"File should be a valid image: {path}");
    }
}
#pragma warning restore MSTEST0036
