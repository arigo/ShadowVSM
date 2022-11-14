Shader "Hidden/ShadowVSM/Depth" {
    Properties
    {
        _Color("Main Color", Color) = (1,1,1,1)
    }

    SubShader {
        Tags { "RenderType"="Opaque" }

        Pass
        {
            CGPROGRAM
            #include "UnityCG.cginc"
            #pragma vertex vert
            #pragma fragment frag

            #include "ShadowVSMCustomDepth.cginc"

            float4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                float4 col = _Color;
                clip(col.a - 0.799);   /* clip if more than 20% transparent */

                return vsm_depth_frag(i.vertex);
            }
            ENDCG
        }
    }
}
