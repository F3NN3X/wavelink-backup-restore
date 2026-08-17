using System;
using System.Buffers;
using System.IO;
using System.Text.Json;
using WaveLinkBackup.Core.Abstractions;

namespace WaveLinkBackup.App.Hosting;

/// <summary>
/// shell.json, beside settings.json in %LOCALAPPDATA%\WaveLinkBackup.
///
/// Hand-written with Utf8JsonWriter and JsonDocument, matching SettingsSerializer and
/// ManifestSerializer. SourceGuardTests only polices Core, but a second serialization style in
/// the same product for the same job is its own kind of debt.
///
/// Read is TOLERANT per field and Save NEVER THROWS. Losing a window position is not worth an
/// exception on a shutdown path, and it is not worth refusing to start either.
/// </summary>
public sealed class ShellStateRepository(IFileSystem fileSystem, string directoryPath)
{
    public const string FileName = "shell.json";

    public const int CurrentSchemaVersion = 1;

    public string FilePath { get; } = Path.Combine(directoryPath, FileName);

    public ShellState Read()
    {
        if (!fileSystem.FileExists(FilePath)) return ShellState.Default;

        byte[] bytes;
        try { bytes = fileSystem.ReadSharedBytes(FilePath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return ShellState.Default; }

        JsonDocument document;
        try { document = JsonDocument.Parse(bytes); }
        catch (Exception ex) when (ex is JsonException or ArgumentException) { return ShellState.Default; }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return ShellState.Default;

            return new ShellState(
                Left: Number(root, "left"),
                Top: Number(root, "top"),
                Width: Number(root, "width"),
                Height: Number(root, "height"),
                IsMaximized: Bool(root, "isMaximized") ?? ShellState.Default.IsMaximized,
                ClosingHidesToTray: Bool(root, "closingHidesToTray") ?? ShellState.Default.ClosingHidesToTray);
        }
    }

    public void Save(ShellState state)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("schemaVersion", CurrentSchemaVersion);

            WriteNumber(writer, "left", state.Left);
            WriteNumber(writer, "top", state.Top);
            WriteNumber(writer, "width", state.Width);
            WriteNumber(writer, "height", state.Height);

            writer.WriteBoolean("isMaximized", state.IsMaximized);
            writer.WriteBoolean("closingHidesToTray", state.ClosingHidesToTray);

            writer.WriteEndObject();
        }

        // Not atomic, deliberately. SettingsRepository writes through a temp file because
        // losing settings.json costs the user their configuration; losing a window position
        // costs one restore to 1180x760, which is where it starts anyway.
        try
        {
            fileSystem.CreateDirectory(directoryPath);
            fileSystem.WriteBytes(FilePath, buffer.WrittenSpan.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort. This runs on the shutdown path, where throwing would turn a lost
            // window position into a failure to exit.
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, double? value)
    {
        if (value is { } number && !double.IsNaN(number) && !double.IsInfinity(number))
        {
            writer.WriteNumber(name, number);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static double? Number(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetDouble(out var number)
            ? number
            : null;

    private static bool? Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
}
