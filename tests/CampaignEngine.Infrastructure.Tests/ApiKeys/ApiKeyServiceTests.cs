using CampaignEngine.Application.DTOs.ApiKeys;
using CampaignEngine.Application.Interfaces;
using CampaignEngine.Domain.Entities;
using CampaignEngine.Infrastructure.ApiKeys;
using CampaignEngine.Infrastructure.Tests.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CampaignEngine.Infrastructure.Tests.ApiKeys;

/// <summary>
/// Integration tests for ApiKeyService using the EF Core in-memory provider.
/// Covers key creation (BCrypt hashing), validation, revocation, and rotation.
///
/// Note: BCrypt.Verify is called on each ValidateAsync — test execution is intentionally
/// slightly slower (~100ms per BCrypt call) but is required for correctness verification.
/// </summary>
public class ApiKeyServiceTests : DbContextTestBase
{
    private readonly ApiKeyService _service;

    public ApiKeyServiceTests()
    {
        var logger = new Mock<IAppLogger<ApiKeyService>>();
        _service = new ApiKeyService(Context, logger.Object);
    }

    // ----------------------------------------------------------------
    // CreateAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task Create_ValidRequest_PersistsKeyAndReturnsPlaintext()
    {
        // Arrange
        var request = new CreateApiKeyRequest
        {
            Name = "Test Key",
            Description = "Integration test",
            ExpiresInDays = 365
        };

        // Act
        var response = await _service.CreateAsync(request, "admin");

        // Assert
        response.Should().NotBeNull();
        response.PlaintextKey.Should().StartWith("ce_");
        response.PlaintextKey.Should().HaveLength(35); // "ce_" + 32 Base64 chars
        response.Key.Name.Should().Be("Test Key");
        response.Key.IsActive.Should().BeTrue();
        response.Key.ExpiresAt.Should().NotBeNull();

        // Verify persisted
        var persisted = await Context.ApiKeys.FirstOrDefaultAsync(k => k.Id == response.Key.Id);
        persisted.Should().NotBeNull();
        persisted!.KeyHash.Should().NotBeNullOrEmpty();
        // Plaintext must NOT be stored
        persisted.KeyHash.Should().NotBe(response.PlaintextKey);
        // BCrypt hashes start with $2
        persisted.KeyHash.Should().StartWith("$2");
    }

    [Fact]
    public async Task Create_DuplicateName_ThrowsValidationException()
    {
        // Arrange
        var request = new CreateApiKeyRequest { Name = "Duplicate Key" };
        await _service.CreateAsync(request, "admin");

        // Act & Assert
        var act = async () => await _service.CreateAsync(request, "admin");
        await act.Should().ThrowAsync<CampaignEngine.Domain.Exceptions.ValidationException>()
            .WithMessage("*already exists*");
    }

    [Fact]
    public async Task Create_WithExpiresInDays_SetsExpiresAt()
    {
        // Arrange
        var request = new CreateApiKeyRequest { Name = "Expiring Key", ExpiresInDays = 30 };

        // Act
        var response = await _service.CreateAsync(request, "admin");

        // Assert
        response.Key.ExpiresAt.Should().NotBeNull();
        response.Key.ExpiresAt!.Value.Should().BeCloseTo(DateTime.UtcNow.AddDays(30), precision: TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Create_WithNoExpiry_ExpiresAtIsNull()
    {
        // Arrange
        var request = new CreateApiKeyRequest { Name = "Never Expiring Key", ExpiresInDays = null };

        // Act
        var response = await _service.CreateAsync(request, "admin");

        // Assert
        response.Key.ExpiresAt.Should().BeNull();
    }

    [Fact]
    public async Task Create_StoresKeyPrefix()
    {
        // Arrange
        var request = new CreateApiKeyRequest { Name = "Prefix Test Key" };

        // Act
        var response = await _service.CreateAsync(request, "admin");

        // Assert — prefix must be the first 8 chars of the plaintext key
        var persisted = await Context.ApiKeys.FirstAsync(k => k.Id == response.Key.Id);
        persisted.KeyPrefix.Should().Be(response.PlaintextKey[..8]);
    }

    // ----------------------------------------------------------------
    // ValidateAsync — happy path
    // ----------------------------------------------------------------

    [Fact]
    public async Task Validate_WithValidKey_ReturnsApiKeyAndUpdatesLastUsedAt()
    {
        // Arrange
        var created = await _service.CreateAsync(
            new CreateApiKeyRequest { Name = "Valid Key" }, "admin");

        // Act
        var result = await _service.ValidateAsync(created.PlaintextKey);

        // Assert
        result.Should().NotBeNull();
        result!.Name.Should().Be("Valid Key");
        result.LastUsedAt.Should().NotBeNull();
        result.LastUsedAt!.Value.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    // ----------------------------------------------------------------
    // ValidateAsync — negative cases
    // ----------------------------------------------------------------

    [Fact]
    public async Task Validate_WithWrongKey_ReturnsNull()
    {
        // Arrange — create a key but validate with a different value
        await _service.CreateAsync(new CreateApiKeyRequest { Name = "Another Key" }, "admin");

        // Act
        var result = await _service.ValidateAsync("ce_wrongkeyvalue_00000000000000000");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Validate_WithRevokedKey_ReturnsNull()
    {
        // Arrange
        var created = await _service.CreateAsync(
            new CreateApiKeyRequest { Name = "To Revoke" }, "admin");
        await _service.RevokeAsync(created.Key.Id);

        // Act
        var result = await _service.ValidateAsync(created.PlaintextKey);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Validate_WithExpiredKey_ReturnsNull()
    {
        // Arrange — create a key and manually set ExpiresAt to the past
        var created = await _service.CreateAsync(
            new CreateApiKeyRequest { Name = "Expired Key", ExpiresInDays = 365 }, "admin");

        var entity = await Context.ApiKeys.FirstAsync(k => k.Id == created.Key.Id);
        entity.ExpiresAt = DateTime.UtcNow.AddDays(-1); // Expired yesterday
        await Context.SaveChangesAsync();

        // Act
        var result = await _service.ValidateAsync(created.PlaintextKey);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task Validate_WithEmptyKey_ReturnsNull()
    {
        var result = await _service.ValidateAsync(string.Empty);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Validate_WithShortKey_ReturnsNull()
    {
        // Key shorter than 8 chars cannot have a valid prefix
        var result = await _service.ValidateAsync("ce_123");
        result.Should().BeNull();
    }

    // ----------------------------------------------------------------
    // RevokeAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task Revoke_ActiveKey_SetsIsActiveFalse()
    {
        // Arrange
        var created = await _service.CreateAsync(
            new CreateApiKeyRequest { Name = "Revoke Me" }, "admin");

        // Act
        await _service.RevokeAsync(created.Key.Id);

        // Assert
        var persisted = await Context.ApiKeys.FirstAsync(k => k.Id == created.Key.Id);
        persisted.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task Revoke_AlreadyRevokedKey_ThrowsValidationException()
    {
        // Arrange
        var created = await _service.CreateAsync(
            new CreateApiKeyRequest { Name = "Already Revoked" }, "admin");
        await _service.RevokeAsync(created.Key.Id);

        // Act & Assert
        var act = async () => await _service.RevokeAsync(created.Key.Id);
        await act.Should().ThrowAsync<CampaignEngine.Domain.Exceptions.ValidationException>()
            .WithMessage("*already revoked*");
    }

    [Fact]
    public async Task Revoke_NonExistentKey_ThrowsNotFoundException()
    {
        var act = async () => await _service.RevokeAsync(Guid.NewGuid());
        await act.Should().ThrowAsync<CampaignEngine.Domain.Exceptions.NotFoundException>();
    }

    // ----------------------------------------------------------------
    // RotateAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task Rotate_ActiveKey_RevokesOldAndCreatesNew()
    {
        // Arrange
        var created = await _service.CreateAsync(
            new CreateApiKeyRequest { Name = "Rotate Me" }, "admin");

        // Act
        var rotated = await _service.RotateAsync(created.Key.Id, "admin");

        // Assert — old key is revoked
        var oldKey = await Context.ApiKeys.FirstAsync(k => k.Id == created.Key.Id);
        oldKey.IsActive.Should().BeFalse();

        // New key is active and has a different ID
        rotated.Key.IsActive.Should().BeTrue();
        rotated.Key.Id.Should().NotBe(created.Key.Id);
        rotated.PlaintextKey.Should().StartWith("ce_");

        // New plaintext key should actually validate
        var validated = await _service.ValidateAsync(rotated.PlaintextKey);
        validated.Should().NotBeNull();
    }

    [Fact]
    public async Task Rotate_NonExistentKey_ThrowsNotFoundException()
    {
        var act = async () => await _service.RotateAsync(Guid.NewGuid(), "admin");
        await act.Should().ThrowAsync<CampaignEngine.Domain.Exceptions.NotFoundException>();
    }

    // ----------------------------------------------------------------
    // GetAllAsync / GetByIdAsync
    // ----------------------------------------------------------------

    [Fact]
    public async Task GetAll_ReturnsAllKeys_IncludingRevoked()
    {
        // Arrange
        var k1 = await _service.CreateAsync(new CreateApiKeyRequest { Name = "Key 1" }, "admin");
        var k2 = await _service.CreateAsync(new CreateApiKeyRequest { Name = "Key 2" }, "admin");
        await _service.RevokeAsync(k2.Key.Id);

        // Act
        var all = await _service.GetAllAsync();

        // Assert
        all.Should().HaveCountGreaterThanOrEqualTo(2);
        all.Should().Contain(k => k.Id == k1.Key.Id);
        all.Should().Contain(k => k.Id == k2.Key.Id && !k.IsActive);
    }

    [Fact]
    public async Task GetById_ExistingKey_ReturnsDto()
    {
        // Arrange
        var created = await _service.CreateAsync(
            new CreateApiKeyRequest { Name = "GetById Key" }, "admin");

        // Act
        var result = await _service.GetByIdAsync(created.Key.Id);

        // Assert
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Key.Id);
        result.Name.Should().Be("GetById Key");
    }

    [Fact]
    public async Task GetById_NonExistentKey_ReturnsNull()
    {
        var result = await _service.GetByIdAsync(Guid.NewGuid());
        result.Should().BeNull();
    }
}
