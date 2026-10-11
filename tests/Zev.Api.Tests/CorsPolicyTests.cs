using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Zev.Api.Data;

namespace Zev.Api.Tests;

// Any origin is allowed only in Development (LAN access from a phone); every other environment keeps Web:Origin.
public sealed class CorsPolicyTests
{
    [Fact]
    public async Task OutsideDevelopmentOnlyTheConfiguredWebOriginIsAllowed()
    {
        using var factory = new TestFactory();
        using var client = factory.CreateClient();

        async Task<HttpResponseMessage> PreflightAsync(string origin)
        {
            using var request = new HttpRequestMessage(HttpMethod.Options, "/api/auth/login");
            request.Headers.Add("Origin", origin);
            request.Headers.Add("Access-Control-Request-Method", "POST");
            request.Headers.Add("Access-Control-Request-Headers", "content-type");
            return await client.SendAsync(request);
        }

        var configured = await PreflightAsync("https://zev.example.invalid");
        Assert.Equal("https://zev.example.invalid", configured.Headers.GetValues("Access-Control-Allow-Origin").Single());
        Assert.Equal("true", configured.Headers.GetValues("Access-Control-Allow-Credentials").Single());

        var lan = await PreflightAsync("http://192.168.1.25:5173");
        Assert.False(lan.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private sealed class TestFactory : WebApplicationFactory<Program>
    {
        private readonly string databaseName = Guid.NewGuid().ToString();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureLogging(logging => logging.ClearProviders());
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Server=localhost;Database=TestOnly;Trusted_Connection=True",
                ["Jwt:SigningKey"] = "test-only-signing-key-with-at-least-32-characters",
                ["Jwt:Issuer"] = "test-issuer",
                ["Jwt:Audience"] = "test-audience",
                ["Web:Origin"] = "https://zev.example.invalid"
            }));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<DbContextOptions<ZevDbContext>>();
                services.RemoveAll<IDbContextOptionsConfiguration<ZevDbContext>>();
                services.RemoveAll<ZevDbContext>();
                services.AddDbContext<ZevDbContext>(options => options.UseInMemoryDatabase(databaseName)
                    .ConfigureWarnings(warnings => warnings.Ignore(InMemoryEventId.TransactionIgnoredWarning)));
            });
        }
    }
}
