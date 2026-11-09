var builder = WebApplication.CreateBuilder(args);

builder.Services.AddChatWebSocket();

var app = builder.Build();

// Sirve wwwroot/index.html en "/".
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseChatWebSocket("/ws", options =>
{
    // Ping/pong cada 30 s: mantiene viva la conexión a través de proxies y detecta clientes muertos.
    options.KeepAliveInterval = TimeSpan.FromSeconds(30);

    // Solo estos orígenes (páginas) pueden abrir el WebSocket desde un navegador.
    options.AllowedOrigins.Add("http://localhost:5223");
    options.AllowedOrigins.Add("https://localhost:7163");
});

app.Run();
