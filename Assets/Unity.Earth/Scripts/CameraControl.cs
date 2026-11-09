using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class CameraControl : MonoBehaviour
{
    public float speed = 150;
    /// <summary>
    /// 距离
    /// </summary>
    public float distance;
    /// <summary>
    /// 相机距海平面高度
    /// </summary>
    public float height;
    float distanceMin = 0.04f;
    float distanceMax = 16000f;
    public Vector3 currentEulerAngles;
    public Text text;

    Coroutine locationRoutine;
    bool isAnimating;
    Camera cam;

    void Awake()
    {
        cam = GetComponent<Camera>();
    }

    // Start is called before the first frame update
    void Start()
    {
        // 以场景中摆好的位姿初始化累加角，避免启动时被 Inspector 里的旧值覆盖（瞬移）。
        // 约定：currentEulerAngles.x 存 yaw、.y 存 pitch，与 PointRot 一致。
        Vector3 e = transform.rotation.eulerAngles;
        currentEulerAngles = new Vector3(e.y, e.x, 0f);
        distance = transform.position.magnitude;
    }

    // Update is called once per frame
    void Update()
    {
        distance = Vector3.Distance(Vector3.zero, transform.position);

        if (isAnimating)
        {
            // 定位动画期间屏蔽拖动与滚轮，避免两处同时写 transform
            return;
        }
        if (Input.GetMouseButton(0))
        {
            PointRot();
        }
        if (Input.GetAxis("Mouse ScrollWheel") < 0 && distance < distanceMax)
        {
            transform.Translate(Vector3.back * Speed());
        }
        if (Input.GetAxis("Mouse ScrollWheel") > 0 && distance - Earth.radius >= distanceMin)
        {
            transform.Translate(Vector3.forward * Speed());
        }
    }

    void LateUpdate()
    {
        height = distance - Earth.radius;
        text.text = "高度：" + (height * 10f).ToString("F2") + "千米";
        UpdateClipPlanes();
    }

    /// <summary>
    /// 按相机到地表的距离动态设置裁剪面。
    /// 场景里静态的 near=0.01 / far=2000000 比值达 2e8，24 位深度缓冲在地表处
    /// 只能分辨约 200 个单位，双层瓦片与标记物必然 z-fighting。
    /// 这里把 far 收敛到「相机到地球最远点」，near 跟随地表距离，运行时逐帧覆盖，
    /// 因此无需改动 Earth.unity 里那两个静态占位值。
    /// </summary>
    void UpdateClipPlanes()
    {
        if (cam == null)
        {
            return;
        }
        float dist = transform.position.magnitude;
        float surfaceDist = Mathf.Max(0.001f, dist - Earth.radius);
        cam.nearClipPlane = Mathf.Max(0.01f, surfaceDist * 0.1f);
        cam.farClipPlane = dist + Earth.radius + 10f;
    }

    float Speed()
    {
        float ratio = (transform.position.magnitude - Earth.radius) / (Earth.radius);
        return speed * (ratio);
    }

    void PointRot()
    {
        currentEulerAngles.z = 0;
        currentEulerAngles.x += Input.GetAxis("Mouse X") * Speed() * 0.01f;
        currentEulerAngles.y -= Input.GetAxis("Mouse Y") * Speed() * 0.01f;
        distance = Vector3.Distance(transform.position, Vector3.zero);
        Quaternion quaternion = Quaternion.Euler(currentEulerAngles.y, currentEulerAngles.x, 0);
        Vector3 vector = quaternion * new Vector3(0, 0, -distance);
        transform.rotation = quaternion;
        transform.position = vector;
    }

    /// <summary>
    /// 按经纬度旋转相机（保持当前距地心距离）。重复调用会中断上一段动画，不会叠加。
    /// </summary>
    public void ToLocation(float lon, float lat, float time)
    {
        if (locationRoutine != null)
        {
            StopCoroutine(locationRoutine);
            locationRoutine = null;
        }
        locationRoutine = StartCoroutine(MoveToLocation(lon, lat, Mathf.Max(0.01f, time)));
    }

    IEnumerator MoveToLocation(float lon, float lat, float time)
    {
        isAnimating = true;
        Quaternion from = transform.rotation;
        Quaternion to = Quaternion.Euler(lat, 90f - lon, 0f);
        float dist = transform.position.magnitude;

        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime / time;
            t = Mathf.Min(t, 1f);
            // Slerp 走最短弧，避免跨 0/360° 时绕远路
            Quaternion q = Quaternion.Slerp(from, to, t);
            transform.rotation = q;
            transform.position = q * new Vector3(0f, 0f, -dist);
            yield return null;
        }

        Vector3 e = transform.rotation.eulerAngles;
        currentEulerAngles = new Vector3(e.y, e.x, 0f);
        distance = transform.position.magnitude;
        isAnimating = false;
        locationRoutine = null;
    }
}
