using System;
using UnityEngine;

public class Earth
{
    /// <summary>
    /// 地球赤道半径6378137米
    /// 单位万米
    /// </summary>
    public static float radius = 637.8137f;

    /// <summary>
    /// 地球赤道周长的一半 赤道周长PI*r = 20037508.3427892
    /// </summary>
    public static double halfEquatorCircle = 20037508.3427892;

    /// <summary>
    /// 全项目唯一的「地理经纬度 → 球面点」映射。
    /// 与原有 ToLocationPos 完全等价（已数值验证，误差 0），沿用其轴向约定：
    /// lon=0 落在 -X，lat=90 落在 +Y，lon 增大时向 -Z 方向旋转。
    /// 瓦片网格、定位标记、相机反解必须共用此函数，避免再出现多套相位不同的球面公式。
    /// </summary>
    public static Vector3 GeoToPoint(double lonDeg, double latDeg)
    {
        double lon = lonDeg * Math.PI / 180.0;
        double lat = latDeg * Math.PI / 180.0;
        double cosLat = Math.Cos(lat);
        return new Vector3(
            (float)(-radius * cosLat * Math.Cos(lon)),
            (float)(radius * Math.Sin(lat)),
            (float)(-radius * cosLat * Math.Sin(lon)));
    }

    /// <summary>
    /// WebMercator 归一化 y（0=最北，1=最南）→ 地理纬度（度）。
    /// 标准 XYZ 瓦片方案中 row=0 对应 +85.05112878°，row=n 对应 -85.05112878°。
    /// </summary>
    public static double MercatorY01ToLat(double y01)
    {
        double latRad = 2.0 * Math.Atan(Math.Exp(Math.PI * (1.0 - 2.0 * y01))) - Math.PI * 0.5;
        return latRad * 180.0 / Math.PI;
    }

    /// <summary>
    /// 瓦片 (col,row) 的角点球面坐标。
    /// u=0/1 → 西/东边界；v=0/1 → 北/南边界。
    /// col 与瓦片 URL 的 x 一致、row 与 y 一致，均为标准 WebMercator 索引。
    /// </summary>
    public static Vector3 TileCorner(int level, int col, int row, int u, int v)
    {
        double n = Math.Pow(2.0, level);
        double lonDeg = -180.0 + 360.0 * (col + u) / n;
        double latDeg = MercatorY01ToLat((row + v) / n);
        return GeoToPoint(lonDeg, latDeg);
    }

    /// <summary>
    /// 生成一个瓦片的四边形网格。
    /// 顶点顺序与 uv 对应：0 西北(0,1)、1 西南(0,0)、2 东南(1,0)、3 东北(1,1)。
    /// </summary>
    public static void CreatMesh(GameObject go, Material mat, int level, int row, int col)
    {
        Vector3[] points = new Vector3[4];
        points[0] = TileCorner(level, col, row, 0, 0);
        points[1] = TileCorner(level, col, row, 0, 1);
        points[2] = TileCorner(level, col, row, 1, 1);
        points[3] = TileCorner(level, col, row, 1, 0);
        int[] triangles = new int[6];
        Vector2[] uvs = new Vector2[4];
        uvs[0] = new Vector2(0, 1);
        uvs[1] = new Vector2(0, 0);
        uvs[2] = new Vector2(1, 0);
        uvs[3] = new Vector2(1, 1);
        triangles[0] = 1;
        triangles[1] = 0;
        triangles[2] = 3;
        triangles[3] = 3;
        triangles[4] = 2;
        triangles[5] = 1;
        MeshFilter filter = go.AddComponent<MeshFilter>();
        filter.mesh.vertices = points;
        filter.mesh.triangles = triangles;
        filter.mesh.uv = uvs;
        // Unlit/alpha-test shader 不读法线，跳过 RecalculateNormals 可省掉每瓦片一次 CPU 计算
        filter.mesh.RecalculateBounds();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.material = mat;
    }

    public static Vector3 ToLocationPos(float lon, float lat)
    {
        return GeoToPoint(lon, lat);
    }
}
