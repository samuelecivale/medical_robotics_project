using UnityEngine;

public class BuildCleanRCMRobot : MonoBehaviour
{
    [ContextMenu("Build Pretty RCM Robot")]
    public void Build()
    {
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        ClearChildren();

        Material robotMat = MakeMaterial("Robot_Matte_Grey", new Color(0.62f, 0.62f, 0.60f));
        Material jointMat = MakeMaterial("Joint_Dark", new Color(0.20f, 0.22f, 0.24f));
        Material toolMat = MakeMaterial("Tool_Dark_Metal", new Color(0.08f, 0.08f, 0.08f));
        Material entryMat = MakeMaterial("Entry_Red", new Color(1.0f, 0.12f, 0.08f));
        Material targetMat = MakeMaterial("Target_Green", new Color(0.15f, 0.9f, 0.25f));
        Material baseMat = MakeMaterial("Base_Dark", new Color(0.25f, 0.25f, 0.25f));
        Material floorMat = MakeMaterial("Floor_Matte", new Color(0.42f, 0.42f, 0.42f));

        CreateFloor(floorMat);
        CreateBase(baseMat);

        Transform[] joints = new Transform[6];

        Transform joint1 = CreateEmpty("Joint1", transform, new Vector3(0f, 0.2f, 0f));
        joints[0] = joint1;
        CreateJointSphere(joint1, jointMat, 0.16f);

        Transform currentJoint = joint1;

        Vector3[] linkVectors =
        {
            new Vector3(0f, 1.0f, 0f),
            new Vector3(0.85f, 0.18f, 0f),
            new Vector3(0.75f, 0.00f, 0f),
            new Vector3(0.48f, -0.05f, 0f),
            new Vector3(0.34f, 0.00f, 0f)
        };

        for (int i = 0; i < 5; i++)
        {
            Transform link = CreateEmpty("Link" + (i + 1), currentJoint, linkVectors[i]);

            CreateCylinder(
                "Link" + (i + 1) + "Mesh",
                link,
                -0.5f * linkVectors[i],
                -linkVectors[i],
                0.075f,
                robotMat
            );

            Transform nextJoint = CreateEmpty("Joint" + (i + 2), link, Vector3.zero);
            joints[i + 1] = nextJoint;
            CreateJointSphere(nextJoint, jointMat, 0.12f);

            currentJoint = nextJoint;
        }

        // Wrist / final link, to avoid the visible empty gap before the tool
        Vector3 wristVector = new Vector3(0.45f, 0f, 0f);

        CreateCylinder(
            "Link6_WristMesh",
            currentJoint,
            0.5f * wristVector,
            wristVector,
            0.055f,
            robotMat
        );

        Transform toolFrame = CreateEmpty("ToolFrame", currentJoint, wristVector);
        toolFrame.localRotation = Quaternion.Euler(0f, 90f, 0f);

        CreateCylinder(
            "ToolVisual",
            toolFrame,
            new Vector3(0f, 0f, 0.7f),
            new Vector3(0f, 0f, 1.4f),
            0.025f,
            toolMat
        );

        Transform entry = FindOrCreatePoint(
            "EntryPoint",
            toolFrame.position + toolFrame.forward * 0.45f,
            entryMat,
            0.14f
        );

        Transform target = FindOrCreatePoint(
            "TargetPoint",
            toolFrame.position + toolFrame.forward * 1.00f,
            targetMat,
            0.14f
        );

        AddLabel("ENTRY", entry.position + new Vector3(0f, 0.5f, 0f), entryMat.color);
        AddLabel("TARGET", target.position + new Vector3(0f, 0.18f, 0f), targetMat.color);

        DoubleRCMUnityController controller = GetComponent<DoubleRCMUnityController>();
        if (controller == null)
            controller = gameObject.AddComponent<DoubleRCMUnityController>();

        controller.joints = joints;
        controller.toolFrame = toolFrame;
        controller.entryPoint = entry;
        controller.targetPoint = target;

        SetupCameraAndLight();
    }

    private void ClearChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(transform.GetChild(i).gameObject);
            else
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }

    private Transform CreateEmpty(string name, Transform parent, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        return go.transform;
    }

    private void CreateCylinder(string name, Transform parent, Vector3 localPosition, Vector3 direction, float radius, Material mat)
    {
        GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cyl.name = name;
        cyl.transform.SetParent(parent, false);
        cyl.transform.localPosition = localPosition;
        cyl.transform.localRotation = Quaternion.FromToRotation(Vector3.up, direction.normalized);
        cyl.transform.localScale = new Vector3(radius, direction.magnitude * 0.5f, radius);

        Collider col = cyl.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
                Destroy(col);
            else
                DestroyImmediate(col);
        }

        Renderer r = cyl.GetComponent<Renderer>();
        r.material = mat;
    }

    private void CreateJointSphere(Transform parent, Material mat, float radius)
    {
        GameObject s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = parent.name + "_Visual";
        s.transform.SetParent(parent, false);
        s.transform.localPosition = Vector3.zero;
        s.transform.localRotation = Quaternion.identity;
        s.transform.localScale = Vector3.one * radius;

        Collider col = s.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
                Destroy(col);
            else
                DestroyImmediate(col);
        }

        Renderer r = s.GetComponent<Renderer>();
        r.material = mat;
    }

    private void CreateBase(Material mat)
    {
        GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObj.name = "RobotBase";
        baseObj.transform.SetParent(transform, false);
        baseObj.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        baseObj.transform.localRotation = Quaternion.identity;
        baseObj.transform.localScale = new Vector3(0.35f, 0.08f, 0.35f);

        Collider col = baseObj.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
                Destroy(col);
            else
                DestroyImmediate(col);
        }

        baseObj.GetComponent<Renderer>().material = mat;
    }

    private void CreateFloor(Material mat)
    {
        GameObject floor = GameObject.Find("DemoFloor");
        if (floor == null)
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "DemoFloor";
        }

        floor.transform.position = new Vector3(1.5f, -0.02f, 0f);
        floor.transform.rotation = Quaternion.identity;
        floor.transform.localScale = new Vector3(2.5f, 1f, 2.5f);

        Collider col = floor.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
                Destroy(col);
            else
                DestroyImmediate(col);
        }

        floor.GetComponent<Renderer>().material = mat;
    }

    private Transform FindOrCreatePoint(string name, Vector3 position, Material mat, float radius)
    {
        GameObject point = GameObject.Find(name);
        if (point == null)
            point = new GameObject(name);

        point.transform.position = position;
        point.transform.rotation = Quaternion.identity;
        point.transform.localScale = Vector3.one;

        for (int i = point.transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(point.transform.GetChild(i).gameObject);
            else
                DestroyImmediate(point.transform.GetChild(i).gameObject);
        }

        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name + "_Sphere";
        sphere.transform.SetParent(point.transform, false);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.one * radius;

        Collider col = sphere.GetComponent<Collider>();
        if (col != null)
        {
            if (Application.isPlaying)
                Destroy(col);
            else
                DestroyImmediate(col);
        }

        sphere.GetComponent<Renderer>().material = mat;

        return point.transform;
    }

    private void AddLabel(string text, Vector3 position, Color color)
    {
        GameObject label = new GameObject(text + "_Label");
        label.transform.position = position;
        label.transform.rotation = Quaternion.Euler(65f, 0f, 0f);
        label.transform.localScale = Vector3.one;

        TextMesh tm = label.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 64;
        tm.characterSize = 0.045f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
    }

    private Material MakeMaterial(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material mat = new Material(shader);
        mat.name = name;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);
        else
            mat.color = color;

        return mat;
    }

    private void SetupCameraAndLight()
    {
        Camera cam = Camera.main;
        if (cam != null)
        {
            cam.transform.position = new Vector3(1.9f, 1.35f, -5.3f);
            cam.transform.rotation = Quaternion.Euler(12f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = 1.75f;
            cam.clearFlags = CameraClearFlags.Skybox;
        }

        Light light = FindObjectOfType<Light>();
        if (light == null)
        {
            GameObject lightObj = new GameObject("Directional Light");
            light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
        }

        light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        light.intensity = 1.2f;
    }
}