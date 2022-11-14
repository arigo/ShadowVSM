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
            #pragma multi_compile _ VSM_DRAW_TRANSPARENT_SHADOWS

#ifdef VSM_DRAW_TRANSPARENT_SHADOWS
            #include "Dither Functions.cginc"
#endif
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
                #if defined(VSM_DRAW_TRANSPARENT_SHADOWS)
                ditherClip(i.vertex, col.a);
                #else
                if (col.a < 0.5) discard;
                #endif

                return vsm_depth_frag(i.vertex);
            }
            ENDCG
        }
    }
}
