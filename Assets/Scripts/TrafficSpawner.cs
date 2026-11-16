using System.Collections.Generic;
using UnityEngine;

namespace TwoLaneSimulation
{
    /// <summary>
    /// Trình quản lý sinh xe (TrafficSpawner) cho đường 4 làn chạy 2 chiều ngược nhau:
    /// - 2 làn bên trái (X = -6.0m, X = -2.0m): Xe chạy ngược chiều theo trục -Z.
    /// - 2 làn bên phải (X = +2.0m, X = +6.0m): Xe chạy xuôi chiều theo trục +Z.
    /// - Đảm bảo phân bổ khoảng cách an toàn, chống đè chồng xe khi sinh.
    /// </summary>
    public class TrafficSpawner : MonoBehaviour
    {
        [Header("Vehicle Prefabs")]
        [Tooltip("Prefab xe chạy xuôi chiều (+Z)")]
        public GameObject forwardVehiclePrefab;

        [Tooltip("Prefab xe chạy ngược chiều (-Z)")]
        public GameObject reverseVehiclePrefab;

        [Header("Traffic Flow Configuration")]
        [Tooltip("Tổng số xe sinh ra trên cả 4 làn")]
        public int totalVehicles = 24;

        [Tooltip("Tọa độ X của 4 làn đường: 2 làn trái ngược chiều, 2 làn phải xuôi chiều")]
        public float[] lanePositions = new float[] { -6.0f, -2.0f, 2.0f, 6.0f };

        [Tooltip("Khoảng cách tối thiểu theo trục Z giữa 2 xe cùng làn (m)")]
        public float minIntervalZ = 16.0f;

        [Tooltip("Khoảng cách tối đa theo trục Z giữa 2 xe cùng làn (m)")]
        public float maxIntervalZ = 35.0f;

        public float spawnHeightY = 0.5f;

        [Header("Personality Range (Độ Vội)")]
        public float minHurryFactor = 0.75f;
        public float maxHurryFactor = 1.35f;

        private void Start()
        {
            SpawnAllVehicles();
        }

        public void SpawnAllVehicles()
        {
            if (forwardVehiclePrefab == null && reverseVehiclePrefab == null)
            {
                Debug.LogWarning("[TrafficSpawner] Chưa gán Prefab xe!");
                return;
            }

            int numLanes = lanePositions.Length;
            int vehiclesPerLane = Mathf.Max(1, totalVehicles / numLanes);

            List<Vector3> spawnedPositions = new List<Vector3>();

            for (int l = 0; l < numLanes; l++)
            {
                float laneX = lanePositions[l];
                bool isReverseLane = (laneX < 0f);

                float currentZ = isReverseLane ? Random.Range(440f, 470f) : Random.Range(20f, 50f);
                float zStepSign = isReverseLane ? -1f : 1f;

                for (int v = 0; v < vehiclesPerLane; v++)
                {
                    Vector3 candidatePos = new Vector3(laneX, spawnHeightY, currentZ);
                    candidatePos = ResolveOverlap(candidatePos, spawnedPositions);

                    Quaternion spawnRot = isReverseLane ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                    GameObject prefabToUse = isReverseLane ? (reverseVehiclePrefab ?? forwardVehiclePrefab)
                                                          : (forwardVehiclePrefab ?? reverseVehiclePrefab);

                    GameObject carObj = Instantiate(prefabToUse, candidatePos, spawnRot, transform);
                    carObj.name = $"AIF_Vehicle_Lane{l}_{v:D2}";
                    carObj.tag = "Vehicle";

                    var aifAgent = carObj.GetComponent<AIFVehicleAgent>();
                    if (aifAgent != null)
                    {
                        aifAgent.drivingDirection = isReverseLane ? -1 : 1;
                        aifAgent.hurryFactor = Random.Range(minHurryFactor, maxHurryFactor);
                        aifAgent.fixedHeightY = spawnHeightY;
                    }

                    var visualizer = carObj.GetComponent<CarCollisionVisualizer>();
                    if (visualizer != null)
                    {
                        Color safeColor = CarCollisionVisualizer.SafeColorPalette[(v * numLanes + l) % CarCollisionVisualizer.SafeColorPalette.Length];
                        visualizer.ResetColor();
                        visualizer.SetNormalColor(safeColor);
                    }

                    spawnedPositions.Add(candidatePos);
                    float gap = Random.Range(minIntervalZ, maxIntervalZ);
                    currentZ += zStepSign * gap;

                    if (isReverseLane && currentZ < 30f) currentZ = 470f;
                    if (!isReverseLane && currentZ > 470f) currentZ = 30f;
                }
            }

            Debug.Log($"[TrafficSpawner] Đã spawn thành công {spawnedPositions.Count} xe AIF trên 4 làn đường ngược chiều.");
        }

        private Vector3 ResolveOverlap(Vector3 candidate, List<Vector3> existing)
        {
            Vector3 resolved = candidate;
            int attempts = 0;
            while (attempts < 20)
            {
                bool conflict = false;
                for (int i = 0; i < existing.Count; i++)
                {
                    float dx = Mathf.Abs(resolved.x - existing[i].x);
                    float dz = Mathf.Abs(resolved.z - existing[i].z);
                    if (dx < 2.0f && dz < 10.0f)
                    {
                        conflict = true;
                        break;
                    }
                }

                if (!conflict) return resolved;
                resolved.z += (resolved.x < 0f) ? -5.0f : 5.0f;
                attempts++;
            }
            return resolved;
        }

        private void OnDrawGizmosSelected()
        {
            if (lanePositions == null) return;
            Gizmos.color = Color.cyan;
            for (int i = 0; i < lanePositions.Length; i++)
            {
                float x = lanePositions[i];
                Gizmos.DrawLine(new Vector3(x, spawnHeightY, 0f), new Vector3(x, spawnHeightY, 500f));
            }
        }
    }
}
