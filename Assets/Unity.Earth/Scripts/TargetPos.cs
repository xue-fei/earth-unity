using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

public class TargetPos : MonoBehaviour
{
    public CameraControl cameraControl;
    public EarthManager earthManager;
    public InputField lat;
    public InputField lon;
    public Transform prop;
    [Tooltip("标记物沿球面法线抬离的高度（单位与 Earth.radius 一致：万米）。只用于避免几何半埋；地表深度闪烁是 near/far 比例过大导致，需另行调整相机裁剪面。")]
    public float propHeightOffset = 1f;
    [Tooltip("输入框里的经纬度是否为 WGS84。开启时，使用高德(GCJ-02)渠道会自动转换；若你的坐标本来就是 GCJ-02 请关闭。")]
    public bool inputIsWgs84 = true;

    /// <summary>定位按钮回调。</summary>
    public void CameraToTarget()
    {
        RefreshPosition();
    }

    /// <summary>
    /// 按当前输入框重新定位相机与标记物。
    /// 渠道切换后由 EarthManager.SetChannel 调用，用新渠道的坐标系重算 GCJ-02 偏移。
    /// </summary>
    public void RefreshPosition()
    {
        float lonValue;
        float latValue;
        if (!float.TryParse(lon.text, NumberStyles.Float, CultureInfo.InvariantCulture, out lonValue) ||
            !float.TryParse(lat.text, NumberStyles.Float, CultureInfo.InvariantCulture, out latValue))
        {
            Debug.LogWarning("[TargetPos] 经纬度输入无效，请输入数字（经度 -180~180，纬度 -90~90）。");
            return;
        }
        if (lonValue < -180f || lonValue > 180f || latValue < -90f || latValue > 90f)
        {
            Debug.LogWarning("[TargetPos] 经纬度超出范围：经度 -180~180，纬度 -90~90。");
            return;
        }

        // 球面就是瓦片服务的坐标系：高德瓦片按 GCJ-02 编号，ArcGIS 按 WGS84。
        // 用户输入若为 WGS84，需先转到 GCJ-02 才能与高德影像对位（中国境内约偏移几百米）；
        // GPSUtil 内部对境外坐标不转换。
        double lonOnMap = lonValue;
        double latOnMap = latValue;
        if (inputIsWgs84 && earthManager != null && earthManager.NeedsGcj02)
        {
            double gcjLat;
            double gcjLon;
            GPSUtil.WGS84_to_GCJ02(latValue, lonValue, out gcjLat, out gcjLon);
            latOnMap = gcjLat;
            lonOnMap = gcjLon;
        }

        cameraControl.ToLocation((float)lonOnMap, (float)latOnMap, 2f);
        // 抬离球面，避免标记物半埋以及和地表网格争夺深度
        Vector3 dir = Earth.ToLocationPos((float)lonOnMap, (float)latOnMap).normalized;
        prop.position = dir * (Earth.radius + propHeightOffset);
    }
}
