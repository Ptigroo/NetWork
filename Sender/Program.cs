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
    public static byte[] messageAsBytes = [];
    public static List<Packet> packetsToSend = [];
    public static string message = string.Empty;
    public static IPEndPoint receiverEndpoint = null!;
    private static ushort dataSize = 0;
    private static ushort maxNumberOfBytesPerPackage = 2;
    private static ushort startOfWindow;
    private static ushort endOfWindow { get 
        {
            if (startOfWindow < ushort.MaxValue - ConnectionManager.WindowSize)
            {
                return (ushort)(startOfWindow + ConnectionManager.WindowSize);
            }
            else
            {
                return (ushort)(startOfWindow + ConnectionManager.WindowSize - ushort.MaxValue);
            }
        } }
    private static async Task Main(string[] args)
    {

        InitConnectionParameters();
        Console.WriteLine("Initiating handshake...");
        var hanshakeSuccessfull = await ConnectionManager.InitiateHandshake(udpClient, receiverEndpoint, senderSequenceNumber);
        if (!hanshakeSuccessfull)
        {
            Console.WriteLine("Handshake failed, closing program");
            return;
        }
        Console.WriteLine("Handshake successful.");
        senderSequenceNumber++;
        dataSize = (ushort)Encoding.UTF8.GetByteCount(message);
        messageAsBytes = Encoding.UTF8.GetBytes(message);
        CompleteMessageWithEmptyByteIfNotEvenNumber();
        Console.WriteLine("Message to send: " + message);
        if (hanshakeSuccessfull)
        {
            isSending = true;
            //ReadReceiverResponsesThread();
            _ = Task.Run(ReadReceiverResponsesThread);
        }
        CreatePacketsOfXBytes(maxNumberOfBytesPerPackage);
        await SendAllPackets();
        await HandleFinPacket();
    }
    private static void InitConnectionParameters()
    {
#if DEBUG
        var receiverIp = "127.0.0.1";
        var receiverPort = 5000;
        message = "2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld2HelloWorld";
        var listeningOn = 5001;
#else
        if (args.Length != 3)
        {
            Console.WriteLine("Usage: Sender <ReceiverIP> <ReceiverPort> <Message>");
            return;
        }
        var receiverIp = args[0];
        var receiverPort = int.Parse(args[1]);
        message = args[2];
#endif


        receiverEndpoint = new IPEndPoint(IPAddress.Parse(receiverIp), receiverPort);
    }

    private static void CompleteMessageWithEmptyByteIfNotEvenNumber()
    {
        if (dataSize % 2 != 0)
        {
            byte[] newArray = new byte[messageAsBytes.Length + 1];
            messageAsBytes.CopyTo(newArray, 1);
            newArray[0] = new byte();
            messageAsBytes = newArray;
            dataSize++;
        }
    }
    private static void CreatePacketsOfXBytes(ushort maxNumberOfBytesPerPackage)
    {

        int nextByteIndex = 0;
        int bytesLeftToSend = dataSize;
        while (bytesLeftToSend > 0)
        {
            var packet = new Packet
            {
                TotalDataSize = dataSize,
                SequenceNumber = (ushort)(senderSequenceNumber),
                Data = messageAsBytes[nextByteIndex..(nextByteIndex + maxNumberOfBytesPerPackage)]
            };

            //var packetBytes = packet.Serialize();
            packetsToSend.Add(packet);
            nextByteIndex = nextByteIndex + maxNumberOfBytesPerPackage;
            senderSequenceNumber++;
            bytesLeftToSend -= 2;
        }
    }
    private static async Task SendAllPackets()
    {

        senderSequenceNumber = firstSequenceNumber;
        startOfWindow = senderSequenceNumber;
        while (senderSequenceNumber < packetsToSend.Count + firstSequenceNumber)
        {
            if (isOutOfWindow())
            {
                Console.WriteLine("Out of window... Waiting for receiver before going onward");
                await Task.Delay(3000);
                if (isOutOfWindow())
                {
                    Console.WriteLine("Still out of window... Closing the program");
                    //Qu'est ce que je fait ici est-ce que je ferme réellement le programme ou est'ce que je continue d'attendre que la fenêtre glisse ?
                }
            }
            var serializedPacket = packetsToSend[senderSequenceNumber - firstSequenceNumber].Serialize();
            await udpClient.SendAsync(serializedPacket, serializedPacket.Length, receiverEndpoint);
        }
        Console.WriteLine("All packets sent to receiver.");
    }
    public static async Task HandleFinPacket()
    {
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

    /// <summary>
    /// Comportement lors de la réponse ACK du receveur. Enregistre les réponses si elles ne sont pas 
    /// déjà enregistrées pour le même numéro de séquence. 
    /// Et reset la connexion si le nombre de réponses pour le même numéro de séquence du sender (Dans le data) est égal à 3.
    /// </summary>
    public static async Task ReadReceiverResponsesThread()
    {
        Console.WriteLine("befor while ");
        while (isSending)
        {
            Console.WriteLine("in while ");
                var receiveTask = udpClient.ReceiveAsync();
            if (await Task.WhenAny(receiveTask, Task.Delay(3000)) == receiveTask)
            {
                    var result = receiveTask.Result;
                    Console.WriteLine("after receive while ");
                    var packet = Packet.Deserialize(result.Buffer);
                    if (packet.ACK)
                    {
                        Console.WriteLine("received ack ");
                        if (!IsADuplicatedResponse(packet))
                        {
                            Console.WriteLine("not duplicated");
                            responsesFromReceiver.Add(packet);
                            ushort highestSequenceNumber = GetHighestSequenceNumberRespondedFromReceiver();
                            if (responsesFromReceiver.Count(response => response.SequenceNumber == highestSequenceNumber) == 3)
                            {
                                //Restart at sequence number 
                                senderSequenceNumber = (ushort)highestSequenceNumber;
                            }
                            Console.WriteLine("Highest sequence number responded from receiver: " + highestSequenceNumber);
                            SlideWindow(highestSequenceNumber);
                        }
                    }
            }
        }
    }
    private static void SlideWindow(ushort highestSequenceNumber)
    {
        if (responsesFromReceiver.Count(response => response.SequenceNumber == highestSequenceNumber) == 1)
        {
            startOfWindow++;
            if (endOfWindow < startOfWindow)
            {
                responsesFromReceiver.RemoveAll(response => response.SequenceNumber > endOfWindow && response.SequenceNumber < startOfWindow);
            }
            else
            {
                responsesFromReceiver.RemoveAll(response => response.SequenceNumber < startOfWindow);
            }
            Console.WriteLine("Sliding window");
            Console.WriteLine("New start of window: " + startOfWindow);
            Console.WriteLine("New end of window: " + endOfWindow);
        }
    }
    private static ushort GetHighestSequenceNumberRespondedFromReceiver()
    {
        if (endOfWindow < startOfWindow)
        {
            if (responsesFromReceiver.Any(response => response.SequenceNumber < endOfWindow))
            {
                return responsesFromReceiver.Where(response => response.SequenceNumber < endOfWindow).Max(response => response.SequenceNumber);
            }
        }
        return responsesFromReceiver.Max(response => response.SequenceNumber);
    }
    private static bool IsADuplicatedResponse(Packet packet)
    {
        return responsesFromReceiver.Any(response => response.SequenceNumber == packet.SequenceNumber);
    }
    private static bool isOutOfWindow()
    {
        if (endOfWindow > startOfWindow)
        {
            if (senderSequenceNumber > endOfWindow)
            {
                return true;
            }
        }
        else
        {
            if (senderSequenceNumber > endOfWindow && senderSequenceNumber < startOfWindow)
            {
                return true;
            }
        }
        return false;
    }
}