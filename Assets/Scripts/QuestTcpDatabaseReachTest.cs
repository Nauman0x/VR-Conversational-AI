using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Quest/VR TCP reachability test — run on headset before Npgsql. Attach to an empty GameObject in a test scene.
/// </summary>
public class QuestTcpDatabaseReachTest : MonoBehaviour
{
    [SerializeField] private string host = "YOUR_AIVEN_HOST.aivencloud.com";
    [SerializeField] private int port = 18242;
    [SerializeField] private int timeoutMs = 10000;

    private async void Start()
    {
        await TestTcp();
    }

    private async Task TestTcp()
    {
        try
        {
            Debug.Log("[TCP TEST] Resolving host: " + host);

            IPAddress[] addresses = await Dns.GetHostAddressesAsync(host);

            foreach (IPAddress address in addresses)
            {
                Debug.Log("[TCP TEST] DNS result: " + address + " | " + address.AddressFamily);
            }

            foreach (IPAddress address in addresses)
            {
                if (address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                Debug.Log("[TCP TEST] Trying IPv4: " + address);

                using TcpClient client = new TcpClient(AddressFamily.InterNetwork);

                Task connectTask = client.ConnectAsync(address, port);
                Task timeoutTask = Task.Delay(timeoutMs);

                Task completed = await Task.WhenAny(connectTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    Debug.LogError("[TCP TEST] Timeout connecting to " + address + ":" + port);
                    continue;
                }

                await connectTask;

                if (client.Connected)
                {
                    Debug.Log("[TCP TEST] SUCCESS: Connected to " + address + ":" + port);
                    return;
                }
            }

            Debug.LogError("[TCP TEST] No IPv4 connection succeeded.");
        }
        catch (Exception ex)
        {
            Debug.LogError("[TCP TEST] Failed: " + ex.GetType().FullName + " | " + ex.Message);
        }
    }
}
