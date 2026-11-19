using MinecraftClone.Core;

namespace MinecraftClone.Items
{
    public static class ToolRules
    {

        public static float Hardness(BlockType t)
        {
            if (t.IsPlant() || t.IsTorch() || t == BlockType.Tnt) return 0f;   // IsPlant включает огонь
            if (t.IsSign()) return 1f;
            if (t >= BlockType.WhiteWool && t <= BlockType.BlackWool) return 0.8f;
            switch (t)
            {
                case BlockType.OakLeaves: return 0.2f;
                case BlockType.Glass: return 0.3f;
                case BlockType.Dirt: case BlockType.Sand: return 0.5f;
                case BlockType.Grass: case BlockType.Gravel: case BlockType.Sponge: return 0.6f;
                case BlockType.Stone: case BlockType.Bookshelf: return 1.5f;
                case BlockType.Cobblestone:
                case BlockType.MossyCobblestone:
                case BlockType.Bricks:
                case BlockType.StoneSlab:
                case BlockType.StoneSlabDouble:
                case BlockType.OakPlanks:
                case BlockType.OakLog: return 2f;
                case BlockType.CoalOre: case BlockType.IronOre: case BlockType.GoldOre: case BlockType.GoldBlock: return 3f;
                case BlockType.IronBlock: return 5f;
                case BlockType.Obsidian: return 50f;
                default: return 1f;
            }
        }

        public static ToolKind PreferredTool(BlockType t)
        {
            switch (t)
            {
                case BlockType.OakLog:
                case BlockType.OakPlanks:
                case BlockType.Bookshelf:
                    return ToolKind.Axe;
                case BlockType.Stone:
                case BlockType.Cobblestone:
                case BlockType.MossyCobblestone:
                case BlockType.Bricks:
                case BlockType.StoneSlab:
                case BlockType.StoneSlabDouble:
                case BlockType.CoalOre:
                case BlockType.IronOre:
                case BlockType.GoldOre:
                case BlockType.IronBlock:
                case BlockType.GoldBlock:
                case BlockType.Obsidian:
                    return ToolKind.Pickaxe;
                case BlockType.Grass:
                case BlockType.Dirt:
                case BlockType.Sand:
                case BlockType.Gravel:
                    return ToolKind.Shovel;
                default: return ToolKind.None;
            }
        }

        public static float BreakTime(BlockType t, ItemDef held)
        {
            ToolKind need = PreferredTool(t);
            bool right = held != null && held.Tool != ToolKind.None && held.Tool == need;

            float mul = need == ToolKind.Pickaxe ? 5f : 1.5f;   // камень рукой: 1.5 × 5 = 7.5 с
            float time = Hardness(t) * mul;
            if (right) time /= held.MiningSpeed;                 // с подходящим инструментом в 4 раза быстрее
            return time;
        }
    }
}