using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MDSM.Worlds;
namespace MDSM.Rendering;

// Camera-space poses shared by every item in a representation category.
// Row vectors: shape, local animations, camera placement, then walking bob (View).
internal static class HeldItemPose
{
    internal static Matrix World(ViewModelSelection selection,Matrix swing,Matrix equip,Matrix? facing=null)
    {
        bool sprite=selection.Kind==ViewModelKind.Sprite||selection.Kind==ViewModelKind.Plant;
        bool torch=Inventories.Items.TryGetBlock(selection.Item,out var block)&&block==BlockType.Torch;
        bool tool=sprite&&Inventories.Items.Get(selection.Item).Tool!=Inventories.ToolKind.None;
        Matrix pose=torch?Matrix.CreateRotationY(-.35f)*Matrix.CreateRotationZ(.30f)*Matrix.CreateScale(.9f)
            :sprite?Matrix.CreateRotationY(tool?.12f:-.15f)*Matrix.CreateRotationZ(tool?.35f:.25f)*Matrix.CreateScale(tool?1.65f:1.35f)
            :Matrix.CreateRotationY(-MathHelper.PiOver4)*Matrix.CreateScale(.5f);
        Vector3 position=torch?new(.85f,-.44f,-1f):sprite?new(tool?1.05f:1.04f,-.55f,-1f):new(.56f,-.52f,-.72f);
        // Tools' atlas artwork already runs from lower-left grip to upper-right
        // blade/head. Reflecting that plane reverses the actual grip-to-tip axis.
        pose*=tool?Matrix.Identity:Matrix.CreateScale(-1,1,1);
        if(facing.HasValue)
            return pose*Matrix.CreateTranslation(position)*facing.Value*Matrix.CreateTranslation(-position)*swing*equip*Matrix.CreateTranslation(position);
        return pose*swing*equip*Matrix.CreateTranslation(position);
    }
    // Perspective projects X/-Z. Reflect that ratio about the silhouette center
    // while preserving depth. A simple X scale about the world anchor cannot do
    // this when the near and far ends of an angled item have different depths.
    internal static Matrix SpriteFacing(Vector3[] mesh,Matrix rest)
    {
        float min=float.PositiveInfinity,max=float.NegativeInfinity;
        const float tanHalfFov=.70020754f; // tan(70 degrees / 2)
        void Include(Vector3 p){float slope=p.X/-p.Z;min=System.MathF.Min(min,slope);max=System.MathF.Max(max,slope);}
        void Edge(Vector3 a,Vector3 b)
        {
            float begin=0,end=1;
            // Clip to the visible vertical extent before choosing the pivot;
            // the hidden handle below the screen must not shift the visible item.
            for(int plane=0;plane<3;plane++)
            {
                float fa=plane==0?a.Y+a.Z*tanHalfFov:plane==1?-a.Y+a.Z*tanHalfFov:a.Z+.05f;
                float fb=plane==0?b.Y+b.Z*tanHalfFov:plane==1?-b.Y+b.Z*tanHalfFov:b.Z+.05f;
                if(fa>0&&fb>0)return;
                if((fa>0)!=(fb>0)){float t=fa/(fa-fb);if(fa>0)begin=System.MathF.Max(begin,t);else end=System.MathF.Min(end,t);}
            }
            if(begin>end)return;
            Include(Vector3.Lerp(a,b,begin));Include(Vector3.Lerp(a,b,end));
        }
        for(int i=0;i<mesh.Length;i+=3)
        {
            Vector3 a=Vector3.Transform(mesh[i],rest),b=Vector3.Transform(mesh[i+1],rest),c=Vector3.Transform(mesh[i+2],rest);
            Edge(a,b);Edge(b,c);Edge(c,a);
        }
        if(!float.IsFinite(min+max))return Matrix.Identity;
        Matrix mirror=Matrix.Identity;mirror.M11=-1;mirror.M31=-(min+max);
        return mirror;
    }
}
