using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RedLoader;

namespace KelvinTwo;

/// <summary>
/// Remembers which world-sim actor id is the clone, per save, so the same Kelvin
/// stays "Kelvin 2" across reloads. Stored in Mods/KelvinTwo/clones.json,
/// keyed by "SaveType:SaveId".
/// </summary>
internal sealed class CloneStore
{
    private readonly string _path;
    private Dictionary<string, int> _bySave = new();

    internal CloneStore(string path)
    {
        _path = path;
        try
        {
            if (File.Exists(path))
                _bySave = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(path)) ?? new();
        }
        catch (Exception ex)
        {
            RLog.Error("[KelvinTwo] clones.json unreadable, starting fresh: " + ex.Message);
        }
    }

    internal bool TryGet(string saveId, out int uniqueId) => _bySave.TryGetValue(saveId, out uniqueId);

    internal void Set(string saveId, int uniqueId)
    {
        _bySave = new Dictionary<string, int>(_bySave) { [saveId] = uniqueId };
        try
        {
            File.WriteAllText(_path, JsonSerializer.Serialize(_bySave, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            RLog.Error("[KelvinTwo] could not write clones.json: " + ex.Message);
        }
    }
}
