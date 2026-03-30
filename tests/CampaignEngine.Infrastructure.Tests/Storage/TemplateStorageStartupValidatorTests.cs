using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Startup;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Tests.Storage;

/// <summary>
/// Unit tests for <see cref="TemplateStorageStartupValidator"/>.
///
/// US-004 TASK-004-07: Validate RootPath null/empty, non-existent, and non-writable scenarios.
///
/// Each test that touches the file system creates an isolated temp directory and
/// deletes it in Dispose to avoid cross-test pollution.
/// </summary>
public sealed class TemplateStorageStartupValidatorTests : IDisposable
{
    private readonly Mock<IAppLogger<TemplateStorageStartupValidator>> _logger;
    private readonly List<string> _tempDirs = new();

    public TemplateStorageStartupValidatorTests()
    {
        _logger = new Mock<IAppLogger<TemplateStorageStartupValidator>>();
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best-effort cleanup
            }
        }
    }

    // ================================================================
    // Helpers
    // ================================================================

    private TemplateStorageStartupValidator CreateSut(string rootPath)
    {
        var options = Options.Create(new TemplateStorageOptions { RootPath = rootPath });
        return new TemplateStorageStartupValidator(options, _logger.Object);
    }

    private string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tssv_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    // ================================================================
    // Null / empty path — TASK-004-04 (null/empty guard)
    // ================================================================

    [Fact]
    public void ValidateRootPath_WhenRootPathIsNull_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(null!);
        var act = () => sut.ValidateRootPath(null!);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*TemplateStorage:RootPath is not configured*");
    }

    [Fact]
    public void ValidateRootPath_WhenRootPathIsEmpty_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(string.Empty);
        var act = () => sut.ValidateRootPath(string.Empty);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*TemplateStorage:RootPath is not configured*");
    }

    [Fact]
    public void ValidateRootPath_WhenRootPathIsWhitespace_ThrowsInvalidOperationException()
    {
        var sut = CreateSut("   ");
        var act = () => sut.ValidateRootPath("   ");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*TemplateStorage:RootPath is not configured*");
    }

    [Fact]
    public void ValidateRootPath_WhenRootPathIsNull_LogsError()
    {
        var sut = CreateSut(null!);
        try { sut.ValidateRootPath(null!); } catch { /* expected */ }

        _logger.Verify(l => l.LogError(
            It.IsAny<InvalidOperationException>(),
            It.Is<string>(m => m.Contains("TemplateStorage:RootPath is not configured")),
            It.IsAny<object[]>()), Times.Once);
    }

    // ================================================================
    // Directory does not exist — TASK-004-04 (existence check)
    // ================================================================

    [Fact]
    public void ValidateRootPath_WhenDirectoryDoesNotExist_ThrowsInvalidOperationException()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"tssv_nonexistent_{Guid.NewGuid():N}");
        var sut = CreateSut(nonExistentPath);

        var act = () => sut.ValidateRootPath(nonExistentPath);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage($"*'{nonExistentPath}' does not exist*");
    }

    [Fact]
    public void ValidateRootPath_WhenDirectoryDoesNotExist_LogsError()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"tssv_nonexistent_{Guid.NewGuid():N}");
        var sut = CreateSut(nonExistentPath);

        try { sut.ValidateRootPath(nonExistentPath); } catch { /* expected */ }

        _logger.Verify(l => l.LogError(
            It.IsAny<InvalidOperationException>(),
            It.Is<string>(m => m.Contains("does not exist")),
            It.IsAny<object[]>()), Times.Once);
    }

    // ================================================================
    // Directory is writable — TASK-004-05 (happy path)
    // ================================================================

    [Fact]
    public void ValidateRootPath_WhenDirectoryExistsAndIsWritable_DoesNotThrow()
    {
        var writableDir = CreateTempDir();
        var sut = CreateSut(writableDir);

        var act = () => sut.ValidateRootPath(writableDir);

        act.Should().NotThrow();
    }

    [Fact]
    public void ValidateRootPath_WhenDirectoryExistsAndIsWritable_LogsInformation()
    {
        var writableDir = CreateTempDir();
        var sut = CreateSut(writableDir);

        sut.ValidateRootPath(writableDir);

        _logger.Verify(l => l.LogInformation(
            It.Is<string>(m => m.Contains("startup validation passed")),
            It.IsAny<object[]>()), Times.Once);
    }

    [Fact]
    public void ValidateRootPath_WhenWritable_DoesNotLeaveStartupCheckFile()
    {
        var writableDir = CreateTempDir();
        var sut = CreateSut(writableDir);

        sut.ValidateRootPath(writableDir);

        var checkFile = Path.Combine(writableDir, ".startup_check");
        File.Exists(checkFile).Should().BeFalse(
            "the temp check file must be deleted after a successful validation");
    }

    // ================================================================
    // StartAsync delegates to ValidateRootPath
    // ================================================================

    [Fact]
    public async Task StartAsync_WhenRootPathIsValid_CompletesWithoutException()
    {
        var writableDir = CreateTempDir();
        var sut = CreateSut(writableDir);

        await sut.StartAsync(CancellationToken.None);
        // No exception thrown — test passes.
    }

    [Fact]
    public async Task StartAsync_WhenRootPathIsEmpty_ThrowsInvalidOperationException()
    {
        var sut = CreateSut(string.Empty);

        var act = async () => await sut.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*TemplateStorage:RootPath is not configured*");
    }

    [Fact]
    public async Task StartAsync_WhenDirectoryDoesNotExist_ThrowsInvalidOperationException()
    {
        var nonExistentPath = Path.Combine(Path.GetTempPath(), $"tssv_nonexistent_{Guid.NewGuid():N}");
        var sut = CreateSut(nonExistentPath);

        var act = async () => await sut.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage($"*'{nonExistentPath}' does not exist*");
    }
}
