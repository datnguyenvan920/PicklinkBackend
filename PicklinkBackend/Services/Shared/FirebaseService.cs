using System;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Database;
using Firebase.Database.Query;
using Firebase.Database.Streaming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace PicklinkBackend.Services.Shared;

public class FirebaseService : IFirebaseService
{
    private readonly FirebaseClient? _firebaseClient;
    private readonly ILogger<FirebaseService> _logger;

    public bool IsConfigured => _firebaseClient != null;

    public FirebaseService(IConfiguration configuration, ILogger<FirebaseService> logger)
    {
        _logger = logger;

        var databaseUrl = configuration["Firebase:DatabaseUrl"];
        var authSecret = configuration["Firebase:AuthSecret"];
        var credentialPath = configuration["Firebase:CredentialPath"];

        if (string.IsNullOrWhiteSpace(databaseUrl))
        {
            _logger.LogWarning("Firebase:DatabaseUrl is not configured. Firebase realtime sync is disabled.");
            _firebaseClient = null;
            return;
        }

        try
        {
            var options = new FirebaseOptions();

            if (!string.IsNullOrWhiteSpace(credentialPath) && System.IO.File.Exists(credentialPath))
            {
                var googleCredential = Google.Apis.Auth.OAuth2.GoogleCredential.FromFile(credentialPath)
                    .CreateScoped("https://www.googleapis.com/auth/userinfo.email", "https://www.googleapis.com/auth/firebase.database");

                options.AuthTokenAsyncFactory = async () =>
                {
                    return await googleCredential.UnderlyingCredential.GetAccessTokenForRequestAsync();
                };
                _logger.LogInformation("FirebaseService initialized using Service Account Credential file at: {path}", credentialPath);
            }
            else if (!string.IsNullOrWhiteSpace(authSecret))
            {
                options.AuthTokenAsyncFactory = () => Task.FromResult(authSecret);
                _logger.LogInformation("FirebaseService initialized using Database AuthSecret.");
            }
            else
            {
                _logger.LogInformation("FirebaseService initialized in open access mode (no auth secret/credential file provided).");
            }

            _firebaseClient = new FirebaseClient(databaseUrl, options);
            _logger.LogInformation("FirebaseService successfully connected with URL: {url}", databaseUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize FirebaseClient with URL: {url}", databaseUrl);
            _firebaseClient = null;
        }
    }

    public async Task SyncQueueAsync<T>(int queueId, T queueData, CancellationToken cancellationToken = default) where T : class
    {
        if (_firebaseClient == null) return;

        try
        {
            await _firebaseClient
                .Child("matchmaking_queues")
                .Child(queueId.ToString())
                .PutAsync(queueData);

            _logger.LogInformation("Firebase: Synced queue #{queueId} to Firebase Realtime Database.", queueId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase: Failed to sync queue #{queueId}.", queueId);
        }
    }

    public async Task RemoveQueueAsync(int queueId, CancellationToken cancellationToken = default)
    {
        if (_firebaseClient == null) return;

        try
        {
            await _firebaseClient
                .Child("matchmaking_queues")
                .Child(queueId.ToString())
                .DeleteAsync();

            _logger.LogInformation("Firebase: Removed queue #{queueId} from Firebase Realtime Database.", queueId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase: Failed to remove queue #{queueId}.", queueId);
        }
    }

    public IObservable<FirebaseEvent<T>>? SubscribeToQueueChanges<T>() where T : class
    {
        if (_firebaseClient == null) return null;

        try
        {
            return _firebaseClient
                .Child("matchmaking_queues")
                .AsObservable<T>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase: Failed to subscribe to matchmaking_queues streaming events.");
            return null;
        }
    }

    public async Task SyncChatMessageAsync<T>(int conversationId, int messageId, T messageData, CancellationToken cancellationToken = default) where T : class
    {
        if (_firebaseClient == null) return;

        try
        {
            await _firebaseClient
                .Child("conversations")
                .Child(conversationId.ToString())
                .Child("messages")
                .Child(messageId.ToString())
                .PutAsync(messageData);

            _logger.LogInformation("Firebase: Synced chat message #{messageId} in conversation #{conversationId} to Firebase.", messageId, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase: Failed to sync chat message #{messageId} in conversation #{conversationId}.", messageId, conversationId);
        }
    }

    public async Task RemoveChatMessageAsync(int conversationId, int messageId, CancellationToken cancellationToken = default)
    {
        if (_firebaseClient == null) return;

        try
        {
            await _firebaseClient
                .Child("conversations")
                .Child(conversationId.ToString())
                .Child("messages")
                .Child(messageId.ToString())
                .DeleteAsync();

            _logger.LogInformation("Firebase: Removed chat message #{messageId} from conversation #{conversationId} in Firebase.", messageId, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase: Failed to remove chat message #{messageId} from conversation #{conversationId}.", messageId, conversationId);
        }
    }

    public async Task SyncReadReceiptAsync(int conversationId, int userId, DateTime lastReadAt, int? lastReadMessageId = null, CancellationToken cancellationToken = default)
    {
        if (_firebaseClient == null) return;

        try
        {
            var receiptData = new
            {
                UserId = userId,
                LastReadAt = lastReadAt.ToString("o"),
                LastReadMessageId = lastReadMessageId
            };

            await _firebaseClient
                .Child("conversations")
                .Child(conversationId.ToString())
                .Child("read_receipts")
                .Child(userId.ToString())
                .PutAsync(receiptData);

            _logger.LogInformation("Firebase: Synced read receipt for user #{userId} in conversation #{conversationId} to Firebase.", userId, conversationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Firebase: Failed to sync read receipt for user #{userId} in conversation #{conversationId}.", userId, conversationId);
        }
    }
}
