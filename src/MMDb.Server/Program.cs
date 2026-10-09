using MMDb.Contracts;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();

var app = builder.Build();

var api = app.MapGroup("/api/v1");
api.MapGet("/health", () => new HealthResponse("ok"));

app.Run();
