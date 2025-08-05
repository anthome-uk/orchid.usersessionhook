using MQTTnet;

namespace Orchid.UserSessionHook;

public class Program
{
    public static async Task Main(string[] args)
    {
        if (args.Length == 0)
        {
            Usage();
            return;
        }
        
        switch (args[0])
        {
            case "start":
                await Start();
                break;
            case "stop":
                await Stop();
                break;
            default:
                Usage();
                return;
        }
    }

    private static void Usage()
    {
        Console.WriteLine("Usage: Orchid.UserSessionHook.exe <start/stop>");
        Environment.Exit(-1);
    }

    private static async Task Start()
    {
        await SendMachineState(true);
    }

    private static async Task Stop()
    {
        await SendMachineState(false);
    }

    private static async Task SendMachineState(bool online)
    {
        var mqttFactory = new MqttClientFactory();
        using var mqttClient = mqttFactory.CreateMqttClient();
        var mqttClientOptions = new MqttClientOptionsBuilder().WithTcpServer("beast.anthome.uk").WithCredentials("anthony", "Fr35n3l42").Build();

        await mqttClient.ConnectAsync(mqttClientOptions, CancellationToken.None);
        
        var message = new MqttApplicationMessageBuilder()
            .WithTopic($"usersessionhook/status/{Environment.MachineName}")
            .WithRetainFlag()
            .WithPayload($"{{ \"online\": \"{online}\", \"platform\": \"{Environment.OSVersion.Platform}\", \"lastChanged\": \"{DateTime.UtcNow:O}\" }}")
            .Build();

        await mqttClient.PublishAsync(message);
    }
}