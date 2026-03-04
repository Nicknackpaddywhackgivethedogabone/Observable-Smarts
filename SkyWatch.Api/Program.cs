using SkyWatch.Api.Services;
using SkyWatch.Api.Workers;

var builder = WebApplication.CreateBuilder(args);

// Load local secrets file (not committed to source control)
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: true);

// Add services
builder.Services.AddControllers()
    .AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Observable Smarts API", Version = "v1",
        Description = "OSINT Live Globe — satellite positions, flights, ships, and imagery" });
});

// Memory cache
builder.Services.AddMemoryCache();

// HTTP clients
builder.Services.AddHttpClient("Celestrak", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("ObservableSmarts/1.0");
});
builder.Services.AddHttpClient("OpenSky", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("ObservableSmarts/1.0");
});
builder.Services.AddHttpClient("AisHub", c =>
{
    c.Timeout = TimeSpan.FromSeconds(15);
});
builder.Services.AddHttpClient("Copernicus", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("ObservableSmarts/1.0");
});
builder.Services.AddHttpClient("USGS", c =>
{
    c.Timeout = TimeSpan.FromSeconds(30);
    c.DefaultRequestHeaders.UserAgent.ParseAdd("ObservableSmarts/1.0");
});

// Application services
builder.Services.AddScoped<TleService>();
builder.Services.AddScoped<ImagingFootprintService>();
builder.Services.AddScoped<FlightService>();
builder.Services.AddScoped<ShipService>();
builder.Services.AddScoped<OpenSourceImageryService>();
builder.Services.AddScoped<RegionOfInterestService>();

// Background workers
builder.Services.AddHostedService<SatellitePositionWorker>();
builder.Services.AddHostedService<FlightWorker>();
builder.Services.AddHostedService<ShipWorker>();
builder.Services.AddHostedService<ImageryWorker>();

// CORS — open for local network
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Observable Smarts API v1");
});

app.UseCors();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapControllers();

// Fallback to index.html for SPA routing
app.MapFallbackToFile("index.html");

app.Run();
