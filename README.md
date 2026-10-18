# RG Valorant Monitor

<img src="icon.svg" alt="Logo de RG Valorant Monitor" width="96">

RG Valorant Monitor es una aplicación de escritorio para Windows que muestra información de la partida actual de VALORANT. Está desarrollada con C#, .NET 8 y WinUI 3.

## Funciones

- Jugadores de la partida y agentes seleccionados.
- Rango actual, RR, rango máximo y estadísticas disponibles.
- Historial de encuentros con la fecha y el agente usado.
- Resolución de nombres después de la partida cuando estuvieron ocultos durante el encuentro.
- Notas y etiquetas locales para jugadores.
- Información de skins.
- Overlays opcionales para OBS: marcador en vivo, tarjeta de fin de partida y reencuentros. Se sirven desde `localhost` y vienen desactivados.

## Requisitos

Para usar la aplicación:

- Windows 10 versión 1809 o posterior.
- [Microsoft Visual C++ Redistributable 2015–2022 x64](https://learn.microsoft.com/es-es/cpp/windows/latest-supported-vc-redist).
- VALORANT abierto.

Para compilar el proyecto:

- .NET 8 SDK.
- Visual Studio con las herramientas de desarrollo de escritorio para .NET y Windows App SDK.
- MSBuild de Visual Studio.

Los scripts incluidos esperan encontrar MSBuild en la ruta definida dentro de cada archivo. Si Visual Studio está instalado en otra edición o ubicación, ajusta la variable `$msbuild`.

## Ejecutar desde el código fuente

Clona el repositorio:

```powershell
git clone https://github.com/x-Raul/RGVMonitor.git
cd RGVMonitor
```

Para compilar y abrir la aplicación:

```powershell
.\run.ps1
```

También puedes abrir `RGVMonitor.sln` en Visual Studio y compilar la plataforma `x64`.

## Generar el ejecutable portable

```powershell
.\publish.ps1
```

El resultado se guarda en `dist\RGVMonitor.exe`. Es una publicación `win-x64`
autocontenida: el primer arranque extrae temporalmente los componentes de WinUI 3
incluidos en el propio ejecutable.

Si después de publicar el Explorador sigue mostrando el icono anterior, es la caché
de iconos de Windows, que lo guarda por ruta. El ejecutable ya lleva el nuevo.

## Versión

La barra de título muestra la versión junto al nombre, con el commit desde el que se
compiló (por ejemplo `v1.0.0 · b722af2`). El ejecutable la incluye también en
Propiedades > Detalles.

La versión se define en `<Version>` dentro de `src-cs\RGVMonitor.App\RGVMonitor.App.csproj`.
Súbela antes de cada publicación.

## Estructura

- `src-cs\RGVMonitor.App`: interfaz WinUI 3.
- `src-cs\RGVMonitor.Core`: modelos, lógica y servicios.
- `src-cs\RGVMonitor.Core.Tests`: pruebas automatizadas.
- `tools`: utilidades del proyecto. `gen_icon.py` genera `icon.ico` y la vista previa del logo.
- `icon.svg`: el logo en formato vectorial.
- `run.ps1`: compilación y ejecución local.
- `publish.ps1`: publicación del ejecutable portable.

## Datos y privacidad

RGVMonitor funciona de forma local y obtiene la información desde la sesión de VALORANT abierta en el equipo. Las notas, etiquetas y el historial se guardan en `%APPDATA%\RGVMonitor`.

No compartas archivos de sesión, credenciales ni tokens generados por el cliente de Riot.

## Aviso

RGVMonitor no está afiliado, respaldado ni patrocinado por Riot Games. VALORANT, Riot Games y sus marcas relacionadas pertenecen a Riot Games, Inc.
