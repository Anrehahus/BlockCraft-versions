using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.Management;
using MinecraftClone.ScriptableObjects;

namespace MinecraftClone.Rendering
{
    public static class ChunkMeshBuilder
    {
        private static readonly List<Vector3> solidVertices = new List<Vector3>(8000);
        private static readonly List<Vector2> solidUvs = new List<Vector2>(8000);
        private static readonly List<int> solidTriangles = new List<int>(24000);

        private static readonly List<Vector3> waterVertices = new List<Vector3>(4000);
        private static readonly List<Vector2> waterUvs = new List<Vector2>(4000);
        private static readonly List<int> waterTriangles = new List<int>(12000);

        private static readonly List<Vector3> lavaVertices = new List<Vector3>(4000);
        private static readonly List<Vector2> lavaUvs = new List<Vector2>(4000);
        private static readonly List<int> lavaTriangles = new List<int>(12000);

        private static readonly List<Vector3> colliderVertices = new List<Vector3>(8000);
        private static readonly List<int> colliderTriangles = new List<int>(24000);

        private static readonly List<Vector3> triggerVertices = new List<Vector3>(2000);
        private static readonly List<int> triggerTriangles = new List<int>(6000);

        private static readonly List<Vector3> allVertices = new List<Vector3>(16000);
        private static readonly List<Vector2> allUvs = new List<Vector2>(16000);

        public static Mesh BuildChunkMesh(ChunkData chunkData, Dictionary<BlockType, BlockDataSO> blockDatabase, Dictionary<string, Rect> uvMap, WorldManager worldManager)
        {
            solidVertices.Clear();
            solidUvs.Clear();
            solidTriangles.Clear();

            waterVertices.Clear();
            waterUvs.Clear();
            waterTriangles.Clear();

            lavaVertices.Clear();
            lavaUvs.Clear();
            lavaTriangles.Clear();

            int width = chunkData.Width;
            int height = chunkData.Height;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < width; z++)
                    {
                        BlockType currentBlock = chunkData.GetBlock(x, y, z);
                        if (currentBlock == BlockType.Air) continue;

                        if (!blockDatabase.TryGetValue(currentBlock, out BlockDataSO blockData) || blockData == null)
                            continue;

                        if (currentBlock == BlockType.OakSapling)
                        {
                            GenerateCrossMesh(x, y, z, blockData, uvMap, solidVertices, solidTriangles, solidUvs);
                            continue;
                        }

                        if (currentBlock == BlockType.Water)
                        {
                            GenerateBlockMesh(chunkData, x, y, z, blockData, uvMap, worldManager, waterVertices, waterTriangles, waterUvs);
                        }
                        else if (currentBlock == BlockType.Lava)
                        {
                            GenerateBlockMesh(chunkData, x, y, z, blockData, uvMap, worldManager, lavaVertices, lavaTriangles, lavaUvs);
                        }
                        else
                        {
                            GenerateBlockMesh(chunkData, x, y, z, blockData, uvMap, worldManager, solidVertices, solidTriangles, solidUvs);
                        }
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.subMeshCount = 3;

            allVertices.Clear();
            allVertices.Capacity = Mathf.Max(allVertices.Capacity, solidVertices.Count + waterVertices.Count + lavaVertices.Count);
            allVertices.AddRange(solidVertices);
            allVertices.AddRange(waterVertices);
            allVertices.AddRange(lavaVertices);

            allUvs.Clear();
            allUvs.Capacity = Mathf.Max(allUvs.Capacity, solidUvs.Count + waterUvs.Count + lavaUvs.Count);
            allUvs.AddRange(solidUvs);
            allUvs.AddRange(waterUvs);
            allUvs.AddRange(lavaUvs);

            mesh.SetVertices(allVertices);
            mesh.SetUVs(0, allUvs);

            mesh.SetTriangles(solidTriangles, 0, calculateBounds: false);

            int waterOffset = solidVertices.Count;
            mesh.SetTriangles(waterTriangles, 1, calculateBounds: false, baseVertex: waterOffset);

            int lavaOffset = solidVertices.Count + waterVertices.Count;
            mesh.SetTriangles(lavaTriangles, 2, calculateBounds: false, baseVertex: lavaOffset);

            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        public static Mesh BuildColliderMesh(ChunkData chunkData, Dictionary<BlockType, BlockDataSO> blockDatabase, WorldManager worldManager)
        {
            colliderVertices.Clear();
            colliderTriangles.Clear();

            int width = chunkData.Width;
            int height = chunkData.Height;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < width; z++)
                    {
                        BlockType currentBlock = chunkData.GetBlock(x, y, z);
                        if (currentBlock == BlockType.Air) continue;
                        if (!blockDatabase.TryGetValue(currentBlock, out BlockDataSO blockData) || blockData == null) continue;

                        if (blockData.IsSolid && !blockData.IsLiquid)
                        {
                            GenerateColliderBlockMesh(chunkData, x, y, z, blockData, worldManager, colliderVertices, colliderTriangles);
                        }
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.SetVertices(colliderVertices);
            mesh.SetTriangles(colliderTriangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Mesh BuildTriggerMesh(ChunkData chunkData, Dictionary<BlockType, BlockDataSO> blockDatabase, WorldManager worldManager)
        {
            triggerVertices.Clear();
            triggerTriangles.Clear();

            int width = chunkData.Width;
            int height = chunkData.Height;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < width; z++)
                    {
                        BlockType currentBlock = chunkData.GetBlock(x, y, z);
                        if (currentBlock == BlockType.Air) continue;
                        if (!blockDatabase.TryGetValue(currentBlock, out BlockDataSO blockData) || blockData == null) continue;

                        if (blockData.IsSolid && !blockData.IsLiquid) continue;

                        GenerateColliderBlockMesh(chunkData, x, y, z, blockData, worldManager, triggerVertices, triggerTriangles);
                    }
                }
            }

            Mesh mesh = new Mesh();
            mesh.SetVertices(triggerVertices);
            mesh.SetTriangles(triggerTriangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
        }

        private static void GenerateCrossMesh(int x, int y, int z, BlockDataSO blockData, Dictionary<string, Rect> uvMap, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            Vector3 basePos = new Vector3(x, y, z);
            Texture2D tex = blockData.SideTexture != null ? blockData.SideTexture : blockData.TopTexture;
            Rect uv = GetSafeUV(uvMap, tex);

            AddFace(basePos + new Vector3(0, 0, 0), basePos + new Vector3(0, 1, 0), basePos + new Vector3(1, 1, 1), basePos + new Vector3(1, 0, 1), uv, vertices, triangles, uvs);
            AddFace(basePos + new Vector3(1, 0, 1), basePos + new Vector3(1, 1, 1), basePos + new Vector3(0, 1, 0), basePos + new Vector3(0, 0, 0), uv, vertices, triangles, uvs);

            AddFace(basePos + new Vector3(0, 0, 1), basePos + new Vector3(0, 1, 1), basePos + new Vector3(1, 1, 0), basePos + new Vector3(1, 0, 0), uv, vertices, triangles, uvs);
            AddFace(basePos + new Vector3(1, 0, 0), basePos + new Vector3(1, 1, 0), basePos + new Vector3(0, 1, 1), basePos + new Vector3(0, 0, 1), uv, vertices, triangles, uvs);
        }

        private static void GenerateBlockMesh(ChunkData chunk, int x, int y, int z, BlockDataSO blockData, Dictionary<string, Rect> uvMap, WorldManager worldManager, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            Vector3 blockPos = new Vector3(x, y, z);
            float h = blockData.IsLiquid ? 0.875f : 1.0f;

            if (ShouldRenderFace(chunk, x, y, z, Vector3Int.up, worldManager, blockData))
            {
                Rect uv = GetSafeUV(uvMap, blockData.TopTexture);
                AddFace(blockPos + new Vector3(0, h, 0), blockPos + new Vector3(0, h, 1), blockPos + new Vector3(1, h, 1), blockPos + new Vector3(1, h, 0), uv, vertices, triangles, uvs);
            }

            if (ShouldRenderFace(chunk, x, y, z, Vector3Int.down, worldManager, blockData))
            {
                Rect uv = GetSafeUV(uvMap, blockData.BottomTexture);
                AddFace(blockPos + new Vector3(0, 0, 1), blockPos + new Vector3(0, 0, 0), blockPos + new Vector3(1, 0, 0), blockPos + new Vector3(1, 0, 1), uv, vertices, triangles, uvs);
            }

            if (ShouldRenderFace(chunk, x, y, z, Vector3Int.forward, worldManager, blockData))
            {
                Rect uv = GetSafeUV(uvMap, blockData.SideTexture);
                AddFace(blockPos + new Vector3(1, 0, 1), blockPos + new Vector3(1, h, 1), blockPos + new Vector3(0, h, 1), blockPos + new Vector3(0, 0, 1), uv, vertices, triangles, uvs);
            }

            if (ShouldRenderFace(chunk, x, y, z, Vector3Int.back, worldManager, blockData))
            {
                Rect uv = GetSafeUV(uvMap, blockData.SideTexture);
                AddFace(blockPos + new Vector3(0, 0, 0), blockPos + new Vector3(0, h, 0), blockPos + new Vector3(1, h, 0), blockPos + new Vector3(1, 0, 0), uv, vertices, triangles, uvs);
            }

            if (ShouldRenderFace(chunk, x, y, z, Vector3Int.right, worldManager, blockData))
            {
                Rect uv = GetSafeUV(uvMap, blockData.SideTexture);
                AddFace(blockPos + new Vector3(1, 0, 0), blockPos + new Vector3(1, h, 0), blockPos + new Vector3(1, h, 1), blockPos + new Vector3(1, 0, 1), uv, vertices, triangles, uvs);
            }

            if (ShouldRenderFace(chunk, x, y, z, Vector3Int.left, worldManager, blockData))
            {
                Rect uv = GetSafeUV(uvMap, blockData.SideTexture);
                AddFace(blockPos + new Vector3(0, 0, 1), blockPos + new Vector3(0, h, 1), blockPos + new Vector3(0, h, 0), blockPos + new Vector3(0, 0, 0), uv, vertices, triangles, uvs);
            }
        }

        private static void GenerateColliderBlockMesh(ChunkData chunk, int x, int y, int z, BlockDataSO blockData, WorldManager worldManager, List<Vector3> vertices, List<int> triangles)
        {
            Vector3 blockPos = new Vector3(x, y, z);
            float h = blockData.IsLiquid ? 0.875f : 1.0f;

            if (ShouldRenderColliderFace(chunk, x, y, z, Vector3Int.up, worldManager))
                AddColliderFace(blockPos + new Vector3(0, h, 0), blockPos + new Vector3(0, h, 1), blockPos + new Vector3(1, h, 1), blockPos + new Vector3(1, h, 0), vertices, triangles);

            if (ShouldRenderColliderFace(chunk, x, y, z, Vector3Int.down, worldManager))
                AddColliderFace(blockPos + new Vector3(0, 0, 1), blockPos + new Vector3(0, 0, 0), blockPos + new Vector3(1, 0, 0), blockPos + new Vector3(1, 0, 1), vertices, triangles);

            if (ShouldRenderColliderFace(chunk, x, y, z, Vector3Int.forward, worldManager))
                AddColliderFace(blockPos + new Vector3(1, 0, 1), blockPos + new Vector3(1, h, 1), blockPos + new Vector3(0, h, 1), blockPos + new Vector3(0, 0, 1), vertices, triangles);

            if (ShouldRenderColliderFace(chunk, x, y, z, Vector3Int.back, worldManager))
                AddColliderFace(blockPos + new Vector3(0, 0, 0), blockPos + new Vector3(0, h, 0), blockPos + new Vector3(1, h, 0), blockPos + new Vector3(1, 0, 0), vertices, triangles);

            if (ShouldRenderColliderFace(chunk, x, y, z, Vector3Int.right, worldManager))
                AddColliderFace(blockPos + new Vector3(1, 0, 0), blockPos + new Vector3(1, h, 0), blockPos + new Vector3(1, h, 1), blockPos + new Vector3(1, 0, 1), vertices, triangles);

            if (ShouldRenderColliderFace(chunk, x, y, z, Vector3Int.left, worldManager))
                AddColliderFace(blockPos + new Vector3(0, 0, 1), blockPos + new Vector3(0, h, 1), blockPos + new Vector3(0, h, 0), blockPos + new Vector3(0, 0, 0), vertices, triangles);
        }

        private static bool ShouldRenderFace(ChunkData chunk, int localX, int localY, int localZ, Vector3Int offset, WorldManager worldManager, BlockDataSO currentData)
        {
            int neighborLocalX = localX + offset.x;
            int neighborLocalY = localY + offset.y;
            int neighborLocalZ = localZ + offset.z;

            int width = chunk.Width;
            int height = chunk.Height;

            if (neighborLocalY < 0 || neighborLocalY >= height)
                return true;

            BlockType neighborType;

            if (neighborLocalX >= 0 && neighborLocalX < width && neighborLocalZ >= 0 && neighborLocalZ < width)
            {
                neighborType = chunk.GetBlock(neighborLocalX, neighborLocalY, neighborLocalZ);
            }
            else if (worldManager != null)
            {
                int worldX = (chunk.Position.x * width) + neighborLocalX;
                int worldY = neighborLocalY;
                int worldZ = (chunk.Position.y * width) + neighborLocalZ;
                neighborType = worldManager.GetBlockAtWorldPosition(worldX, worldY, worldZ);
            }
            else
            {
                return true;
            }

            return EvaluateNeighbor(neighborType, currentData, worldManager);
        }

        private static bool EvaluateNeighbor(BlockType neighborType, BlockDataSO currentData, WorldManager worldManager)
        {
            if (neighborType == BlockType.Air) return true;

            if (currentData.Type == BlockType.Water && neighborType == BlockType.Water) return false;
            if (currentData.Type == BlockType.Lava && neighborType == BlockType.Lava) return false;

            if (worldManager != null && worldManager.BlockDatabase != null && worldManager.BlockDatabase.TryGetValue(neighborType, out BlockDataSO neighborData))
            {
                if (currentData.IsLiquid && neighborData.IsLiquid) return false;
                return !neighborData.IsOpaque;
            }
            return false;
        }

        private static bool ShouldRenderColliderFace(ChunkData chunk, int localX, int localY, int localZ, Vector3Int offset, WorldManager worldManager)
        {
            int neighborLocalX = localX + offset.x;
            int neighborLocalY = localY + offset.y;
            int neighborLocalZ = localZ + offset.z;

            int width = chunk.Width;
            int height = chunk.Height;

            if (neighborLocalY < 0 || neighborLocalY >= height)
                return true;

            BlockType neighborType;

            if (neighborLocalX >= 0 && neighborLocalX < width && neighborLocalZ >= 0 && neighborLocalZ < width)
            {
                neighborType = chunk.GetBlock(neighborLocalX, neighborLocalY, neighborLocalZ);
            }
            else if (worldManager != null)
            {
                int worldX = (chunk.Position.x * width) + neighborLocalX;
                int worldY = neighborLocalY;
                int worldZ = (chunk.Position.y * width) + neighborLocalZ;
                neighborType = worldManager.GetBlockAtWorldPosition(worldX, worldY, worldZ);
            }
            else
            {
                return true;
            }

            if (neighborType == BlockType.Air) return true;

            if (worldManager != null && worldManager.BlockDatabase != null && worldManager.BlockDatabase.TryGetValue(neighborType, out BlockDataSO neighborData))
            {
                return !neighborData.IsSolid || neighborData.IsLiquid;
            }
            return false;
        }

        private static Rect GetSafeUV(Dictionary<string, Rect> uvMap, Texture2D texture)
        {
            if (texture != null && uvMap != null && uvMap.TryGetValue(texture.name, out Rect rect))
            {
                return rect;
            }

            if (uvMap != null && uvMap.Count > 0)
            {
                foreach (var kvp in uvMap)
                    return kvp.Value;
            }

            return new Rect(0, 0, 1, 1);
        }

        private static void AddFace(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Rect uvRect, List<Vector3> vertices, List<int> triangles, List<Vector2> uvs)
        {
            int vIndex = vertices.Count;

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            triangles.Add(vIndex);
            triangles.Add(vIndex + 1);
            triangles.Add(vIndex + 2);

            triangles.Add(vIndex);
            triangles.Add(vIndex + 2);
            triangles.Add(vIndex + 3);

            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
        }

        private static void AddColliderFace(Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, List<Vector3> vertices, List<int> triangles)
        {
            int vIndex = vertices.Count;

            vertices.Add(v0);
            vertices.Add(v1);
            vertices.Add(v2);
            vertices.Add(v3);

            triangles.Add(vIndex);
            triangles.Add(vIndex + 1);
            triangles.Add(vIndex + 2);

            triangles.Add(vIndex);
            triangles.Add(vIndex + 2);
            triangles.Add(vIndex + 3);
        }
    }
}