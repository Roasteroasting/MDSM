#if OPENGL
#define SV_POSITION POSITION
#define VS_PROFILE vs_3_0
#define PS_PROFILE ps_3_0
#else
#define VS_PROFILE vs_4_0_level_9_1
#define PS_PROFILE ps_4_0_level_9_1
#endif
float4x4 ViewProjection;
float Stage;
texture Texture;
texture Cracks;
sampler BaseSampler=sampler_state {Texture=<Texture>;MinFilter=Point;MagFilter=Point;MipFilter=Point;AddressU=Clamp;AddressV=Clamp;};
sampler CrackSampler=sampler_state {Texture=<Cracks>;MinFilter=Point;MagFilter=Point;MipFilter=Point;AddressU=Clamp;AddressV=Clamp;};
struct Input {float4 Position:POSITION0;float2 Base:TEXCOORD0;float2 Damage:TEXCOORD1;};
struct Output {float4 Position:SV_POSITION;float2 Base:TEXCOORD0;float2 Damage:TEXCOORD1;};
Output VSMain(Input input){Output o;o.Position=mul(input.Position,ViewProjection);o.Base=input.Base;o.Damage=input.Damage;return o;}
float4 PSMain(Output input):COLOR0
{
    float alpha=tex2D(BaseSampler,input.Base).a;clip(alpha-.5);
    float2 uv=float2((Stage*16+.5+saturate(input.Damage.x)*15)/160,(.5+saturate(input.Damage.y)*15)/16);
    float4 crack=tex2D(CrackSampler,uv);clip(crack.a-.03);
    return float4(lerp(float3(1,1,1),crack.rgb,alpha),1);
}
technique CrackOverlay {pass P0 {VertexShader=compile VS_PROFILE VSMain();PixelShader=compile PS_PROFILE PSMain();}}
