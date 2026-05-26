using UnityEngine;

public class BuildROSAStyleRCMS : MonoBehaviour
{
    [ContextMenu("Build ROSA-like RCM Scene")]
    public void Build()
    {
        transform.position = Vector3.zero;
        transform.rotation = Quaternion.identity;
        transform.localScale = Vector3.one;

        ClearRobotChildren();
        DestroyIfExists("SurgicalSkull");
        DestroyIfExists("EntryPoint");
        DestroyIfExists("TargetPoint");
        DestroyIfExists("Entry_Label");
        DestroyIfExists("Target_Label");
        DestroyIfExists("Trajectory_Line");
        DestroyIfExists("DemoFloor");

        DisableOldControllers();

        Material robotMat = MakeMaterial("ROSA_White", new Color(0.82f, 0.84f, 0.84f, 1f), false);
        Material darkMat = MakeMaterial("ROSA_Dark", new Color(0.18f, 0.20f, 0.22f, 1f), false);
        Material jointMat = MakeMaterial("Joint_Dark", new Color(0.08f, 0.09f, 0.10f, 1f), false);
        Material toolMat = MakeMaterial("Tool_Black", new Color(0.02f, 0.02f, 0.02f, 1f), false);
        Material skullMat = MakeMaterial("Skull_Transparent", new Color(0.95f, 0.78f, 0.62f, 0.28f), true);
        Material entryMat = MakeMaterial("Entry_Red", new Color(1f, 0.05f, 0.02f, 1f), false);
        Material targetMat = MakeMaterial("Target_Green", new Color(0.1f, 1f, 0.25f, 1f), false);
        Material trajectoryMat = MakeMaterial("Trajectory_Blue", new Color(0.1f, 0.35f, 1f, 1f), false);
        Material floorMat = MakeMaterial("Floor_Grey", new Color(0.45f, 0.45f, 0.45f, 1f), false);

        CreateFloor(floorMat);
        CreateBaseAndColumn(darkMat, robotMat);

        Transform[] joints = new Transform[6];

        Transform joint1 = CreateEmpty("Joint1", transform, new Vector3(0f, 0.62f, 0f));
        joints[0] = joint1;
        CreateJointVisual(joint1, jointMat, 0.18f);

        Transform currentJoint = joint1;

        Vector3[] linkVectors =
        {
            new Vector3(0.00f, 0.62f, 0.00f),
            new Vector3(0.58f, 0.28f, 0.00f),
            new Vector3(0.68f, -0.08f, 0.00f),
            new Vector3(0.38f, 0.04f, 0.00f),
            new Vector3(0.27f, 0.00f, 0.00f)
        };

        float[] radii =
        {
            0.085f,
            0.075f,
            0.065f,
            0.050f,
            0.045f
        };

        for (int i = 0; i < 5; i++)
        {
            Transform link = CreateEmpty("Link" + (i + 1), currentJoint, linkVectors[i]);

            CreateCylinder(
                "Link" + (i + 1) + "_Body",
                link,
                -0.5f * linkVectors[i],
                -linkVectors[i],
                radii[i],
                robotMat
            );

            Transform nextJoint = CreateEmpty("Joint" + (i + 2), link, Vector3.zero);
            joints[i + 1] = nextJoint;

            float jointRadius = i < 2 ? 0.14f : 0.105f;
            CreateJointVisual(nextJoint, jointMat, jointRadius);

            currentJoint = nextJoint;
        }

        Vector3 finalGuideVector = new Vector3(0.50f, 0f, 0f);

        CreateCylinder(
            "Distal_Tool_Guide",
            currentJoint,
            0.5f * finalGuideVector,
            finalGuideVector,
            0.04f,
            darkMat
        );

        Transform toolFrame = CreateEmpty("ToolFrame", currentJoint, finalGuideVector);
        toolFrame.localRotation = Quaternion.Euler(0f, 90f, 0f);

        const float needleLength = 1.50f;

        CreateCylinder(
            "ToolVisual_Needle",
            toolFrame,
            new Vector3(0f, 0f, needleLength * 0.5f),
            new Vector3(0f, 0f, needleLength),
            0.018f,
            toolMat
        );

        Transform toolTip = CreateEmpty("ToolTip", toolFrame, new Vector3(0f, 0f, needleLength));
        CreatePointVisual("ToolTip_Visual", toolTip, toolMat, 0.035f);

        // Entry is on the skull boundary.
        // Target is inside the skull.
        // The ideal needle axis is the segment entry -> target.
        Vector3 skullCenter = new Vector3(3.35f, 1.32f, 0.26f);
        Vector3 entryPos = new Vector3(3.06f, 1.32f, 0.26f);
        Vector3 targetPos = new Vector3(3.43f, 1.32f, 0.26f);

        GameObject skull = CreateSkull(skullCenter, skullMat);

        Transform entry = CreatePoint("EntryPoint", entryPos, entryMat, 0.075f);
        Transform target = CreatePoint("TargetPoint", targetPos, targetMat, 0.075f);

        CreateCylinder(
            "Trajectory_Line",
            null,
            0.5f * (entryPos + targetPos),
            targetPos - entryPos,
            0.010f,
            trajectoryMat,
            true
        );

        AddLabel("ENTRY", entryPos + new Vector3(0f, 0.16f, 0f), entryMat.color);
        AddLabel("TARGET", targetPos + new Vector3(0f, 0.16f, 0f), targetMat.color);

        DoubleRCMUnityController2 controller = GetComponent<DoubleRCMUnityController2>();
        if (controller == null)
            controller = gameObject.AddComponent<DoubleRCMUnityController2>();

        controller.joints = joints;
        controller.jointAxesLocal = new Vector3[]
        {
            Vector3.up,
            Vector3.forward,
            Vector3.forward,
            Vector3.right,
            Vector3.forward,
            Vector3.up
        };

        controller.toolFrame = toolFrame;
        controller.toolTip = toolTip;
        controller.autoCreateToolTip = false;
        controller.toolLength = needleLength;

        controller.entryPoint = entry;
        controller.targetPoint = target;
        controller.skullCollider = skull.GetComponent<Collider>();
        controller.skullPlaneNormalReference = entry;

        controller.mode = DoubleRCMUnityController2.RCMMode.Double;

        controller.solverIterations = 1;
        controller.damping = 0.20f;
        controller.finiteDifferenceDeg = 0.5f;
        controller.maxDeltaDegPerIteration = 0.08f;
        controller.ikStepScale = 0.08f;

        controller.entryWeight = 3.0f;
        controller.targetTipWeight = 2.0f;
        controller.skullAvoidanceWeight = 4.0f;
        controller.avoidArmLinksFromSkull = true;
        controller.armSkullAvoidanceWeight = 12.0f;
        controller.armSafetyMargin = 0.10f;
        controller.armAvoidanceSamplesPerSegment = 6;
        controller.useFiniteNeedleSegmentForEntry = true;

        controller.useDemoStartPose = true;
        controller.demoWaitBeforeSolving = 1.0f;
        controller.demoJointAnglesDeg = new float[]
        {
            45f,
            -35f,
            45f,
            -25f,
            30f,
            20f
        };

        controller.logToCsv = true;
        controller.logFileName = "rcm_log.csv";
        controller.logEverySeconds = 0.02f;

        SetupCameraAndLights();
    }

    private void DisableOldControllers()
    {
        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();

        foreach (MonoBehaviour behaviour in behaviours)
        {
            if (behaviour == null)
                continue;

            if (behaviour.GetType().Name == "DoubleRCMUnityController")
                behaviour.enabled = false;
        }
    }

    private void ClearRobotChildren()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
                Destroy(transform.GetChild(i).gameObject);
            else
                DestroyImmediate(transform.GetChild(i).gameObject);
        }
    }

    private void DestroyIfExists(string objectName)
    {
        GameObject obj = GameObject.Find(objectName);

        if (obj == null)
            return;

        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    private Transform CreateEmpty(string name, Transform parent, Vector3 localPosition)
    {
        GameObject go = new GameObject(name);

        if (parent != null)
            go.transform.SetParent(parent, false);

        go.transform.localPosition = localPosition;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;

        return go.transform;
    }

    private void CreateBaseAndColumn(Material darkMat, Material robotMat)
    {
        GameObject baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        baseObj.name = "ROSA_Base";
        baseObj.transform.SetParent(transform, false);
        baseObj.transform.localPosition = new Vector3(0f, 0.08f, 0f);
        baseObj.transform.localScale = new Vector3(0.42f, 0.08f, 0.42f);
        RemoveCollider(baseObj);
        baseObj.GetComponent<Renderer>().material = darkMat;

        GameObject column = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        column.name = "ROSA_Vertical_Column";
        column.transform.SetParent(transform, false);
        column.transform.localPosition = new Vector3(0f, 0.34f, 0f);
        column.transform.localScale = new Vector3(0.13f, 0.28f, 0.13f);
        RemoveCollider(column);
        column.GetComponent<Renderer>().material = robotMat;

        GameObject shoulderCover = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shoulderCover.name = "ROSA_Shoulder_Cover";
        shoulderCover.transform.SetParent(transform, false);
        shoulderCover.transform.localPosition = new Vector3(0f, 0.62f, 0f);
        shoulderCover.transform.localScale = new Vector3(0.34f, 0.22f, 0.34f);
        RemoveCollider(shoulderCover);
        shoulderCover.GetComponent<Renderer>().material = robotMat;
    }

    private GameObject CreateSkull(Vector3 center, Material skullMat)
    {
        GameObject skull = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        skull.name = "SurgicalSkull";
        skull.transform.position = center;
        skull.transform.rotation = Quaternion.identity;
        skull.transform.localScale = new Vector3(0.58f, 0.62f, 0.48f);

        Renderer renderer = skull.GetComponent<Renderer>();
        if (renderer != null)
            renderer.material = skullMat;

        SphereCollider collider = skull.GetComponent<SphereCollider>();
        if (collider == null)
            collider = skull.AddComponent<SphereCollider>();

        return skull;
    }

    private Transform CreatePoint(string name, Vector3 position, Material mat, float radius)
    {
        GameObject point = new GameObject(name);
        point.transform.position = position;
        point.transform.rotation = Quaternion.identity;
        point.transform.localScale = Vector3.one;

        CreatePointVisual(name + "_Sphere", point.transform, mat, radius);

        return point.transform;
    }

    private void CreatePointVisual(string name, Transform parent, Material mat, float radius)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = name;
        sphere.transform.SetParent(parent, false);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localScale = Vector3.one * radius;
        RemoveCollider(sphere);
        sphere.GetComponent<Renderer>().material = mat;
    }

    private void CreateJointVisual(Transform parent, Material mat, float radius)
    {
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = parent.name + "_Visual";
        sphere.transform.SetParent(parent, false);
        sphere.transform.localPosition = Vector3.zero;
        sphere.transform.localRotation = Quaternion.identity;
        sphere.transform.localScale = Vector3.one * radius;
        RemoveCollider(sphere);
        sphere.GetComponent<Renderer>().material = mat;
    }

    private void CreateCylinder(
        string name,
        Transform parent,
        Vector3 localPosition,
        Vector3 direction,
        float radius,
        Material mat,
        bool worldSpace = false
    )
    {
        if (direction.sqrMagnitude < 1e-8f)
            return;

        GameObject cyl = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cyl.name = name;

        Quaternion rot = Quaternion.FromToRotation(Vector3.up, direction.normalized);

        if (parent != null)
        {
            cyl.transform.SetParent(parent, false);

            if (worldSpace)
            {
                cyl.transform.position = localPosition;
                cyl.transform.rotation = rot;
            }
            else
            {
                cyl.transform.localPosition = localPosition;
                cyl.transform.localRotation = rot;
            }
        }
        else
        {
            cyl.transform.position = localPosition;
            cyl.transform.rotation = rot;
        }

        cyl.transform.localScale = new Vector3(radius, direction.magnitude * 0.5f, radius);

        RemoveCollider(cyl);
        cyl.GetComponent<Renderer>().material = mat;
    }

    private void CreateFloor(Material mat)
    {
        GameObject floor = GameObject.Find("DemoFloor");

        if (floor == null)
        {
            floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "DemoFloor";
        }

        floor.transform.position = new Vector3(1.8f, -0.02f, 0f);
        floor.transform.rotation = Quaternion.identity;
        floor.transform.localScale = new Vector3(3.0f, 1f, 3.0f);

        RemoveCollider(floor);
        floor.GetComponent<Renderer>().material = mat;
    }

    private void AddLabel(string text, Vector3 position, Color color)
    {
        GameObject label = new GameObject(text + "_Label");
        label.transform.position = position;
        label.transform.rotation = Quaternion.Euler(65f, 0f, 0f);
        label.transform.localScale = Vector3.one;

        TextMesh tm = label.AddComponent<TextMesh>();
        tm.text = text;
        tm.fontSize = 48;
        tm.characterSize = 0.018f;
        tm.anchor = TextAnchor.MiddleCenter;
        tm.alignment = TextAlignment.Center;
        tm.color = color;
    }

    private Material MakeMaterial(string name, Color color, bool transparent)
    {
        Shader shader =
            Shader.Find("Universal Render Pipeline/Lit") ??
            Shader.Find("HDRP/Lit") ??
            Shader.Find("Standard") ??
            Shader.Find("Sprites/Default") ??
            Shader.Find("Unlit/Color");

        Material mat = new Material(shader);
        mat.name = name;

        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", color);

        if (mat.HasProperty("_Color"))
            mat.SetColor("_Color", color);

        if (transparent)
        {
            mat.renderQueue = 3000;

            if (mat.HasProperty("_Surface"))
                mat.SetFloat("_Surface", 1f);

            if (mat.HasProperty("_Blend"))
                mat.SetFloat("_Blend", 0f);

            if (mat.HasProperty("_AlphaClip"))
                mat.SetFloat("_AlphaClip", 0f);

            if (mat.HasProperty("_ZWrite"))
                mat.SetFloat("_ZWrite", 0f);

            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);

            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_ALPHABLEND_ON");
        }

        return mat;
    }

    private void RemoveCollider(GameObject obj)
    {
        Collider col = obj.GetComponent<Collider>();

        if (col == null)
            return;

        if (Application.isPlaying)
            Destroy(col);
        else
            DestroyImmediate(col);
    }

    private void SetupCameraAndLights()
    {
        Camera cam = Camera.main;

        if (cam != null)
        {
            cam.transform.position = new Vector3(1.95f, 1.55f, -5.4f);
            cam.transform.rotation = Quaternion.Euler(13f, 0f, 0f);
            cam.orthographic = true;
            cam.orthographicSize = 1.75f;
        }

        Light existing = Object.FindAnyObjectByType<Light>();

        if (existing == null)
        {
            GameObject lightObj = new GameObject("Directional Light");
            existing = lightObj.AddComponent<Light>();
            existing.type = LightType.Directional;
        }

        existing.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
        existing.intensity = 1.25f;
    }
}
