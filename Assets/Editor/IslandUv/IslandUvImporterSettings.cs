using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Read/write IslandUV settings stored in AssetImporter.userData as JSON.
/// Format: { "IslandUV": { "version": 1, ...settings... } }
/// The root object may contain other keys; we only touch the "IslandUV" object.
/// </summary>
public static class IslandUvImporterSettings
{
    public const string RootKey = "IslandUV";
    public const int CurrentVersion = 1;

    [Serializable]
    private class RootContainer
    {
        // Note: JsonUtility can't handle Dictionary. We use a tiny parser for root.
        public IslandUvContainer IslandUV;
    }

    [Serializable]
    private class IslandUvContainer
    {
        public int version = CurrentVersion;

    // Mirror of IslandUvSettings.Settings
        public bool enabled;
        public float thresholdDeg;
        public int targetUvChannel;
        public bool allowAcrossSubMeshes;
    public IslandUvSettings.NormalSource normalSource;
    public IslandUvSettings.Propagation propagation;
        public bool ignoreSmall;
    public IslandUvSettings.SmallIsland smallIsland;
        public int minIslandTris;
        public float minIslandAreaRatio;

    public static IslandUvContainer FromSettings(IslandUvSettings.Settings s)
        {
            return new IslandUvContainer
            {
                version = CurrentVersion,
                enabled = s.enabled,
                thresholdDeg = s.thresholdDeg,
                targetUvChannel = s.targetUvChannel,
                allowAcrossSubMeshes = s.allowAcrossSubMeshes,
                normalSource = s.normalSource,
                propagation = s.propagation,
                ignoreSmall = s.ignoreSmall,
                smallIsland = s.smallIsland,
                minIslandTris = s.minIslandTris,
                minIslandAreaRatio = s.minIslandAreaRatio,
            };
        }

        public IslandUvSettings.Settings ToSettings()
        {
            return new IslandUvSettings.Settings
            {
                enabled = enabled,
                thresholdDeg = thresholdDeg,
                targetUvChannel = targetUvChannel,
                allowAcrossSubMeshes = allowAcrossSubMeshes,
                normalSource = normalSource,
                propagation = propagation,
                ignoreSmall = ignoreSmall,
                smallIsland = smallIsland,
                minIslandTris = minIslandTris,
                minIslandAreaRatio = minIslandAreaRatio,
            };
        }
    }

    public static IslandUvSettings.Settings DefaultSettings => new IslandUvSettings.Settings
    {
        enabled = false,
        thresholdDeg = 25f,
        targetUvChannel = 2,
        allowAcrossSubMeshes = true,
        normalSource = IslandUvSettings.NormalSource.Vertex,
        propagation = IslandUvSettings.Propagation.Local,
        ignoreSmall = false,
        smallIsland = IslandUvSettings.SmallIsland.TriCount,
        minIslandTris = 4,
        minIslandAreaRatio = 0.001f,
    };

    public static bool TryGetSettings(AssetImporter importer, out IslandUvSettings.Settings settings, out bool usedDefault)
    {
        settings = DefaultSettings;
        usedDefault = true;
        if (importer == null) return false;

        string userData = importer.userData;
        if (string.IsNullOrWhiteSpace(userData)) return true;

        // Parse root as a dictionary-ish so we can preserve other keys.
        if (!TryParseRoot(userData, out var rootMap))
            return true; // parse failure -> default

        if (!rootMap.TryGetValue(RootKey, out string islandUvJson) || string.IsNullOrWhiteSpace(islandUvJson))
            return true; // missing -> default

        try
        {
            var container = JsonUtility.FromJson<IslandUvContainer>(islandUvJson);
            if (container == null) return true;

            // Versioning hook (future)
            // For now, accept any version >= 1.
            settings = container.ToSettings();
            usedDefault = false;
            return true;
        }
        catch
        {
            return true; // any failure -> default
        }
    }

    public static void SetSettings(AssetImporter importer, IslandUvSettings.Settings settings)
    {
        if (importer == null) throw new ArgumentNullException(nameof(importer));
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var rootMap = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(importer.userData))
        {
            if (TryParseRoot(importer.userData, out var existing))
                rootMap = existing;
        }

        var container = IslandUvContainer.FromSettings(settings);
        string islandUvJson = JsonUtility.ToJson(container);
        rootMap[RootKey] = islandUvJson;
        importer.userData = BuildRoot(rootMap);
    }

    public static void ClearSettings(AssetImporter importer)
    {
        if (importer == null) throw new ArgumentNullException(nameof(importer));

        if (string.IsNullOrWhiteSpace(importer.userData)) return;
        if (!TryParseRoot(importer.userData, out var rootMap))
        {
            // If userData is not a root object we understand, don't touch it.
            return;
        }

        if (!rootMap.Remove(RootKey)) return;
        importer.userData = rootMap.Count == 0 ? string.Empty : BuildRoot(rootMap);
    }

    public static bool SettingsEqual(IslandUvSettings.Settings a, IslandUvSettings.Settings b)
    {
        if (ReferenceEquals(a, b)) return true;
        if (a == null || b == null) return false;

        // Float comparisons: small epsilon.
        const float eps = 1e-5f;
        bool feq(float x, float y) => Mathf.Abs(x - y) <= eps;

        return a.enabled == b.enabled
            && feq(a.thresholdDeg, b.thresholdDeg)
            && a.targetUvChannel == b.targetUvChannel
            && a.allowAcrossSubMeshes == b.allowAcrossSubMeshes
            && a.normalSource == b.normalSource
            && a.propagation == b.propagation
            && a.ignoreSmall == b.ignoreSmall
            && a.smallIsland == b.smallIsland
            && a.minIslandTris == b.minIslandTris
            && feq(a.minIslandAreaRatio, b.minIslandAreaRatio);
    }

    // -----------------
    // Minimal root JSON handling
    // -----------------

    // Root format is a JSON object with values that are JSON literals (objects/arrays/strings/numbers).
    // We only need to read and rewrite root while preserving other keys.

    private static bool TryParseRoot(string json, out Dictionary<string, string> map)
    {
        map = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            var parser = new RootParser(json);
            return parser.TryParseObject(map);
        }
        catch
        {
            map.Clear();
            return false;
        }
    }

    private static string BuildRoot(Dictionary<string, string> map)
    {
        // Keep stable ordering for diffs.
        var keys = new List<string>(map.Keys);
        keys.Sort(StringComparer.Ordinal);

        var parts = new List<string>(keys.Count);
        foreach (var k in keys)
        {
            string keyJson = JsonUtility.ToJson(new StringWrapper { v = k });
            // JsonUtility.ToJson wraps as {"v":"..."}. Extract the quoted string.
            // This avoids writing our own string escaping.
            string quoted = ExtractQuotedValue(keyJson);
            parts.Add(quoted + ":" + map[k]);
        }
        return "{" + string.Join(",", parts) + "}";
    }

    [Serializable]
    private class StringWrapper { public string v; }

    private static string ExtractQuotedValue(string jsonObject)
    {
        // jsonObject: {"v":"..."}
        int colon = jsonObject.IndexOf(':');
        if (colon < 0) return "\"\"";
        string after = jsonObject.Substring(colon + 1).Trim();
        if (after.EndsWith("}")) after = after.Substring(0, after.Length - 1).Trim();
        return after;
    }

    private sealed class RootParser
    {
        private readonly string _s;
        private int _i;

        public RootParser(string s)
        {
            _s = s ?? string.Empty;
            _i = 0;
        }

        public bool TryParseObject(Dictionary<string, string> map)
        {
            SkipWs();
            if (!Consume('{')) return false;
            SkipWs();
            if (Consume('}')) return true;

            while (true)
            {
                SkipWs();
                if (!TryParseString(out string key)) return false;
                SkipWs();
                if (!Consume(':')) return false;
                SkipWs();
                if (!TryParseJsonValue(out string valueLiteral)) return false;

                map[key] = valueLiteral;

                SkipWs();
                if (Consume('}')) return true;
                if (!Consume(',')) return false;
            }
        }

        private bool TryParseString(out string value)
        {
            value = null;
            if (!Consume('"')) return false;
            int start = _i;
            bool escaped = false;
            var sb = new System.Text.StringBuilder();
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (escaped)
                {
                    // Keep it simple: handle common escapes.
                    switch (c)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (_i + 4 <= _s.Length)
                            {
                                string hex = _s.Substring(_i, 4);
                                _i += 4;
                                if (ushort.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out ushort code))
                                    sb.Append((char)code);
                            }
                            break;
                        default: sb.Append(c); break;
                    }
                    escaped = false;
                    continue;
                }

                if (c == '\\') { escaped = true; continue; }
                if (c == '"') { value = sb.ToString(); return true; }
                sb.Append(c);
            }
            return false;
        }

        private bool TryParseJsonValue(out string literal)
        {
            int start = _i;
            if (_i >= _s.Length) { literal = null; return false; }

            char c = _s[_i];
            if (c == '{')
            {
                if (!ScanBalanced('{', '}', out int end)) { literal = null; return false; }
                literal = _s.Substring(start, end - start);
                _i = end;
                return true;
            }
            if (c == '[')
            {
                if (!ScanBalanced('[', ']', out int end)) { literal = null; return false; }
                literal = _s.Substring(start, end - start);
                _i = end;
                return true;
            }
            if (c == '"')
            {
                // scan string including quotes
                _i++; // consume first quote
                bool escaped = false;
                while (_i < _s.Length)
                {
                    char ch = _s[_i++];
                    if (escaped) { escaped = false; continue; }
                    if (ch == '\\') { escaped = true; continue; }
                    if (ch == '"') break;
                }
                literal = _s.Substring(start, _i - start);
                return true;
            }

            // number / true / false / null
            while (_i < _s.Length)
            {
                char ch = _s[_i];
                if (ch == ',' || ch == '}' || char.IsWhiteSpace(ch)) break;
                _i++;
            }
            literal = _s.Substring(start, _i - start);
            return !string.IsNullOrWhiteSpace(literal);
        }

        private bool ScanBalanced(char open, char close, out int endIndex)
        {
            int depth = 0;
            bool inString = false;
            bool escaped = false;
            while (_i < _s.Length)
            {
                char c = _s[_i++];
                if (inString)
                {
                    if (escaped) { escaped = false; continue; }
                    if (c == '\\') { escaped = true; continue; }
                    if (c == '"') { inString = false; continue; }
                    continue;
                }

                if (c == '"') { inString = true; continue; }

                if (c == open) depth++;
                else if (c == close)
                {
                    depth--;
                    if (depth == 0)
                    {
                        endIndex = _i;
                        return true;
                    }
                }
            }
            endIndex = -1;
            return false;
        }

        private void SkipWs()
        {
            while (_i < _s.Length && char.IsWhiteSpace(_s[_i])) _i++;
        }

        private bool Consume(char c)
        {
            if (_i < _s.Length && _s[_i] == c) { _i++; return true; }
            return false;
        }
    }
}
