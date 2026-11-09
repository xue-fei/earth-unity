using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using UnityEditor;
using UnityEngine;

public class MapCacheWindow : EditorWindow
{
    static MapCacheWindow mapWindow;
    static List<string> channelStrings = new List<string>();
    static List<string> typeStrings = new List<string>();

    static int channelIndex = 0;
    static int typeIndex = 0;

    /// <summary>
    /// 默认只缓存 level 5（1024 片）。每升一级瓦片数翻 4 倍，
    /// 原实现 5~19 全量约为 3.67e11 片，点击即卡死编辑器并撑爆磁盘。
    /// </summary>
    static int MinLevel = 5;
    static int MaxLevel = 5;

    const int RequestTimeoutMs = 8000;

    /// <summary>
    /// level,lat,lon
    /// </summary>
    static string mapUrl = "";

    [MenuItem("工具/地图缓存工具", false, 0)]
    static void Init()
    {
        mapWindow = (MapCacheWindow)EditorWindow.GetWindow(typeof(MapCacheWindow), false, "地图缓存工具", true);
        mapWindow.Show();
        RefreshEnums();
    }

    static void RefreshEnums()
    {
        channelStrings = Enum.GetNames(typeof(MapChannel)).ToList();
        typeStrings = Enum.GetNames(typeof(MapType)).ToList();
        channelIndex = Mathf.Clamp(channelIndex, 0, channelStrings.Count - 1);
        typeIndex = Mathf.Clamp(typeIndex, 0, typeStrings.Count - 1);
    }

    private void OnFocus()
    {
        RefreshEnums();
    }

    private void OnGUI()
    {
        if (channelStrings.Count == 0 || typeStrings.Count == 0)
        {
            RefreshEnums();
        }

        EditorGUILayout.Space();
        GUILayout.Label("地图渠道");
        channelIndex = GUILayout.Toolbar(channelIndex, channelStrings.ToArray());
        EditorGUILayout.Space();
        GUILayout.Label("地图类型");
        typeIndex = GUILayout.Toolbar(typeIndex, typeStrings.ToArray());
        EditorGUILayout.Space();

        MinLevel = EditorGUILayout.IntSlider("起始层级", MinLevel, 0, 19);
        MaxLevel = EditorGUILayout.IntSlider("结束层级", MaxLevel, 0, 19);
        if (MaxLevel < MinLevel)
        {
            MaxLevel = MinLevel;
        }

        long total = TileCount(MinLevel, MaxLevel);
        EditorGUILayout.HelpBox(
            string.Format("预计瓦片数：{0:N0}（每升一级翻 4 倍）。请确认磁盘空间与服务端配额。", total),
            MessageType.Warning);

        if (GUILayout.Button("开始缓存"))
        {
            string channelStr = channelStrings[channelIndex];
            string typeStr = typeStrings[typeIndex];
            MapChannel channel = (MapChannel)Enum.Parse(typeof(MapChannel), channelStr);
            MapType type = (MapType)Enum.Parse(typeof(MapType), typeStr);

            if (channel == MapChannel.None || type == MapType.None)
            {
                EditorUtility.DisplayDialog("参数无效", "请选择有效的地图渠道与地图类型。", "确定");
                return;
            }
            if (channel == MapChannel.ArcGIS && type == MapType.RoadMap)
            {
                EditorUtility.DisplayDialog("组合无效",
                    "ArcGIS 渠道只提供 World_Imagery 影像，没有路网瓦片。请改选高德。", "确定");
                return;
            }

            mapUrl = ResolveUrl(channel, type);
            string rootPath = GetChannelPath(channel, type);

            if (!EditorUtility.DisplayDialog("确认缓存",
                    string.Format("渠道：{0} / 类型：{1}\n层级：{2} ~ {3}\n预计瓦片：{4:N0}\n目录：{5}",
                        channel, type, MinLevel, MaxLevel, total, rootPath),
                    "开始", "取消"))
            {
                return;
            }

            EarthStart(rootPath);
        }
    }

    static string ResolveUrl(MapChannel channel, MapType type)
    {
        switch (channel)
        {
            case MapChannel.ArcGIS:
                return MapUrl.ArcGIS;
            case MapChannel.AutoNavi:
                if (type == MapType.RoadMap)
                {
                    return "https://wprd03.is.autonavi.com/appmaptile?style=8&x={2}&y={1}&z={0}";
                }
                return MapUrl.AutoNavi; // style=6 卫星，也是 Terrain 的兜底
            default:
                return "";
        }
    }

    /// <summary>
    /// 每次都从 persistentDataPath 重新拼接，避免静态字段累加成
    /// .../AutoNavi/Satellite/AutoNavi/Satellite/ 导致运行时读不到缓存。
    /// 拼接方式与 MapLayer.Init 保持一致。
    /// </summary>
    static string GetChannelPath(MapChannel channel, MapType type)
    {
        return (Application.persistentDataPath + "/tempMap/" + channel + "/" + type + "/")
            .Replace('\\', '/');
    }

    static long TileCount(int min, int max)
    {
        long total = 0;
        for (int level = min; level <= max; level++)
        {
            total += 1L << (2 * level); // 4^level
        }
        return total;
    }

    public void EarthStart(string rootPath)
    {
        if (!Directory.Exists(rootPath))
        {
            Directory.CreateDirectory(rootPath);
        }

        long total = TileCount(MinLevel, MaxLevel);
        long done = 0, success = 0, failed = 0, skipped = 0;
        bool cancelled = false;

        try
        {
            for (int level = MinLevel; level <= MaxLevel && !cancelled; level++)
            {
                int subdivisions = 1 << level;
                string levelDir = rootPath + level + "/";
                if (!Directory.Exists(levelDir))
                {
                    Directory.CreateDirectory(levelDir);
                }

                for (int lat = 0; lat < subdivisions && !cancelled; lat++)
                {
                    string latDir = levelDir + lat + "/";
                    if (!Directory.Exists(latDir))
                    {
                        Directory.CreateDirectory(latDir);
                    }

                    for (int lon = 0; lon < subdivisions; lon++)
                    {
                        done++;
                        string path = latDir + lon + ".jpg";

                        if (File.Exists(path))
                        {
                            skipped++;
                        }
                        else
                        {
                            string url = string.Format(mapUrl, level, lat, lon);
                            if (DownloadTile(url, path))
                            {
                                success++;
                            }
                            else
                            {
                                failed++;
                            }

                            // 下载是耗时大头，下载后立即检查取消
                            if (ShowProgress(level, done, total, success, failed, skipped))
                            {
                                cancelled = true;
                                break;
                            }
                        }

                        // 全部命中缓存时也要保持界面可响应
                        if (!cancelled && (done & 15) == 0)
                        {
                            if (ShowProgress(level, done, total, success, failed, skipped))
                            {
                                cancelled = true;
                                break;
                            }
                        }
                    }
                }
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        Debug.Log(string.Format(
            "[地图缓存] {0}：成功 {1}，跳过 {2}，失败 {3}，目录 {4}",
            cancelled ? "已取消" : "完成", success, skipped, failed, rootPath));
    }

    static bool ShowProgress(int level, long done, long total, long success, long failed, long skipped)
    {
        float progress = total > 0 ? (float)done / total : 1f;
        return EditorUtility.DisplayCancelableProgressBar(
            "正在缓存地图 level " + level,
            string.Format("{0:N0}/{1:N0}  成功 {2} / 失败 {3} / 已存在 {4}",
                done, total, success, failed, skipped),
            progress);
    }

    /// <summary>
    /// 下载到 .tmp 后再原子改名，避免中断留下 0 字节的 .jpg 被 File.Exists 判为已缓存。
    /// 单个瓦片失败只记日志，不中断整批。
    /// </summary>
    static bool DownloadTile(string url, string path)
    {
        string tmp = path + ".tmp";
        try
        {
            using (var client = new TimeoutWebClient { Timeout = RequestTimeoutMs })
            {
                client.DownloadFile(url, tmp);
            }

            var info = new FileInfo(tmp);
            if (!info.Exists || info.Length == 0)
            {
                throw new IOException("下载内容为空");
            }

            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning("[地图缓存] 下载失败 " + url + " -> " + path + "：" + e.Message);
            try
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
            catch
            {
                // 清理失败不影响后续瓦片
            }
            return false;
        }
    }

    /// <summary>
    /// WebClient 默认无超时，服务端限流时会永久卡住编辑器主线程。
    /// </summary>
    sealed class TimeoutWebClient : WebClient
    {
        public int Timeout { get; set; }

        protected override WebRequest GetWebRequest(Uri address)
        {
            WebRequest request = base.GetWebRequest(address);
            if (request != null)
            {
                request.Timeout = Timeout;
            }
            return request;
        }
    }
}
