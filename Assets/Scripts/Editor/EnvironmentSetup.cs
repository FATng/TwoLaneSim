using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace TwoLaneSimulation.Editor
{
    /// <summary>
    /// Công cụ Editor thiết lập môi trường mô phỏng 4 làn đường song song, vạch kẻ đường Zebra Crossing và Người đi bộ.
    /// Vạch kẻ đường nằm ở Layer 2 (Ignore Raycast) và KHÔNG CÓ COLLIDER.
    /// Không bao gồm logic điều khiển xe hoặc mô hình AI xe.
    /// </summary>
    public static class EnvironmentSetup
    {
        private const string ScenePath = "Assets/Scenes/TwoLaneSimulationScene.unity";
        private const string RoadMaterialPath = "Assets/Materials/RoadMaterial.mat";
        private const string GuardrailMaterialPath = "Assets/Materials/GuardrailMaterial.mat";
        private const string ZebraCrossingMaterialPath = "Assets/Materials/ZebraCrossingMaterial.mat";
        private const string PedestrianMaterialPath = "Assets/Materials/PedestrianMaterial.mat";
        private const string TailLightMaterialPath = "Assets/Materials/TailLightMaterial.mat";
        private const string HeadLightMaterialPath = "Assets/Materials/HeadLightMaterial.mat";
        private const string CarForwardMatPath = "Assets/Materials/CarBody_Forward.mat";
        private const string CarReverseMatPath = "Assets/Materials/CarBody_Reverse.mat";
        private const string CarNeutralMatPath = "Assets/Materials/CarBody_Default.mat";

        private const string PrefabDirectory = "Assets/Prefabs";
        private const string CarForwardPrefabPath = "Assets/Prefabs/Car_Forward.prefab";
        private const string CarReversePrefabPath = "Assets/Prefabs/Car_Reverse.prefab";
        private const string CarNeutralPrefabPath = "Assets/Prefabs/Car_Neutral.prefab";
        private const string PedestrianPrefabPath = "Assets/Prefabs/Pedestrian.prefab";

        [MenuItem("Tools/TwoLane Simulation/Rebuild Scene and Prefabs")]
        public static void RebuildAll()
        {
            CreateAllCarPrefabs();
            CreatePedestrianPrefab();
            SetupEnvironment();
            Debug.Log("[TwoLaneSimulation] Đã tái tạo toàn bộ Prefab, Zebra Crossings và Scene 4 làn đường thành công!");
        }

        [MenuItem("Tools/TwoLane Simulation/Create All Car Prefabs")]
        public static void CreateAllCarPrefabs()
        {
            EnsureTagExists("Vehicle");
            EnsureDirectoryExists(PrefabDirectory);

            CreateCarPrefab(CarForwardPrefabPath, CarForwardMatPath, "Car_Forward", false);
            CreateCarPrefab(CarReversePrefabPath, CarReverseMatPath, "Car_Reverse", true);
            CreateCarPrefab(CarNeutralPrefabPath, CarNeutralMatPath, "Car_Neutral", false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TwoLaneSimulation] Đã tạo thành công các Prefab xe tiêu chuẩn!");
        }

        public static void CreateCarPrefab(string prefabPath, string bodyMatPath, string prefabName, bool isReverse)
        {
            GameObject car = GameObject.CreatePrimitive(PrimitiveType.Cube);
            car.name = prefabName;
            car.tag = "Vehicle";
            car.transform.position = Vector3.zero;
            car.transform.rotation = isReverse ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            car.transform.localScale = new Vector3(1.8f, 1f, 4f);

            Rigidbody rb = car.GetComponent<Rigidbody>();
            if (rb == null) rb = car.AddComponent<Rigidbody>();
            rb.mass = 1500f;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;

            Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>(bodyMatPath);
            if (bodyMat != null) car.GetComponent<Renderer>().sharedMaterial = bodyMat;

            Material tailMat = AssetDatabase.LoadAssetAtPath<Material>(TailLightMaterialPath);
            Material headMat = AssetDatabase.LoadAssetAtPath<Material>(HeadLightMaterialPath);

            CreateLightCube(car.transform, "RearLight_Left", new Vector3(-0.38f, 0.05f, -0.505f), tailMat);
            CreateLightCube(car.transform, "RearLight_Right", new Vector3(0.38f, 0.05f, -0.505f), tailMat);
            CreateLightCube(car.transform, "HeadLight_Left", new Vector3(-0.38f, 0.05f, 0.505f), headMat);
            CreateLightCube(car.transform, "HeadLight_Right", new Vector3(0.38f, 0.05f, 0.505f), headMat);

            PrefabUtility.SaveAsPrefabAsset(car, prefabPath);
            Object.DestroyImmediate(car);
        }

        [MenuItem("Tools/TwoLane Simulation/Create Pedestrian Prefab")]
        public static void CreatePedestrianPrefab()
        {
            EnsureTagExists("Pedestrian");
            EnsureDirectoryExists(PrefabDirectory);

            GameObject ped = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ped.name = "Pedestrian";
            ped.tag = "Pedestrian";
            ped.transform.position = Vector3.zero;
            ped.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            ped.transform.localScale = new Vector3(0.6f, 1.8f, 0.6f);

            BoxCollider col = ped.GetComponent<BoxCollider>();
            if (col == null) col = ped.AddComponent<BoxCollider>();
            col.center = Vector3.zero;
            col.size = Vector3.one;

            Rigidbody rb = ped.GetComponent<Rigidbody>();
            if (rb == null) rb = ped.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;

            PedestrianAgent agent = ped.AddComponent<PedestrianAgent>();
            agent.walkingSpeed = 1.4f;
            agent.roadWidth = 16f;
            agent.targetX = 9.2f;
            agent.destroyOnArrival = true;

            Material pedMat = AssetDatabase.LoadAssetAtPath<Material>(PedestrianMaterialPath);
            if (pedMat != null) ped.GetComponent<Renderer>().sharedMaterial = pedMat;

            PrefabUtility.SaveAsPrefabAsset(ped, PedestrianPrefabPath);
            Object.DestroyImmediate(ped);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TwoLaneSimulation] Đã tạo thành công Pedestrian Prefab!");
        }

        private static void CreateLightCube(Transform parent, string name, Vector3 localPos, Material mat)
        {
            GameObject lightObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lightObj.name = name;
            Object.DestroyImmediate(lightObj.GetComponent<Collider>());
            lightObj.transform.SetParent(parent, false);
            lightObj.transform.localPosition = localPos;
            lightObj.transform.localRotation = Quaternion.identity;
            lightObj.transform.localScale = new Vector3(0.18f, 0.22f, 0.03f);
            if (mat != null) lightObj.GetComponent<Renderer>().sharedMaterial = mat;
        }

        [MenuItem("Tools/TwoLane Simulation/Setup Environment")]
        public static void SetupEnvironment()
        {
            EnsureTagExists("Vehicle");
            EnsureTagExists("Pedestrian");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            Material roadMat = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialPath);
            Material guardMat = AssetDatabase.LoadAssetAtPath<Material>(GuardrailMaterialPath);
            Material zebraMat = AssetDatabase.LoadAssetAtPath<Material>(ZebraCrossingMaterialPath);

            // 1. Mặt đường Road 4 làn
            GameObject road = GameObject.CreatePrimitive(PrimitiveType.Plane);
            road.name = "FourLaneRoad";
            road.transform.position = new Vector3(0f, 0f, 250f);
            road.transform.localScale = new Vector3(1.6f, 1f, 50f);
            if (roadMat != null) road.GetComponent<Renderer>().sharedMaterial = roadMat;

            // 2. Rào chắn 2 bên mép ngoài
            GameObject leftGuard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            leftGuard.name = "LeftGuardrail";
            leftGuard.transform.position = new Vector3(-8.15f, 0.5f, 250f);
            leftGuard.transform.localScale = new Vector3(0.3f, 1.2f, 500f);
            if (guardMat != null) leftGuard.GetComponent<Renderer>().sharedMaterial = guardMat;

            GameObject rightGuard = GameObject.CreatePrimitive(PrimitiveType.Cube);
            rightGuard.name = "RightGuardrail";
            rightGuard.transform.position = new Vector3(8.15f, 0.5f, 250f);
            rightGuard.transform.localScale = new Vector3(0.3f, 1.2f, 500f);
            if (guardMat != null) rightGuard.GetComponent<Renderer>().sharedMaterial = guardMat;

            // 3. Vạch kẻ đường Zebra Crossing (KHÔNG CÓ COLLIDER, Layer 2 Ignore Raycast)
            float[] zebraPositions = new float[] { 120f, 250f, 380f };
            float[] stripeX = new float[] { -7.2f, -6.0f, -4.8f, -3.6f, -2.4f, -1.2f, 0.0f, 1.2f, 2.4f, 3.6f, 4.8f, 6.0f, 7.2f };

            foreach (float z in zebraPositions)
            {
                GameObject crossingObj = new GameObject($"ZebraCrossing_{Mathf.RoundToInt(z)}m");
                crossingObj.layer = 2; // Ignore Raycast

                for (int i = 0; i < stripeX.Length; i++)
                {
                    GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    stripe.name = $"ZebraStripe_{i + 1:D2}";
                    stripe.layer = 2; // Ignore Raycast
                    Object.DestroyImmediate(stripe.GetComponent<Collider>()); // XÓA HOÀN TOÀN COLLIDER
                    stripe.transform.SetParent(crossingObj.transform);
                    stripe.transform.position = new Vector3(stripeX[i], 0.015f, z);
                    stripe.transform.localScale = new Vector3(0.6f, 0.01f, 4.0f);
                    if (zebraMat != null) stripe.GetComponent<Renderer>().sharedMaterial = zebraMat;
                }
            }

            // 4. Đặt các xe mẫu ở 4 làn
            GameObject forwardPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarForwardPrefabPath);
            GameObject reversePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarReversePrefabPath);

            float[] rightOuterZ = new float[] { 60f, 130f, 200f, 270f, 345f, 420f, 485f };
            float[] rightInnerZ = new float[] { 30f, 95f, 165f, 235f, 310f, 385f, 460f };
            float[] leftInnerZ  = new float[] { 470f, 405f, 335f, 265f, 190f, 120f, 45f };
            float[] leftOuterZ  = new float[] { 440f, 370f, 300f, 225f, 155f, 85f, 20f };

            SpawnLaneVehicles(forwardPrefab, "Vehicle_RightOuter", 6.0f, rightOuterZ, false);
            SpawnLaneVehicles(forwardPrefab, "Vehicle_RightInner", 2.0f, rightInnerZ, false);
            SpawnLaneVehicles(reversePrefab, "Vehicle_LeftInner", -2.0f, leftInnerZ, true);
            SpawnLaneVehicles(reversePrefab, "Vehicle_LeftOuter", -6.0f, leftOuterZ, true);

            // 5. TrafficSystem & PedestrianSpawner
            GameObject trafficSys = new GameObject("TrafficSystem");
            PedestrianSpawner spawner = trafficSys.AddComponent<PedestrianSpawner>();
            GameObject pedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PedestrianPrefabPath);
            spawner.pedestrianPrefab = pedPrefab;
            spawner.autoSpawn = true;
            spawner.roadWidth = 16f;
            spawner.designatedCrossings = zebraPositions;
            spawner.designatedCrossingBias = 0.75f;

            // 6. Camera bao quát
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                mainCam.transform.position = new Vector3(0f, 38f, 20f);
                mainCam.transform.rotation = Quaternion.Euler(32f, 0f, 0f);
                mainCam.farClipPlane = 1000f;
            }

            // 7. Lưu Scene
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TwoLaneSimulation] Đã thiết lập xong môi trường 4 làn xe, Zebra Crossing và Spawner tại: {ScenePath}");
        }

        private static void SpawnLaneVehicles(GameObject prefab, string prefix, float xPos, float[] zPositions, bool isReverse)
        {
            for (int i = 0; i < zPositions.Length; i++)
            {
                GameObject car = prefab != null ? (GameObject)PrefabUtility.InstantiatePrefab(prefab) : GameObject.CreatePrimitive(PrimitiveType.Cube);
                car.name = $"{prefix}_{i + 1:D2}";
                car.transform.position = new Vector3(xPos, 0.5f, zPositions[i]);
                car.transform.rotation = isReverse ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            }
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string[] parts = path.Split('/');
                string cur = parts[0];
                for (int i = 1; i < parts.Length; i++)
                {
                    string next = cur + "/" + parts[i];
                    if (!AssetDatabase.IsValidFolder(next))
                    {
                        AssetDatabase.CreateFolder(cur, parts[i]);
                    }
                    cur = next;
                }
            }
        }

        private static void EnsureTagExists(string tagName)
        {
            var assets = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (assets == null || assets.Length == 0) return;

            SerializedObject tagManager = new SerializedObject(assets[0]);
            SerializedProperty tagsProp = tagManager.FindProperty("tags");
            if (tagsProp == null) return;

            bool found = false;
            for (int i = 0; i < tagsProp.arraySize; i++)
            {
                if (tagsProp.GetArrayElementAtIndex(i).stringValue.Equals(tagName))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                tagsProp.InsertArrayElementAtIndex(tagsProp.arraySize);
                tagsProp.GetArrayElementAtIndex(tagsProp.arraySize - 1).stringValue = tagName;
                tagManager.ApplyModifiedProperties();
            }
        }

        private static void AddSceneToBuildSettings(string scenePath)
        {
            var currentScenes = EditorBuildSettings.scenes;
            foreach (var s in currentScenes)
            {
                if (s.path == scenePath) return;
            }

            var newScenes = new EditorBuildSettingsScene[currentScenes.Length + 1];
            currentScenes.CopyTo(newScenes, 0);
            newScenes[newScenes.Length - 1] = new EditorBuildSettingsScene(scenePath, true);
            EditorBuildSettings.scenes = newScenes;
        }
    }
}
