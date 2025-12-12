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

    // Start is called before the first frame update
    void Start()
    {

    }

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

        EarthStart(MinLevel);
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        if (Input.GetMouseButton(0) || Input.GetAxis("Mouse ScrollWheel") != 0)
        {
            CamerPosToMap();
        }
    }

    private void Update()
    {

    }

    Vector3 zeroPoint;
    Vector3 camerVec;
    float camDis;
    double LonAngle;
    double LatAngle;
    /// <summary>
    /// 通过相机位置调取地图
    /// </summary>
    void CamerPosToMap()
    {
        zeroPoint = new Vector3(Earth.radius, 0, 0);
        camerVec = mainCamera.transform.position - Vector3.zero;
        //camDis = camerVec.magnitude;
        camDis = Vector3.Distance(Vector3.zero, mainCamera.transform.position);//camerVec.magnitude;
        //相机经度
        LonAngle = Vector3.Angle(zeroPoint, new Vector3(camerVec.x, 0, camerVec.z));
        if (LonAngle < 0)
        {
            LonAngle = 360 + LonAngle;
        }
        //相机维度
        LatAngle = Vector3.Angle(new Vector3(camerVec.x, 0, camerVec.z), camerVec);

        if (camerVec.y < 0)
        {
            LatAngle = -LatAngle;
        }
        Earth.Rectify(ref LonAngle, ref LatAngle, 3);
        //Debug.Log("经度:" + LonAngle + "维度:" + LatAngle);
        NowLevel = altitudeToZoom((camDis - Earth.radius) * 10000);
        NowLevel = Math.Clamp(NowLevel, MinLevel, MaxLevel);

        double subdivisions;
        double unitlongiAngle;
        double halfSubdivisions;
        ReturnSubParam(NowLevel, out subdivisions, out unitlongiAngle, out halfSubdivisions);
        int lon = (int)subdivisions - (int)(LonAngle / unitlongiAngle);
        lon = Math.Clamp(lon, 0, (int)subdivisions - 1);
        int lat = Earth.GetLatValue(LatAngle, halfSubdivisions);
        lat = Math.Clamp(lat, 0, (int)subdivisions - 1);
        //Debug.Log("lon:"+ lon+" lat:"+lat);
        int length = (int)Math.Sqrt(NowLevel);
        for (int i = -length; i < length; i++)
        {
            for (int j = -length; j < length; j++)
            {
                if (lon < subdivisions - 1 && lon + i >= 0 && lat < subdivisions - 1 && lat + j >= 0)
                {
                    ReadMap(lat + j, lon + i, NowLevel);
                }
            }
        }
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
                foreach (var item in MapFas)
                {
                    long i = Math.Abs(value - item.Key);
                    if (i > 1)
                    {
                        item.Value.SetActive(false);
                    }
                    else
                    {
                        item.Value.SetActive(true);
                    }
                    //item.Value.SetActive(item.Key == value);
                }
                nowLevel = value;
            }
        }
    }

    #region 地球顶点创建

    public void EarthStart(int level)
    {
        double subdivisions;
        double unitlongiAngle;
        double halfSubdivisions;
        //赤道细分2的指数倍
        ReturnSubParam(level, out subdivisions, out unitlongiAngle, out halfSubdivisions);
        NowLevel = level;
        Debug.Log("subdivisions:" + subdivisions);
        Debug.Log("halfSubdivisions:" + halfSubdivisions);
        for (int lon = 0; lon < subdivisions; lon++)
        {
            for (int lat = 0; lat < subdivisions; lat++)
            {
                //Debug.Log("lat:" + lat + " lon:" + lon);
                ReadMap(unitlongiAngle, halfSubdivisions, lat, lon, level);
            }
        }
    }

    /// <summary>
    /// 返回细分参数
    /// </summary>
    /// <param name="level">层级</param>
    /// <param name="subdivisions">分段</param>
    /// <param name="unitlongiAngle">经度单位角度</param>
    /// <param name="halfSubdivisions">一半的分段</param>
    void ReturnSubParam(int level, out double subdivisions, out double unitlongiAngle, out double halfSubdivisions)
    {
        //细分2的level次方
        subdivisions = Math.Pow(2, level);
        unitlongiAngle = (360.00000000000d / (subdivisions * 1.000000d));
        //Debug.Log(unitlongiAngle);
        halfSubdivisions = subdivisions * 0.500000d;
    }

    #endregion

    #region 得到地图
    /// <summary>
    /// 得到地图瓦片
    /// </summary>
    /// <param name="unitlongiAngle">纬度</param>
    /// <param name="halfSubdivisions">一半的细分值</param>
    /// <param name="LatValue">纬度段</param>
    /// <param name="LonValue">经度段</param>
    /// <param name="Level">层级</param>
    void ReadMap(int LatValue, int LonValue, int Level)
    {
        double subdivisions;
        double unitlongiAngle;
        double halfSubdivisions;
        ReturnSubParam(Level, out subdivisions, out unitlongiAngle, out halfSubdivisions);
        //Debug.Log("LonValue:" + LonValue);
        ReadMap(unitlongiAngle, halfSubdivisions, LatValue, LonValue, Level);
    }

    void ReadMap(double unitlongiAngle, double halfSubdivisions, int lat, int lon, int level)
    {
        string mapID = level + "&" + lat + "&" + lon;
        if (!mapDic.ContainsKey(mapID))
        {
            GameObject go = new GameObject(mapID);
            go.transform.parent = MapFas[level].transform;
            mapDic.Add(mapID, go);
            LoadMap(go, unitlongiAngle, halfSubdivisions, lat, lon, level);
        }
    }

    void LoadMap(GameObject go, double unitlongiAngle, double halfSubdivisions, int lat, int lon, int level)
    {
        //第一个参数是层级，第二个是纬度，第三个是经度 
        string url = string.Format(mapUrl, level, lat, lon);
        bool localHad = false;
        if (Application.platform != RuntimePlatform.WebGLPlayer)
        {
            if (File.Exists(tempMapPath + level + "/" + lat + "/" + lon + ".jpg"))
            {
                url = "file://" + tempMapPath + level + "/" + lat + "/" + lon + ".jpg";
                localHad = true;
            }
            if (!Directory.Exists(tempMapPath + level + "/" + lat))
            {
                Directory.CreateDirectory(tempMapPath + level + "/" + lat);
            }
        }
        Material mat = new Material(material);
        mat.color = new Color(1, 1, 1, 0f);
        mat.renderQueue = 2000 + level * 40 + renderQueueAdd;//调整渲染列队
        mat.SetFloat("_OffsetFactor", renderQueueAdd);
        mat.SetFloat("_OffsetUnits", renderQueueAdd);
        Earth.CreatMesh(go, mat, unitlongiAngle, halfSubdivisions, lat, lon);

        string savePath = tempMapPath + level + "/" + lat + "/" + lon + ".jpg";
        StartCoroutine(GetTexture(url, localHad, savePath, (texture2D) =>
        {
            if (texture2D)
            {
                texture2D.wrapMode = TextureWrapMode.Clamp;
                mat.mainTexture = texture2D;
                mat.color = Color.white;
            }
        }));
    }

    IEnumerator GetTexture(string url, bool localHad, string savePath, Action<Texture2D> action)
    {
        yield return new WaitForEndOfFrame();
        using (var webRequest = UnityWebRequestTexture.GetTexture(url))
        {
            webRequest.certificateHandler = new WebRequestSkipCertificate();
            webRequest.timeout = 15000;
            yield return webRequest.SendWebRequest();
            if (webRequest.result == UnityWebRequest.Result.Success)
            {
                Texture2D texture2D = DownloadHandlerTexture.GetContent(webRequest);
                if (action != null)
                {
                    action(texture2D);
                }
                if (Application.platform != RuntimePlatform.WebGLPlayer)
                {
                    if (!localHad)
                    {
                        File.WriteAllBytesAsync(savePath, webRequest.downloadHandler.data);
                    }
                }
                webRequest.Dispose();
            }
            else
            {
                Debug.Log(webRequest.error);
            }
        }
    }
    #endregion
}