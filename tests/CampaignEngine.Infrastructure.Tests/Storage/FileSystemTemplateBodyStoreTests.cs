using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Exceptions;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="FileSystemTemplateBodyStore"/>.
///
/// US-002 TASK-002-06: Write/read/delete happy-path operations.
/// US-002 TASK-002-07: Exception scenario coverage.
///
/// Each test class instance creates an isolated temp directory and deletes it
/// on disposal, ensuring no cross-test file-system pollution.
/// </summary>
public sealed class FileSystemTemplateBodyStoreTests : IDisposable
{
    private readonly string _rootPath;
    private readonly FileSystemTemplateBodyStore _sut;
    private readonly Mock<IAppLogger<FileSystemTemplateBodyStore>> _logger;

    public FileSystemTemplateBodyStoreTests()
    {
        _rootPath = Path.Combine(Path.GetTempPath(), $"fsts_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_rootPath);

        _logger = new Mock<IAppLogger<FileSystemTemplateBodyStore>>();

        var options = Options.Create(new TemplateBodyStoreOptions
        {
            RootPath = _rootPath
        });

        _sut = new FileSystemTemplateBodyStore(options, _logger.Object);
    }

    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
            Directory.Delete(_rootPath, recursive: true);
    }

    // ================================================================
    // TASK-002-06: Happy-path — WriteAsync
    // ================================================================

    [Fact]
    public async Task WriteAsync_WithValidContent_CreatesFileOnDisk()
    {
        var content = "Hello template!"u8.ToArray();
        using var stream = new MemoryStream(content);

        var returnedPath = await _sut.WriteAsync("email/hello.html", stream);

        var expectedFile = Path.Combine(_rootPath, "email", "hello.html");
        File.Exists(expectedFile).Should().BeTrue();
        (await File.ReadAllBytesAsync(expectedFile)).Should().BeEquivalentTo(content);
        returnedPath.Should().Be("email/hello.html");
    }

    [Fact]
    public async Task WriteAsync_CreatesSubdirectoryWhenMissing()
    {
        using var stream = new MemoryStream("data"u8.ToArray());

        await _sut.WriteAsync("nested/sub/file.html", stream);

        var expectedFile = Path.Combine(_rootPath, "nested", "sub", "file.html");
        File.Exists(expectedFile).Should().BeTrue();
    }

    [Fact]
    public async Task WriteAsync_OverwritesExistingFile()
    {
        var first  = "first"u8.ToArray();
        var second = "second content"u8.ToArray();

        using var stream1 = new MemoryStream(first);
        await _sut.WriteAsync("overwrite.html", stream1);

        using var stream2 = new MemoryStream(second);
        await _sut.WriteAsync("overwrite.html", stream2);

        var result = await File.ReadAllBytesAsync(Path.Combine(_rootPath, "overwrite.html"));
        result.Should().BeEquivalentTo(second);
    }

    [Fact]
    public async Task WriteAsync_NoTempFileLeftAfterSuccess()
    {
        using var stream = new MemoryStream("content"u8.ToArray());

        await _sut.WriteAsync("clean.html", stream);

        var tmpFile = Path.Combine(_rootPath, "clean.html.tmp");
        File.Exists(tmpFile).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_ReturnsSuppliedPathUnchanged()
    {
        using var stream = new MemoryStream("x"u8.ToArray());
        const string relativePath = "channel/template.docx";

        var returned = await _sut.WriteAsync(relativePath, stream);

        returned.Should().Be(relativePath);
    }

    // ================================================================
    // TASK-002-06: Happy-path — ReadAsync
    // ================================================================

    [Fact]
    public async Task ReadAsync_ReturnsStreamWithCorrectContent()
    {
        var expected = "template body content"u8.ToArray();
        await File.WriteAllBytesAsync(Path.Combine(_rootPath, "body.html"), expected);

        await using var stream = await _sut.ReadAsync("body.html");

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(expected);
    }

    [Fact]
    public async Task ReadAsync_ReturnedStreamIsReadable()
    {
        await File.WriteAllTextAsync(Path.Combine(_rootPath, "readable.html"), "<p>test</p>");

        await using var stream = await _sut.ReadAsync("readable.html");

        stream.Should().NotBeNull();
        stream.CanRead.Should().BeTrue();
    }

    [Fact]
    public async Task ReadAsync_RoundTrip_WriteAndReadProducesIdenticalBytes()
    {
        var original = new byte[] { 0x50, 0x4B, 0x03, 0x04 }; // DOCX magic bytes
        using var writeStream = new MemoryStream(original);
        await _sut.WriteAsync("letter.docx", writeStream);

        await using var readStream = await _sut.ReadAsync("letter.docx");

        using var ms = new MemoryStream();
        await readStream.CopyToAsync(ms);
        ms.ToArray().Should().BeEquivalentTo(original);
    }

    // ================================================================
    // TASK-002-06: Happy-path — DeleteAsync
    // ================================================================

    [Fact]
    public async Task DeleteAsync_ExistingFile_RemovesItFromDisk()
    {
        var filePath = Path.Combine(_rootPath, "todelete.html");
        await File.WriteAllTextAsync(filePath, "content");

        await _sut.DeleteAsync("todelete.html");

        File.Exists(filePath).Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_MissingFile_IsNoOp()
    {
        // No file written; should not throw
        var act = () => _sut.DeleteAsync("nonexistent.html");

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DeleteAsync_NullOrEmptyPath_IsNoOp()
    {
        var actNull  = () => _sut.DeleteAsync(null!);
        var actEmpty = () => _sut.DeleteAsync(string.Empty);
        var actWhite = () => _sut.DeleteAsync("   ");

        await actNull.Should().NotThrowAsync();
        await actEmpty.Should().NotThrowAsync();
        await actWhite.Should().NotThrowAsync();
    }

    // ================================================================
    // TASK-002-07: Exception scenarios — ReadAsync
    // ================================================================

    [Fact]
    public async Task ReadAsync_NullPath_ThrowsTemplateBodyNotFoundException()
    {
        var act = () => _sut.ReadAsync(null!);

        await act.Should().ThrowAsync<TemplateBodyNotFoundException>();
    }

    [Fact]
    public async Task ReadAsync_EmptyPath_ThrowsTemplateBodyNotFoundException()
    {
        var act = () => _sut.ReadAsync(string.Empty);

        await act.Should().ThrowAsync<TemplateBodyNotFoundException>();
    }

    [Fact]
    public async Task ReadAsync_WhitespacePath_ThrowsTemplateBodyNotFoundException()
    {
        var act = () => _sut.ReadAsync("   ");

        await act.Should().ThrowAsync<TemplateBodyNotFoundException>();
    }

    [Fact]
    public async Task ReadAsync_FileDoesNotExist_ThrowsTemplateBodyNotFoundException()
    {
        var act = () => _sut.ReadAsync("missing/file.html");

        await act.Should().ThrowAsync<TemplateBodyNotFoundException>()
            .Where(ex => ex.Path == "missing/file.html");
    }

    [Fact]
    public async Task ReadAsync_NotFoundExceptionContainsRequestedPath()
    {
        const string path = "templates/email/missing.html";
        var act = () => _sut.ReadAsync(path);

        var ex = await act.Should().ThrowAsync<TemplateBodyNotFoundException>();
        ex.Which.Path.Should().Be(path);
    }

    // ================================================================
    // TASK-002-07: Exception scenarios — WriteAsync
    // ================================================================

    [Fact]
    public async Task WriteAsync_NullPath_ThrowsArgumentException()
    {
        using var stream = new MemoryStream("x"u8.ToArray());

        var act = () => _sut.WriteAsync(null!, stream);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_EmptyPath_ThrowsArgumentException()
    {
        using var stream = new MemoryStream("x"u8.ToArray());

        var act = () => _sut.WriteAsync(string.Empty, stream);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task WriteAsync_NullStream_ThrowsArgumentNullException()
    {
        var act = () => _sut.WriteAsync("file.html", null!);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task WriteAsync_UnreadableStream_ThrowsTemplateBodyCorruptedException()
    {
        // Use a closed stream to simulate an I/O failure during copy
        var closedStream = new MemoryStream();
        closedStream.Close();

        var act = () => _sut.WriteAsync("bad.html", closedStream);

        await act.Should().ThrowAsync<TemplateBodyCorruptedException>()
            .Where(ex => ex.Path == "bad.html");
    }

    [Fact]
    public async Task WriteAsync_CorruptedExceptionContainsPath()
    {
        var closedStream = new MemoryStream();
        closedStream.Close();

        var act = () => _sut.WriteAsync("templates/fail.html", closedStream);

        var ex = await act.Should().ThrowAsync<TemplateBodyCorruptedException>();
        ex.Which.Path.Should().Be("templates/fail.html");
    }

    [Fact]
    public async Task WriteAsync_CorruptedException_HasInnerException()
    {
        var closedStream = new MemoryStream();
        closedStream.Close();

        var act = () => _sut.WriteAsync("fail.html", closedStream);

        var ex = await act.Should().ThrowAsync<TemplateBodyCorruptedException>();
        ex.Which.InnerException.Should().NotBeNull();
    }
}
