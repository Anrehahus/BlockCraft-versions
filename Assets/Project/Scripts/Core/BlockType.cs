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
    }

    public static class BlockTypeUtil
    {
        public static bool IsPlant(this BlockType t)
        {
            return t == BlockType.OakSapling || t == BlockType.BrownMushroom ||
                   t == BlockType.RedMushroom || t == BlockType.Dandelion || t == BlockType.Poppy;
        }
    }
}