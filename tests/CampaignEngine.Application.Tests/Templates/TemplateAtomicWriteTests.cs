using CampaignEngine.Application.DTOs.Templates;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Application.Interfaces.Repositories;
using CampaignEngine.Application.Interfaces.Storage;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Domain.Enums;
using CampaignEngine.Domain.Exceptions;
using CampaignEngine.Infrastructure.Persistence;
using CampaignEngine.Infrastructure.Persistence.Repositories;
using CampaignEngine.Infrastructure.Templates;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Application.Tests.Templates;

/// <summary>
/// Unit tests for US-005: Atomic file write with concurrency guard.
/// TASK-005-05: Create success path.
/// TASK-005-06: DB failure → file cleanup.
/// TASK-005-07: Concurrent update → 409 (ConcurrencyException).
/// </summary>
public class TemplateAtomicWriteTests : IDisposable
{
    private readonly CampaignEngineDbContext _context;
    private readonly Mock<ITemplateBodyStore> _bodyStoreMock;
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly Mock<ITemplateRepository> _templateRepositoryMock;

    public TemplateAtomicWriteTests()
    {
        var options = new DbContextOptionsBuilder<CampaignEngineDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new CampaignEngineDbContext(options);
        _bodyStoreMock = new Mock<ITemplateBodyStore>();
        _unitOfWorkMock = new Mock<IUnitOfWork>();
        _templateRepositoryMock = new Mock<ITemplateRepository>();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private TemplateService BuildService(
        ITemplateRepository? templateRepository = null,
        IUnitOfWork? unitOfWork = null,
        ITemplateBodyStore? bodyStore = null)
    {
        var logger = new Mock<IAppLogger<TemplateService>>();
        var manifestService = new Mock<IPlaceholderManifestService>();
        var parserService = new Mock<IPlaceholderParserService>();

        return new TemplateService(
            templateRepository ?? _templateRepositoryMock.Object,
            unitOfWork ?? _unitOfWorkMock.Object,
            logger.Object,
            manifestService.Object,
            parserService.Object,
            bodyStore ?? _bodyStoreMock.Object);
    }

    // ----------------------------------------------------------------
    // TASK-005-05: Create success path
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WithDocxContent_WritesFileThenCommits()
    {
        // Arrange — real repo + real UoW backed by in-memory DB
        var templateRepository = new TemplateRepository(_context);
        var unitOfWork = new UnitOfWork(_context);

        var docxStream = new MemoryStream(new byte[] { 1, 2, 3 });
        string? capturedWritePath = null;

        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((path, _, _) => capturedWritePath = path)
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        var service = BuildService(templateRepository, unitOfWork, _bodyStoreMock.Object);

        var request = new CreateTemplateRequest
        {
            Name = "Docx Template",
            Channel = ChannelType.Letter,
            DocxContent = docxStream
        };

        // Act
        var result = await service.CreateAsync(request);

        // Assert — file was written
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), docxStream, It.IsAny<CancellationToken>()),
            Times.Once);

        // Assert — BodyPath on entity matches the path that was written
        result.BodyPath.Should().Be(capturedWritePath);
        result.BodyPath.Should().Contain("v1.docx");

        // Assert — delete was NOT called (no failure)
        _bodyStoreMock.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CreateAsync_WithHtmlContent_WritesFileThenCommits()
    {
        // Arrange
        var templateRepository = new TemplateRepository(_context);
        var unitOfWork = new UnitOfWork(_context);

        var htmlStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes("<html></html>"));
        string? capturedWritePath = null;

        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((path, _, _) => capturedWritePath = path)
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        var service = BuildService(templateRepository, unitOfWork, _bodyStoreMock.Object);

        var request = new CreateTemplateRequest
        {
            Name = "Html Template",
            Channel = ChannelType.Email,
            HtmlContent = htmlStream
        };

        // Act
        var result = await service.CreateAsync(request);

        // Assert — file was written
        _bodyStoreMock.Verify(
            s => s.WriteAsync(It.IsAny<string>(), htmlStream, It.IsAny<CancellationToken>()),
            Times.Once);

        result.BodyPath.Should().Be(capturedWritePath);
        result.BodyPath.Should().Contain("v1.html");
    }

    // ----------------------------------------------------------------
    // TASK-005-06: DB failure → file cleanup
    // ----------------------------------------------------------------

    [Fact]
    public async Task CreateAsync_WhenDbCommitFails_DeletesWrittenFile()
    {
        // Arrange — mock unit of work that throws on CommitAsync
        _unitOfWorkMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated DB failure"));

        _templateRepositoryMock
            .Setup(r => r.NameExistsAsync(
                It.IsAny<string>(), It.IsAny<ChannelType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _templateRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<Template>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var docxStream = new MemoryStream(new byte[] { 1, 2, 3 });
        string? writtenPath = null;

        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((path, _, _) => writtenPath = path)
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        _bodyStoreMock
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = BuildService();

        var request = new CreateTemplateRequest
        {
            Name = "Orphan Test",
            Channel = ChannelType.Letter,
            DocxContent = docxStream
        };

        // Act — should throw the DB exception
        var act = () => service.CreateAsync(request);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert — the file that was written must be deleted on cleanup
        writtenPath.Should().NotBeNull();
        _bodyStoreMock.Verify(
            s => s.DeleteAsync(writtenPath!, CancellationToken.None),
            Times.Once);
    }

    [Fact]
    public async Task CreateAsync_WhenNoFileWritten_DbFailureDoesNotCallDelete()
    {
        // Arrange — template with no file stream (BodyPath supplied directly)
        _unitOfWorkMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated DB failure"));

        _templateRepositoryMock
            .Setup(r => r.NameExistsAsync(
                It.IsAny<string>(), It.IsAny<ChannelType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _templateRepositoryMock
            .Setup(r => r.AddAsync(It.IsAny<Template>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = BuildService();

        var request = new CreateTemplateRequest
        {
            Name = "No File Template",
            Channel = ChannelType.Email,
            BodyPath = "templates/test/v1.html"
            // No DocxContent, no HtmlContent
        };

        // Act
        var act = () => service.CreateAsync(request);
        await act.Should().ThrowAsync<InvalidOperationException>();

        // Assert — no file was written so no cleanup should happen
        _bodyStoreMock.Verify(
            s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ----------------------------------------------------------------
    // TASK-005-07: Concurrent update → 409 (ConcurrencyException)
    // ----------------------------------------------------------------

    [Fact]
    public async Task UpdateAsync_WhenDbUpdateConcurrencyException_ThrowsConcurrencyException()
    {
        // Arrange — existing tracked template
        var existingTemplate = new Template
        {
            Name = "Concurrent Template",
            Channel = ChannelType.Letter,
            BodyPath = "templates/abc/v1.docx",
            Version = 1
        };

        _templateRepositoryMock
            .Setup(r => r.GetTrackedAsync(existingTemplate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTemplate);

        _templateRepositoryMock
            .Setup(r => r.NameExistsAsync(
                It.IsAny<string>(), It.IsAny<ChannelType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _templateRepositoryMock
            .Setup(r => r.AddHistoryAsync(It.IsAny<TemplateHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // UoW throws DbUpdateConcurrencyException
        _unitOfWorkMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                "Concurrency token mismatch"));

        var service = BuildService();

        var request = new UpdateTemplateRequest
        {
            Name = "Concurrent Template",
            BodyPath = "templates/abc/v1.docx"
        };

        // Act
        var act = () => service.UpdateAsync(existingTemplate.Id, request);

        // Assert — should surface as ConcurrencyException (maps to HTTP 409)
        await act.Should().ThrowAsync<ConcurrencyException>()
            .WithMessage("*modified by another request*");
    }

    [Fact]
    public async Task UpdateAsync_WhenConcurrencyExceptionAndFileWasWritten_DeletesOrphanedFile()
    {
        // Arrange — existing tracked template
        var existingTemplate = new Template
        {
            Name = "Concurrent With File",
            Channel = ChannelType.Letter,
            BodyPath = "templates/abc/v1.docx",
            Version = 1
        };

        _templateRepositoryMock
            .Setup(r => r.GetTrackedAsync(existingTemplate.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingTemplate);

        _templateRepositoryMock
            .Setup(r => r.NameExistsAsync(
                It.IsAny<string>(), It.IsAny<ChannelType>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        _templateRepositoryMock
            .Setup(r => r.AddHistoryAsync(It.IsAny<TemplateHistory>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        string? writtenPath = null;
        _bodyStoreMock
            .Setup(s => s.WriteAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<string, Stream, CancellationToken>((path, _, _) => writtenPath = path)
            .ReturnsAsync((string path, Stream _, CancellationToken _) => path);

        _bodyStoreMock
            .Setup(s => s.CopyAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _bodyStoreMock
            .Setup(s => s.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException(
                "Concurrency token mismatch"));

        var service = BuildService();

        var docxStream = new MemoryStream(new byte[] { 1, 2, 3 });

        var request = new UpdateTemplateRequest
        {
            Name = "Concurrent With File",
            DocxContent = docxStream
        };

        // Act
        var act = () => service.UpdateAsync(existingTemplate.Id, request);
        await act.Should().ThrowAsync<ConcurrencyException>();

        // Assert — the newly written file must be cleaned up
        writtenPath.Should().NotBeNull();
        _bodyStoreMock.Verify(
            s => s.DeleteAsync(writtenPath!, CancellationToken.None),
            Times.Once);
    }
}
