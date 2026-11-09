using System.Net.WebSockets;
using System.Text.Json;

/// <summary>
/// Lógica del chat: registra al cliente, recibe sus mensajes y los difunde.
/// No sabe nada de HTTP ni de rutas: solo trabaja con un WebSocket ya aceptado.
/// </summary>
public class ChatHandler(ConnectionManager manager, ILogger<ChatHandler> logger)
{
    private const int MaxMessageSize = 64 * 1024; // 64 KB

    // Web defaults = camelCase en los nombres de propiedad (userName -> "userName").
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private Task Broadcast(string type, string user, string text) =>
        manager.BroadcastAsync(JsonSerializer.Serialize(
            new ServerMessage(type, user, text, DateTimeOffset.UtcNow), Json));

    public async Task HandleAsync(WebSocket socket, string? requestedName, CancellationToken ct)
    {
        var id = manager.Add(socket);

        // El nombre viene del cliente: hay que validarlo, nunca fiarse.
        var name = string.IsNullOrWhiteSpace(requestedName)
            ? $"Usuario-{id.ToString()[..4]}"
            : requestedName.Trim()[..Math.Min(requestedName.Trim().Length, 20)];

        logger.LogInformation("{Name} conectado. Total: {Count}", name, manager.Count);
        await Broadcast("system", "Sistema", $"{name} se unió al chat");

        try
        {
            await ReceiveLoop(socket, name, ct);
        }
        finally
        {
            manager.Remove(id);
            logger.LogInformation("{Name} desconectado. Total: {Count}", name, manager.Count);
            await Broadcast("system", "Sistema", $"{name} salió del chat");
        }
    }

    private async Task ReceiveLoop(WebSocket socket, string name, CancellationToken ct)
    {
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessage(socket, ct);

                if (message is null) // el cliente pidió cerrar
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Cierre confirmado", ct);
                    break;
                }

                var (type, data) = message.Value;

                if (data.Length > MaxMessageSize)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "Mensaje demasiado grande", ct);
                    break;
                }

                if (type != WebSocketMessageType.Text)
                    continue; // en el chat solo difundimos texto

                ClientMessage? incoming;
                try
                {
                    incoming = JsonSerializer.Deserialize<ClientMessage>(data, Json);
                }
                catch (JsonException)
                {
                    continue; // JSON inválido: lo ignoramos en vez de tumbar la conexión
                }

                if (string.IsNullOrWhiteSpace(incoming?.Text))
                    continue;

                await Broadcast("chat", name, incoming.Text);
            }
        }
        catch (WebSocketException ex)
        {
            logger.LogWarning("{Name}: conexión interrumpida ({Code})", name, ex.WebSocketErrorCode);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("{Name}: operación cancelada", name);
        }
        finally
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent)
                socket.Abort();
        }
    }

    // Lee frames hasta EndOfMessage. Devuelve null si el cliente envió Close.
    private static async Task<(WebSocketMessageType Type, byte[] Data)?> ReceiveMessage(
        WebSocket socket, CancellationToken ct)
    {
        var buffer = new byte[4 * 1024];
        using var ms = new MemoryStream();
        ValueWebSocketReceiveResult result;
        WebSocketMessageType type;

        do
        {
            result = await socket.ReceiveAsync(buffer.AsMemory(), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;

            type = result.MessageType;
            ms.Write(buffer, 0, result.Count);

            if (ms.Length > MaxMessageSize)
                break;
        } while (!result.EndOfMessage);

        return (type, ms.ToArray());
    }
}
