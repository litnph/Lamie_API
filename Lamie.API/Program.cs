using Lamie.API.Middlewares;
using Lamie.API.Health;
using Lamie.API.Options;
using Lamie.API.Services;
using Lamie.API.Authorization;
using Lamie.Application;
using Lamie.Application.Channels;
using Lamie.Application.ChatAnalysis;
using Lamie.Application.Common.Behaviors;
using Lamie.Application.Common.Persistence;
using Lamie.Application.Common.Storage;
using Lamie.Application.Common.Uploads;
using Lamie.Application.Expenses;
using Lamie.Application.Identity;
using Lamie.Application.Reports;
using Lamie.Application.Settings.Products.Commands;
using Lamie.Domain.Entities;
using Lamie.Domain.Repositories;
using Lamie.Infrastructure.Options;
using Lamie.Infrastructure.Persistence;
using Lamie.Infrastructure.Persistence.Repositories;
using Lamie.Infrastructure.Storage;
using AutoMapper;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// Console/debug logging is sufficient for this API. The Windows Event Log provider can
// throw when the process identity cannot write to Event Log, masking the original API error.
builder.Logging.ClearProviders();
builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services
    .AddControllers()
    .ConfigureApiBehaviorOptions(options =>
    {
        options.InvalidModelStateResponseFactory = context =>
        {
            var errors = context.ModelState
                .Where(x => x.Value?.Errors.Count > 0)
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value!.Errors
                        .Select(e => string.IsNullOrWhiteSpace(e.ErrorMessage) ? "Invalid value" : e.ErrorMessage)
                        .ToArray()
                );

            return new BadRequestObjectResult(new
            {
                success = false,
                code = "VALIDATION_ERROR",
                message = "Validation failed",
                errors
            });
        };
    });
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
var allowedOrigins = (builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [])
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();
if (allowedOrigins.Any(origin =>
        !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
        || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)))
{
    throw new InvalidOperationException("CORS origins must be absolute HTTP or HTTPS origins.");
}
if (!builder.Environment.IsDevelopment())
{
    var configurationErrors = new List<string>();
    if (string.IsNullOrWhiteSpace(jwtOptions.Issuer)
        || string.IsNullOrWhiteSpace(jwtOptions.Audience)
        || Encoding.UTF8.GetByteCount(jwtOptions.SigningKey) < 32)
        configurationErrors.Add("JWT issuer, audience, and a signing key of at least 32 bytes are required");
    if (allowedOrigins.Length == 0)
        configurationErrors.Add("at least one CORS origin is required");
    if (string.IsNullOrWhiteSpace(builder.Configuration["AllowedHosts"])
        || builder.Configuration["AllowedHosts"] == "*")
        configurationErrors.Add("AllowedHosts must be restricted");
    var connectionString = builder.Configuration.GetConnectionString("Default");
    if (string.IsNullOrWhiteSpace(connectionString)
        || connectionString.Contains("(localdb)", StringComparison.OrdinalIgnoreCase))
        configurationErrors.Add("a non-LocalDB production connection string is required");

    if (configurationErrors.Count > 0)
        throw new InvalidOperationException(
            $"Production configuration is invalid: {string.Join("; ", configurationErrors)}.");
}
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<BootstrapAdminOptions>(
    builder.Configuration.GetSection(BootstrapAdminOptions.SectionName));
builder.Services.Configure<AdministrativeDataOptions>(
    builder.Configuration.GetSection(AdministrativeDataOptions.SectionName));
builder.Services.Configure<AdministrativeAddressResolutionOptions>(
    builder.Configuration.GetSection(AdministrativeAddressResolutionOptions.SectionName));
builder.Services.AddOptions<ChatScreenshotAnalysisOptions>()
    .Bind(builder.Configuration.GetSection(ChatScreenshotAnalysisOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.TessdataPath), "TessdataPath is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.TesseractExecutablePath), "TesseractExecutablePath is required.")
    .Validate(options => options.MaximumFiles is >= 1 and <= ImageUploadPolicy.MaximumFileCount, "MaximumFiles is invalid.")
    .Validate(options => options.MaximumFileBytes is > 0 and <= ImageUploadPolicy.MaximumFileBytes, "MaximumFileBytes is invalid.")
    .Validate(options => options.MaximumImagePixels is >= 1_000_000 and <= 100_000_000, "MaximumImagePixels is invalid.")
    .Validate(options => options.MaximumImageDimension is >= 1_000 and <= 32_768, "MaximumImageDimension is invalid.")
    .Validate(options => options.MaximumImageAspectRatio is >= 1m and <= 20m, "MaximumImageAspectRatio is invalid.")
    .Validate(options => options.MaximumWorkingImagePixels is >= 500_000
        && options.MaximumWorkingImagePixels <= options.MaximumImagePixels, "MaximumWorkingImagePixels is invalid.")
    .Validate(options => options.MaximumWorkingImageDimension is >= 1_000 and <= 8_192, "MaximumWorkingImageDimension is invalid.")
    .Validate(options => options.OcrTimeoutSeconds is >= 1 and <= 120, "OcrTimeoutSeconds is invalid.")
    .Validate(options => options.PlatformSignalTargetWidth is >= 800 and <= 4_000, "PlatformSignalTargetWidth is invalid.")
    .Validate(options => options.HeaderTargetWidth is >= 800 and <= 4_000, "HeaderTargetWidth is invalid.")
    .Validate(options => options.PlatformSignalTargetWidth <= options.MaximumWorkingImageDimension
        && options.HeaderTargetWidth <= options.MaximumWorkingImageDimension, "OCR target widths exceed the working-copy limit.")
    .Validate(options => options.PlatformMinimumScore is > 0m and <= 1m, "PlatformMinimumScore is invalid.")
    .Validate(options => options.PlatformMinimumMargin is >= 0m and <= 1m, "PlatformMinimumMargin is invalid.")
    .ValidateOnStart();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = string.IsNullOrWhiteSpace(jwtOptions.SigningKey)
                ? null
                : new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            NameClaimType = "name",
            RoleClaimType = "role",
            ClockSkew = TimeSpan.FromSeconds(30)
        };
    });
builder.Services.AddLamieAuthorization();
builder.Services.AddHttpContextAccessor();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IAccessControlCache, AccessControlCache>();
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IUserPermissionResolver, UserPermissionResolver>();
builder.Services.AddScoped<IAccessAuditWriter, AccessAuditWriter>();
builder.Services.AddScoped<IIdentityService, IdentityService>();
builder.Services.AddScoped<IRoleService, RoleService>();
builder.Services.AddScoped<IPermissionManagementService, PermissionManagementService>();
builder.Services.AddScoped<IPermissionCatalogSynchronizer, PermissionCatalogSynchronizer>();
builder.Services.AddScoped<INavigationService, NavigationService>();
builder.Services.AddScoped<IChannelService, ChannelService>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IAdministrativeAddressService, AdministrativeAddressService>();
builder.Services.AddScoped<IAdministrativeDataImporter, AdministrativeDataImporter>();
builder.Services.AddSingleton<IChatOcrProvider, TesseractChatOcrProvider>();
builder.Services.AddSingleton<IChatImagePreprocessor, ChatImagePreprocessor>();
builder.Services.AddSingleton<IConversationHeaderExtractor, ConversationHeaderExtractor>();
builder.Services.AddSingleton<IChatPlatformDetector, ZaloChatPlatformDetector>();
builder.Services.AddSingleton<IChatPlatformDetector, MetaMessengerChatPlatformDetector>();
builder.Services.AddSingleton<IChatPlatformDetector, TikTokChatPlatformDetector>();
builder.Services.AddSingleton<IChatScreenshotAnalyzer, ChatScreenshotAnalyzer>();
builder.Services.AddScoped<ICustomerService, CustomerService>();
builder.Services.AddScoped<IDashboardService, DashboardService>();
builder.Services.AddScoped<IExpenseCategoryService, ExpenseCategoryService>();
builder.Services.AddScoped<IExpenseService, ExpenseService>();
builder.Services.AddScoped<IFinancialReportService, FinancialReportService>();
builder.Services.AddSingleton<IFinancialReportExportService, FinancialReportExportService>();
builder.Services.AddScoped<IReferentialIntegrityService, ReferentialIntegrityService>();
builder.Services.AddHostedService<BootstrapAdminHostedService>();
builder.Services.AddHostedService<PermissionCatalogHostedService>();
builder.Services.AddHostedService<AdministrativeDataImportHostedService>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

// AutoMapper
builder.Services.AddAutoMapper(
    cfg => { },
    typeof(Lamie.Application.AssemblyReference).Assembly);

// MediatR
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly)
);
builder.Services.AddScoped<IValidator<CreateProductCommand>, CreateProductValidator>();
builder.Services.AddScoped<IValidator<UpdateProductCommand>, UpdateProductValidator>();
builder.Services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        x => x.MigrationsAssembly(typeof(AppDbContext).Assembly.GetName().Name)
    );

    options.UseSnakeCaseNamingConvention();
});

// Repository
builder.Services.AddScoped<IProductRepository, ProductRepository>();
builder.Services.AddScoped<ITagRepository, TagRepository>();
builder.Services.AddScoped<IColorRepository, ColorRepository>();
builder.Services.AddScoped<ICategoryRepository, CategoryRepository>();
builder.Services.AddScoped<IProductTypeRepository, ProductTypeRepository>();
builder.Services.AddScoped<ICollectionRepository, CollectionRepository>();
builder.Services.AddScoped<IOccasionRepository, OccasionRepository>();
builder.Services.AddScoped<IStyleRepository, StyleRepository>();
builder.Services.AddScoped<ILanguageRepository, LanguageRepository>();

// Local file storage
var localStorageOptions = builder.Configuration
    .GetSection(LocalStorageOptions.SectionName)
    .Get<LocalStorageOptions>() ?? new LocalStorageOptions();

var localStorageRootPath = LocalFileStorage.ResolveRootPath(
    localStorageOptions.RootPath,
    builder.Environment.ContentRootPath);
var localStoragePublicBasePath = LocalFileStorage.NormalizePublicBasePath(
    localStorageOptions.PublicBasePath);

Directory.CreateDirectory(localStorageRootPath);
builder.Services.AddSingleton<IFileStorage>(
    new LocalFileStorage(localStorageOptions, localStorageRootPath));

builder.Services.AddCors(options =>
{
    options.AddPolicy("AdminClient", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            success = false,
            code = "RATE_LIMITED",
            message = "Too many requests. Please try again later."
        }, cancellationToken);
    };
});
builder.Services.AddHsts(options =>
{
    options.MaxAge = TimeSpan.FromDays(365);
    options.IncludeSubDomains = true;
});

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:ApplyMigrationsOnStartup"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await dbContext.Database.MigrateAsync();
}

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}
//else
//{
//    app.UseHsts();
//}
app.UseSwagger();
app.UseSwaggerUI();

// Middlewares (place early to catch exceptions)
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers.XContentTypeOptions = "nosniff";
        context.Response.Headers.XFrameOptions = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers.Append("Permissions-Policy", "camera=(), microphone=(), geolocation=()");
        return Task.CompletedTask;
    });
    await next();
});

app.UseHttpsRedirection();

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(localStorageRootPath),
    RequestPath = localStoragePublicBasePath,
    OnPrepareResponse = context =>
    {
        context.Context.Response.Headers.CacheControl = "public,max-age=604800";
        context.Context.Response.Headers.XContentTypeOptions = "nosniff";
    }
});

app.UseRouting();
app.UseCors("AdminClient");
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = _ => false
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready")
});
app.MapControllers();

app.Run();
