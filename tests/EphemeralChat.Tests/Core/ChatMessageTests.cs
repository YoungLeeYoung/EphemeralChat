using EphemeralChat.Core.Models;

namespace EphemeralChat.Tests.Core;

public class ChatMessageTests
{
    [Fact]
    public void ChatMessage_PreservesConstructorValues()
    {
        var sentAt = DateTimeOffset.Now;

        var message = new ChatMessage("Alice", "hello", sentAt);

        Assert.Equal("Alice", message.Sender);
        Assert.Equal("hello", message.Content);
        Assert.Equal(sentAt, message.SentAt);
    }
}
