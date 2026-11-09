using UnityEngine.Networking;

/// <summary>
/// 跳过Web请求证书避免出现 报错：【SSL CA certificate error】 与 【Curl error 60: Cert verify failed: UNITYTLS_X509VERIFY_FLAG_USER_ERROR1】
///
/// 【安全警示】ValidateCertificate 无条件 return true 等于完全关闭 TLS 证书校验，
/// 会一并放行中间人攻击与伪造证书。仅限开发 / 内网联调环境使用。
/// 正式交付（尤其政企项目）前必须移除本类，并删除 MapLayer.LoadTile 中的
/// `webRequest.certificateHandler = new WebRequestSkipCertificate();`，
/// 改为修复服务端证书链或导入正确的 CA 根证书。
/// 本次按约定保留原有逻辑不变，仅补充警示说明。
/// </summary>
public class WebRequestSkipCertificate : CertificateHandler
{
    protected override bool ValidateCertificate(byte[] certificateData)
    {
        return true;
    }
}