# Changelog - NetWork

All notable changes to the NetWork project will be documented in this file.

## Table of Contents

- [Unreleased](#unreleased)
- [Released](#released)
    - [v0.1.0 - 2023-10-17](#v010---2023-10-17)

## Unreleased

## Released

### v0.1.0 - 2024-12-04

- Communication
  - Update **main** methods in both Sender and Receiver to be able to exchange a message through UDP connection
  - Add **FIN**/**FIN ACK** feature in both receiver and sender
- Connection
  - Create **ConnectionManager** class
    - Add **InitiateHandshake** method to allow sender to initiate handshake (SYN) process with receiver
    - Add **WaitForHandshake** method to allow receiver to be called for handshake validation process (SYN-ACK)
    - Ensure message is only exchange between sender and receiver if 3-way handshake process is previously completed
- Serialization
  - Create **Packet** class
    - Add fields required in a typical packet, including TCP required flags SYN, ACK, FIN, RST
    - Add **Serialize** method
    - Add **Deserialize** method
- Solution creation
  - Create empty dotnet solution
  - Add projects to solution
    - Add **Receiver** project
    - Add **Sender** project
    - Add **Shared** project


