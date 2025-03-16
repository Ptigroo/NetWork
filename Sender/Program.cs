using System.Net;
using System.Net.Sockets;
using System.Text;
using Shared;

namespace Sender;

internal class Program
{
    public static bool isSending = false;
    public static UdpClient udpClient = new UdpClient();
    public static List<Packet> responsesFromReceiver  = new List<Packet>();
    public static ushort firstSequenceNumber = (ushort)new Random().Next(1, ushort.MaxValue);
    public static ushort senderSequenceNumber = firstSequenceNumber;
    public static int windowSize = senderSequenceNumber + 256;
    public static byte[] messageAsBytes = [];
    public static List<Packet> packetsToSend = [];
    private static async Task Main(string[] args)
    {
        
#if DEBUG
        var receiverIp = "127.0.0.1";
        var receiverPort = 5000;
        string message = "2HelloWorld";
        var listeningOn = 5001;
#else
        if (args.Length != 3)
        {
            Console.WriteLine("Usage: Sender <ReceiverIP> <ReceiverPort> <Message>");
            return;
        }
        var receiverIp = args[0];
        var receiverPort = int.Parse(args[1]);
        string message = args[2];
#endif


        var receiverEndpoint = new IPEndPoint(IPAddress.Parse(receiverIp), receiverPort);
        // Initiate handshake
        Console.WriteLine("Initiating handshake...");
        var isConnected = await ConnectionManager.InitiateHandshake(udpClient, receiverEndpoint, senderSequenceNumber);
        senderSequenceNumber++;
        ushort dataSize = (ushort)Encoding.UTF8.GetByteCount(message);
        int bytesToSend = dataSize;
        messageAsBytes = Encoding.UTF8.GetBytes(message);
        if (dataSize % 2 != 0)
        {
            byte[] newArray = new byte[messageAsBytes.Length + 1];
            messageAsBytes.CopyTo(newArray, 1);
            newArray[0] = new byte();
            messageAsBytes = newArray;
            dataSize++;
        }
        int nextByteIndex = 0;
        Console.WriteLine("Message to send: " + message);
        if (isConnected)
        {
            isSending = true;
            SetResponseReader();
        }
        while (bytesToSend > 0)
        {
            Console.WriteLine("Handshake successful.");
            var packet = new Packet
            {
                TotalDataSize = dataSize,
                SequenceNumber = (ushort)(senderSequenceNumber),
                Data = messageAsBytes[nextByteIndex..(nextByteIndex + 2)]
            };

            //var packetBytes = packet.Serialize();
            packetsToSend.Add(packet);
            nextByteIndex = nextByteIndex + 2;
            senderSequenceNumber++;
            bytesToSend -= 2;
        }
        senderSequenceNumber = firstSequenceNumber;
        if (isConnected)
        {
            while (senderSequenceNumber < packetsToSend.Count + firstSequenceNumber)
            {
                var serializedPacket = packetsToSend[senderSequenceNumber - firstSequenceNumber].Serialize();
                await udpClient.SendAsync(serializedPacket, serializedPacket.Length, receiverEndpoint);
                senderSequenceNumber++;
            }
            Console.WriteLine("Sent data packet to receiver.");
            // Send FIN Packet to initiate connection termination
            var finPacket = new Packet
            {
                FIN = true,
                SequenceNumber = (ushort)(senderSequenceNumber),
                TotalDataSize = 0,
                Data = new byte[0]
            };
            var finBytes = finPacket.Serialize();
            await udpClient.SendAsync(finBytes, finBytes.Length, receiverEndpoint);
            senderSequenceNumber++;
            Console.WriteLine("Sent FIN packet to receiver. Waiting for FIN-ACK.");
            // Wait for FIN-ACK
            var receiveTask = udpClient.ReceiveAsync();
            if (await Task.WhenAny(receiveTask, Task.Delay(3000)) == receiveTask)
            {
                var result = receiveTask.Result;
                var finAckPacket = Packet.Deserialize(result.Buffer);
                if (finAckPacket.FIN && finAckPacket.ACK)
                {
                    Console.WriteLine("Received FIN-ACK. Sending final ACK.");
                    // Send final ACK to complete termination
                    var finalAckPacket = new Packet
                    {
                        ACK = true,
                        SequenceNumber = (ushort)(senderSequenceNumber + 2),
                        TotalDataSize = 0,
                        Data = new byte[0]
                    };
                    var finalAckBytes = finalAckPacket.Serialize();
                    await udpClient.SendAsync(finalAckBytes, finalAckBytes.Length, receiverEndpoint);
                    isSending = false;
                    Console.WriteLine("Connection terminated gracefully.");
                }
            }
            else
            {
                Console.WriteLine("Did not receive FIN-ACK. Connection termination failed.");
            }
        }
        else
        {
            Console.WriteLine("Handshake failed.");
        }
    }

    /// <summary>
    /// Comportement lors de la réponse ACK du receveur. Enregistre les réponses si elles ne sont pas 
    /// déjà enregistrées pour le même numéro de séquence. 
    /// Et reset la connexion si le nombre de réponses pour le même numéro de séquence du sender (Dans le data) est égal à 3.
    /// </summary>
    public static async Task SetResponseReader()
    {
        while (isSending)
        {
            var result = await udpClient.ReceiveAsync();
            var packet = Packet.Deserialize(result.Buffer);
            if (packet.ACK)
            {
                windowSize++;
                if (!responsesFromReceiver.Any(response => response.SequenceNumber == packet.SequenceNumber))
                {
                    responsesFromReceiver.Add(packet);
                    int highestSequenceNumber = responsesFromReceiver.Max(x => x.SequenceNumber);
                    if (responsesFromReceiver.Count(response => response.SequenceNumber == highestSequenceNumber) == 3)
                    {
                        //Restart at sequence number 
                        senderSequenceNumber = (ushort)highestSequenceNumber;
                    }
                }

            }
        }
    }
    public static async Task<bool> EndOfWindow()
    {
        return windowSize == senderSequenceNumber;
    }
}