using Microsoft.Data.Sqlite;
using Volo.Abp;
using Volo.Abp.AspNetCore.Mvc;
using Volo.Abp.AspNetCore.Uow;
using Volo.Abp.Autofac;
using Volo.Abp.Data;
using Volo.Abp.EntityFrameworkCore;
using Volo.Abp.EntityFrameworkCore.Sqlite;
using Volo.Abp.Modularity;

namespace Repro;

[DependsOn(
    typeof(AbpAspNetCoreMvcModule),
    typeof(AbpAutofacModule),
    typeof(AbpEntityFrameworkCoreSqliteModule)
)]
public class ReproModule : AbpModule
{
    private string connectionString = "";

    public override void ConfigureServices(ServiceConfigurationContext context)
    {
        var pipeline = context.Services.GetConfiguration()["Repro:Pipeline"] ?? Pipelines.Template;

        var databasePath = Path.Combine(Path.GetTempPath(), $"abp-uow-repro-{Guid.NewGuid():N}.db");
        connectionString = $"Data Source={databasePath};Foreign Keys=True";

        context.Services.AddAbpDbContext<ReproDbContext>(options =>
        {
            options.AddDefaultRepositories(includeAllEntities: true);
        });

        Configure<AbpDbConnectionOptions>(options =>
        {
            options.ConnectionStrings.Default = connectionString;
        });

        Configure<AbpDbContextOptions>(options =>
        {
            options.UseSqlite();
        });

        Configure<AbpAspNetCoreUnitOfWorkOptions>(options =>
        {
            options.CompleteUnitOfWorkOnResponseStarting = pipeline == Pipelines.TemplateWithCompleteOnResponseStarting;
        });
    }

    public override void OnPreApplicationInitialization(ApplicationInitializationContext context)
    {
        // The foreign key is deferred, so SaveChanges succeeds and the violation surfaces at COMMIT.
        // It stands in for any commit-time failure: a deferred constraint, a serialization failure, a lost connection.
        using var connection = new SqliteConnection(connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Customers (Id INTEGER NOT NULL PRIMARY KEY);
            CREATE TABLE Orders (
                Id TEXT NOT NULL PRIMARY KEY,
                CustomerId INTEGER NOT NULL REFERENCES Customers (Id) DEFERRABLE INITIALLY DEFERRED
            );
            INSERT INTO Customers (Id) VALUES (1);
            """;
        command.ExecuteNonQuery();
    }

    public override void OnApplicationInitialization(ApplicationInitializationContext context)
    {
        var app = context.GetApplicationBuilder();
        var pipeline = context.GetConfiguration()["Repro:Pipeline"] ?? Pipelines.Template;

        app.UseRouting();

        if (pipeline != Pipelines.WithoutUnitOfWorkMiddleware)
        {
            app.UseUnitOfWork();
        }

        app.UseConfiguredEndpoints();
    }
}

public static class Pipelines
{
    public const string Template = "template";
    public const string TemplateWithCompleteOnResponseStarting = "template-complete-on-response-starting";
    public const string WithoutUnitOfWorkMiddleware = "without-uow-middleware";
}
