using System;

namespace CommunityWadCompiler.App.ViewModels;

/// <summary>Categorías de visualización del encabezado de una línea de la consola.</summary>
public enum LogEntryKind
{
    Plain,
    Info,
    Warning,
    Error,
    Success,
}

/// <summary>Una línea del registro de la consola, separada en su encabezado (tag, que se
/// colorea y pone en negrita) y el resto del texto (estilo normal).</summary>
public sealed record LogEntry(string Header, string Rest, LogEntryKind Kind)
{
    public static readonly LogEntry Separator = new("\u200B", "", LogEntryKind.Plain);

    private static readonly (string Prefix, LogEntryKind Kind)[] Prefixes =
    {
        ("[ERROR]", LogEntryKind.Error),
        ("[WARNING]", LogEntryKind.Warning),
        ("[AVISO]", LogEntryKind.Warning),
        ("[INFO]", LogEntryKind.Info),
        ("COMPILADO OK", LogEntryKind.Success),
        ("BUILD OK", LogEntryKind.Success),
        ("COMPILACIÓN FALLIDA", LogEntryKind.Error),
        ("BUILD FAILED", LogEntryKind.Error),
    };

    public static LogEntry Create(string line)
    {
        var trimmed = line.TrimStart();
        foreach (var (prefix, kind) in Prefixes)
        {
            if (!trimmed.StartsWith(prefix, StringComparison.Ordinal))
                continue;
            return new LogEntry(prefix, trimmed[prefix.Length..], kind);
        }

        if (trimmed.StartsWith("──", StringComparison.Ordinal))
            return new LogEntry(trimmed, "", LogEntryKind.Info);

        return new LogEntry("", line, LogEntryKind.Plain);
    }
}