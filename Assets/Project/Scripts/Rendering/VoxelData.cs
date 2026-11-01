using UnityEngine;

namespace MinecraftClone.Rendering
{
    public static class VoxelData
    {
        // 8 вершин куба 1x1x1
        public static readonly Vector3[] Vertices = new Vector3[]
        {
            new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(0, 1, 0),
            new Vector3(0, 0, 1), new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1)
        };

        // Направления проверки 6 соседних блоков (Front, Back, Top, Bottom, Left, Right)
        public static readonly Vector3Int[] FaceChecks = new Vector3Int[]
        {
            new Vector3Int(0, 0, -1), // Front
            new Vector3Int(0, 0, 1),  // Back
            new Vector3Int(0, 1, 0),  // Top
            new Vector3Int(0, -1, 0), // Bottom
            new Vector3Int(-1, 0, 0), // Left
            new Vector3Int(1, 0, 0)   // Right
        };

        // Порядок вершин для сборки треугольников каждой из 6 граней
        public static readonly int[,] FaceTriangles = new int[,]
        {
            { 0, 3, 2, 1 }, // Front
            { 5, 6, 7, 4 }, // Back
            { 3, 7, 6, 2 }, // Top
            { 1, 5, 4, 0 }, // Bottom
            { 4, 7, 3, 0 }, // Left
            { 1, 2, 6, 5 }  // Right
        };
    }
}