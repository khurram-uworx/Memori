using Memori;
using Memori.Augmentation;
using Memori.Models;
using Memori.Storage;
using Microsoft.Extensions.AI;
using OllamaSharp;
using Sample.Components;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

IChatClient chatClient = new OllamaApiClient(new Uri("http://localhost:11434"),
    "llama3.2");
IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator = new OllamaApiClient(new Uri("http://localhost:11434"),
    "all-minilm");

var conversationStorage = new InMemoryConversationStorage();
var vectorStore = new InMemoryVectorStore();
var factCollection = vectorStore.GetCollection<string, MemoryFactRecord>("memori_facts");
var options = new MemoriOptions
{
    PromptContextTagName = "custom_context",
    RecallRelevanceThreshold = 0,
};
var memori = new Memori.Memori(conversationStorage, factCollection, options,
    augmentationClient: new PromptAugmentationClient(chatClient));
chatClient = new MemoriChatClient(chatClient, memori);
builder.Services.AddScoped(_ => memori);

//var vectorStorePath = Path.Combine(AppContext.BaseDirectory, "vector-store.db");
//var vectorStoreConnectionString = $"Data Source={vectorStorePath}";
//builder.Services.AddSqliteVectorStore(_ => vectorStoreConnectionString);
//builder.Services.AddSqliteCollection<string, IngestedChunk>(IngestedChunk.CollectionName, vectorStoreConnectionString);

//builder.Services.AddSingleton<DataIngestor>();
//builder.Services.AddSingleton<SemanticSearch>();
//builder.Services.AddKeyedSingleton("ingestion_directory", new DirectoryInfo(Path.Combine(builder.Environment.WebRootPath, "Data")));
builder.Services.AddChatClient(chatClient).UseFunctionInvocation().UseLogging();
builder.Services.AddEmbeddingGenerator(embeddingGenerator);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();

app.UseStaticFiles();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
