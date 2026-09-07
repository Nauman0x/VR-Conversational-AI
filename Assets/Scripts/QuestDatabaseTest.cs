using System;
using System.Threading.Tasks;
using Npgsql;
using UnityEngine;

/// <summary>
/// Minimal Npgsql OpenAsync test for Quest — assign Aiven fields in the Inspector. Not for production shipping.
/// </summary>
public class QuestDatabaseTest : MonoBehaviour
{
    [Header("Aiven PostgreSQL")]
    [SerializeField] private string host = "YOUR_AIVEN_HOST.aivencloud.com";
    [SerializeField] private int port = 18242;
    [SerializeField] private string database = "defaultdb";
    [SerializeField] private string username = "avnadmin";
    [SerializeField] private string password = "YOUR_PASSWORD";

    private async void Start()
    {
        await TestDatabaseConnection();
    }

    private async Task TestDatabaseConnection()
    {
        string connectionString =
            "Host=" + host + ";" +
            "Port=" + port + ";" +
            "Database=" + database + ";" +
            "Username=" + username + ";" +
            "Password=" + password + ";" +
            "Ssl Mode=Require;" +
            "Trust Server Certificate=true;" +
            "Timeout=20;" +
            "Command Timeout=20;" +
            "Pooling=false;" +
            "Keepalive=10;";

        try
        {
            Debug.Log("[DB TEST] Creating connection...");

            await using NpgsqlConnection connection = new NpgsqlConnection(connectionString);

            Debug.Log("[DB TEST] Opening connection (OpenAsync)...");
            await connection.OpenAsync();

            Debug.Log("[DB TEST] Connected successfully.");

            await using NpgsqlCommand command = new NpgsqlCommand("SELECT 1;", connection);
            object result = await command.ExecuteScalarAsync();

            Debug.Log("[DB TEST] Query success. Result: " + result);
        }
        catch (Exception ex)
        {
            Debug.LogError("[DB TEST] Failed.");
            Debug.LogError("[DB TEST] Type: " + ex.GetType().FullName);
            Debug.LogError("[DB TEST] Message: " + ex.Message);
            Debug.LogError("[DB TEST] Stack: " + ex.StackTrace);

            if (ex.InnerException != null)
            {
                Debug.LogError("[DB TEST] Inner Type: " + ex.InnerException.GetType().FullName);
                Debug.LogError("[DB TEST] Inner Message: " + ex.InnerException.Message);
            }
        }
    }
}
