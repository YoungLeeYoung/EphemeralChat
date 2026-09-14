using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EphemeralChat.Security.Identity;

namespace EphemeralChat.Tests.Security;

public class PeerIdCalculatorTests
{
    private static readonly Regex PeerIdPattern = new("^[A-Z2-7]{26}$", RegexOptions.Compiled);

    [Fact]
    public void FromPublicKey_IsDeterministicForTheSameKey()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string first = PeerIdCalculator.FromPublicKey(key);
        string second = PeerIdCalculator.FromPublicKey(key);

        Assert.Equal(first, second);
    }

    [Fact]
    public void FromPublicKey_IsStableAcrossKeySerialization()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] pkcs8 = key.ExportPkcs8PrivateKey();

        using var reimported = ECDsa.Create();
        reimported.ImportPkcs8PrivateKey(pkcs8, out _);

        Assert.Equal(PeerIdCalculator.FromPublicKey(key), PeerIdCalculator.FromPublicKey(reimported));
    }

    [Fact]
    public void FromPublicKey_DiffersForDifferentKeys()
    {
        using var keyA = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var keyB = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        Assert.NotEqual(PeerIdCalculator.FromPublicKey(keyA), PeerIdCalculator.FromPublicKey(keyB));
    }

    [Fact]
    public void FromPublicKey_HasExpectedFormat()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        string peerId = PeerIdCalculator.FromPublicKey(key);

        Assert.Matches(PeerIdPattern, peerId);
    }
}
