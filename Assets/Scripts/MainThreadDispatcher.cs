using System;
using System.Collections;
using System.Collections.Concurrent;
using UnityEngine;

/// <summary>
/// Runs actions and coroutines on Unity's main thread (required for UnityWebRequest).
/// </summary>
[DefaultExecutionOrder(-500)]
public sealed class MainThreadDispatcher : MonoBehaviour
{
    private static MainThreadDispatcher _instance;
    private static readonly ConcurrentQueue<Action> PendingActions = new ConcurrentQueue<Action>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        EnsureInstance();
    }

    private static MainThreadDispatcher EnsureInstance()
    {
        if (_instance != null)
        {
            return _instance;
        }

        GameObject host = new GameObject(nameof(MainThreadDispatcher));
        DontDestroyOnLoad(host);
        _instance = host.AddComponent<MainThreadDispatcher>();
        return _instance;
    }

    private void Update()
    {
        while (PendingActions.TryDequeue(out Action action))
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception exception)
            {
                Debug.LogError("[MainThreadDispatcher] Action failed: " + exception.Message);
            }
        }
    }

    public static void Enqueue(Action action)
    {
        if (action == null)
        {
            return;
        }

        PendingActions.Enqueue(action);
        EnsureInstance();
    }

    public static void RunCoroutine(IEnumerator routine)
    {
        if (routine == null)
        {
            return;
        }

        Enqueue(() => EnsureInstance().StartCoroutine(routine));
    }
}
