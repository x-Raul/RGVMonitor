namespace RGVMonitor.Core;

/// Escritura de los archivos de datos sin riesgo de perderlos.
public static class SafeFile
{
    /// Escribe en un temporal y lo mueve encima del original. Si la app se cierra
    /// o falla a mitad, el archivo anterior sigue entero: nunca queda medio escrito.
    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tmp = path + ".tmp";
        using (var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
        using (var writer = new StreamWriter(stream))
        {
            writer.Write(content);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }
        File.Move(tmp, path, overwrite: true);
    }

    /// Aparta un archivo que no se puede interpretar (p. ej. "stats.corrupt-20261017-221500.json")
    /// para que guardar de nuevo no lo pise. Así se puede recuperar a mano.
    public static string? SetAsideUnreadable(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
            var name = Path.GetFileNameWithoutExtension(path);
            var target = Path.Combine(dir, $"{name}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(path)}");
            File.Move(path, target, overwrite: false);
            return target;
        }
        catch { return null; }
    }
}
