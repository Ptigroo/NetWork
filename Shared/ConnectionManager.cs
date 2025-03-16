// -----------------------------------------------------------------------
// <copyright>
//     Author: Gaspard de Villenfagne de Vogelsank - Nicolas Denoël
//     Description: Manage connection processes between sender and receiver, 
//      including initial handshake, etc.
// </copyright>
// -----------------------------------------------------------------------

using System.Net;
using System.Net.Sockets;

namespace Shared;

/// <summary>
///     Manages the three-way handshake process to establish a reliable connection over UDP.
///     Mimics TCP connection setup using SYN, SYN-ACK, and ACK packets.
/// </summary>
public class ConnectionManager
{
    /// <summary>
    ///     Initiates the handshake process as a sender.
    ///     Sends a SYN packet, waits for a SYN-ACK response, and completes with an ACK packet.
    /// </summary>
    /// <param name="udpClient">The UDP client used for sending and receiving packets.</param>
    /// <param name="receiverEndpoint">The receiver's IP address and port.</param>
    /// <param name="initialSequenceNumber">The sender's initial sequence number.</param>
    /// <returns>True if the handshake succeeds; otherwise, false.</returns>
    public static async Task<bool> InitiateHandshake(UdpClient udpClient, IPEndPoint receiverEndpoint,
        ushort initialSequenceNumber)
    {
        // Step 1: Create and send the SYN packet
        var synPacket = new Packet
        {
            SYN = true,
            SequenceNumber = initialSequenceNumber,
            TotalDataSize = 0,
            Data = new byte[0]
        };
        var synBytes = synPacket.Serialize();
        await udpClient.SendAsync(synBytes, synBytes.Length, receiverEndpoint);

        // Step 2: Wait for SYN-ACK response with a timeout
        var receiveTask = udpClient.ReceiveAsync();
        if (await Task.WhenAny(receiveTask, Task.Delay(3000)) == receiveTask)
        {
            var result = receiveTask.Result;
            var responsePacket = Packet.Deserialize(result.Buffer);
            if (responsePacket.SYN && responsePacket.ACK)
            {
                // Step 3: Create and send the final ACK packet
                var ackPacket = new Packet
                {
                    ACK = true,
                    SequenceNumber = (ushort)(initialSequenceNumber + 1),
                    TotalDataSize = 0,
                    Data = new byte[0]
                };
                var ackBytes = ackPacket.Serialize();
                await udpClient.SendAsync(ackBytes, ackBytes.Length, receiverEndpoint);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     Waits for and handles the handshake process as a receiver.
    ///     Responds to a SYN packet with a SYN-ACK packet, and validates the sender's final ACK.
    /// </summary>
    /// <param name="udpClient">The UDP client used for receiving and responding to packets.</param>
    /// <param name="initialSequenceNumber">The receiver's initial sequence number.</param>
    /// <returns>True if the handshake succeeds; otherwise, false.</returns>
    public static async Task<bool> WaitForHandshake(UdpClient udpClient, ushort initialSequenceNumber)
    {
        var senderEndpoint = new IPEndPoint(IPAddress.Any, 0); // Placeholder for sender's endpoint

        // Step 1: Wait for a SYN packet
        var receiveTask = udpClient.ReceiveAsync();
        var result = await receiveTask;
        var synPacket = Packet.Deserialize(result.Buffer);

        if (synPacket.SYN)
        {
            // Step 2: Create and send a SYN-ACK packet in response
            var synAckPacket = new Packet
            {
                SYN = true,
                ACK = true,
                SequenceNumber = initialSequenceNumber,
                TotalDataSize = 0,
                Data = new byte[0]
            };
            var synAckBytes = synAckPacket.Serialize();
            await udpClient.SendAsync(synAckBytes, synAckBytes.Length, result.RemoteEndPoint);

            // Step 3: Wait for the final ACK packet with a timeout
            receiveTask = udpClient.ReceiveAsync();
            if (await Task.WhenAny(receiveTask, Task.Delay(3000)) == receiveTask)
            {
                result = receiveTask.Result;
                var ackPacket = Packet.Deserialize(result.Buffer);
                if (ackPacket.ACK) return true;
            }
        }

        return false;
    }
}