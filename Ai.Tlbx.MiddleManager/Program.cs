using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Ai.Tlbx.MiddleManager.Models;
using Ai.Tlbx.MiddleManager.Services;
using Ai.Tlbx.MiddleManager.Settings;
using Ai.Tlbx.MiddleManager.Shells;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

namespace Ai.Tlbx.MiddleManager;

public class Program
{
    private const int DefaultPort = 2000;
    private const string DefaultBindAddress = "0.0.0.0";

    public static void Main(string[] args)
    {
        var (port, bindAddress) = ParseCommandLineArgs(args);
        var builder = CreateBuilder(args);
        var app = builder.Build();
        var version = GetVersion();

        ConfigureStaticFiles(app);
        var (sessionManager, muxManager) = ConfigureServices(app);
        MapApiEndpoints(app, sessionManager, version);
        MapWebSocketMiddleware(app, sessionManager, muxManager);

        PrintWelcomeBanner(port, bindAddress, app.Services.GetRequiredService<SettingsService>(), version);
        RunWithPortErrorHandling(app, port, bindAddress);
    }

    private static (int port, string bindAddress) ParseCommandLineArgs(string[] args)
    {
        var port = DefaultPort;
        var bindAddress = DefaultBindAddress;

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
            {
                port = p;
                i++;
            }
            else if (args[i] == "--bind" && i + 1 < args.Length)
            {
                bindAddress = args[i + 1];
                i++;
            }
        }

        return (port, bindAddress);
    }

    private static WebApplicationBuilder CreateBuilder(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonContext.Default);
        });

        builder.Services.AddSingleton<ShellRegistry>();
        builder.Services.AddSingleton<SettingsService>();
        builder.Services.AddSingleton<SessionManager>();

        return builder;
    }

    private static string GetVersion()
    {
        return Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "1.0.0";
    }

    private static void ConfigureStaticFiles(WebApplication app)
    {
#if DEBUG
        var wwwrootPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "wwwroot");
        IFileProvider fileProvider = Directory.Exists(wwwrootPath)
            ? new PhysicalFileProvider(Path.GetFullPath(wwwrootPath))
            : new EmbeddedWebRootFileProvider(Assembly.GetExecutingAssembly(), "Ai.Tlbx.MiddleManager.Aot");
#else
        IFileProvider fileProvider = new EmbeddedWebRootFileProvider(
            Assembly.GetExecutingAssembly(),
            "Ai.Tlbx.MiddleManager.Aot");
#endif

        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileProvider });

        var contentTypeProvider = new FileExtensionContentTypeProvider();
        contentTypeProvider.Mappings[".ico"] = "image/x-icon";
        contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json";

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = fileProvider,
            ContentTypeProvider = contentTypeProvider,
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.Remove("ETag");
                ctx.Context.Response.Headers.CacheControl = "no-store, no-cache, must-revalidate";
                ctx.Context.Response.Headers.Pragma = "no-cache";
            }
        });

        app.UseWebSockets();
    }

    private static (SessionManager, MuxConnectionManager) ConfigureServices(WebApplication app)
    {
        var sessionManager = app.Services.GetRequiredService<SessionManager>();
        var muxManager = new MuxConnectionManager(sessionManager);
        sessionManager.SetMuxManager(muxManager);
        return (sessionManager, muxManager);
    }

    private static void MapApiEndpoints(WebApplication app, SessionManager sessionManager, string version)
    {
        app.MapGet("/api/version", () => Results.Text(version));

        app.MapGet("/api/networks", () =>
        {
            var interfaces = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                             && ni.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses
                    .Where(addr => addr.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    .Select(addr => new NetworkInterfaceDto
                    {
                        Name = ni.Name,
                        Ip = addr.Address.ToString()
                    }))
                .ToList();
            return Results.Json(interfaces, AppJsonContext.Default.ListNetworkInterfaceDto);
        });

        app.MapGet("/api/shells", () =>
        {
            var shells = sessionManager.ShellRegistry.GetAllShells().Select(s => new ShellInfoDto
            {
                Type = s.ShellType.ToString(),
                DisplayName = s.DisplayName,
                IsAvailable = s.IsAvailable(),
                SupportsOsc7 = s.SupportsOsc7
            }).ToList();
            return Results.Json(shells, AppJsonContext.Default.ListShellInfoDto);
        });

        app.MapGet("/api/settings", () =>
        {
            var settings = sessionManager.SettingsService.Load();
            return Results.Json(settings, AppJsonContext.Default.MiddleManagerSettings);
        });

        app.MapPut("/api/settings", (MiddleManagerSettings settings) =>
        {
            sessionManager.SettingsService.Save(settings);
            return Results.Ok();
        });

        app.MapGet("/api/sessions", () =>
            Results.Json(sessionManager.GetSessionList(), AppJsonContext.Default.SessionListDto));

        app.MapPost("/api/sessions", (CreateSessionRequest? request) =>
        {
            var cols = request?.Cols ?? 120;
            var rows = request?.Rows ?? 30;

            ShellType? shellType = null;
            if (!string.IsNullOrEmpty(request?.Shell) && Enum.TryParse<ShellType>(request.Shell, true, out var parsed))
            {
                shellType = parsed;
            }

            var session = sessionManager.CreateSession(cols, rows, shellType);
            var info = new SessionInfoDto
            {
                Id = session.Id,
                Pid = session.Pid,
                CreatedAt = session.CreatedAt,
                IsRunning = session.IsRunning,
                ExitCode = session.ExitCode,
                CurrentWorkingDirectory = session.CurrentWorkingDirectory,
                Cols = session.Cols,
                Rows = session.Rows,
                ShellType = session.ShellType.ToString(),
                Name = session.Name,
                LastActiveViewerId = session.LastActiveViewerId
            };
            return Results.Json(info, AppJsonContext.Default.SessionInfoDto);
        });

        app.MapDelete("/api/sessions/{id}", (string id) =>
        {
            sessionManager.CloseSession(id);
            return Results.Ok();
        });

        app.MapPost("/api/sessions/{id}/resize", (string id, ResizeRequest request) =>
        {
            var session = sessionManager.GetSession(id);
            if (session is null)
            {
                return Results.NotFound();
            }
            var accepted = session.Resize(request.Cols, request.Rows, request.ViewerId);
            return Results.Json(new ResizeResponse
            {
                Accepted = accepted,
                Cols = session.Cols,
                Rows = session.Rows
            }, AppJsonContext.Default.ResizeResponse);
        });

        app.MapGet("/api/sessions/{id}/buffer", (string id) =>
        {
            var session = sessionManager.GetSession(id);
            if (session is null)
            {
                return Results.NotFound();
            }
            return Results.Text(session.GetBuffer());
        });

        app.MapPut("/api/sessions/{id}/name", (string id, RenameSessionRequest request) =>
        {
            if (!sessionManager.RenameSession(id, request.Name))
            {
                return Results.NotFound();
            }
            return Results.Ok();
        });
    }

    private static void MapWebSocketMiddleware(WebApplication app, SessionManager sessionManager, MuxConnectionManager muxManager)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/ws"))
            {
                await next(context);
                return;
            }

            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            var path = context.Request.Path.Value ?? "";

            if (path == "/ws/state")
            {
                await HandleStateWebSocketAsync(context, sessionManager);
                return;
            }

            if (path == "/ws/mux")
            {
                await HandleMuxWebSocketAsync(context, sessionManager, muxManager);
                return;
            }

            context.Response.StatusCode = 404;
        });
    }

    private static void RunWithPortErrorHandling(WebApplication app, int port, string bindAddress)
    {
        try
        {
            app.Run($"http://{bindAddress}:{port}");
        }
        catch (IOException ex) when (ex.InnerException is System.Net.Sockets.SocketException socketEx &&
            socketEx.SocketErrorCode == System.Net.Sockets.SocketError.AddressAlreadyInUse)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"  Error: Port {port} is already in use by another process.");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine($"  Try one of the following:");
            Console.WriteLine($"    - Close the application using port {port}");
            Console.WriteLine($"    - Use a different port: mm --port 2001");
            Console.WriteLine();
            Environment.Exit(1);
        }
    }

    private static async Task HandleMuxWebSocketAsync(HttpContext context, SessionManager sessionManager, MuxConnectionManager muxManager)
    {
        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var clientId = Guid.NewGuid().ToString("N");
        var client = muxManager.AddClient(clientId, ws);

        try
        {
            var initFrame = new byte[MuxProtocol.HeaderSize + 32];
            initFrame[0] = 0xFF;
            Encoding.ASCII.GetBytes(clientId.AsSpan(0, 8), initFrame.AsSpan(1, 8));
            Encoding.UTF8.GetBytes(clientId, initFrame.AsSpan(MuxProtocol.HeaderSize));
            await client.SendAsync(initFrame);

            foreach (var session in sessionManager.Sessions)
            {
                var buffer = session.GetBuffer();
                if (!string.IsNullOrEmpty(buffer))
                {
                    var bufferBytes = Encoding.UTF8.GetBytes(buffer);
                    var frame = MuxProtocol.CreateOutputFrame(session.Id, bufferBytes);
                    await client.SendAsync(frame);
                }
            }

            var receiveBuffer = new byte[MuxProtocol.MaxFrameSize];

            while (ws.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result;
                try
                {
                    result = await ws.ReceiveAsync(receiveBuffer, CancellationToken.None);
                }
                catch (WebSocketException)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary && result.Count >= MuxProtocol.HeaderSize)
                {
                    if (MuxProtocol.TryParseFrame(receiveBuffer.AsSpan(0, result.Count), out var type, out var sessionId, out var payload))
                    {
                        switch (type)
                        {
                            case MuxProtocol.TypeTerminalInput:
                                await muxManager.HandleInputAsync(sessionId, payload.ToArray(), clientId);
                                break;

                            case MuxProtocol.TypeResize:
                                var (cols, rows) = MuxProtocol.ParseResizePayload(payload);
                                muxManager.HandleResize(sessionId, cols, rows, clientId);
                                break;
                        }
                    }
                }
            }
        }
        finally
        {
            muxManager.RemoveClient(clientId);

            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    private static async Task HandleStateWebSocketAsync(HttpContext context, SessionManager sessionManager)
    {
        using var ws = await context.WebSockets.AcceptWebSocketAsync();
        var sendLock = new SemaphoreSlim(1, 1);

        async void OnStateChange()
        {
            if (ws.State != WebSocketState.Open)
            {
                return;
            }

            await sendLock.WaitAsync();
            try
            {
                if (ws.State != WebSocketState.Open)
                {
                    return;
                }

                var json = JsonSerializer.Serialize(sessionManager.GetSessionList(), AppJsonContext.Default.SessionListDto);
                var bytes = Encoding.UTF8.GetBytes(json);
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
            }
            catch
            {
            }
            finally
            {
                sendLock.Release();
            }
        }

        var listenerId = sessionManager.AddStateListener(OnStateChange);

        try
        {
            OnStateChange();

            var buffer = new byte[1024];
            while (ws.State == WebSocketState.Open)
            {
                try
                {
                    var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }
                }
                catch
                {
                    break;
                }
            }
        }
        finally
        {
            sessionManager.RemoveStateListener(listenerId);
            sendLock.Dispose();

            if (ws.State == WebSocketState.Open)
            {
                try
                {
                    await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
                }
                catch
                {
                }
            }
        }
    }

    private static void PrintWelcomeBanner(int port, string bindAddress, SettingsService settingsService, string version)
    {
        var settings = settingsService.Load();

        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine();
        Console.WriteLine(@"  __  __ _     _     _ _      __  __");
        Console.WriteLine(@" |  \/  (_) __| | __| | | ___|  \/  | __ _ _ __   __ _  __ _  ___ _ __");
        Console.WriteLine(@" | |\/| | |/ _` |/ _` | |/ _ \ |\/| |/ _` | '_ \ / _` |/ _` |/ _ \ '__|");
        Console.WriteLine(@" | |  | | | (_| | (_| | |  __/ |  | | (_| | | | | (_| | (_| |  __/ |");
        Console.WriteLine(@" |_|  |_|_|\__,_|\__,_|_|\___|_|  |_|\__,_|_| |_|\__,_|\__, |\___|_|");
        Console.ForegroundColor = ConsoleColor.Green;
        Console.Write(@"   by Johannes Schmidt - https://github.com/AiTlbx");
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine(@"     |___/");
        
        
        Console.ResetColor();
        Console.WriteLine();

        var platform = OperatingSystem.IsWindows() ? "Windows"
            : OperatingSystem.IsMacOS() ? "macOS"
            : OperatingSystem.IsLinux() ? "Linux"
            : "Unknown";

        Console.WriteLine($"  Version:  {version}");
        Console.WriteLine($"  Platform: {platform}");
        Console.WriteLine($"  Shell:    {settings.DefaultShell}");
        Console.WriteLine();
        Console.WriteLine($"  Listening on http://{bindAddress}:{port}");
        Console.WriteLine();

        switch (settingsService.LoadStatus)
        {
            case SettingsLoadStatus.LoadedFromFile:
                Console.WriteLine($"  Settings: Loaded from {settingsService.SettingsPath}");
                break;
            case SettingsLoadStatus.ErrorFallbackToDefault:
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"  Settings: Error loading {settingsService.SettingsPath}");
                Console.WriteLine($"            {settingsService.LoadError}");
                Console.WriteLine($"            Using default settings");
                Console.ResetColor();
                break;
            default:
                Console.WriteLine($"  Settings: Using defaults (no settings file)");
                break;
        }

        Console.WriteLine();
    }
}
