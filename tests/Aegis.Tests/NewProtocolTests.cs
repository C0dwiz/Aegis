using Aegis.Protocol;
using Aegis.Handlers;
using Aegis.Data.Entities;
using MessagePack;
using MessagePack.Resolvers;
using Xunit;

namespace Aegis.Tests;

public class NewProtocolTests
{
    private static readonly MessagePackSerializerOptions SerializerOptions =
        MessagePackSerializerOptions.Standard.WithResolver(ContractlessStandardResolver.Instance);

    private static T DeserializeRoundTrip<T>(T value)
    {
        var data = MessagePackSerializer.Serialize(value, SerializerOptions);
        return MessagePackSerializer.Deserialize<T>(data, SerializerOptions);
    }

    [Fact]
    public void MessageType_ShouldContainNewTypes()
    {
        // Assert
        Assert.Equal(13, (int)MessageType.ChannelMessage);
        Assert.Equal(14, (int)MessageType.ChannelCreate);
        Assert.Equal(15, (int)MessageType.ChannelJoin);
        Assert.Equal(16, (int)MessageType.ChannelLeave);
        Assert.Equal(17, (int)MessageType.PrivateChatMessage);
        Assert.Equal(18, (int)MessageType.UserSearch);
        Assert.Equal(19, (int)MessageType.UserSearchResult);
        Assert.Equal(20, (int)MessageType.Register);
        Assert.Equal(21, (int)MessageType.RegisterResponse);
        Assert.Equal(41, (int)MessageType.ChatListRequest);
        Assert.Equal(42, (int)MessageType.ChatListResponse);
        Assert.Equal(43, (int)MessageType.PrivateChatHistoryRequest);
        Assert.Equal(44, (int)MessageType.PrivateChatHistoryResponse);
        Assert.Equal(45, (int)MessageType.ChannelHistoryRequest);
        Assert.Equal(46, (int)MessageType.ChannelHistoryResponse);
        Assert.Equal(47, (int)MessageType.PrivateChatMessageEvent);
        Assert.Equal(48, (int)MessageType.ChannelMessageEvent);
    }

    [Fact]
    public void RegistrationRequest_Serialization_ShouldWork()
    {
        // Arrange
        var request = new RegistrationRequest("testuser", "test@example.com", "password123", "public_key");

        // Act
        var deserialized = DeserializeRoundTrip(request);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.Username, deserialized.Username);
        Assert.Equal(request.Email, deserialized.Email);
        Assert.Equal(request.Password, deserialized.Password);
        Assert.Equal(request.PublicKey, deserialized.PublicKey);
    }

    [Fact]
    public void RegistrationResponse_Serialization_ShouldWork()
    {
        // Arrange
        var user = new RegisteredUserInfo(1, "testuser");
        var response = new RegistrationResponse(true, "Success", user);

        // Act
        var deserialized = DeserializeRoundTrip(response);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.Success, deserialized.Success);
        Assert.Equal(response.Message, deserialized.Message);
        Assert.Equal(response.User?.Id, deserialized.User?.Id);
        Assert.Equal(response.User?.Username, deserialized.User?.Username);
    }

    [Fact]
    public void UserSearchRequest_Serialization_ShouldWork()
    {
        // Arrange
        var request = new UserSearchRequest("john", 20);

        // Act
        var deserialized = DeserializeRoundTrip(request);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.Query, deserialized.Query);
        Assert.Equal(request.Limit, deserialized.Limit);
    }

    [Fact]
    public void UserSearchResponse_Serialization_ShouldWork()
    {
        // Arrange
        var users = new List<UserSearchResult>
        {
            new UserSearchResult(1, "john_doe"),
            new UserSearchResult(2, "jane_doe")
        };
        var response = new UserSearchResponse(true, users, "Found users");

        // Act
        var deserialized = DeserializeRoundTrip(response);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.Success, deserialized.Success);
        Assert.Equal(response.Message, deserialized.Message);
        Assert.Equal(response.Users.Count, deserialized.Users.Count);
        Assert.Equal(response.Users[0].Id, deserialized.Users[0].Id);
        Assert.Equal(response.Users[0].Username, deserialized.Users[0].Username);
    }

    [Fact]
    public void ChannelMessageRequest_Serialization_ShouldWork()
    {
        // Arrange
        var request = new ChannelMessageRequest(1, "Hello, channel!", MessageContentType.Text, 5);

        // Act
        var deserialized = DeserializeRoundTrip(request);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.ChannelId, deserialized.ChannelId);
        Assert.Equal(request.Content, deserialized.Content);
        Assert.Equal(request.ContentType, deserialized.ContentType);
        Assert.Equal(request.ReplyToMessageId, deserialized.ReplyToMessageId);
    }

    [Fact]
    public void ChannelMessageResponse_Serialization_ShouldWork()
    {
        // Arrange
        var channelMessage = new Aegis.Data.Entities.ChannelMessage
        {
            Id = 1,
            ChannelId = 1,
            FromUserId = 1,
            Content = "Hello, channel!",
            ContentType = MessageContentType.Text,
            CreatedAt = DateTime.UtcNow
        };
        var response = new ChannelMessageResponse(true, 1, "Message sent");

        // Act
        var deserialized = DeserializeRoundTrip(response);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.Success, deserialized.Success);
        Assert.Equal(response.MessageText, deserialized.MessageText);
        Assert.Equal(response.MessageId, deserialized.MessageId);
    }

    [Fact]
    public void ChannelCreateRequest_Serialization_ShouldWork()
    {
        // Arrange
        var request = new ChannelCreateRequest("Test Channel", "Test Description", ChannelType.Private);

        // Act
        var deserialized = DeserializeRoundTrip(request);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.Name, deserialized.Name);
        Assert.Equal(request.Description, deserialized.Description);
        Assert.Equal(request.Type, deserialized.Type);
    }

    [Fact]
    public void ChannelCreateResponse_Serialization_ShouldWork()
    {
        // Arrange
        var channel = new Aegis.Data.Entities.Channel
        {
            Id = 1,
            Name = "Test Channel",
            Description = "Test Description",
            Type = ChannelType.Public,
            CreatedAt = DateTime.UtcNow
        };
        var response = new ChannelCreateResponse(true, 1, "Channel created");

        // Act
        var deserialized = DeserializeRoundTrip(response);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.Success, deserialized.Success);
        Assert.Equal(response.Message, deserialized.Message);
        Assert.Equal(response.ChannelId, deserialized.ChannelId);
    }

    [Fact]
    public void PrivateChatMessageRequest_Serialization_ShouldWork()
    {
        // Arrange
        var request = new PrivateChatMessageRequest(2, "Hello, private!", MessageContentType.Text);

        // Act
        var deserialized = DeserializeRoundTrip(request);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(request.ToUserId, deserialized.ToUserId);
        Assert.Equal(request.Content, deserialized.Content);
        Assert.Equal(request.ContentType, deserialized.ContentType);
    }

    [Fact]
    public void PrivateChatMessageResponse_Serialization_ShouldWork()
    {
        // Arrange
        var message = new Aegis.Data.Entities.Message
        {
            Id = 1,
            FromUserId = 1,
            ToUserId = 2,
            Content = "Hello, private!",
            ContentType = MessageContentType.Text,
            CreatedAt = DateTime.UtcNow
        };
        var privateChat = new Aegis.Data.Entities.PrivateChat
        {
            Id = 1,
            User1Id = 1,
            User2Id = 2,
            CreatedAt = DateTime.UtcNow
        };
        var response = new PrivateChatMessageResponse(true, 1, "Message sent");

        // Act
        var deserialized = DeserializeRoundTrip(response);

        // Assert
        Assert.NotNull(deserialized);
        Assert.Equal(response.Success, deserialized.Success);
        Assert.Equal(response.MessageText, deserialized.MessageText);
        Assert.Equal(response.MessageId, deserialized.MessageId);
    }

    [Fact]
    public void MessageFlags_ShouldContainExpectedValues()
    {
        // Assert
        Assert.Equal(0x00, (int)MessageFlags.None);
        Assert.Equal(0x01, (int)MessageFlags.RequiresAck);
        Assert.Equal(0x02, (int)MessageFlags.IsRetransmit);
        Assert.Equal(0x04, (int)MessageFlags.Compressed);
        Assert.Equal(0x08, (int)MessageFlags.Encrypted);
        Assert.Equal(0x10, (int)MessageFlags.Priority);
    }

    [Fact]
    public void AckStatus_ShouldContainExpectedValues()
    {
        // Assert
        Assert.Equal(0, (int)AckStatus.Ok);
        Assert.Equal(1, (int)AckStatus.Error);
        Assert.Equal(2, (int)AckStatus.Retry);
        Assert.Equal(3, (int)AckStatus.NotImplemented);
    }

    [Fact]
    public void ChannelType_ShouldContainExpectedValues()
    {
        // Assert
        Assert.Equal(0, (int)ChannelType.Public);
        Assert.Equal(1, (int)ChannelType.Private);
        Assert.Equal(2, (int)ChannelType.Group);
    }

    [Fact]
    public void ChannelMemberRole_ShouldContainExpectedValues()
    {
        // Assert
        Assert.Equal(0, (int)ChannelMemberRole.Member);
        Assert.Equal(1, (int)ChannelMemberRole.Moderator);
        Assert.Equal(2, (int)ChannelMemberRole.Admin);
        Assert.Equal(3, (int)ChannelMemberRole.Owner);
    }
}
