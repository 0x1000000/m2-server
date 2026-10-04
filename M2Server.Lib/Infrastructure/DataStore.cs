using System.Text.Json;
using System.Text.Json.Serialization;
using M2Server.Lib.Domain;

namespace M2Server.Lib.Infrastructure;

public sealed class DataStore
{
    private readonly bool _allowLegacy;
    private readonly string _file;

    private readonly object _gate = new();
    private readonly string _legacyFile;
    private readonly bool _persistChanges;
    private AppDocument _document;
    private AppDocument _saved;

    public DataStore(string? directory = null, bool persistChanges = true)
    {
        var dir = directory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Constants.DataDirectoryName
        );
        if (persistChanges)
        {
            Directory.CreateDirectory(dir);
        }

        this._file = Path.Combine(dir, Constants.DataFileName);
        this._legacyFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Constants.DataDirectoryName,
            Constants.DataFileName
        );
        if (directory is null && !File.Exists(this._file) && !File.Exists(ServiceDataDirectory.ReferencePath) &&
            File.Exists(this._legacyFile))
        {
            Directory.CreateDirectory(dir);
            File.Copy(this._legacyFile, this._file);
        }

        this._persistChanges = persistChanges;
        this._allowLegacy = directory is null;
        var source = File.Exists(this._file) || !this._allowLegacy || File.Exists(ServiceDataDirectory.ReferencePath)
            ? this._file
            : this._legacyFile;
        this._document = File.Exists(source)
            ? JsonSerializer.Deserialize(File.ReadAllText(source), AppDocumentJsonContext.Default.AppDocument) ??
              new AppDocument()
            : new AppDocument();
        this._saved = Clone(this._document);
    }

    public string DirectoryPath => Path.GetDirectoryName(this._file)!;

    public bool HasDrafts => this.Read(document => SerializeDocument(document) != SerializeDocument(this._saved));

    public event EventHandler? Changed;

    public T Read<T>(Func<AppDocument, T> select)
    {
        lock (this._gate)
        {
            return select(Clone(this._document));
        }
    }

    public T Update<T>(Func<AppDocument, T> change)
    {
        lock (this._gate)
        {
            var copy = Clone(this._document);
            var result = change(copy);
            if (this._persistChanges)
            {
                this.Write(copy);
                this._saved = Clone(copy);
            }

            this._document = copy;
            this.Changed?.Invoke(this, EventArgs.Empty);
            return result;
        }
    }

    public string Serialize()
    {
        return this.Read(document => JsonSerializer.Serialize(document, AppDocumentJsonContext.Default.AppDocument));
    }

    public T ReadSaved<T>(Func<AppDocument, T> select)
    {
        lock (this._gate)
        {
            return select(Clone(this._saved));
        }
    }

    public void Save()
    {
        lock (this._gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this._file)!);
            var document = Clone(this._document);
            if (File.Exists(this._file))
            {
                var onDisk = JsonSerializer.Deserialize(File.ReadAllText(this._file), AppDocumentJsonContext.Default.AppDocument);
                if (onDisk is not null && onDisk.Settings.PasswordHash == document.Settings.PasswordHash &&
                    document.Settings.WebEnabled != false)
                {
                    document.Tokens = onDisk.Tokens;
                }
            }

            this.Write(document);
            this._document = document;
            this._saved = Clone(document);
        }
    }

    public void RestoreSaved(AppDocument previous)
    {
        lock (this._gate)
        {
            this.Write(previous);
            this._saved = Clone(previous);
        }
    }

    public bool IsDraftPreset(Guid id)
    {
        return this.Read(document =>
            {
                var current = document.Presets.FirstOrDefault(p => p.Id == id);
                var saved = this._saved.Presets.FirstOrDefault(p => p.Id == id);
                return current is not null && (saved is null ||
                                               JsonSerializer.Serialize(current, AppDocumentJsonContext.Default.Preset) !=
                                               JsonSerializer.Serialize(saved, AppDocumentJsonContext.Default.Preset));
            }
        );
    }

    public bool IsDraftScript(Guid id)
    {
        return this.Read(document =>
            {
                var current = document.Scripts.FirstOrDefault(s => s.Id == id);
                var saved = this._saved.Scripts.FirstOrDefault(s => s.Id == id);
                return current is not null && (saved is null ||
                                               JsonSerializer.Serialize(current, AppDocumentJsonContext.Default.ScriptEntry) !=
                                               JsonSerializer.Serialize(saved, AppDocumentJsonContext.Default.ScriptEntry));
            }
        );
    }

    private static string SerializeDocument(AppDocument document)
    {
        return JsonSerializer.Serialize(document, AppDocumentJsonContext.Default.AppDocument);
    }

    public void ReplaceFromJson(string json)
    {
        var document = JsonSerializer.Deserialize(json, AppDocumentJsonContext.Default.AppDocument) ??
                       throw new InvalidDataException("The configuration is empty.");
        lock (this._gate)
        {
            if (this._persistChanges)
            {
                this.Write(document);
            }

            this._document = document;
            this._saved = Clone(document);
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Write(AppDocument document)
    {
        var temp = this._file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(document, AppDocumentJsonContext.Default.AppDocument));
        File.Move(temp, this._file, true);
    }

    public void Reload()
    {
        lock (this._gate)
        {
            var source = File.Exists(this._file) || !this._allowLegacy || File.Exists(ServiceDataDirectory.ReferencePath)
                ? this._file
                : this._legacyFile;
            this._document = File.Exists(source)
                ? JsonSerializer.Deserialize(File.ReadAllText(source), AppDocumentJsonContext.Default.AppDocument) ??
                  new AppDocument()
                : new AppDocument();
            this._saved = Clone(this._document);
        }
    }

    public void DiscardDrafts()
    {
        lock (this._gate)
        {
            this._document = Clone(this._saved);
            this.Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private static AppDocument Clone(AppDocument source)
    {
        return JsonSerializer.Deserialize(
            JsonSerializer.Serialize(source, AppDocumentJsonContext.Default.AppDocument),
            AppDocumentJsonContext.Default.AppDocument
        )!;
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true
)]
[JsonSerializable(typeof(AppDocument))]
[JsonSerializable(typeof(Preset))]
[JsonSerializable(typeof(ScriptEntry))]
public sealed partial class AppDocumentJsonContext : JsonSerializerContext;