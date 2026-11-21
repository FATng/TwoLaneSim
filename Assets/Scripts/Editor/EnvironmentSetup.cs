using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using Unity.InferenceEngine;

namespace TwoLaneSimulation.Editor
{
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
        private const string ModelAssetPath = "Assets/Models/traffic_aif_agent.onnx";
        private const string MotorcycleModelAssetPath = "Assets/Models/traffic_motorcycle_agent.onnx";

        private const string PrefabDirectory = "Assets/Prefabs";
        private const string CarForwardPrefabPath = "Assets/Prefabs/Car_Forward.prefab";
        private const string CarReversePrefabPath = "Assets/Prefabs/Car_Reverse.prefab";
        private const string CarNeutralPrefabPath = "Assets/Prefabs/Car_Neutral.prefab";
        private const string MotorcycleForwardPrefabPath = "Assets/Prefabs/Motorcycle_Forward.prefab";
        private const string MotorcycleReversePrefabPath = "Assets/Prefabs/Motorcycle_Reverse.prefab";
        private const string MotorcycleNeutralPrefabPath = "Assets/Prefabs/Motorcycle_Neutral.prefab";
        private const string PedestrianPrefabPath = "Assets/Prefabs/Pedestrian.prefab";

        [MenuItem("Tools/TwoLane Simulation/Rebuild Scene and Prefabs")]
        public static void RebuildAll()
        {
            CreateAllCarPrefabs();
            CreateAllMotorcyclePrefabs();
            CreatePedestrianPrefab();
            SetupEnvironment();
            Debug.Log("[TwoLaneSimulation] Đã tái tạo toàn bộ Prefab (Ô tô + Xe máy), Zebra Crossings và Scene 4 làn đường AI thành công!");
        }

        [MenuItem("Tools/TwoLane Simulation/Create All Car Prefabs (with Sentis AI)")]
        public static void CreateAllCarPrefabs()
        {
            EnsureTagExists("Vehicle");
            EnsureDirectoryExists(PrefabDirectory);

            CreateCarPrefab(CarForwardPrefabPath, CarForwardMatPath, "Car_Forward", false);
            CreateCarPrefab(CarReversePrefabPath, CarReverseMatPath, "Car_Reverse", true);
            CreateCarPrefab(CarNeutralPrefabPath, CarNeutralMatPath, "Car_Neutral", false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TwoLaneSimulation] Đã tạo thành công các Prefab ô tô tiêu chuẩn tích hợp AI Sentis!");
        }

        [MenuItem("Tools/TwoLane Simulation/Create All Motorcycle Prefabs (with Sentis AI)")]
        public static void CreateAllMotorcyclePrefabs()
        {
            EnsureTagExists("Vehicle");
            EnsureDirectoryExists(PrefabDirectory);

            CreateMotorcyclePrefab(MotorcycleForwardPrefabPath, CarForwardMatPath, "Motorcycle_Forward", false);
            CreateMotorcyclePrefab(MotorcycleReversePrefabPath, CarReverseMatPath, "Motorcycle_Reverse", true);
            CreateMotorcyclePrefab(MotorcycleNeutralPrefabPath, CarNeutralMatPath, "Motorcycle_Neutral", false);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TwoLaneSimulation] Đã tạo thành công các Prefab xe máy tiêu chuẩn tích hợp AI Sentis!");
        }

        public static void CreateMotorcyclePrefab(string prefabPath, string bodyMatPath, string prefabName, bool isReverse)
        {
            GameObject moto = GameObject.CreatePrimitive(PrimitiveType.Cube);
            moto.name = prefabName;
            moto.tag = "Vehicle";
            moto.transform.position = Vector3.zero;
            moto.transform.rotation = isReverse ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity;
            moto.transform.localScale = new Vector3(0.6f, 1f, 2.666667f);

            Rigidbody rb = moto.GetComponent<Rigidbody>();
            if (rb == null) rb = moto.AddComponent<Rigidbody>();
            rb.mass = 300f;
            rb.useGravity = false;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationY | RigidbodyConstraints.FreezeRotationZ;

            Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>(bodyMatPath);
            if (bodyMat != null) moto.GetComponent<Renderer>().sharedMaterial = bodyMat;

            Material tailMat = AssetDatabase.LoadAssetAtPath<Material>(TailLightMaterialPath);
            Material headMat = AssetDatabase.LoadAssetAtPath<Material>(HeadLightMaterialPath);

            GameObject rLeft = CreateLightCube(moto.transform, "RearLight_Left", new Vector3(-0.35f, 0.05f, -0.505f), tailMat);
            GameObject rRight = CreateLightCube(moto.transform, "RearLight_Right", new Vector3(0.35f, 0.05f, -0.505f), tailMat);
            CreateLightCube(moto.transform, "HeadLight_Left", new Vector3(-0.35f, 0.05f, 0.505f), headMat);
            CreateLightCube(moto.transform, "HeadLight_Right", new Vector3(0.35f, 0.05f, 0.505f), headMat);

            var lightsCtrl = moto.AddComponent<CarLightsController>();
            if (rLeft != null) lightsCtrl.leftTailLightRenderer = rLeft.GetComponent<Renderer>();
            if (rRight != null) lightsCtrl.rightTailLightRenderer = rRight.GetComponent<Renderer>();

            var visualizer = moto.AddComponent<CarCollisionVisualizer>();
            visualizer.bodyRenderer = moto.GetComponent<Renderer>();

            var aifAgent = moto.AddComponent<AIFVehicleAgent>();
            aifAgent.drivingDirection = isReverse ? -1 : 1;
            aifAgent.roadSectionCenter = isReverse ? -4.0f : 4.0f;
            aifAgent.minSafeX = isReverse ? -7.4f : 0.6f;
            aifAgent.maxSafeX = isReverse ? -0.6f : 7.4f;
            aifAgent.allowedLanes = isReverse ? new float[] { -6f, -2f } : new float[] { 2f, 6f };
            aifAgent.maxAcceleration = 12.0f;
            aifAgent.motorcycleMaxAcceleration = 12.0f;
            aifAgent.maxHeadingAngle = 28.0f;
            aifAgent.motorcycleMaxHeadingAngle = 28.0f;

            ModelAsset onnxModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(MotorcycleModelAssetPath);
            if (onnxModel == null) onnxModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelAssetPath);
            if (onnxModel != null)
            {
                var serializedAgent = new SerializedObject(aifAgent);
                serializedAgent.FindProperty("onnxModelAsset").objectReferenceValue = onnxModel;
                serializedAgent.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(moto, prefabPath);
            Object.DestroyImmediate(moto);
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

            GameObject rLeft = CreateLightCube(car.transform, "RearLight_Left", new Vector3(-0.38f, 0.05f, -0.505f), tailMat);
            GameObject rRight = CreateLightCube(car.transform, "RearLight_Right", new Vector3(0.38f, 0.05f, -0.505f), tailMat);
            CreateLightCube(car.transform, "HeadLight_Left", new Vector3(-0.38f, 0.05f, 0.505f), headMat);
            CreateLightCube(car.transform, "HeadLight_Right", new Vector3(0.38f, 0.05f, 0.505f), headMat);

            var lightsCtrl = car.AddComponent<CarLightsController>();
            if (rLeft != null) lightsCtrl.leftTailLightRenderer = rLeft.GetComponent<Renderer>();
            if (rRight != null) lightsCtrl.rightTailLightRenderer = rRight.GetComponent<Renderer>();

            var visualizer = car.AddComponent<CarCollisionVisualizer>();
            visualizer.bodyRenderer = car.GetComponent<Renderer>();

            var aifAgent = car.AddComponent<AIFVehicleAgent>();
            aifAgent.drivingDirection = isReverse ? -1 : 1;
            aifAgent.roadSectionCenter = isReverse ? -4.0f : 4.0f;
            aifAgent.minSafeX = isReverse ? -7.4f : 0.6f;
            aifAgent.maxSafeX = isReverse ? -0.6f : 7.4f;
            aifAgent.allowedLanes = isReverse ? new float[] { -6f, -2f } : new float[] { 2f, 6f };

            ModelAsset onnxModel = AssetDatabase.LoadAssetAtPath<ModelAsset>(ModelAssetPath);
            if (onnxModel != null)
            {
                var serializedAgent = new SerializedObject(aifAgent);
                serializedAgent.FindProperty("onnxModelAsset").objectReferenceValue = onnxModel;
                serializedAgent.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(car, prefabPath);
            Object.DestroyImmediate(car);
        }

        [MenuItem("Tools/TwoLane Simulation/Create Pedestrian Prefab")]
        public static void CreatePedestrianPrefab()
        {
            EnsureTagExists("Pedestrian");
            EnsureDirectoryExists(PrefabDirectory);

            GameObject ped = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            ped.name = "Pedestrian";
            ped.tag = "Pedestrian";
            ped.transform.position = Vector3.zero;
            ped.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            ped.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);

            var col = ped.GetComponent<CapsuleCollider>();
            if (col != null) col.isTrigger = true;

            Rigidbody rb = ped.GetComponent<Rigidbody>();
            if (rb == null) rb = ped.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

            Material pedMat = AssetDatabase.LoadAssetAtPath<Material>(PedestrianMaterialPath);
            if (pedMat != null) ped.GetComponent<Renderer>().sharedMaterial = pedMat;

            ped.AddComponent<PedestrianAgent>();

            PrefabUtility.SaveAsPrefabAsset(ped, PedestrianPrefabPath);
            Object.DestroyImmediate(ped);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[TwoLaneSimulation] Đã tạo thành công Prefab Pedestrian!");
        }

        [MenuItem("Tools/TwoLane Simulation/Setup Environment")]
        public static void SetupEnvironment()
        {
            EnsureTagExists("Vehicle");
            EnsureTagExists("Pedestrian");

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cam = new GameObject("Main Camera");
            cam.tag = "MainCamera";
            Camera c = cam.AddComponent<Camera>();
            c.clearFlags = CameraClearFlags.Skybox;
            c.fieldOfView = 60f;
            c.farClipPlane = 1000f;
            cam.AddComponent<AudioListener>();
            cam.transform.position = new Vector3(0f, 38f, 20f);
            cam.transform.rotation = Quaternion.Euler(32f, 0f, 0f);

            GameObject lightObj = new GameObject("Directional Light");
            Light l = lightObj.AddComponent<Light>();
            l.type = LightType.Directional;
            l.color = new Color(1f, 0.96f, 0.84f);
            l.intensity = 1.0f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            GameObject road = GameObject.CreatePrimitive(PrimitiveType.Plane);
            road.name = "FourLaneRoad";
            road.transform.position = new Vector3(0f, 0f, 250f);
            road.transform.localScale = new Vector3(1.6f, 1f, 50f);
            Material roadMat = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialPath);
            if (roadMat != null) road.GetComponent<Renderer>().sharedMaterial = roadMat;

            Material guardMat = AssetDatabase.LoadAssetAtPath<Material>(GuardrailMaterialPath);
            CreateGuardrail("LeftGuardrail", -8.15f, guardMat);
            CreateGuardrail("RightGuardrail", 8.15f, guardMat);

            Material zebraMat = AssetDatabase.LoadAssetAtPath<Material>(ZebraCrossingMaterialPath);
            CreateZebraCrossing(120f, zebraMat);
            CreateZebraCrossing(250f, zebraMat);
            CreateZebraCrossing(380f, zebraMat);

            GameObject trafficSystem = new GameObject("TrafficSystem");
            trafficSystem.AddComponent<TrafficFieldManager>();

            var spawner = trafficSystem.AddComponent<TrafficSpawner>();
            spawner.forwardCarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarForwardPrefabPath);
            spawner.reverseCarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CarReversePrefabPath);
            spawner.forwardMotorcyclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MotorcycleForwardPrefabPath);
            spawner.reverseMotorcyclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MotorcycleReversePrefabPath);
            spawner.forwardVehiclePrefab = spawner.forwardCarPrefab;
            spawner.reverseVehiclePrefab = spawner.reverseCarPrefab;

            var pedSpawner = trafficSystem.AddComponent<PedestrianSpawner>();
            pedSpawner.pedestrianPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PedestrianPrefabPath);

            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[TwoLaneSimulation] Đã thiết lập hoàn chỉnh Scene tại: " + ScenePath);
        }

        private static void CreateGuardrail(string name, float x, Material mat)
        {
            GameObject gr = GameObject.CreatePrimitive(PrimitiveType.Cube);
            gr.name = name;
            gr.transform.position = new Vector3(x, 0.5f, 250f);
            gr.transform.localScale = new Vector3(0.3f, 1.2f, 500f);
            if (mat != null) gr.GetComponent<Renderer>().sharedMaterial = mat;
        }

        private static void CreateZebraCrossing(float z, Material mat)
        {
            GameObject group = new GameObject($"ZebraCrossing_{Mathf.RoundToInt(z)}m");
            group.layer = 2;
            group.transform.position = new Vector3(0f, 0f, z);

            int stripeCount = 15;
            float startX = -7.0f;
            float spacing = 1.0f;

            for (int i = 0; i < stripeCount; i++)
            {
                float x = startX + i * spacing;
                GameObject stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
                stripe.name = $"ZebraStripe_{i + 1:02d}";
                stripe.layer = 2;
                stripe.transform.SetParent(group.transform);
                stripe.transform.localPosition = new Vector3(x, 0.002f, 0f);
                stripe.transform.localScale = new Vector3(0.5f, 0.002f, 4f);

                var col = stripe.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);

                if (mat != null) stripe.GetComponent<Renderer>().sharedMaterial = mat;
            }
        }

        private static GameObject CreateLightCube(Transform parent, string name, Vector3 localPos, Material mat)
        {
            GameObject lightObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            lightObj.name = name;
            lightObj.transform.SetParent(parent);
            lightObj.transform.localPosition = localPos;
            lightObj.transform.localScale = new Vector3(0.15f, 0.15f, 0.05f);

            var col = lightObj.GetComponent<Collider>();
            if (col != null) Object.DestroyImmediate(col);

            if (mat != null) lightObj.GetComponent<Renderer>().sharedMaterial = mat;
            return lightObj;
        }

        private static void EnsureDirectoryExists(string path)
        {
            if (!AssetDatabase.IsValidFolder(path))
            {
                string parent = System.IO.Path.GetDirectoryName(path).Replace("\\", "/");
                string folder = System.IO.Path.GetFileName(path);
                AssetDatabase.CreateFolder(parent, folder);
            }
        }

        private static void EnsureTagExists(string tag)
        {
            UnityEngine.Object[] asset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset");
            if (asset != null && asset.Length > 0)
            {
                SerializedObject so = new SerializedObject(asset[0]);
                SerializedProperty tags = so.FindProperty("tags");
                for (int i = 0; i < tags.arraySize; i++)
                {
                    if (tags.GetArrayElementAtIndex(i).stringValue.Equals(tag)) return;
                }
                tags.InsertArrayElementAtIndex(tags.arraySize);
                tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = tag;
                so.ApplyModifiedProperties();
                so.Update();
            }
        }
    }
}
