using UnityEngine;

namespace MinecraftClone.Core
{
    public enum BlockType
    {
        Air,
        Grass,
        Dirt,
        Stone,
        Cobblestone,
        OakPlanks,
        OakSapling,
        Bedrock,
        Water,
        Lava,
        Gravel,
        Sand,
        OakLog,
        IronOre,
        GoldOre,
        OakLeaves,
        CoalOre,
        Glass,
        Sponge,
        BrownMushroom,
        RedMushroom,
        Dandelion,
        Poppy,
        GoldBlock,
        WhiteWool,
        OrangeWool,
        MagentaWool,
        LightBlueWool,
        YellowWool,
        LimeWool,
        PinkWool,
        GrayWool,
        LightGrayWool,
        CyanWool,
        PurpleWool,
        BlueWool,
        BrownWool,
        GreenWool,
        RedWool,
        BlackWool,
        SignStanding0, SignStanding1, SignStanding2, SignStanding3,
        SignStanding4, SignStanding5, SignStanding6, SignStanding7,   // стоящая, поворот i*45°
        SignWallPosX, SignWallNegX, SignWallPosZ, SignWallNegZ,       // на стене, смотрит в сторону оси
    }

    public static class BlockTypeUtil
    {
        public static bool IsPlant(this BlockType t)
        {
            return t == BlockType.OakSapling || t == BlockType.BrownMushroom ||
                   t == BlockType.RedMushroom || t == BlockType.Dandelion || t == BlockType.Poppy;
        }

        public static bool IsSign(this BlockType t) => t >= BlockType.SignStanding0 && t <= BlockType.SignWallNegZ;
        public static bool IsStandingSign(this BlockType t) => t >= BlockType.SignStanding0 && t <= BlockType.SignStanding7;

        // Куда смотрит лицевая сторона (в сторону читающего)
        public static Vector3 SignFacing(this BlockType t)
        {
            if (t.IsStandingSign())
            {
                float a = ((int)t - (int)BlockType.SignStanding0) * 45f * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
            switch (t)
            {
                case BlockType.SignWallPosX: return Vector3.right;
                case BlockType.SignWallNegX: return Vector3.left;
                case BlockType.SignWallPosZ: return Vector3.forward;
                default: return Vector3.back;
            }
        }

        // Смещение до блока, на котором держится табличка
        public static Vector3Int SignSupportOffset(this BlockType t)
        {
            if (t.IsStandingSign()) return Vector3Int.down;
            Vector3 f = t.SignFacing();
            return new Vector3Int(-Mathf.RoundToInt(f.x), 0, -Mathf.RoundToInt(f.z));
        }

        public static BlockType StandingSignFromYaw(float yawDegrees)
        {
            int i = ((Mathf.RoundToInt(yawDegrees / 45f) % 8) + 8) % 8;
            return BlockType.SignStanding0 + i;
        }

        public static BlockType WallSignFromNormal(Vector3Int n)
        {
            if (n.x > 0) return BlockType.SignWallPosX;
            if (n.x < 0) return BlockType.SignWallNegX;
            if (n.z > 0) return BlockType.SignWallPosZ;
            return BlockType.SignWallNegZ;
        }
    }
}