using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json.Serialization;
using M2Server.Lib;
using M2Server.Lib.Domain;
using M2Server.Lib.Infrastructure;
using M2Server.Lib.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace M2Server.Web;

[SupportedOSPlatform("windows")]
public sealed class ApiServer(
    DataStore store,
    Security security,
    ConsoleExecutorManager executor,
    CertificateService certificates,
    IFileProvider webFiles,
    ILogger<ApiServer>? logger = null)
{
    private readonly ILogger<ApiServer> _logger = logger ?? NullLogger<ApiServer>.Instance;
    private readonly LatestOperationQueue<ConsoleExecutorManager.CommandResult> _profileQueue = new();
    private WebApplication? _app;
    private X509Certificate2? _certificate;

    public bool IsRunning => this._app is not null;

    public async Task StartAsync()
    {
        var port = store.Read(x => x.Settings.HttpsPort);
        var thumbprint = store.Read(x => x.Settings.CertificateThumbprint);
        var certificate = string.IsNullOrWhiteSpace(thumbprint) ? null : certificates.GetServerCertificate(thumbprint);
        this._certificate = certificate;
        var allowLan = store.Read(x => x.Settings.AllowLanAccess || x.Settings.AllowPublicLanAccess);
        var builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddWindowsService(options => options.ServiceName = Constants.WebServiceName);
        builder.Services.ConfigureHttpJsonOptions(options
            => options.SerializerOptions.TypeInfoResolverChain.Insert(0, ApiJsonContext.Default)
        );
        builder.WebHost.UseKestrel(options =>
            {
                if (allowLan)
                {
                    options.ListenAnyIP(
                        port,
                        listen =>
                        {
                            if (certificate is not null)
                            {
                                listen.UseHttps(certificate);
                            }
                        }
                    );
                }
                else
                {
                    options.ListenLocalhost(
                        port,
                        listen =>
                        {
                            if (certificate is not null)
                            {
                                listen.UseHttps(certificate);
                            }
                        }
                    );
                }
            }
        );
        var app = builder.Build();
        app.Use(async (ctx, next) =>
            {
                store.Reload();
                ctx.Response.Headers.CacheControl = "no-store";
                if (ctx.Request.Path.StartsWithSegments("/api") && ctx.Request.Path != "/api/health" &&
                    ctx.Request.Path != "/api/login" && !security.Valid(ctx.Request.Headers.Authorization))
                {
                    ctx.Response.StatusCode = 401;
                    return;
                }

                await next();
            }
        );
        app.MapGet("/api/health", () => Results.Json(new HealthResponse("ok"), ApiJsonContext.Default.HealthResponse));
        app.MapPost(
            "/api/login",
            (LoginRequest request) =>
            {
                var token = security.Login(request.Password);
                return token is null
                    ? Results.Unauthorized()
                    : Results.Json(
                        new LoginResponse(token, DateTimeOffset.UtcNow.AddYears(1)),
                        ApiJsonContext.Default.LoginResponse
                    );
            }
        );
        app.MapGet("/api/presets", () => Results.Json(store.Read(x => x.Presets), ApiJsonContext.Default.ListPreset));
        app.MapPost(
            "/api/presets/{id:guid}/activate",
            async (Guid id, HttpContext context) =>
            {
                var preset = store.Read(x => x.Presets.FirstOrDefault(p => p.Id == id));
                if (preset is null)
                {
                    this._logger.LogWarning(
                        "Profile activation request {RequestId} referenced missing profile {ProfileId}",
                        context.TraceIdentifier,
                        id
                    );
                    return Results.NotFound();
                }

                this._logger.LogInformation(
                    "Profile activation request {RequestId}: starting profile {Profile} ({ProfileId})",
                    context.TraceIdentifier,
                    preset.Name,
                    preset.Id
                );
                var queued = await this._profileQueue.EnqueueAsync(_ => executor.RunAsync(
                        Constants.ApplyProfileArgument,
                        id,
                        CancellationToken.None
                    )
                );
                if (queued.Superseded)
                {
                    this._logger.LogWarning(
                        "Profile activation request {RequestId} for {Profile} ({ProfileId}) was superseded",
                        context.TraceIdentifier,
                        preset.Name,
                        preset.Id
                    );
                    return Results.Json(
                        new ErrorResponse("Profile activation was superseded by a newer request."),
                        ApiJsonContext.Default.ErrorResponse,
                        statusCode: 409
                    );
                }

                this._logger.Log(
                    queued.Value!.ExitCode == 0 ? LogLevel.Information : LogLevel.Warning,
                    "Profile activation request {RequestId}: profile {Profile} ({ProfileId}) finished with exit code {ExitCode}; command error={CommandError}",
                    context.TraceIdentifier,
                    preset.Name,
                    preset.Id,
                    queued.Value.ExitCode,
                    queued.Value.Error
                );
                return ActionResult(queued.Value!, "Profile activated.", "Profile switch failed.");
            }
        );
        app.MapGet(
            "/api/scripts",
            () => Results.Json(
                store.Read(x => x.Scripts.Select(s => new ScriptSummary(s.Id, s.Name)).ToList()),
                ApiJsonContext.Default.ListScriptSummary
            )
        );
        app.MapPost(
            "/api/scripts/{id:guid}/run",
            async (Guid id, HttpContext context) =>
            {
                var script = store.Read(x => x.Scripts.FirstOrDefault(s => s.Id == id));
                if (script is null)
                {
                    return Results.NotFound();
                }

                var result = await executor.RunAsync(Constants.RunScriptArgument, id, context.RequestAborted);
                return ActionResult(result, "Script completed.", "Script failed.");
            }
        );
        app.Map("/api/{**path}", () => Results.NotFound());
        app.UseStaticFiles(new StaticFileOptions { FileProvider = webFiles });
        app.MapFallback(async context =>
            {
                var index = webFiles.GetFileInfo("index.html");
                if (!index.Exists)
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    return;
                }

                context.Response.ContentType = "text/html; charset=utf-8";
                context.Response.ContentLength = index.Length;
                await using var stream = index.CreateReadStream();
                await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
            }
        );

        await app.StartAsync();
        this._app = app;
    }

    public async Task StopAsync()
    {
        if (this._app is null)
        {
            return;
        }

        await this._app.StopAsync().ConfigureAwait(false);
        await this._app.DisposeAsync().ConfigureAwait(false);
        this._app = null;
        this._certificate?.Dispose();
        this._certificate = null;
    }

    public Task WaitForShutdownAsync()
    {
        return this._app?.WaitForShutdownAsync() ?? Task.CompletedTask;
    }

    private static IResult ActionResult(ConsoleExecutorManager.CommandResult result, string success, string failure)
    {
        if (result.ExitCode is null)
        {
            return Results.Json(
                new ErrorResponse(result.Error ?? "The app command could not run."),
                ApiJsonContext.Default.ErrorResponse,
                statusCode: 503
            );
        }

        if (result.ExitCode == 0)
        {
            return Results.Json(new ActionResponse(success), ApiJsonContext.Default.ActionResponse);
        }

        if (result.ExitCode == 2)
        {
            return Results.NotFound();
        }

        return Results.Json(
            new ErrorResponse($"{failure} Exit code: {result.ExitCode}."),
            ApiJsonContext.Default.ErrorResponse,
            statusCode: 400
        );
    }

    internal sealed record LoginRequest(string Password);

    internal sealed record LoginResponse(string Token, DateTimeOffset ExpiresAt);

    internal sealed record HealthResponse(string Status);

    internal sealed record ScriptSummary(Guid Id, string Name);

    internal sealed record ActionResponse(string Message);

    internal sealed record ErrorResponse(string Error);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(ApiServer.LoginRequest))]
[JsonSerializable(typeof(ApiServer.LoginResponse))]
[JsonSerializable(typeof(ApiServer.HealthResponse))]
[JsonSerializable(typeof(ApiServer.ScriptSummary[]))]
[JsonSerializable(typeof(List<ApiServer.ScriptSummary>))]
[JsonSerializable(typeof(ApiServer.ActionResponse))]
[JsonSerializable(typeof(ApiServer.ErrorResponse))]
[JsonSerializable(typeof(List<Preset>))]
[JsonSerializable(typeof(Preset))]
[JsonSerializable(typeof(string))]
internal sealed partial class ApiJsonContext : JsonSerializerContext;