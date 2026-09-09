using System.Collections.Generic;
using System.Globalization;

namespace CommunityWadCompiler.Core.Localization;

/// <summary>
/// Resolves the human-readable messages the merge pipeline surfaces in the console log
/// (progress lines, warnings, errors). The Core layer knows no resource system, so every
/// message is a key with a Spanish default; the app installs a resolver (see
/// <see cref="Resolve"/>) that translates each key through its own resource dictionaries.
/// When no resolver is installed (e.g. unit tests) or the key cannot be translated, the
/// Spanish default is used, which keeps test assertions on message text stable.
/// </summary>
public static class CoreMessages
{
    /// <summary>The translation callback the UI installs: (key, fallback) → localized text.</summary>
    public static Func<string, string, string> Resolve { get; set; } = (_, fallback) => fallback;

    /// <summary>Every message key. Hosting apps snapshot these (translated) when the UI
    /// language changes, so lookups can be served off the UI thread.</summary>
    public static IReadOnlyCollection<string> Keys => Defaults.Keys;

    /// <summary>Resolves and formats a console-facing message for the active language.</summary>
    public static string Get(string key, params object[] args)
    {
        string fallback = Defaults.TryGetValue(key, out string? text) ? text : key;
        string resolved = Resolve(key, fallback);
        if (resolved == key)
            resolved = fallback;
        return args.Length == 0 ? resolved : string.Format(CultureInfo.InvariantCulture, resolved, args);
    }

    private static readonly Dictionary<string, string> Defaults = new()
    {
        ["Merge.OpenBaseWad"] = "Abriendo WAD base...",
        ["Merge.NoBaseWad"] = "  (sin WAD base)",
        ["Merge.OpenInputs"] = "Abriendo WADs de entrada...",
        ["Merge.OpenResources"] = "Abriendo WADs de recursos...",
        ["Merge.NoResourceWad"] = "  (sin WAD de recursos)",
        ["Merge.ScanMusic"] = "Escaneando lumps de música (MUS/MIDI)...",
        ["Merge.MusicFound"] = "  - {0} lump(s) de música detectados.",
        ["Merge.DetectMaps"] = "Detectando mapas...",
        ["Merge.AnalyzeUsage"] = "Analizando texturas/flats usados por los mapas...",
        ["Merge.UsageFound"] = "  - {0} texturas de pared y {1} flats detectados.",
        ["Merge.MergeTextures"] = "Fusionando texturas (PNAMES/TEXTURE1/TEXTURE2)...",
        ["Merge.ExcludedTextures"] = "  {0} textura(s) excluidas por no usarse.",
        ["Merge.Assemble"] = "Ensamblando WAD de salida...",
        ["Merge.CopyingMap"] = "  Copiando {0} -> {1}",
        ["Merge.MapNoSlot"] = "Mapa '{0}' de '{1}' no tiene slot asignado; se omite.",
        ["Merge.DuplicateSlot"] = "Dos mapas se asignaron al slot '{0}'. Revisá las asignaciones.",
        ["Merge.MapInfoGenerated"] = "MAPINFO generado para {0} mapa(s).",
        ["Merge.MapInfoSkipped"] = "Se omitieron lumps MAPINFO/ZMAPINFO existentes en los WADs; se usa el MAPINFO generado con los nombres.",
        ["Merge.ZdoomTexturesDropped"] = "Se detectaron lumps ZDoom 'TEXTURES'; su fusión aún no está implementada y se omitieron.",
        ["Merge.MusicRenamed"] = "Música '{0}' (de '{1}') renombrada a '{2}'.",
        ["Merge.MusicNotInWads"] = "Música '{0}' no se encontró en los WADs cargados; el MAPINFO la referencia igualmente (debe existir en el IWAD o idéntica en el motor).",
        ["Merge.IntermissionMusicAdded"] = "Música de intermisión '{0}' añadida desde '{1}'.",
        ["Merge.IntermissionMusicMissing"] = "Música de intermisión '{0}' no se encontró en los WADs cargados; el MAPINFO la referencia igualmente (debe existir en el IWAD o idéntica en el motor).",
        ["Merge.ExternalMusicAdded"] = "Música externa '{0}' copiada desde '{1}'.",
        ["Merge.ExternalMusicMissing"] = "Música externa '{0}': archivo no encontrado '{1}'.",
        ["Merge.ExternalMusicUnsupported"] = "Música externa '{0}': extensión '{1}' no soportada.",
        ["Merge.SkyMissing"] = "Textura de sky '{0}' no se encontró en los WADs de recursos; el MAPINFO la referencia igualmente.",
        ["Merge.TextureWithoutPnames"] = "{0}: contiene TEXTUREx sin PNAMES; se intenta resolver por nombre.",
        ["Merge.TextureDuplicate"] = "Textura '{0}' duplicada; se mantiene la primera definición.",
        ["Merge.PatchIndexOutOfRange"] = "Textura '{0}': índice de patch {1} fuera de rango.",
        ["Merge.TextureLumpTooShort"] = "Lump vacío o demasiado corto para ser TEXTUREx.",
        ["Merge.InvalidTextureCount"] = "Número de texturas inválido ({0}).",
        ["Merge.MalformedTexture"] = "Textura #{0} (offset {1}) mal formada; se omite.",
        ["Merge.WadReadFailed"] = "No se pudo leer '{0}': {1}",
        ["Merge.WadTooSmall"] = "El archivo es demasiado pequeño para ser un WAD.",
        ["Merge.UnknownHeader"] = "Cabecera de WAD desconocida '{0}' (se esperaba IWAD o PWAD).",
        ["Merge.InvalidLumpDirectory"] = "Directorio de lumps inválido o fuera de rango.",
        ["Merge.LumpOutOfBounds"] = "El lump '{0}' apunta fuera del archivo.",

        // MAPINFO header/notes comments written into the generated lump.
        ["MapInfo.HeaderLine"] = "// MAPINFO generado automáticamente por Community Wad Compiler",
        ["MapInfo.Project"] = "// Proyecto: {0}",
        ["MapInfo.Version"] = "// Versión: {0}",
        ["MapInfo.Compiled"] = "// Compilado: {0}",
        ["MapInfo.Author"] = "// Autor: {0}",
        ["MapInfo.Status"] = "// Estado: {0}",
        ["MapInfo.LastModified"] = "// Última modificación: {0}",
    };
}