using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;

/// <summary>
/// Lleva el registro de los clientes conectados y permite difundir mensajes.
/// Se registra como singleton: una única instancia compartida por todas las conexiones.
/// </summary>
public class ConnectionManager
{
    // ConcurrentDictionary: muchas conexiones lo modifican a la vez desde hilos distintos.
    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();

    public Guid Add(WebSocket socket)
    {
        var id = Guid.NewGuid();
        _connections[id] = new Connection(socket);
        return id;
    }

    public void Remove(Guid id) => _connections.TryRemove(id, out _);

    public int Count => _connections.Count;

    /// <summary>Envía el texto a todos los clientes conectados.</summary>
    public async Task BroadcastAsync(string text)
    {
        var bytes = Encoding.UTF8.GetBytes(text);

        // En paralelo; un cliente roto no debe impedir que los demás reciban el mensaje.
        var sends = _connections.Values.Select(async c =>
        {
            try { await c.SendAsync(bytes); }
            catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
            {
                // Se limpiará cuando su propio bucle de recepción detecte la desconexión.
            }
        });

        await Task.WhenAll(sends);
    }

    private sealed class Connection(WebSocket socket)
    {
        // WebSocket solo admite UN SendAsync a la vez: serializamos los envíos con un semáforo.
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public async Task SendAsync(byte[] data)
        {
            await _sendLock.WaitAsync();
            try
            {
                if (socket.State == WebSocketState.Open)
                    await socket.SendAsync(data, WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);
            }
            finally
            {
                _sendLock.Release();
            }
        }
    }
}
