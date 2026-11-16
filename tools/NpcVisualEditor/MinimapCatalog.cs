using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NpcVisualEditor
{
    /// <summary>
    /// 小地图坐标校准：读部署区 对照表.csv（地图文件名,宽度,高度,小地图文件）。
    /// 地图内对象坐标是"格坐标"（如 0~700），小地图 PNG 像素尺寸可能非等比
    /// （如 0.map 逻辑 700x700，PNG 1050x700），必须按逻辑宽高归一化定位。
    /// </summary>
    public class MinimapCatalog
    {
        private readonly Dictionary<string, MapSize> _sizes =
            new(StringComparer.OrdinalIgnoreCase);

        private class MapSize
        {
            public int Width, Height;
            public string Png;
        }

        public MinimapCatalog(string clientDir)
        {
            string csv = Path.Combine(clientDir ?? "", "Map", "MiniMaps_Export", "对照表.csv");
            if (!File.Exists(csv)) return;
            try
            {
                foreach (var line in File.ReadAllLines(csv).Skip(1))
                {
                    var p = line.Split(',');
                    if (p.Length < 3) continue;
                    if (!int.TryParse(p[1], out int w) || !int.TryParse(p[2], out int h)) continue;
                    if (w <= 0 || h <= 0) continue;
                    _sizes[p[0].Trim()] = new MapSize
                    {
                        Width = w, Height = h,
                        Png = p.Length > 3 ? p[3].Trim() : p[0].Replace(".map", "") + "_MiniMap.png"
                    };
                }
            }
            catch { }
        }

        public bool TryGet(string mapFileName, out int logicalWidth, out int logicalHeight, out string pngName)
        {
            if (_sizes.TryGetValue(mapFileName, out var s))
            {
                logicalWidth = s.Width; logicalHeight = s.Height; pngName = s.Png;
                return true;
            }
            logicalWidth = logicalHeight = 0; pngName = null;
            return false;
        }
    }
}
