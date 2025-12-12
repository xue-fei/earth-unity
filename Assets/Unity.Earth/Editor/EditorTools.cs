using UnityEditor;
using UnityEngine;

public class EditorTools : Editor
{
    [MenuItem("工具/打开沙盒文件夹", false, 0)]
    static void OpenPersistentDataPath()
    {
        System.Diagnostics.Process.Start(@Application.persistentDataPath);
    }

    [MenuItem("工具/Clear PlayerPrefs")]
    public static void ClearPlayerPrefss()
    {
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("PlayerPrefs 已清除！");
    }
}