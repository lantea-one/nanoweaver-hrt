using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Moq;
using NanoWeaverHrt.Security.Integrity;
using Xunit;

namespace NanoWeaverHrt.Tests;

/// <summary>
/// Unit and integration tests for <see cref="AssetIntegrityService"/> verifying W3C Subresource Integrity
/// calculation, path normalization, caching behavior, and error handling.
/// </summary>
public sealed class AssetIntegrityServiceTests
{
    /// <summary>
    /// Verifies that constructing <see cref="AssetIntegrityService"/> with a null environment throws <see cref="ArgumentNullException"/>.
    /// </summary>
    [Fact]
    public void Constructor_NullEnvironment_ThrowsArgumentNullException()
    {
        // WHAT: Tests null guard on constructor.
        // WHY:  AssetIntegrityService requires a valid IWebHostEnvironment.
        // HOW:  Asserts that passing null throws ArgumentNullException.
        Assert.Throws<ArgumentNullException>(() => new AssetIntegrityService(null!));
    }

    /// <summary>
    /// Verifies that passing null, empty, or whitespace relative paths returns an empty string without throwing.
    /// </summary>
    /// <param name="invalidPath">The invalid or empty relative path.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\r\n")]
    public void GetSubresourceIntegrity_NullOrWhitespace_ReturnsEmptyString(string? invalidPath)
    {
        // WHAT: Tests empty or whitespace guard on GetSubresourceIntegrity.
        // WHY:  Invalid paths must not trigger file system probes or exceptions.
        // HOW:  Calls GetSubresourceIntegrity with invalid input and asserts string.Empty.
        Mock<IWebHostEnvironment> envMock = new();
        AssetIntegrityService service = new(envMock.Object);

        string result = service.GetSubresourceIntegrity(invalidPath!);
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that when an asset exists via <see cref="IFileProvider"/>, its SHA-384 digest is computed,
    /// formatted as W3C SRI, and served from cache on subsequent calls.
    /// </summary>
    [Fact]
    public void GetSubresourceIntegrity_ValidFileFromProvider_ComputesAndCachesDigest()
    {
        // WHAT: Tests successful SRI computation from IFileProvider.
        // WHY:  Standard ASP.NET Core asset hosting relies on WebRootFileProvider.
        // HOW:  Mocks IFileInfo with sample bytes, verifies sha384 prefix, and checks caching.
        byte[] testBytes = Encoding.UTF8.GetBytes("console.log('test-asset-script');");
        byte[] expectedHash = SHA384.HashData(testBytes);
        string expectedSri = $"sha384-{Convert.ToBase64String(expectedHash)}";

        Mock<IFileInfo> fileMock = new();
        fileMock.SetupGet(f => f.Exists).Returns(true);
        fileMock.Setup(f => f.CreateReadStream()).Returns(() => new MemoryStream(testBytes));

        Mock<IFileProvider> providerMock = new();
        providerMock.Setup(p => p.GetFileInfo("js/test.js")).Returns(fileMock.Object);

        Mock<IWebHostEnvironment> envMock = new();
        envMock.SetupGet(e => e.WebRootFileProvider).Returns(providerMock.Object);

        AssetIntegrityService service = new(envMock.Object);

        // First call computes hash from provider stream
        string result1 = service.GetSubresourceIntegrity("js/test.js");
        Assert.Equal(expectedSri, result1);
        fileMock.Verify(f => f.CreateReadStream(), Times.Once);

        // Second call retrieves from cache without re-invoking CreateReadStream
        string result2 = service.GetSubresourceIntegrity("js/test.js");
        Assert.Equal(expectedSri, result2);
        fileMock.Verify(f => f.CreateReadStream(), Times.Once);
    }

    /// <summary>
    /// Verifies that paths with leading slashes, tildes, and backslashes normalize to canonical keys
    /// and hit the same cache entry.
    /// </summary>
    /// <param name="pathVariation">Path formatted with tilde, leading slash, or backslash.</param>
    [Theory]
    [InlineData("js/app.js")]
    [InlineData("/js/app.js")]
    [InlineData("///js/app.js")]
    [InlineData("~/js/app.js")]
    [InlineData("~//js/app.js")]
    [InlineData("js\\app.js")]
    [InlineData("  ~/js\\app.js  ")]
    public void GetSubresourceIntegrity_PathVariations_NormalizeDeterministically(string pathVariation)
    {
        // WHAT: Tests path normalization across varied formats.
        // WHY:  Razor templates may reference assets as "~/js/app.js" or "/js/app.js".
        // HOW:  Mocks file provider for canonical key and asserts normalized path lookup.
        byte[] testBytes = Encoding.UTF8.GetBytes("const app = 42;");
        byte[] expectedHash = SHA384.HashData(testBytes);
        string expectedSri = $"sha384-{Convert.ToBase64String(expectedHash)}";

        Mock<IFileInfo> fileMock = new();
        fileMock.SetupGet(f => f.Exists).Returns(true);
        fileMock.Setup(f => f.CreateReadStream()).Returns(() => new MemoryStream(testBytes));

        Mock<IFileProvider> providerMock = new();
        providerMock.Setup(p => p.GetFileInfo("js/app.js")).Returns(fileMock.Object);

        Mock<IWebHostEnvironment> envMock = new();
        envMock.SetupGet(e => e.WebRootFileProvider).Returns(providerMock.Object);

        AssetIntegrityService service = new(envMock.Object);

        string result = service.GetSubresourceIntegrity(pathVariation);
        Assert.Equal(expectedSri, result);
    }

    /// <summary>
    /// Verifies that when an asset cannot be found anywhere, an empty string is returned.
    /// </summary>
    [Fact]
    public void GetSubresourceIntegrity_NonExistentAsset_ReturnsEmptyString()
    {
        // WHAT: Tests lookup failure when asset does not exist.
        // WHY:  Missing files must not throw unhandled exceptions or crash page rendering.
        // HOW:  Mocks IFileInfo with Exists = false and invalid disk paths, asserting string.Empty.
        Mock<IFileInfo> fileMock = new();
        fileMock.SetupGet(f => f.Exists).Returns(false);

        Mock<IFileProvider> providerMock = new();
        providerMock.Setup(p => p.GetFileInfo(It.IsAny<string>())).Returns(fileMock.Object);

        Mock<IWebHostEnvironment> envMock = new();
        envMock.SetupGet(e => e.WebRootFileProvider).Returns(providerMock.Object);
        envMock.SetupGet(e => e.WebRootPath).Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        envMock.SetupGet(e => e.ContentRootPath).Returns(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));

        AssetIntegrityService service = new(envMock.Object);

        string result = service.GetSubresourceIntegrity("js/nonexistent.js");
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that when a file exists but contains zero bytes, an empty string is returned.
    /// </summary>
    [Fact]
    public void GetSubresourceIntegrity_EmptyFile_ReturnsEmptyString()
    {
        // WHAT: Tests zero-byte file handling.
        // WHY:  Empty files do not require integrity checking and must not produce dummy hashes.
        // HOW:  Mocks IFileInfo returning an empty byte stream and asserts string.Empty.
        Mock<IFileInfo> fileMock = new();
        fileMock.SetupGet(f => f.Exists).Returns(true);
        fileMock.Setup(f => f.CreateReadStream()).Returns(() => new MemoryStream([]));

        Mock<IFileProvider> providerMock = new();
        providerMock.Setup(p => p.GetFileInfo(It.IsAny<string>())).Returns(fileMock.Object);

        Mock<IWebHostEnvironment> envMock = new();
        envMock.SetupGet(e => e.WebRootFileProvider).Returns(providerMock.Object);

        AssetIntegrityService service = new(envMock.Object);

        string result = service.GetSubresourceIntegrity("js/empty.js");
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that when the file provider returns a non-existent file, the service falls back to physical disk lookup.
    /// </summary>
    [Fact]
    public void GetSubresourceIntegrity_ProviderMissing_FallsBackToDiskPath()
    {
        // WHAT: Tests physical disk candidate directory fallback logic.
        // WHY:  In development and containerized test hosts, WebRootFileProvider may not be attached.
        // HOW:  Creates a temporary file in a simulated WebRoot directory and verifies successful hashing.
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver_asset_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string jsDir = Path.Combine(tempDir, "js");
            Directory.CreateDirectory(jsDir);
            string filePath = Path.Combine(jsDir, "fallback.js");
            byte[] fileBytes = Encoding.UTF8.GetBytes("window.fallbackLoaded = true;");
            File.WriteAllBytes(filePath, fileBytes);

            byte[] expectedHash = SHA384.HashData(fileBytes);
            string expectedSri = $"sha384-{Convert.ToBase64String(expectedHash)}";

            Mock<IFileProvider> emptyProvider = new();
            Mock<IFileInfo> missingFile = new();
            missingFile.SetupGet(f => f.Exists).Returns(false);
            emptyProvider.Setup(p => p.GetFileInfo(It.IsAny<string>())).Returns(missingFile.Object);

            Mock<IWebHostEnvironment> envMock = new();
            envMock.SetupGet(e => e.WebRootFileProvider).Returns(emptyProvider.Object);
            envMock.SetupGet(e => e.WebRootPath).Returns(tempDir);

            AssetIntegrityService service = new(envMock.Object);

            string result = service.GetSubresourceIntegrity("js/fallback.js");
            Assert.Equal(expectedSri, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup failure in test teardown
                }
            }
        }
    }

    /// <summary>
    /// Verifies that when the file provider and WebRootPath are missing the file, ContentRootPath/wwwroot is used as fallback.
    /// </summary>
    [Fact]
    public void GetSubresourceIntegrity_ContentRootFallback_ComputesDigest()
    {
        // WHAT: Tests physical disk ContentRootPath/wwwroot fallback logic.
        // WHY:  Ensures that when WebRootPath is not set, ContentRootPath/wwwroot resolves assets.
        // HOW:  Creates temporary wwwroot inside a simulated ContentRootPath directory and verifies hashing.
        string tempDir = Path.Combine(Path.GetTempPath(), "weaver_content_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            string wwwrootDir = Path.Combine(tempDir, "wwwroot", "js");
            Directory.CreateDirectory(wwwrootDir);
            string filePath = Path.Combine(wwwrootDir, "content-fallback.js");
            byte[] fileBytes = Encoding.UTF8.GetBytes("window.contentFallbackLoaded = true;");
            File.WriteAllBytes(filePath, fileBytes);

            byte[] expectedHash = SHA384.HashData(fileBytes);
            string expectedSri = $"sha384-{Convert.ToBase64String(expectedHash)}";

            Mock<IFileProvider> emptyProvider = new();
            Mock<IFileInfo> missingFile = new();
            missingFile.SetupGet(f => f.Exists).Returns(false);
            emptyProvider.Setup(p => p.GetFileInfo(It.IsAny<string>())).Returns(missingFile.Object);

            Mock<IWebHostEnvironment> envMock = new();
            envMock.SetupGet(e => e.WebRootFileProvider).Returns(emptyProvider.Object);
            envMock.SetupGet(e => e.WebRootPath).Returns(string.Empty);
            envMock.SetupGet(e => e.ContentRootPath).Returns(tempDir);

            AssetIntegrityService service = new(envMock.Object);

            string result = service.GetSubresourceIntegrity("js/content-fallback.js");
            Assert.Equal(expectedSri, result);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try
                {
                    Directory.Delete(tempDir, recursive: true);
                }
                catch
                {
                    // Ignore cleanup failure in test teardown
                }
            }
        }
    }

    /// <summary>
    /// Verifies that when WebRootFileProvider is null and disk paths do not contain the file, an empty string is returned.
    /// </summary>
    [Fact]
    public void GetSubresourceIntegrity_NullProviderAndMissingDiskFiles_ReturnsEmpty()
    {
        // WHAT: Tests null WebRootFileProvider with non-existent disk paths.
        // WHY:  Guarantees null provider does not throw and handles missing disk assets.
        // HOW:  Configures environment with null provider and non-existent paths, asserting string.Empty.
        Mock<IWebHostEnvironment> envMock = new();
        // Null WebRootFileProvider tested to verify defensive null-propagation when ASP.NET environment is uninitialized.
        envMock.SetupGet(e => e.WebRootFileProvider).Returns((IFileProvider)null! /* Justified: Simulating uninitialized provider */);
        envMock.SetupGet(e => e.WebRootPath).Returns(string.Empty);
        envMock.SetupGet(e => e.ContentRootPath).Returns(string.Empty);

        AssetIntegrityService service = new(envMock.Object);

        string result = service.GetSubresourceIntegrity("js/missing-nowhere.js");
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Verifies that <see cref="AssetIntegrityService.ClearCache"/> empties the cache, forcing fresh reads.
    /// </summary>
    [Fact]
    public void ClearCache_InvalidatesCache_ForcesRecomputation()
    {
        // WHAT: Tests ClearCache cache invalidation behavior.
        // WHY:  Allows flushing stale digests during dynamic file updates.
        // HOW:  Populates cache, clears cache, and verifies provider stream is queried again.
        byte[] testBytes = Encoding.UTF8.GetBytes("var x = 1;");

        Mock<IFileInfo> fileMock = new();
        fileMock.SetupGet(f => f.Exists).Returns(true);
        fileMock.Setup(f => f.CreateReadStream()).Returns(() => new MemoryStream(testBytes));

        Mock<IFileProvider> providerMock = new();
        providerMock.Setup(p => p.GetFileInfo("js/clear.js")).Returns(fileMock.Object);

        Mock<IWebHostEnvironment> envMock = new();
        envMock.SetupGet(e => e.WebRootFileProvider).Returns(providerMock.Object);

        AssetIntegrityService service = new(envMock.Object);

        string first = service.GetSubresourceIntegrity("js/clear.js");
        Assert.False(string.IsNullOrEmpty(first));
        fileMock.Verify(f => f.CreateReadStream(), Times.Once);

        service.ClearCache();

        string second = service.GetSubresourceIntegrity("js/clear.js");
        Assert.Equal(first, second);
        fileMock.Verify(f => f.CreateReadStream(), Times.Exactly(2));
    }

    /// <summary>
    /// Integration test verifying that real authored assets in the repository
    /// (css/app.css, js/app.js, js/weaver-session.js, js/weaver-auth.js, js/weaver-visualizer.js,
    /// and lib/weaver-crypto-vendor.js) are resolved and their SHA-384 SRI digests are generated.
    /// </summary>
    [Fact]
    public void Integration_AuthoredAssets_ResolveAndComputeExactSriDigests()
    {
        // WHAT: Verifies real static files in src/NanoWeaverHrt/wwwroot against live AssetIntegrityService.
        // WHY:  Ensures that App.razor can resolve all authored scripts and styles during startup.
        // HOW:  Points IWebHostEnvironment at the real solution wwwroot directory and asserts non-empty SRI digests.
        string solutionRoot = TestFixturesHelper.GetSolutionRoot();
        string wwwroot = Path.Combine(solutionRoot, "src", "NanoWeaverHrt", "wwwroot");
        Assert.True(Directory.Exists(wwwroot), $"wwwroot must exist at {wwwroot}");

        PhysicalFileProvider physicalProvider = new(wwwroot);
        Mock<IWebHostEnvironment> envMock = new();
        envMock.SetupGet(e => e.WebRootFileProvider).Returns(physicalProvider);
        envMock.SetupGet(e => e.WebRootPath).Returns(wwwroot);
        envMock.SetupGet(e => e.ContentRootPath).Returns(Path.Combine(solutionRoot, "src", "NanoWeaverHrt"));

        AssetIntegrityService service = new(envMock.Object);

        string[] authoredAssets =
        [
            "css/app.css",
            "js/app.js",
            "js/weaver-session.js",
            "js/weaver-auth.js",
            "js/weaver-visualizer.js",
            "lib/weaver-crypto-vendor.js"
        ];

        foreach (string asset in authoredAssets)
        {
            string sri = service.GetSubresourceIntegrity(asset);
            Assert.False(string.IsNullOrEmpty(sri), $"SRI for '{asset}' must not be empty.");
            Assert.StartsWith("sha384-", sri, StringComparison.Ordinal);
        }

        // Verify the vendored crypto module matches the expected pinned SRI hash
        string vendorSri = service.GetSubresourceIntegrity("lib/weaver-crypto-vendor.js");
        Assert.Equal("sha384-2PWlMHMrV/enpeAeEB0EtHqLjYv9Rql4aPLZNGynBOQPFkoacC01+5WwbCsr8lgD", vendorSri);
    }
}
