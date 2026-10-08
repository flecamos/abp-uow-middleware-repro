using Repro;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseAutofac();
await builder.AddApplicationAsync<ReproModule>();

var app = builder.Build();
await app.InitializeApplicationAsync();
await app.RunAsync();

public partial class Program;
