using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace StormLab.Supercell.Rainfall.EditorTools
{
    public static class RainfallDemo
    {
        [MenuItem("Supercell Lab/Rainfall/Create Standalone Demo Scene")]
        public static void Create()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            var camera = Camera.main;
            camera.transform.position = new Vector3(0, 2, -8);
            camera.transform.rotation = Quaternion.Euler(5, 0, 0);
            camera.backgroundColor = new Color(.11f, .15f, .19f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            var rain = new GameObject("Rainfall (15 mm per hour)").AddComponent<LocalRainfall>();
            rain.targetCamera = camera;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Ground"; floor.transform.localScale = Vector3.one * 10;
            var roof = GameObject.CreatePrimitive(PrimitiveType.Cube);
            roof.name = "Shelter roof (move camera beneath to test)";
            roof.transform.position = new Vector3(5, 4, 2); roof.transform.localScale = new Vector3(6, .3f, 6);
            var gauge = new GameObject("Outdoor rain gauge").AddComponent<RainGauge>();
            gauge.transform.position = new Vector3(0, .2f, 0); gauge.fallbackRainfall = rain;
            var covered = new GameObject("Covered rain gauge").AddComponent<RainGauge>();
            covered.transform.position = new Vector3(5, .2f, 2); covered.fallbackRainfall = rain;
            Selection.activeGameObject = rain.gameObject;
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        }
    }
}
