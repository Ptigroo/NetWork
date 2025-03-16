using System.Net;
using System.Net.Sockets;
using System.Text;
using Shared;

namespace Receiver;

internal class Program
{
    public static List<int> sequenceNumbersReceived = new List<int>();
    public static int highestCorrectSequenceNumbersReceived = 0;
    private static async Task Main(string[] args)
    {

#if DEBUG
        int listeningPort = 5000;
        string outputFile = "./output.txt";
        int sendingOn = 5001;
        string senderIp = "127.0.0.1";
#else
        if (args.Length != 2)
        {
            Console.WriteLine("Usage: Receiver <ListeningPort> <OutputFile>");
            return;
        }
        var listeningPort = int.Parse(args[0]);
        var outputFile = args[1];
#endif

        var udpClient = new UdpClient(listeningPort);
        Console.WriteLine($"Receiver is listening on port {listeningPort}");
        var senderEndpoint = new IPEndPoint(IPAddress.Parse(senderIp), sendingOn);
        var receiverSequenceNumber = (ushort)new Random().Next(1, ushort.MaxValue);

        // Wait for handshake
        Console.WriteLine("Waiting for handshake...");
        var isConnected = await ConnectionManager.WaitForHandshake(udpClient, receiverSequenceNumber);
        var buffer = new List<byte>();
        if (isConnected)
        {
            Console.WriteLine("Handshake successful.");
            // Proceed to receive data
            var remoteEndpoint = new IPEndPoint(IPAddress.Any, 0);
            while (true)
            {
                var result = await udpClient.ReceiveAsync();
                var packet = Packet.Deserialize(result.Buffer);
                if (highestCorrectSequenceNumbersReceived == 0)
                {
                    sequenceNumbersReceived.Add(packet.SequenceNumber);
                    highestCorrectSequenceNumbersReceived = packet.SequenceNumber;
                }
                else if (packet.SequenceNumber == highestCorrectSequenceNumbersReceived +1)
                {
                    sequenceNumbersReceived.Add(packet.SequenceNumber);
                    SetHighestCorrectSequenceNumber();
                }
                var responsePacket = new Packet
                {
                    ACK = true,
                    TotalDataSize = 2,
                    Data = Encoding.UTF8.GetBytes(packet.SequenceNumber.ToString()),
                    SequenceNumber = receiverSequenceNumber
                };
                await udpClient.SendAsync(responsePacket.Data, responsePacket.TotalDataSize, senderEndpoint);
                // Check for FIN flag to terminate connection
                receiverSequenceNumber++;
                if (packet.FIN)
                {
                    Console.WriteLine("Received FIN. Sending FIN-ACK.");
                    // Send FIN-ACK
                    var finAckPacket = new Packet
                    {
                        FIN = true,
                        ACK = true,
                        SequenceNumber = packet.SequenceNumber,
                        TotalDataSize = 0,
                        Data = new byte[0]
                    };
                    var finAckBytes = finAckPacket.Serialize();
                    await udpClient.SendAsync(finAckBytes, finAckBytes.Length, result.RemoteEndPoint);
                    // Wait for final ACK from Sender
                    var ackResult = await udpClient.ReceiveAsync();
                    var finalAckPacket = Packet.Deserialize(ackResult.Buffer);
                    Console.WriteLine(finalAckPacket.ACK
                        ? "Received final ACK. Connection terminated gracefully."
                        : "Did not receive final ACK. Connection termination may be incomplete.");
                    break;
                }
                var message = Encoding.UTF8.GetString(packet.Data);
                Console.WriteLine($"Received message: {message}");
            }
        }
        else
        {
            Console.WriteLine("Handshake failed.");
        }
    }
    public static void SetHighestCorrectSequenceNumber()
    {
        highestCorrectSequenceNumbersReceived = sequenceNumbersReceived.First(number => number > highestCorrectSequenceNumbersReceived && sequenceNumbersReceived.Any(t => t + 1 == number)) ;
    }
}