using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Input;
using Microsoft.Data.Sqlite;
using QiuniuPlayer.UI.Models;

namespace QiuniuPlayer.UI.Services.Storage;

public class Db : IAsyncDisposable
{
    private const string DbFile = "app.db";
    private readonly SqliteConnection _conn;
    public Db()
    {
        var path = Path.Combine(AppContext.BaseDirectory, DbFile);
        var firstRun = !File.Exists(path);

        _conn = new SqliteConnection($"Data Source={DbFile}");
        _conn.Open();
        if (firstRun)
        {
            InitAsync().GetAwaiter().GetResult();
        }
    }

    private async Task InitAsync()
    {
        var command = _conn.CreateCommand();
        // 配置表 Config
        command.CommandText = @"
        CREATE TABLE IF NOT EXISTS Config (
            Key TEXT PRIMARY KEY,
            Value TEXT
        );
        ";
        await command.ExecuteNonQueryAsync();
        // 歌曲合集 SongCollections
        command.CommandText = @"
        CREATE TABLE IF NOT EXISTS SongCollections (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT,
            AddedDate DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        ";
        await command.ExecuteNonQueryAsync();
        // 歌曲表 AudioFiles
        command.CommandText = @"
        CREATE TABLE IF NOT EXISTS AudioFiles (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SongCollectionId INTEGER,
            Title TEXT,
            Artist TEXT,
            Album TEXT,
            duration INTEGER,
            FilePath TEXT,
            CoverImagePath TEXT,
            SourceType TEXT,
            AddedDate DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        ";
        await command.ExecuteNonQueryAsync();
        // 播放列表 Playlists
        command.CommandText = @"
        CREATE TABLE IF NOT EXISTS Playlists (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            name TEXT,
            AddedDate DATETIME DEFAULT CURRENT_TIMESTAMP
        );
        ";
        await command.ExecuteNonQueryAsync();

        // 2. 默认配置
        var downloads = Path.Combine(AppContext.BaseDirectory, "downloads");
        Directory.CreateDirectory(downloads);

        var defaultConfigs = new[]
        {
            new Config { Key = ConfigKeys.Language, Value = "zh-CN" },
            new Config { Key = ConfigKeys.Theme, Value = "Dark" },
            new Config { Key = ConfigKeys.DownloadFolder, Value = downloads }
        };

        foreach (var config in defaultConfigs)
        {
            await SetConfig(config.Key, config.Value);
        }
        await command.DisposeAsync();
    }
    public ValueTask DisposeAsync() => _conn.DisposeAsync();

    public async Task<string?> GetConfig(string key)
    {
        var command = _conn.CreateCommand();
        command.CommandText = $"SELECT value FROM config WHERE key = $key";
        command.Parameters.AddWithValue("$key", key);
        var s = await command.ExecuteScalarAsync();
        await command.DisposeAsync();
        return s is null ? null : s.ToString();
    }

    public async Task<Boolean> SetConfig(string key, string value)
    {
        var command = _conn.CreateCommand();
        command.CommandText = $"INSERT INTO config (key, value) VALUES ($key, $value) ON CONFLICT(key) DO UPDATE SET value = $value";
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        var result = await command.ExecuteNonQueryAsync();
        await command.DisposeAsync();
        return result > 0;
    }
}
