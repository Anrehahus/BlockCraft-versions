// Scripts/Generation/ITerrainPass.cs
using MinecraftClone.Core;

namespace MinecraftClone.Generation
{
    public interface ITerrainPass
    {
        void Process(ChunkData chunkData);
    }
}