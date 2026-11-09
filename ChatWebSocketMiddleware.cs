/// <summary>
/// Middleware: intercepta las peticiones a la ruta indicada, hace el handshake
/// y delega en ChatHandler. El resto de peticiones pasan al siguiente middleware.
/// </summary>
public class ChatWebSocketMiddleware(RequestDelegate next, string path)
{
    // Los servicios se piden en InvokeAsync (no en el constructor) para poder usar
    // servicios con cualquier ciclo de vida: el constructor se ejecuta una sola vez.
    public async Task InvokeAsync(HttpContext context, ChatHandler handler)
    {
        if (!context.Request.Path.Equals(path, StringComparison.OrdinalIgnoreCase))
        {
            await next(context); // no es nuestra ruta
            return;
        }

        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsync("Esta ruta solo acepta conexiones WebSocket.");
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        // Los navegadores no permiten cabeceras personalizadas en WebSocket;
        // por eso los datos extra (nombre, token) suelen ir en la query string.
        await handler.HandleAsync(socket, context.Request.Query["name"], context.RequestAborted);
    }
}

public static class ChatWebSocketExtensions
{
    public static IServiceCollection AddChatWebSocket(this IServiceCollection services)
    {
        services.AddSingleton<ConnectionManager>();
        services.AddSingleton<ChatHandler>();
        return services;
    }

    public static IApplicationBuilder UseChatWebSocket(
        this IApplicationBuilder app, string path = "/ws", Action<WebSocketOptions>? configure = null)
    {
        var options = new WebSocketOptions();
        configure?.Invoke(options);
        app.UseWebSockets(options);
        return app.UseMiddleware<ChatWebSocketMiddleware>(path);
    }
}
