using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public class MapLayer : MonoBehaviour
{
    Camera mainCamera;
    public GameObject earth;
    public GameObject layer;
    public MapType mapType = MapType.None;
    public MapChannel mapChannel = MapChannel.None;
    /// <summary>
    /// level,lat,lon
    /// </summary>
    public string mapUrl = "";
    public string tempMapPath;
    public int MaxLevel = 19;
    public int MinLevel = 5;
    private int nowLevel;

    public Material material;
    Dictionary<string, GameObject> mapDic = new Dictionary<string, GameObject>();
    public Dictionary<long, GameObject> MapFas = new Dictionary<long, GameObject>();
    int renderQueueAdd;

    #region 瓦片调度与资源生命周期

    class TileRequest
    {
        public string key;
        public int level;
        public int lat;   // 行 row，对应瓦片 URL 的 y
        public int lon;   // 列 col，对应瓦片 URL 的 x
    }

    [Header("瓦片调度")]
    [Tooltip("同时在途的网络请求数。WebGL 浏览器同域连接数约 6，不要调太高。")]
    public int maxConcurrentRequests = 6;
    [Tooltip("本图层常驻的瓦片上限，超出后按 LRU 销毁 GameObject / Mesh / Material / Texture。")]
    public int maxCachedTiles = 1024;
    [Tooltip("单次视野请求的瓦片数上限（安全阀）。按屏幕视锥计算正常只用几十块，异常配置时自动降层。")]
    public int maxVisibleTiles = 900;
    [Tooltip("等待队列上限，超出时丢弃最旧的请求（保住新视野）。")]
    public int maxPendingTiles = 4096;
    [Tooltip("单个瓦片失败后的重试次数。")]
    public int maxRetries = 2;

    const int RequestTimeoutSeconds = 15;

    readonly LinkedList<TileRequest> pending = new LinkedList<TileRequest>();
    readonly HashSet<string> pendingKeys = new HashSet<string>();
    readonly Dictionary<string, LinkedListNode<TileRequest>> pendingNodes =
        new Dictionary<string, LinkedListNode<TileRequest>>();
    readonly LinkedList<string> lruOrder = new LinkedList<string>();
    readonly Dictionary<string, LinkedListNode<string>> lruNodes = new Dictionary<string, LinkedListNode<string>>();
    bool workersStarted;

    #endregion

    public void Init(GameObject earth, MapChannel mapChannel, MapType mapType, Material material, int renderQueueAdd)
    {
        mainCamera = Camera.main;
        this.earth = earth;
        this.mapChannel = mapChannel;
        this.mapType = mapType;
        this.material = material;
        this.renderQueueAdd = renderQueueAdd;
        tempMapPath = Application.persistentDataPath + "/tempMap/";
        switch (mapChannel)
        {
            case MapChannel.ArcGIS:
                mapUrl = MapUrl.ArcGIS;
                break;
            case MapChannel.AutoNavi:
                mapUrl = MapUrl.AutoNavi;
                if (mapType == MapType.Satellite)
                {
                    mapUrl = "https://wprd03.is.autonavi.com/appmaptile?style=6&x={2}&y={1}&z={0}";
                }
                if (mapType == MapType.RoadMap)
                {
                    mapUrl = "https://wprd03.is.autonavi.com/appmaptile?style=8&x={2}&y={1}&z={0}";
                }
                break;
        }
        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            tempMapPath = tempMapPath + mapChannel.ToString() + "/" + mapType.ToString() + "/";
            if (!Directory.Exists(tempMapPath))
            {
                Directory.CreateDirectory(tempMapPath);
            }
        }
        layer = new GameObject(mapType.ToString());
        layer.transform.parent = earth.transform;

        // 不再全量预载最低层：任何层级都只按当前可见球冠请求瓦片，且只显示一层。
        // 固定常驻 L5 底图会让高缩放时视野边缘露出低清影像，滚轮一动就"跳图"。
        if (!workersStarted)
        {
            workersStarted = true;
            int workers = Mathf.Max(1, maxConcurrentRequests);
            for (int i = 0; i < workers; i++)
            {
                StartCoroutine(LoadWorker());
            }
        }

        // 初始视野优先入队；原先只在鼠标有输入时才算瓦片，静止启动会缺图。
        CamerPosToMap();
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        pending.Clear();
        pendingKeys.Clear();
        pendingNodes.Clear();
        foreach (var item in mapDic)
        {
            ReleaseTileObjects(item.Value, null);
        }
        mapDic.Clear();
        lruOrder.Clear();
        lruNodes.Clear();
    }

    // 相机动画（定位/切渠道）期间没有鼠标输入，只在输入时刷新会漏掉视野变化。
    // CamerPosToMap 只做字典查找与 LRU Touch，逐帧调用开销可控。
    void FixedUpdate()
    {
        if (mainCamera == null)
        {
            return;
        }
        CamerPosToMap();
    }

    /// <summary>
    /// 按屏幕视锥覆盖的地表范围请求「当前层级」的瓦片。
    /// 层级由高度映射，另设瓦片数预算作为安全阀。只显示单层，不用最低层做常驻底图。
    /// </summary>
    void CamerPosToMap()
    {
        Vector3 p = mainCamera.transform.position;
        double mag = p.magnitude;
        if (mag < 1e-6)
        {
            return;   // 相机位于地心时经纬度无定义，避免除零产生 NaN 索引
        }

        // GeoToPoint: x=-r*cosLat*cosLon, y=r*sinLat, z=-r*cosLat*sinLon
        double lon = Math.Atan2(-p.z, -p.x) * 180.0 / Math.PI;   // [-180,180]，带符号
        double sinLat = Math.Max(-1.0, Math.Min(1.0, p.y / mag));
        double lat = Math.Asin(sinLat) * 180.0 / Math.PI;

        // 用屏幕视锥打到球面的边缘，而不是几何可见球冠 acos(R/d)。
        // 贴地时球冠 0.7°、屏幕却只有 0.002°：按球冠请求会把预算撑爆，
        // 被迫逐级降层到 12 级，高缩放永远看不到细节。
        double viewRadius = ScreenViewRadiusDeg(mag);

        int level = Mathf.Clamp(altitudeToZoom((float)(mag - Earth.radius) * 10000f), MinLevel, MaxLevel);
        int rowMin, rowMax, colMin, colMax;
        long tiles = ComputeVisibleRange(level, lon, lat, viewRadius,
                                        out rowMin, out rowMax, out colMin, out colMax);
        while (level > MinLevel && tiles > maxVisibleTiles)
        {
            level--;
            tiles = ComputeVisibleRange(level, lon, lat, viewRadius,
                                        out rowMin, out rowMax, out colMin, out colMax);
        }

        NowLevel = level;

        int n = 1 << Mathf.Clamp(level, 0, 30);
        int col = Mathf.Clamp((int)((lon + 180.0) / 360.0 * n), 0, n - 1);
        int row = Mathf.Clamp((int)(LatToMercatorY01(lat) * n), 0, n - 1);

        // 视线中心先入队并保持在队首，缩放后中央最先出图；ReadMap 自带去重。
        const int inner = 2;
        for (int r = Mathf.Max(0, row - inner); r <= Mathf.Min(n - 1, row + inner); r++)
        {
            for (int c = col - inner; c <= col + inner; c++)
            {
                ReadMap(r, WrapCol(c, n), level, true);
            }
        }

        // 再按可见包围盒补齐视野内其余瓦片，数量受预算约束。
        int queued = 0;
        for (int r = rowMin; r <= rowMax && queued < maxVisibleTiles; r++)
        {
            for (int c = colMin; c <= colMax && queued < maxVisibleTiles; c++)
            {
                ReadMap(r, WrapCol(c, n), level, false);
                queued++;
            }
        }
    }

    /// <summary>
    /// 屏幕视锥边缘射线打到球面后，相对星下点的地心角（度）。
    /// 相机恒看向地心（position = rotation * (0,0,-d)）。设 u=d/R、β=屏幕对角半角：
    ///   射线与球相交：α = asin(u·sinβ) − β
    ///   射线擦过球外：地平线落在屏幕内，需覆盖整个可见球冠 α = acos(1/u)
    /// 两式在 u·sinβ = 1 处连续（均为 90°−β）。
    /// </summary>
    double ScreenViewRadiusDeg(double mag)
    {
        double u = mag / Earth.radius;
        float aspect = mainCamera.aspect > 0.01f ? mainCamera.aspect : 1.777f;
        double tanHalfFov = Math.Tan(mainCamera.fieldOfView * 0.5 * Math.PI / 180.0);
        double beta = Math.Atan(tanHalfFov * Math.Sqrt(1.0 + (double)aspect * aspect));
        double sinB = Math.Sin(beta);
        if (u * sinB >= 1.0)
        {
            return Math.Acos(Math.Min(1.0, 1.0 / u)) * 180.0 / Math.PI;
        }
        // 相机贴球面时 α→0；钳到 0 防止浮点误差产生负半径
        return Math.Max(0.0, (Math.Asin(u * sinB) - beta) * 180.0 / Math.PI);
    }

    static int WrapCol(int c, int n)
    {
        return ((c % n) + n) % n;
    }

    /// <summary>WebMercator 归一化 y（0=最北，1=最南），极区钳制到 ±85.05112878°。</summary>
    static double LatToMercatorY01(double latDeg)
    {
        double r = Math.Max(-85.05112878, Math.Min(85.05112878, latDeg)) * Math.PI / 180.0;
        return 0.5 - Math.Log(Math.Tan(Math.PI * 0.25 + r * 0.5)) / (2.0 * Math.PI);
    }

    /// <summary>
    /// 以 (lon,lat) 为中心、viewRadiusDeg 为地心角半径的球面区域，在指定层级
    /// 覆盖的瓦片行列范围，返回瓦片总数。
    /// 列号为未环绕的连续值（可能为负或 >= n），由 WrapCol 处理跨日期线。
    /// </summary>
    static long ComputeVisibleRange(int level, double lon, double lat, double viewRadiusDeg,
                                    out int rowMin, out int rowMax, out int colMin, out int colMax)
    {
        int n = 1 << Mathf.Clamp(level, 0, 30);
        double sinR = Math.Sin(viewRadiusDeg * Math.PI / 180.0);
        double cosLat = Math.Cos(lat * Math.PI / 180.0);
        // 区域含极点时经度覆盖整圈，否则按球面几何求最大经度偏离
        double dlon = (cosLat <= sinR) ? 180.0 :
                      Math.Asin(Math.Min(1.0, sinR / cosLat)) * 180.0 / Math.PI;

        rowMin = Mathf.Clamp((int)(LatToMercatorY01(lat + viewRadiusDeg) * n), 0, n - 1);
        rowMax = Mathf.Clamp((int)(LatToMercatorY01(lat - viewRadiusDeg) * n), 0, n - 1);
        if (rowMax < rowMin)
        {
            rowMax = rowMin;
        }

        colMin = (int)Math.Floor((lon - dlon + 180.0) / 360.0 * n);
        colMax = (int)Math.Ceiling((lon + dlon + 180.0) / 360.0 * n);
        if (colMax - colMin + 1 >= n)
        {
            colMin = 0;
            colMax = n - 1;
        }

        return (long)(rowMax - rowMin + 1) * (colMax - colMin + 1);
    }

    int altitudeToZoom(float altitude)
    {
        double A = 40487.57d;
        double B = 0.00007096758d;
        double C = 91610.74d;
        double D = -40467.74d;

        return (int)Math.Round(D + (A - D) / (1 + Math.Pow(altitude / C, B)));
    }


    public int NowLevel
    {
        get
        {
            return nowLevel;
        }
        set
        {
            if (!MapFas.ContainsKey(value))
            {
                if (layer == null)
                {
                    return;
                }
                GameObject obj = layer.transform.Find(value.ToString())?.gameObject;
                if (obj == null)
                {
                    obj = new GameObject(value.ToString());
                    obj.transform.parent = layer.transform;
                    obj.transform.SetAsLastSibling();
                }
                MapFas.Add(value, obj);
            }
            if (value != nowLevel)
            {
                // 只显示当前层。固定常驻 MinLevel 底图会让高缩放视野边缘露出低清
                // 影像（滚轮时"跳"出 L5 图案），多层同时贴同一球面还会交错重叠。
                LinkedListNode<TileRequest> node = pending.First;
                while (node != null)
                {
                    LinkedListNode<TileRequest> next = node.Next;
                    if (node.Value.level != value)
                    {
                        pendingKeys.Remove(node.Value.key);
                        pendingNodes.Remove(node.Value.key);
                        pending.Remove(node);
                    }
                    node = next;
                }

                foreach (var item in MapFas)
                {
                    item.Value.SetActive(item.Key == value);
                }
                nowLevel = value;
            }
        }
    }

    #region 请求队列

    void Enqueue(TileRequest req, bool priority)
    {
        if (!pendingKeys.Add(req.key))
        {
            return;
        }
        LinkedListNode<TileRequest> node = priority ? pending.AddFirst(req) : pending.AddLast(req);
        pendingNodes[req.key] = node;

        while (pending.Count > maxPendingTiles)
        {
            LinkedListNode<TileRequest> last = pending.Last;
            pendingKeys.Remove(last.Value.key);
            pendingNodes.Remove(last.Value.key);
            pending.RemoveLast();
        }
    }

    /// <summary>
    /// 把仍在队列中的视野中心瓦片提到队首（CamerPosToMap 逐帧调用，缩放时优先出图）。
    /// </summary>
    void Promote(string key)
    {
        LinkedListNode<TileRequest> node;
        if (!pendingNodes.TryGetValue(key, out node) || node.List == null || node == pending.First)
        {
            return;
        }
        pending.Remove(node);
        pending.AddFirst(node);
    }

    IEnumerator LoadWorker()
    {
        while (true)
        {
            if (pending.Count == 0)
            {
                yield return null;
                continue;
            }

            TileRequest req = pending.First.Value;
            pending.RemoveFirst();
            pendingNodes.Remove(req.key);

            // 切层后已出队但尚未下载的请求直接丢弃，避免把旧层瓦片挂到
            // 已隐藏的分组下、并占用 LRU 名额。
            if (req.level != nowLevel)
            {
                pendingKeys.Remove(req.key);
                continue;
            }

            // pendingKeys 在整个请求（含重试）期间保留，防止同一瓦片重复入队
            yield return LoadTile(req);
            pendingKeys.Remove(req.key);
        }
    }

    /// <summary>
    /// 一个 worker 同时只处理一个瓦片，N 个 worker 即并发上限。
    /// 排队中不创建任何 Unity 对象，避免 2048 个 GameObject / Mesh / Material 一次性常驻。
    /// </summary>
    IEnumerator LoadTile(TileRequest req)
    {
        // try 内赋值不构成"确定赋值"，先给初值避免 CS0165
        string url = "";
        string savePath = "";
        bool localHad = false;
        GameObject go = null;
        Material mat = null;
        bool prepared = true;

        try
        {
            url = string.Format(mapUrl, req.level, req.lat, req.lon);
            savePath = tempMapPath + req.level + "/" + req.lat + "/" + req.lon + ".jpg";

            if (Application.platform != RuntimePlatform.WebGLPlayer)
            {
                if (File.Exists(savePath))
                {
                    url = "file://" + savePath;
                    localHad = true;
                }
                string dir = tempMapPath + req.level + "/" + req.lat;
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            go = new GameObject(req.key);
            go.transform.parent = MapFas[req.level].transform;
            mat = new Material(material);
            mat.color = new Color(1, 1, 1, 0f);
            // 每个图层占一段连续队列（层内按 level 排序，每层最多 20 个槽位），
            // 避免原 level*40 让相邻 LOD 层级的队列值跨过另一图层区间、造成两图层交错。
            // renderQueueAdd：卫星 100（后画）、路网 50（先画）；配合下面正的 Offset，
            // 底图被推远、先画的路网占住较近深度，路网透明区才能透出卫星影像。
            mat.renderQueue = 2000 + renderQueueAdd + Mathf.Clamp(req.level - MinLevel, 0, 19);
            mat.SetFloat("_OffsetFactor", renderQueueAdd);
            mat.SetFloat("_OffsetUnits", renderQueueAdd);
            Earth.CreatMesh(go, mat, req.level, req.lat, req.lon);
        }
        catch (Exception e)
        {
            prepared = false;
            Debug.LogWarning("[MapLayer] 瓦片准备失败 " + req.key + "：" + e.Message);
            ReleaseTileObjects(go, mat);
        }

        // yield 语句不能放在 catch 块内，这里在普通流程里提前结束协程
        if (!prepared)
        {
            yield break;
        }

        bool success = false;
        for (int attempt = 0; attempt <= maxRetries && !success; attempt++)
        {
            using (var webRequest = UnityWebRequestTexture.GetTexture(url))
            {
                webRequest.certificateHandler = new WebRequestSkipCertificate();
                webRequest.timeout = RequestTimeoutSeconds;
                yield return webRequest.SendWebRequest();

                success = webRequest.result == UnityWebRequest.Result.Success;
                if (success)
                {
                    try
                    {
                        Texture2D texture2D = DownloadHandlerTexture.GetContent(webRequest);
                        texture2D.wrapMode = TextureWrapMode.Clamp;
                        mat.mainTexture = texture2D;
                        mat.color = Color.white;
                        WriteCache(savePath, webRequest.downloadHandler.data, localHad);
                    }
                    catch (Exception e)
                    {
                        success = false;
                        Debug.LogWarning("[MapLayer] 瓦片解析失败 " + req.key + "：" + e.Message);
                    }
                }
                else
                {
                    Debug.LogWarning("[MapLayer] 瓦片请求失败 " + req.key +
                                     "（第 " + (attempt + 1) + " 次）：" + webRequest.error + " url=" + url);
                }
            }

            if (!success && attempt < maxRetries)
            {
                yield return new WaitForSeconds(0.5f * (attempt + 1));
            }
        }

        if (success)
        {
            mapDic[req.key] = go;
            Touch(req.key);
            EvictIfNeeded();
        }
        else
        {
            // 不写入 mapDic：下次视野刷新时会重新入队，避免"一次失败永久空白"
            ReleaseTileObjects(go, mat);
        }
    }

    /// <summary>
    /// 原子落盘，避免中断留下 0 字节 .jpg 被 File.Exists 当成已缓存。
    /// </summary>
    static void WriteCache(string path, byte[] data, bool localHad)
    {
        if (Application.platform == RuntimePlatform.WebGLPlayer || localHad || data == null || data.Length == 0)
        {
            return;
        }
        string tmp = path + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogWarning("[MapLayer] 缓存写入失败 " + path + "：" + e.Message);
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
        }
    }

    #endregion

    #region LRU 与资源释放

    void Touch(string key)
    {
        LinkedListNode<string> node;
        if (lruNodes.TryGetValue(key, out node))
        {
            lruOrder.Remove(node);
            lruOrder.AddFirst(node);
        }
        else
        {
            lruNodes[key] = lruOrder.AddFirst(key);
        }
    }

    void EvictIfNeeded()
    {
        // 视野内瓦片每帧被 Touch 移到队首，离场/旧层的自然落到队尾被回收。
        while (mapDic.Count > maxCachedTiles && lruOrder.Count > 0)
        {
            string key = lruOrder.Last.Value;
            lruOrder.RemoveLast();
            lruNodes.Remove(key);

            GameObject go;
            if (mapDic.TryGetValue(key, out go))
            {
                mapDic.Remove(key);
                ReleaseTileObjects(go, null);
            }
        }
    }

    /// <summary>
    /// 显式释放运行时 new 出来的 Material / Texture2D / Mesh。
    /// 这些不是场景序列化资源，Resources.UnloadUnusedAssets() 在被引用时无法回收。
    /// </summary>
    static void ReleaseTileObjects(GameObject go, Material mat)
    {
        if (go != null)
        {
            MeshFilter filter = go.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                DestroyObject(filter.sharedMesh);
            }

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.sharedMaterial != null && renderer.sharedMaterial != mat)
            {
                DestroyMaterial(renderer.sharedMaterial);
            }
            DestroyObject(go);
        }

        if (mat != null)
        {
            DestroyMaterial(mat);
        }
    }

    static void DestroyMaterial(Material mat)
    {
        if (mat.mainTexture != null)
        {
            DestroyObject(mat.mainTexture);
        }
        DestroyObject(mat);
    }

    static void DestroyObject(UnityEngine.Object obj)
    {
        if (obj == null)
        {
            return;
        }
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(obj);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(obj);
        }
    }

    #endregion

    #region 得到地图

    /// <summary>
    /// 得到地图瓦片。lat=行 row（URL 的 y），lon=列 col（URL 的 x）。
    /// </summary>
    void ReadMap(int lat, int lon, int level, bool priority = false)
    {
        string mapID = level + "&" + lat + "&" + lon;
        if (mapDic.ContainsKey(mapID))
        {
            Touch(mapID);   // 视野内的瓦片保持新鲜，不会被 LRU 误淘汰
            return;
        }
        if (pendingKeys.Contains(mapID))
        {
            if (priority)
            {
                Promote(mapID);
            }
            return;
        }

        Enqueue(new TileRequest
        {
            key = mapID,
            level = level,
            lat = lat,
            lon = lon
        }, priority);
    }

    #endregion
}
