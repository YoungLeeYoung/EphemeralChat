using EphemeralChat.Signaling.Server;

namespace EphemeralChat.Tests.Signaling;

public class MessageValidatorTests
{
    private const string ValidPeerId = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    [Fact]
    public void IsValidPeerId_Accepts26UppercaseBase32Chars()
    {
        Assert.True(MessageValidator.IsValidPeerId(ValidPeerId));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abcdefghijklmnopqrstuvwxyz234567")]
    [InlineData("A")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ2345678")]
    [InlineData("ABCDEF0HIJKLMNOPQRSTUVWXYZ")]
    public void IsValidPeerId_RejectsMalformedIds(string? peerId)
    {
        Assert.False(MessageValidator.IsValidPeerId(peerId));
    }

    [Fact]
    public void ValidateRegistration_AcceptsWellFormedRegistration()
    {
        string publicKey = Convert.ToBase64String(new byte[91]);

        Assert.Null(MessageValidator.ValidateRegistration(ValidPeerId, publicKey));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("short", null)]
    public void ValidateRegistration_RejectsMissingData(string? peerId, string? publicKey)
    {
        Assert.NotNull(MessageValidator.ValidateRegistration(peerId, publicKey));
    }

    [Fact]
    public void ValidateRegistration_RejectsNonBase64PublicKey()
    {
        Assert.NotNull(MessageValidator.ValidateRegistration(ValidPeerId, "not base64!!!"));
    }

    [Fact]
    public void ValidateRegistration_RejectsPublicKeyOutsideSizeRange()
    {
        Assert.NotNull(MessageValidator.ValidateRegistration(
            ValidPeerId, Convert.ToBase64String(new byte[8])));
        Assert.NotNull(MessageValidator.ValidateRegistration(
            ValidPeerId, Convert.ToBase64String(new byte[2048])));
    }

    [Fact]
    public void ValidateTarget_RejectsSelfTargeting()
    {
        Assert.NotNull(MessageValidator.ValidateTarget(ValidPeerId, ValidPeerId));
    }
}
