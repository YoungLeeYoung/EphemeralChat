using EphemeralChat.Security.Primitives;

namespace EphemeralChat.Tests.Security;

public class Base32EncoderTests
{
    [Theory]
    [InlineData("", "")]
    [InlineData("f", "MY======")]
    [InlineData("fo", "MZXQ====")]
    [InlineData("foo", "MZXW6===")]
    [InlineData("foob", "MZXW6YQ=")]
    [InlineData("fooba", "MZXW6YTB")]
    [InlineData("foobar", "MZXW6YTBOI======")]
    public void Encode_MatchesRfc4648TestVectors(string input, string expected)
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes(input);

        Assert.Equal(expected, Base32Encoder.Encode(bytes, padding: true));
    }

    [Fact]
    public void Encode_OmitsPaddingByDefault()
    {
        byte[] bytes = System.Text.Encoding.ASCII.GetBytes("foobar");

        Assert.Equal("MZXW6YTBOI", Base32Encoder.Encode(bytes));
    }
}
