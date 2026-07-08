using System.IdentityModel.Tokens.Jwt;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace PicklinkBackend.Services.Auth;

public class GoogleAuthService : IGoogleAuthService
{
    private const string GoogleOpenIdConfigurationUrl =
        "https://accounts.google.com/.well-known/openid-configuration";

    private static readonly string[] ValidGoogleIssuers =
    [
        "https://accounts.google.com",
        "accounts.google.com"
    ];

    private readonly IConfiguration _configuration;
    private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;

    public GoogleAuthService(IConfiguration configuration)
    {
        _configuration = configuration;
        _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
            GoogleOpenIdConfigurationUrl,
            new OpenIdConnectConfigurationRetriever(),
            new HttpDocumentRetriever { RequireHttps = true });
    }

    public async Task<GoogleUserInfo> VerifyIdTokenAsync(
        string idToken,
        CancellationToken cancellationToken = default)
    {
        var clientId = _configuration["Authentication:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new InvalidOperationException("Authentication:Google:ClientId is not configured.");
        }

        var openIdConfiguration = await _configurationManager.GetConfigurationAsync(cancellationToken);
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = ValidGoogleIssuers,
            ValidateAudience = true,
            ValidAudience = clientId,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = openIdConfiguration.SigningKeys,
            ClockSkew = TimeSpan.FromMinutes(5)
        };

        var tokenHandler = new JwtSecurityTokenHandler
        {
            MapInboundClaims = false
        };
        var principal = tokenHandler.ValidateToken(idToken, validationParameters, out var validatedToken);

        if (validatedToken is not JwtSecurityToken jwtToken ||
            !string.Equals(jwtToken.Header.Alg, SecurityAlgorithms.RsaSha256, StringComparison.Ordinal))
        {
            throw new SecurityTokenException("Invalid Google token signing algorithm.");
        }

        var subject = principal.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        var email = principal.FindFirst(JwtRegisteredClaimNames.Email)?.Value;
        var emailVerified = principal.FindFirst("email_verified")?.Value;

        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            throw new SecurityTokenException("Google token does not contain a valid subject or email.");
        }

        if (!string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
        {
            throw new SecurityTokenException("Google email is not verified.");
        }

        return new GoogleUserInfo(
            subject,
            email,
            principal.FindFirst("name")?.Value,
            principal.FindFirst("picture")?.Value);
    }
}
