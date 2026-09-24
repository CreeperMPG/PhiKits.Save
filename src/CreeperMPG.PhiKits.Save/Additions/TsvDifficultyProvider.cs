using CreeperMPG.PhiKits.Save.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CreeperMPG.PhiKits.Save.Additions
{
    public class TsvDifficultyProvider : IDifficultyProvider
    {
        private readonly Dictionary<string, float[]> _map;

        public bool IsLoaded { get; }

        public int Count => _map.Count;

        private TsvDifficultyProvider(Dictionary<string, float[]> map)
        {
            _map = map;
            IsLoaded = true;
        }

        public static TsvDifficultyProvider FromFile(string path)
        {
            var map = new Dictionary<string, float[]>(StringComparer.Ordinal);
            foreach (string rawLine in File.ReadAllLines(path))
            {
                string line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#')) continue;

                string[] parts = line.Split('\t');
                if (parts.Length < 2) continue;

                // 首列是曲目 ID，其后是各难度定数（最多 4 个：EZ/HD/IN/AT）
                var values = new float[4];
                for (int i = 0; i < 4; i++)
                {
                    int col = i + 1;
                    values[i] = (col < parts.Length && float.TryParse(parts[col],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out float v))
                        ? v : 0;
                }
                map[parts[0]] = values;
            }
            return new TsvDifficultyProvider(map);
        }

        public float? GetDifficulty(string songId, int difficultyIndex)
        {
            if (difficultyIndex < 0 || difficultyIndex > 3) return null;

            // 存档里的 key 带版本后缀（形如 "Glaciaxion.SunsetRay.0"），TSV 里没有，
            // 所以查表前先剥掉最后一个 ".N"。
            string normalized = StripVersionSuffix(songId);

            if (!_map.TryGetValue(normalized, out float[]? values)) return null;

            float value = values[difficultyIndex];
            return value < 0 ? null : value;
        }

        /// <summary>剥掉末尾的 ".数字" 版本后缀</summary>
        internal static string StripVersionSuffix(string songId)
        {
            int dot = songId.LastIndexOf('.');
            if (dot <= 0 || dot == songId.Length - 1) return songId;

            for (int i = dot + 1; i < songId.Length; i++)
                if (!char.IsDigit(songId[i])) return songId;   // 末尾不是纯数字，原样返回

            return songId[..dot];
        }
    }
}
