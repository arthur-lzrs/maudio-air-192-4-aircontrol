namespace AirControl.Audio;

/// <summary>
/// Log de diagnóstico temporário para investigar S7 (research.md) — 0x88890008 em
/// ~75-90% das reaberturas do app com o AIR 192|4. NÃO faz parte do contrato da feature;
/// remover depois que a causa-raiz for confirmada e corrigida.
/// </summary>
public static class DiagLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AirControl",
        "diagnostics.log");

    private static readonly object Lock = new();

    public static void Write(string message)
    {
        try
        {
            lock (Lock)
            {
                var dir = Path.GetDirectoryName(LogPath)!;
                Directory.CreateDirectory(dir);
                File.AppendAllText(LogPath, $"{DateTimeOffset.Now:HH:mm:ss.fff} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Log de diagnóstico nunca pode derrubar o app.
        }
    }
}
