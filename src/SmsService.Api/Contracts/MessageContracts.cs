using SmsService.Api.Validation;

namespace SmsService.Api.Contracts;

/// <summary>
/// Format rules are attributes, checked by <see cref="ValidationFilter{T}"/> before the endpoint runs.
/// Business rules (the number belongs to an employee, idempotency) are checked in MessageService.
/// </summary>
public sealed record SendMessageRequest(
    [property: IsraeliMobilePhone] string? To,
    [property: SmsBody] string? Body);

public sealed record SendMessageResponse(Guid Id, string Status);

/// <summary>
/// Response of GET /messages/{id}: the status only. Recipient, body, attempts and errors are not exposed here.
/// </summary>
public sealed record MessageStatusResponse(Guid Id, string Status);

/// <summary>
/// Item of GET /messages: id, masked recipient number (+97250****321) and status only.
/// Body, attempts, errors and employee names are not exposed here.
/// </summary>
public sealed record MessageListItemResponse(Guid Id, string To, string Status);

/// <summary>Wrapped in an object so pagination fields can be added later without breaking clients.</summary>
public sealed record MessageListResponse(IReadOnlyList<MessageListItemResponse> Items);
