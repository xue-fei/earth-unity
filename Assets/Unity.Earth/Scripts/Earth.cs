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

    public static double latRectify = 0.00012;
    public static double lonRectify = 3.5e-05;

    /// <summary>
    /// 得到经纬点
    /// </summary>
    /// <param name="unitlongiAngle">单位经度角</param>
    /// <param name="halfSubdivisions">一半的细分值</param>
    /// <param name="LatValue">纬度段</param>
    /// <param name="LonValue">经度段</param>
    public static Vector3 GetLatLonPinot(double unitlongiAngle, double halfSubdivisions, int LatValue, int LonValue)
    {
        //赤道与本初子午线交点
        Vector3 zeroPoint = new Vector3(radius, 0, 0);
        //得到经度
        double longiAngle = -unitlongiAngle * LonValue;
        //墨卡托Y值 //这里把墨卡托的Y值原点从赤道与本初子午线交点移动到左上角
        double mercatorY = (halfEquatorCircle / halfSubdivisions) * (halfSubdivisions - LatValue);
        return GetLatitude(mercatorY, longiAngle, zeroPoint);
    }

    public static Vector3 GetLatitude(double mercatorY, double longiAngle, Vector3 zeroPoint)
    {
        //新建变换矩阵
        Matrix4x4 matRot = new Matrix4x4();
        double latitudeAngle = mercatorTolat(mercatorY);
        Rectify(ref longiAngle, ref latitudeAngle, 1);
        //转四元数
        Quaternion quaternion = Quaternion.Euler(new Vector3(0, float.Parse(longiAngle.ToString()), float.Parse((latitudeAngle).ToString())));
        matRot.SetTRS(Vector3.zero, quaternion, new Vector3(1, 1, 1));
        return matRot.MultiplyPoint3x4(zeroPoint);
    }

    /// <summary>
    /// 墨卡托转纬度
    /// </summary>
    /// <param name="mercatorY"></param>
    /// <returns></returns>
    public static double mercatorTolat(double mercatorY)
    {
        double y = mercatorY / halfEquatorCircle * 180.0000000d;
        y = 180.00000d / Math.PI * (2 * Math.Atan(Math.Exp(y * Math.PI / 180.000000d)) - Math.PI * 0.5000000d);
        return y;
    }

    public static void Rectify(ref double longiAngle, ref double latitudeAngle, int i)
    {
        // Debug.Log("longiAngle0=" + longiAngle);
        longiAngle += (longiAngle * lonRectify) * i;
        //Debug.Log("longiAngle1=" + longiAngle);
        latitudeAngle += (latitudeAngle * latRectify) * i;
    }

    public static void CreatMesh(GameObject go, Material mat, double unitlongiAngle, double halfSubdivisions, int lat, int lon)
    {
        Vector3[] points = new Vector3[4];
        points[0] = GetLatLonPinot(unitlongiAngle, halfSubdivisions, lat, lon);
        points[1] = GetLatLonPinot(unitlongiAngle, halfSubdivisions, lat + 1, lon);
        points[2] = GetLatLonPinot(unitlongiAngle, halfSubdivisions, lat + 1, lon + 1);
        points[3] = GetLatLonPinot(unitlongiAngle, halfSubdivisions, lat, lon + 1);
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
        filter.mesh.RecalculateNormals();
        filter.mesh.RecalculateBounds();
        MeshRenderer renderer = go.AddComponent<MeshRenderer>();
        renderer.material = mat;
    }

    /// <summary>
    /// 根据经纬度计算球面坐标
    /// </summary>
    /// <param name="longitude">经度</param>
    /// <param name="latitude">纬度</param>
    /// <returns></returns>
    public Vector3 GetSphericalCoordinates(double longitude, double latitude)
    {
        latitude = latitude * Mathf.PI / 180D;
        longitude = longitude * Mathf.PI / 180D;
        double x = radius * Mathf.Cos((float)latitude) * Mathf.Sin((float)longitude);
        double y = radius * Mathf.Sin((float)latitude);
        double z = -radius * Mathf.Cos((float)latitude) * Mathf.Cos((float)longitude);
        return new Vector3((float)x, (float)y, (float)z);
    }

    public static Vector3 ToLocationPos(float lon, float lat)
    {
        Quaternion quaternion = Quaternion.Euler(lat, 90 - lon, (float)0);
        Vector3 vector = ((Vector3)(quaternion * new Vector3((float)0, (float)0, -Earth.radius)));
        return vector;
    }

    public static int GetLatValue(double LatAngle, double halfSubdivisions)
    {
        double mercatorY = latToMercator(LatAngle);
        int LatValue1 = (int)Math.Ceiling(((mercatorY * halfSubdivisions) / halfEquatorCircle));
        //int LatValue = (int)(halfSubdivisions - (LatAngle * halfSubdivisions / 180));
        return (int)(halfSubdivisions - LatValue1);
    }

    public static double GetAngle(Vector3 from, Vector3 to)
    {
        double fromMagni = from.magnitude;
        //Debug.Log(fromMagni);
        double toMagni = to.magnitude;
        // Debug.Log(toMagni);
        double fromToMagni = (to - from).magnitude;
        // Debug.Log(fromToMagni);
        double Angle = (Math.Pow(fromMagni, 2) + Math.Pow(toMagni, 2) - Math.Pow(fromToMagni, 2)) / (2 * fromMagni * toMagni);
        return (Math.Acos(Angle) * (180 / Math.PI));
    }

    /// <summary>
    /// 纬度转墨卡托
    /// </summary>
    /// <param name="lat"></param>
    /// <returns></returns>
    public static double latToMercator(double lat)
    {
        double y = Math.Log(Math.Tan((90 + lat) * Math.PI / 360.00000000d)) / (Math.PI / 180.000000000d);
        y = y * halfEquatorCircle / 180.0000000000d;
        return y;
    }
}