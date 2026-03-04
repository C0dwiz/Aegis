using Microsoft.EntityFrameworkCore;
using Aegis.Data.Entities;
using Aegis.Data.Repositories;
using Aegis.Data;
using Xunit;
using Microsoft.EntityFrameworkCore.InMemory;

namespace Aegis.Tests;

public class RepositoryTests : IDisposable
{
    private readonly AegisDbContext _context;
    private readonly UserRepository _userRepository;
    private readonly ChannelRepository _channelRepository;
    private readonly PrivateChatRepository _privateChatRepository;

    public RepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AegisDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        _context = new AegisDbContext(options);
        _userRepository = new UserRepository(_context);
        _channelRepository = new ChannelRepository(_context);
        _privateChatRepository = new PrivateChatRepository(_context);
    }

    [Fact]
    public async Task UserRepository_CreateAndGetUser_ShouldWork()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hashed_password",
            PublicKey = "public_key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        // Act
        var createdUser = await _userRepository.CreateAsync(user);
        var retrievedUser = await _userRepository.GetByIdAsync(createdUser.Id);

        // Assert
        Assert.NotNull(createdUser);
        Assert.NotNull(retrievedUser);
        Assert.Equal(createdUser.Id, retrievedUser.Id);
        Assert.Equal("testuser", retrievedUser.Username);
        Assert.Equal("test@example.com", retrievedUser.Email);
    }

    [Fact]
    public async Task UserRepository_GetByUsername_ShouldReturnCorrectUser()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hashed_password",
            PublicKey = "public_key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _userRepository.CreateAsync(user);

        // Act
        var foundUser = await _userRepository.GetByUsernameAsync("testuser");

        // Assert
        Assert.NotNull(foundUser);
        Assert.Equal("testuser", foundUser.Username);
        Assert.Equal("test@example.com", foundUser.Email);
    }

    [Fact]
    public async Task UserRepository_SearchByUsername_ShouldReturnMatchingUsers()
    {
        // Arrange
        var users = new List<User>
        {
            new User { Username = "john_doe", Email = "john@example.com", PasswordHash = "hash", PublicKey = "key", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Username = "jane_doe", Email = "jane@example.com", PasswordHash = "hash", PublicKey = "key", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow },
            new User { Username = "bob_smith", Email = "bob@example.com", PasswordHash = "hash", PublicKey = "key", IsActive = true, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow }
        };

        foreach (var user in users)
        {
            await _userRepository.CreateAsync(user);
        }

        // Act
        var searchResults = await _userRepository.SearchByUsernameAsync("john");

        // Assert
        Assert.Single(searchResults);
        Assert.Equal("john_doe", searchResults.First().Username);
    }

    [Fact]
    public async Task ChannelRepository_CreateAndGetChannel_ShouldWork()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash",
            PublicKey = "key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _userRepository.CreateAsync(user);

        var channel = new Channel
        {
            Name = "Test Channel",
            Description = "Test Description",
            Type = ChannelType.Public,
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            MemberCount = 1
        };

        // Act
        var createdChannel = await _channelRepository.CreateAsync(channel);
        var retrievedChannel = await _channelRepository.GetByIdAsync(createdChannel.Id);

        // Assert
        Assert.NotNull(createdChannel);
        Assert.NotNull(retrievedChannel);
        Assert.Equal(createdChannel.Id, retrievedChannel.Id);
        Assert.Equal("Test Channel", retrievedChannel.Name);
        Assert.Equal("Test Description", retrievedChannel.Description);
        Assert.Equal(ChannelType.Public, retrievedChannel.Type);
    }

    [Fact]
    public async Task ChannelRepository_IsUserMember_ShouldReturnCorrectResult()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash",
            PublicKey = "key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _userRepository.CreateAsync(user);

        var channel = new Channel
        {
            Name = "Test Channel",
            Type = ChannelType.Public,
            CreatedByUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            IsActive = true,
            MemberCount = 1
        };
        await _channelRepository.CreateAsync(channel);

        var channelMember = new ChannelMember
        {
            ChannelId = channel.Id,
            UserId = user.Id,
            Role = ChannelMemberRole.Owner,
            JoinedAt = DateTime.UtcNow,
            IsActive = true
        };
        _context.ChannelMembers.Add(channelMember);
        await _context.SaveChangesAsync();

        // Act
        var isMember = await _channelRepository.IsUserMemberAsync(channel.Id, user.Id);

        // Assert
        Assert.True(isMember);
    }

    [Fact]
    public async Task ChannelRepository_GetUserChannels_ShouldReturnUserChannels()
    {
        // Arrange
        var user = new User
        {
            Username = "testuser",
            Email = "test@example.com",
            PasswordHash = "hash",
            PublicKey = "key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _userRepository.CreateAsync(user);

        var channels = new List<Channel>();
        for (int i = 0; i < 3; i++)
        {
            var channel = new Channel
            {
                Name = $"Channel {i}",
                Type = ChannelType.Public,
                CreatedByUserId = user.Id,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true,
                MemberCount = 1
            };
            channels.Add(await _channelRepository.CreateAsync(channel));

            var channelMember = new ChannelMember
            {
                ChannelId = channel.Id,
                UserId = user.Id,
                Role = ChannelMemberRole.Owner,
                JoinedAt = DateTime.UtcNow,
                IsActive = true
            };
            _context.ChannelMembers.Add(channelMember);
        }
        await _context.SaveChangesAsync();

        // Act
        var userChannels = await _channelRepository.GetUserChannelsAsync(user.Id);

        // Assert
        Assert.Equal(3, userChannels.Count());
    }

    [Fact]
    public async Task PrivateChatRepository_CreateAndGetPrivateChat_ShouldWork()
    {
        // Arrange
        var user1 = new User
        {
            Username = "user1",
            Email = "user1@example.com",
            PasswordHash = "hash",
            PublicKey = "key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var user2 = new User
        {
            Username = "user2",
            Email = "user2@example.com",
            PasswordHash = "hash",
            PublicKey = "key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _userRepository.CreateAsync(user1);
        await _userRepository.CreateAsync(user2);

        // Act
        var privateChat = await _privateChatRepository.CreatePrivateChatAsync(user1.Id, user2.Id);
        var retrievedChat = await _privateChatRepository.GetByIdAsync(privateChat.Id);

        // Assert
        Assert.NotNull(privateChat);
        Assert.NotNull(retrievedChat);
        Assert.Equal(privateChat.Id, retrievedChat.Id);
        Assert.Equal(Math.Min(user1.Id, user2.Id), retrievedChat.User1Id);
        Assert.Equal(Math.Max(user1.Id, user2.Id), retrievedChat.User2Id);
    }

    [Fact]
    public async Task PrivateChatRepository_GetPrivateChat_ShouldReturnCorrectChat()
    {
        // Arrange
        var user1 = new User
        {
            Username = "user1",
            Email = "user1@example.com",
            PasswordHash = "hash",
            PublicKey = "key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        var user2 = new User
        {
            Username = "user2",
            Email = "user2@example.com",
            PasswordHash = "hash",
            PublicKey = "key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _userRepository.CreateAsync(user1);
        await _userRepository.CreateAsync(user2);

        var privateChat = await _privateChatRepository.CreatePrivateChatAsync(user1.Id, user2.Id);

        // Act
        var retrievedChat = await _privateChatRepository.GetPrivateChatAsync(user1.Id, user2.Id);

        // Assert
        Assert.NotNull(retrievedChat);
        Assert.Equal(privateChat.Id, retrievedChat.Id);
    }

    [Fact]
    public async Task PrivateChatRepository_GetUserPrivateChats_ShouldReturnUserChats()
    {
        // Arrange
        var user1 = new User
        {
            Username = "user1",
            Email = "user1@example.com",
            PasswordHash = "hash",
            PublicKey = "key",
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _userRepository.CreateAsync(user1);

        var chats = new List<PrivateChat>();
        for (int i = 0; i < 3; i++)
        {
            var user2 = new User
            {
                Username = $"user{i + 2}",
                Email = $"user{i + 2}@example.com",
                PasswordHash = "hash",
                PublicKey = "key",
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _userRepository.CreateAsync(user2);

            chats.Add(await _privateChatRepository.CreatePrivateChatAsync(user1.Id, user2.Id));
        }

        // Act
        var userChats = await _privateChatRepository.GetUserPrivateChatsAsync(user1.Id);

        // Assert
        Assert.Equal(3, userChats.Count());
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
