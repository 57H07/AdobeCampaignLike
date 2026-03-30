using CampaignEngine.Application.Interfaces;
using CampaignEngine.Infrastructure.Configuration;
using CampaignEngine.Infrastructure.Identity;
using CampaignEngine.Infrastructure.Persistence.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CampaignEngine.Infrastructure.Tests.Persistence;

/// <summary>
/// Unit tests for <see cref="AdminSeeder"/>.
/// Uses Moq to mock UserManager and RoleManager to avoid any database dependency.
/// Verifies idempotency (no-op when admin exists) and creation (when no admin exists).
/// </summary>
public class AdminSeederTests
{
    // ----------------------------------------------------------------
    // Test helpers
    // ----------------------------------------------------------------

    private static Mock<UserManager<ApplicationUser>> CreateUserManagerMock()
    {
        // UserManager has a complex constructor — Moq requires we pass the minimal deps.
        var store = new Mock<IUserStore<ApplicationUser>>();
        return new Mock<UserManager<ApplicationUser>>(
            store.Object,
            null!, null!, null!, null!, null!, null!, null!, null!);
    }

    private static Mock<RoleManager<ApplicationRole>> CreateRoleManagerMock()
    {
        var store = new Mock<IRoleStore<ApplicationRole>>();
        return new Mock<RoleManager<ApplicationRole>>(
            store.Object, null!, null!, null!, null!);
    }

    private static DefaultAdminOptions DefaultOptions() => new()
    {
        UserName = "admin",
        Email = "admin@campaignengine.local",
        Password = "Admin@1234!",
        DisplayName = "Default Administrator"
    };

    private static DefaultAdminOptions CustomOptions() => new()
    {
        UserName = "sysadmin",
        Email = "sysadmin@mycompany.com",
        Password = "Str0ng&UniqueP@ss!",
        DisplayName = "System Administrator"
    };

    private static AdminSeeder CreateSeeder(
        Mock<UserManager<ApplicationUser>> userManagerMock,
        Mock<RoleManager<ApplicationRole>> roleManagerMock,
        DefaultAdminOptions? options = null)
    {
        var opts = Options.Create(options ?? DefaultOptions());
        var logger = new Mock<IAppLogger<AdminSeeder>>();
        return new AdminSeeder(
            userManagerMock.Object,
            roleManagerMock.Object,
            opts,
            logger.Object);
    }

    // ----------------------------------------------------------------
    // Role seeding
    // ----------------------------------------------------------------

    [Fact]
    public async Task SeedAsync_WhenRolesMissing_CreatesAllRoles()
    {
        // Arrange
        var userMgr = CreateUserManagerMock();
        var roleMgr = CreateRoleManagerMock();

        // No roles exist
        roleMgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(false);
        roleMgr.Setup(r => r.CreateAsync(It.IsAny<ApplicationRole>()))
            .ReturnsAsync(IdentityResult.Success);

        // Simulate no admin users so seeder proceeds past the idempotency check
        userMgr.Setup(u => u.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<ApplicationUser>());
        userMgr.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        userMgr.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var seeder = CreateSeeder(userMgr, roleMgr);

        // Act
        await seeder.SeedAsync();

        // Assert — CreateAsync called once per role (Admin, Designer, Operator)
        roleMgr.Verify(r => r.CreateAsync(It.IsAny<ApplicationRole>()), Times.Exactly(3));
    }

    [Fact]
    public async Task SeedAsync_WhenRolesAlreadyExist_DoesNotRecreateRoles()
    {
        // Arrange
        var userMgr = CreateUserManagerMock();
        var roleMgr = CreateRoleManagerMock();

        // All roles already exist
        roleMgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        userMgr.Setup(u => u.GetUsersInRoleAsync(It.IsAny<string>()))
            .ReturnsAsync(new List<ApplicationUser>());
        userMgr.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        userMgr.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var seeder = CreateSeeder(userMgr, roleMgr);

        // Act
        await seeder.SeedAsync();

        // Assert — CreateAsync never called because roles exist
        roleMgr.Verify(r => r.CreateAsync(It.IsAny<ApplicationRole>()), Times.Never);
    }

    // ----------------------------------------------------------------
    // Admin user seeding — idempotency
    // ----------------------------------------------------------------

    [Fact]
    public async Task SeedAsync_WhenAdminAlreadyExists_DoesNotCreateUser()
    {
        // Arrange
        var userMgr = CreateUserManagerMock();
        var roleMgr = CreateRoleManagerMock();

        roleMgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        // An admin user already exists
        var existingAdmin = new ApplicationUser { UserName = "existingadmin", Email = "existing@test.com" };
        userMgr.Setup(u => u.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser> { existingAdmin });

        var seeder = CreateSeeder(userMgr, roleMgr);

        // Act
        await seeder.SeedAsync();

        // Assert — user creation never called
        userMgr.Verify(
            u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task SeedAsync_WhenNoAdminExists_CreatesAdminUser()
    {
        // Arrange
        var userMgr = CreateUserManagerMock();
        var roleMgr = CreateRoleManagerMock();

        roleMgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        userMgr.Setup(u => u.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>());
        userMgr.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        userMgr.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var seeder = CreateSeeder(userMgr, roleMgr);

        // Act
        await seeder.SeedAsync();

        // Assert
        userMgr.Verify(
            u => u.CreateAsync(
                It.Is<ApplicationUser>(u =>
                    u.UserName == "admin" && u.Email == "admin@campaignengine.local"),
                "Admin@1234!"),
            Times.Once);
        userMgr.Verify(
            u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Admin"),
            Times.Once);
    }

    [Fact]
    public async Task SeedAsync_WhenNoAdminExists_CreatesAdminWithConfiguredCredentials()
    {
        // Arrange
        var userMgr = CreateUserManagerMock();
        var roleMgr = CreateRoleManagerMock();

        roleMgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        userMgr.Setup(u => u.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>());
        userMgr.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);
        userMgr.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Success);

        var seeder = CreateSeeder(userMgr, roleMgr, CustomOptions());

        // Act
        await seeder.SeedAsync();

        // Assert — uses custom credentials, not defaults
        userMgr.Verify(
            u => u.CreateAsync(
                It.Is<ApplicationUser>(u =>
                    u.UserName == "sysadmin" && u.Email == "sysadmin@mycompany.com"),
                "Str0ng&UniqueP@ss!"),
            Times.Once);
    }

    // ----------------------------------------------------------------
    // Error handling
    // ----------------------------------------------------------------

    [Fact]
    public async Task SeedAsync_WhenUserCreationFails_DoesNotAssignRole()
    {
        // Arrange
        var userMgr = CreateUserManagerMock();
        var roleMgr = CreateRoleManagerMock();

        roleMgr.Setup(r => r.RoleExistsAsync(It.IsAny<string>()))
            .ReturnsAsync(true);

        userMgr.Setup(u => u.GetUsersInRoleAsync("Admin"))
            .ReturnsAsync(new List<ApplicationUser>());

        // Simulate creation failure (e.g., duplicate email)
        var identityError = new IdentityError { Description = "DuplicateEmail" };
        userMgr.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
            .ReturnsAsync(IdentityResult.Failed(identityError));

        var seeder = CreateSeeder(userMgr, roleMgr);

        // Act
        await seeder.SeedAsync();

        // Assert — AddToRoleAsync never called after failed creation
        userMgr.Verify(
            u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()),
            Times.Never);
    }
}
