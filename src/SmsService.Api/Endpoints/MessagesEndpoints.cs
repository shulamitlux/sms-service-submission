using Microsoft.AspNetCore.Mvc;
using SmsService.Api.Contracts;
using SmsService.Api.Domain;
using SmsService.Api.Services;
using SmsService.Api.Validation;

namespace SmsService.Api.Endpoints;

/// <summary>
/// HTTP layer only: reads the request, calls <see cref="IMessageService"/>, and maps the result to a response.
/// Invalid input is rejected through <see cref="InvalidInput"/>: format errors in detail,
/// a number that belongs to no employee with a generic 400 only.
/// </summary>
public static class MessagesEndpoints
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    public static void MapMessagesEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/messages").WithTags("Messages");

        group.MapPost("/", SendMessage)
            .AddEndpointFilter<ValidationFilter<SendMessageRequest>>()
            .Produces<SendMessageResponse>(StatusCodes.Status202Accepted)
            .Produces<SendMessageResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict);

        group.MapGet("/{id}", GetMessage)
            .Produces<MessageStatusResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListMessages)
            .Produces<MessageListResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest);
    }

    private static async Task<IResult> SendMessage(
        SendMessageRequest request,
        [FromHeader(Name = IdempotencyKeyHeader)] string? idempotencyKey,
        IMessageService messages,
        HttpContext http,
        CancellationToken ct)
    {
        var result = await messages.SendAsync(request.To, request.Body, idempotencyKey, ct);

        return result switch
        {
            SendMessageResult.Created c =>
                Results.Accepted($"/messages/{c.Id}", new SendMessageResponse(c.Id, c.Status.ToString())),
            SendMessageResult.AlreadyExists e =>
                Results.Ok(new SendMessageResponse(e.Id, e.Status.ToString())),
            SendMessageResult.IdempotencyConflict =>
                Results.Problem(
                    statusCode: StatusCodes.Status409Conflict,
                    title: "Idempotency-Key conflict",
                    detail: "Idempotency-Key was already used for a message with a different recipient or body"),
            SendMessageResult.Invalid i => InvalidInput.Reject(http, i.Errors),
            SendMessageResult.RecipientNotAllowed => InvalidInput.RejectGeneric(http, "recipient is not an employee's number"),
            _ => throw new InvalidOperationException($"Unhandled result {result.GetType().Name}")
        };
    }

    private static async Task<IResult> GetMessage(string id, IMessageService messages, HttpContext http, CancellationToken ct)
    {
        if (!Guid.TryParse(id, out var messageId))
            return InvalidInput.Reject(http, "id", "Invalid message id");

        return await messages.GetStatusAsync(messageId, ct) is { } status
            ? Results.Ok(status)
            : Results.Problem(statusCode: StatusCodes.Status404NotFound, title: "Message not found");
    }

    private static async Task<IResult> ListMessages(string? status, IMessageService messages, HttpContext http, CancellationToken ct)
    {
        // The status filter is optional: without it, the newest messages of all statuses are returned.
        // It could be made required (400 when missing) if a spec asks to prevent listing everything at once.
        if (string.IsNullOrWhiteSpace(status))
            return Results.Ok(new MessageListResponse(await messages.ListAsync(null, ct)));

        // Match by name only (Enum.TryParse would also accept numbers like "1").
        var name = Enum.GetNames<MessageStatus>()
            .FirstOrDefault(n => string.Equals(n, status.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return InvalidInput.Reject(http, "status",
                $"Unknown status. Allowed values: {string.Join(", ", Enum.GetNames<MessageStatus>())}");

        var items = await messages.ListAsync(Enum.Parse<MessageStatus>(name), ct);
        return Results.Ok(new MessageListResponse(items));
    }
}
