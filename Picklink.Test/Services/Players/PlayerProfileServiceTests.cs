using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Moq;
using NUnit.Framework;
using PicklinkBackend.Data;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories.Implementations;
using PicklinkBackend.Services.Players;
using PicklinkBackend.Services.Players.Implementations;
using PicklinkBackend.Services.Venues;
using Match = PicklinkBackend.Models.Match;

namespace Picklink.Test.Services.Players
{
    [TestFixture]
    public class PlayerProfileServiceTests
    {
        private ApplicationDbContext _dbContext;
        private UserRepository _userRepository;
        private Mock<IWebHostEnvironment> _environmentMock;
        private Mock<IConfiguration> _configurationMock;
        private Mock<IHttpClientFactory> _httpClientFactoryMock;
        private CloudinaryDestroyService _cloudinaryDestroyService;
        private PlayerProfileService _playerProfileService;
        private string _tempWebRoot;

        [SetUp]
        public void Setup()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _dbContext = new ApplicationDbContext(options);
            _userRepository = new UserRepository(_dbContext);

            _tempWebRoot = Path.Combine(Path.GetTempPath(), "picklink_test_wwwroot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempWebRoot);

            _environmentMock = new Mock<IWebHostEnvironment>();
            _environmentMock.Setup(e => e.WebRootPath).Returns(_tempWebRoot);
            _environmentMock.Setup(e => e.ContentRootPath).Returns(_tempWebRoot);

            _configurationMock = new Mock<IConfiguration>();
            _httpClientFactoryMock = new Mock<IHttpClientFactory>();
            _cloudinaryDestroyService = new CloudinaryDestroyService(_configurationMock.Object, _httpClientFactoryMock.Object);

            _playerProfileService = new PlayerProfileService(
                _userRepository,
                _environmentMock.Object,
                _cloudinaryDestroyService);
        }

        [TearDown]
        public void TearDown()
        {
            _dbContext.Dispose();
            if (Directory.Exists(_tempWebRoot))
            {
                try { Directory.Delete(_tempWebRoot, true); } catch { }
            }
        }

        private static User CreateUser(int userId, string username, string email, string userType = "Player", string? city = null, string? commune = null, string? profileImageUrl = null)
        {
            return new User
            {
                UserId = userId,
                Username = username,
                Email = email,
                PasswordHash = "hashed_123456",
                UserType = userType,
                City = city,
                Commune = commune,
                ProfileImageUrl = profileImageUrl,
                IsLocked = false
            };
        }

        #region 1. GetMeAsync Tests (6 test cases)

        [Test]
        public async Task GetMeAsync_1_WhenUserIdIsNull_ReturnsUnauthorized()
        {
            // Act
            var result = await _playerProfileService.GetMeAsync(null, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Unauthorized));
        }

        [Test]
        public async Task GetMeAsync_2_WhenUserNotFound_ReturnsNotFound()
        {
            // Act
            var result = await _playerProfileService.GetMeAsync(999, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.NotFound));
        }

        [Test]
        public async Task GetMeAsync_3_WhenUserExistsWithoutPlayer_ReturnsBasicUserProfileResponse()
        {
            // Arrange
            var user = CreateUser(1, "user_no_player", "noplayer@test.vn", "User", "Hà Nội", "Mỹ Đình");
            await _dbContext.Users.AddAsync(user);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetMeAsync(1, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.UserId, Is.EqualTo(1));
            Assert.That(result.Value.Username, Is.EqualTo("user_no_player"));
            Assert.That(result.Value.PlayerId, Is.Null);
        }

        [Test]
        public async Task GetMeAsync_4_WhenUserAndPlayerExist_ReturnsFullUserProfileWithPlayerStats()
        {
            // Arrange
            var user = CreateUser(2, "pro_player", "pro@test.vn", "Player", "TP.HCM");
            var player = new Player
            {
                PlayerId = 10,
                UserId = 2,
                SkillLevel = 3.5,
                Prestige = 5,
                Bio = "Pickleball enthusiast",
                BirthDate = new DateOnly(1995, 5, 20),
                HeightCm = 175,
                WeightKg = 70
            };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetMeAsync(2, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.PlayerId, Is.EqualTo(10));
            Assert.That(result.Value.SkillLevel, Is.EqualTo(3.5));
            Assert.That(result.Value.Bio, Is.EqualTo("Pickleball enthusiast"));
            Assert.That(result.Value.HeightCm, Is.EqualTo(175));
        }

        [Test]
        public async Task GetMeAsync_5_CalculatesMatchesPlayedCountAccurately()
        {
            // Arrange
            var user = CreateUser(3, "match_player", "matches@test.vn", "Player");
            var player = new Player { PlayerId = 20, UserId = 3, SkillLevel = 2.0 };
            var match1 = new Match { MatchId = 101, MatchType = "Standard", Status = "Completed", MatchTime = DateTime.UtcNow.AddDays(-2) };
            var match2 = new Match { MatchId = 102, MatchType = "Standard", Status = "Completed", MatchTime = DateTime.UtcNow.AddDays(-1) };

            var participant1 = new MatchParticipant { ParticipantId = 1, MatchId = 101, PlayerId = 20, Status = "Accepted" };
            var participant2 = new MatchParticipant { ParticipantId = 2, MatchId = 102, PlayerId = 20, Status = "Approved" };

            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.Matches.AddRangeAsync(match1, match2);
            await _dbContext.MatchParticipants.AddRangeAsync(participant1, participant2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetMeAsync(3, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value!.MatchesPlayed, Is.EqualTo(2));
        }

        [Test]
        public async Task GetMeAsync_6_LimitsMatchHistoryToRecent20Items()
        {
            // Arrange
            var user = CreateUser(4, "history_player", "history@test.vn", "Player");
            var player = new Player { PlayerId = 30, UserId = 4, SkillLevel = 2.5 };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);

            for (int i = 1; i <= 25; i++)
            {
                var match = new Match { MatchId = 200 + i, MatchType = "Standard", Status = "Completed", MatchTime = DateTime.UtcNow.AddHours(-i) };
                var participant = new MatchParticipant { ParticipantId = 100 + i, MatchId = 200 + i, PlayerId = 30, Status = "Accepted" };
                await _dbContext.Matches.AddAsync(match);
                await _dbContext.MatchParticipants.AddAsync(participant);
            }
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetMeAsync(4, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value!.MatchHistory.Count, Is.EqualTo(20));
        }

        #endregion

        #region 2. GetPublicPlayerProfileAsync Tests (6 test cases)

        [Test]
        public async Task GetPublicPlayerProfileAsync_1_WhenPlayerExists_ReturnsPublicProfileData()
        {
            // Arrange
            var user = CreateUser(10, "public_star", "star@test.vn", "Player", "Đà Nẵng", "Hải Châu", "https://cdn.picklink.vn/star.jpg");
            var player = new Player
            {
                PlayerId = 50,
                UserId = 10,
                SkillLevel = 4.0,
                Prestige = 5,
                Bio = "Tournament player",
                PlayFrequency = "Daily",
                PreferredTimeSlot = "Evening"
            };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetPublicPlayerProfileAsync(50, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.PlayerId, Is.EqualTo(50));
            Assert.That(result.Value.Username, Is.EqualTo("public_star"));
            Assert.That(result.Value.SkillLevel, Is.EqualTo(4.0));
            Assert.That(result.Value.City, Is.EqualTo("Đà Nẵng"));
            Assert.That(result.Value.PlayFrequency, Is.EqualTo("Daily"));
        }

        [Test]
        public async Task GetPublicPlayerProfileAsync_2_WhenPlayerNotFound_ReturnsNotFound()
        {
            // Act
            var result = await _playerProfileService.GetPublicPlayerProfileAsync(999, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.NotFound));
        }

        [Test]
        public async Task GetPublicPlayerProfileAsync_3_CountsOnlyApprovedOrAcceptedMatches()
        {
            // Arrange
            var user = CreateUser(11, "approved_only", "approved@test.vn", "Player");
            var player = new Player { PlayerId = 51, UserId = 11 };
            var match1 = new Match { MatchId = 301, MatchType = "Standard", Status = "Completed", MatchTime = DateTime.UtcNow };
            var match2 = new Match { MatchId = 302, MatchType = "Standard", Status = "Completed", MatchTime = DateTime.UtcNow };

            var part1 = new MatchParticipant { ParticipantId = 201, MatchId = 301, PlayerId = 51, Status = "Approved" };
            var part2 = new MatchParticipant { ParticipantId = 202, MatchId = 302, PlayerId = 51, Status = "Accepted" };

            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.Matches.AddRangeAsync(match1, match2);
            await _dbContext.MatchParticipants.AddRangeAsync(part1, part2);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetPublicPlayerProfileAsync(51, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value!.MatchesPlayed, Is.EqualTo(2));
        }

        [Test]
        public async Task GetPublicPlayerProfileAsync_4_IgnoresPendingOrRejectedMatchParticipantsInCount()
        {
            // Arrange
            var user = CreateUser(12, "filter_player", "filter@test.vn", "Player");
            var player = new Player { PlayerId = 52, UserId = 12 };
            var match1 = new Match { MatchId = 303, MatchType = "Standard", Status = "Scheduled", MatchTime = DateTime.UtcNow };
            var match2 = new Match { MatchId = 304, MatchType = "Standard", Status = "Scheduled", MatchTime = DateTime.UtcNow };
            var match3 = new Match { MatchId = 305, MatchType = "Standard", Status = "Scheduled", MatchTime = DateTime.UtcNow };

            var part1 = new MatchParticipant { ParticipantId = 203, MatchId = 303, PlayerId = 52, Status = "Approved" };
            var part2 = new MatchParticipant { ParticipantId = 204, MatchId = 304, PlayerId = 52, Status = "Pending" };
            var part3 = new MatchParticipant { ParticipantId = 205, MatchId = 305, PlayerId = 52, Status = "Rejected" };

            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.Matches.AddRangeAsync(match1, match2, match3);
            await _dbContext.MatchParticipants.AddRangeAsync(part1, part2, part3);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetPublicPlayerProfileAsync(52, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value!.MatchesPlayed, Is.EqualTo(1)); // Only Approved counted
        }

        [Test]
        public async Task GetPublicPlayerProfileAsync_5_MapsUserAndPlayerFieldsCorrectly()
        {
            // Arrange
            var user = CreateUser(13, "mapped_user", "mapped@test.vn", "Player", "Cần Thơ", "Ninh Kiều", "https://img.com/avatar.png");
            var player = new Player
            {
                PlayerId = 53,
                UserId = 13,
                SkillLevel = 2.75,
                Prestige = 5,
                PlayerSubType = "Competitive",
                Bio = "Ready for any challenge"
            };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetPublicPlayerProfileAsync(53, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value!.PlayerSubType, Is.EqualTo("Competitive"));
            Assert.That(result.Value.Bio, Is.EqualTo("Ready for any challenge"));
            Assert.That(result.Value.ProfileImageUrl, Is.EqualTo("https://img.com/avatar.png"));
        }

        [Test]
        public async Task GetPublicPlayerProfileAsync_6_ReturnsNullOptionalFieldsWhenEmpty()
        {
            // Arrange
            var user = CreateUser(14, "empty_fields_user", "empty@test.vn", "Player");
            var player = new Player { PlayerId = 54, UserId = 14 };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _playerProfileService.GetPublicPlayerProfileAsync(54, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(result.Value!.Bio, Is.Null);
            Assert.That(result.Value.City, Is.Null);
            Assert.That(result.Value.ProfileImageUrl, Is.Null);
        }

        #endregion

        #region 3. UploadAvatarAsync Tests (6 test cases)

        [Test]
        public async Task UploadAvatarAsync_1_WhenUserIdIsNull_ReturnsUnauthorized()
        {
            // Arrange
            var fileMock = new Mock<IFormFile>();

            // Act
            var result = await _playerProfileService.UploadAvatarAsync(fileMock.Object, null, "http://localhost:5209", CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Unauthorized));
        }

        [Test]
        public async Task UploadAvatarAsync_2_WhenFileLengthIsZero_ReturnsBadRequest()
        {
            // Arrange
            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.Length).Returns(0);

            // Act
            var result = await _playerProfileService.UploadAvatarAsync(fileMock.Object, 1, "http://localhost:5209", CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("chọn ảnh đại diện"));
        }

        [Test]
        public async Task UploadAvatarAsync_3_WhenFileExceeds2MB_ReturnsBadRequest()
        {
            // Arrange
            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.Length).Returns(3 * 1024 * 1024); // 3MB > 2MB

            // Act
            var result = await _playerProfileService.UploadAvatarAsync(fileMock.Object, 1, "http://localhost:5209", CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("không được vượt quá 2MB"));
        }

        [Test]
        public async Task UploadAvatarAsync_4_WhenFileExtensionNotAllowed_ReturnsBadRequest()
        {
            // Arrange
            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.Length).Returns(1024);
            fileMock.Setup(f => f.FileName).Returns("script.exe");

            // Act
            var result = await _playerProfileService.UploadAvatarAsync(fileMock.Object, 1, "http://localhost:5209", CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("Chỉ hỗ trợ ảnh JPG, PNG, WEBP hoặc GIF"));
        }

        [Test]
        public async Task UploadAvatarAsync_5_WhenFileSignatureDoesNotMatchContentType_ReturnsBadRequest()
        {
            // Arrange
            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.Length).Returns(100);
            fileMock.Setup(f => f.FileName).Returns("fake.jpg");
            fileMock.Setup(f => f.ContentType).Returns("image/jpeg");

            // Random invalid bytes that don't match JPEG signature FF D8 FF
            var invalidBytes = new byte[] { 0x00, 0x11, 0x22, 0x33, 0x44, 0x55 };
            fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(invalidBytes));

            // Act
            var result = await _playerProfileService.UploadAvatarAsync(fileMock.Object, 1, "http://localhost:5209", CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("Nội dung tệp không khớp"));
        }

        [Test]
        public async Task UploadAvatarAsync_6_WithValidJpegImage_SavesFileUpdatesUrlAndReturnsSuccess()
        {
            // Arrange
            var user = CreateUser(60, "avatar_user", "avatar@test.vn", "Player");
            await _dbContext.Users.AddAsync(user);
            await _dbContext.SaveChangesAsync();

            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.Length).Returns(128);
            fileMock.Setup(f => f.FileName).Returns("my_photo.jpg");
            fileMock.Setup(f => f.ContentType).Returns("image/jpeg");

            // Valid JPEG magic header (FF D8 FF)
            var validJpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01 };
            fileMock.Setup(f => f.OpenReadStream()).Returns(() => new MemoryStream(validJpeg));
            fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

            // Act
            var result = await _playerProfileService.UploadAvatarAsync(fileMock.Object, 60, "http://localhost:5209", CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(user.ProfileImageUrl, Does.StartWith("http://localhost:5209/uploads/avatars/user-60-"));
            Assert.That(user.ProfileImageUrl, Does.EndWith(".jpg"));
        }

        #endregion

        #region 4. UpdateMeAsync Tests (6 test cases)

        [Test]
        public async Task UpdateMeAsync_1_WhenUserIdIsNull_ReturnsUnauthorized()
        {
            // Arrange
            var request = new UpdateUserProfileRequest { Username = "newname" };

            // Act
            var result = await _playerProfileService.UpdateMeAsync(request, null, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Unauthorized));
        }

        [Test]
        public async Task UpdateMeAsync_2_WhenUsernameIsEmpty_ReturnsBadRequest()
        {
            // Arrange
            var request = new UpdateUserProfileRequest { Username = "   " };

            // Act
            var result = await _playerProfileService.UpdateMeAsync(request, 1, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("Vui lòng nhập tên người dùng"));
        }

        [Test]
        public async Task UpdateMeAsync_3_WhenBirthDateIsInTheFuture_ReturnsBadRequest()
        {
            // Arrange
            var request = new UpdateUserProfileRequest
            {
                Username = "validname",
                BirthDate = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)) // Future date
            };

            // Act
            var result = await _playerProfileService.UpdateMeAsync(request, 1, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("Ngày sinh không được lớn hơn"));
        }

        [Test]
        public async Task UpdateMeAsync_4_WhenUsernameAlreadyTakenByAnotherUser_ReturnsConflict()
        {
            // Arrange
            var user1 = CreateUser(70, "user_alpha", "alpha@test.vn", "Player");
            var user2 = CreateUser(71, "user_beta", "beta@test.vn", "Player");
            await _dbContext.Users.AddRangeAsync(user1, user2);
            await _dbContext.SaveChangesAsync();

            var request = new UpdateUserProfileRequest { Username = "user_beta" }; // User 70 tries to take User 71's name

            // Act
            var result = await _playerProfileService.UpdateMeAsync(request, 70, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Conflict));
            Assert.That(result.ErrorMessage, Does.Contain("Tên người dùng này đã được sử dụng"));
        }

        [Test]
        public async Task UpdateMeAsync_5_WhenUserNotFound_ReturnsNotFound()
        {
            // Arrange
            var request = new UpdateUserProfileRequest { Username = "nonexistent_user" };

            // Act
            var result = await _playerProfileService.UpdateMeAsync(request, 999, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.NotFound));
        }

        [Test]
        public async Task UpdateMeAsync_6_WithValidData_UpdatesUserAndPlayerProfileSuccessfully()
        {
            // Arrange
            var user = CreateUser(80, "old_name", "update_me@test.vn", "Player");
            var player = new Player { PlayerId = 100, UserId = 80, SkillLevel = 1.0, Prestige = 5 };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.Players.AddAsync(player);
            await _dbContext.SaveChangesAsync();

            var request = new UpdateUserProfileRequest
            {
                Username = "new_cool_name",
                City = "Hà Nội",
                Commune = "Cầu Giấy",
                SkillLevel = 3.0,
                PlayerSubType = "Competitive",
                PlayFrequency = "Weekly",
                PreferredTimeSlot = "18:00 - 20:00",
                Bio = "Loving Pickleball!",
                BirthDate = new DateOnly(2000, 1, 1),
                Gender = "Male",
                HeightCm = 180,
                WeightKg = 75
            };

            // Act
            var result = await _playerProfileService.UpdateMeAsync(request, 80, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(PlayerProfileResultStatus.Success));
            Assert.That(user.Username, Is.EqualTo("new_cool_name"));
            Assert.That(user.City, Is.EqualTo("Hà Nội"));
            Assert.That(player.SkillLevel, Is.EqualTo(3.0));
            Assert.That(player.PlayerSubType, Is.EqualTo("Competitive"));
            Assert.That(player.HeightCm, Is.EqualTo(180));
            Assert.That(player.WeightKg, Is.EqualTo(75));
        }

        #endregion
    }
}
