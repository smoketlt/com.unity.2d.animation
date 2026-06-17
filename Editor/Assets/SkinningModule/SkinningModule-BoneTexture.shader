Shader "Hidden/SkinningModule-BoneTexture"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Opacity ("Opacity", Float) = 1
        _VertexColorBlend ("VertexColorBlend", Float) = 0
        _Color ("Tint", Color) = (1,1,1,1)
    }

    CGINCLUDE
    #pragma vertex vert
    #pragma fragment frag
    #pragma target 2.0

    #include "UnityCG.cginc"

    struct appdata_t
    {
        float4 vertex : POSITION;
        fixed4 color : COLOR;
        float2 texcoord : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct v2f
    {
        float4 vertex : SV_POSITION;
        fixed4 color : COLOR;
        float2 texcoord : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    sampler2D _MainTex;
    uniform float4 _MainTex_ST;
    fixed4 _Color;
    uniform fixed _Opacity;
    uniform fixed _VertexColorBlend;
    uniform bool _AdjustLinearForGamma;

    v2f vert(appdata_t v)
    {
        v2f o;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.vertex = UnityObjectToClipPos(v.vertex);
        o.color = v.color;
        o.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
        return o;
    }

    fixed4 frag(v2f i) : SV_Target
    {
        fixed4 col = tex2D(_MainTex, i.texcoord) * _Color;

        if (_AdjustLinearForGamma)
            col.rgb = LinearToGammaSpace(col.rgb);

        col = lerp(col, i.color, _VertexColorBlend);
        col.a *= _Opacity;
        return col;
    }
    ENDCG

    SubShader
    {
        Tags { "ForceSupported" = "True" }

        Lighting Off
        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            ENDCG
        }
    }
}
