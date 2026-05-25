using UnityEngine;
using System.IO;
using System.Globalization;

public class DoubleRCMUnityController : MonoBehaviour
{
    public enum RCMMode
    {
        Entry,
        Target,
        Double
    }

    [Header("Robot")]
    public Transform[] joints;

    public Vector3[] jointAxesLocal =
    {
        Vector3.up,
        Vector3.forward,
        Vector3.forward,
        Vector3.right,
        Vector3.forward,
        Vector3.up
    };

    [Header("RCM references")]
    public Transform toolFrame;
    public Transform entryPoint;
    public Transform targetPoint;

    [Header("Controller")]
    public RCMMode mode = RCMMode.Double;
    public bool solveIK = true;
    public int solverIterations = 2;
    public float damping = 0.12f;
    public float finiteDifferenceDeg = 0.5f;
    public float maxDeltaDegPerIteration = 0.4f;

    [Header("Debug")]
    public float entryErrorMm;
    public float targetErrorMm;

    [Header("CSV Logging")]
    public bool logToCsv = true;
    public string logFileName = "rcm_log.csv";
    public float logEverySeconds = 0.02f;

    private Quaternion[] initialRotations;
    private StreamWriter logWriter;
    private float lastLogTime;

    private void Start()
    {
        StoreInitialPose();
        StartLogging();
    }

    private void Update()
    {
        HandleInput();

        if (!ReferencesAreValid())
            return;

        if (solveIK)
        {
            for (int i = 0; i < solverIterations; i++)
                SolveOneIKStep();
        }

        entryErrorMm = PointToToolAxisDistance(entryPoint.position) * 1000f;
        targetErrorMm = PointToToolAxisDistance(targetPoint.position) * 1000f;

        LogSample();
    }

    private bool ReferencesAreValid()
    {
        return joints != null &&
               joints.Length > 0 &&
               toolFrame != null &&
               entryPoint != null &&
               targetPoint != null;
    }

    private void HandleInput()
    {
        if (Input.GetKeyDown(KeyCode.Alpha1))
            mode = RCMMode.Entry;

        if (Input.GetKeyDown(KeyCode.Alpha2))
            mode = RCMMode.Target;

        if (Input.GetKeyDown(KeyCode.Alpha3))
            mode = RCMMode.Double;

        if (Input.GetKeyDown(KeyCode.R))
            ResetPose();
    }

    private void StoreInitialPose()
    {
        if (joints == null)
            return;

        initialRotations = new Quaternion[joints.Length];

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
                initialRotations[i] = joints[i].localRotation;
        }
    }

    private void ResetPose()
    {
        if (initialRotations == null || joints == null)
            return;

        for (int i = 0; i < joints.Length; i++)
        {
            if (joints[i] != null)
                joints[i].localRotation = initialRotations[i];
        }
    }

    private void SolveOneIKStep()
    {
        Vector3[] errorVectors = GetCurrentErrorVectors();

        int m = errorVectors.Length * 3;
        int n = joints.Length;

        float[] e = new float[m];

        for (int i = 0; i < errorVectors.Length; i++)
        {
            e[i * 3 + 0] = errorVectors[i].x;
            e[i * 3 + 1] = errorVectors[i].y;
            e[i * 3 + 2] = errorVectors[i].z;
        }

        float[,] J = ComputeNumericalJacobian(e, m, n);
        float[] dq = SolveDampedLeastSquares(J, e, m, n);

        for (int i = 0; i < n; i++)
        {
            if (joints[i] == null)
                continue;

            Vector3 axis = GetJointAxis(i);

            float deltaDeg = dq[i] * Mathf.Rad2Deg;
            deltaDeg = Mathf.Clamp(deltaDeg, -maxDeltaDegPerIteration, maxDeltaDegPerIteration);

            joints[i].localRotation =
                joints[i].localRotation * Quaternion.AngleAxis(deltaDeg, axis);
        }
    }

    private Vector3[] GetCurrentErrorVectors()
    {
        if (mode == RCMMode.Entry)
        {
            return new Vector3[]
            {
                PointToToolAxisErrorVector(entryPoint.position)
            };
        }

        if (mode == RCMMode.Target)
        {
            return new Vector3[]
            {
                PointToToolAxisErrorVector(targetPoint.position)
            };
        }

        return new Vector3[]
        {
            PointToToolAxisErrorVector(entryPoint.position),
            PointToToolAxisErrorVector(targetPoint.position)
        };
    }

    private float[,] ComputeNumericalJacobian(float[] baseError, int m, int n)
    {
        float[,] J = new float[m, n];

        float epsRad = finiteDifferenceDeg * Mathf.Deg2Rad;

        Quaternion[] originalRotations = new Quaternion[n];

        for (int i = 0; i < n; i++)
        {
            if (joints[i] != null)
                originalRotations[i] = joints[i].localRotation;
        }

        for (int j = 0; j < n; j++)
        {
            if (joints[j] == null)
                continue;

            Vector3 axis = GetJointAxis(j);

            joints[j].localRotation =
                originalRotations[j] * Quaternion.AngleAxis(finiteDifferenceDeg, axis);

            Vector3[] perturbedErrors = GetCurrentErrorVectors();

            float[] ePerturbed = new float[m];

            for (int k = 0; k < perturbedErrors.Length; k++)
            {
                ePerturbed[k * 3 + 0] = perturbedErrors[k].x;
                ePerturbed[k * 3 + 1] = perturbedErrors[k].y;
                ePerturbed[k * 3 + 2] = perturbedErrors[k].z;
            }

            for (int row = 0; row < m; row++)
                J[row, j] = (ePerturbed[row] - baseError[row]) / epsRad;

            joints[j].localRotation = originalRotations[j];
        }

        return J;
    }

    private float[] SolveDampedLeastSquares(float[,] J, float[] e, int m, int n)
    {
        // dq = - J^T (J J^T + lambda^2 I)^-1 e

        float[,] A = new float[m, m];

        for (int r = 0; r < m; r++)
        {
            for (int c = 0; c < m; c++)
            {
                float sum = 0f;

                for (int k = 0; k < n; k++)
                    sum += J[r, k] * J[c, k];

                A[r, c] = sum;
            }

            A[r, r] += damping * damping;
        }

        float[] y = SolveLinearSystem(A, e, m);

        float[] dq = new float[n];

        for (int j = 0; j < n; j++)
        {
            float sum = 0f;

            for (int r = 0; r < m; r++)
                sum += J[r, j] * y[r];

            dq[j] = -sum;
        }

        return dq;
    }

    private float[] SolveLinearSystem(float[,] A, float[] b, int n)
    {
        float[,] M = new float[n, n + 1];

        for (int r = 0; r < n; r++)
        {
            for (int c = 0; c < n; c++)
                M[r, c] = A[r, c];

            M[r, n] = b[r];
        }

        for (int i = 0; i < n; i++)
        {
            int pivot = i;
            float maxAbs = Mathf.Abs(M[i, i]);

            for (int r = i + 1; r < n; r++)
            {
                float value = Mathf.Abs(M[r, i]);

                if (value > maxAbs)
                {
                    maxAbs = value;
                    pivot = r;
                }
            }

            if (pivot != i)
            {
                for (int c = i; c <= n; c++)
                {
                    float tmp = M[i, c];
                    M[i, c] = M[pivot, c];
                    M[pivot, c] = tmp;
                }
            }

            float diag = M[i, i];

            if (Mathf.Abs(diag) < 1e-8f)
                diag = 1e-8f;

            for (int c = i; c <= n; c++)
                M[i, c] /= diag;

            for (int r = 0; r < n; r++)
            {
                if (r == i)
                    continue;

                float factor = M[r, i];

                for (int c = i; c <= n; c++)
                    M[r, c] -= factor * M[i, c];
            }
        }

        float[] x = new float[n];

        for (int i = 0; i < n; i++)
            x[i] = M[i, n];

        return x;
    }

    private Vector3 PointToToolAxisErrorVector(Vector3 point)
    {
        Vector3 a = toolFrame.position;
        Vector3 d = toolFrame.forward.normalized;

        Vector3 ap = point - a;
        Vector3 closest = a + Vector3.Dot(ap, d) * d;

        return point - closest;
    }

    private float PointToToolAxisDistance(Vector3 point)
    {
        return PointToToolAxisErrorVector(point).magnitude;
    }

    private Vector3 ClosestPointOnToolAxis(Vector3 point)
    {
        Vector3 a = toolFrame.position;
        Vector3 d = toolFrame.forward.normalized;

        return a + Vector3.Dot(point - a, d) * d;
    }

    private Vector3 GetJointAxis(int i)
    {
        if (jointAxesLocal == null || i >= jointAxesLocal.Length)
            return Vector3.forward;

        if (jointAxesLocal[i].sqrMagnitude < 0.0001f)
            return Vector3.forward;

        return jointAxesLocal[i].normalized;
    }

    private void StartLogging()
    {
        if (!logToCsv)
            return;

        string folder = Path.Combine(Application.dataPath, "../RCM_logs");

        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        string path = Path.Combine(folder, logFileName);

        logWriter = new StreamWriter(path, false);
        logWriter.WriteLine("time,mode,entry_error_mm,target_error_mm,tool_x,tool_y,tool_z,entry_x,entry_y,entry_z,target_x,target_y,target_z");

        Debug.Log("RCM CSV log saved to: " + path);
    }

    private void LogSample()
    {
        if (!logToCsv || logWriter == null)
            return;

        if (Time.time - lastLogTime < logEverySeconds)
            return;

        lastLogTime = Time.time;

        Vector3 toolPos = toolFrame.position;
        Vector3 entryPos = entryPoint.position;
        Vector3 targetPos = targetPoint.position;

        logWriter.WriteLine(
            F(Time.time) + "," +
            mode.ToString() + "," +
            F(entryErrorMm) + "," +
            F(targetErrorMm) + "," +
            F(toolPos.x) + "," +
            F(toolPos.y) + "," +
            F(toolPos.z) + "," +
            F(entryPos.x) + "," +
            F(entryPos.y) + "," +
            F(entryPos.z) + "," +
            F(targetPos.x) + "," +
            F(targetPos.y) + "," +
            F(targetPos.z)
        );
    }

    private string F(float value)
    {
        return value.ToString("F5", CultureInfo.InvariantCulture);
    }

    private void CloseLog()
    {
        if (logWriter != null)
        {
            logWriter.Flush();
            logWriter.Close();
            logWriter = null;
        }
    }

    private void OnApplicationQuit()
    {
        CloseLog();
    }

    private void OnDestroy()
    {
        CloseLog();
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(10, 10, 600, 25), "Mode: " + mode + "    1 Entry | 2 Target | 3 Double | R Reset");
        GUI.Label(new Rect(10, 35, 600, 25), "Entry error: " + entryErrorMm.ToString("F2") + " mm");
        GUI.Label(new Rect(10, 60, 600, 25), "Target error: " + targetErrorMm.ToString("F2") + " mm");
    }

    private void OnDrawGizmos()
    {
        if (toolFrame == null)
            return;

        Gizmos.color = Color.black;
        Gizmos.DrawLine(
            toolFrame.position - toolFrame.forward * 0.15f,
            toolFrame.position + toolFrame.forward * 1.2f
        );

        if (entryPoint != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(entryPoint.position, ClosestPointOnToolAxis(entryPoint.position));
        }

        if (targetPoint != null)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawLine(targetPoint.position, ClosestPointOnToolAxis(targetPoint.position));
        }
    }
}