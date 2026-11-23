#if OPENGL
#define SV_POSITION POSITION
#define VS_PROFILE vs_3_0
#define PS_PROFILE ps_3_0
#else
#define VS_PROFILE vs_4_0_level_9_1
#define PS_PROFILE ps_4_0_level_9_1
#endif
float4x4 ViewProjection;
float Daylight;
float Brightness;
float3 FogColor;
float WaterFog;
float FogStart;
float FogEnd;
float AlphaCutoff;
texture Texture;
sampler TextureSampler = sampler_state { Texture = <Texture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
struct VSInput { float4 Position : POSITION0; float4 Color : COLOR0; float2 Uv : TEXCOORD0; };
struct VSOutput { float4 Position : SV_POSITION; float4 Color : COLOR0; float2 Uv : TEXCOORD0; float Distance : TEXCOORD1; float CornerLight : TEXCOORD2; float3 Tint : TEXCOORD3; };
VSOutput VSMain(VSInput input)
{
    VSOutput output;
    output.Position=mul(input.Position,ViewProjection);
    // Perspective clip W is positive eye-space depth. Avoid a separately
    // optimized non-square View uniform on DesktopGL.
    output.Distance=output.Position.w;
    output.Color=input.Color;output.Uv=input.Uv;
    output.Tint=1;
    if(input.Uv.x>=1)
    {
        // RGB565 occupies the integer portion, outside the actual atlas UV.
        float2 packed=floor(input.Uv);
        float red=floor((packed.x-1)/64);
        output.Tint=float3(red/31, (packed.x-1-red*64)/63, packed.y/31);
        output.Uv=input.Uv-packed;
    }
    output.CornerLight=-1;
    // Smooth mesh vertices carry quarter light levels, marked by the otherwise
    // unused block-light byte range 64..120. Flat encoding/fragment math stays intact.
    if(input.Color.b>63.5/255)
    {
        float blockByte=floor(input.Color.b*255+.5);
        float sky=floor(input.Color.g*255+.5)*.25,block=(blockByte-64)*.25;
        float level=max(block,max(0,sky-(14-floor(Daylight+.00001))));
        float minimum=.025+.12*Brightness;
        output.CornerLight=input.Color.r*(minimum+(1-minimum)*pow(saturate(level/14),1.7));
    }
    return output;
}
float4 PSMain(VSOutput input) : COLOR0
{
    float4 tex=tex2D(TextureSampler,input.Uv);
    clip(tex.a*input.Color.a-AlphaCutoff);
    float3 rgb;
    if(input.CornerLight>=0)rgb=tex.rgb*input.CornerLight;
    else
    {
        float sky=floor(input.Color.g*255+.5),block=floor(input.Color.b*255+.5);
        float level=max(block,max(0,sky-(14-floor(Daylight+.00001))));
        float minimum=.025+.12*Brightness;
        float light=minimum+(1-minimum)*pow(saturate(level/14),1.7);
        rgb=tex.rgb*input.Color.r*light;
    }
    float fog=WaterFog>.5?1-exp(-max(0,input.Distance)*.1):saturate((input.Distance-FogStart)/(FogEnd-FogStart));
    return float4(lerp(rgb*input.Tint,FogColor,fog),tex.a*input.Color.a);
}
technique BlockLighting { pass P0 { VertexShader=compile VS_PROFILE VSMain(); PixelShader=compile PS_PROFILE PSMain(); } }
