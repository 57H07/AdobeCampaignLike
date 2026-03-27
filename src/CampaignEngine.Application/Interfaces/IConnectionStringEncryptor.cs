namespace CampaignEngine.Application.Interfaces;

/// <summary>
/// Provides encryption and decryption for sensitive connection strings
/// stored in the database. Implementations use ASP.NET Core Data Protection.
/// </summary>
public interface IConnectionStringEncryptor
{
    /// <summary>
    /// Encrypts a plaintext connection string for safe storage in the database.
    /// </summary>
    string Encrypt(string plaintext);

    /// <summary>
    /// Decrypts an encrypted connection string retrieved from the database.
    /// </summary>
    string Decrypt(string ciphertext);
}
