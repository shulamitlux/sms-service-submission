using SmsService.Api.Contracts;
using SmsService.Api.Domain;

namespace SmsService.Api.Services;

/// <summary>
/// Message use cases: validation, idempotency, persistence and queries.
/// Knows nothing about HTTP; the endpoints translate requests and results.
/// </summary>
public interface IMessageService
{
    /// <summary>Validates and saves a new message, or returns the existing one for a repeated idempotency key.</summary>
    Task<SendMessageResult> SendAsync(string? to, string? body, string? idempotencyKey, CancellationToken ct);

    /// <summary>The message status, or null if no such message exists.</summary>
    Task<MessageStatusResponse?> GetStatusAsync(Guid id, CancellationToken ct);

    /// <summary>
    /// The newest messages, up to <see cref="MessageService.MaxListSize"/>.
    /// Filtered by <paramref name="status"/> when given; all statuses when null.
    /// </summary>
    Task<IReadOnlyList<MessageListItemResponse>> ListAsync(MessageStatus? status, CancellationToken ct);
}
