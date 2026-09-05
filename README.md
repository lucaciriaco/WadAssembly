# CommunityWadCompiler

Herramienta en C# (.NET 8) con interfaz gráfica (Avalonia) para compilar proyectos de
comunidad de Doom: toma varios `.wad` aportados por distintos autores y genera **un único
PWAD** con los mapas ordenados en sus slots finales, recursos fusionados y texturas
deduplicadas.

## Estructura

```
src/
  CommunityWadCompiler.Core/   Librería: formato WAD, mapas, fusión de texturas, pipeline
  CommunityWadCompiler.App/    UI Avaonia (ventana principal, view models)
tests/
  CommunityWadCompiler.Tests/  Pruebas de roundtrip, detección de mapas y fusión
```

## Lo que ya funciona

- **Lector/escritor WAD**: parsea `IWAD`/`PWAD` (cabecera, directorio, lumps) con carga completa en memoria y escritura alineada a 4 bytes.
- **Detección de mapas**: `E1M1..E4M9`, `MAP01..`, y formatos UDMF (TEXTMAP/ENDMAP).
- **Fusión de texturas**: PNAMES + TEXTURE1/TEXTURE2 en formato Doom clásico, con parches deduplicados por nombre e índices re-mapeados; texturas deduplicadas (primera definición gana, igual que el motor).
- **Fusión de recursos**: lumps genéricos deduplicados por nombre (primera aparición gana); opción de no recopiar recursos del WAD base/IWAD.
- **Asignación de slots**: automática (MAP01...) o manual, editable desde la grilla.
- **Guardar/cargar proyecto** en JSON.

## Limitaciones actuales (TODOs)

- Fusión del lump ZDoom **`TEXTURES`** (texto) aún no implementada; se omite con aviso.
- Lumps de lista tipo boom (`ANIMATED`, `SWITCHES`, `ANIMDEFS`, `SNDINFO`, `UMAPINFO`,
  `MUSINFO`, ...) usan "primera fuente gana" en vez de concatenación/fusión real.
- Detección de formato Strife en TEXTUREx es básica.

## Compilar

Requerido: .NET 8 SDK.

```
dotnet build CommunityWadCompiler.sln
dotnet run --project src/CommunityWadCompiler.App
```

## Uso rápido

1. **Agregar WADs...**: cargá los `.wad` de los autores.
2. Reordená con Subir/Bajar; editá el **Slot final** de cada mapa en la grilla (los dejan
   en blanco se asignan automáticamente).
3. (Opcional) elegí un **WAD base** (DOOM2.WAD) para sembrar TEXTURE1/PNAMES.
4. Definí la **salida** y pulsá **Compilar**.

El resultado se abre con cualquier port moderno: `yoursourceport -iwad doom2.wad -file megawad.wad`.