using System.Collections.Generic;
using UnityEngine;
using MinecraftClone.Core;
using MinecraftClone.ScriptableObjects;
using MinecraftClone.Management;

namespace MinecraftClone.Rendering
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class ChunkRenderer : MonoBehaviour
    {
        private MeshFilter meshFilter;
        private MeshRenderer meshRenderer;

        // Основной физический коллайдер (для твердых блоков, по которым ходим)
        private MeshCollider solidCollider;

        // Триггерный коллайдер (невидимый объем для четкого выделения саженцев Raycast-ом)
        private MeshCollider triggerCollider;

        private List<Vector3> vertices = new List<Vector3>();
        private List<Vector2> uvs = new List<Vector2>();

        private List<int> solidTriangles = new List<int>();
        private List<int> collisionTriangles = new List<int>();  // Только твердая физика
        private List<int> plantTriangles = new List<int>();      // Хитбокс саженцев для клика
        private List<int> waterTriangles = new List<int>();
        private List<int> lavaTriangles = new List<int>();

        private Mesh chunkMesh;
        private Mesh solidCollisionMesh;
        private Mesh triggerCollisionMesh;

        private ulong _solidHash, _plantHash, _appliedSolidHash, _appliedPlantHash;
        private bool _solidApplied, _plantApplied;

        private static readonly Vector3Int[] Directions =
        {
            Vector3Int.up, Vector3Int.down, Vector3Int.left, Vector3Int.right,
            new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        private static readonly Vector3[][] FaceVertsTable =
        {
            new[]{ new Vector3(0,1,0), new Vector3(0,1,1), new Vector3(1,1,1), new Vector3(1,1,0) }, // 0 верх
            new[]{ new Vector3(0,0,1), new Vector3(0,0,0), new Vector3(1,0,0), new Vector3(1,0,1) }, // 1 низ
            new[]{ new Vector3(0,0,1), new Vector3(0,1,1), new Vector3(0,1,0), new Vector3(0,0,0) }, // 2 -X
            new[]{ new Vector3(1,0,0), new Vector3(1,1,0), new Vector3(1,1,1), new Vector3(1,0,1) }, // 3 +X
            new[]{ new Vector3(1,0,1), new Vector3(1,1,1), new Vector3(0,1,1), new Vector3(0,0,1) }, // 4 +Z
            new[]{ new Vector3(0,0,0), new Vector3(0,1,0), new Vector3(1,1,0), new Vector3(1,0,0) }, // 5 -Z
        };

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            meshRenderer = GetComponent<MeshRenderer>();

            MeshCollider[] colliders = GetComponents<MeshCollider>();
            solidCollider = colliders.Length > 0 ? colliders[0] : gameObject.AddComponent<MeshCollider>();

            if (colliders.Length > 1)
            {
                triggerCollider = colliders[1];
            }
            else
            {
                triggerCollider = gameObject.AddComponent<MeshCollider>();
            }

            // Настройка триггерного коллайдера для Raycast
            triggerCollider.convex = true;
            triggerCollider.isTrigger = true;

            chunkMesh = new Mesh { name = "ChunkMesh" };
            solidCollisionMesh = new Mesh { name = "SolidCollisionMesh" };
            triggerCollisionMesh = new Mesh { name = "TriggerCollisionMesh" };

            meshFilter.mesh = chunkMesh;

            Transform oldWater = transform.Find("WaterRenderer");
            if (oldWater != null)
            {
                DestroyImmediate(oldWater.gameObject);
            }
        }

        public void RenderChunk(
            ChunkData chunkData,
            Dictionary<BlockType, BlockDataSO> blockDatabase,
            Dictionary<string, Rect> uvMap,
            WorldManager worldManager,
            Material blocksMaterial,
            Material waterMaterial,
            Material lavaMaterial)
        {
            if (chunkData == null) return;

            ClearMeshData();

            int width = chunkData.Width;
            int height = chunkData.Height;

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    for (int z = 0; z < width; z++)
                    {
                        BlockType type = chunkData.GetBlock(x, y, z);
                        if (type == BlockType.Air) continue;

                        GenerateBlockMesh(x, y, z, type, chunkData, blockDatabase, uvMap, worldManager);
                    }
                }
            }

            ApplyMesh(blocksMaterial, waterMaterial, lavaMaterial);
        }

        private void GenerateBlockMesh(
            int x, int y, int z,
            BlockType type,
            ChunkData chunkData,
            Dictionary<BlockType, BlockDataSO> blockDatabase,
            Dictionary<string, Rect> uvMap,
            WorldManager worldManager)
        {
            if (IsPlantBlock(type))
            {
                AddPlantData(x, y, z, type, blockDatabase, uvMap);
                return;
            }

            Vector3Int[] directions = Directions;

            int width = chunkData.Width;
            int height = chunkData.Height;

            for (int i = 0; i < directions.Length; i++)
            {
                Vector3Int dir = directions[i];
                int neighborX = x + dir.x;
                int neighborY = y + dir.y;
                int neighborZ = z + dir.z;

                BlockType neighborType;

                if (neighborX >= 0 && neighborX < width &&
                    neighborY >= 0 && neighborY < height &&
                    neighborZ >= 0 && neighborZ < width)
                {
                    neighborType = chunkData.GetBlock(neighborX, neighborY, neighborZ);
                }
                else
                {
                    Vector2Int chunkPos = chunkData.Position;
                    int worldX = chunkPos.x * width + neighborX;
                    int worldZ = chunkPos.y * width + neighborZ;
                    neighborType = worldManager.GetBlockAtWorldPosition(worldX, neighborY, worldZ);
                }

                if (ShouldRenderFace(type, neighborType))
                {
                    AddFaceData(x, y, z, i, type, blockDatabase, uvMap);
                }
            }
        }

        private bool IsPlantBlock(BlockType type)
        {
            return type == BlockType.OakSapling;
        }

        private bool ShouldRenderFace(BlockType current, BlockType neighbor)
        {
            if (current == BlockType.Water)
            {
                return neighbor == BlockType.Air || neighbor == BlockType.Lava || IsPlantBlock(neighbor);
            }

            if (current == BlockType.Lava)
            {
                return neighbor == BlockType.Air || neighbor == BlockType.Water || IsPlantBlock(neighbor);
            }

            if (neighbor == BlockType.Air || neighbor == BlockType.Water || neighbor == BlockType.Lava || IsPlantBlock(neighbor))
            {
                return true;
            }

            return false;
        }

        private void AddFaceData(
            int x, int y, int z,
            int faceIndex,
            BlockType type,
            Dictionary<BlockType, BlockDataSO> blockDatabase,
            Dictionary<string, Rect> uvMap)
        {
            int vCount = vertices.Count;
            Vector3 blockPos = new Vector3(x, y, z);

            Vector3[] faceVerts = GetFaceVertices(faceIndex);
            for (int i = 0; i < 4; i++)
            {
                vertices.Add(blockPos + faceVerts[i]);
            }

            if (type == BlockType.Water)
            {
                waterTriangles.Add(vCount);
                waterTriangles.Add(vCount + 1);
                waterTriangles.Add(vCount + 2);
                waterTriangles.Add(vCount + 2);
                waterTriangles.Add(vCount + 3);
                waterTriangles.Add(vCount);
            }
            else if (type == BlockType.Lava)
            {
                lavaTriangles.Add(vCount);
                lavaTriangles.Add(vCount + 1);
                lavaTriangles.Add(vCount + 2);
                lavaTriangles.Add(vCount + 2);
                lavaTriangles.Add(vCount + 3);
                lavaTriangles.Add(vCount);
            }
            else
            {
                solidTriangles.Add(vCount);
                solidTriangles.Add(vCount + 1);
                solidTriangles.Add(vCount + 2);
                solidTriangles.Add(vCount + 2);
                solidTriangles.Add(vCount + 3);
                solidTriangles.Add(vCount);

                collisionTriangles.Add(vCount);
                collisionTriangles.Add(vCount + 1);
                collisionTriangles.Add(vCount + 2);
                collisionTriangles.Add(vCount + 2);
                collisionTriangles.Add(vCount + 3);
                collisionTriangles.Add(vCount);

                _solidHash = _solidHash * 1099511628211UL + (ulong)(((x * 256 + y) * 256 + z) * 8 + faceIndex + 1);
            }

            Rect uvRect = GetBlockUV(type, faceIndex, blockDatabase, uvMap);
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
        }

        private void AddPlantData(
            int x, int y, int z,
            BlockType type,
            Dictionary<BlockType, BlockDataSO> blockDatabase,
            Dictionary<string, Rect> uvMap)
        {
            _plantHash = _plantHash * 1099511628211UL + (ulong)((x * 256 + y) * 256 + z + 1);

            Vector3 blockPos = new Vector3(x, y, z);
            Rect uvRect = GetBlockUV(type, 0, blockDatabase, uvMap);

            // Визуальный X-образный меш (двусторонний с ровными текстурами)
            // Диагональ 1
            AddQuad(blockPos, new Vector3(0, 0, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1), new Vector3(0, 1, 0), uvRect);
            AddQuad(blockPos, new Vector3(1, 0, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0), new Vector3(1, 1, 1), uvRect);

            // Диагональ 2
            AddQuad(blockPos, new Vector3(0, 0, 1), new Vector3(1, 1, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 1), uvRect);
            AddQuad(blockPos, new Vector3(1, 0, 0), new Vector3(0, 1, 1), new Vector3(0, 0, 1), new Vector3(1, 1, 0), uvRect);

            // Создаем объемный невидимый куб-триггер для удобного выделения Raycast-ом с любой стороны
            AddPlantTriggerBox(blockPos);
        }

        private void AddQuad(Vector3 offset, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 v3, Rect uvRect)
        {
            int vCount = vertices.Count;

            vertices.Add(offset + v0);
            vertices.Add(offset + v1);
            vertices.Add(offset + v2);
            vertices.Add(offset + v3);

            solidTriangles.Add(vCount);
            solidTriangles.Add(vCount + 1);
            solidTriangles.Add(vCount + 2);

            solidTriangles.Add(vCount);
            solidTriangles.Add(vCount + 3);
            solidTriangles.Add(vCount + 1);

            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMax));
            uvs.Add(new Vector2(uvRect.xMax, uvRect.yMin));
            uvs.Add(new Vector2(uvRect.xMin, uvRect.yMax));
        }

        private void AddPlantTriggerBox(Vector3 blockPos)
        {
            // Уменьшенный куб по центру блока саженца (от 0.2 до 0.8 по краям)
            Vector3 min = blockPos + new Vector3(0.2f, 0f, 0.2f);
            Vector3 max = blockPos + new Vector3(0.8f, 0.8f, 0.8f);

            Vector3[] bVerts = new Vector3[]
            {
                new Vector3(min.x, min.y, min.z), new Vector3(max.x, min.y, min.z),
                new Vector3(max.x, max.y, min.z), new Vector3(min.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z), new Vector3(max.x, min.y, max.z),
                new Vector3(max.x, max.y, max.z), new Vector3(min.x, max.y, max.z)
            };

            int[] bIndices = new int[]
            {
                0, 2, 1, 0, 3, 2, // Front
                4, 5, 6, 4, 6, 7, // Back
                0, 1, 5, 0, 5, 4, // Bottom
                2, 3, 7, 2, 7, 6, // Top
                0, 4, 7, 0, 7, 3, // Left
                1, 2, 6, 1, 6, 5  // Right
            };

            int vCount = vertices.Count;
            vertices.AddRange(bVerts);

            for (int i = 0; i < bIndices.Length; i++)
            {
                plantTriangles.Add(vCount + bIndices[i]);
            }

            // Добавляем пустышки в uvs, чтобы размер соответствовал массиву vertices
            for (int i = 0; i < 8; i++)
            {
                uvs.Add(Vector2.zero);
            }
        }

        private Vector3[] GetFaceVertices(int faceIndex) => FaceVertsTable[faceIndex];

        private Rect GetBlockUV(
            BlockType type,
            int faceIndex,
            Dictionary<BlockType, BlockDataSO> blockDatabase,
            Dictionary<string, Rect> uvMap)
        {
            if (uvMap == null || blockDatabase == null || !blockDatabase.TryGetValue(type, out BlockDataSO data))
            {
                return new Rect(0, 0, 1, 1);
            }

            Texture2D tex = data.SideTexture;
            if (faceIndex == 0 && data.TopTexture != null) tex = data.TopTexture;
            else if (faceIndex == 1 && data.BottomTexture != null) tex = data.BottomTexture;

            if (tex != null && uvMap.TryGetValue(tex.name, out Rect rect))
            {
                return rect;
            }

            return new Rect(0, 0, 1, 1);
        }

        private void ApplyMesh(Material blocksMaterial, Material waterMaterial, Material lavaMaterial)
        {
            chunkMesh.Clear();
            chunkMesh.SetVertices(vertices);

            List<Material> activeMaterials = new List<Material>();
            List<List<int>> subMeshTriangles = new List<List<int>>();

            if (solidTriangles.Count > 0)
            {
                subMeshTriangles.Add(solidTriangles);
                activeMaterials.Add(blocksMaterial);
            }
            if (waterTriangles.Count > 0)
            {
                subMeshTriangles.Add(waterTriangles);
                activeMaterials.Add(waterMaterial);
            }
            if (lavaTriangles.Count > 0)
            {
                subMeshTriangles.Add(lavaTriangles);
                activeMaterials.Add(lavaMaterial);
            }

            chunkMesh.subMeshCount = subMeshTriangles.Count;
            for (int i = 0; i < subMeshTriangles.Count; i++)
            {
                chunkMesh.SetTriangles(subMeshTriangles[i], i);
            }

            chunkMesh.SetUVs(0, uvs);
            chunkMesh.RecalculateNormals();
            chunkMesh.RecalculateBounds();

            meshRenderer.sharedMaterials = activeMaterials.ToArray();

            // 1. Коллизия для физики ходьбы (без саженцев)
            if (solidCollider != null && (!_solidApplied || _solidHash != _appliedSolidHash))
            {
                solidCollisionMesh.Clear();
                solidCollisionMesh.SetVertices(vertices);
                solidCollisionMesh.SetTriangles(collisionTriangles, 0);

                solidCollider.sharedMesh = null;
                solidCollider.sharedMesh = solidCollisionMesh;

                _appliedSolidHash = _solidHash;
                _solidApplied = true;
            }

            // 2. Невидимый хитбокс для Raycast (саженцы)
            if (triggerCollider != null && (!_plantApplied || _plantHash != _appliedPlantHash))
            {
                triggerCollisionMesh.Clear();
                if (plantTriangles.Count > 0)
                {
                    triggerCollisionMesh.SetVertices(vertices);
                    triggerCollisionMesh.SetTriangles(plantTriangles, 0);

                    triggerCollider.sharedMesh = null;
                    triggerCollider.sharedMesh = triggerCollisionMesh;
                    triggerCollider.convex = true;
                }
                else
                {
                    triggerCollider.sharedMesh = null;
                }

                _appliedPlantHash = _plantHash;
                _plantApplied = true;
            }
        }

        public void ClearMesh()
        {
            ClearMeshData();
            _solidApplied = false;
            _plantApplied = false;
            if (chunkMesh != null) chunkMesh.Clear();
            if (solidCollisionMesh != null) solidCollisionMesh.Clear();
            if (triggerCollisionMesh != null) triggerCollisionMesh.Clear();
            if (solidCollider != null) solidCollider.sharedMesh = null;
            if (triggerCollider != null) triggerCollider.sharedMesh = null;
        }

        private void ClearMeshData()
        {
            vertices.Clear();
            uvs.Clear();

            solidTriangles.Clear();
            collisionTriangles.Clear();
            plantTriangles.Clear();
            waterTriangles.Clear();
            lavaTriangles.Clear();

            _solidHash = 0;
            _plantHash = 0;
        }
    }
}