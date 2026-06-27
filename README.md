<h1 align="center">TheoraSharp</h1>

<p align="center">
  A managed C# reader and decoder for Ogg streams containing Theora video and Vorbis audio.
</p>

<p align="center">
  <img alt=".NET 9" src="https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white">
  <img alt="C#" src="https://img.shields.io/badge/C%23-100%25-239120?logo=csharp&logoColor=white">
  <img alt="NuGet planned" src="https://img.shields.io/badge/NuGet-planned-004880?logo=nuget&logoColor=white">
  <img alt="Project status" src="https://img.shields.io/badge/status-pre--release-f59e0b">
</p>

---

**TheoraSharp** is a pure C# implementation for reading Ogg bitstreams and decoding:

- **Theora video** into packed 32-bit pixel buffers
- **Vorbis audio** into interleaved floating-point PCM samples
- Multiplexed Ogg streams through a small pull-based API

The project began as a C# port of the Java Theora decoder used by the Cortado web video player applet.

> [!IMPORTANT]
> The public API is still being refined. A NuGet package is planned, but it will be published only after the current refactoring and performance work is complete.

## Features

- Managed Theora video decoding
- Managed Vorbis audio decoding
- Ogg page and packet parsing
- File, `Stream`, and `byte[]` input
- Frame width, height, and frame-rate metadata
- Audio channel count, sample rate, sample positions, and timing metadata
- Optional recovery from corrupted Ogg pages
- No native decoder dependency in the library project

## Requirements

- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Installation

### Current development version

There is no NuGet package yet. Clone the repository and reference the library project directly:

```bash
git clone https://github.com/DJYar/TheoraSharp.git
```

```bash
dotnet add YourProject.csproj reference path/to/TheoraSharp/TheoraSharp/TheoraSharp.csproj
```

Or add the project reference manually:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/TheoraSharp/TheoraSharp/TheoraSharp.csproj" />
</ItemGroup>
```

### NuGet

A package will be published after the decoder API has been refactored and the main decoding paths have been optimized.

The future installation command will be:

```bash
dotnet add package TheoraSharp
```

This command is shown for reference only. The package is not available yet.

## Quick start

```csharp
using TheoraSharp;

var source = new OggVideoReader("video.ogv");

using var frames = source.StartReading<uint>(
    throwOnCorruptedPacket: false);

while (frames.MoveNext())
{
    uint[] pixels = frames.Current;

    Console.WriteLine(
        $"Decoded {source.Width}x{source.Height} frame at {source.Fps:0.##} FPS");

    // pixels.Length == source.Width * source.Height
    // Each pixel is packed as 0xAARRGGBB.
}
```

The stream headers are decoded while enumeration advances, so dimensions and frame-rate metadata become available during reading.

## Reading from a stream or memory

```csharp
using TheoraSharp;

// From an existing stream
using Stream input = File.OpenRead("video.ogv");
var fromStream = new OggVideoReader(input);

// From an in-memory buffer
byte[] data = await File.ReadAllBytesAsync("video.ogv");
var fromMemory = new OggVideoReader(data);
```

An optional buffer size can be supplied to any constructor:

```csharp
var source = new OggVideoReader("video.ogv", bufferSize: 16 * 1024);
```

## Working with decoded video frames

`StartReading<uint>()` returns frames as one-dimensional `uint[]` buffers.

```csharp
using var frames = source.StartReading<uint>();

while (frames.MoveNext())
{
    uint[] pixels = frames.Current;

    for (var y = 0; y < source.Height; y++)
    {
        for (var x = 0; x < source.Width; x++)
        {
            uint argb = pixels[y * source.Width + x];

            byte a = (byte)(argb >> 24);
            byte r = (byte)(argb >> 16);
            byte g = (byte)(argb >> 8);
            byte b = (byte)argb;

            // Upload to a texture, copy into an image, or process directly.
        }
    }
}
```

The current decoder produces opaque pixels with an alpha value of `255`.

## Working with decoded audio

Vorbis audio is decoded while the Ogg stream is enumerated. New chunks are appended to `AudioChunks`.

```csharp
using var frames = source.StartReading<uint>(
    throwOnCorruptedPacket: false);

var audioCursor = 0;

while (frames.MoveNext())
{
    uint[] pixels = frames.Current;

    while (audioCursor < source.AudioChunks.Count)
    {
        DecodedAudioChunk chunk = source.AudioChunks[audioCursor++];

        float[] samples = chunk.Samples;

        Console.WriteLine(
            $"{chunk.Channels} channels, " +
            $"{chunk.SampleRate} Hz, " +
            $"{chunk.SampleCount} samples per channel, " +
            $"starts at {chunk.StartTimeSeconds:0.000}s");

        // Samples are interleaved:
        // L, R, L, R, ... for stereo audio.
    }
}
```

Each `DecodedAudioChunk` exposes:

| Property | Description |
|---|---|
| `Samples` | Interleaved floating-point PCM samples |
| `Channels` | Number of audio channels |
| `SampleRate` | Sample rate in Hz |
| `SampleCount` | Number of samples per channel |
| `StartSample` | Absolute starting sample index |
| `StartTimeSeconds` | Chunk start time |
| `DurationSeconds` | Chunk duration |
| `GranulePosition` | Ogg granule position |
| `PacketNumber` | Source Ogg packet number |

Reader-level audio metadata is also available through:

```csharp
source.AudioChannels;
source.AudioSampleRate;
source.AudioSampleCount;
source.AudioDecoders;
source.AudioChunks;
```

## Corrupted stream handling

By default, malformed Ogg page data causes decoding to fail:

```csharp
using var frames = source.StartReading<uint>(
    throwOnCorruptedPacket: true);
```

For best-effort playback, corrupted pages can be skipped:

```csharp
using var frames = source.StartReading<uint>(
    throwOnCorruptedPacket: false);
```

This option applies to damaged Ogg page data. Decoder errors inside valid packets may still throw.

## Running the sample decoder

Build the complete solution:

```bash
dotnet build TheoraSharp.sln
```

Run the terminal demo with an `.ogv` file:

```bash
dotnet run --project Decoder/Decoder.csproj -- path/to/video.ogv
```

The demo renders decoded frames in the terminal. On Windows, it also plays decoded Vorbis audio through WinMM.

## Current status

The decoder works, but the repository should currently be treated as a development snapshot rather than a stable package.

Before the first NuGet release, the main goals are:

- Refactor and simplify the public API
- Profile and optimize decoding hot paths
- Reduce unnecessary allocations
- Improve validation and compatibility coverage
- Finalize package metadata and documentation

Breaking API changes may happen before the first packaged release.

## Project structure

```text
TheoraSharp/
├── Decoder/                 # Terminal playback and decoding example
├── TheoraSharp/
│   ├── Ogg/                 # Ogg bitstream parsing
│   ├── Theora/              # Theora decoder implementation
│   ├── Vorbis/              # Vorbis decoder implementation
│   ├── OggVideoReader.cs    # High-level Ogg reader
│   ├── TheoraDec.cs         # Video decoder adapter
│   └── VorbisAudioDec.cs    # Audio decoder adapter
└── TheoraSharp.sln
```

## Contributing

Bug reports, test files, profiling results, and focused pull requests are welcome.

When reporting a decoding problem, include:

- The container and codec details
- The expected and actual result
- The exception or corrupted-frame symptoms
- A minimal reproducible sample, when redistribution is permitted

Please avoid committing copyrighted media that cannot legally be redistributed.

## Acknowledgements

TheoraSharp is based on the Java Theora decoder from **Cortado**, the historical web video player applet.

The project also builds on the specifications and ecosystem around:

- [Ogg](https://xiph.org/ogg/)
- [Theora](https://www.theora.org/)
- [Vorbis](https://xiph.org/vorbis/)