using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Numbered auth / database trace lines — Unity console only (VR on-screen HUD is disabled).
/// </summary>
public static class AuthFlowTrace
{
    private static int _attemptCounter;

    /// <summary>Starts a new traced sign-in or sign-up attempt and returns its attempt id.</summary>
    public static int BeginAttempt(string flowName)
    {
        int attemptId = ++_attemptCounter;
        Step(flowName + "-A001", "attempt=" + attemptId + " begin");
        return attemptId;
    }

    /// <summary>Writes one trace line to the Unity console (not the VR HUD).</summary>
    public static void Step(string stepCode, string detail, LogType type = LogType.Log)
    {
        string line = "[AuthTrace][" + stepCode + "] " + (detail ?? string.Empty);
        switch (type)
        {
            case LogType.Warning:
                Debug.LogWarning(line);
                break;
            case LogType.Error:
            case LogType.Exception:
            case LogType.Assert:
                Debug.LogError(line);
                break;
            default:
                Debug.Log(line);
                break;
        }
    }

    /// <summary>Writes a trace line that includes a flattened exception chain.</summary>
    public static void StepException(string stepCode, string detail, Exception exception)
    {
        Step(stepCode, detail + " | ex=" + FlattenException(exception), LogType.Error);
    }

    /// <summary>Logs PostgreSQL target settings without secrets.</summary>
    public static void LogPostgresTarget(string stepCode, string host, int port, string database, string username, bool requireSsl)
    {
        Step(
            stepCode,
            "host=" + (host ?? string.Empty) +
            " port=" + port +
            " db=" + (database ?? string.Empty) +
            " user=" + (username ?? string.Empty) +
            " ssl=" + requireSsl);
    }

    /// <summary>Builds a single-line exception chain for device logs.</summary>
    public static string FlattenException(Exception exception)
    {
        if (exception == null)
        {
            return "(null)";
        }

        StringBuilder builder = new StringBuilder(512);
        Exception current = exception;
        int depth = 0;

        while (current != null && depth < 6)
        {
            if (depth > 0)
            {
                builder.Append(" <= ");
            }

            builder.Append(current.GetType().Name);
            if (!string.IsNullOrWhiteSpace(current.Message))
            {
                builder.Append(": ");
                builder.Append(current.Message.Replace('\n', ' ').Replace('\r', ' '));
            }

            current = current.InnerException;
            depth++;
        }

        return builder.ToString();
    }
}
