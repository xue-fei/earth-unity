using UnityEngine;
using UnityEngine.UI;

public class EarthManager : MonoBehaviour
{
    public GameObject earth;
    public Material material;
    public MapLayer mapLayer;   // 保留原有场景序列化引用

    [Header("图层 Toggle（下拉切换时联动勾选状态）")]
    public Toggle satelliteToggle;
    public Toggle roadToggle;
    [Header("渠道切换后按当前输入框重定位")]
    public TargetPos targetPos;

    // 与渠道下拉默认 m_Value=1（高德）保持一致
    MapChannel channel = MapChannel.AutoNavi;
    bool satelliteOn = true;
    bool roadOn = true;

    /// <summary>
    /// 当前瓦片渠道是否使用 GCJ-02（火星坐标）。高德是，ArcGIS(WGS84) 不是。
    /// TargetPos 据此决定要不要把输入的 WGS84 坐标转换后再定位。
    /// </summary>
    public bool NeedsGcj02
    {
        get { return channel == MapChannel.AutoNavi; }
    }

    void Start()
    {
        Application.targetFrameRate = 60;
        // 以场景中 Toggle 的初始勾选为准，避免脚本默认值与 UI 不一致
        if (satelliteToggle != null)
        {
            satelliteOn = satelliteToggle.isOn;
        }
        if (roadToggle != null)
        {
            roadOn = roadToggle.isOn;
        }
        RebuildEarth();
    }

    /// <summary>渠道下拉：0=ArcGis，1=高德。切换会重建整个 Earth（重新拉取瓦片，本地缓存照常复用）。</summary>
    public void SetChannel(int index)
    {
        MapChannel next = index == 0 ? MapChannel.ArcGIS : MapChannel.AutoNavi;
        if (next == channel && earth != null)
        {
            return;
        }
        channel = next;
        RebuildEarth();
        if (targetPos != null)
        {
            // 用新渠道的坐标系重算标记物与相机位置，避免沿用上一个渠道的 GCJ-02 偏移
            targetPos.RefreshPosition();
        }
    }

    /// <summary>类型下拉：0=卫星、1=路网。只切换显隐并联动两个 Toggle，不重建、不碰 Offset。</summary>
    public void SetMapType(int index)
    {
        satelliteOn = index == 0;
        roadOn = index != 0;
        if (satelliteToggle != null)
        {
            satelliteToggle.SetIsOnWithoutNotify(satelliteOn);
        }
        if (roadToggle != null)
        {
            roadToggle.SetIsOnWithoutNotify(roadOn);
        }
        ApplyVisibility();
    }

    /// <summary>「卫星」Toggle。</summary>
    public void SetSatelliteVisible(bool on)
    {
        satelliteOn = on;
        ApplyVisibility();
    }

    /// <summary>「路网」Toggle。</summary>
    public void SetRoadMapVisible(bool on)
    {
        roadOn = on;
        ApplyVisibility();
    }

    void RebuildEarth()
    {
        if (earth != null)
        {
            earth.SetActive(false);   // 立即停掉旧层的 FixedUpdate
            Destroy(earth);           // 帧末触发 MapLayer.OnDestroy，清理协程与运行时资源
        }
        earth = new GameObject("Earth");

        // 卫星层始终 100（后画 + 较大 Offset 推远），路网层始终 50（先画占近深度）。
        // 两者不能交换，否则不透明的卫星层会盖掉路网。
        mapLayer = earth.AddComponent<MapLayer>();
        mapLayer.Init(earth, channel, MapType.Satellite, material, 100);

        // ArcGIS 只有 World_Imagery，没有路网服务；只建单层，避免重复请求同一套影像瓦片
        if (channel == MapChannel.AutoNavi)
        {
            MapLayer road = earth.AddComponent<MapLayer>();
            road.Init(earth, channel, MapType.RoadMap, material, 50);
        }
        ApplyVisibility();
    }

    void ApplyVisibility()
    {
        SetLayerVisible(MapType.Satellite, satelliteOn);
        SetLayerVisible(MapType.RoadMap, roadOn);
    }

    void SetLayerVisible(MapType type, bool on)
    {
        if (earth == null)
        {
            return;
        }
        // MapLayer.Init 里创建的子物体名即 mapType.ToString()
        Transform t = earth.transform.Find(type.ToString());
        if (t != null)
        {
            t.gameObject.SetActive(on);
        }
    }

    private void OnDestroy()
    {
        Resources.UnloadUnusedAssets();
    }
}
