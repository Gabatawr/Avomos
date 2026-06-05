using Avomos.Api;
using Avomos.Api.Features.Chat;
using Avomos.Api.Models;
using Avomos.Api.Pipelines;
using Avomos.Api.Services;
using MediatR;
using Microsoft.SemanticKernel;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(Program).Assembly);
    cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
});

builder.Services.AddSingleton<LlmCache>();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<RiderSeeder>();
builder.Services.AddSingleton<RiderService>();
builder.Services.AddSingleton<ChatSessionService>();
builder.Services.AddSingleton(new QdrantClient(LyricDocument.QdrantHost));
var qdrantHost = Environment.GetEnvironmentVariable("QDRANT_HOST") ?? "localhost";
builder.Services.AddHttpClient("qdrant", c => c.BaseAddress = new Uri($"http://{qdrantHost}:6333"));

builder.Services.AddSingleton(sp =>
{
    var kernelBuilder = Kernel.CreateBuilder();
    var llmEndpoint = builder.Configuration["Llm:Endpoint"]!.TrimEnd('/') + '/';
    var llmHttpClient = new HttpClient { BaseAddress = new Uri(llmEndpoint) };
    kernelBuilder.AddOpenAIChatCompletion(
        modelId: builder.Configuration["Llm:ChatModelId"]!,
        apiKey: builder.Configuration["Llm:ApiKey"]!,
        httpClient: llmHttpClient
    );
    kernelBuilder.Plugins.AddFromObject(new AvomosPlugin());
    return kernelBuilder.Build();
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("https://suno.com")
              .AllowAnyMethod()
              .AllowAnyHeader()
              .AllowCredentials());
});

var app = builder.Build();
app.UseCors();

var qdrant = app.Services.GetRequiredService<QdrantClient>();
for (var retry = 0; ; retry++)
{
    try
    {
        var collections = await qdrant.ListCollectionsAsync();
        if (!collections.Contains(LyricDocument.Collection))
        {
            await qdrant.CreateCollectionAsync(LyricDocument.Collection, LyricDocument.VectorConfig);
        }
        break;
    }
    catch when (retry < 30)
    {
        await Task.Delay(2000);
    }
}

var seeder = app.Services.GetRequiredService<RiderSeeder>();
await seeder.SeedIfEmptyAsync();

ApiEndpoints.Map(app);

app.Run();
