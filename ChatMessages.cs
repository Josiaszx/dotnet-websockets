/// <summary>Lo que envía el cliente: {"text":"hola"}.</summary>
public record ClientMessage(string? Text);

/// <summary>
/// Lo que envía el servidor: {"type":"chat","user":"Ana","text":"hola","time":"..."}.
/// Type = "chat" para mensajes de usuarios, "system" para avisos (entradas/salidas).
/// </summary>
public record ServerMessage(string Type, string User, string Text, DateTimeOffset Time);
