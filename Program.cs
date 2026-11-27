using System.Text.Json;
using MQTTnet;
using MQTTnet.Exceptions;
using MQTTnet.Protocol;

namespace Orchid.UserSessionHook;

public class Program
{
    // Distinct exit codes make failures readable in journalctl / systemd logs.
    private const int ExitOk = 0;
    private const int ExitUsage = 1;
    private const int ExitConnectFailed = 2;
    private const int ExitSendFailed = 3;
    private const int ExitConfigFailed = 4;

    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private const int MaxAttempts = 3;
    private const string CredentialsFileName = ".mqtt.env";

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return ExitUsage;
        }

        switch (args[0])
        {
            case "start":
                return await SendMachineState(true);
            case "stop":
                return await SendMachineState(false);
            default:
                Usage();
                return ExitUsage;
        }
    }

    private static void Usage()
    {
        Console.Error.WriteLine("Usage: Orchid.UserSessionHook.exe <start/stop>");
    }

    // Reads "Username: ..." / "Password: ..." lines from ~/.mqtt.env.
    private static (string Username, string Password)? LoadCredentials()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), CredentialsFileName);

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Credentials file not found: {path}");
            return null;
        }

        string[] lines;
        try
        {
            lines = File.ReadAllLines(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not read {path}: {ex.Message}");
            return null;
        }

        string? username = null;
        string? password = null;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator < 0)
            {
                continue;
            }

            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();

            switch (key)
            {
                case "Username":
                    username = value;
                    break;
                case "Password":
                    password = value;
                    break;
            }
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            Console.Error.WriteLine($"{path} must contain both a Username and a Password line.");
            return null;
        }

        return (username, password);
    }

    private static async Task<int> SendMachineState(bool online)
    {
        var credentials = LoadCredentials();
        if (credentials == null)
        {
            return ExitConfigFailed;
        }

        var mqttFactory = new MqttClientFactory();
        using var mqttClient = mqttFactory.CreateMqttClient();

        var mqttClientOptions = new MqttClientOptionsBuilder()
            .WithTcpServer("dockerhost.anthome.uk")
            .WithCredentials(credentials.Value.Username, credentials.Value.Password)
            .WithClientId($"orchid-usersessionhook-{Environment.MachineName.ToLower()}")
            .WithCleanSession()
            .WithTimeout(DefaultTimeout)
            .Build();

        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"usersessionhook/status/{Environment.MachineName.ToLower()}")
            .WithRetainFlag()
            .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
            .WithPayload(JsonSerializer.Serialize(new
            {
                online,
                platform = Environment.OSVersion.Platform.ToString(),
                lastChanged = DateTime.UtcNow.ToString("O")
            }))
            .Build();

        // The broker may be briefly unreachable at login/logout, so retry with backoff.
        int lastFailure = ExitSendFailed;

        for (int attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            using var timeout = new CancellationTokenSource(DefaultTimeout);

            try
            {
                lastFailure = ExitConnectFailed;
                var connectResult = await mqttClient.ConnectAsync(mqttClientOptions, timeout.Token);

                if (connectResult.ResultCode != MqttClientConnectResultCode.Success)
                {
                    Console.Error.WriteLine($"attempt {attempt}: connect rejected ({connectResult.ResultCode} {connectResult.ReasonString})".Trim());
                    // Auth/authorisation failures won't improve on retry.
                    if (connectResult.ResultCode is MqttClientConnectResultCode.BadUserNameOrPassword
                        or MqttClientConnectResultCode.NotAuthorized
                        or MqttClientConnectResultCode.ClientIdentifierNotValid)
                    {
                        return lastFailure;
                    }
                }
                else
                {
                    lastFailure = ExitSendFailed;
                    var publishResult = await mqttClient.PublishAsync(message, timeout.Token);

                    if (!publishResult.IsSuccess)
                    {
                        Console.Error.WriteLine($"attempt {attempt}: publish rejected ({publishResult.ReasonCode} {publishResult.ReasonString})".Trim());
                    }
                    else
                    {
                        await DisconnectBestEffort(mqttClient);
                        return ExitOk;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.Error.WriteLine($"attempt {attempt}: timed out after {DefaultTimeout.TotalSeconds}s");
            }
            catch (MqttCommunicationException ex)
            {
                Console.Error.WriteLine($"attempt {attempt}: {ex.Message}");
            }

            if (attempt < MaxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }

        return ExitSendFailed;
    }

    private static async Task DisconnectBestEffort(IMqttClient mqttClient)
    {
        // A failed close must not mask a publish that already succeeded.
        try
        {
            await mqttClient.DisconnectAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"disconnect failed (ignored): {ex.Message}");
        }
    }
}
