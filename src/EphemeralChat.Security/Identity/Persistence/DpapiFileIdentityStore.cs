using System.Buffers.Binary;
using System.Security.Cryptography;

namespace EphemeralChat.Security.Identity.Persistence;

/// <summary>
/// File-backed identity store. The file contains only a DPAPI-protected blob;
/// plaintext key material never touches the disk. The directory is injected,
/// which lets tests run against a temp directory.
/// </summary>
public sealed class DpapiFileIdentityStore : ILocalIdentityStore
{
    private const string FileMagic = "ECID";
    private const int FormatVersion = 1;

    private readonly string _filePath;
    private readonly IIdentityKeyProtector _protector;

    public DpapiFileIdentityStore(string directory, IIdentityKeyProtector protector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));

        Directory.CreateDirectory(directory);
        _filePath = Path.Combine(directory, "identity.dat");
    }

    public bool Exists() => File.Exists(_filePath);

    public void Save(IdentityStorageData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        byte[] payload = Serialize(data.PrivateKeyPkcs8);
        byte[] protectedBlob;
        try
        {
            protectedBlob = _protector.Protect(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }

        File.WriteAllBytes(_filePath, protectedBlob);
    }

    public IdentityStorageData? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        byte[] protectedBlob = File.ReadAllBytes(_filePath);
        byte[] payload = _protector.Unprotect(protectedBlob);
        try
        {
            return Deserialize(payload);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
    }

    public void Delete()
    {
        if (File.Exists(_filePath))
        {
            File.Delete(_filePath);
        }
    }

    private static byte[] Serialize(byte[] privateKeyPkcs8)
    {
        // Layout: "ECID" | version (uint32 LE) | key length (uint32 LE) | PKCS#8 key
        var payload = new byte[FileMagic.Length + sizeof(int) * 2 + privateKeyPkcs8.Length];
        System.Text.Encoding.ASCII.GetBytes(FileMagic).CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(FileMagic.Length), FormatVersion);
        BinaryPrimitives.WriteUInt32LittleEndian(
            payload.AsSpan(FileMagic.Length + sizeof(int)), (uint)privateKeyPkcs8.Length);
        privateKeyPkcs8.CopyTo(payload, FileMagic.Length + sizeof(int) * 2);
        return payload;
    }

    private static IdentityStorageData Deserialize(byte[] payload)
    {
        int headerLength = FileMagic.Length + sizeof(int) * 2;
        if (payload.Length < headerLength)
        {
            throw new InvalidDataException("Identity payload is truncated.");
        }

        if (System.Text.Encoding.ASCII.GetString(payload, 0, FileMagic.Length) != FileMagic)
        {
            throw new InvalidDataException("Identity payload has an unknown format.");
        }

        uint version = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(FileMagic.Length));
        if (version != FormatVersion)
        {
            throw new InvalidDataException($"Identity payload version {version} is not supported.");
        }

        uint keyLength = BinaryPrimitives.ReadUInt32LittleEndian(payload.AsSpan(FileMagic.Length + sizeof(int)));
        if (keyLength == 0 || keyLength != payload.Length - headerLength)
        {
            throw new InvalidDataException("Identity payload length is inconsistent.");
        }

        var key = new byte[keyLength];
        Buffer.BlockCopy(payload, headerLength, key, 0, key.Length);
        return new IdentityStorageData(key);
    }
}
