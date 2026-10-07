namespace Carnicerias.PlatformAccess;

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string? password, string? encodedHash);
}
