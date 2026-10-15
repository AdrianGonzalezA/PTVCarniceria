namespace Carnicerias.Central.Infrastructure;

public sealed class CentralBranchCredential
{
    private CentralBranchCredential()
    {
        Nonce = [];
        Ciphertext = [];
        Tag = [];
    }

    private CentralBranchCredential(Guid installationId, CentralEncryptedBranchCredential encrypted)
    {
        InstallationId = installationId;
        KeyVersion = encrypted.KeyVersion;
        Nonce = (byte[])encrypted.Nonce.Clone();
        Ciphertext = (byte[])encrypted.Ciphertext.Clone();
        Tag = (byte[])encrypted.Tag.Clone();
    }

    public Guid InstallationId { get; private set; }
    public int KeyVersion { get; private set; }
    public byte[] Nonce { get; private set; }
    public byte[] Ciphertext { get; private set; }
    public byte[] Tag { get; private set; }

    public static CentralBranchCredential Create(Guid installationId,
        CentralEncryptedBranchCredential encrypted)
    {
        if (installationId == Guid.Empty || encrypted.KeyVersion < 1
            || encrypted.Nonce.Length != 12 || encrypted.Ciphertext.Length != 32
            || encrypted.Tag.Length != 16)
            throw new ArgumentException("Invalid encrypted branch credential.");
        return new CentralBranchCredential(installationId, encrypted);
    }

    public CentralEncryptedBranchCredential ToEnvelope() =>
        new(KeyVersion, (byte[])Nonce.Clone(), (byte[])Ciphertext.Clone(),
            (byte[])Tag.Clone());

    public void ReplaceWith(CentralBranchCredential replacement)
    {
        if (replacement.InstallationId != InstallationId)
            throw new ArgumentException("Credential belongs to another installation.");
        KeyVersion = replacement.KeyVersion;
        Nonce = (byte[])replacement.Nonce.Clone();
        Ciphertext = (byte[])replacement.Ciphertext.Clone();
        Tag = (byte[])replacement.Tag.Clone();
    }
}
