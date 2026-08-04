using System;
using System.Threading;
using System.Threading.Tasks;
using Firebase.Database.Streaming;

namespace PicklinkBackend.Services.Shared;

public interface IFirebaseService
{
    bool IsConfigured { get; }
    Task SyncQueueAsync<T>(int queueId, T queueData, CancellationToken cancellationToken = default) where T : class;
    Task RemoveQueueAsync(int queueId, CancellationToken cancellationToken = default);
    IObservable<FirebaseEvent<T>>? SubscribeToQueueChanges<T>() where T : class;
    Task SyncChatMessageAsync<T>(int conversationId, int messageId, T messageData, CancellationToken cancellationToken = default) where T : class;
    Task RemoveChatMessageAsync(int conversationId, int messageId, CancellationToken cancellationToken = default);
    Task SyncReadReceiptAsync(int conversationId, int userId, DateTime lastReadAt, int? lastReadMessageId = null, CancellationToken cancellationToken = default);
}
