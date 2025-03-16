# net-work

Sender                           Receiver
----------------------------------------------------------------
InitiateHandshake
Send SYN ---------------------> WaitForHandshake
Receive SYN
Send SYN-ACK <-----------------
Receive SYN-ACK
Send ACK ---------------------> WaitFor ACK
Receive ACK
Proceed to send data            Handshake complete