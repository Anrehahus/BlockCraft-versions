// Scripts/Generation/TerrainPipeline.cs
using System.Collections.Generic;
using MinecraftClone.Core;

namespace MinecraftClone.Generation
{
    public class TerrainPipeline
    {
        private readonly List<ITerrainPass> _passes = new List<ITerrainPass>();

        public TerrainPipeline()
        {
            // Строгий порядок вызова проходов
            _passes.Add(new HeightmapPass());
            _passes.Add(new VeinPass());
            _passes.Add(new CavePass());
            _passes.Add(new SurfacePass());
        }

        public void GenerateChunk(ChunkData chunkData)
        {
            for (int i = 0; i < _passes.Count; i++)
            {
                _passes[i].Process(chunkData);
            }
        }
    }
}