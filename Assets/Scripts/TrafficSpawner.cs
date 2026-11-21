using System.Collections.Generic;
using UnityEngine;

namespace TwoLaneSimulation
{
    public class TrafficSpawner : MonoBehaviour
    {
        [Header("Car Prefabs (Mật độ thấp)")]
        [Tooltip("Prefab ô tô chạy xuôi chiều (+Z)")]
        public GameObject forwardCarPrefab;

        [Tooltip("Prefab ô tô chạy ngược chiều (-Z)")]
        public GameObject reverseCarPrefab;

        [Header("Motorcycle Prefabs (Mật độ cao)")]
        [Tooltip("Prefab xe máy chạy xuôi chiều (+Z)")]
        public GameObject forwardMotorcyclePrefab;

        [Tooltip("Prefab xe máy chạy ngược chiều (-Z)")]
        public GameObject reverseMotorcyclePrefab;

        [Header("Legacy Compatibility")]
        [Tooltip("Prefab xe chạy xuôi chiều (+Z) - dành cho tương thích cấu hình cũ")]
        public GameObject forwardVehiclePrefab;

        [Tooltip("Prefab xe chạy ngược chiều (-Z) - dành cho tương thích cấu hình cũ")]
        public GameObject reverseVehiclePrefab;

        [Header("Traffic Density Configuration")]
        [Tooltip("Tổng số ô tô sinh ra trên cả 4 làn (mật độ thấp hơn)")]
        public int totalCars = 8;

        [Tooltip("Tổng số xe máy sinh ra trên cả 4 làn (mật độ cao)")]
        public int totalMotorcycles = 28;

        [Tooltip("Tọa độ X của 4 làn đường: 2 làn trái ngược chiều, 2 làn phải xuôi chiều")]
        public float[] lanePositions = new float[] { -6.0f, -2.0f, 2.0f, 6.0f };

        [Header("Car Spacing Settings")]
        [Tooltip("Khoảng cách tối thiểu theo trục Z cho ô tô (m)")]
        public float minIntervalZCar = 18.0f;

        [Tooltip("Khoảng cách tối đa theo trục Z cho ô tô (m)")]
        public float maxIntervalZCar = 36.0f;

        [Header("Motorcycle Spacing Settings")]
        [Tooltip("Khoảng cách tối thiểu theo trục Z cho xe máy (m, mật độ cao nên cự ly ngắn hơn)")]
        public float minIntervalZMotorcycle = 6.0f;

        [Tooltip("Khoảng cách tối đa theo trục Z cho xe máy (m)")]
        public float maxIntervalZMotorcycle = 14.0f;

        [Tooltip("Độ lệch ngang ngẫu nhiên của xe máy trong làn (m) để đi so le")]
        public float motorcycleLateralOffset = 0.65f;

        public float spawnHeightY = 0.5f;

        [Header("Personality Range (Độ Vội)")]
        public float minHurryFactor = 0.75f;
        public float maxHurryFactor = 1.35f;

        private struct SpawnedRecord
        {
            public Vector3 position;
            public bool isMotorcycle;
        }

        private void Start()
        {
            SpawnAllVehicles();
        }

        public void SpawnAllVehicles()
        {
            if (forwardCarPrefab == null && forwardVehiclePrefab != null) forwardCarPrefab = forwardVehiclePrefab;
            if (reverseCarPrefab == null && reverseVehiclePrefab != null) reverseCarPrefab = reverseVehiclePrefab;
            if (forwardVehiclePrefab == null && forwardCarPrefab != null) forwardVehiclePrefab = forwardCarPrefab;
            if (reverseVehiclePrefab == null && reverseCarPrefab != null) reverseVehiclePrefab = reverseCarPrefab;

            if (forwardCarPrefab == null && reverseCarPrefab == null && forwardMotorcyclePrefab == null && reverseMotorcyclePrefab == null)
            {
                Debug.LogWarning("[TrafficSpawner] Chưa gán Prefab xe!");
                return;
            }

            int numLanes = lanePositions.Length;
            int carsPerLane = Mathf.Max(0, totalCars / numLanes);
            int motorcyclesPerLane = Mathf.Max(0, totalMotorcycles / numLanes);

            List<SpawnedRecord> spawnedRecords = new List<SpawnedRecord>();
            int vehicleCounter = 0;

            for (int l = 0; l < numLanes; l++)
            {
                float laneX = lanePositions[l];
                bool isReverseLane = (laneX < 0f);

                float currentZ = isReverseLane ? Random.Range(440f, 470f) : Random.Range(20f, 50f);
                float zStepSign = isReverseLane ? -1f : 1f;

                List<bool> laneVehicleTypes = BuildLaneVehicleTypes(carsPerLane, motorcyclesPerLane);

                for (int v = 0; v < laneVehicleTypes.Count; v++)
                {
                    bool isMotorcycle = laneVehicleTypes[v];

                    float posX = laneX;
                    if (isMotorcycle)
                    {
                        float jitter = (v % 2 == 0) ? -motorcycleLateralOffset : motorcycleLateralOffset;
                        posX = laneX + jitter;
                    }

                    Vector3 candidatePos = new Vector3(posX, spawnHeightY, currentZ);
                    candidatePos = ResolveOverlap(candidatePos, isMotorcycle, spawnedRecords, zStepSign);

                    Quaternion spawnRot = isReverseLane ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
                    GameObject prefabToUse;

                    if (isMotorcycle)
                    {
                        prefabToUse = isReverseLane
                            ? (reverseMotorcyclePrefab ?? reverseCarPrefab ?? reverseVehiclePrefab)
                            : (forwardMotorcyclePrefab ?? forwardCarPrefab ?? forwardVehiclePrefab);
                    }
                    else
                    {
                        prefabToUse = isReverseLane
                            ? (reverseCarPrefab ?? reverseVehiclePrefab ?? forwardCarPrefab)
                            : (forwardCarPrefab ?? forwardVehiclePrefab ?? reverseCarPrefab);
                    }

                    if (prefabToUse == null) continue;

                    GameObject vehicleObj = Instantiate(prefabToUse, candidatePos, spawnRot, transform);
                    vehicleObj.name = isMotorcycle ? $"AIF_Motorcycle_Lane{l}_{v:D2}" : $"AIF_Car_Lane{l}_{v:D2}";
                    vehicleObj.tag = "Vehicle";

                    var aifAgent = vehicleObj.GetComponent<AIFVehicleAgent>();
                    if (aifAgent != null)
                    {
                        aifAgent.drivingDirection = isReverseLane ? -1 : 1;
                        aifAgent.hurryFactor = Random.Range(minHurryFactor, maxHurryFactor);
                        aifAgent.fixedHeightY = spawnHeightY;
                    }

                    var visualizer = vehicleObj.GetComponent<CarCollisionVisualizer>();
                    if (visualizer != null)
                    {
                        Color safeColor = CarCollisionVisualizer.SafeColorPalette[vehicleCounter % CarCollisionVisualizer.SafeColorPalette.Length];
                        visualizer.ResetColor();
                        visualizer.SetNormalColor(safeColor);
                    }

                    spawnedRecords.Add(new SpawnedRecord { position = candidatePos, isMotorcycle = isMotorcycle });
                    vehicleCounter++;

                    float gap = isMotorcycle ? Random.Range(minIntervalZMotorcycle, maxIntervalZMotorcycle)
                                             : Random.Range(minIntervalZCar, maxIntervalZCar);
                    currentZ += zStepSign * gap;

                    if (isReverseLane && currentZ < 30f) currentZ = 470f;
                    if (!isReverseLane && currentZ > 470f) currentZ = 30f;
                }
            }

            Debug.Log($"[TrafficSpawner] Đã spawn thành công {spawnedRecords.Count} phương tiện ({totalCars} ô tô, {totalMotorcycles} xe máy) trên 4 làn đường ngược chiều.");
        }

        private List<bool> BuildLaneVehicleTypes(int carCount, int motoCount)
        {
            List<bool> types = new List<bool>();
            int total = carCount + motoCount;
            if (total == 0) return types;

            if (carCount == 0)
            {
                for (int i = 0; i < motoCount; i++) types.Add(true);
                return types;
            }

            int motoPerCar = Mathf.Max(1, motoCount / carCount);
            int addedCars = 0;
            int addedMotos = 0;

            for (int i = 0; i < carCount; i++)
            {
                for (int m = 0; m < motoPerCar && addedMotos < motoCount; m++)
                {
                    types.Add(true);
                    addedMotos++;
                }
                types.Add(false);
                addedCars++;
            }

            while (addedMotos < motoCount)
            {
                types.Add(true);
                addedMotos++;
            }

            return types;
        }

        private Vector3 ResolveOverlap(Vector3 candidate, bool isMotorcycle, List<SpawnedRecord> existing, float zStepSign)
        {
            Vector3 resolved = candidate;
            int attempts = 0;

            while (attempts < 25)
            {
                bool conflict = false;

                for (int i = 0; i < existing.Count; i++)
                {
                    float dx = Mathf.Abs(resolved.x - existing[i].position.x);
                    float dz = Mathf.Abs(resolved.z - existing[i].position.z);

                    float minDx = (isMotorcycle && existing[i].isMotorcycle) ? 0.85f : 1.4f;
                    float minDz = (isMotorcycle && existing[i].isMotorcycle) ? 4.5f :
                                  (!isMotorcycle && !existing[i].isMotorcycle) ? 8.5f : 6.0f;

                    if (dx < minDx && dz < minDz)
                    {
                        conflict = true;
                        break;
                    }
                }

                if (!conflict) return resolved;

                resolved.z += zStepSign * 3.5f;
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
