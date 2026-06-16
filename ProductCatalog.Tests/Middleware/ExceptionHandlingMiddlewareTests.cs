using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using ProductCatalog.Api.Middleware;
using ProductCatalog.Domain.Exceptions;
using System.Text.Json;

namespace ProductCatalog.Tests.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private static async Task<(int StatusCode, JsonDocument Body)> InvokeAsync(RequestDelegate next)
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();

        var middleware = new ExceptionHandlingMiddleware(next, NullLogger<ExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(ctx);

        ctx.Response.Body.Seek(0, SeekOrigin.Begin);
        var doc = await JsonDocument.ParseAsync(ctx.Response.Body);
        return (ctx.Response.StatusCode, doc);
    }

    [Fact]
    public async Task Given_ProductNotFoundException_WhenInvoked_Then_Returns404WithProblemDetails()
    {
        var (statusCode, body) = await InvokeAsync(_ => throw new ProductNotFoundException(1));

        statusCode.Should().Be(404);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(404);
        body.RootElement.GetProperty("title").GetString().Should().Be("Not Found");
        body.RootElement.GetProperty("detail").GetString().Should()
            .Contain("1");
    }

    [Fact]
    public async Task Given_ValidationException_WhenInvoked_Then_Returns400WithFieldErrors()
    {
        var failures = new[] { new ValidationFailure("Name", "Name is required") };

        var (statusCode, body) = await InvokeAsync(_ => throw new ValidationException(failures));

        statusCode.Should().Be(400);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(400);
        body.RootElement.GetProperty("title").GetString().Should().Be("Validation Failed");

        var errors = body.RootElement.GetProperty("errors");
        errors.GetProperty("Name").EnumerateArray()
            .Select(e => e.GetString())
            .Should().Contain("Name is required");
    }

    [Fact]
    public async Task Given_UnhandledException_WhenInvoked_Then_Returns500WithoutInternalDetails()
    {
        var (statusCode, body) = await InvokeAsync(_ => throw new InvalidOperationException("internal secret"));

        statusCode.Should().Be(500);
        body.RootElement.GetProperty("status").GetInt32().Should().Be(500);

        var raw = body.RootElement.GetRawText();
        raw.Should().NotContain("internal secret");
        raw.Should().NotContain("InvalidOperationException");
        body.RootElement.GetProperty("detail").GetString()
            .Should().Be("An unexpected error occurred.");
    }
}
