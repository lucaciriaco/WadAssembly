namespace CommunityWadCompiler.Core.Merge;

/// <summary>
/// Outcome of a <see cref="WadMerger"/> run: stats plus a human-readable report.
/// </summary>
public sealed class MergeResult
{
    public bool Success { get; set; }

    public string? OutputPath { get; set; }

    public List<string> Errors { get; } = new();

    public List<string> Warnings { get; } = new();

    public List<string> Info { get; } = new();

    public int MapsAdded { get; set; }

    public int LumpsCopied { get; set; }

    public int DuplicatesSkipped { get; set; }

    public int PatchesMerged { get; set; }

    public int TexturesMerged { get; set; }

    public int TexturesDuplicated { get; set; }

    public int TexturesExcluded { get; set; }

    public int FlatsCopied { get; set; }

    public long OutputBytes { get; set; }

    public TimeSpan Elapsed { get; set; }

    public string ToReport()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Success ? $"COMPILADO OK ({Elapsed.TotalSeconds:F1}s)" : "COMPILACIÓN FALLIDA");
        if (OutputPath is not null)
            sb.AppendLine($"  Salida: {OutputPath} ({OutputBytes:N0} bytes)");
        sb.AppendLine($"  Mapas: {MapsAdded}");
        sb.AppendLine($"  Lumps copiados: {LumpsCopied} | Duplicados omitidos: {DuplicatesSkipped}");
        sb.AppendLine($"  Parches: {PatchesMerged} | Texturas: {TexturesMerged} | Texturas duplicadas omitidas: {TexturesDuplicated}");
        if (TexturesExcluded > 0)
            sb.AppendLine($"  Texturas sin uso excluidas: {TexturesExcluded}");
        if (FlatsCopied > 0)
            sb.AppendLine($"  Flats copiados del WAD de recursos: {FlatsCopied}");
        foreach (string e in Errors)
            sb.AppendLine($"  [ERROR] {e}");
        foreach (string w in Warnings)
            sb.AppendLine($"  [AVISO] {w}");
        foreach (string i in Info)
            sb.AppendLine($"  [INFO] {i}");
        return sb.ToString();
    }
}