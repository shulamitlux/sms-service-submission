using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using SmsService.Api.Contracts;
using SmsService.Api.Data;
using SmsService.Api.Domain;
using SmsService.Api.Sending;
using SmsService.Api.Validation;

namespace SmsService.Api.Services;

/// <summary>Outcome of <see cref="MessageService.SendAsync"/>. The endpoint maps each case to an HTTP response.</summary>
public abstract record SendMessageResult
{
    /// <summary>A new message was saved and handed to the worker.</summary>
    public sealed record Created(Guid Id, MessageStatus Status) : SendMessageResult;

    /// <summary>The idempotency key was already used with the same recipient and body.</summary>
    public sealed record AlreadyExists(Guid Id, MessageStatus Status) : SendMessageResult;

    /// <summary>The idempotency key was already used with a different recipient or body.</summary>
    public sealed record IdempotencyConflict : SendMessageResult;

    /// <summary>Format validation failed. Keys are the request field names; returned to the client in detail.</summary>
    public sealed record Invalid(Dictionary<string, string[]> Errors) : SendMessageResult;

    /// <summary>
    /// The number is valid but belongs to no employee. Returned to the client as a generic 400 only,
    /// so the response does not reveal which numbers belong to employees.
    /// </summary>
    public sealed record RecipientNotAllowed : SendMessageResult;
}

/// <inheritdoc />
public class MessageService(AppDbContext db, SendWorkerSignal signal, TimeProvider clock, ILogger<MessageService> logger)
    : IMessageService
{
    public const int MaxIdempotencyKeyLength = 100;
    public const int MaxListSize = 100;

    // Same name as the HTTP header, so the client sees the error under the field it sent.
    private const string IdempotencyKeyField = "Idempotency-Key";

    /// <inheritdoc />
    public async Task<SendMessageResult> SendAsync(string? to, string? body, string? idempotencyKey, CancellationToken ct)
    {
        // 1. Format errors: collected together and returned to the client in detail.
        //    Over HTTP, ValidationFilter already rejects most of them before this point.
        var errors = new Dictionary<string, string[]>();

        var phone = PhoneNumberNormalizer.Normalize(to);
        if (!phone.IsValid) errors["to"] = [phone.Error!];

        var trimmedBody = body?.Trim() ?? "";
        if (MessageBodyRules.Validate(trimmedBody) is { } bodyError) errors["body"] = [bodyError];

        if (idempotencyKey is not null && (idempotencyKey.Trim().Length == 0 || idempotencyKey.Length > MaxIdempotencyKeyLength))
            errors[IdempotencyKeyField] = [$"Idempotency-Key must be 1-{MaxIdempotencyKeyLength} characters long"];

        if (errors.Count > 0) return new SendMessageResult.Invalid(errors);

        // 2. The system sends SMS to employees only: the number must belong to at least one employee.
        //    A number may be shared by several employees; it is still one message to one number.
        //    Generic error on purpose: saying "not an employee" would let anyone find out which numbers
        //    belong to employees. The real reason goes to the log only.
        if (!await db.EmployeePhones.AnyAsync(p => p.PhoneE164 == phone.E164, ct))
        {
            logger.LogInformation("Rejected message: number does not belong to any employee. ToSuffix={ToSuffix}",
                LogHelpers.PhoneSuffix(phone.E164!));
            return new SendMessageResult.RecipientNotAllowed();
        }

        if (idempotencyKey is not null &&
            await FindByIdempotencyKey(idempotencyKey, phone.E164!, trimmedBody, ct) is { } existingResult)
            return existingResult;

        var message = Message.Create(phone.E164!, trimmedBody, MessageBodyRules.CountSegments(trimmedBody), idempotencyKey, clock.GetUtcNow());
        db.Messages.Add(message);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (idempotencyKey is not null && IsUniqueViolation(ex))
        {
            // Two concurrent requests with the same key: the other one won the race.
            logger.LogInformation("Idempotency key race resolved in favor of the existing message");
            db.ChangeTracker.Clear();
            return await FindByIdempotencyKey(idempotencyKey, phone.E164!, trimmedBody, ct)
                   ?? throw new InvalidOperationException("Unique violation on idempotency key but no message found.", ex);
        }

        logger.LogInformation("Message {MessageId} created. Status={Status} Segments={Segments} ToSuffix={ToSuffix}",
            message.Id, message.Status, message.Segments, LogHelpers.PhoneSuffix(message.To));
        signal.Notify(message.Id);

        return new SendMessageResult.Created(message.Id, message.Status);
    }

    /// <inheritdoc />
    public Task<MessageStatusResponse?> GetStatusAsync(Guid id, CancellationToken ct) =>
        db.Messages.AsNoTracking()
            .Where(m => m.Id == id)
            .Select(m => new MessageStatusResponse(m.Id, m.Status.ToString()))
            .SingleOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<MessageListItemResponse>> ListAsync(MessageStatus? status, CancellationToken ct)
    {
        var query = db.Messages.AsNoTracking();
        if (status is { } s)
            query = query.Where(m => m.Status == s);

        var rows = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(MaxListSize)
            .Select(m => new { m.Id, m.To, m.Status })
            .ToListAsync(ct);

        return rows.Select(m => new MessageListItemResponse(m.Id, PhoneMask.Mask(m.To), m.Status.ToString())).ToList();
    }

    /// <summary>Same key + same content -> the existing message. Same key + different content -> conflict.</summary>
    private async Task<SendMessageResult?> FindByIdempotencyKey(string key, string toE164, string body, CancellationToken ct)
    {
        var existing = await db.Messages.AsNoTracking()
            .Where(m => m.IdempotencyKey == key)
            .Select(m => new { m.Id, m.To, m.Body, m.Status })
            .SingleOrDefaultAsync(ct);

        if (existing is null) return null;

        if (existing.To != toE164 || existing.Body != body)
            return new SendMessageResult.IdempotencyConflict();

        return new SendMessageResult.AlreadyExists(existing.Id, existing.Status);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is SqlException { Number: 2601 or 2627 };
}
