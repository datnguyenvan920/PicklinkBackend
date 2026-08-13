using System;
using System.Collections.Generic;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Moq;
using NUnit.Framework;
using PicklinkBackend.DTOs;
using PicklinkBackend.Models;
using PicklinkBackend.Repositories;
using PicklinkBackend.Services.Auth;
using PicklinkBackend.Services.Auth.Implementations;
using PicklinkBackend.Services.Infrastructure;

namespace Picklink.Test.Services.Auth
{
    [TestFixture]
    public class AuthServiceTests
    {
        private Mock<IUserRepository> _userRepositoryMock;
        private Mock<IPasswordHasher> _passwordHasherMock;
        private Mock<IJwtTokenService> _jwtTokenServiceMock;
        private Mock<IGoogleAuthService> _googleAuthServiceMock;
        private Mock<IEmailSender> _emailSenderMock;
        private Mock<ILogger<AuthService>> _loggerMock;
        private AuthService _authService;

        [SetUp]
        public void Setup()
        {
            _userRepositoryMock = new Mock<IUserRepository>();
            _passwordHasherMock = new Mock<IPasswordHasher>();
            _jwtTokenServiceMock = new Mock<IJwtTokenService>();
            _googleAuthServiceMock = new Mock<IGoogleAuthService>();
            _emailSenderMock = new Mock<IEmailSender>();
            _loggerMock = new Mock<ILogger<AuthService>>();

            _passwordHasherMock
                .Setup(h => h.Hash(It.IsAny<string>()))
                .Returns<string>(p => $"hashed_{p}");

            _passwordHasherMock
                .Setup(h => h.Verify(It.IsAny<string>(), It.IsAny<string>()))
                .Returns<string, string>((pwd, hash) => hash == $"hashed_{pwd}");

            _jwtTokenServiceMock
                .Setup(j => j.GenerateToken(It.IsAny<User>()))
                .Returns(("fake.jwt.token", DateTime.UtcNow.AddHours(2)));

            _authService = new AuthService(
                _userRepositoryMock.Object,
                _passwordHasherMock.Object,
                _jwtTokenServiceMock.Object,
                _googleAuthServiceMock.Object,
                _emailSenderMock.Object,
                _loggerMock.Object);
        }

        #region 1. RegisterAsync Tests (6 test cases)

        [Test]
        public async Task RegisterAsync_1_WithValidData_ReturnsSuccessAndAuthResponse()
        {
            // Arrange
            var request = new RegisterRequest
            {
                Username = "testplayer",
                Email = "test@picklink.vn",
                Password = "Password123!"
            };

            _userRepositoryMock
                .Setup(r => r.ExistsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            _userRepositoryMock
                .Setup(r => r.ExistsByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _authService.RegisterAsync(request, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(result.Value, Is.Not.Null);
            Assert.That(result.Value!.Token, Is.EqualTo("fake.jwt.token"));
            Assert.That(result.Value.User.Email, Is.EqualTo("test@picklink.vn"));
            _userRepositoryMock.Verify(r => r.AddUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Once);
            _userRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task RegisterAsync_2_WhenEmailAlreadyExists_ReturnsConflict()
        {
            // Arrange
            var request = new RegisterRequest { Username = "newuser", Email = "exist@picklink.vn", Password = "123" };
            _userRepositoryMock
                .Setup(r => r.ExistsByEmailAsync("exist@picklink.vn", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _authService.RegisterAsync(request, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Conflict));
            Assert.That(result.ErrorMessage, Does.Contain("Email"));
            _userRepositoryMock.Verify(r => r.AddUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task RegisterAsync_3_WhenUsernameAlreadyExists_ReturnsConflict()
        {
            // Arrange
            var request = new RegisterRequest { Username = "existinguser", Email = "unique@picklink.vn", Password = "123" };
            _userRepositoryMock
                .Setup(r => r.ExistsByEmailAsync("unique@picklink.vn", It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            _userRepositoryMock
                .Setup(r => r.ExistsByUsernameAsync("existinguser", It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _authService.RegisterAsync(request, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Conflict));
            Assert.That(result.ErrorMessage, Does.Contain("Tên người dùng"));
        }

        [Test]
        public async Task RegisterAsync_4_TrimsInputData_SuccessfullyRegisters()
        {
            // Arrange
            var request = new RegisterRequest
            {
                Username = "  padded_user  ",
                Email = "  PaddedEmail@Picklink.VN  ",
                Password = "pwd"
            };

            User? capturedUser = null;
            _userRepositoryMock
                .Setup(r => r.AddUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .Callback<User, CancellationToken>((u, _) => capturedUser = u)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _authService.RegisterAsync(request, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(capturedUser, Is.Not.Null);
            Assert.That(capturedUser!.Username, Is.EqualTo("padded_user"));
            Assert.That(capturedUser.Email, Is.EqualTo("paddedemail@picklink.vn"));
        }

        [Test]
        public async Task RegisterAsync_5_WithOptionalFields_SavesCityCommuneAndProfileImage()
        {
            // Arrange
            var request = new RegisterRequest
            {
                Username = "user_full",
                Email = "full@test.vn",
                Password = "123",
                City = "Hà Nội",
                Commune = "Tây Mỗ",
                ProfileImageUrl = "https://cdn.picklink.vn/avatar.jpg"
            };

            User? captured = null;
            _userRepositoryMock
                .Setup(r => r.AddUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .Callback<User, CancellationToken>((u, _) => captured = u)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _authService.RegisterAsync(request, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(captured!.City, Is.EqualTo("Hà Nội"));
            Assert.That(captured.Commune, Is.EqualTo("Tây Mỗ"));
            Assert.That(captured.ProfileImageUrl, Is.EqualTo("https://cdn.picklink.vn/avatar.jpg"));
            Assert.That(captured.UserType, Is.EqualTo("User"));
        }

        [Test]
        public async Task RegisterAsync_6_HashesPassword_StoresHashedPassword()
        {
            // Arrange
            var request = new RegisterRequest { Username = "secureuser", Email = "sec@test.vn", Password = "RawSecretPassword" };
            User? savedUser = null;
            _userRepositoryMock
                .Setup(r => r.AddUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .Callback<User, CancellationToken>((u, _) => savedUser = u)
                .Returns(Task.CompletedTask);

            // Act
            await _authService.RegisterAsync(request, CancellationToken.None);

            // Assert
            Assert.That(savedUser!.PasswordHash, Is.EqualTo("hashed_RawSecretPassword"));
            _passwordHasherMock.Verify(h => h.Hash("RawSecretPassword"), Times.Once);
        }

        #endregion

        #region 2. LoginAsync Tests (6 test cases)

        [Test]
        public async Task LoginAsync_1_WithValidCredentials_ReturnsSuccessAndJwtToken()
        {
            // Arrange
            var user = new User { UserId = 1, Email = "linh@picklink.test", Username = "linh", PasswordHash = "hashed_123456", IsLocked = false };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("linh@picklink.test", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.LoginAsync(new LoginRequest { Email = "linh@picklink.test", Password = "123456" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(result.Value!.Token, Is.EqualTo("fake.jwt.token"));
            Assert.That(result.Value.User.UserId, Is.EqualTo(1));
        }

        [Test]
        public async Task LoginAsync_2_WhenUserNotFound_ReturnsUnauthorized()
        {
            // Arrange
            _userRepositoryMock.Setup(r => r.GetByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

            // Act
            var result = await _authService.LoginAsync(new LoginRequest { Email = "unknown@picklink.vn", Password = "123" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Unauthorized));
            Assert.That(result.ErrorMessage, Does.Contain("không đúng"));
        }

        [Test]
        public async Task LoginAsync_3_WhenPasswordIsIncorrect_ReturnsUnauthorized()
        {
            // Arrange
            var user = new User { UserId = 2, Email = "user@test.vn", PasswordHash = "hashed_correct_pass", IsLocked = false };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("user@test.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.LoginAsync(new LoginRequest { Email = "user@test.vn", Password = "wrong_password" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task LoginAsync_4_WhenUserIsLocked_ReturnsForbidden()
        {
            // Arrange
            var user = new User { UserId = 3, Email = "locked@test.vn", PasswordHash = "hashed_123", IsLocked = true, LockReason = "Spamming" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("locked@test.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.LoginAsync(new LoginRequest { Email = "locked@test.vn", Password = "123" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Forbidden));
        }

        [Test]
        public async Task LoginAsync_5_CaseInsensitiveEmail_ReturnsSuccess()
        {
            // Arrange
            var user = new User { UserId = 4, Email = "user@picklink.vn", PasswordHash = "hashed_123", IsLocked = false };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("user@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.LoginAsync(new LoginRequest { Email = "USER@PICKLINK.VN", Password = "123" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
        }

        [Test]
        public async Task LoginAsync_6_TrimsEmail_ReturnsSuccess()
        {
            // Arrange
            var user = new User { UserId = 5, Email = "trimmed@test.vn", PasswordHash = "hashed_123", IsLocked = false };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("trimmed@test.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.LoginAsync(new LoginRequest { Email = "  trimmed@test.vn  ", Password = "123" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
        }

        #endregion

        #region 3. GoogleLoginAsync Tests (6 test cases)

        [Test]
        public async Task GoogleLoginAsync_1_WithValidTokenAndExistingUser_ReturnsSuccess()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub1", "guser@gmail.com", "G User", "https://img.com/pic.jpg");
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync("valid_id_token", It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);

            var user = new User { UserId = 10, Email = "guser@gmail.com", Username = "guser", IsLocked = false, ProfileImageUrl = "https://img.com/pic.jpg" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("guser@gmail.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "valid_id_token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(result.Value!.User.Email, Is.EqualTo("guser@gmail.com"));
        }

        [Test]
        public async Task GoogleLoginAsync_2_WhenGoogleTokenInvalid_ReturnsUnauthorized()
        {
            // Arrange
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync("invalid_token", It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SecurityTokenException("Invalid signature"));

            // Act
            var result = await _authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "invalid_token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task GoogleLoginAsync_3_WhenUserNotFound_ReturnsNotFound()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub2", "notfound@gmail.com", "Name", null);
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("notfound@gmail.com", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

            // Act
            var result = await _authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.NotFound));
            Assert.That(result.ErrorMessage, Does.Contain("chưa được đăng ký"));
        }

        [Test]
        public async Task GoogleLoginAsync_4_WhenUserIsLocked_ReturnsForbidden()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub3", "locked@gmail.com", "Locked", null);
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);
            var user = new User { UserId = 11, Email = "locked@gmail.com", IsLocked = true };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("locked@gmail.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Forbidden));
        }

        [Test]
        public async Task GoogleLoginAsync_5_WhenUserProfileImageEmpty_UpdatesWithGooglePicture()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub4", "sync_img@gmail.com", "User", "https://google.com/new_pic.jpg");
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);

            var user = new User { UserId = 12, Email = "sync_img@gmail.com", ProfileImageUrl = null, IsLocked = false };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("sync_img@gmail.com", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(user.ProfileImageUrl, Is.EqualTo("https://google.com/new_pic.jpg"));
            _userRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GoogleLoginAsync_6_WhenGoogleAuthServiceThrowsConfigError_ReturnsProblem()
        {
            // Arrange
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Missing Google client ID"));

            // Act
            var result = await _authService.GoogleLoginAsync(new GoogleLoginRequest { IdToken = "token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Problem));
        }

        #endregion

        #region 4. GoogleRegisterAsync Tests (6 test cases)

        [Test]
        public async Task GoogleRegisterAsync_1_WithValidToken_CreatesUserAndPlayerProfile()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub10", "newgoogle@gmail.com", "New Google User", "https://img.com/avatar.png");
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync("g_reg_token", It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);
            _userRepositoryMock.Setup(r => r.ExistsByEmailAsync("newgoogle@gmail.com", It.IsAny<CancellationToken>())).ReturnsAsync(false);
            _userRepositoryMock.Setup(r => r.ExistsByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);

            // Act
            var result = await _authService.GoogleRegisterAsync(new GoogleLoginRequest { IdToken = "g_reg_token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(result.Value, Is.Not.Null);
            _userRepositoryMock.Verify(r => r.AddUserAsync(It.Is<User>(u => u.UserType == "Player" && u.Email == "newgoogle@gmail.com"), It.IsAny<CancellationToken>()), Times.Once);
            _userRepositoryMock.Verify(r => r.AddPlayerAsync(It.Is<Player>(p => p.SkillLevel == 1 && p.Prestige == 5), It.IsAny<CancellationToken>()), Times.Once);
            transactionMock.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task GoogleRegisterAsync_2_WhenEmailAlreadyExists_ReturnsConflict()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub11", "existing@gmail.com", "User", null);
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);
            _userRepositoryMock.Setup(r => r.ExistsByEmailAsync("existing@gmail.com", It.IsAny<CancellationToken>())).ReturnsAsync(true);

            // Act
            var result = await _authService.GoogleRegisterAsync(new GoogleLoginRequest { IdToken = "token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Conflict));
            Assert.That(result.ErrorMessage, Does.Contain("đã được đăng ký"));
        }

        [Test]
        public async Task GoogleRegisterAsync_3_WhenGoogleTokenInvalid_ReturnsUnauthorized()
        {
            // Arrange
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new ArgumentException("Token malformed"));

            // Act
            var result = await _authService.GoogleRegisterAsync(new GoogleLoginRequest { IdToken = "malformed" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task GoogleRegisterAsync_4_WhenUsernameCollides_GeneratesUniqueUsername()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub12", "collide@gmail.com", "DuplicateName", null);
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);
            _userRepositoryMock.Setup(r => r.ExistsByEmailAsync("collide@gmail.com", It.IsAny<CancellationToken>())).ReturnsAsync(false);

            _userRepositoryMock.SetupSequence(r => r.ExistsByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true)
                .ReturnsAsync(false);

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);

            User? createdUser = null;
            _userRepositoryMock.Setup(r => r.AddUserAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
                .Callback<User, CancellationToken>((u, _) => createdUser = u)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _authService.GoogleRegisterAsync(new GoogleLoginRequest { IdToken = "token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(createdUser!.Username, Does.StartWith("DuplicateName-"));
        }

        [Test]
        public async Task GoogleRegisterAsync_5_CreatesPlayerWithDefaultSkillAndPrestige()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub13", "player_default@gmail.com", "PlayerOne", null);
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);
            _userRepositoryMock.Setup(r => r.ExistsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            _userRepositoryMock.Setup(r => r.ExistsByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);

            Player? createdPlayer = null;
            _userRepositoryMock.Setup(r => r.AddPlayerAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
                .Callback<Player, CancellationToken>((p, _) => createdPlayer = p)
                .Returns(Task.CompletedTask);

            // Act
            await _authService.GoogleRegisterAsync(new GoogleLoginRequest { IdToken = "token" }, CancellationToken.None);

            // Assert
            Assert.That(createdPlayer, Is.Not.Null);
            Assert.That(createdPlayer!.SkillLevel, Is.EqualTo(1.0));
            Assert.That(createdPlayer.Prestige, Is.EqualTo(5));
        }

        [Test]
        public async Task GoogleRegisterAsync_6_CommitsTransactionSuccessfully()
        {
            // Arrange
            var googleInfo = new GoogleUserInfo("sub14", "tx_test@gmail.com", "TxUser", null);
            _googleAuthServiceMock.Setup(g => g.VerifyIdTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(googleInfo);
            _userRepositoryMock.Setup(r => r.ExistsByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);
            _userRepositoryMock.Setup(r => r.ExistsByUsernameAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(false);

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);

            // Act
            var result = await _authService.GoogleRegisterAsync(new GoogleLoginRequest { IdToken = "token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            transactionMock.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region 5. ForgotPasswordAsync Tests (6 test cases)

        [Test]
        public async Task ForgotPasswordAsync_1_WithExistingEmail_GeneratesTokenAndSendsEmail()
        {
            // Arrange
            var user = new User { UserId = 20, Email = "member@picklink.vn", Username = "member" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("member@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);
            _userRepositoryMock.Setup(r => r.GetActivePasswordResetTokensAsync(20, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PasswordResetToken>());

            // Act
            var result = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "member@picklink.vn" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            _userRepositoryMock.Verify(r => r.AddPasswordResetTokenAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()), Times.Once);
            _emailSenderMock.Verify(e => e.SendPasswordResetCodeAsync(
                "member@picklink.vn", "member", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Once);
            transactionMock.Verify(t => t.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ForgotPasswordAsync_2_WhenEmailNotFound_ReturnsSuccessWithoutSendingEmail()
        {
            // Arrange
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("nonexistent@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

            // Act
            var result = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "nonexistent@picklink.vn" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            _emailSenderMock.Verify(e => e.SendPasswordResetCodeAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Test]
        public async Task ForgotPasswordAsync_3_ExpiresOldActiveTokens_BeforeAddingNewOne()
        {
            // Arrange
            var user = new User { UserId = 21, Email = "expire_old@picklink.vn", Username = "user21" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("expire_old@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var oldToken = new PasswordResetToken { ResetTokenId = 1, UserId = 21, UsedAt = null };
            _userRepositoryMock.Setup(r => r.GetActivePasswordResetTokensAsync(21, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PasswordResetToken> { oldToken });

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);

            // Act
            await _authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "expire_old@picklink.vn" }, CancellationToken.None);

            // Assert
            Assert.That(oldToken.UsedAt, Is.Not.Null);
        }

        [Test]
        public async Task ForgotPasswordAsync_4_WhenEmailSenderThrowsInvalidOperation_RollsBackAndReturnsServerError()
        {
            // Arrange
            var user = new User { UserId = 22, Email = "smtp_err@picklink.vn", Username = "user22" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("smtp_err@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);
            _userRepositoryMock.Setup(r => r.GetActivePasswordResetTokensAsync(22, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PasswordResetToken>());

            _emailSenderMock.Setup(e => e.SendPasswordResetCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SMTP host not set"));

            // Act
            var result = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "smtp_err@picklink.vn" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.ServerError));
            Assert.That(result.ErrorMessage, Does.Contain("chưa được cấu hình"));
            transactionMock.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ForgotPasswordAsync_5_WhenEmailSenderThrowsSmtpException_RollsBackAndReturnsServerError()
        {
            // Arrange
            var user = new User { UserId = 23, Email = "smtp_net_err@picklink.vn", Username = "user23" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("smtp_net_err@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);
            _userRepositoryMock.Setup(r => r.GetActivePasswordResetTokensAsync(23, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PasswordResetToken>());

            _emailSenderMock.Setup(e => e.SendPasswordResetCodeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new SmtpException("Connection refused"));

            // Act
            var result = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "smtp_net_err@picklink.vn" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.ServerError));
            Assert.That(result.ErrorMessage, Does.Contain("Không thể gửi mã"));
            transactionMock.Verify(t => t.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ForgotPasswordAsync_6_GeneratesTokenWith15MinutesExpiration()
        {
            // Arrange
            var user = new User { UserId = 24, Email = "time_check@picklink.vn", Username = "user24" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("time_check@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var transactionMock = new Mock<IDbContextTransaction>();
            _userRepositoryMock.Setup(r => r.BeginTransactionAsync(It.IsAny<CancellationToken>())).ReturnsAsync(transactionMock.Object);
            _userRepositoryMock.Setup(r => r.GetActivePasswordResetTokensAsync(24, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PasswordResetToken>());

            PasswordResetToken? createdToken = null;
            _userRepositoryMock.Setup(r => r.AddPasswordResetTokenAsync(It.IsAny<PasswordResetToken>(), It.IsAny<CancellationToken>()))
                .Callback<PasswordResetToken, CancellationToken>((t, _) => createdToken = t)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _authService.ForgotPasswordAsync(new ForgotPasswordRequest { Email = "time_check@picklink.vn" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(createdToken, Is.Not.Null);
            Assert.That((createdToken!.ExpiresAt - createdToken.CreatedAt).TotalMinutes, Is.EqualTo(15).Within(0.1));
        }

        #endregion

        #region 6. VerifyResetCodeAsync Tests (6 test cases)

        [Test]
        public async Task VerifyResetCodeAsync_1_WithValidCode_ReturnsSuccess()
        {
            // Arrange
            _userRepositoryMock
                .Setup(r => r.IsPasswordResetTokenValidAsync("valid@picklink.vn", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            // Act
            var result = await _authService.VerifyResetCodeAsync(new VerifyPasswordResetCodeRequest { Email = "valid@picklink.vn", Token = "12345678" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
        }

        [Test]
        public async Task VerifyResetCodeAsync_2_WithInvalidCode_ReturnsBadRequest()
        {
            // Arrange
            _userRepositoryMock
                .Setup(r => r.IsPasswordResetTokenValidAsync("bad@picklink.vn", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _authService.VerifyResetCodeAsync(new VerifyPasswordResetCodeRequest { Email = "bad@picklink.vn", Token = "99999999" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("không hợp lệ"));
        }

        [Test]
        public async Task VerifyResetCodeAsync_3_WithExpiredCode_ReturnsBadRequest()
        {
            // Arrange
            _userRepositoryMock
                .Setup(r => r.IsPasswordResetTokenValidAsync("expired@picklink.vn", It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _authService.VerifyResetCodeAsync(new VerifyPasswordResetCodeRequest { Email = "expired@picklink.vn", Token = "11112222" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.BadRequest));
        }

        [Test]
        public async Task VerifyResetCodeAsync_4_WithUsedCode_ReturnsBadRequest()
        {
            // Arrange
            _userRepositoryMock
                .Setup(r => r.IsPasswordResetTokenValidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);

            // Act
            var result = await _authService.VerifyResetCodeAsync(new VerifyPasswordResetCodeRequest { Email = "used@picklink.vn", Token = "used_token" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.BadRequest));
        }

        [Test]
        public async Task VerifyResetCodeAsync_5_TrimsEmailAndToken_ReturnsSuccess()
        {
            // Arrange
            string? capturedEmail = null;
            _userRepositoryMock
                .Setup(r => r.IsPasswordResetTokenValidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, DateTime, CancellationToken>((e, _, _, _) => capturedEmail = e)
                .ReturnsAsync(true);

            // Act
            var result = await _authService.VerifyResetCodeAsync(new VerifyPasswordResetCodeRequest { Email = "  trim@picklink.vn  ", Token = "  88887777  " }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(capturedEmail, Is.EqualTo("trim@picklink.vn"));
        }

        [Test]
        public async Task VerifyResetCodeAsync_6_CaseInsensitiveEmail_ReturnsSuccess()
        {
            // Arrange
            string? capturedEmail = null;
            _userRepositoryMock
                .Setup(r => r.IsPasswordResetTokenValidAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, DateTime, CancellationToken>((e, _, _, _) => capturedEmail = e)
                .ReturnsAsync(true);

            // Act
            await _authService.VerifyResetCodeAsync(new VerifyPasswordResetCodeRequest { Email = "CASE@PICKLINK.VN", Token = "12345678" }, CancellationToken.None);

            // Assert
            Assert.That(capturedEmail, Is.EqualTo("case@picklink.vn"));
        }

        #endregion

        #region 7. ResetPasswordAsync Tests (6 test cases)

        [Test]
        public async Task ResetPasswordAsync_1_WithValidCodeAndUser_UpdatesPasswordHashAndMarksTokenUsed()
        {
            // Arrange
            var user = new User { UserId = 30, Email = "reset_ok@picklink.vn", PasswordHash = "hashed_old_pwd" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("reset_ok@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var resetToken = new PasswordResetToken { ResetTokenId = 10, UserId = 30, ExpiresAt = DateTime.UtcNow.AddMinutes(10), UsedAt = null };
            _userRepositoryMock.Setup(r => r.GetValidPasswordResetTokenAsync(30, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(resetToken);
            _userRepositoryMock.Setup(r => r.GetActivePasswordResetTokensAsync(30, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PasswordResetToken> { resetToken });

            // Act
            var result = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = "reset_ok@picklink.vn", Token = "12345678", NewPassword = "NewSecurePassword123!" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(user.PasswordHash, Is.EqualTo("hashed_NewSecurePassword123!"));
            Assert.That(resetToken.UsedAt, Is.Not.Null);
            _userRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task ResetPasswordAsync_2_WhenUserNotFound_ReturnsBadRequest()
        {
            // Arrange
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("notfound@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

            // Act
            var result = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = "notfound@picklink.vn", Token = "12345678", NewPassword = "pwd" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("không hợp lệ"));
        }

        [Test]
        public async Task ResetPasswordAsync_3_WhenTokenNotFoundOrInvalid_ReturnsBadRequest()
        {
            // Arrange
            var user = new User { UserId = 31, Email = "no_token@picklink.vn" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("no_token@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);
            _userRepositoryMock.Setup(r => r.GetValidPasswordResetTokenAsync(31, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((PasswordResetToken?)null);

            // Act
            var result = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = "no_token@picklink.vn", Token = "invalid_token", NewPassword = "pwd" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.BadRequest));
        }

        [Test]
        public async Task ResetPasswordAsync_4_WhenTokenIsExpired_ReturnsBadRequest()
        {
            // Arrange
            var user = new User { UserId = 32, Email = "expired_tok@picklink.vn" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("expired_tok@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var expiredToken = new PasswordResetToken { ResetTokenId = 11, UserId = 32, ExpiresAt = DateTime.UtcNow.AddMinutes(-5), UsedAt = null };
            _userRepositoryMock.Setup(r => r.GetValidPasswordResetTokenAsync(32, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(expiredToken);

            // Act
            var result = await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = "expired_tok@picklink.vn", Token = "tok", NewPassword = "pwd" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.BadRequest));
        }

        [Test]
        public async Task ResetPasswordAsync_5_InvalidatesAllOtherActiveTokensForUser()
        {
            // Arrange
            var user = new User { UserId = 33, Email = "multi_tok@picklink.vn" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("multi_tok@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var currentToken = new PasswordResetToken { ResetTokenId = 12, UserId = 33, ExpiresAt = DateTime.UtcNow.AddMinutes(10) };
            var otherToken = new PasswordResetToken { ResetTokenId = 13, UserId = 33, ExpiresAt = DateTime.UtcNow.AddMinutes(10), UsedAt = null };

            _userRepositoryMock.Setup(r => r.GetValidPasswordResetTokenAsync(33, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(currentToken);
            _userRepositoryMock.Setup(r => r.GetActivePasswordResetTokensAsync(33, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PasswordResetToken> { currentToken, otherToken });

            // Act
            await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = "multi_tok@picklink.vn", Token = "tok", NewPassword = "pwd" }, CancellationToken.None);

            // Assert
            Assert.That(currentToken.UsedAt, Is.Not.Null);
            Assert.That(otherToken.UsedAt, Is.Not.Null);
        }

        [Test]
        public async Task ResetPasswordAsync_6_SavesChangesToRepository()
        {
            // Arrange
            var user = new User { UserId = 34, Email = "save_check@picklink.vn" };
            _userRepositoryMock.Setup(r => r.GetByEmailAsync("save_check@picklink.vn", It.IsAny<CancellationToken>())).ReturnsAsync(user);

            var token = new PasswordResetToken { ResetTokenId = 14, UserId = 34, ExpiresAt = DateTime.UtcNow.AddMinutes(10) };
            _userRepositoryMock.Setup(r => r.GetValidPasswordResetTokenAsync(34, It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(token);
            _userRepositoryMock.Setup(r => r.GetActivePasswordResetTokensAsync(34, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PasswordResetToken> { token });

            // Act
            await _authService.ResetPasswordAsync(new ResetPasswordRequest { Email = "save_check@picklink.vn", Token = "tok", NewPassword = "pwd" }, CancellationToken.None);

            // Assert
            _userRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        #endregion

        #region 8. AssignRoleAsync Tests (6 test cases)

        [Test]
        public async Task AssignRoleAsync_1_WhenUserIdIsNull_ReturnsUnauthorized()
        {
            // Act
            var result = await _authService.AssignRoleAsync(null, new AssignRoleRequest { Role = "Player" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task AssignRoleAsync_2_WhenUserNotFound_ReturnsNotFound()
        {
            // Arrange
            _userRepositoryMock.Setup(r => r.GetByIdAsync(999, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

            // Act
            var result = await _authService.AssignRoleAsync(999, new AssignRoleRequest { Role = "Player" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.NotFound));
        }

        [Test]
        public async Task AssignRoleAsync_3_WhenUserAlreadyHasRole_ReturnsConflict()
        {
            // Arrange
            var user = new User { UserId = 40, UserType = "VenueOwner" };
            _userRepositoryMock.Setup(r => r.GetByIdAsync(40, It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.AssignRoleAsync(40, new AssignRoleRequest { Role = "Player" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Conflict));
            Assert.That(result.ErrorMessage, Does.Contain("đã được gán vai trò"));
        }

        [Test]
        public async Task AssignRoleAsync_4_AsVenueOwner_SetsUserTypeToVenueOwner()
        {
            // Arrange
            var user = new User { UserId = 41, UserType = "User" };
            _userRepositoryMock.Setup(r => r.GetByIdAsync(41, It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.AssignRoleAsync(41, new AssignRoleRequest { Role = "VenueOwner" }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(user.UserType, Is.EqualTo("VenueOwner"));
            _userRepositoryMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task AssignRoleAsync_5_AsPlayerWithValidExperience_CreatesPlayerRecordWithMappedSkill()
        {
            // Arrange
            var user = new User { UserId = 42, UserType = "User" };
            _userRepositoryMock.Setup(r => r.GetByIdAsync(42, It.IsAny<CancellationToken>())).ReturnsAsync(user);

            Player? addedPlayer = null;
            _userRepositoryMock.Setup(r => r.AddPlayerAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
                .Callback<Player, CancellationToken>((p, _) => addedPlayer = p)
                .Returns(Task.CompletedTask);

            // Act
            var result = await _authService.AssignRoleAsync(42, new AssignRoleRequest { Role = "Player", Experience = ExperienceLevel.Intermediate }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(user.UserType, Is.EqualTo("Player"));
            Assert.That(addedPlayer, Is.Not.Null);
            Assert.That(addedPlayer!.SkillLevel, Is.EqualTo(1.5));
            Assert.That(addedPlayer.Prestige, Is.EqualTo(5));
        }

        [Test]
        public async Task AssignRoleAsync_6_AsPlayerWithoutExperience_ReturnsBadRequest()
        {
            // Arrange
            var user = new User { UserId = 43, UserType = "User" };
            _userRepositoryMock.Setup(r => r.GetByIdAsync(43, It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.AssignRoleAsync(43, new AssignRoleRequest { Role = "Player", Experience = null }, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.BadRequest));
            Assert.That(result.ErrorMessage, Does.Contain("kinh nghiệm"));
        }

        #endregion

        #region 9. GetMeAsync & GetRoleStatusAsync Tests (6 test cases)

        [Test]
        public async Task GetMeAsync_1_WhenUserIdIsNull_ReturnsUnauthorized()
        {
            // Act
            var result = await _authService.GetMeAsync(null, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task GetMeAsync_2_WhenUserExists_ReturnsUserResponse()
        {
            // Arrange
            var user = new User { UserId = 50, Email = "me@picklink.vn", Username = "myusername", UserType = "Player" };
            _userRepositoryMock.Setup(r => r.GetByIdAsync(50, It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.GetMeAsync(50, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            Assert.That(result.Value!.UserId, Is.EqualTo(50));
            Assert.That(result.Value.Email, Is.EqualTo("me@picklink.vn"));
        }

        [Test]
        public async Task GetMeAsync_3_WhenUserNotFound_ReturnsNotFound()
        {
            // Arrange
            _userRepositoryMock.Setup(r => r.GetByIdAsync(51, It.IsAny<CancellationToken>())).ReturnsAsync((User?)null);

            // Act
            var result = await _authService.GetMeAsync(51, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.NotFound));
        }

        [Test]
        public async Task GetRoleStatusAsync_4_WhenUserIdIsNull_ReturnsUnauthorized()
        {
            // Act
            var result = await _authService.GetRoleStatusAsync(null, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Unauthorized));
        }

        [Test]
        public async Task GetRoleStatusAsync_5_WhenUserHasDefaultUserType_ReturnsHasRoleFalse()
        {
            // Arrange
            var user = new User { UserId = 52, UserType = "User" };
            _userRepositoryMock.Setup(r => r.GetByIdAsync(52, It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.GetRoleStatusAsync(52, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            var hasRoleProp = result.Value!.GetType().GetProperty("hasRole");
            var userTypeProp = result.Value.GetType().GetProperty("userType");
            Assert.That((bool)hasRoleProp!.GetValue(result.Value)!, Is.False);
            Assert.That((string)userTypeProp!.GetValue(result.Value)!, Is.EqualTo("User"));
        }

        [Test]
        public async Task GetRoleStatusAsync_6_WhenUserIsVenueOwnerOrPlayer_ReturnsHasRoleTrue()
        {
            // Arrange
            var user = new User { UserId = 53, UserType = "VenueOwner" };
            _userRepositoryMock.Setup(r => r.GetByIdAsync(53, It.IsAny<CancellationToken>())).ReturnsAsync(user);

            // Act
            var result = await _authService.GetRoleStatusAsync(53, CancellationToken.None);

            // Assert
            Assert.That(result.Status, Is.EqualTo(AuthServiceResultStatus.Success));
            var hasRoleProp = result.Value!.GetType().GetProperty("hasRole");
            var userTypeProp = result.Value.GetType().GetProperty("userType");
            Assert.That((bool)hasRoleProp!.GetValue(result.Value)!, Is.True);
            Assert.That((string)userTypeProp!.GetValue(result.Value)!, Is.EqualTo("VenueOwner"));
        }

        #endregion
    }
}
