// 由 Unity 内置 Soft Edge Unlit 精简而来：只保留一个 Pass。
// 原 shader 为植被设计两个 Pass（不透明部分 + 半透明软边），第二个 Pass 带
// RequireOption=SoftVegetation；本项目的瓦片是不透明 jpg 或带透明底的路网 png，
// 第二 Pass 对 jpg 会被 clip 全裁但仍照样发一次 draw call（约翻倍 DC）。
// 现在用单个 alpha-test Pass：jpg 全通过，路网 png 的透明底被裁掉、线条保留。
// MIT license (see Unity built-in shader source).

Shader "Legacy Shaders/Transparent/Cutout/Soft Edge Unlit" {
Properties {
    _Color ("Main Color", Color) = (1, 1, 1, 1)
    _MainTex ("Base (RGB) Alpha (A)", 2D) = "white" {}
    _Cutoff ("Base Alpha cutoff", Range (0,.9)) = .5
    _OffsetFactor ("Offset Factor", Float) = 0
    _OffsetUnits ("Offset Units", Float) = 0
}

SubShader {
    Tags { "Queue"="AlphaTest" "IgnoreProjector"="True" "RenderType"="TransparentCutout" }
    Lighting off
    Cull Off

    Pass {
        // 瓦片是球面四边形，需要写入深度做图层遮挡；加载中 _Color.a=0 由 clip 隐藏。
        ZWrite On
        Offset [_OffsetFactor], [_OffsetUnits]

        CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog

            #include "UnityCG.cginc"

            struct appdata_t {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f {
                float4 vertex : SV_POSITION;
                float2 texcoord : TEXCOORD0;
                UNITY_FOG_COORDS(1)
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed _Cutoff;
            fixed4 _Color;

            v2f vert (appdata_t v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                UNITY_TRANSFER_FOG(o, o.vertex);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                half4 col = _Color * tex2D(_MainTex, i.texcoord);
                clip(col.a - _Cutoff);
                UNITY_APPLY_FOG(i.fogCoord, col);
                return col;
            }
        ENDCG
    }
}

}
