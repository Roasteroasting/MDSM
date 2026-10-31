using System;

namespace MDSM.Testing;

// Known fixed fields, independent of the production reader. Used by byte-corruption/legacy fixtures.
internal static class SaveLayout
{
    public static int BlockEnd(byte[] bytes)
    {
        int nameLength = BitConverter.ToInt32(bytes, 24);
        return 52 + nameLength + BitConverter.ToInt32(bytes, 48 + nameLength);
    }
    public static int Health(byte[] bytes)
    {
        int drops = BlockEnd(bytes) + 8 + 36 * 3;
        return drops + 4 + BitConverter.ToInt32(bytes, drops) * 23;
    }
    public static int MobCount(byte[] bytes) => Health(bytes) + 4;
    public static int MobRecords(byte[] bytes) => MobCount(bytes) + 4;
    public static int Population(byte[] bytes) => MobRecords(bytes) + BitConverter.ToInt32(bytes, MobCount(bytes)) * 21;
    public static int Survival(byte[] bytes)
    {
        int pending=Population(bytes)+16;int gravity=pending+4+BitConverter.ToInt32(bytes,pending)*9;
        int falling=gravity+4+BitConverter.ToInt32(bytes,gravity)*4;
        return falling+4+BitConverter.ToInt32(bytes,falling)*25;
    }
}
