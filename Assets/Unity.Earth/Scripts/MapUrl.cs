
/// <summary>
/// 瓦片服务地址。两个地址目前都只用于开发 / 演示，正式交付前必须评估授权。
///
/// - ArcGIS World_Imagery：Esri 服务条款要求保留数据来源署名，且对调用量、商用有许可约束。
/// - 高德 wprd03.is.autonavi.com/appmaptile：无 key 的未授权公开接口，无 SLA，
///   随时可能变更或封禁，不适合政企交付。建议换成有授权的天地图或自有 WMTS。
///
/// 替换入口：只改下面的 URL 模板即可。占位符 {0}=z(level)、{1}=y(row)、{2}=x(col)，
/// 与 MapLayer.LoadTile 里 string.Format(mapUrl, level, lat, lon) 的顺序一致。
/// 坐标系：高德用 GCJ-02、ArcGIS 用 WGS84。TargetPos 已接入 GPSUtil，
/// 输入框默认按 WGS84 解释，切到高德渠道时自动转 GCJ-02（可用 inputIsWgs84 关闭）。
/// 换成其它 GCJ-02 服务时，需同步维护 EarthManager.NeedsGcj02 的渠道判定。
/// </summary>
public class MapUrl
{
    public static string ArcGIS = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{0}/{1}/{2}";

    /// <summary>
    /// style 6卫星 7行政区划 8路网
    /// </summary>
    public static string AutoNavi = "https://wprd03.is.autonavi.com/appmaptile?style=6&x={2}&y={1}&z={0}";
}