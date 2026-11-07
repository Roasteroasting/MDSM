#if OPENGL
#define SV_POSITION POSITION
#define VS_PROFILE vs_3_0
#define PS_PROFILE ps_3_0
#else
#define VS_PROFILE vs_4_0_level_9_1
#define PS_PROFILE ps_4_0_level_9_1
#endif
float4x4 ViewProjection;
float4x4 View;
float Daylight;
float Brightness;
float3 FogColor;
float FogStart;
float FogEnd;
float AlphaCutoff;
texture Texture;
sampler TextureSampler = sampler_state { Texture = <Texture>; MinFilter = Point; MagFilter = Point; MipFilter = Point; AddressU = Clamp; AddressV = Clamp; };
struct VSInput { float4 Position : POSITION0; float4 Color : COLOR0; float2 Uv : TEXCOORD0; };
struct VSOutput { float4 Position : SV_POSITION; float4 Color : COLOR0; float2 Uv : TEXCOORD0; float Distance : TEXCOORD1; };
VSOutput VSMain(VSInput input)
{
    VSOutput output;
    output.Position=mul(input.Position,ViewProjection);
    output.Distance=-mul(input.Position,View).z;
    output.Color=input.Color;output.Uv=input.Uv;return output;
}
float4 PSMain(VSOutput input) : COLOR0
{
    float4 tex=tex2D(TextureSampler,input.Uv);
    clip(tex.a*input.Color.a-AlphaCutoff);
    float sky=floor(input.Color.g*255+.5),block=floor(input.Color.b*255+.5);
    float level=max(block,max(0,sky-(14-floor(Daylight+.00001))));
    float minimum=.025+.12*Brightness;
    float light=minimum+(1-minimum)*pow(saturate(level/14),1.7);
    float3 rgb=tex.rgb*input.Color.r*light;
    float fog=saturate((input.Distance-FogStart)/(FogEnd-FogStart));
    return float4(lerp(rgb,FogColor,fog),tex.a*input.Color.a);
}
technique BlockLighting { pass P0 { VertexShader=compile VS_PROFILE VSMain(); PixelShader=compile PS_PROFILE PSMain(); } }
