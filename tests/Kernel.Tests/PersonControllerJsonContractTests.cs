using System.Net;
using System.Net.Http.Json;
using ICP.Platform.Modules.Organization.Models;
using ICP.Platform.Modules.Organization.Repositories;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace ICP.Platform.Kernel.Tests;

public class PersonControllerJsonContractTests : IClassFixture<PersonControllerJsonContractTests.TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public PersonControllerJsonContractTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreatePerson_AcceptsStringEnumEmploymentStatus_FromFrontendPayload()
    {
        using var client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });

        var response = await client.PostAsJsonAsync("/api/organization/persons", new
        {
            personNo = "P" + Guid.NewGuid().ToString("N")[..8],
            name = "张三",
            employmentStatus = "FullTime",
            isEnabled = true
        });

        Assert.True(
            response.StatusCode == HttpStatusCode.Created,
            $"Expected Created but got {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    public class TestWebApplicationFactory : WebApplicationFactory<Program>, IDisposable
    {
        private readonly string _installRoot;

        public TestWebApplicationFactory()
        {
            _installRoot = Path.Combine(Path.GetTempPath(), $"person_json_contract_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_installRoot);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting("Platform:PluginInstallRoot", _installRoot);
            builder.UseSetting("Platform:SkipLocalAuthSeeding", "true");
            builder.UseSetting("Persistence:AutoMigrate", "false");
            builder.UseSetting("DevelopmentSchemaSync:Enabled", "false");
            builder.UseSetting("ASPNETCORE_ENVIRONMENT", "Development");
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IPersonRepository>();
                services.AddSingleton<IPersonRepository, InMemoryPersonRepository>();
            });
        }

        public new void Dispose()
        {
            base.Dispose();
            if (Directory.Exists(_installRoot))
            {
                Directory.Delete(_installRoot, recursive: true);
            }
        }
    }

    private sealed class InMemoryPersonRepository : IPersonRepository
    {
        private readonly List<Person> _persons = [];

        public Task<Person?> GetByIdAsync(Guid personId, CancellationToken cancellationToken = default) =>
            Task.FromResult(_persons.FirstOrDefault(person => person.Id == personId));

        public Task<IReadOnlyList<Person>> GetListAsync(
            string? keyword,
            Guid? companyId,
            Guid? departmentId,
            EmploymentStatus? employmentStatus,
            CancellationToken cancellationToken = default)
        {
            IEnumerable<Person> query = _persons;

            if (employmentStatus.HasValue)
            {
                query = query.Where(person => person.EmploymentStatus == employmentStatus.Value);
            }

            return Task.FromResult<IReadOnlyList<Person>>(query.ToList());
        }

        public Task<bool> ExistsPersonNoAsync(
            Guid? companyId,
            string personNo,
            Guid? excludedPersonId = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(_persons.Any(person =>
                person.CompanyId == companyId &&
                person.Id != excludedPersonId &&
                string.Equals(person.PersonNo, personNo, StringComparison.OrdinalIgnoreCase)));

        public Task CreateAsync(Person person, CancellationToken cancellationToken = default)
        {
            _persons.Add(person);
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Person person, CancellationToken cancellationToken = default)
        {
            var index = _persons.FindIndex(existing => existing.Id == person.Id);
            if (index >= 0)
            {
                _persons[index] = person;
            }

            return Task.CompletedTask;
        }
    }
}
