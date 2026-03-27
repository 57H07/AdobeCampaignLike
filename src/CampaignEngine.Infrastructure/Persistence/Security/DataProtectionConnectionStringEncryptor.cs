using CampaignEngine.Application.Interfaces;
using Microsoft.AspNetCore.DataProtection;

namespace CampaignEngine.Infrastructure.Persistence.Security;

/// <summary>
/// Encrypts and decrypts data source connection strings using ASP.NET Core Data Protection.
/// Keys are managed by the Data Protection system (file system, DPAPI, Azure Key Vault, etc.)
/// and rotated automatically.
/// </summary>
public class DataProtectionConnectionStringEncryptor : IConnectionStringEncryptor
{
    private const string PurposeString = "CampaignEngine.DataSource.ConnectionString";

    private readonly IDataProtector _protector;

    public DataProtectionConnectionStringEncryptor(IDataProtectionProvider dataProtectionProvider)
    {
        _protector = dataProtectionProvider.CreateProtector(PurposeString);
    }

    /// <inheritdoc/>
    public string Encrypt(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(plaintext));

        return _protector.Protect(plaintext);
    }

    /// <inheritdoc/>
    public string Decrypt(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
            throw new ArgumentException("Ciphertext cannot be null or empty.", nameof(ciphertext));

        return _protector.Unprotect(ciphertext);
    }
}
