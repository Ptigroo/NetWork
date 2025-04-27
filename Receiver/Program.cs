using System.Net;
using System.Net.Sockets;
using System.Text;
using Shared;

namespace Receiver;

internal class Program
{
    public static List<int> sequenceNumbersReceived = new List<int>();
    public static int highestCorrectSequenceNumbersReceived = 0;
    static UdpClient udpClient = null!;
    static int listeningPort;
    static string outputFile = string.Empty;
    static int sendingOn;
    static string senderIp = string.Empty;
    static IPEndPoint senderEndpoint;
    static ushort receiverSequenceNumber;
    static UdpReceiveResult udpReceivedResult;
    static Packet receivedPacket;
    private static async Task Main(string[] args)
    {
        InitConnectionParameters();
        Console.WriteLine("Waiting for handshake...");
        bool hanshakeSuccessfull = await ConnectionManager.WaitForHandshake(udpClient, receiverSequenceNumber);
        if (!hanshakeSuccessfull)
        {
            Console.WriteLine("Handshake failed, closing program");
            return;
        }
        Console.WriteLine("Handshake successful.");
        while (true)
        {
            await ReceivePacket();
            await RespondToSender();
            if (receivedPacket.FIN)
            {
                await HandleFinPacketReceived();
                break;
            }
        }
    }

    private static void InitConnectionParameters()
    {
#if DEBUG
        listeningPort = 5000;
        outputFile = "./output.txt";
        sendingOn = 5001;
        senderIp = "127.0.0.1";
#else
                if (args.Length != 2)
                {
                    Console.WriteLine("Usage: Receiver <ListeningPort> <OutputFile>");
                    return;
                }
                listeningPort = int.Parse(args[0]);
                outputFile = args[1];
#endif

        udpClient = new UdpClient(listeningPort);
        Console.WriteLine($"Receiver is listening on port {listeningPort}");
        senderEndpoint = new IPEndPoint(IPAddress.Parse(senderIp), sendingOn);
        receiverSequenceNumber = (ushort)new Random().Next(1, ushort.MaxValue);
    }

    private static async Task ReceivePacket()
    {
        udpReceivedResult = await udpClient.ReceiveAsync();
        receivedPacket = Packet.Deserialize(udpReceivedResult.Buffer);
        if (highestCorrectSequenceNumbersReceived == 0)
        {
            sequenceNumbersReceived.Add(receivedPacket.SequenceNumber);
            highestCorrectSequenceNumbersReceived = receivedPacket.SequenceNumber;
        }
        else if (receivedPacket.SequenceNumber == highestCorrectSequenceNumbersReceived + 1)
        {
            sequenceNumbersReceived.Add(receivedPacket.SequenceNumber);
            SetHighestCorrectSequenceNumber();
        }
        var message = Encoding.UTF8.GetString(receivedPacket.Data);
        Console.WriteLine($"Received message: {message}");
    }
    private static async Task HandleFinPacketReceived()
    {
        Console.WriteLine("Received FIN. Sending FIN-ACK.");
        // Send FIN-ACK
        var finAckPacket = new Packet
        {
            FIN = true,
            ACK = true,
            SequenceNumber = receivedPacket.SequenceNumber,
            TotalDataSize = 0,
            Data = new byte[0]
        };
        var finAckBytes = finAckPacket.Serialize();
        await udpClient.SendAsync(finAckBytes, finAckBytes.Length, udpReceivedResult.RemoteEndPoint);
        // Wait for final ACK from Sender
        var ackResult = await udpClient.ReceiveAsync();
        var finalAckPacket = Packet.Deserialize(ackResult.Buffer);
        Console.WriteLine(finalAckPacket.ACK
            ? "Received final ACK. Connection terminated gracefully."
            : "Did not receive final ACK. Connection termination may be incomplete.");
    }
    private static async Task RespondToSender()
    {
        var responsePacket = new Packet
        {
            ACK = true,
            TotalDataSize = 2,
            Data = Encoding.UTF8.GetBytes(receivedPacket.SequenceNumber.ToString()),
            SequenceNumber = receiverSequenceNumber
        };
        await udpClient.SendAsync(responsePacket.Data, responsePacket.TotalDataSize, senderEndpoint);
        receiverSequenceNumber++;
    }
    public static void SetHighestCorrectSequenceNumber()
    {
        highestCorrectSequenceNumbersReceived = sequenceNumbersReceived.First(number => number > highestCorrectSequenceNumbersReceived && sequenceNumbersReceived.Any(t => t + 1 == number));
    }
}