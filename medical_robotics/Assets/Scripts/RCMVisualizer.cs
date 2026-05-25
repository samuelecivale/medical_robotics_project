using UnityEngine;

public class RCMVisualizer : MonoBehaviour
{
    public Transform toolFrame;
    public Transform entryPoint;
    public Transform targetPoint;

    public float lineLength = 0.6f;
    public float sphereRadius = 0.015f;

    void OnDrawGizmos()
    {
        if (toolFrame != null)
        {
            Vector3 p0 = toolFrame.position;
            Vector3 axis = toolFrame.forward.normalized;

            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(p0 - lineLength * axis, p0 + lineLength * axis);
        }

        if (entryPoint != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(entryPoint.position, sphereRadius);
        }

        if (targetPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawSphere(targetPoint.position, sphereRadius);
        }

        if (toolFrame != null && entryPoint != null)
            DrawPointToToolLine(entryPoint.position, Color.yellow);

        if (toolFrame != null && targetPoint != null)
            DrawPointToToolLine(targetPoint.position, Color.red);
    }

    void DrawPointToToolLine(Vector3 point, Color color)
    {
        Vector3 p0 = toolFrame.position;
        Vector3 axis = toolFrame.forward.normalized;

        Vector3 v = point - p0;
        Vector3 closest = p0 + Vector3.Dot(v, axis) * axis;

        Gizmos.color = color;
        Gizmos.DrawLine(point, closest);
    }
}
