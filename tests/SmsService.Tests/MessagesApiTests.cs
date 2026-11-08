using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmsService.Api.Contracts;
using SmsService.Api.Endpoints;
using SmsService.Api.Services;
using SmsService.Api.Validation;
using SmsService.Tests.Infrastructure;

namespace SmsService.Tests;

[Collection(SqlServerCollection.Name)]
public class MessagesApiTests(SqlServerFixture sql)
{
    private static HttpRequestMessage Post(object body, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/messages") { Content = JsonContent.Create(body) };
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    [Fact]
    public async Task Same_idempotency_key_twice_returns_same_id_and_stores_one_message()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();
        var body = new { to = "050-7654321", body = "שלום, הישיבה נדחתה למחר" };

        var first = await client.SendAsync(Post(body, "key-1"));
        var second = await client.SendAsync(Post(body, "key-1"));

        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        first.Headers.Location!.ToString().Should().StartWith("/messages/");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstId = (await first.Content.ReadFromJsonAsync<SendMessageResponse>())!.Id;
        var secondId = (await second.Content.ReadFromJsonAsync<SendMessageResponse>())!.Id;
        secondId.Should().Be(firstId);

        await using var db = SqlServerFixture.CreateContext(cs);
        (await db.Messages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Same_idempotency_key_with_different_body_returns_409()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        await client.SendAsync(Post(new { to = "0507654321", body = "א" }, "key-2"));
        var conflict = await client.SendAsync(Post(new { to = "0507654321", body = "ב" }, "key-2"));

        conflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Format errors: 400 ValidationProblem with the failing fields and reasons.</summary>
    private static async Task<Dictionary<string, string[]>> ShouldBeValidationProblem(HttpResponseMessage response, params string[] fields)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        problem!.Errors.Keys.Should().BeEquivalentTo(fields);
        return new Dictionary<string, string[]>(problem.Errors);
    }

    /// <summary>A number that belongs to no employee: generic 400, no field names, no reasons.</summary>
    private static async Task ShouldBeGenericInvalidInput(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        doc.RootElement.GetProperty("title").GetString().Should().Be(InvalidInput.Title);
        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("detail", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_format_returns_all_errors_per_field_and_logs_them()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        var response = await client.SendAsync(Post(new { to = "02-6543210", body = "   " }));

        var errors = await ShouldBeValidationProblem(response, "to", "body");
        errors["to"].Should().Contain(PhoneNumberNormalizer.InvalidError);
        errors["body"].Should().Contain(MessageBodyRules.RequiredError);

        factory.Logs.Messages.Should().Contain(m =>
            m.Contains(PhoneNumberNormalizer.InvalidError) && m.Contains(MessageBodyRules.RequiredError));
    }

    [Fact]
    public async Task Missing_fields_return_required_errors_per_field()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        var response = await client.SendAsync(Post(new { }));

        var errors = await ShouldBeValidationProblem(response, "to", "body");
        errors["to"].Should().Contain(PhoneNumberNormalizer.RequiredError);
        errors["body"].Should().Contain(MessageBodyRules.RequiredError);
        factory.Logs.Messages.Should().Contain(m =>
            m.Contains(PhoneNumberNormalizer.RequiredError) && m.Contains(MessageBodyRules.RequiredError));
    }

    [Fact]
    public async Task Post_to_valid_number_that_belongs_to_no_employee_returns_400_and_stores_nothing()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs); // startup seeds employees from employees.seed.json
        var client = factory.CreateClient();

        // A valid Israeli mobile number that is not in the seed file.
        var response = await client.SendAsync(Post(new { to = "050-9999999", body = "hello" }));

        // Generic on purpose: the response must not reveal whether the number belongs to an employee.
        await ShouldBeGenericInvalidInput(response);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("employee");
        factory.Logs.Messages.Should().Contain(m => m.Contains("does not belong to any employee"));

        await using var db = SqlServerFixture.CreateContext(cs);
        (await db.Messages.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Post_to_number_shared_by_two_employees_creates_a_single_message()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        // In the seed file, 053-3333333 is shared by three employees.
        var response = await client.SendAsync(Post(new { to = "053-3333333", body = "hello" }));

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        await using var db = SqlServerFixture.CreateContext(cs);
        (await db.Messages.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Get_by_id_returns_status_only_and_handles_404_and_400()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        var created = await client.SendAsync(Post(new { to = "050-7654321", body = "hello" }));
        var id = (await created.Content.ReadFromJsonAsync<SendMessageResponse>())!.Id;

        var response = await client.GetAsync($"/messages/{id}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<MessageStatusResponse>();
        status!.Id.Should().Be(id);
        status.Status.Should().BeOneOf("Pending", "Sending", "Sent", "Failed");

        // Only the id and the status are part of the status response.
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("\"to\"").And.NotContain("\"body\"").And.NotContain("\"employees\"")
            .And.NotContain("\"attempts\"").And.NotContain("\"lastError\"").And.NotContain("\"updatedAt\"");

        (await client.GetAsync($"/messages/{Guid.NewGuid()}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        await ShouldBeValidationProblem(await client.GetAsync("/messages/not-a-guid"), "id");
    }

    [Fact]
    public async Task List_returns_id_masked_phone_and_status_only()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        var created = await client.SendAsync(Post(new { to = "053-3333333", body = "hello" }));
        var id = (await created.Content.ReadFromJsonAsync<SendMessageResponse>())!.Id;

        var response = await client.GetAsync("/messages?status=Pending");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<MessageListResponse>();
        var message = list!.Items.Should().ContainSingle().Subject;
        message.Id.Should().Be(id);
        // Masked: enough to recognize the recipient, not enough to collect employee numbers from the list.
        message.To.Should().Be("+97253****333");
        (await response.Content.ReadAsStringAsync()).Should().NotContain("+972533333333");
        message.Status.Should().Be("Pending");

        // Only the id, the phone number and the status are part of a list item.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        json.RootElement.GetProperty("items")[0].EnumerateObject().Select(p => p.Name)
            .Should().BeEquivalentTo("id", "to", "status");
    }

    [Fact]
    public async Task List_filters_by_status_case_insensitively()
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();
        await client.SendAsync(Post(new { to = "0507654321", body = "one" }));

        var pending = await client.GetFromJsonAsync<MessageListResponse>("/messages?status=PENDING");
        var sent = await client.GetFromJsonAsync<MessageListResponse>("/messages?status=sent");

        pending!.Items.Should().ContainSingle().Which.Status.Should().Be("Pending");
        sent!.Items.Should().BeEmpty();
    }

    [Theory]
    [InlineData("/messages")]
    [InlineData("/messages?status=")]
    [InlineData("/messages?status=%20")]
    public async Task List_without_status_returns_all_statuses(string url)
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();
        await client.SendAsync(Post(new { to = "0507654321", body = "one" }));
        await client.SendAsync(Post(new { to = "053-3333333", body = "two" }));

        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await response.Content.ReadFromJsonAsync<MessageListResponse>();
        list!.Items.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("/messages?status=Bogus")]
    [InlineData("/messages?status=1")]
    public async Task List_rejects_unknown_status(string url)
    {
        var cs = await sql.CreateDatabaseAsync();
        await using var factory = new ApiFactory(cs);
        var client = factory.CreateClient();

        var response = await client.GetAsync(url);

        // A format error: the client is told which values are allowed.
        var errors = await ShouldBeValidationProblem(response, "status");
        errors["status"].Single().Should().Contain("Pending");
        factory.Logs.Messages.Should().Contain(m => m.Contains("status") && m.Contains("Allowed values"));
    }
}
