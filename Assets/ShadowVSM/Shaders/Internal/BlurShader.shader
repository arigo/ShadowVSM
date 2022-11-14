Shader "Hidden/ShadowVSM/BlurShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ BLUR_Y BLUR_LINEAR_AND_SQUARE_PART

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            struct f2a
            {
                float4 color0 : COLOR0;
                float4 color1 : COLOR1;
            };

            sampler _MainTex;
            float4 _Color;
            float2 BlurPixelSize;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

/* can be defined or undefined here */
//#define LARGE_BLUR

#ifdef LARGE_BLUR
    #define LOOP      [unroll] for (int delta = -10; delta <= 10; delta++)
    #define COEFF(x)  (x) * Coefficients[abs(delta)]
#else
    #define LOOP      [unroll] for (int delta = -1; delta <= 1; delta++)
    #define COEFF(x)  x
#endif

#if BLUR_LINEAR_AND_SQUARE_PART
            f2a
#else
            float4
#endif
                frag (v2f i) : SV_Target
            {
                float Coefficients[11] = { 0.14107424,
                    0.132526984, 0.109868729, 0.080381679, 0.051898313, 0.029570767,
                    0.014869116, 0.00659813, 0.002583865, 0.00089296, 0.000272337 };

#if BLUR_Y
                /* first pass: from the RGBA texture produced by shadowCam to the intermediate RGBA texture */
                float3 col = float3(0, 0, 0);
                LOOP
                {
                    float2 index = i.uv;
                    index.y += BlurPixelSize * delta;
                    col += COEFF(tex2D(_MainTex, index).rgb);
                }
                return float4(col, 0);
#elif BLUR_LINEAR_AND_SQUARE_PART
                /* pass 2: blur the intermediate RGBA texture horizontally into the final R textures */
                float3 col = float3(0, 0, 0);
                LOOP
                {
                    float2 index = i.uv;
                    index.x += BlurPixelSize * delta;
                    col += COEFF(tex2D(_MainTex, index).rgb);
                }
                if (col.z == 0) col = float3(64, 64, 1);
                float2 result = col.xy / col.z;
                f2a OUT;
                OUT.color0 = float4(result.x, 0, 0, 0);
                OUT.color1 = float4(result.y, 0, 0, 0);
                return OUT;
#else
                return _Color;
#endif
            }
            ENDCG
        }
    }
}
