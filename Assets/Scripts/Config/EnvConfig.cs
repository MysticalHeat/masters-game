using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MastersGame.Config
{
    public static class EnvConfig
    {
        private static readonly string[] SearchPaths =
        {
            Path.Combine(Application.dataPath, "..", ".env"),
            Path.Combine(Application.dataPath, ".env"),
            Path.Combine(Application.streamingAssetsPath, "..", ".env"),
            Path.Combine(Application.persistentDataPath, ".env"),
        };

        private static Dictionary<string, string> cachedValues;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        public static void Load()
        {
            cachedValues = new Dictionary<string, string>();
            LoadFromEnv();
        }

        [ContextMenu("Reload .env")]
        public static void Reload()
        {
            cachedValues = null;
            EnsureLoaded();
        }

        public static string Get(string key, string defaultValue = null)
        {
            EnsureLoaded();
            return cachedValues.TryGetValue(key, out var value) ? value : defaultValue;
        }

        private static void EnsureLoaded()
        {
            if (cachedValues != null)
            {
                return;
            }

            cachedValues = new Dictionary<string, string>();
            LoadFromEnv();
        }

        private static void LoadFromEnv()
        {
            foreach (var path in SearchPaths)
            {
                var fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath))
                {
                    continue;
                }

                try
                {
                    Debug.Log($"[EnvConfig] Reading .env from: {fullPath}");
                    var lines = File.ReadAllLines(fullPath);
                    var loadedCount = 0;
                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#"))
                        {
                            continue;
                        }

                        var separatorIndex = trimmed.IndexOf('=');
                        if (separatorIndex <= 0)
                        {
                            continue;
                        }

                        var key = trimmed.Substring(0, separatorIndex).Trim();
                        var value = trimmed.Substring(separatorIndex + 1).Trim();

                        if (value.StartsWith("\"") && value.EndsWith("\""))
                        {
                            value = value.Substring(1, value.Length - 2);
                        }
                        else if (value.StartsWith("'") && value.EndsWith("'"))
                        {
                            value = value.Substring(1, value.Length - 2);
                        }

                        if (!cachedValues.ContainsKey(key))
                        {
                            cachedValues[key] = value;
                            loadedCount++;
                        }
                    }

                    Debug.Log($"[EnvConfig] Loaded {loadedCount} entries from {fullPath}");
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[EnvConfig] Failed to read .env from {fullPath}: {exception.Message}");
                }

                break;
            }

            foreach (var envKey in new[] { "MASTERS_LLM_BASE_URL", "MASTERS_LLM_API_KEY", "MASTERS_LLM_MODEL" })
            {
                var envValue = Environment.GetEnvironmentVariable(envKey, EnvironmentVariableTarget.Process);
                if (!string.IsNullOrWhiteSpace(envValue) && !cachedValues.ContainsKey(envKey))
                {
                    cachedValues[envKey] = envValue;
                    Debug.Log($"[EnvConfig] Loaded from env: {envKey}");
                }
            }
        }
    }
}
